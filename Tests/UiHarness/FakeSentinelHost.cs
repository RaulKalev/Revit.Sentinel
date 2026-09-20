using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using Sentinel.Core.Geometry;
using Sentinel.Core.Library;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Sentinel.Core.Status;
using Sentinel.UI.Services;

namespace Sentinel.UiHarness
{
    /// <summary>
    /// In-memory stand-in for the Revit host: synthetic linked doors, simulated placement (elements are fake ids),
    /// simulated deletions/moves. Callbacks are posted to the dispatcher like the real host.
    /// </summary>
    public class FakeSentinelHost : ISentinelHost
    {
        public const string LinkUid = "link-arh";
        private readonly Dispatcher _dispatcher;
        private readonly List<DiscoveredDoor> _doors = new List<DiscoveredDoor>();
        private readonly List<DiscoveredDoor> _interiorDoors = new List<DiscoveredDoor>();

        /// <summary>Second architecture link (interior), as in projects where doors are split across models.</summary>
        public const string InteriorLinkUid = "link-sis";

        private IEnumerable<DiscoveredDoor> AllDoors => _doors.Concat(_interiorDoors);
        private readonly HashSet<string> _deletedElements = new HashSet<string>();
        private int _elementCounter = 1000;

        public FakeSentinelHost(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            BuildDoors();
            Project = DefaultLibrary.CreateProject();
            foreach (var c in Project.ComponentDefinitions.Where(c => c.Category != ComponentCategory.Intercom))
            {
                c.FamilyName = "EULE_Security_" + c.Category;
                c.TypeName = "Standard";
            }
            Project.Settings.DiscoveryLinkUniqueId = LinkUid;
        }

        public string DocumentTitle => "Harness_Project_EL";
        public SentinelProject Project { get; private set; }
        public bool IsReadOnly { get; set; }
        public IList<string> LoadWarnings { get; set; } = new List<string>();
        public bool IsNewProject { get; set; }
        public PreviewScene LastScene { get; private set; }
        public int SaveCount { get; private set; }
        public List<string> Log { get; } = new List<string>();

        public event EventHandler ProjectReloaded;
        public event EventHandler<TrackedElementsChangedEventArgs> TrackedElementsChanged;
        public event EventHandler DocumentClosing;

        public IReadOnlyList<DiscoveredDoor> Doors => _doors;

        private void Post(Action a) => _dispatcher.BeginInvoke(a);

        // ------------------------------------------------------------------ synthetic model

        private void BuildDoors()
        {
            // Corridor along X between y = -1500 and y = +1500. North offices face +Y (side A = office).
            for (int i = 0; i < 8; i++)
            {
                AddDoor("D10" + (i + 1), new Vec3(2000 + i * 3500, 1500, 0), Vec3.UnitY, Vec3.UnitX,
                    "1.0" + (i + 1) + " Office", "1.00 Corridor", i % 3 == 0 ? "EI30" : "", DoorGeometrySource.FamilyInstance);
            }
            // South side (facing -Y): technical rooms.
            AddDoor("D120", new Vec3(4000, -1500, 0), -Vec3.UnitY, -Vec3.UnitX, "1.20 Server Room", "1.00 Corridor", "EI60", DoorGeometrySource.FamilyInstance);
            AddDoor("D121", new Vec3(9000, -1500, 0), -Vec3.UnitY, -Vec3.UnitX, "1.21 Electrical Room", "1.00 Corridor", "EI60", DoorGeometrySource.FamilyInstance);
            AddDoor("D122", new Vec3(14000, -1500, 0), -Vec3.UnitY, -Vec3.UnitX, "1.22 Archive", "1.00 Corridor", "EI30", DoorGeometrySource.EstimatedFromGeometry);
            // Rotated door in a 30° wall.
            var f = Vec3.UnitY.RotateAboutZ(30);
            AddDoor("D130", new Vec3(30000, 3000, 0), f, Vec3.UnitX.RotateAboutZ(30), "1.30 Meeting", "1.00 Corridor", "", DoorGeometrySource.FamilyInstance);
            AddDoor("D131", new Vec3(33000, 3000, 3600), Vec3.UnitY, Vec3.UnitX, "2.01 Office", "2.00 Lobby", "", DoorGeometrySource.FamilyInstance, "Level 2");

            // Interior link: one door modelled in both links (same opening as D103), two interior-only doors and a
            // window exported into the Doors category.
            AddDoor("SKU13", new Vec3(9080, 1540, 0), Vec3.UnitY, Vec3.UnitX, "1.03 Office", "1.00 Corridor", "", DoorGeometrySource.EstimatedFromGeometry, interior: true);
            AddDoor("SKU20", new Vec3(20000, 6000, 0), Vec3.UnitY, Vec3.UnitX, "1.40 Kitchen", "1.41 Store", "", DoorGeometrySource.EstimatedFromGeometry, interior: true);
            AddDoor("SKU21", new Vec3(23000, 6000, 0), Vec3.UnitY, Vec3.UnitX, "1.42 Toilet", "1.40 Kitchen", "", DoorGeometrySource.EstimatedFromGeometry, interior: true);
            AddDoor("SW1", new Vec3(26000, 6000, 0), Vec3.UnitY, Vec3.UnitX, "1.43 Office", "1.40 Kitchen", "", DoorGeometrySource.EstimatedFromGeometry, interior: true, typeName: "Window 27");
        }

        private void AddDoor(string mark, Vec3 origin, Vec3 facing, Vec3 width, string roomA, string roomB, string fire,
            DoorGeometrySource source, string level = "Level 1", bool interior = false, string typeName = null)
        {
            var d = new DiscoveredDoor
            {
                Current = new SourceDoorReference
                {
                    LinkInstanceUniqueId = interior ? InteriorLinkUid : LinkUid,
                    LinkName = interior ? "SIS_Model.ifc : 2" : "ARH_Model.ifc",
                    LinkDocumentTitle = interior ? "SIS_Model.ifc" : "ARH_Model.ifc",
                    DoorUniqueId = "door-" + mark,
                    DoorElementId = 400000 + _doors.Count + _interiorDoors.Count,
                    IfcGlobalId = "3" + mark + "x$Ab12CdEfGhIjKl".Substring(0, 12),
                    Mark = mark,
                    FamilyName = source == DoorGeometrySource.FamilyInstance ? "M_Single-Flush" : null,
                    TypeName = typeName ?? (source == DoorGeometrySource.FamilyInstance ? "1000 x 2100mm" : "IfcDoor Single Swing"),
                    LevelName = level,
                    SideARoom = roomA,
                    SideBRoom = roomB,
                    LastKnownGeometry = new DoorGeometry
                    {
                        Origin = origin,
                        Facing = facing,
                        WidthAxis = width,
                        WidthMm = 1000,
                        HeightMm = 2100,
                        WallThicknessMm = 200,
                        Hinge = source == DoorGeometrySource.FamilyInstance ? HingeSide.NegativeWidthAxis : HingeSide.Unknown,
                        Source = source
                    },
                    LastKnownParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "FireRating", fire },
                        { "IsExternal", "False" }
                    }
                }
            };
            _allParameters[mark] = new Dictionary<string, string>(d.Current.LastKnownParameters, StringComparer.OrdinalIgnoreCase);
            if (source == DoorGeometrySource.EstimatedFromGeometry) d.ReadWarnings.Add("Door orientation estimated from geometry; hinge side unknown.");
            (interior ? _interiorDoors : _doors).Add(d);
        }

        public DiscoveredDoor Door(string mark) => AllDoors.First(d => d.Current.Mark == mark);

        // Every parameter each door has in the "linked model"; discovery reads only the captured ones, like Revit.
        private readonly Dictionary<string, Dictionary<string, string>> _allParameters = new Dictionary<string, Dictionary<string, string>>();

        /// <summary>Gives a door a parameter in the linked model (read by the next discovery if it is captured).</summary>
        public void SetParameter(string mark, string name, string value) => _allParameters[mark][name] = value;

        /// <summary>Simulates a change in the linked model (door moved).</summary>
        public void MoveDoor(string mark, Vec3 delta)
        {
            var g = Door(mark).Current.LastKnownGeometry;
            g.Origin = g.Origin + delta;
        }

        public void RemoveDoor(string mark) => _doors.RemoveAll(d => d.Current.Mark == mark);

        /// <summary>Simulates the user deleting a placed element in Revit (raises the change event).</summary>
        public void DeleteElement(string uid)
        {
            _deletedElements.Add(uid);
            var c = Project.DoorSetInstances.SelectMany(i => i.Components).FirstOrDefault(x => x.ElementUniqueId == uid);
            if (c != null)
                Post(() => TrackedElementsChanged?.Invoke(this, new TrackedElementsChangedEventArgs { DeletedElementIds = { c.ElementId } }));
        }

        // ------------------------------------------------------------------ ISentinelHost

        public void Load(Action<OperationResult> done) => Post(() => done(OperationResult.Ok("Loaded harness project.")));

        public void SaveProject(string reason, Action<OperationResult> done)
        {
            SaveCount++;
            Log.Add("SAVE (" + reason + ")");
            Post(() => done(OperationResult.Ok()));
        }

        public void GetLinks(Action<IList<LinkInfo>> done) => Post(() => done(new List<LinkInfo>
        {
            new LinkInfo { UniqueId = LinkUid, Name = "ARH_Model.ifc", DocumentTitle = "ARH_Model.ifc", IsIfc = true, IsLoaded = true },
            new LinkInfo { UniqueId = InteriorLinkUid, Name = "SIS_Model.ifc : 2", DocumentTitle = "SIS_Model.ifc", IsIfc = true, IsLoaded = true },
            new LinkInfo { UniqueId = "link-rkv", Name = "RKV_Model.rvt", DocumentTitle = "RKV_Model", IsIfc = false, IsLoaded = false }
        }));

        public void GetLinkLevels(IList<string> linkUniqueIds, Action<IList<string>> done) =>
            Post(() => done(new List<string> { "Level 1", "Level 2", "Roof" }));

        public void DiscoverDoors(DiscoveryRequest request, Action<DiscoveryResult> done)
        {
            var r = new DiscoveryResult { Success = true };
            var names = new List<string>();
            foreach (var uid in request.LinkUniqueIds)
            {
                var source = uid == LinkUid ? _doors : uid == InteriorLinkUid ? _interiorDoors : null;
                if (source == null) { r.Warnings.Add("\"" + uid + "\" is not loaded."); continue; }
                var name = uid == LinkUid ? "ARH_Model.ifc" : "SIS_Model.ifc";
                names.Add(name);
                var found = source.Where(d => request.Scope != DiscoveryScope.SelectedLevels || request.LevelNames.Contains(d.Current.LevelName)).ToList();
                var captured = Project.Settings.CapturedParameterNames ?? new List<string>();
                foreach (var d in found)
                    d.Current.LastKnownParameters = _allParameters[d.Current.Mark]
                        .Where(kv => captured.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                if (request.SkipWindowTypes)
                {
                    var before = found.Count;
                    found = found.Where(d => !(d.Current.TypeName ?? "").StartsWith("Window", StringComparison.OrdinalIgnoreCase)).ToList();
                    r.SkippedWindowTypes += before - found.Count;
                }
                r.Doors.AddRange(found.Select(d => new DiscoveredDoor { Current = d.Current.Clone(), ReadWarnings = d.ReadWarnings.ToList() }));
                r.CountsByLink.Add(name + ": " + found.Count);
            }
            if (names.Count == 0) { Post(() => done(new DiscoveryResult { Error = "Select at least one linked model first." })); return; }
            r.LinkName = string.Join(" + ", names);
            r.PossibleDuplicates = CrossLinkDuplicates.Mark(r.Doors);
            foreach (var d in r.Doors.Where(x => x.PossibleDuplicateOf != null)) d.ReadWarnings.Add("Probably the same door as " + d.PossibleDuplicateOf + ".");
            if (r.SkippedWindowTypes > 0) r.Warnings.Add(r.SkippedWindowTypes + " window type(s) in the Doors category were left out.");
            var estimated = r.Doors.Count(d => d.Geometry.Source == DoorGeometrySource.EstimatedFromGeometry);
            if (estimated > 0) r.Warnings.Add(estimated + " door(s) use estimated IFC geometry.");
            Log.Add("DISCOVER " + request.Scope + " [" + string.Join(",", request.LinkUniqueIds) + "] → " + r.Doors.Count);
            Post(() => done(r));
        }

        public void Refresh(Action<RefreshResult> done)
        {
            Log.Add("REFRESH (all doors)");
            var r = new RefreshResult { Success = true };
            foreach (var inst in Project.DoorSetInstances)
            {
                var check = new SourceCheck { InstanceId = inst.Id };
                var door = AllDoors.FirstOrDefault(d => d.Key == inst.Source.Key);
                if (door == null) check.State = SourceState.Missing;
                else
                {
                    check.Current = new DiscoveredDoor { Current = door.Current.Clone() };
                    check.Comparison = SourceChangeDetector.Compare(inst.Source, door.Geometry, door.Current.LastKnownParameters, Project.Settings);
                    check.State = check.Comparison.HasChanges ? SourceState.Changed : SourceState.Ok;
                }
                r.Sources[inst.Id] = check;

                foreach (var c in inst.Components)
                {
                    if (string.IsNullOrEmpty(c.ElementUniqueId)) continue;
                    if (_deletedElements.Contains(c.ElementUniqueId)) { c.State = ComponentState.Missing; r.MissingComponents++; }
                    else if (c.State == ComponentState.Missing) c.State = ComponentState.Placed;
                }
            }
            Log.Add("REFRESH missing=" + r.MissingComponents);
            Post(() => done(r));
        }

        /// <summary>Doors with a 45 mm lining (another model) on side A, overlapping the door's wall face by 5 mm.</summary>
        public HashSet<string> LinedDoors { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void CheckWallClearances(IList<string> instanceIds, Action<OperationResult> done)
        {
            var moved = 0;
            foreach (var inst in instanceIds.Select(Project.FindInstance).Where(i => i != null))
            {
                var def = Project.FindDoorSet(inst.DefinitionId);
                var door = AllDoors.FirstOrDefault(d => d.Key == inst.Source?.Key);
                if (def == null || door == null) continue;
                var plan = DoorSetPlacementCalculator.Calculate(inst, def, door.Geometry, Project, false);
                var measured = new Dictionary<string, double>();
                foreach (var p in plan.Placements.Where(x => !x.IsBuiltIn && x.Side == ResolvedSide.SideA && LinedDoors.Contains(door.Current.Mark)))
                {
                    var exit = WallClearance.ExitDistance(new[] { new MaterialSpan(-5, 45), new MaterialSpan(-200, 0) });
                    if (exit >= 1) measured[WallClearance.Key(p.SlotKey, p.Side.ToString())] = exit;
                }
                if (WallClearance.Store(inst, measured)) moved += measured.Count;
            }
            Log.Add("WALLCHECK " + moved);
            Post(() => done(OperationResult.Ok(moved > 0 ? moved + " component(s) moved out of a wall." : null)));
        }

        public void ShowPreview(PreviewScene scene, bool zoom, Action<OperationResult> done)
        {
            LastScene = scene;
            Log.Add("PREVIEW " + string.Join(",", scene.Doors.SelectMany(d => d.Placements).Select(p => p.Label + "@" + p.Side)));
            Post(() => done(OperationResult.Ok()));
        }

        public void ClearPreview()
        {
            LastScene = null;
            Log.Add("PREVIEW CLEARED");
        }

        public void Place(PlacementRequest request, Action<PlacementBatchResult> done)
        {
            var batch = new PlacementBatchResult();
            foreach (var id in request.InstanceIds)
            {
                var inst = Project.FindInstance(id);
                var result = new PlacementDoorResult { InstanceId = id, DoorName = inst?.Source?.DisplayName };
                batch.Doors.Add(result);
                var door = inst != null ? AllDoors.FirstOrDefault(d => d.Key == inst.Source.Key) : null;
                if (inst == null || door == null)
                {
                    result.Outcome = PlacementOutcome.Failed;
                    result.Messages.Add("Source door no longer available.");
                    if (inst != null) inst.LastError = "Source door no longer available.";
                    continue;
                }
                var def = Project.FindDoorSet(inst.DefinitionId);
                var plan = DoorSetPlacementCalculator.Calculate(inst, def, door.Geometry, Project);
                var diff = PlacementDiff.Compute(inst, plan, request.OverwriteManual);
                var failures = new List<string>();
                // Same order and re-apply rule as the Revit executor: built-in components after their carriers.
                var carriersInWork = new HashSet<string>(diff.Where(d => d.Target != null && d.Action != DiffAction.Remove &&
                    d.Action != DiffAction.Unchanged && d.Action != DiffAction.KeepManual).Select(d => d.SlotKey));
                foreach (var d in diff.Where(d => d.Target != null && d.Target.IsBuiltIn && d.Action == DiffAction.Unchanged &&
                                                  carriersInWork.Contains(d.Target.CarrierSlotKey)))
                    d.Action = DiffAction.Move;
                foreach (var item in diff.OrderBy(d => d.Target != null && d.Target.IsBuiltIn ? 1 : 0))
                {
                    if (item.Target != null && item.Target.IsBuiltIn && item.Action != DiffAction.Remove &&
                        item.Action != DiffAction.Unchanged && item.Action != DiffAction.KeepManual)
                    {
                        var carrier = inst.Components.FirstOrDefault(x => x.SlotKey == item.Target.CarrierSlotKey && !x.IsBuiltIn && x.State == ComponentState.Placed);
                        var b = item.Existing ?? new PlacedComponentInstance();
                        if (item.Existing == null) inst.Components.Add(b);
                        b.SlotKey = item.Target.SlotKey;
                        b.Label = item.Target.Label;
                        b.ComponentDefinitionId = item.Target.ComponentDefinitionId;
                        b.CalculatedPosition = item.Target.Position;
                        b.CalculatedRotationDeg = item.Target.InstanceRotationDeg;
                        if (carrier == null)
                        {
                            b.State = ComponentState.Failed;
                            b.ElementUniqueId = null;
                            b.LastError = "the " + item.Target.CarrierLabel + " is not placed.";
                            failures.Add(item.Label + ": " + b.LastError);
                            continue;
                        }
                        SetParameter(carrier.ElementUniqueId, item.Target.CarrierParameter, 1);
                        SetParameter(carrier.ElementUniqueId, item.Target.CarrierOtherParameter, 0);
                        b.ElementUniqueId = carrier.ElementUniqueId;
                        b.ElementId = carrier.ElementId;
                        b.State = ComponentState.Placed;
                        b.PlacedPosition = carrier.PlacedPosition;
                        b.PlacedFamilyName = null;
                        b.PlacedTypeName = null;
                        b.PlacedHosting = BuiltInHosting.For(item.Target.CarrierParameter);
                        b.PlacementNote = "Built in: " + item.Target.Explanation;
                        b.LastError = null;
                        continue;
                    }
                    var wasBuiltIn = item.Existing != null && item.Existing.IsBuiltIn;
                    switch (item.Action)
                    {
                        case DiffAction.Remove:
                            if (wasBuiltIn) SetParameter(item.Existing.ElementUniqueId, BuiltInHosting.ParameterOf(item.Existing.PlacedHosting), 0);
                            else DeletedByPlacement.Add(item.Existing.ElementUniqueId);
                            inst.Components.Remove(item.Existing);
                            break;
                        case DiffAction.Create:
                        case DiffAction.Move:
                        case DiffAction.Replace:
                            if (wasBuiltIn) SetParameter(item.Existing.ElementUniqueId, BuiltInHosting.ParameterOf(item.Existing.PlacedHosting), 0);
                            else if (item.Existing?.ElementUniqueId != null) DeletedByPlacement.Add(item.Existing.ElementUniqueId);
                            var c = item.Existing ?? new PlacedComponentInstance();
                            if (item.Existing == null) inst.Components.Add(c);
                            c.SlotKey = item.Target.SlotKey;
                            c.Label = item.Target.Label;
                            c.ComponentDefinitionId = item.Target.ComponentDefinitionId;
                            c.CalculatedPosition = item.Target.Position;
                            c.CalculatedRotationDeg = item.Target.InstanceRotationDeg;
                            if (item.Target.HasErrors)
                            {
                                c.State = ComponentState.Failed;
                                c.ElementUniqueId = null;
                                c.LastError = item.Target.Issues.First().Message;
                                failures.Add(item.Label + ": " + c.LastError);
                                break;
                            }
                            var n = _elementCounter++;
                            c.ElementUniqueId = "elem-" + n;
                            c.ElementId = n;
                            c.State = ComponentState.Placed;
                            c.PlacedPosition = item.Target.Position;
                            c.PlacedRotationDeg = item.Target.InstanceRotationDeg;
                            c.PlacedFamilyName = item.Target.FamilyName;
                            c.PlacedTypeName = item.Target.TypeName;
                            c.PlacedHosting = "Unhosted";
                            c.PlacementNote = item.Action + ": " + item.Target.Explanation;
                            c.LastError = null;
                            break;
                    }
                }
                DoorSetInstanceOperations.AcceptSourceChanges(inst, door.Current);
                inst.PlacedConfigurationHash = plan.ConfigurationHash;
                inst.LastError = failures.Count > 0 ? string.Join(" ", failures) : null;
                result.Messages.Add(PlacementDiff.Summarize(diff));
                result.Messages.AddRange(failures);
                result.Messages.AddRange(plan.Issues.Where(i => i.Severity == IssueSeverity.Warning).Select(i => i.Message));
                result.Outcome = failures.Count > 0 || plan.Issues.Any(i => i.Severity == IssueSeverity.Warning)
                    ? PlacementOutcome.NeedsReview : PlacementOutcome.Placed;
                inst.ReviewState = result.Outcome == PlacementOutcome.NeedsReview ? ReviewState.NeedsReview :
                    request.Mode == PlacementMode.Confirmed ? ReviewState.Reviewed : ReviewState.NotReviewed;
                // Like the real host: the door as read for this placement, compared after accepting its changes.
                var cmp = SourceChangeDetector.Compare(inst.Source, door.Geometry, door.Current.LastKnownParameters, Project.Settings);
                batch.Sources[id] = new SourceCheck
                {
                    InstanceId = id,
                    Current = door,
                    Comparison = cmp,
                    State = cmp.HasChanges ? SourceState.Changed : SourceState.Ok
                };
            }
            SaveCount++;
            Log.Add("PLACE " + request.Mode + ": " + batch.Summary);
            Post(() => done(batch));
        }

        /// <summary>Yes/No parameter values written to fake elements (element uid → parameter → 0/1).</summary>
        public Dictionary<string, Dictionary<string, int>> ElementParameters { get; } = new Dictionary<string, Dictionary<string, int>>();

        /// <summary>Elements a placement run deleted (a built-in component must never delete its carrier).</summary>
        public HashSet<string> DeletedByPlacement { get; } = new HashSet<string>();

        public int ParameterValue(string elementUid, string name)
        {
            Dictionary<string, int> map;
            int v;
            return elementUid != null && ElementParameters.TryGetValue(elementUid, out map) && map.TryGetValue(name, out v) ? v : -1;
        }

        private void SetParameter(string elementUid, string name, int value)
        {
            if (elementUid == null || string.IsNullOrWhiteSpace(name)) return;
            Dictionary<string, int> map;
            if (!ElementParameters.TryGetValue(elementUid, out map)) ElementParameters[elementUid] = map = new Dictionary<string, int>();
            map[name] = value;
        }

        public void DeletePlacedComponents(IList<string> instanceIds, bool removeRecords, Action<OperationResult> done)
        {
            var n = 0;
            foreach (var inst in instanceIds.Select(Project.FindInstance).Where(i => i != null))
            {
                n += inst.Components.Count(c => !string.IsNullOrEmpty(c.ElementUniqueId));
                if (removeRecords) inst.Components.Clear();
                else foreach (var c in inst.Components) { c.ElementUniqueId = null; c.State = ComponentState.Planned; }
            }
            Log.Add("DELETE components " + n);
            Post(() => done(OperationResult.Ok(n + " element(s) deleted.")));
        }

        public void Navigate(NavigationRequest request, Action<OperationResult> done)
        {
            Log.Add("NAVIGATE " + request.Kind + " " + request.Source?.Mark + " " + (request.View?.ToString() ?? "default"));
            Post(() => done(OperationResult.Ok()));
        }

        public void GetFamilyTypes(Action<IList<FamilyTypeInfo>> done)
        {
            var list = Project.ComponentDefinitions.Where(c => c.IsFamilyConfigured).Select(c => new FamilyTypeInfo
            {
                FamilyName = c.FamilyName,
                TypeName = c.TypeName,
                CategoryName = "Security Devices",
                PlacementType = c.Category == ComponentCategory.ElectricLock ? "OneLevelBased" : "WorkPlaneBased",
                IsSupported = true
            }).ToList();
            list.Add(new FamilyTypeInfo { FamilyName = "EULE_Intercom_Panel", TypeName = "Surface", CategoryName = "Communication Devices", PlacementType = "WorkPlaneBased", IsSupported = true });
            list.Add(new FamilyTypeInfo { FamilyName = "Wall_Hosted_Box", TypeName = "Type 1", CategoryName = "Electrical Fixtures", PlacementType = "OneLevelBasedHosted", IsSupported = false });
            Post(() => done(list));
        }

        public void Shutdown() => Log.Add("SHUTDOWN");

        public void RaiseReloaded() => Post(() => ProjectReloaded?.Invoke(this, EventArgs.Empty));
        public void RaiseClosing() => Post(() => DocumentClosing?.Invoke(this, EventArgs.Empty));
    }
}
