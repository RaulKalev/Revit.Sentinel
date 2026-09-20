using System;
using System.Collections.Generic;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Sentinel.Core.Status;

namespace Sentinel.UI.Services
{
    /// <summary>
    /// Everything the UI needs from Revit, expressed with Sentinel.Core types only. The Revit implementation
    /// queues each call onto an ExternalEvent (valid API context, Revit transactions) and invokes the callback on
    /// the UI dispatcher afterwards. The UI never touches the Revit API directly.
    /// </summary>
    public interface ISentinelHost
    {
        string DocumentTitle { get; }

        /// <summary>The loaded project (UI edits it in memory; persist with <see cref="SaveProject"/>).</summary>
        SentinelProject Project { get; }

        /// <summary>True when the stored data comes from a newer Sentinel: all writes are blocked.</summary>
        bool IsReadOnly { get; }

        /// <summary>Warnings produced while loading (corrupt items kept, newer version...).</summary>
        IList<string> LoadWarnings { get; }

        /// <summary>True when no Sentinel data existed and the starter library was created in memory.</summary>
        bool IsNewProject { get; }

        /// <summary>Raised when stored data changed outside Sentinel's own saves (Undo/Redo) and was reloaded.</summary>
        event EventHandler ProjectReloaded;

        /// <summary>Raised after other transactions deleted/modified elements that Sentinel tracks (element ids).</summary>
        event EventHandler<TrackedElementsChangedEventArgs> TrackedElementsChanged;

        /// <summary>Raised when the bound Revit document is closing; the UI should close.</summary>
        event EventHandler DocumentClosing;

        /// <summary>Releases event subscriptions and preview graphics.</summary>
        void Shutdown();

        void Load(Action<OperationResult> done);
        void SaveProject(string reason, Action<OperationResult> done);

        void GetLinks(Action<IList<LinkInfo>> done);
        /// <summary>Level names of the given links (union, lowest first).</summary>
        void GetLinkLevels(IList<string> linkUniqueIds, Action<IList<string>> done);
        void DiscoverDoors(DiscoveryRequest request, Action<DiscoveryResult> done);

        /// <summary>Verifies sources, checks placed components and rebuilds relationships from element tags.</summary>
        void Refresh(Action<RefreshResult> done);

        /// <summary>
        /// Measures wall material in front of the wall-side components of the given sets (all loaded models) and stores
        /// the push-outs on the instances. Message: what moved (null when nothing changed).
        /// </summary>
        void CheckWallClearances(IList<string> instanceIds, Action<OperationResult> done);

        void ShowPreview(PreviewScene scene, bool zoom, Action<OperationResult> done);
        void ClearPreview();

        void Place(PlacementRequest request, Action<PlacementBatchResult> done);

        /// <summary>Deletes the placed elements of the given instances (records are kept unless removeRecords).</summary>
        void DeletePlacedComponents(IList<string> instanceIds, bool removeRecords, Action<OperationResult> done);

        void Navigate(NavigationRequest request, Action<OperationResult> done);

        void GetFamilyTypes(Action<IList<FamilyTypeInfo>> done);
    }

    public class OperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }

        public static OperationResult Ok(string message = null) => new OperationResult { Success = true, Message = message };
        public static OperationResult Fail(string message) => new OperationResult { Success = false, Message = message };
    }

    public class TrackedElementsChangedEventArgs : EventArgs
    {
        public List<long> DeletedElementIds { get; set; } = new List<long>();
        public List<long> ModifiedElementIds { get; set; } = new List<long>();
    }

    public class LinkInfo
    {
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public string DocumentTitle { get; set; }
        public bool IsIfc { get; set; }
        public bool IsLoaded { get; set; }

        public string DisplayName => (IsIfc ? "[IFC] " : "") + Name + (IsLoaded ? "" : " (not loaded)");

        /// <summary>Name without Revit's instance suffix ("SA.ifc : 12" → "SA.ifc"), for summaries.</summary>
        public string ShortName => Short(Name);

        public static string Short(string linkName)
        {
            if (string.IsNullOrWhiteSpace(linkName)) return linkName;
            var i = linkName.LastIndexOf(" : ", StringComparison.Ordinal);
            return i > 0 ? linkName.Substring(0, i) : linkName;
        }
    }

    public class DiscoveryRequest
    {
        /// <summary>Linked models to collect doors from (one or more).</summary>
        public List<string> LinkUniqueIds { get; set; } = new List<string>();

        /// <summary>Leave out Doors-category elements whose type name starts with "Window".</summary>
        public bool SkipWindowTypes { get; set; }
        public DiscoveryScope Scope { get; set; }
        public List<string> LevelNames { get; set; } = new List<string>();
    }

    public class DiscoveryResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        /// <summary>Link names joined for messages ("AR.ifc + SA.ifc").</summary>
        public string LinkName { get; set; }

        /// <summary>Doors found per link, in request order ("AR.ifc: 97").</summary>
        public List<string> CountsByLink { get; set; } = new List<string>();

        /// <summary>Doors that appear to be modelled in more than one link.</summary>
        public int PossibleDuplicates { get; set; }

        public int SkippedWindowTypes { get; set; }
        public List<DiscoveredDoor> Doors { get; set; } = new List<DiscoveredDoor>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>Live state of one instance's source door.</summary>
    public class SourceCheck
    {
        public string InstanceId { get; set; }
        public SourceState State { get; set; }
        public DiscoveredDoor Current { get; set; }
        public SourceComparison Comparison { get; set; }

        /// <summary>Door or link identity was re-established (e.g. via IFC GlobalId) and updated in the record.</summary>
        public bool Reidentified { get; set; }
    }

    public class RefreshResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public Dictionary<string, SourceCheck> Sources { get; set; } = new Dictionary<string, SourceCheck>();
        public int MissingComponents { get; set; }
        public int ModifiedComponents { get; set; }
        public int RecoveredSets { get; set; }
        public int RelinkedComponents { get; set; }
        public int DuplicateElements { get; set; }
        public int UntrackedElements { get; set; }
        public List<string> Messages { get; set; } = new List<string>();
    }

    /// <summary>One door in the preview scene.</summary>
    public class PreviewDoor
    {
        public DoorGeometry Door { get; set; }
        public AccessDirection Direction { get; set; }
        public HingeSide Hinge { get; set; }
        public List<CalculatedPlacement> Placements { get; set; } = new List<CalculatedPlacement>();
        public bool IsCurrent { get; set; }
    }

    public class PreviewScene
    {
        public List<PreviewDoor> Doors { get; set; } = new List<PreviewDoor>();
    }

    public enum PlacementMode
    {
        /// <summary>User reviewed the preview and confirmed (instances marked Reviewed).</summary>
        Confirmed,
        /// <summary>Automatic batch placement (instances stay reviewable, marked NotReviewed/NeedsReview).</summary>
        Batch,
        /// <summary>Bring existing placements in line with the current configuration.</summary>
        Update
    }

    public class PlacementRequest
    {
        public List<string> InstanceIds { get; set; } = new List<string>();
        public PlacementMode Mode { get; set; }
        public bool OverwriteManual { get; set; }
    }

    public enum PlacementOutcome
    {
        Placed,
        NeedsReview,
        Failed,
        Skipped,
        NoChanges
    }

    public class PlacementDoorResult
    {
        public string InstanceId { get; set; }
        public string DoorName { get; set; }
        public PlacementOutcome Outcome { get; set; }
        public List<string> Messages { get; set; } = new List<string>();
    }

    public class PlacementBatchResult
    {
        public List<PlacementDoorResult> Doors { get; set; } = new List<PlacementDoorResult>();
        public string FatalError { get; set; }

        /// <summary>
        /// Source doors of the placed sets as read (and compared after placement) during this run. The UI merges these
        /// instead of running a full refresh afterwards, which re-read every door of the project and took seconds.
        /// </summary>
        public Dictionary<string, SourceCheck> Sources { get; set; } = new Dictionary<string, SourceCheck>();

        public int Count(PlacementOutcome o)
        {
            var n = 0;
            foreach (var d in Doors) if (d.Outcome == o) n++;
            return n;
        }

        public string Summary =>
            FatalError != null ? "Placement aborted: " + FatalError :
            Count(PlacementOutcome.Placed) + " placed, " + Count(PlacementOutcome.NeedsReview) + " require review, " +
            Count(PlacementOutcome.Failed) + " failed" +
            (Count(PlacementOutcome.NoChanges) > 0 ? ", " + Count(PlacementOutcome.NoChanges) + " unchanged" : "") +
            (Count(PlacementOutcome.Skipped) > 0 ? ", " + Count(PlacementOutcome.Skipped) + " skipped" : "");
    }

    public enum NavigationKind
    {
        /// <summary>Zoom to the source door (and the set's components).</summary>
        ZoomToDoor,
        /// <summary>Select the placed components in Revit.</summary>
        SelectComponents,
        /// <summary>Select the linked source door (Revit 2023+ linked selection).</summary>
        SelectSourceDoor
    }

    public class NavigationRequest
    {
        public NavigationKind Kind { get; set; }
        public SourceDoorReference Source { get; set; }
        public DoorGeometry Geometry { get; set; }
        public List<string> ElementUniqueIds { get; set; } = new List<string>();

        /// <summary>View to zoom in; null = the project setting.</summary>
        public DoorZoomView? View { get; set; }
    }

    public class FamilyTypeInfo
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string CategoryName { get; set; }
        public string PlacementType { get; set; }
        public bool IsSupported { get; set; }

        public string DisplayName => FamilyName + " : " + TypeName;
    }
}
