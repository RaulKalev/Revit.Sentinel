# Milestone 1 verification

State of verification at the Milestone 1 commit (2026-09-18).

## Done

| Check | How | Result |
|---|---|---|
| Build Revit 2024 target (net48) | `dotnet build Sentinel/Sentinel.csproj -c Release` | ✅ 0 warnings, 0 errors |
| Build Revit 2026 target (net8.0-windows) | same | ✅ 0 warnings, 0 errors |
| Core unit tests (placement math, overrides, flip, statuses, change detection, diff/update, rules, IFC footprint, persistence, migration safety) | `dotnet test Tests/Sentinel.Core.Tests` | ✅ 67/67 on net8.0 and 67/67 on net48 |
| UI end-to-end with a fake host (the real window + view models) | `Tests/UiHarness` | ✅ 40/40 checks – see below |
| IFC door data assumptions | read-only query of a real IFC architectural link in a running Revit 2026 session | Doors arrive as DirectShapes without family and without LevelId; the door code is the element name → handled (footprint frame, level by elevation, name fallback for the mark) |

UI harness checks (Tests/UiHarness/out/harness-log.txt): discovery; manual assignment; IFC door reports an assumed
hinge; batch placement and its "N placed, N require review, N failed" summary; deleting a placed reader → *Missing
Component* immediately while the set still lists the reader; flip after placement → *Modified*; reader offset override
stored separately (only the changed value); added component without family → *Error* with the reason "Intercom family not
configured"; ignore; moved source door → *Source Changed*; deleted source door → *Orphaned*; preview start/flip (reader
changes side in the preview scene)/confirm (placed + reviewed)/advance/skip/exit (graphics cleared); update placement
re-creates the deleted reader, applies the flip and follows the moved door; manually modified component kept during
update and mentioned in the confirmation; rule suggestion; set-type rename keeps the editor; template edit marks placed
sets *Modified*; invalid input never written; library export; save/load of the UI-built project (instances, overrides,
direction, rules); edits saved through the host.

Screenshots and plan renders of the preview geometry are written to `Tests/UiHarness/out/`.

## Not run

| Check | Why | How to run |
|---|---|---|
| Plugin startup, ribbon and modeless window **inside Revit** | Revit 2026 was in use with another project during development; a new unsigned add-in also needs a one-time *Load Once / Always Load* confirmation by the user | `./Tools/Run-RevitSelfTest.ps1 -RevitVersion 2026 -RevitExe "<path to Revit.exe>"` |
| Door discovery, single-door preview + placement, batch placement, hosting on linked wall faces, tags, missing-component detection, update, save → reopen, recovery from element tags **in Revit** | same; covered by the in-Revit self-test (`Sentinel/Diagnostics/SelfTest.cs`), which builds its own test models and never opens user projects | same script; results in `%TEMP%\SentinelSelfTest\selftest-results.txt` |
| Revit 2024 in-Revit run | this machine's Revit 2024 has no project/family templates installed, which the self-test needs to build its test models | install the Revit 2024 content (templates), then `-RevitVersion 2024` |
| DirectContext3D preview visuals in Revit views | needs Revit | open Sentinel, select a door with a set, *Preview…* |

A placement failure producing a visible reason without corrupting other sets is covered by the executor design (one
transaction per door, records updated only after commit, failures recorded per component), by the UI harness
(unconfigured Intercom), and by the in-Revit self-test (door T104) – the latter not yet executed.
