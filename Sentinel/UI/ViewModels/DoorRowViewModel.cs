using System.Globalization;
using System.Linq;
using Sentinel.Core.Models;
using Sentinel.Core.Status;
using Sentinel.UI.Mvvm;
using Sentinel.UI.Services;

namespace Sentinel.UI.ViewModels
{
    /// <summary>One door in the Doors grid: a discovered door, a stored set instance, or both.</summary>
    public class DoorRowViewModel : ObservableObject
    {
        private readonly SentinelSession _session;

        public DoorRowViewModel(SentinelSession session, DiscoveredDoor door, DoorSetInstance instance)
        {
            _session = session;
            Door = door;
            Instance = instance;
            Update();
        }

        public DiscoveredDoor Door { get; set; }
        public DoorSetInstance Instance { get; set; }

        /// <summary>Source for display: live discovery data if available, else the stored snapshot.</summary>
        public SourceDoorReference Source => Door?.Current ?? Instance?.Source;

        public string Key => Source?.Key;

        public string Mark { get; private set; }
        public string TypeName { get; private set; }
        public string Level { get; private set; }
        public string Rooms { get; private set; }
        public string AccessText { get; private set; }

        /// <summary>Controlled direction ("Corridor → Office") or, without a set, both sides ("Office · Corridor").</summary>
        public string AccessOrRooms => !string.IsNullOrEmpty(AccessText) ? AccessText : SentinelSession.SideName(Source, true) + "  ·  " + SentinelSession.SideName(Source, false);
        public string SetCode { get; private set; }
        public string SetName { get; private set; }
        public SetStatus Status { get; private set; }
        public string StatusText { get; private set; }
        public string StatusBrushKey { get; private set; }
        public string Reason { get; private set; }
        public string ReviewText { get; private set; }
        public string ComponentsText { get; private set; }
        public string SearchBlob { get; private set; }
        public SetStatusResult Evaluation { get; private set; }

        public bool IsAssigned => Instance != null && !string.IsNullOrEmpty(Instance.DefinitionId) && !Instance.IsIgnored;
        public bool HasPlacedElements => Instance != null && Instance.HasPlacedElements;

        /// <summary>A cross-link duplicate left out in favour of the same door in a higher-priority model.</summary>
        public bool IsLeftOutDuplicate { get; private set; }

        /// <summary>
        /// Both doors of a cross-link duplicate are listed (neither was left out, e.g. both have a set): worth a hint.
        /// </summary>
        public bool ShowsDuplicateHint =>
            !IsLeftOutDuplicate && !string.IsNullOrEmpty(Door?.PossibleDuplicateOf) &&
            !(Door.DuplicatePartner?.PreferredDuplicate == Door && _session.Project.FindInstanceBySourceKey(Door.DuplicatePartner.Key) == null);

        public void Update()
        {
            var src = Source;
            Mark = src?.DisplayName ?? "(unknown door)";
            TypeName = src?.TypeName;
            Level = src?.LevelName;
            Rooms = SentinelSession.SideName(src, true) + " | " + SentinelSession.SideName(src, false);

            var def = Instance != null ? _session.Project.FindDoorSet(Instance.DefinitionId) : null;
            var none = Instance != null && Instance.IsNoAccessControl;
            SetCode = none ? "None" : Instance == null || string.IsNullOrEmpty(Instance.DefinitionId) ? "—" : def?.Code ?? "(deleted)";
            SetName = none ? NoAccessControlChoice.Name : def?.Name ?? "";
            AccessText = Instance != null && !string.IsNullOrEmpty(Instance.DefinitionId)
                ? SentinelSession.AccessText(src, Instance.AccessDirection)
                : "";

            Evaluation = _session.Evaluate(Instance);
            // Same door in a model with a higher priority (Settings → Doors in several models): that one is listed instead.
            IsLeftOutDuplicate = Instance == null && Door?.PreferredDuplicate?.Current != null;
            if (IsLeftOutDuplicate)
            {
                var other = Door.PreferredDuplicate.Current;
                Evaluation = new SetStatusResult { Status = SetStatus.Ignored };
                Evaluation.Issues.Add(new StatusIssue(IssueSeverity.Info, IssueCodes.DuplicateLeftOut,
                    "Same door as " + other.DisplayName + " in " + LinkInfo.Short(other.LinkName) + ", which is used instead (model priority in Settings)."));
            }
            Status = Evaluation.Status;
            StatusText = DoorSetStatusEvaluator.StatusText(Status);
            StatusBrushKey = BrushKey(Status);
            Reason = Evaluation.PrimaryReason;
            // Same opening in another source link (architecture split across models): say so while the door is free.
            if (string.IsNullOrEmpty(Reason) && ShowsDuplicateHint)
                Reason = "Probably the same door as " + Door.PossibleDuplicateOf;

            if (Instance == null || string.IsNullOrEmpty(Instance.DefinitionId))
                ReviewText = "";
            else if (Instance.ReviewState == ReviewState.Reviewed) ReviewText = "✓ Reviewed";
            else if (Instance.ReviewState == ReviewState.NeedsReview) ReviewText = "⚠ Review";
            else ReviewText = Instance.HasPlacedElements ? "Not reviewed" : "";

            if (Instance != null && Instance.Components.Count > 0)
            {
                var placed = Instance.Components.Count(c => c.State == ComponentState.Placed || c.State == ComponentState.ManuallyModified);
                ComponentsText = placed.ToString(CultureInfo.InvariantCulture) + "/" + Instance.Components.Count;
            }
            else ComponentsText = "";

            SearchBlob = string.Join(" ", new[]
            {
                Mark, TypeName, Level, src?.SideARoom, src?.SideBRoom, SetCode, SetName, StatusText, src?.IfcGlobalId,
                src?.FamilyName, src?.LinkName, Door?.PossibleDuplicateOf
            }.Where(s => !string.IsNullOrEmpty(s))).ToLowerInvariant();

            RefreshAll();
        }

        public static string BrushKey(SetStatus s)
        {
            switch (s)
            {
                case SetStatus.Placed: return "OkBrush";
                case SetStatus.Ready:
                case SetStatus.Preview: return "InfoBrush";
                case SetStatus.Modified:
                case SetStatus.SourceChanged: return "WarningBrush";
                case SetStatus.MissingComponent:
                case SetStatus.Orphaned:
                case SetStatus.Error: return "ErrorBrush";
                default: return "IconForegroundBrush";
            }
        }
    }
}
