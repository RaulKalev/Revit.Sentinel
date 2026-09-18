using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Sentinel.Core.Library;
using Sentinel.Core.Models;
using Sentinel.Core.Persistence;
using Sentinel.Infrastructure;
using Sentinel.Revit.Collectors;
using Sentinel.Revit.Placement;
using Sentinel.Revit.Preview;
using Sentinel.Revit.Storage;
using Sentinel.Revit.Sync;
using Sentinel.UI.Services;

namespace Sentinel.Revit
{
    /// <summary>
    /// Revit implementation of <see cref="ISentinelHost"/>. Every call is queued onto one ExternalEvent and runs in
    /// a valid API context; results are marshalled back to the WPF dispatcher. Bound to the document that was
    /// active when Sentinel was opened.
    /// </summary>
    internal sealed class RevitSentinelHost : ISentinelHost
    {
        private const string TransactionPrefix = "Sentinel";

        private readonly RevitRequestQueue _queue = new RevitRequestQueue();
        private readonly UIApplication _uiApp;
        private readonly Document _doc;
        private readonly string _pluginVersion;
        private Dispatcher _dispatcher;
        private PreviewGraphicsServer _preview;
        private ElementId _storageId;
        private bool _subscribed;

        public RevitSentinelHost(UIApplication uiApp, Document doc)
        {
            _uiApp = uiApp;
            _doc = doc;
            _pluginVersion = typeof(RevitSentinelHost).Assembly.GetName().Version?.ToString();
            _queue.Initialize(); // valid API context: called from IExternalCommand.Execute
            Subscribe();
        }

        /// <summary>The WPF dispatcher callbacks are marshalled to (set by the window).</summary>
        public void AttachDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

        public Document Document => _doc;
        public string DocumentTitle => _doc.Title;
        public SentinelProject Project { get; private set; } = new SentinelProject();
        public bool IsReadOnly { get; private set; }
        public IList<string> LoadWarnings { get; private set; } = new List<string>();
        public bool IsNewProject { get; private set; }

        public event EventHandler ProjectReloaded;
        public event EventHandler<TrackedElementsChangedEventArgs> TrackedElementsChanged;
        public event EventHandler DocumentClosing;

        // ------------------------------------------------------------------ plumbing

        private void Post(Action a)
        {
            if (a == null) return;
            if (_dispatcher != null) _dispatcher.BeginInvoke(a);
            else a();
        }

        private void Run<T>(string name, Func<UIApplication, T> work, Action<T> done, Func<Exception, T> onError)
        {
            _queue.Enqueue(name, app =>
            {
                T result;
                try
                {
                    result = work(app);
                }
                catch (Exception ex)
                {
                    SentinelLog.Error(name + " failed", ex);
                    result = onError(ex);
                }
                if (done != null) Post(() => done(result));
            });
        }

        private UIDocument ActiveUiDoc(UIApplication app)
        {
            var uidoc = app.ActiveUIDocument;
            return uidoc != null && uidoc.Document.Equals(_doc) ? uidoc : null;
        }

        // ------------------------------------------------------------------ load / save

        public void Load(Action<OperationResult> done)
        {
            Run("Load", app => LoadNow(), done, ex => OperationResult.Fail("Loading Sentinel data failed: " + ex.Message));
        }

        internal OperationResult LoadNow()
        {
            var payload = SentinelProjectStorage.Read(_doc);
            _storageId = SentinelProjectStorage.Find(_doc)?.Id;

            if (payload == null)
            {
                Project = DefaultLibrary.CreateProject();
                IsNewProject = true;
                IsReadOnly = false;
                LoadWarnings = new List<string>();

                // Tagged elements but no project data (data lost): adopt their project id so they can be recovered.
                var tagProject = ElementTagStorage.ReadAll(_doc).Select(kv => kv.Value.ProjectId)
                    .Where(id => !string.IsNullOrEmpty(id)).GroupBy(id => id).OrderByDescending(g => g.Count()).FirstOrDefault();
                if (tagProject != null)
                {
                    Project.ProjectId = tagProject.Key;
                    LoadWarnings.Add("No Sentinel project data found, but placed Sentinel elements exist. Use Refresh to recover their sets.");
                }
                SentinelLog.Info("No Sentinel data in '" + _doc.Title + "'; starter library created in memory.");
                return OperationResult.Ok("New Sentinel project (starter library, not saved yet).");
            }

            var loaded = SentinelProjectSerializer.FromPayload(payload);
            Project = loaded.Project;
            IsReadOnly = loaded.IsNewerThanSupported;
            IsNewProject = false;
            LoadWarnings = loaded.Warnings;
            foreach (var w in loaded.Warnings) SentinelLog.Warn("Load: " + w);
            SentinelLog.Info("Loaded Sentinel data v" + loaded.StoredDataVersion + " from '" + _doc.Title + "': " +
                             Project.DoorSetInstances.Count + " set instance(s).");
            return OperationResult.Ok();
        }

        public void SaveProject(string reason, Action<OperationResult> done)
        {
            Run("Save", app => SaveNow(reason), done, ex => OperationResult.Fail("Saving failed: " + ex.Message));
        }

        /// <summary>Writes the project in its own transaction. Must run in API context, outside other transactions.</summary>
        internal OperationResult SaveNow(string reason)
        {
            if (IsReadOnly) return OperationResult.Fail("Project data is read-only (saved by a newer Sentinel).");
            var payload = SentinelProjectSerializer.ToPayload(Project, Environment.UserName, _pluginVersion);
            var pre = new CollectingFailuresPreprocessor();
            using (var t = new Transaction(_doc, TransactionPrefix + " – save" + (string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")")))
            {
                CollectingFailuresPreprocessor.Attach(t, pre);
                t.Start();
                _storageId = SentinelProjectStorage.Write(_doc, payload);
                var status = t.Commit();
                if (status != TransactionStatus.Committed)
                {
                    var why = pre.Errors.Count > 0 ? string.Join(" ", pre.Errors) : status.ToString();
                    if (_doc.IsWorkshared) why += " (workshared: the Sentinel data element may be owned by another user – synchronize and retry)";
                    return OperationResult.Fail("Saving Sentinel data failed: " + why);
                }
            }
            IsNewProject = false;
            return OperationResult.Ok("Saved.");
        }

        // ------------------------------------------------------------------ discovery / refresh

        public void GetLinks(Action<IList<LinkInfo>> done)
        {
            Run<IList<LinkInfo>>("GetLinks", app => DoorDiscoveryService.GetLinks(_doc), done, ex => new List<LinkInfo>());
        }

        public void GetLinkLevels(string linkUniqueId, Action<IList<string>> done)
        {
            Run<IList<string>>("GetLinkLevels", app => DoorDiscoveryService.GetLevels(_doc, linkUniqueId), done, ex => new List<string>());
        }

        public void DiscoverDoors(DiscoveryRequest request, Action<DiscoveryResult> done)
        {
            Run("DiscoverDoors", app =>
            {
                var uidoc = ActiveUiDoc(app);
                return DoorDiscoveryService.Discover(_doc, uidoc?.ActiveView, request, Project.Settings);
            }, done, ex => new DiscoveryResult { Error = "Door discovery failed: " + ex.Message });
        }

        public void Refresh(Action<RefreshResult> done)
        {
            Run("Refresh", app =>
            {
                bool changed;
                var r = ProjectSynchronizer.Refresh(_doc, Project, out changed);
                if (changed && !IsReadOnly)
                {
                    var save = SaveNow("update relationships");
                    if (!save.Success) r.Messages.Add(save.Message);
                }
                return r;
            }, done, ex => new RefreshResult { Error = "Refresh failed: " + ex.Message });
        }

        // ------------------------------------------------------------------ preview

        public void ShowPreview(PreviewScene scene, bool zoom, Action<OperationResult> done)
        {
            Run("ShowPreview", app =>
            {
                if (_preview == null) _preview = PreviewGraphicsServer.Register();
                _preview.SetScene(_doc, scene);

                var uidoc = ActiveUiDoc(app);
                if (uidoc == null) return OperationResult.Fail("Activate the project \"" + _doc.Title + "\" to see the preview.");

                if (zoom)
                {
                    var current = scene?.Doors.FirstOrDefault(d => d.IsCurrent) ?? scene?.Doors.FirstOrDefault();
                    if (current?.Door != null) FocusOn(uidoc, NavigationService.DoorBox(current.Door, current.Placements));
                }
                uidoc.RefreshActiveView();
                return OperationResult.Ok();
            }, done, ex => OperationResult.Fail("Preview failed: " + ex.Message));
        }

        public void ClearPreview()
        {
            _queue.Enqueue("ClearPreview", app =>
            {
                try
                {
                    if (_preview == null) return;
                    _preview.Clear();
                    ActiveUiDoc(app)?.RefreshActiveView();
                }
                catch (Exception ex) { SentinelLog.Error("ClearPreview failed", ex); }
            });
        }

        // ------------------------------------------------------------------ placement

        public void Place(PlacementRequest request, Action<PlacementBatchResult> done)
        {
            Run("Place", app => PlaceNow(request), done, ex => new PlacementBatchResult { FatalError = ex.Message });
        }

        internal PlacementBatchResult PlaceNow(PlacementRequest request)
        {
            var batch = new PlacementBatchResult();
            if (IsReadOnly)
            {
                batch.FatalError = "Project data is read-only (saved by a newer Sentinel).";
                return batch;
            }

            var instances = request.InstanceIds.Select(Project.FindInstance).Where(i => i != null).ToList();
            if (instances.Count == 0)
            {
                batch.FatalError = "No door sets selected.";
                return batch;
            }

            // Helper view for wall-face raycasting (own transaction, before the group).
            View3D raycastView = null;
            try { raycastView = HelperViews.Ensure(_doc, HelperViews.RaycastViewName, false); }
            catch (Exception ex) { SentinelLog.Error("Raycast view unavailable", ex); }

            var sources = DoorDiscoveryService.VerifySources(_doc, instances, Project.Settings);
            var ctx = new PlacementContext
            {
                Doc = _doc,
                Project = Project,
                Symbols = new FamilySymbolResolver(_doc),
                RaycastView = raycastView,
                User = Environment.UserName
            };

            using (var group = new TransactionGroup(_doc, TransactionPrefix + " – " +
                (request.Mode == PlacementMode.Update ? "update door sets" : "place door sets")))
            {
                group.Start();
                foreach (var inst in instances)
                {
                    SourceCheck sc;
                    sources.TryGetValue(inst.Id, out sc);
                    PlacementDoorResult r;
                    try
                    {
                        r = DoorSetPlacementExecutor.PlaceInstance(ctx, inst, sc, request.Mode, request.OverwriteManual);
                    }
                    catch (Exception ex)
                    {
                        SentinelLog.Error("Placement crashed for " + inst.Id, ex);
                        inst.LastError = "Component creation failed: " + ex.Message;
                        r = new PlacementDoorResult
                        {
                            InstanceId = inst.Id,
                            DoorName = inst.Source?.DisplayName,
                            Outcome = PlacementOutcome.Failed,
                            Messages = { inst.LastError }
                        };
                    }
                    batch.Doors.Add(r);
                    SentinelLog.Info("Place " + r.DoorName + ": " + r.Outcome + " – " + string.Join(" | ", r.Messages));
                }

                var save = SaveNow(request.Mode == PlacementMode.Update ? "update" : "placement");
                if (!save.Success)
                {
                    group.RollBack();
                    batch.FatalError = save.Message + " All placements of this run were rolled back.";
                    LoadNow(); // re-sync memory with the (unchanged) stored data
                    Post(() => ProjectReloaded?.Invoke(this, EventArgs.Empty));
                    return batch;
                }
                group.Assimilate();
            }
            return batch;
        }

        public void DeletePlacedComponents(IList<string> instanceIds, bool removeRecords, Action<OperationResult> done)
        {
            Run("DeletePlacedComponents", app =>
            {
                if (IsReadOnly) return OperationResult.Fail("Project data is read-only.");
                var instances = instanceIds.Select(Project.FindInstance).Where(i => i != null).ToList();
                var deleted = 0;
                using (var group = new TransactionGroup(_doc, TransactionPrefix + " – delete components"))
                {
                    group.Start();
                    using (var t = new Transaction(_doc, TransactionPrefix + " – delete components"))
                    {
                        var pre = new CollectingFailuresPreprocessor();
                        CollectingFailuresPreprocessor.Attach(t, pre);
                        t.Start();
                        foreach (var inst in instances)
                            foreach (var c in inst.Components)
                            {
                                var el = string.IsNullOrEmpty(c.ElementUniqueId) ? null : _doc.GetElement(c.ElementUniqueId);
                                if (el != null) { _doc.Delete(el.Id); deleted++; }
                            }
                        if (t.Commit() != TransactionStatus.Committed)
                        {
                            group.RollBack();
                            return OperationResult.Fail("Deleting components failed: " + string.Join(" ", pre.Errors));
                        }
                    }
                    foreach (var inst in instances)
                    {
                        if (removeRecords)
                        {
                            inst.Components.Clear();
                            inst.PlacedConfigurationHash = null;
                        }
                        else
                        {
                            foreach (var c in inst.Components)
                            {
                                c.ElementUniqueId = null;
                                c.ElementId = 0;
                                c.State = ComponentState.Planned;
                            }
                        }
                        inst.Touch();
                    }
                    var save = SaveNow("delete components");
                    if (!save.Success)
                    {
                        group.RollBack();
                        LoadNow();
                        Post(() => ProjectReloaded?.Invoke(this, EventArgs.Empty));
                        return save;
                    }
                    group.Assimilate();
                }
                return OperationResult.Ok(deleted + " element(s) deleted.");
            }, done, ex => OperationResult.Fail("Deleting components failed: " + ex.Message));
        }

        // ------------------------------------------------------------------ navigation

        public void Navigate(NavigationRequest request, Action<OperationResult> done)
        {
            Run("Navigate", app =>
            {
                var uidoc = ActiveUiDoc(app);
                if (uidoc == null) return OperationResult.Fail("Activate the project \"" + _doc.Title + "\" first.");
                return NavigationService.Navigate(uidoc, request, box => FocusOn(uidoc, box));
            }, done, ex => OperationResult.Fail("Navigation failed: " + ex.Message));
        }

        /// <summary>Zooms to a box: in the active 3D view (if allowed by settings) or in the Sentinel Focus view.</summary>
        private void FocusOn(UIDocument uidoc, BoundingBoxXYZ box)
        {
            if (box == null) return;
            var padded = Geometry.GeometryUtils.Inflate(box, RevitUnits.MmToFt(1500));

            View3D view = null;
            if (Project.Settings.PreviewInActiveView) view = uidoc.ActiveView as View3D;
            if (view == null || view.IsTemplate)
            {
                view = HelperViews.Ensure(_doc, HelperViews.FocusViewName, true);
                using (var t = new Transaction(_doc, TransactionPrefix + " – focus"))
                {
                    t.Start();
                    view.IsSectionBoxActive = true;
                    view.SetSectionBox(padded);
                    t.Commit();
                }
            }
            if (uidoc.ActiveView.Id != view.Id) uidoc.ActiveView = view;
            var uiView = uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id);
            uiView?.ZoomAndCenterRectangle(padded.Min, padded.Max);
        }

        // ------------------------------------------------------------------ families

        public void GetFamilyTypes(Action<IList<FamilyTypeInfo>> done)
        {
            Run<IList<FamilyTypeInfo>>("GetFamilyTypes", app =>
                new FilteredElementCollector(_doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .Where(s => s.Family != null && s.Category != null && s.Category.CategoryType == CategoryType.Model)
                    .Select(s =>
                    {
                        var pt = s.Family.FamilyPlacementType;
                        return new FamilyTypeInfo
                        {
                            FamilyName = s.Family.Name,
                            TypeName = s.Name,
                            CategoryName = s.Category.Name,
                            PlacementType = pt.ToString(),
                            IsSupported = pt == FamilyPlacementType.OneLevelBased || pt == FamilyPlacementType.WorkPlaneBased
                        };
                    })
                    .OrderBy(f => f.CategoryName).ThenBy(f => f.FamilyName).ThenBy(f => f.TypeName)
                    .ToList(),
                done, ex => new List<FamilyTypeInfo>());
        }

        // ------------------------------------------------------------------ Revit events

        private void Subscribe()
        {
            if (_subscribed) return;
            _uiApp.Application.DocumentChanged += OnDocumentChanged;
            _uiApp.Application.DocumentClosing += OnDocumentClosing;
            _subscribed = true;
        }

        public void Shutdown()
        {
            try
            {
                if (_subscribed)
                {
                    _uiApp.Application.DocumentChanged -= OnDocumentChanged;
                    _uiApp.Application.DocumentClosing -= OnDocumentClosing;
                    _subscribed = false;
                }
            }
            catch (Exception ex) { SentinelLog.Error("Unsubscribe failed", ex); }

            var preview = _preview;
            _preview = null;
            if (preview != null)
            {
                _queue.Enqueue("UnregisterPreview", app =>
                {
                    preview.Clear();
                    preview.Unregister();
                    try { app.ActiveUIDocument?.RefreshActiveView(); } catch { }
                });
            }
            // The ExternalEvent is not disposed from inside its own handler; it is released with the host.
        }

        private void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            try
            {
                if (e.Document != null && e.Document.Equals(_doc))
                    Post(() => DocumentClosing?.Invoke(this, EventArgs.Empty));
            }
            catch { }
        }

        private void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            try
            {
                if (!e.GetDocument().Equals(_doc)) return;

                var names = e.GetTransactionNames();
                var isUndoRedo = e.Operation == UndoOperation.TransactionUndone || e.Operation == UndoOperation.TransactionRedone;
                var ownTransaction = !isUndoRedo && names != null && names.Any(n => n != null && n.StartsWith(TransactionPrefix, StringComparison.Ordinal));
                if (ownTransaction) return;

                var modified = e.GetModifiedElementIds();
                var deleted = e.GetDeletedElementIds();
                var added = e.GetAddedElementIds();

                // Undo/redo touched our data storage → reload the project from the model.
                var storageTouched = _storageId != null &&
                    (modified.Contains(_storageId) || deleted.Contains(_storageId) || added.Contains(_storageId));
                if (isUndoRedo && (storageTouched || (_storageId == null && added.Count > 0)))
                {
                    // The model is consistent again; re-read on the next idle (cannot read storage mid-event safely).
                    _queue.Enqueue("ReloadAfterUndo", app =>
                    {
                        LoadNow();
                        Post(() => ProjectReloaded?.Invoke(this, EventArgs.Empty));
                    });
                    return;
                }

                if (deleted.Count == 0 && modified.Count == 0) return;
                var tracked = new HashSet<long>(Project.DoorSetInstances.SelectMany(i => i.Components).Where(c => c.ElementId > 0).Select(c => c.ElementId));
                if (tracked.Count == 0) return;

                var args = new TrackedElementsChangedEventArgs
                {
                    DeletedElementIds = deleted.Select(RevitCompat.IdValue).Where(tracked.Contains).ToList(),
                    ModifiedElementIds = modified.Select(RevitCompat.IdValue).Where(tracked.Contains).ToList()
                };
                if (args.DeletedElementIds.Count > 0 || args.ModifiedElementIds.Count > 0)
                    Post(() => TrackedElementsChanged?.Invoke(this, args));
            }
            catch (Exception ex)
            {
                SentinelLog.Error("DocumentChanged handling failed", ex);
            }
        }
    }
}
