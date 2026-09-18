# Sentinel architecture

```
Sentinel.sln
├── Sentinel.Core            netstandard2.0, no Revit API – domain, placement math, rules, status, persistence
│   ├── Geometry             Vec3 (mm), DoorGeometry (door frame), FootprintAnalyzer (IFC door frames)
│   ├── Models               ComponentDefinition, PlacementRule, SecuritySetDefinition/DoorSetDefinition,
│   │                        SecuritySetInstance/DoorSetInstance, SourceDoorReference, PlacedComponentInstance,
│   │                        SetOverrides, SentinelSettings, SentinelProject, DiscoveredDoor + DoorMatcher
│   ├── Placement            EffectiveSetResolver (template + overrides), DoorSetPlacementCalculator,
│   │                        PlacementDiff (update planning), DoorSetInstanceOperations (assign/flip/add/remove…)
│   ├── Status               DoorSetStatusEvaluator, SourceChangeDetector, ComponentDriftDetector
│   ├── Rules                AssignmentRule + evaluator (suggestions only)
│   ├── Persistence          SentinelProjectSerializer (SentinelDataVersion, migrations), LibraryFile
│   └── Library              DefaultLibrary (starter components, DS-01, DS-02)
├── Sentinel                 Revit plugin (net48 → Revit 2024, net8.0-windows → Revit 2026)
│   ├── Setup                App (ribbon), Command (single modeless window per document)
│   ├── Infrastructure       SentinelLog, RevitCompat (version-specific API notes), RevitUnits (ft ↔ mm)
│   ├── Revit
│   │   ├── RevitSentinelHost    ISentinelHost implementation; queues all work on one ExternalEvent
│   │   ├── RevitRequestQueue    FIFO IExternalEventHandler (requests never overwrite each other)
│   │   ├── Collectors           DoorDiscoveryService (links, scopes, source verification), DoorReader
│   │   ├── Geometry             solids, wall measuring, boxes
│   │   ├── Placement            DoorSetPlacementExecutor, family/level lookup, wall-face ray casting, failure handling
│   │   ├── Preview              PreviewGraphicsServer (DirectContext3D transient graphics)
│   │   ├── Storage              SentinelProjectStorage (DataStorage), ElementTagStorage (per-element identity)
│   │   ├── Sync                 ProjectSynchronizer (refresh, relink, recovery)
│   │   └── NavigationService    select / zoom (linked door selection via SetReferences)
│   ├── UI
│   │   ├── Services             ISentinelHost (Core types only), SentinelSession, IDialogService
│   │   ├── ViewModels           Main, Doors, DoorRow, Inspector, InspectorComponent, Preview, DoorSets, Components, Rules, Settings
│   │   ├── Views                XAML pages and panels
│   │   └── Themes               Dark/Light brushes, SentinelStyles, ThemeManager, TitleBar, WindowResizer (from the baseline)
│   └── Diagnostics          SelfTest (opt-in, env-var gated in-Revit end-to-end test)
└── Tests
    ├── Sentinel.Core.Tests  xUnit, net8.0 + net48
    └── UiHarness            WPF exe: real UI + fake host, scripted checks, screenshots
```

## Principles

- **Calculation is separate from execution.** `DoorSetPlacementCalculator` is pure and deterministic; the same
  `PlacementPlan` feeds the preview graphics, the status evaluation (configuration hash) and the executor.
- **The UI never calls the Revit API.** View models talk to `ISentinelHost` using Core types only; the Revit host runs
  every request inside an ExternalEvent and marshals results back to the dispatcher. The same view models run in the
  UI harness with a fake host.
- **Statuses are derived, not stored.** They are recomputed from component records, source checks and the current plan.
- **Nothing is forgotten.** Deleted elements turn into *Missing* records; manual moves are *Manually Modified* and
  kept; source changes are reported until accepted; project data can be rebuilt from element tags.
- **Transactions are small and scoped.** One transaction per door inside a transaction group; a failing door rolls
  back only itself and records the reason; in-memory records change only after the door's transaction committed;
  the project data is saved at the end of the group (if that fails, the whole group is rolled back and memory reloaded).

## Door frame

`DoorGeometry` (host coordinates, mm): origin = centre of the opening at the door bottom on the wall centre plane;
`Facing` = side A normal; `WidthAxis` along the wall; width, height, wall thickness, hinge side.

- Revit family doors: LocationPoint, FacingOrientation, HandOrientation, DOOR_WIDTH/HEIGHT, host wall faces measured
  along the facing to find the wall centre and thickness.
- IFC doors (DirectShape): minimum-area rectangle of the plan footprint (short axis = facing, canonical sign), bounding
  Z range, `OverallWidth/Height` parameters when present, nearest linked wall measured for thickness. Hinge unknown.
- Link transform applied to all points and vectors.

## Placement

1. `EffectiveSetResolver`: template slots − removed + added, rule overrides applied.
2. Calculator per slot: resolve side from access direction (unsecured/secured, mirrored copy for *Both*), reference
   point on the width axis (hinge sign from override → door → settings), offsets, height, orientation →
   position, facing, instance rotation, explanation, issues.
3. `PlacementDiff` against existing records: Create / Move / Replace / Remove / KeepManual / Unchanged.
4. Executor: level-based → unhosted point + rotation; face/work-plane-based → ray cast (walls, links included) from
   the device side to the wall face and host there, else a vertical work plane (reported); tag + parameters written.

## Persistence format

Extensible Storage schema `SentinelProjectData` (GUID `5E3C1A7B-9D24-4F6E-8B13-2A7C9E0D4F61`, vendor RKTL):
`DataVersion:int`, `Header:string` (project id + settings), `Items:string[]` – one envelope per item:

```json
{"Type":"DoorSetInstance","Id":"…","Data":{ … }}
```

Types: `ComponentDefinition`, `DoorSetDefinition`, `DoorSetInstance`, `AssignmentRule`. Enums are stored by name.
Rules for evolving the format: bump `SentinelProjectSerializer.CurrentDataVersion` for any persisted shape change and
add a step to `SentinelDataMigrator`; never rename stored enum members; the Revit schema itself should not change.

Element tag schema `SentinelElementTag` (GUID `B8F2D6A4-3C71-4E95-A0D8-6F1B2E7C9A53`): project, set, component, slot,
definition, component definition, source link/door/IFC ids.

## Version-specific code

See `Sentinel/Infrastructure/RevitCompat.cs`. Both targets compile from the same C# 7.3 sources; no `#if` blocks are
currently required (ElementId.Value, `FilteredElementCollector(doc, viewId, linkId)` and `Selection.SetReferences`
exist in 2024 and 2026).
