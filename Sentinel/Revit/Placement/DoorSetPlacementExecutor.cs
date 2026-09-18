using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Sentinel.Infrastructure;
using Sentinel.Revit.Storage;
using Sentinel.UI.Services;

namespace Sentinel.Revit.Placement
{
    /// <summary>Expected, user-fixable reason why one component could not be created.</summary>
    internal sealed class ComponentPlacementException : Exception
    {
        public string Code { get; }
        public ComponentPlacementException(string code, string message) : base(message) { Code = code; }
    }

    internal sealed class PlacementContext
    {
        public Document Doc { get; set; }
        public SentinelProject Project { get; set; }
        public FamilySymbolResolver Symbols { get; set; }
        public View3D RaycastView { get; set; }
        public string User { get; set; }

        private WallFaceFinder _walls;
        public WallFaceFinder Walls => _walls ?? (_walls = RaycastView != null ? new WallFaceFinder(RaycastView) : null);
    }

    /// <summary>
    /// Creates/updates the Revit elements of one door set instance from a calculated plan (see
    /// <see cref="DoorSetPlacementCalculator"/>). One transaction per door: a failure rolls back only that door
    /// and is reported with a reason; the in-memory records are changed only after the transaction committed.
    /// </summary>
    internal static class DoorSetPlacementExecutor
    {
        public static PlacementDoorResult PlaceInstance(PlacementContext ctx, DoorSetInstance inst, SourceCheck source,
            PlacementMode mode, bool overwriteManual)
        {
            var result = new PlacementDoorResult { InstanceId = inst.Id, DoorName = inst.Source?.DisplayName ?? inst.Id };
            var project = ctx.Project;

            if (inst.IsIgnored)
            {
                result.Outcome = PlacementOutcome.Skipped;
                result.Messages.Add("Door is marked as ignored.");
                return result;
            }

            var def = project.FindDoorSet(inst.DefinitionId);
            if (def == null)
            {
                return Fail(inst, result, "Assigned door set type no longer exists.");
            }

            if (source == null || source.Current == null || source.State == SourceState.Missing || source.State == SourceState.LinkUnavailable)
            {
                var reason = source != null && source.State == SourceState.LinkUnavailable
                    ? "Source link is not loaded or was removed."
                    : "Source door no longer available.";
                return Fail(inst, result, reason);
            }

            var geometry = source.Current.Geometry;
            var plan = DoorSetPlacementCalculator.Calculate(inst, def, geometry, project);
            if (plan.HasBlockingErrors)
            {
                return Fail(inst, result, string.Join(" ", plan.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Message)));
            }

            var diff = PlacementDiff.Compute(inst, plan, overwriteManual);
            var work = diff.Where(d => d.Action != DiffAction.Unchanged && d.Action != DiffAction.KeepManual).ToList();
            var sourceWasChanged = source.Comparison != null && source.Comparison.HasChanges;

            if (work.Count == 0)
            {
                // Nothing to change in Revit; still record review/snapshot.
                if (sourceWasChanged) DoorSetInstanceOperations.AcceptSourceChanges(inst, source.Current.Current);
                if (!string.IsNullOrEmpty(inst.PlacedConfigurationHash) || inst.HasPlacedElements)
                    inst.PlacedConfigurationHash = plan.ConfigurationHash;
                if (mode == PlacementMode.Confirmed) MarkReviewed(inst, ctx.User);
                result.Outcome = PlacementOutcome.NoChanges;
                var kept = diff.Count(d => d.Action == DiffAction.KeepManual);
                result.Messages.Add(kept > 0 ? kept + " manually modified component(s) kept." : "Already up to date.");
                return result;
            }

            var pending = new List<Action>();
            var componentFailures = new List<string>();
            var warnings = new List<string>();
            var createdOrMoved = 0;
            var pre = new CollectingFailuresPreprocessor();
            var doc = ctx.Doc;
            var mark = inst.Source?.DisplayName ?? "door";

            using (var t = new Transaction(doc, "Sentinel – place " + mark))
            {
                CollectingFailuresPreprocessor.Attach(t, pre);
                try
                {
                    t.Start();

                    foreach (var item in work)
                    {
                        var existingElement = item.Existing != null && !string.IsNullOrEmpty(item.Existing.ElementUniqueId)
                            ? doc.GetElement(item.Existing.ElementUniqueId)
                            : null;

                        switch (item.Action)
                        {
                            case DiffAction.Remove:
                                if (existingElement != null) doc.Delete(existingElement.Id);
                                var toRemove = item.Existing;
                                pending.Add(() => inst.Components.Remove(toRemove));
                                break;

                            case DiffAction.Move:
                                var existingIsUnhosted = item.Existing != null && (item.Existing.PlacedHosting ?? "Unhosted") == "Unhosted";
                                if (existingElement != null && existingIsUnhosted)
                                {
                                    MoveUnhosted(doc, existingElement, item.Target);
                                    createdOrMoved++;
                                    pending.Add(RecordPlaced(ctx, inst, item, existingElement, item.Existing.PlacedHosting, "Moved", item.Existing.Id));
                                    break;
                                }
                                // Hosted elements cannot be moved off their face: recreate.
                                goto case DiffAction.Replace;

                            case DiffAction.Replace:
                            case DiffAction.Create:
                                if (existingElement != null) doc.Delete(existingElement.Id);
                                try
                                {
                                    // Id is decided before creation so the element tag and the record agree.
                                    var componentId = item.Existing != null ? item.Existing.Id : Ids.New();
                                    string hosting;
                                    var created = CreateComponent(ctx, inst, def, item.Target, componentId, warnings, out hosting);
                                    createdOrMoved++;
                                    pending.Add(RecordPlaced(ctx, inst, item, created, hosting,
                                        item.Action == DiffAction.Create ? "Created" : "Recreated", componentId));
                                }
                                catch (ComponentPlacementException cex)
                                {
                                    componentFailures.Add(item.Label + ": " + cex.Message);
                                    var failedItem = item;
                                    var msg = cex.Message;
                                    pending.Add(() => RecordFailed(inst, failedItem, msg));
                                }
                                break;
                        }
                    }

                    var status = t.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        var why = pre.Errors.Count > 0 ? string.Join(" ", pre.Errors) : "Revit rolled back the transaction (" + status + ").";
                        return Fail(inst, result, "Component creation failed: " + why);
                    }
                }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    SentinelLog.Error("Placement failed for " + mark, ex);
                    return Fail(inst, result, "Component creation failed: " + ex.Message);
                }
            }

            // Committed: now update the in-memory records.
            foreach (var a in pending) a();
            if (sourceWasChanged)
            {
                DoorSetInstanceOperations.AcceptSourceChanges(inst, source.Current.Current);
                warnings.Add("Source door changes accepted: " + string.Join("; ", source.Comparison.Details));
            }
            inst.PlacedConfigurationHash = plan.ConfigurationHash;
            inst.PlacedDefinitionRevision = def.Revision;
            inst.LastError = componentFailures.Count > 0 ? string.Join(" ", componentFailures) : null;
            inst.Touch();

            warnings.AddRange(pre.Warnings.Distinct().Select(w => "Revit: " + w));
            warnings.AddRange(plan.Issues.Where(i => i.Severity == IssueSeverity.Warning).Select(i => i.Message));

            result.Messages.AddRange(componentFailures);
            result.Messages.AddRange(warnings);

            if (componentFailures.Count > 0 && createdOrMoved == 0 && !inst.HasPlacedElements)
                result.Outcome = PlacementOutcome.Failed;
            else if (componentFailures.Count > 0 || warnings.Count > 0)
                result.Outcome = PlacementOutcome.NeedsReview;
            else
                result.Outcome = PlacementOutcome.Placed;

            if (result.Outcome == PlacementOutcome.NeedsReview) inst.ReviewState = ReviewState.NeedsReview;
            else if (mode == PlacementMode.Confirmed && result.Outcome == PlacementOutcome.Placed) MarkReviewed(inst, ctx.User);
            else if (mode == PlacementMode.Batch) inst.ReviewState = ReviewState.NotReviewed;

            result.Messages.Insert(0, PlacementDiff.Summarize(diff));
            return result;
        }

        private static PlacementDoorResult Fail(DoorSetInstance inst, PlacementDoorResult result, string reason)
        {
            inst.LastError = reason;
            result.Outcome = PlacementOutcome.Failed;
            result.Messages.Add(reason);
            return result;
        }

        private static void MarkReviewed(DoorSetInstance inst, string user)
        {
            inst.ReviewState = ReviewState.Reviewed;
            inst.ReviewedBy = user;
            inst.ReviewedUtc = DateTime.UtcNow.ToString("o");
        }

        // ------------------------------------------------------------------ records

        private static Action RecordPlaced(PlacementContext ctx, DoorSetInstance inst, DiffItem item, Element element,
            string hosting, string verb, string componentId)
        {
            Vec3 pos;
            double rot;
            ElementPose.TryRead(element, out pos, out rot);
            var uid = element.UniqueId;
            var eid = RevitCompat.IdValue(element.Id);
            var target = item.Target;
            var existing = item.Existing;
            var user = ctx.User;

            return () =>
            {
                var c = existing;
                if (c == null)
                {
                    c = new PlacedComponentInstance { Id = componentId };
                    inst.Components.Add(c);
                }
                c.SlotKey = target.SlotKey;
                c.ComponentDefinitionId = target.ComponentDefinitionId;
                c.Label = target.Label;
                c.ElementUniqueId = uid;
                c.ElementId = eid;
                c.State = ComponentState.Placed;
                c.CalculatedPosition = target.Position;
                c.CalculatedRotationDeg = target.InstanceRotationDeg;
                c.PlacedPosition = pos;
                c.PlacedRotationDeg = rot;
                c.ActualPosition = pos;
                c.ActualRotationDeg = rot;
                c.ManualPositionAccepted = false;
                c.ManualPositionAcceptedAt = null;
                c.PlacedFamilyName = target.FamilyName;
                c.PlacedTypeName = target.TypeName;
                c.PlacedHosting = hosting;
                c.PlacedUtc = DateTime.UtcNow.ToString("o");
                c.PlacedBy = user;
                c.PlacementNote = verb + ": " + target.Explanation + (hosting == "Unhosted" ? "" : " [" + hosting + "]");
                c.LastError = null;
            };
        }

        private static void RecordFailed(DoorSetInstance inst, DiffItem item, string message)
        {
            var c = item.Existing;
            if (c == null)
            {
                c = new PlacedComponentInstance();
                inst.Components.Add(c);
            }
            c.SlotKey = item.Target.SlotKey;
            c.ComponentDefinitionId = item.Target.ComponentDefinitionId;
            c.Label = item.Target.Label;
            c.ElementUniqueId = null;
            c.ElementId = 0;
            c.State = ComponentState.Failed;
            c.CalculatedPosition = item.Target.Position;
            c.CalculatedRotationDeg = item.Target.InstanceRotationDeg;
            c.PlacementNote = item.Target.Explanation;
            c.LastError = message;
        }

        // ------------------------------------------------------------------ element creation

        private static FamilyInstance CreateComponent(PlacementContext ctx, DoorSetInstance inst, DoorSetDefinition def,
            CalculatedPlacement target, string componentId, List<string> warnings, out string hosting)
        {
            var doc = ctx.Doc;
            if (target.HasErrors)
                throw new ComponentPlacementException(target.Issues.First(i => i.Severity == IssueSeverity.Error).Code,
                    target.Issues.First(i => i.Severity == IssueSeverity.Error).Message.Replace(target.Label + ": ", ""));

            var symbol = ctx.Symbols.Find(target.FamilyName, target.TypeName);
            if (symbol == null)
                throw new ComponentPlacementException(IssueCodes.FamilyNotLoaded,
                    "family \"" + target.FamilyName + " : " + target.TypeName + "\" is not loaded in the project.");
            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            var pos = RevitUnits.ToXyzFt(target.Position);
            if (double.IsNaN(pos.X) || double.IsNaN(pos.Y) || double.IsNaN(pos.Z))
                throw new ComponentPlacementException(IssueCodes.InvalidPlacementPoint, "placement point invalid.");

            var placementType = symbol.Family.FamilyPlacementType;
            FamilyInstance fi;
            switch (placementType)
            {
                case FamilyPlacementType.OneLevelBased:
                    if (target.HostBehavior == HostBehavior.LinkedWallFace)
                        warnings.Add(target.Label + ": family is level-based and cannot be hosted on a wall face; placed unhosted.");
                    fi = CreateUnhosted(doc, symbol, pos, target.InstanceRotationDeg);
                    hosting = "Unhosted";
                    break;

                case FamilyPlacementType.WorkPlaneBased:
                    fi = CreateWorkPlaneBased(ctx, symbol, pos, target, warnings, out hosting);
                    break;

                case FamilyPlacementType.OneLevelBasedHosted:
                    throw new ComponentPlacementException(IssueCodes.UnsupportedFamily,
                        "wall-hosted families cannot be hosted by linked walls; use a face-based or level-based family.");

                default:
                    throw new ComponentPlacementException(IssueCodes.UnsupportedFamily,
                        "unsupported family placement type (" + placementType + "); use a face-based or level-based family.");
            }

            if (fi == null)
                throw new ComponentPlacementException(IssueCodes.CreationFailed, "component creation failed.");

            var level = LevelFinder.BelowOrLowest(doc, pos.Z);
            if (level != null)
            {
                var sched = fi.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
                if (sched != null && !sched.IsReadOnly) { try { sched.Set(level.Id); } catch { } }
            }

            WriteParameters(fi, ctx.Project.FindComponent(target.ComponentDefinitionId), target, warnings);
            WriteIdentity(ctx, fi, inst, def, target, componentId, warnings);
            return fi;
        }

        private static FamilyInstance CreateUnhosted(Document doc, FamilySymbol symbol, XYZ pos, double rotationDeg)
        {
            var level = LevelFinder.BelowOrLowest(doc, pos.Z);
            if (level == null) throw new ComponentPlacementException(IssueCodes.CreationFailed, "the project has no levels.");

            var fi = doc.Create.NewFamilyInstance(pos, symbol, level, StructuralType.NonStructural);
            var offset = pos.Z - level.Elevation;
            var p = fi.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM) ?? fi.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
            if (p != null && !p.IsReadOnly) p.Set(offset);
            doc.Regenerate();

            // Make sure the final point is exactly the calculated one (families differ in how Z is handled).
            var lp = fi.Location as LocationPoint;
            if (lp != null)
            {
                var delta = pos - lp.Point;
                if (delta.GetLength() > RevitUnits.MmToFt(0.5)) ElementTransformUtils.MoveElement(doc, fi.Id, delta);
            }
            RotateTo(doc, fi, rotationDeg);
            return fi;
        }

        private static void MoveUnhosted(Document doc, Element element, CalculatedPlacement target)
        {
            var lp = element.Location as LocationPoint;
            if (lp == null) throw new InvalidOperationException("Element has no point location.");
            var pos = RevitUnits.ToXyzFt(target.Position);
            var delta = pos - lp.Point;
            if (delta.GetLength() > 1e-6) ElementTransformUtils.MoveElement(doc, element.Id, delta);
            doc.Regenerate();
            RotateTo(doc, element, target.InstanceRotationDeg);
        }

        private static void RotateTo(Document doc, Element element, double targetDeg)
        {
            Vec3 pos;
            double current;
            if (!ElementPose.TryRead(element, out pos, out current)) return;
            var delta = targetDeg - current;
            if (Angles.Difference(targetDeg, current) < 0.01) return;
            var p = RevitUnits.ToXyzFt(pos);
            var axis = Line.CreateBound(p, p + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, element.Id, axis, delta * Math.PI / 180.0);
        }

        private static FamilyInstance CreateWorkPlaneBased(PlacementContext ctx, FamilySymbol symbol, XYZ pos,
            CalculatedPlacement target, List<string> warnings, out string hosting)
        {
            var doc = ctx.Doc;
            var wallSide = !target.WallNormal.IsZero;
            var wantsFace = target.HostBehavior != HostBehavior.Unhosted && wallSide;

            if (target.HostBehavior == HostBehavior.LinkedWallFace && !wallSide)
                warnings.Add(target.Label + ": in-wall components cannot be face hosted; placed on a vertical work plane.");

            if (wantsFace)
            {
                var n = RevitUnits.ToXyzDir(target.WallNormal);
                Reference face;
                XYZ onFace;
                var search = RevitUnits.MmToFt(Math.Max(100, ctx.Project.Settings.WallSearchDistanceMm));
                if (ctx.Walls != null && ctx.Walls.TryFind(pos, n, search, 0, out face, out onFace))
                {
                    try
                    {
                        var refDir = XYZ.BasisZ.CrossProduct(n).Normalize();
                        var fi = doc.Create.NewFamilyInstance(face, onFace, refDir, symbol);
                        hosting = "LinkedWallFace";
                        return fi;
                    }
                    catch (Exception ex)
                    {
                        SentinelLog.Warn("Face hosting failed for " + target.Label + ": " + ex.Message);
                        if (target.HostBehavior == HostBehavior.LinkedWallFace)
                            throw new ComponentPlacementException(IssueCodes.WallNotFound, "wall face found but hosting failed (" + ex.Message + ").");
                    }
                }
                else if (target.HostBehavior == HostBehavior.LinkedWallFace)
                {
                    throw new ComponentPlacementException(IssueCodes.WallNotFound, "Wall/reference could not be determined.");
                }
                warnings.Add(target.Label + ": no wall face found behind the calculated point; placed on a vertical work plane instead.");
            }

            // Vertical work plane through the calculated point, facing the device front.
            var normal = RevitUnits.ToXyzDir(wallSide ? target.WallNormal : target.Facing);
            if (normal.GetLength() < 1e-9) normal = XYZ.BasisY;
            normal = new XYZ(normal.X, normal.Y, 0).Normalize();
            var plane = Plane.CreateByNormalAndOrigin(normal, pos);
            var sp = SketchPlane.Create(doc, plane);
            var dir = XYZ.BasisZ.CrossProduct(normal).Normalize();
            var inst = doc.Create.NewFamilyInstance(sp.GetPlaneReference(), pos, dir, symbol);
            hosting = "WorkPlane";
            return inst;
        }

        // ------------------------------------------------------------------ data on elements

        private static void WriteParameters(FamilyInstance fi, ComponentDefinition def, CalculatedPlacement target, List<string> warnings)
        {
            if (def?.Parameters == null) return;
            foreach (var pa in def.Parameters)
            {
                if (pa == null || string.IsNullOrWhiteSpace(pa.Name)) continue;
                var p = fi.LookupParameter(pa.Name) ?? fi.Symbol?.LookupParameter(pa.Name);
                if (p == null || p.IsReadOnly || p.Element is ElementType)
                {
                    warnings.Add(target.Label + ": parameter \"" + pa.Name + "\" not found or read-only on the instance.");
                    continue;
                }
                if (!TrySet(p, pa.Value ?? ""))
                    warnings.Add(target.Label + ": value \"" + pa.Value + "\" could not be written to \"" + pa.Name + "\".");
            }
        }

        private static bool TrySet(Parameter p, string value)
        {
            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String:
                        return p.Set(value);
                    case StorageType.Integer:
                        int i;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return p.Set(i);
                        var v = value.Trim().ToLowerInvariant();
                        if (v == "yes" || v == "true" || v == "jah") return p.Set(1);
                        if (v == "no" || v == "false" || v == "ei") return p.Set(0);
                        return p.SetValueString(value);
                    case StorageType.Double:
                        return p.SetValueString(value);
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void WriteIdentity(PlacementContext ctx, FamilyInstance fi, DoorSetInstance inst, DoorSetDefinition def,
            CalculatedPlacement target, string componentId, List<string> warnings)
        {
            ElementTagStorage.Write(fi, new ElementTag
            {
                ProjectId = ctx.Project.ProjectId,
                SetId = inst.Id,
                ComponentId = componentId,
                SlotKey = target.SlotKey,
                DefinitionId = def.Id,
                ComponentDefinitionId = target.ComponentDefinitionId,
                SourceLinkUniqueId = inst.Source?.LinkInstanceUniqueId,
                SourceDoorUniqueId = inst.Source?.DoorUniqueId,
                SourceIfcGlobalId = inst.Source?.IfcGlobalId
            });

            var paramName = ctx.Project.Settings.IdentityParameterName;
            if (!string.IsNullOrWhiteSpace(paramName))
            {
                var p = fi.LookupParameter(paramName);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    p.Set((def.Code ?? def.Name) + " / " + (inst.Source?.DisplayName ?? ""));
                else
                    warnings.Add(target.Label + ": identity parameter \"" + paramName + "\" not found or not a writable text parameter.");
            }
        }
    }
}
