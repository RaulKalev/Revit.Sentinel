# Sentinel

Revit plugin (RK Tools) for designing access control and security devices around doors. Sentinel discovers doors
in a linked IFC/Revit model, lets you assign reusable **door set types** (reader, contact, lock, REX…), previews the
proposed device positions, places the device families and keeps every placed set **reviewable and editable** afterwards.

Sentinel is a designer-assistance tool: it detects, suggests, previews, places by explicit rules and reports
inconsistencies. It never makes a decision you cannot see and undo.

- Revit 2024 (.NET Framework 4.8) and Revit 2026 (.NET 8), single merged DLL per version
- Modeless WPF window, RK Tools look (dark/light), every model change through ExternalEvent + Transactions
- All state stored in the Revit project (Extensible Storage, versioned data format)

Status: **Milestone 1** – see [What is verified](docs/VERIFICATION.md) and [Known limitations](#known-limitations).

---

## Workflow

### 1. Configure components (once per project or via library import)
**Components** page – the device library (Card Reader, Door Contact, Electric Lock, REX PIR/Button, Emergency
Release, Keypad, Intercom, Glass Break, PIR, Panic Button, Custom). For each component pick the **Revit family type**
from the families loaded in the project. Nothing is hard-coded: an unmapped component is reported as
*"Card Reader family not configured"* and is never placed silently.

Supported families: **face-based / work-plane-based** (hosted on the linked wall face) and **level-based** (placed
unhosted at the calculated point). Wall-hosted families cannot be hosted by linked walls and are rejected with a reason.

Unhosted families: Sentinel assumes the device front is the family's local **−Y** axis (the side Revit's *Front* view
looks at). If your family faces +Y, set *Rotation offset* = 180. Face-based families face away from their host face.

### 2. Door set types
**Door Sets** page – e.g. `DS-01 Standard Office Access` (Reader, Door Contact, Lock) and `DS-02 Secure Technical
Door` (+ REX PIR) are provided as examples. Each component slot has a **placement rule**:

| Rule field | Meaning |
|---|---|
| Reference | Latch jamb (opening edge), hinge jamb or door centre |
| Side | Unsecured / secured side (follow the access direction), fixed side A / B, or in wall / frame |
| Along wall | From a jamb: + outwards, − over the leaf. From the centre: + towards the hinge |
| From wall | Distance from the wall face (in wall: from the wall centre towards side A) |
| Height | From door bottom or door head; blank = component default |
| Orientation | Face away from wall, face toward door, follow wall, fixed rotation (+ extra rotation) |
| Duplicate | Place a mirrored copy on the other side when access is controlled in both directions |

### 3. Doors
**Doors** page:
1. Choose the **linked model** and **scope** (all linked doors, doors in the active view, selected levels) → **Find Doors**.
2. Select doors → choose a door set → **Assign** (or **Suggest from rules…**, always with confirmation).
3. **Preview…** walks through the selection: *Previous / Next / Flip Set / Swap hinge / Change set / Edit Components /
   Skip / Confirm*. Preview graphics are temporary (DirectContext3D) – nothing is written until **Confirm**.
4. Or **Place automatically** for many doors: the result reads e.g. *"34 placed, 2 require review, 1 failed"*, and
   every door gets its own reason.
5. **Refresh** checks everything: deleted components (*Missing Component*, e.g. "⚠ Reader missing"), manually moved
   components (*Modified*, never pushed back automatically), moved/rotated/resized/deleted source doors
   (*Source Changed* / *Orphaned*), unloaded links.
6. **Update placement** applies configuration changes (flip, set change, overrides, template edits, source moves) to
   already placed sets after showing exactly what will be created, moved, replaced or deleted. Manually modified
   components are kept unless you explicitly choose to overwrite them.

The **inspector** (right panel) shows one door: set, status and reasons, access direction (*Corridor → Server Room*),
**Flip**, hinge (+ **Swap hinge**), components with their state and the reason for their position, per-door overrides
(add / remove / change type / edit rule values), source data (link, mark, size, wall, IFC GlobalId, parameters),
**Accept source changes**, notes, select / zoom actions.

Statuses: Unassigned · Ready · Preview · Placed · Modified · Missing Component · Source Changed · Orphaned · Error · Ignored.

### 4. Rules (optional)
**Rules** page – *IF Room/parameter conditions THEN door set* with priorities (equals, does not equal, contains, does not
contain, starts with, greater than, less than, is empty, is not empty). Rules only **suggest**; nothing depends on them.

### 5. Settings
Hinge convention of the door families, tolerances, captured source parameters (FireRating, SecurityRating…),
optional identity text parameter on placed elements, preview view, library export/import.

---

## Concepts and data model

```
ComponentDefinition ──► DoorSetDefinition (SecuritySetDefinition) ──► AssignmentRule (suggests)
                                   │
                                   ▼
                        DoorSetInstance (SecuritySetInstance)
                        ├── SourceDoorReference   link UniqueId + door UniqueId + IFC GlobalId + last known geometry/parameters
                        ├── AccessDirection        SideAToSideB | SideBToSideA | Both   (not "mirrored")
                        ├── HingeSideOverride
                        ├── SetOverrides           added components, removed template components, per-rule value overrides
                        ├── PlacedComponentInstance[]  slot, element UniqueId, calculated / placed / actual pose, state, trace
                        └── review state, placed configuration hash, definition revision, notes
```

- **Side A** is the side the source door faces (Revit FacingOrientation, or a stable footprint normal for IFC doors).
- **Overrides** are stored separately from the template; an instance always references its base set type.
- A set instance stays stored when its elements are deleted – the set knows what should exist.
- **Identity**: every placed element carries an Extensible Storage tag (SentinelSetId, SentinelComponentId, slot,
  source door ids). Refresh relinks components from tags, reports copied elements and can rebuild sets if the project
  data is lost.
- **Persistence**: one `DataStorage` element with a tiny schema (`DataVersion`, header JSON, one JSON envelope per
  item). `SentinelDataVersion = 1`; migrations are applied on load; items a version cannot read are preserved
  verbatim; data written by a newer Sentinel opens **read-only**.
- Designed to extend to **room sets** (`SecuritySetKind.Room`, `RoomSetInstance`) and later controller topology
  without changing the storage schema.

Architecture details: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

---

## Build

Requirements: Windows, .NET SDK 8+, Visual Studio 2022 (optional). Revit API assemblies come from NuGet
(Nice3point.Revit.Api, compile only).

```bash
dotnet build Sentinel.sln -c Release
```

- `Sentinel/bin/.../net48/Sentinel.dll` → Revit 2024, `.../net8.0-windows/Sentinel.dll` → Revit 2026
  (Costura merges all dependencies into the one DLL).
- Local builds go to `%UserProfile%\Desktop\DevDlls\Sentinel\` like the other RK Tools plugins; CI builds (or
  `-p:SentinelUseDefaultOutput=true`) use `Sentinel/bin/`.

### Install
Copy `Deploy/Sentinel2024.addin` / `Deploy/Sentinel2026.addin` to `%ProgramData%\Autodesk\Revit\Addins\<version>\` and
set `<Assembly>` to the matching `Sentinel.dll`. Ribbon: **RK Tools → Sentinel**.

## Tests

| Suite | What it covers | Command |
|---|---|---|
| `Tests/Sentinel.Core.Tests` | placement math (sides, flip, both directions, hinge, rotation, heights), overrides, operations, statuses, change/drift detection, diff/update planning, rules, footprint analysis, persistence + migration safety, library files | `dotnet test Tests/Sentinel.Core.Tests -c Release` |
| `Tests/UiHarness` | the real window and view models against a fake host: discovery, assign, batch place, missing component, source moved/deleted, flip, overrides, preview workflow, update, rules, library pages, save/load; writes screenshots + plan renders to `Tests/UiHarness/out` | `dotnet build Tests/UiHarness -c Release` then run `UiHarness.exe` |
| In-Revit self-test | real Revit: builds its own linked model with doors and device families, then discovery, placement, hosting on linked walls, tags, refresh, update, preview server, window, save/reopen, recovery | `./Tools/Run-RevitSelfTest.ps1 -RevitVersion 2026 -RevitExe "<path to Revit.exe>"` |

## Known limitations

- **Not yet exercised in a live Revit session** – see [docs/VERIFICATION.md](docs/VERIFICATION.md). Run the self-test
  before production use.
- Hinge side of Revit door families depends on how the family was authored → project setting + per-door *Swap hinge*.
  IFC doors carry no hinge information: the hinge is assumed and reported as a warning until confirmed.
- IFC doors (DirectShapes) get their frame from the plan footprint of their geometry and the nearest linked wall;
  curved walls and unusual door geometry may give *Unsupported door geometry* or estimated wall thickness.
- Rooms on each side are probed at a point (link rooms, then host rooms / spaces); IFC links often have no rooms.
- Moving a **face-hosted** component (Update placement) re-creates it; Sentinel-defined parameters are re-applied,
  manual parameter edits on that element are lost. Manual moves are kept unless you explicitly overwrite them.
- Wall-hosted families are not supported (linked walls cannot host them).
- One `DataStorage` element holds the Sentinel data: in workshared models only one user at a time can save Sentinel
  changes (Revit element borrowing); synchronise before editing.
- Sentinel creates two helper 3D views: *Sentinel Focus* (navigation/preview) and *Sentinel Placement (do not edit)*
  (wall-face ray casting). Every save is an undoable *Sentinel – save* transaction; Undo/Redo reloads Sentinel data.
- UI is English only.

## Milestone 2 (planned)

- Room-based security sets (PIR, glass break, panic button) on the existing `SecuritySetDefinition` / `Instance` model
- Template update review: compare instances with newer set-type revisions, apply selectively
- IFC hinge detection (OperationType), curved walls, curtain wall doors
- Tagging / schedules helpers, bulk override editing, per-door placement notes in exports
- Controller / I/O association and Pulse topology integration (persistence already keys everything by stable ids)

## Repository

`https://github.com/RaulKalev/Revit.Sentinel` – created from the Revit.Openings (RedeliAvad) baseline; the Openings
domain code was removed, its window chrome, theming, ExternalEvent and Extensible Storage patterns were kept.
