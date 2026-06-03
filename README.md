# Ghost Fleet Logistics - Solar Expanse Mod

A BepInEx mod for **Solar Expanse** that adds a route-based logistics popup to object info windows. Pick a source body, create shipping lanes to other bodies or orbits, assign route-owned spacecraft and launch vehicles, then let Ghost Fleet move resources toward per-route stock targets.

This is an alpha mod. Keep backup saves.

## Current Highlights

- **Route-based logistics popup**: open Logistics from an owned object info panel, add a route, search for a destination, and manage that lane from one popup.
- **Route resource rules**: each route resource has a source keep amount, destination target amount, active/paused state, and priority.
- **Route-owned assets**: assign spacecraft and launch vehicles to a route. The UI shows Ready and Qty counts and releases idle assets back to the vanilla game when unassigned or when a route is removed.
- **Ghost convoy flights**: route dispatch batches compatible craft into visible logistics traffic with compact ship, route, arrival, and cargo rows.
- **Surface and orbit shortcuts**: same-body orbit drops and surface-to-orbit lifts can move stock directly when the route shape and launch support allow it.
- **Launch support awareness**: normal launch vehicles, facility-backed launch vehicles, rails, spin launchers, and space elevators can contribute route lift capacity.
- **Crew-safe cargo accounting**: human cargo consumes crew supplies and counts virtual capsule mass during payload, fuel, and launch planning.
- **Fuel and capacity accounting**: route flights estimate travel timing, outgoing fuel, launch fuel, return fuel, and payload capacity before dispatch.
- **Route health summaries**: rows report states like `Ready`, `Target stocked`, `Waiting for source surplus`, `Waiting for convoy launch`, `No idle vessels`, or paused state.
- **Per-save persistence**: routes, route resources, assigned ghost assets, active flights, route paused state, and collapsed route UI state are saved per save file.
- **Ghost upkeep accounting**: route-owned spacecraft and launch vehicles still contribute maintenance cost while they are in the ghost ledger.

## Installation

1. Install BepInEx 5.x for Solar Expanse.
2. Download a release zip.
3. Extract it into the Solar Expanse install folder so the DLL lands at:

```text
BepInEx\plugins\logisticsmod\LogisticsMod.dll
```

4. Launch the game.
5. Open an owned object's info window and click the Logistics button.

## Tester Builds

Create a tester-ready zip with:

```powershell
.\Package-Release.ps1 -GameDir "I:\SteamLibrary\steamapps\common\Solar Expanse" -IncludeSymbols
```

The zip is written to `dist/` and contains the expected `BepInEx/plugins/logisticsmod/` folder structure. Upload that zip to a GitHub pre-release when asking someone else to test the mod.

## Building

The project lives under:

```text
LogisticsMod\LogisticsMod.csproj
```

Build with:

```powershell
dotnet build .\LogisticsMod\LogisticsMod.csproj -c Release -p:GameDir="I:\SteamLibrary\steamapps\common\Solar Expanse"
```

The project deploys the main build output directly to the configured game's BepInEx plugin folder. The DLL and PDB are also copied to the repository root for local manual testing, but those files are ignored by git. Distribution builds should use `Package-Release.ps1`.

## How It Works

### Quick Start

1. Open the object info window for the body or orbit that should supply resources.
2. Click Logistics.
3. Click **+ Add Route** and choose the destination body or orbit.
4. Open the route.
5. Add one or more route resources.
6. For each resource, set:
   - **Source Keep**: stock to leave behind at the route source.
   - **Destination Target**: stock level the route should try to maintain at the destination.
   - **Priority**: how strongly this resource competes for available route craft.
7. Assign route spacecraft.
8. Assign route launch vehicles when the source needs surface launch support.
9. Let daily logistics processing run.

### Popup Flow

| Area | Purpose |
| --- | --- |
| Route list | Shows shipping lanes from the selected source object. Each route can be opened, paused, collapsed, or removed. |
| Route health | Summarizes the route's most important blocker or readiness state. |
| Route resources | Lists resource icons plus a table with resource, priority, target, and status. |
| Assigned spacecraft | Shows route-owned spacecraft grouped by type with Ready and Qty counts. |
| Assigned launch vehicles | Shows route-owned launch vehicles grouped by type with Ready and Qty counts. |
| Route Traffic | Shows active ghost flights for the route, including craft count, compact route lane, arrival date, and cargo manifest. |

### Route Resource Status

| Status | Meaning |
| --- | --- |
| `Ready` | No current blocker is known. |
| `Target stocked` | Destination stock plus in-flight cargo meets the target. |
| `Waiting for source surplus above N` | The source does not have stock above the keep amount. |
| `Waiting for convoy launch` | The route has demand and source stock, but a ghost convoy has not launched yet. Usually check assigned craft, launch support, fuel, or capacity. |
| `No idle vessels` | Assigned route spacecraft exist, but none are idle at the source. |
| `Paused` | The route or route resource is disabled. |

## Route Execution Model

On each in-game day, `LogisticsObserver.OnDayChange()`:

- releases orphaned route-owned assets when possible
- advances active ghost flights
- refreshes ghost flight visuals
- processes active routes
- applies direct same-body orbit drops where valid
- applies virtual surface-to-orbit lift when enabled and supported
- dispatches ghost convoys with route-owned spacecraft for remaining demand

The current route planner is lane-based, not a global network solver. A route belongs to one source object and one destination object. Each route can carry multiple resources, and resource priority influences how limited idle craft are distributed.

## Surface And Orbit Handling

The route system has three major movement paths:

- **Orbit drop**: source orbit to its body can move stock directly.
- **Virtual surface lift**: body to its own orbit can move stock directly when surface lift is enabled and launch support exists.
- **Ghost convoy flight**: route-owned spacecraft carry cargo between objects using estimated travel time, fuel, and payload checks.

Virtual surface lift is controlled by:

```text
BepInEx\plugins\logisticsmod\LogisticsMod.cfg
```

Relevant config keys:

| Section | Key | Purpose |
| --- | --- | --- |
| `SurfaceLift` | `Enabled` | Enables same-body surface-to-orbit direct logistics movement through launch support. |
| `SurfaceLift` | `PayloadsPerFacilityPerDay` | Controls how many full facility-backed launch payloads each enabled launch facility can move per in-game day. |
| `Diagnostics` | `VerboseLogging` | Writes route and dispatch diagnostics to `BepInEx/LogisticsMod_*.log`. |

## Save/Load Behavior

Logistics data is stored beside each game save:

```text
BepInEx\saves\<SaveName>\LogisticsData.json
```

Saved route data includes:

- routes and destination ids
- route active/paused state
- route collapsed/expanded UI state
- route resource rules
- assigned ghost spacecraft
- assigned ghost launch vehicles
- active ghost flights

Runtime-only state such as committed stock windows, request throttles, virtual lift usage, and ghost visuals is cleared on load. Post-load processing then resumes route planning.

## Diagnostics

Enable `Diagnostics.VerboseLogging` in `BepInEx/plugins/logisticsmod/LogisticsMod.cfg` when testing difficult routes.

Useful log terms:

```text
ROUTE add
ROUTE-DROP
ROUTE-LIFT
GHOST convoy
GHOST flight
GHOST visual
GHOST reserve-lv
GHOST orphan cleanup
UPKEEP ghost-ledger
VIRTUAL-LIFT
REQ throttle-skip
RESET runtime-state
```

For tester guidance, see `TESTING.md`.

## Architecture

```text
ObjectInfoWindow
  -> LogisticsUI
      -> Logistics launcher button
      -> route list
      -> route editor
      -> route resource rules
      -> assigned ghost spacecraft
      -> assigned ghost launch vehicles
      -> route traffic rows

LogisticsNetwork
  -> in-memory route and ghost-ledger data keyed by ObjectInfo.id
  -> route creation/removal
  -> route resource rules
  -> ghost spacecraft adoption/release
  -> ghost launch vehicle reservation/release
  -> orphaned route asset cleanup

LogisticsPersistence
  -> per-save JSON save/load
  -> route, asset, and flight persistence

LogisticsObserver
  -> daily route processing
  -> direct orbit drops
  -> virtual surface lifts
  -> ghost convoy dispatch
  -> ghost flight completion
  -> ghost flight visuals
  -> route status notes

MaintenancePatches
  -> ghost spacecraft and launch vehicle upkeep

SaveLoadPatches
  -> logistics save/load hooks
  -> post-load route planning trigger
```

## Known Limitations

- This is alpha software and can break saves. Back up before testing.
- Routes are source-to-destination lanes, not an arbitrary multi-hop network.
- Surface-to-orbit lift is virtual stock movement, not a physical mission.
- Ghost flight timing and fuel are estimates intended to mirror common vanilla route branches, not a full replacement for the game's planner.
- Crew and return fuel behavior are conservative and need edge-case testing.
- Some legacy save data structures still exist for compatibility, but the current player-facing workflow is route-based.
