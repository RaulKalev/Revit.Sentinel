using System;
using System.Linq;
using System.Windows.Threading;
using Sentinel.UI.Mvvm;
using Sentinel.UI.Services;

namespace Sentinel.UI.ViewModels
{
    public enum SentinelPage
    {
        Doors,
        DoorSets,
        Rules,
        Components,
        Settings
    }

    /// <summary>
    /// Root view model: navigation, status bar, saving. Edits are applied to the in-memory project immediately and
    /// saved to the Revit project shortly afterwards (debounced) through the host, so essential state never lives
    /// only in the WPF session.
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        private readonly DispatcherTimer _saveTimer;
        private SentinelPage _page = SentinelPage.Doors;
        private string _statusMessage = "";
        private bool _statusIsError;
        private bool _isBusy;
        private string _busyText;
        private string _saveState = "";
        private bool _dirty;
        private bool _saving;
        private string _pendingReason;
        private string _banner;

        public MainViewModel(ISentinelHost host, IDialogService dialogs)
        {
            Host = host;
            Dialogs = dialogs;
            Session = new SentinelSession(host);

            Doors = new DoorsViewModel(this);
            DoorSets = new DoorSetsViewModel(this);
            Components = new ComponentsViewModel(this);
            Rules = new RulesViewModel(this);
            Settings = new SettingsViewModel(this);

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _saveTimer.Tick += (s, e) =>
            {
                _saveTimer.Stop();
                SaveNow();
            };

            Host.ProjectReloaded += (s, e) => OnProjectReloaded();
            Host.TrackedElementsChanged += (s, e) => Doors.OnTrackedElementsChanged(e);

            NavigateCommand = new RelayCommand(p =>
            {
                SentinelPage page;
                if (p is SentinelPage) CurrentPage = (SentinelPage)p;
                else if (p != null && Enum.TryParse(p.ToString(), out page)) CurrentPage = page;
            });
            SaveCommand = new RelayCommand(() => { _saveTimer.Stop(); SaveNow(); }, () => !IsReadOnly && !_saving);
        }

        public ISentinelHost Host { get; }
        public IDialogService Dialogs { get; }
        public SentinelSession Session { get; }

        public DoorsViewModel Doors { get; }
        public DoorSetsViewModel DoorSets { get; }
        public ComponentsViewModel Components { get; }
        public RulesViewModel Rules { get; }
        public SettingsViewModel Settings { get; }

        public RelayCommand NavigateCommand { get; }
        public RelayCommand SaveCommand { get; }

        public string DocumentTitle => Host.DocumentTitle;
        public bool IsReadOnly => Host.IsReadOnly;
        public bool IsEditable => !Host.IsReadOnly;

        public SentinelPage CurrentPage
        {
            get => _page;
            set
            {
                if (!Set(ref _page, value)) return;
                OnPropertiesChanged(nameof(IsDoorsPage), nameof(IsDoorSetsPage), nameof(IsRulesPage), nameof(IsComponentsPage), nameof(IsSettingsPage));
                switch (value)
                {
                    case SentinelPage.Doors: Doors.OnActivated(); break;
                    case SentinelPage.DoorSets: DoorSets.OnActivated(); break;
                    case SentinelPage.Components: Components.OnActivated(); break;
                    case SentinelPage.Rules: Rules.OnActivated(); break;
                    case SentinelPage.Settings: Settings.OnActivated(); break;
                }
            }
        }

        public bool IsDoorsPage { get => _page == SentinelPage.Doors; set { if (value) CurrentPage = SentinelPage.Doors; } }
        public bool IsDoorSetsPage { get => _page == SentinelPage.DoorSets; set { if (value) CurrentPage = SentinelPage.DoorSets; } }
        public bool IsRulesPage { get => _page == SentinelPage.Rules; set { if (value) CurrentPage = SentinelPage.Rules; } }
        public bool IsComponentsPage { get => _page == SentinelPage.Components; set { if (value) CurrentPage = SentinelPage.Components; } }
        public bool IsSettingsPage { get => _page == SentinelPage.Settings; set { if (value) CurrentPage = SentinelPage.Settings; } }

        public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
        public bool StatusIsError { get => _statusIsError; private set => Set(ref _statusIsError, value); }
        public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
        public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }
        public string SaveState { get => _saveState; private set => Set(ref _saveState, value); }

        /// <summary>Read-only / load warnings banner (null = hidden).</summary>
        public string Banner { get => _banner; private set { Set(ref _banner, value); OnPropertyChanged(nameof(HasBanner)); } }
        public bool HasBanner => !string.IsNullOrEmpty(_banner);

        // ------------------------------------------------------------------ lifecycle

        public void Initialize()
        {
            BeginBusy("Loading Sentinel data…");
            Host.Load(r =>
            {
                EndBusy();
                OnProjectLoaded();
                if (!r.Success) SetStatus(r.Message, true);
                else if (!string.IsNullOrEmpty(r.Message)) SetStatus(r.Message);
                Doors.StartUp();
            });
        }

        private void OnProjectLoaded()
        {
            var warnings = Host.LoadWarnings?.ToList() ?? new System.Collections.Generic.List<string>();
            Banner = Host.IsReadOnly
                ? "Read-only: " + string.Join(" ", warnings)
                : warnings.Count > 0 ? string.Join(" ", warnings) : null;
            SaveState = Host.IsReadOnly ? "Read-only" : Host.IsNewProject ? "Not saved yet" : "All changes saved";
            _dirty = false;
            OnPropertiesChanged(nameof(IsReadOnly), nameof(IsEditable));
            Doors.OnProjectLoaded();
            DoorSets.OnProjectLoaded();
            Components.OnProjectLoaded();
            Rules.OnProjectLoaded();
            Settings.OnProjectLoaded();
        }

        private void OnProjectReloaded()
        {
            _saveTimer.Stop();
            OnProjectLoaded();
            Doors.RebuildRows();
            SetStatus("Sentinel data reloaded from the model (Undo/Redo).");
        }

        // ------------------------------------------------------------------ saving

        /// <summary>Records an edit; the project is saved shortly afterwards.</summary>
        public void MarkDirty(string reason)
        {
            if (Host.IsReadOnly) return;
            _dirty = true;
            _pendingReason = reason;
            SaveState = "Unsaved changes";
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public void SaveNow(Action<bool> after = null)
        {
            if (Host.IsReadOnly)
            {
                after?.Invoke(false);
                return;
            }
            _saveTimer.Stop();
            _saving = true;
            _dirty = false;
            SaveState = "Saving…";
            var reason = _pendingReason ?? "edit";
            _pendingReason = null;
            Host.SaveProject(reason, r =>
            {
                _saving = false;
                if (r.Success)
                {
                    SaveState = _dirty ? "Unsaved changes" : "All changes saved";
                }
                else
                {
                    _dirty = true;
                    SaveState = "Save failed";
                    SetStatus(r.Message, true);
                }
                RelayCommand.Requery();
                after?.Invoke(r.Success);
            });
        }

        /// <summary>Called when the window closes: saves pending edits (queued before the host shuts down).</summary>
        public void FlushPendingSave()
        {
            if (_dirty && !Host.IsReadOnly) SaveNow();
        }

        /// <summary>Operations that save by themselves (placement, deletes) supersede a pending debounced save.</summary>
        public void CancelPendingSave()
        {
            _saveTimer.Stop();
            _dirty = false;
        }

        public void OnSavedByOperation()
        {
            SaveState = "All changes saved";
        }

        // ------------------------------------------------------------------ status

        public void SetStatus(string text, bool isError = false)
        {
            StatusMessage = text ?? "";
            StatusIsError = isError;
        }

        public void BeginBusy(string text)
        {
            BusyText = text;
            IsBusy = true;
            RelayCommand.Requery();
        }

        public void EndBusy()
        {
            IsBusy = false;
            BusyText = null;
            RelayCommand.Requery();
        }
    }
}
