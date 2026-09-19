using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = Autodesk.Revit.ApplicationServices.Application;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Sentinel.Core.Placement;
using Sentinel.Core.Status;
using Sentinel.Infrastructure;
using Sentinel.Revit;
using Sentinel.Revit.Collectors;
using Sentinel.Revit.Placement;
using Sentinel.Revit.Preview;
using Sentinel.Revit.Storage;
using Sentinel.Revit.Sync;
using Sentinel.UI;
using Sentinel.UI.Services;

namespace Sentinel.Diagnostics
{
    /// <summary>
    /// Opt-in end-to-end self-test that runs inside Revit. It is inert unless the environment variable
    /// SENTINEL_SELFTEST_OUTPUT points to a folder (see Tools/Run-RevitSelfTest.ps1). It never touches user documents:
    /// it builds its own architectural model (walls + doors), links it into a new host model, generates device
    /// families, then exercises discovery, placement, storage, refresh, save/reopen, recovery, preview and the window.
    /// Results go to selftest-results.txt in the output folder.
    /// </summary>
    internal static class SelfTest
    {
        public const string OutputVariable = "SENTINEL_SELFTEST_OUTPUT";
        public const string ExitVariable = "SENTINEL_SELFTEST_EXIT";

        private static readonly StringBuilder Log = new StringBuilder();
        private static int _failed, _passed;
        private static string _out;

        public static void TryAttach(UIControlledApplication app)
        {
            _out = Environment.GetEnvironmentVariable(OutputVariable);
            if (string.IsNullOrWhiteSpace(_out)) return;
            app.Idling += OnIdling;
            SentinelLog.Info("Self-test armed, output: " + _out);
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            var uiapp = sender as UIApplication;
            if (uiapp == null) return;
            uiapp.Idling -= OnIdling;

            Directory.CreateDirectory(_out);
            Say("Sentinel self-test " + typeof(SelfTest).Assembly.GetName().Version + " in " + uiapp.Application.VersionName +
                 " (" + uiapp.Application.VersionBuild + "), " + Environment.Version);
            try
            {
                Run(uiapp);
            }
            catch (Exception ex)
            {
                Fail("Self-test crashed: " + ex);
            }
            Log.Insert(0, (_failed == 0 ? "RESULT: PASS" : "RESULT: FAIL") + " (" + _passed + " passed, " + _failed + " failed)" + Environment.NewLine);
            File.WriteAllText(Path.Combine(_out, "selftest-results.txt"), Log.ToString());

            if (Environment.GetEnvironmentVariable(ExitVariable) == "1")
            {
                try { uiapp.PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit)); }
                catch (Exception ex) { SentinelLog.Error("Self-test exit failed", ex); }
            }
        }

        private static void Run(UIApplication uiapp)
        {
            var app = uiapp.Application;
            var archPath = Path.Combine(_out, "SentinelSelfTest_ARH.rvt");
            var hostPath = Path.Combine(_out, "SentinelSelfTest_Host.rvt");
            var familyTemplates = FamilyTemplateFolder(app);

            // ---------------------------------------------------------------- architectural model
            var arch = NewProject(app);
            FamilySymbol doorSymbol;
            var doorIds = BuildArchitecture(app, arch, familyTemplates, out doorSymbol);
            Check(doorIds.Count == 4, "architectural test model has 4 doors (" + doorIds.Count + ")");
            SaveAs(arch, archPath);
            arch.Close(false);

            // ---------------------------------------------------------------- host model + link + families
            var host = NewProject(app);
            ElementId linkId;
            using (var t = new Transaction(host, "Sentinel self-test – link"))
            {
                t.Start();
                var lt = RevitLinkType.Create(host, ModelPathUtils.ConvertUserVisiblePathToModelPath(archPath), new RevitLinkOptions(false));
                var li = RevitLinkInstance.Create(host, lt.ElementId);
                ElementTransformUtils.MoveElement(host, li.Id, new XYZ(Mm(1000), Mm(2000), 0));
                linkId = li.Id;
                t.Commit();
            }
            var deviceFamily = CreateBoxFamily(app, Path.Combine(familyTemplates, "Metric Generic Model face based.rft"), Path.Combine(_out, "SentinelTestDevice.rfa"), 100, 40);
            var lockFamily = CreateBoxFamily(app, Path.Combine(familyTemplates, "Metric Generic Model.rft"), Path.Combine(_out, "SentinelTestLock.rfa"), 60, 200);
            string deviceType, lockType;
            using (var t = new Transaction(host, "Sentinel self-test – load families"))
            {
                t.Start();
                deviceType = LoadFamily(host, deviceFamily);
                lockType = LoadFamily(host, lockFamily);
                t.Commit();
            }
            Check(deviceType != null && lockType != null, "device families generated and loaded");

            // ---------------------------------------------------------------- Sentinel host
            var sentinel = new RevitSentinelHost(uiapp, host);
            var load = sentinel.LoadNow();
            Check(load.Success && sentinel.IsNewProject, "new project loads the starter library");
            var project = sentinel.Project;
            foreach (var c in project.ComponentDefinitions)
            {
                if (c.Category == ComponentCategory.ElectricLock) { c.FamilyName = "SentinelTestLock"; c.TypeName = lockType; }
                else if (c.Category != ComponentCategory.Intercom) { c.FamilyName = "SentinelTestDevice"; c.TypeName = deviceType; }
            }

            var links = DoorDiscoveryService.GetLinks(host);
            Check(links.Count == 1 && links[0].IsLoaded, "link listed and loaded");
            var discovery = DoorDiscoveryService.Discover(host, null, new DiscoveryRequest { LinkUniqueIds = { links[0].UniqueId }, Scope = DiscoveryScope.AllDoors }, project.Settings);
            Check(discovery.Success && discovery.Doors.Count == 4, "discovery finds the 4 linked doors (" + discovery.Doors.Count + ")");
            foreach (var d in discovery.Doors.OrderBy(d => d.Current.Mark))
            {
                var g = d.Geometry;
                Say("  door " + d.Current.Mark + ": origin " + g?.Origin + " facing " + g?.Facing + " w=" + g?.WidthMm.ToString("0") +
                     " h=" + g?.HeightMm.ToString("0") + " wall=" + g?.WallThicknessMm.ToString("0") + " level=" + d.Current.LevelName +
                     (d.ReadWarnings.Count > 0 ? " warnings: " + string.Join(" ", d.ReadWarnings) : ""));
            }
            Check(discovery.Doors.All(d => d.Geometry != null && d.Geometry.IsValid), "all door frames valid");
            Check(discovery.Doors.All(d => d.Geometry.WallThicknessMm > 50), "wall thickness measured from host walls");
            var t101 = discovery.Doors.First(d => d.Current.Mark == "T101");
            var expectedOrigin = new Vec3(2000 + 1000, 0 + 2000, 0);
            Check(Math.Abs(t101.Geometry.Origin.X - expectedOrigin.X) < 5 && Math.Abs(t101.Geometry.Origin.Y - expectedOrigin.Y) < 5,
                "door origin transformed with the link offset (" + t101.Geometry.Origin + " vs " + expectedOrigin + ")");

            // ---------------------------------------------------------------- assign + place
            var ds01 = project.DoorSetDefinitions.First(d => d.Code == "DS-01");
            var ds02 = project.DoorSetDefinitions.First(d => d.Code == "DS-02");
            var inst = new Dictionary<string, DoorSetInstance>();
            foreach (var d in discovery.Doors)
            {
                var def = d.Current.Mark == "T103" ? ds01 : ds02;
                var i = DoorSetInstanceOperations.Create(d, def);
                project.DoorSetInstances.Add(i);
                inst[d.Current.Mark] = i;
            }
            DoorSetInstanceOperations.AddComponent(inst["T104"], project.ComponentDefinitions.First(c => c.Category == ComponentCategory.Intercom));
            Check(sentinel.SaveNow("self-test").Success && SentinelProjectStorage.Read(host) != null, "project data saved to Extensible Storage");

            var batch = sentinel.PlaceNow(new PlacementRequest { InstanceIds = inst.Values.Select(i => i.Id).ToList(), Mode = PlacementMode.Batch });
            Say("  batch: " + batch.Summary);
            foreach (var r in batch.Doors) Say("    " + r.DoorName + ": " + r.Outcome + " – " + string.Join(" | ", r.Messages));
            Check(batch.FatalError == null, "batch placement completed");
            Check(batch.Doors.Count(r => r.Outcome == PlacementOutcome.Failed) == 0, "no door failed completely");
            Check(batch.Doors.First(r => r.InstanceId == inst["T104"].Id).Messages.Any(m => m.Contains("Intercom family not configured")),
                "unconfigured Intercom reported with a reason, other components placed");
            var placed = inst.Values.SelectMany(i => i.Components).Where(c => !string.IsNullOrEmpty(c.ElementUniqueId)).ToList();
            Check(placed.Count >= 15, "components created (" + placed.Count + ")");
            Check(placed.All(c => host.GetElement(c.ElementUniqueId) != null), "every record points to an existing element");
            Check(ElementTagStorage.ReadAll(host).Count == placed.Count, "every placed element carries a Sentinel tag");
            var hosted = placed.Count(c => c.PlacedHosting == "LinkedWallFace");
            Say("  hosting: " + string.Join(", ", placed.GroupBy(c => c.PlacedHosting).Select(g => g.Key + "=" + g.Count())));
            Check(hosted > 0, "face-based devices hosted on linked wall faces (" + hosted + ")");

            // Reader position relative to the door (latch side, side A at 1000 mm)
            var readerT101 = inst["T101"].Components.First(c => c.Label == "Reader");
            var g101 = t101.Geometry;
            var rel = readerT101.PlacedPosition.Value - g101.Origin;
            Say("  T101 reader local: along=" + rel.Dot(g101.WidthAxis).ToString("0") + " normal=" + rel.Dot(g101.Facing).ToString("0") + " z=" + rel.Z.ToString("0"));
            Check(rel.Dot(g101.Facing) > 0 && Math.Abs(rel.Z - 1000) < 5, "T101 reader is on side A at 1000 mm");

            // ---------------------------------------------------------------- missing + manual + update
            using (var t = new Transaction(host, "Sentinel self-test – user edits"))
            {
                t.Start();
                host.Delete(host.GetElement(readerT101.ElementUniqueId).Id);
                var lockT102 = inst["T102"].Components.First(c => c.Label == "Lock");
                ElementTransformUtils.MoveElement(host, host.GetElement(lockT102.ElementUniqueId).Id, new XYZ(0, 0, Mm(300)));
                t.Commit();
            }
            bool changed;
            var refresh = ProjectSynchronizer.Refresh(host, project, out changed);
            Check(refresh.MissingComponents == 1 && Status(sentinel, inst["T101"]) == SetStatus.MissingComponent, "deleted reader detected (Missing Component)");
            Check(refresh.ModifiedComponents == 1 && Status(sentinel, inst["T102"]) == SetStatus.Modified, "moved lock detected (Manually Modified)");

            DoorSetInstanceOperations.Flip(inst["T102"]);
            var update = sentinel.PlaceNow(new PlacementRequest { InstanceIds = new List<string> { inst["T101"].Id, inst["T102"].Id }, Mode = PlacementMode.Update });
            Say("  update: " + update.Summary);
            ProjectSynchronizer.Refresh(host, project, out changed);
            Check(Status(sentinel, inst["T101"]) == SetStatus.Placed, "Update placement re-created the reader");
            var t102 = discovery.Doors.First(d => d.Current.Mark == "T102").Geometry;
            var reader102 = inst["T102"].Components.First(c => c.Label == "Reader").PlacedPosition.Value;
            Check((reader102 - t102.Origin).Dot(t102.Facing) < 0, "flip moved the T102 reader to side B");
            Check(inst["T102"].Components.First(c => c.Label == "Lock").State == ComponentState.ManuallyModified, "manually moved lock was kept");

            // ---------------------------------------------------------------- preview graphics
            var server = PreviewGraphicsServer.Register();
            var t103 = discovery.Doors.First(d => d.Current.Mark == "T103").Geometry;
            var plan = DoorSetPlacementCalculator.Calculate(inst["T103"], ds01, t103, project);
            server.SetScene(host, new PreviewScene { Doors = { new PreviewDoor { Door = t103, Placements = plan.Placements, IsCurrent = true } } });
            var view3d = HelperViews.Ensure(host, HelperViews.FocusViewName, true);
            Check(server.HasScene && server.CanExecute(view3d) && server.GetBoundingBox(view3d) != null, "DirectContext3D preview server registered and drawable");
            server.Clear();
            server.Unregister();

            // ---------------------------------------------------------------- window inside Revit
            try
            {
                var window = new SentinelWindow(sentinel);
                sentinel.AttachDispatcher(window.Dispatcher);
                window.Show();
                window.UpdateLayout();
                var el = (FrameworkElement)window.Content;
                var rtb = new RenderTargetBitmap((int)el.ActualWidth, (int)el.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(el);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.Create(Path.Combine(_out, "selftest-window.png"))) enc.Save(fs);
                window.Close();
                Check(true, "Sentinel window (WPF, MaterialDesign, themes) opens inside Revit");
            }
            catch (Exception ex)
            {
                Fail("Sentinel window failed inside Revit: " + ex);
            }

            // ---------------------------------------------------------------- save / reopen / recover
            var placedCount = inst.Values.SelectMany(i => i.Components).Count(c => !string.IsNullOrEmpty(c.ElementUniqueId));
            sentinel.Shutdown();
            SaveAs(host, hostPath);
            host.Close(false);

            var reopened = app.OpenDocumentFile(hostPath);
            var sentinel2 = new RevitSentinelHost(uiapp, reopened);
            Check(sentinel2.LoadNow().Success && !sentinel2.IsNewProject, "project data survives save and reopen");
            Check(sentinel2.Project.DoorSetInstances.Count == 4, "4 set instances after reopen");
            Check(sentinel2.Project.DoorSetInstances.First(i => i.Source.Mark == "T102").AccessDirection == AccessDirection.SideBToSideA, "access direction persisted");
            Check(sentinel2.Project.DoorSetInstances.First(i => i.Source.Mark == "T104").Overrides.AddedComponents.Count == 1, "per-door overrides persisted");
            var r2 = ProjectSynchronizer.Refresh(reopened, sentinel2.Project, out changed);
            Check(r2.MissingComponents == 0 && r2.Sources.Values.All(s => s.State == SourceState.Ok), "relationships and sources resolve after reopen");

            using (var t = new Transaction(reopened, "Sentinel self-test – drop data"))
            {
                t.Start();
                reopened.Delete(SentinelProjectStorage.Find(reopened).Id);
                t.Commit();
            }
            sentinel2.LoadNow();
            var r3 = ProjectSynchronizer.Refresh(reopened, sentinel2.Project, out changed);
            var recoveredComponents = sentinel2.Project.DoorSetInstances.SelectMany(i => i.Components).Count();
            Check(r3.RecoveredSets == 4 && recoveredComponents == placedCount, "sets rebuilt from element tags after project data loss (" + r3.RecoveredSets + " sets, " + recoveredComponents + " components)");
            sentinel2.Shutdown();
            reopened.Close(false);
        }

        private static SetStatus Status(RevitSentinelHost host, DoorSetInstance i)
        {
            var def = host.Project.FindDoorSet(i.DefinitionId);
            return DoorSetStatusEvaluator.Evaluate(new DoorSetStatusContext
            {
                Instance = i,
                Definition = def,
                SourceState = SourceState.Ok,
                Plan = DoorSetPlacementCalculator.Calculate(i, def, i.Source.LastKnownGeometry, host.Project)
            }).Status;
        }

        // ------------------------------------------------------------------ model building

        private static Document NewProject(Application app)
        {
            var template = app.DefaultProjectTemplate;
            if (!string.IsNullOrEmpty(template) && File.Exists(template)) return app.NewProjectDocument(template);
            return app.NewProjectDocument(UnitSystem.Metric);
        }

        private static string FamilyTemplateFolder(Application app)
        {
            var p = app.FamilyTemplatePath;
            if (!string.IsNullOrEmpty(p) && File.Exists(Path.Combine(p, "Metric Door.rft"))) return p;
            var guess = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Autodesk", "RVT " + app.VersionNumber, "Family Templates", "English");
            return guess;
        }

        private static List<ElementId> BuildArchitecture(Application app, Document doc, string familyTemplates, out FamilySymbol door)
        {
            door = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_Doors)
                .Cast<FamilySymbol>().FirstOrDefault();
            if (door == null)
            {
                var fam = CreateDoorFamily(app, Path.Combine(familyTemplates, "Metric Door.rft"), Path.Combine(_out, "SentinelTestDoor.rfa"));
                using (var t = new Transaction(doc, "load door"))
                {
                    t.Start();
                    Family f;
                    doc.LoadFamily(fam, out f);
                    door = doc.GetElement(f.GetFamilySymbolIds().First()) as FamilySymbol;
                    t.Commit();
                }
            }

            var ids = new List<ElementId>();
            using (var t = new Transaction(doc, "walls and doors"))
            {
                t.Start();
                if (!door.IsActive) door.Activate();
                var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).FirstOrDefault()
                            ?? Level.Create(doc, 0);
                var w1 = Wall.Create(doc, Line.CreateBound(new XYZ(0, 0, 0), new XYZ(Mm(12000), 0, 0)), level.Id, false);
                var a = 30.0 * Math.PI / 180.0;
                var s = new XYZ(0, Mm(6000), 0);
                var w2 = Wall.Create(doc, Line.CreateBound(s, s + new XYZ(Math.Cos(a), Math.Sin(a), 0) * Mm(9000)), level.Id, false);
                doc.Regenerate();

                var marks = new[] { "T101", "T102", "T103" };
                for (int i = 0; i < 3; i++)
                {
                    var fi = doc.Create.NewFamilyInstance(new XYZ(Mm(2000 + i * 3500), 0, 0), door, w1, level, StructuralType.NonStructural);
                    fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set(marks[i]);
                    ids.Add(fi.Id);
                }
                var p4 = s + new XYZ(Math.Cos(a), Math.Sin(a), 0) * Mm(4000);
                var d4 = doc.Create.NewFamilyInstance(p4, door, w2, level, StructuralType.NonStructural);
                d4.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set("T104");
                ids.Add(d4.Id);
                t.Commit();
            }
            return ids;
        }

        private static string CreateDoorFamily(Application app, string template, string path)
        {
            var fdoc = app.NewFamilyDocument(template);
            SaveAs(fdoc, path);
            fdoc.Close(false);
            return path;
        }

        private static string CreateBoxFamily(Application app, string template, string path, double sizeMm, double depthMm)
        {
            var fdoc = app.NewFamilyDocument(template);
            using (var t = new Transaction(fdoc, "geometry"))
            {
                t.Start();
                var h = Mm(sizeMm / 2);
                var loop = new CurveArray();
                var p = new[] { new XYZ(-h, -h, 0), new XYZ(h, -h, 0), new XYZ(h, h, 0), new XYZ(-h, h, 0) };
                for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(p[i], p[(i + 1) % 4]));
                var profile = new CurveArrArray();
                profile.Append(loop);
                var sp = SketchPlane.Create(fdoc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
                fdoc.FamilyCreate.NewExtrusion(true, profile, sp, Mm(depthMm));
                t.Commit();
            }
            SaveAs(fdoc, path);
            fdoc.Close(false);
            return path;
        }

        private static string LoadFamily(Document doc, string path)
        {
            Family f;
            if (!doc.LoadFamily(path, out f) || f == null) return null;
            var sym = doc.GetElement(f.GetFamilySymbolIds().First()) as FamilySymbol;
            return sym?.Name;
        }

        private static void SaveAs(Document doc, string path)
        {
            doc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
        }

        private static double Mm(double mm) => RevitUnits.MmToFt(mm);

        // ------------------------------------------------------------------ reporting

        private static void Check(bool ok, string what)
        {
            if (ok) _passed++; else _failed++;
            Say((ok ? "PASS  " : "FAIL  ") + what);
        }

        private static void Fail(string what)
        {
            _failed++;
            Say("FAIL  " + what);
        }

        private static void Say(string s)
        {
            Log.AppendLine(s);
            SentinelLog.Info("[self-test] " + s);
        }
    }
}
