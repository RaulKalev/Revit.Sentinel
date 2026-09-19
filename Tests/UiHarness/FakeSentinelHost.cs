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
            if (source == DoorGeometrySource.EstimatedFromGeometry) d.ReadWarnings.Add("Door orientation estimated from geometry; hinge side unknown.");
            (interior ? _interiorDoors : _doors).Add(d);
        }

        public DiscoveredDoor Door(string mark) => AllDoors.First(d => d.Current.Mark == mark);

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
            if (r.PossibleDuplicates > 0) r.Warnings.Add(r.PossibleDuplicates + " door(s) appear in more than one link.");
            var estimated = r.Doors.Count(d => d.Geometry.Source == DoorGeometrySource.EstimatedFromGeometry);
            if (estimated > 0) r.Warnings.Add(estimated + " door(s) use estimated IFC geometry (hinge side unknown).");
            Log.Add("DISCOVER " + request.Scope + " [" + string.Join(",", request.LinkUniqueIds) + "] → " + r.Doors.Count);
            Post(() => done(r));
        }

        public void Refresh(Action<RefreshResult> done)
        {
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
                foreach (var item in diff)
                {
                    switch (item.Action)
                    {
                        case DiffAction.Remove:
                            inst.Components.Remove(item.Existing);
                            break;
                        case DiffAction.Create:
                        case DiffAction.Move:
                        case DiffAction.Replace:
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
            }
            SaveCount++;
            Log.Add("PLACE " + request.Mode + ": " + batch.Summary);
            Post(() => done(batch));
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
            Log.Add("NAVIGATE " + request.Kind + " " + request.Source?.Mark);
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
