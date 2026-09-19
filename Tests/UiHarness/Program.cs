using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sentinel.Core.Geometry;
using Sentinel.Core.Models;
using Sentinel.Core.Persistence;
using Sentinel.Core.Rules;
using Sentinel.UI;
using Sentinel.UI.ViewModels;

namespace Sentinel.UiHarness
{
    /// <summary>
    /// Scripted end-to-end run of the Sentinel UI against <see cref="FakeSentinelHost"/>.
    /// Output: Tests/UiHarness/out/*.png screenshots, plan renders and harness-log.txt. Exit code = failed checks.
    /// </summary>
    public static class Program
    {
        private static readonly StringBuilder Log = new StringBuilder();
        private static int _failures;
        private static string _out;

        [STAThread]
        public static int Main(string[] args)
        {
            _out = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "out");
            Directory.CreateDirectory(_out);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var exit = 0;
            app.Startup += (s, e) =>
            {
                try { Run(); }
                catch (Exception ex)
                {
                    Fail("Harness crashed: " + ex);
                }
                File.WriteAllText(Path.Combine(_out, "harness-log.txt"), Log.ToString());
                exit = _failures;
                app.Shutdown(exit);
            };
            app.Run();
            return exit;
        }

        private static void Run()
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            ThemeManager.PrefsPath = Path.Combine(_out, "ui-prefs.json"); // never touch the real user preferences
            Gallery.Dir = Path.Combine(_out, "gallery");
            if (File.Exists(ThemeManager.PrefsPath)) File.Delete(ThemeManager.PrefsPath);
            var host = new FakeSentinelHost(dispatcher);
            var dialogs = new AutoDialogs(_out);
            var window = new SentinelWindow(host, dialogs) { Width = 1440, Height = 900, Left = 20, Top = 20, ShowActivated = false };
            window.Show();
            Pump(40);

            var main = window.ViewModel;
            var doors = main.Doors;
            Info("Startup: " + main.StatusMessage);
            Check(doors.Rows.Count == host.Doors.Count, "discovery found all " + host.Doors.Count + " doors (rows=" + doors.Rows.Count + ")");
            Check(doors.Rows.All(r => r.Status == SetStatus.Unassigned), "all doors start unassigned");

            // ---------------------------------------------------------------- assignment
            Select(doors, "D101", "D103", "D105", "D131");
            doors.AssignDefinition = main.Session.Project.DoorSetDefinitions.First(d => d.Code == "DS-01");
            doors.AssignCommand.Execute(null);
            Pump();
            Select(doors, "D102", "D104", "D107", "D120", "D122");
            doors.AssignDefinition = main.Session.Project.DoorSetDefinitions.First(d => d.Code == "DS-02");
            doors.AssignCommand.Execute(null);
            Pump();
            Check(Row(doors, "D101").Status == SetStatus.Ready && Row(doors, "D102").SetCode == "DS-02", "manual assignment → Ready");
            Check(Row(doors, "D122").Evaluation.Issues.Any(i => i.Code == IssueCodes.HingeAssumed), "IFC door reports assumed hinge");

            // ---------------------------------------------------------------- batch placement
            Select(doors, "D101", "D102", "D103", "D107", "D131");
            doors.PlaceCommand.Execute(null);
            Pump(30);
            Check(new[] { "D101", "D102", "D103", "D107", "D131" }.All(m => Row(doors, m).Status == SetStatus.Placed), "batch placement → Placed");
            Info("Batch result: " + main.StatusMessage);
            Check(main.StatusMessage.StartsWith("5 placed, 0 require review, 0 failed"), "batch summary text: " + main.StatusMessage);

            // ---------------------------------------------------------------- deleted reader
            var d102 = Row(doors, "D102").Instance;
            var readerUid = d102.Components.First(c => c.Label == "Reader").ElementUniqueId;
            host.DeleteElement(readerUid);
            Pump();
            Check(Row(doors, "D102").Status == SetStatus.MissingComponent, "deleting the reader in Revit → Missing Component (immediate)");
            Check(d102.Components.Any(c => c.Label == "Reader"), "set still remembers the reader");

            // ---------------------------------------------------------------- source moved / orphaned / flipped / overrides
            host.MoveDoor("D103", new Vec3(250, 0, 0));
            host.RemoveDoor("D131");
            Select(doors, "D107");
            doors.Inspector.FlipCommand.Execute(null);
            Pump();
            Check(Row(doors, "D107").Status == SetStatus.Modified, "flip after placement → Modified");

            Select(doors, "D105");
            var reader105 = doors.Inspector.Components.First(c => c.Label == "Reader");
            reader105.EditCommand.Execute(null);
            reader105.Editor.AlongWall = "350";
            reader105.ApplyCommand.Execute(null);
            Pump();
            var ov = Row(doors, "D105").Instance.Overrides.RuleOverrides.SingleOrDefault();
            Check(ov != null && ov.AlongWallOffsetMm == 350 && ov.MountingHeightMm == null, "reader offset override stored separately (only the changed value)");

            Select(doors, "D104");
            doors.Inspector.SelectedAdd = main.Session.Project.ComponentDefinitions.First(c => c.Category == ComponentCategory.Intercom);
            doors.Inspector.AddComponentCommand.Execute(null);
            Pump();
            Check(Row(doors, "D104").Status == SetStatus.Error && Row(doors, "D104").Reason.Contains("Intercom family not configured"),
                "added component without family → Error with reason (" + Row(doors, "D104").Reason + ")");

            Select(doors, "D106");
            doors.IgnoreCommand.Execute(null);
            Pump();
            Check(Row(doors, "D106").Status == SetStatus.Ignored, "ignore door");

            doors.RefreshCommand.Execute(null);
            Pump(20);
            Check(Row(doors, "D103").Status == SetStatus.SourceChanged, "moved source door → Source Changed");
            Check(Row(doors, "D131").Status == SetStatus.Orphaned, "deleted source door → Orphaned");
            Check(Row(doors, "D102").Status == SetStatus.MissingComponent, "refresh keeps Missing Component");

            Select(doors, "D102");
            Check(WorkspaceProbe.WorkspaceOffset(window) == 0, "showing another door starts the review panel at the top");
            Select(doors, "D102");
            Snap(window, "01_doors_missing_component.png");
            Gallery.Capture(window, "01_doors_missing_component", true);
            Gallery.CaptureScaled(window, "01_doors_missing_component", 1.5);

            Select(doors, "D101", "D102", "D103");
            Gallery.Capture(window, "02_doors_multiselect");
            Select(doors);
            Gallery.Capture(window, "03_doors_no_selection_disabled_actions");
            doors.SearchText = "no-such-door";
            Pump();
            Check(doors.HasNoMatches, "a search without results says so instead of showing an empty list");
            Gallery.Capture(window, "17_doors_no_matches");
            doors.SearchText = "";
            Pump();

            // Validation error in the inspector's component editor (not applied to the model)
            Select(doors, "D105");
            var reader105v = doors.Inspector.Components.First(c => c.Label == "Reader");
            reader105v.EditCommand.Execute(null);
            Pump();
            reader105v = doors.Inspector.Components.First(c => c.Label == "Reader");
            var editOffset = WorkspaceProbe.WorkspaceOffset(window);
            Check(editOffset > 0, "Adjust scrolls the component editor into view (offset " + editOffset.ToString("0") + ")");
            reader105v.Editor.AlongWall = "abc";
            Pump();
            Check(WorkspaceProbe.WorkspaceOffset(window) == editOffset, "typing an invalid value keeps the edit, its scroll position and shows the error");
            Gallery.Capture(window, "04_inspector_validation_error");
            Check(!reader105v.ApplyCommand.CanExecute(null), "invalid override value disables Apply");
            reader105v.Editor.AlongWall = "350";
            reader105v.EditCommand.Execute(null);
            Pump();

            // ---------------------------------------------------------------- preview workflow
            Select(doors, "D104", "D120", "D122");
            doors.PreviewCommand.Execute(null);
            Pump();
            Check(doors.Preview.IsActive && Row(doors, "D104").Status == SetStatus.Preview, "preview started, row shows Preview");
            var readerBefore = host.LastScene.Doors[0].Placements.First(p => p.Label == "Reader");
            PlanRenderer.Render(host.LastScene, "D104 DS-02 (+Intercom) – before flip", Path.Combine(_out, "02_plan_D104_before_flip.png"));
            Snap(window, "02_preview_D104.png");
            Gallery.Capture(window, "05_preview", true);

            doors.Preview.FlipCommand.Execute(null);
            Pump();
            var readerAfter = host.LastScene.Doors[0].Placements.First(p => p.Label == "Reader");
            Check(readerBefore.Side != readerAfter.Side, "Flip Set moves the reader to the other side in the preview (" + readerBefore.Side + " → " + readerAfter.Side + ")");
            PlanRenderer.Render(host.LastScene, "D104 DS-02 – after Flip Set", Path.Combine(_out, "03_plan_D104_after_flip.png"));

            doors.Preview.NextCommand.Execute(null);
            Pump();
            PlanRenderer.Render(host.LastScene, "D120 Server Room DS-02 (door facing -Y)", Path.Combine(_out, "04_plan_D120.png"));
            doors.Preview.ConfirmCommand.Execute(null);
            Pump(20);
            Check(Row(doors, "D120").Status == SetStatus.Placed && Row(doors, "D120").Instance.ReviewState == ReviewState.Reviewed,
                "preview Confirm places and marks Reviewed");
            Check(doors.Preview.DoorTitle == "D122", "Confirm advances to the next door");
            PlanRenderer.Render(host.LastScene, "D122 IFC door (hinge assumed)", Path.Combine(_out, "05_plan_D122_ifc.png"));
            doors.Preview.SkipCommand.Execute(null);
            doors.Preview.ExitCommand.Execute(null);
            Pump();
            Check(!doors.Preview.IsActive && host.LastScene == null, "exit preview clears the transient graphics");
            Check(Row(doors, "D122").Status == SetStatus.Ready, "skipped door stays Ready");

            // Rotated door plan
            Select(doors, "D130");
            doors.AssignDefinition = main.Session.Project.DoorSetDefinitions.First(d => d.Code == "DS-02");
            doors.AssignCommand.Execute(null);
            doors.PreviewCommand.Execute(null);
            Pump();
            PlanRenderer.Render(host.LastScene, "D130 in a 30° wall (plan rotated to door frame)", Path.Combine(_out, "06_plan_D130_rotated.png"));
            doors.Preview.ExitCommand.Execute(null);
            Pump();

            // ---------------------------------------------------------------- update placement
            Select(doors, "D102", "D107", "D103");
            doors.UpdateCommand.Execute(null);
            Pump(30);
            Check(Row(doors, "D102").Status == SetStatus.Placed, "Update placement re-creates the deleted reader");
            Check(Row(doors, "D107").Status == SetStatus.Placed, "Update placement applies the flip");
            Check(Row(doors, "D103").Status == SetStatus.Placed, "Update placement follows the moved door");

            // Manual move is kept by default
            var d101 = Row(doors, "D101").Instance;
            var lk = d101.Components.First(c => c.Label == "Reader");
            lk.State = ComponentState.ManuallyModified;
            lk.ActualPosition = lk.PlacedPosition.Value + new Vec3(0, 0, 150);
            DoorSetInstanceOperationsHelper.Flip(d101);
            doors.UpdateRows();
            Select(doors, "D101");
            doors.UpdateCommand.Execute(null);
            Pump(30);
            Check(d101.Components.First(c => c.Label == "Reader").State == ComponentState.ManuallyModified, "manually modified reader is kept during Update (not moved back)");
            Check(dialogs.Transcript.Any(t => t.Contains("manually modified component(s) are kept")), "update confirmation mentions kept manual changes");

            // ---------------------------------------------------------------- rules
            main.CurrentPage = SentinelPage.Rules;
            Pump();
            main.Rules.NewCommand.Execute(null);
            main.Rules.RuleName = "Technical rooms";
            main.Rules.TargetSet = main.Session.Project.DoorSetDefinitions.First(d => d.Code == "DS-02");
            main.Rules.Conditions[0].Field = DoorFacts.SideARoom;
            main.Rules.Conditions[0].Value = "Room";
            main.Rules.AddConditionCommand.Execute(null);
            main.Rules.Conditions[1].Field = "FireRating";
            main.Rules.Conditions[1].Operator = UiChoices.Operators.First(o => o.Value == RuleOperator.Contains);
            main.Rules.Conditions[1].Value = "EI60";
            Pump();
            Snap(window, "07_rules.png");
            Gallery.Capture(window, "06_rules", true);
            main.CurrentPage = SentinelPage.Doors;
            Pump();
            Select(doors);
            doors.SuggestCommand.Execute(null);
            Pump();
            Check(Row(doors, "D121").SetCode == "DS-02", "rule suggestion assigns DS-02 to the electrical room (after confirmation)");
            Check(Row(doors, "D108").Status == SetStatus.Unassigned, "non-matching door stays unassigned");

            // ---------------------------------------------------------------- pages
            main.CurrentPage = SentinelPage.DoorSets;
            Pump();
            main.DoorSets.Selected = main.Session.Project.DoorSetDefinitions.First(d => d.Code == "DS-02");
            Pump();
            var rowsBefore = main.DoorSets.Rows.Count;
            main.DoorSets.Name = "Secure Technical Door";
            main.DoorSets.Code = "DS-02";
            Pump();
            Check(main.DoorSets.Selected != null && main.DoorSets.Selected.Code == "DS-02" && main.DoorSets.Rows.Count == rowsBefore && rowsBefore == 4,
                "renaming a set type keeps the selection and its component rows (" + main.DoorSets.Rows.Count + ")");
            var readerRow = main.DoorSets.Rows.First(r => r.Label == "Reader");
            readerRow.Editor.Height = "1100";
            Pump();
            Check(main.Session.Project.DoorSetDefinitions.First(d => d.Code == "DS-02").Components.First(c => c.Label == "Reader").Rule.MountingHeightMm == 1100,
                "editing a template rule updates the set type");
            Check(main.Session.Evaluate(Row(doors, "D102").Instance).Status == SetStatus.Modified, "template edit marks placed sets as Modified");
            readerRow.Editor.Height = "abc";
            Pump();
            Check(main.Session.Project.DoorSetDefinitions.First(d => d.Code == "DS-02").Components.First(c => c.Label == "Reader").Rule.MountingHeightMm == 1100,
                "invalid input is not written to the model");
            Check(readerRow.IsExpanded, "a set component row with an invalid value opens so the message is visible");
            Gallery.Capture(window, "07_door_sets_validation_error");
            readerRow.Editor.Height = "";
            Pump();
            main.DoorSets.Selected = main.Session.Project.DoorSetDefinitions.First(d => d.Code == "DS-01");
            main.DoorSets.Selected = main.Session.Project.DoorSetDefinitions.First(d => d.Code == "DS-02");
            Pump();
            Check(main.DoorSets.Rows.First(r => r.Label == "Reader").IsExpanded && !main.DoorSets.Rows.First(r => r.Label != "Reader").IsExpanded,
                "expanded set component rows are remembered when the set type is reloaded");
            Snap(window, "08_door_sets.png");
            Gallery.Capture(window, "08_door_sets", true);
            main.CurrentPage = SentinelPage.Doors;
            Pump();
            doors.UpdateRows();
            Check(Row(doors, "D102").Status == SetStatus.Placed, "reverting the template edit (blank height = component default) restores Placed");
            main.CurrentPage = SentinelPage.Components;
            Pump(10);
            main.Components.Selected = main.Session.Project.ComponentDefinitions.First(c => c.Category == ComponentCategory.Intercom);
            Pump();
            Snap(window, "09_components.png");
            Gallery.Capture(window, "09_components_unmapped", true);
            main.CurrentPage = SentinelPage.Settings;
            Pump();
            main.Settings.ExportCommand.Execute(null);
            Pump();
            Check(File.Exists(Path.Combine(_out, "Sentinel.sentinel-library.json")), "library export writes a file");
            Snap(window, "10_settings.png");
            Gallery.Capture(window, "10_settings", true);

            main.CurrentPage = SentinelPage.Doors;
            Select(doors, "D104");
            window.Theme.ToggleTheme();
            Pump();
            Snap(window, "11_doors_light_theme.png");
            window.Theme.ToggleTheme();
            Pump();

            // ---------------------------------------------------------------- multi-link discovery
            Check(doors.SelectedLinks.Count == 1 && doors.SelectedLinks[0].UniqueId == FakeSentinelHost.LinkUid,
                "the single source link stored by older versions is restored");
            doors.Links.First(l => l.UniqueId == FakeSentinelHost.InteriorLinkUid).IsChecked = true;
            Pump();
            Check(doors.SourceText.StartsWith("ARH_Model.ifc + SIS_Model.ifc"), "source text names both links: " + doors.SourceText);
            doors.FindDoorsCommand.Execute(null);
            Pump(30);
            Check(doors.Rows.Count(r => r.Door != null) == host.Doors.Count + 4, "doors of both links are listed (" + main.StatusMessage + ")");
            Check(main.StatusMessage.Contains("ARH_Model.ifc: ") && main.StatusMessage.Contains("SIS_Model.ifc: 4"), "status reports doors per link");
            Check(Row(doors, "SKU13").Reason.StartsWith("Probably the same door as D103"), "a door modelled in both links is flagged: " + Row(doors, "SKU13").Reason);
            Check(Row(doors, "SKU20").Reason == "", "interior-only doors are not flagged");
            Check(main.Session.Project.Settings.GetDiscoveryLinks().SequenceEqual(new[] { FakeSentinelHost.LinkUid, FakeSentinelHost.InteriorLinkUid }),
                "both source links are saved with the project");
            doors.SkipWindowTypes = true;
            doors.FindDoorsCommand.Execute(null);
            Pump(30);
            Check(doors.Rows.All(r => r.Mark != "SW1") && main.StatusMessage.Contains("1 window type(s)"), "window types in the Doors category can be left out");
            Check(main.Session.Project.Settings.DiscoverySkipWindowTypes, "the window option is saved with the project");
            Select(doors, "SKU13");
            Check(doors.Inspector.Subtitle.Contains("SIS_Model.ifc") && !doors.Inspector.Subtitle.Contains(" : 2"),
                "the review panel names the door's link: " + doors.Inspector.Subtitle);
            Check(doors.Inspector.Issues.Any(i => i.Message.StartsWith("Probably the same door as D103 in ARH_Model.ifc")),
                "the review panel explains the possible duplicate");
            Gallery.Capture(window, "18_doors_two_links");
            Select(doors, "D104");

            // ---------------------------------------------------------------- lock built into the magnet contact
            var contactDef = main.Session.Project.ComponentDefinitions.First(c => c.Category == ComponentCategory.DoorContact);
            var lockDef = main.Session.Project.ComponentDefinitions.First(c => c.Category == ComponentCategory.ElectricLock);
            main.CurrentPage = SentinelPage.Components;
            Pump();
            main.Components.Selected = lockDef;
            main.Components.IsBuiltIn = true;
            Pump();
            Check(lockDef.IsBuiltIn && lockDef.CarrierComponentId == contactDef.Id &&
                  lockDef.CarrierParameterLeft == "Lukk vasakul" && lockDef.CarrierParameterRight == "Lukk paremal",
                "'Part of another component' starts with the door contact and the lock parameters");
            Check(main.Components.BuiltInSummary.Contains("Lukk paremal") && main.Components.FamilySectionTitle == "Own family (backup)",
                "the Components page explains the built-in lock: " + main.Components.BuiltInSummary);
            Gallery.Capture(window, "19_components_lock_built_in");
            main.CurrentPage = SentinelPage.Doors;
            Pump();
            Check(Row(doors, "D107").Status == SetStatus.Modified, "changing how the lock is modelled marks placed doors Modified");

            Select(doors, "D105");
            doors.PlaceCommand.Execute(null);
            Pump(30);
            var i105 = Row(doors, "D105").Instance;
            var lock105 = i105.Components.First(c => c.Label == "Lock");
            var contact105 = i105.Components.First(c => c.Label == "Door Contact");
            Check(lock105.IsBuiltIn && lock105.ElementUniqueId == contact105.ElementUniqueId && Row(doors, "D105").Status == SetStatus.Placed,
                "the built-in lock has no element of its own; it rides on the contact (" + Row(doors, "D105").StatusText + ")");
            Check(host.ParameterValue(contact105.ElementUniqueId, "Lukk paremal") == 1 && host.ParameterValue(contact105.ElementUniqueId, "Lukk vasakul") == 0,
                "latch on the right of the contact (seen from its front) switches on “Lukk paremal”");

            var oldContact = contact105.ElementUniqueId;
            doors.Inspector.FlipCommand.Execute(null);
            Pump();
            doors.UpdateCommand.Execute(null);
            Pump(30);
            contact105 = i105.Components.First(c => c.Label == "Door Contact");
            lock105 = i105.Components.First(c => c.Label == "Lock");
            Check(lock105.ElementUniqueId == contact105.ElementUniqueId && contact105.ElementUniqueId != oldContact &&
                  host.ParameterValue(contact105.ElementUniqueId, "Lukk vasakul") == 1 && host.ParameterValue(contact105.ElementUniqueId, "Lukk paremal") == 0,
                "after a flip the contact moves to the other side and the lock switches to “Lukk vasakul”");
            Select(doors, "D105");
            Check(doors.Inspector.Components.First(c => c.Label == "Lock").IsBuiltIn &&
                  doors.Inspector.Components.First(c => c.Label == "Lock").FamilyText.Contains("“Lukk vasakul”"),
                "the review panel shows which contact parameter carries the lock");
            Gallery.Capture(window, "20_doors_lock_built_in");

            // Back to the lock's own family: the contact stays, its lock parameter is switched off.
            main.CurrentPage = SentinelPage.Components;
            Pump();
            main.Components.IsOwnFamily = true;
            main.CurrentPage = SentinelPage.Doors;
            Pump();
            Select(doors, "D105");
            doors.UpdateCommand.Execute(null);
            Pump(30);
            lock105 = i105.Components.First(c => c.Label == "Lock");
            contact105 = i105.Components.First(c => c.Label == "Door Contact");
            Check(!lock105.IsBuiltIn && lock105.ElementUniqueId != contact105.ElementUniqueId && lock105.PlacedFamilyName == lockDef.FamilyName &&
                  !host.DeletedByPlacement.Contains(contact105.ElementUniqueId) && host.ParameterValue(contact105.ElementUniqueId, "Lukk vasakul") == 0,
                "switching back places the lock family and switches the contact's lock off without deleting the contact");
            Select(doors, "D104");

            // Door source popover (links, scope, Find doors) in both themes
            foreach (var dark in new[] { true, false })
            {
                if (window.Theme.IsDarkMode != dark) window.Theme.ToggleTheme();
                window.IsDoorSourceOpen = true;
                Pump();
                System.Threading.Thread.Sleep(250);
                Pump();
                Check(window.DoorSourcePanel.IsVisible, "door source popover opens from the title area");
                Gallery.RenderElement(window.DoorSourcePanel, Path.Combine(Gallery.Dir, "16_door_source_popover_" + (dark ? "dark" : "light") + ".png"));
                window.IsDoorSourceOpen = false;
                Pump();
            }
            if (!window.Theme.IsDarkMode) window.Theme.ToggleTheme();

            // In-window sheets (the window's own non-blocking dialog service)
            bool? sheetAnswer = null;
            window.Sheets.Confirm("Place 34 door sets without review?",
                "Components are written to the model for every selected door. 2 sets have errors and will be skipped with the reasons below.",
                "D104: Intercom family not configured (Components page).\nD131: Source door no longer available in the linked model.",
                "Place 32 sets", "Cancel", ok => sheetAnswer = ok);
            Pump();
            Check(window.Sheets.IsOpen && window.IsSheetShown, "confirmation opens as an in-window sheet (window stays modeless)");
            Gallery.Capture(window, "11_sheet_confirm", true);
            window.Sheets.Complete(false);
            Pump();
            Check(sheetAnswer == false && !window.Sheets.IsOpen, "sheet Cancel answers false and closes");

            string pathAnswer = "unset";
            window.Sheets.PromptPath("Export library", "Components, door set types and rules are written to this file.",
                @"C:\Users\me\Documents\RK Tools\Sentinel\Sentinel.sentinel-library.json", "Export", p => pathAnswer = p);
            window.Sheets.Show("Import failed", "The file is not a Sentinel library.", "Unexpected character at line 1, position 1.", true);
            Pump();
            Gallery.Capture(window, "12_sheet_path");
            window.Sheets.Complete(true);
            Pump();
            Check(pathAnswer != null && pathAnswer.EndsWith("Sentinel.sentinel-library.json") && window.Sheets.IsOpen,
                "path sheet returns the path; the queued error sheet follows");
            Gallery.Capture(window, "13_sheet_error");
            window.Sheets.Complete(true);
            Pump();

            // Accessibility fallbacks: solid materials (reduced transparency) and high contrast
            ThemeManager.ForceSolidMaterials = true;
            window.Theme.ApplyTheme();
            Pump();
            Gallery.Capture(window, "14_doors_solid_materials");
            ThemeManager.ForceSolidMaterials = null;
            ThemeManager.ForceHighContrast = true;
            window.Theme.ApplyTheme();
            Pump();
            Gallery.Render(window, 1.0, Path.Combine(Gallery.Dir, "15_doors_high_contrast.png"));
            ThemeManager.ForceHighContrast = null;
            window.Theme.ApplyTheme();
            Pump();

            // ---------------------------------------------------------------- persistence round trip of the UI-built project
            var project = main.Session.Project;
            var reloaded = SentinelProjectSerializer.FromPayload(SentinelProjectSerializer.ToPayload(project, "harness", "1.0")).Project;
            Check(reloaded.DoorSetInstances.Count == project.DoorSetInstances.Count, "save/load keeps all " + project.DoorSetInstances.Count + " set instances");
            var r105 = reloaded.DoorSetInstances.First(i => i.Source.Mark == "D105");
            Check(r105.Overrides.RuleOverrides.Single().AlongWallOffsetMm == 350, "save/load keeps per-door overrides");
            Check(reloaded.DoorSetInstances.First(i => i.Source.Mark == "D107").AccessDirection == AccessDirection.SideBToSideA, "save/load keeps access direction");
            Check(reloaded.AssignmentRules.Count == 1, "save/load keeps rules");

            main.FlushPendingSave();
            Pump(5);
            Check(host.SaveCount > 0, "edits were saved through the host (" + host.SaveCount + " saves)");

            Log.AppendLine();
            Log.AppendLine("---- host log ----");
            foreach (var l in host.Log) Log.AppendLine(l);
            Log.AppendLine();
            Log.AppendLine("---- dialog transcript ----");
            foreach (var t in dialogs.Transcript) Log.AppendLine(t);
            Log.AppendLine();
            Log.AppendLine("---- final rows ----");
            foreach (var r in doors.Rows) Log.AppendLine(r.Mark.PadRight(6) + " " + r.SetCode.PadRight(6) + " " + r.StatusText.PadRight(18) + " " + r.Reason);

            window.Close();
            Log.Insert(0, (_failures == 0 ? "ALL CHECKS PASSED" : _failures + " CHECK(S) FAILED") + Environment.NewLine + Environment.NewLine);
        }

        // ------------------------------------------------------------------ helpers

        private static DoorRowViewModel Row(DoorsViewModel doors, string mark) => doors.Rows.First(r => r.Mark == mark);

        private static void Select(DoorsViewModel doors, params string[] marks)
        {
            doors.SelectKeys(doors.Rows.Where(r => marks.Contains(r.Mark)).Select(r => r.Key));
            if (marks.Length == 0) doors.SetSelection(new List<DoorRowViewModel>());
            Pump();
        }

        private static void Pump(int rounds = 8)
        {
            for (int i = 0; i < rounds; i++)
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        }

        private static void Check(bool ok, string what)
        {
            Log.AppendLine((ok ? "PASS  " : "FAIL  ") + what);
            if (!ok) _failures++;
        }

        private static void Fail(string what)
        {
            Log.AppendLine("FAIL  " + what);
            _failures++;
        }

        private static void Info(string what) => Log.AppendLine("INFO  " + what);

        private static void Snap(Window w, string file)
        {
            w.UpdateLayout();
            Pump();
            SnapElement((FrameworkElement)w.Content, file);
        }

        private static void SnapElement(FrameworkElement el, string file)
        {
            el.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(el);
            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(el.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(el.ActualHeight * dpi.DpiScaleY),
                96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            rtb.Render(el);
            PlanRenderer.Save(rtb, Path.Combine(_out, file));
            Log.AppendLine("SNAP  " + file);
        }
    }

    internal static class DoorSetInstanceOperationsHelper
    {
        public static void Flip(DoorSetInstance i) => Sentinel.Core.Placement.DoorSetInstanceOperations.Flip(i);
    }
}
