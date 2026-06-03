# Logistics Tab - Solar Expanse Mod

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **Solar Expanse** that adds a Factorio-style logistics tab to object info windows. Configure bodies to **SEND** resources, configure other bodies to **GET** resources, and the mod plans recurring logistics shipments using your existing spacecraft, launch vehicles, orbital payload containers, and launch-assist infrastructure.

## Current Highlights

- **Single-request logistics UI**: players configure one GET request; internal relay legs remain hidden planner state.
- **Ranked source selection**: the planner no longer takes the first usable provider. It enumerates feasible providers, scores routes, logs the rationale, and dispatches the best candidate.
- **One orbital relay handoff**: surface stock can be lifted to that body's orbit with the orbital payload container, then carried onward by an orbit-capable spacecraft.
- **Orbit-aware sourcing**: orbit stock is preferred over deep gravity wells when appropriate, with moon/planet/sibling relationships considered.
- **Launch-assist support**: magnetic launch rails, spin launchers, space elevators, and similar surface infrastructure are treated as low-cost launch options.
- **Spacecraft and launch vehicle quotas**: logistics will not exceed configured quotas and does not hide ships from the vanilla planner.
- **Return-home cycles**: ships used for logistics are marked to return to their source object after delivery.
- **Return fuel handling**: cargo manifests can reserve return fuel, and blocked deliveries can request fuel bootstrap shipments instead of spamming invalid cargo attempts.
- **Useful pending reasons**: PENDING requests explain why nothing is being sent, such as no provider, no spacecraft, no LV quota, no container, or no fuel.
- **Transit details**: GET rows can show the vehicle and expected arrival date when a delivery is actually in transit.
- **Per-save persistence**: logistics data is saved per save file, and runtime state is cleared on load to prevent one save contaminating another.
- **BepInEx diagnostics**: route candidates, blockers, return-home decisions, and other planner rationale are logged to the BepInEx log.

## Installation

1. Install BepInEx 5.x for Solar Expanse.
2. Download a release zip and extract it into the Solar Expanse install folder, or put `LogisticsMod.dll` in `BepInEx/plugins/logisticsmod/LogisticsMod.dll`.
3. Launch the game.
4. Open an object's info window. The logistics controls appear under the normal object sections.

## Tester Builds

Create a tester-ready zip with:

```powershell
.\Package-Release.ps1 -GameDir "I:\SteamLibrary\steamapps\common\Solar Expanse" -IncludeSymbols
```

The zip is written to `dist/` and contains the expected `BepInEx/plugins/logisticsmod/` folder structure. Upload that zip to a GitHub pre-release when asking someone else to test the mod.

## Building

The project lives under:

```text
LogisticsMod/LogisticsMod.csproj
```

Build with:

```powershell
dotnet build .\LogisticsMod\LogisticsMod.csproj -c Release -p:GameDir="I:\SteamLibrary\steamapps\common\Solar Expanse"
```

The project is configured to deploy the main build output directly to the configured game's BepInEx plugin folder:

```text
BepInEx\plugins\logisticsmod\LogisticsMod.dll
```

After each build, the DLL and PDB are also copied to the repository root:

```text
LogisticsMod.dll
LogisticsMod.pdb
```

## How It Works

### Quick Start

1. On a source object, open the Logistics tab and add a **SEND** provider for a resource.
2. Set a minimum keep amount so the source does not export below local reserve.
3. Add **SPACECRAFT** quotas for ship types logistics may use.
4. If the source is a surface body, enable useful **LAUNCH VEHICLE** or launch-assist options.
5. On the destination object, add a **GET** request for the resource.
6. Let daily logistics planning run.

### UI Sections

| Section | Purpose |
| --- | --- |
| **GET - Request Resources** | Resources this object wants delivered. Shows pending, in transit, satisfied, or failed status. |
| **SEND - Provide Resources** | Resources this object can export, with a minimum keep reserve. |
| **SPACECRAFT - Logistics Vessels** | Quotas for reusable spacecraft that logistics may use. |
| **LAUNCH VEHICLE - Surface Shuttles** | Surface launch support, including normal LVs and supported launch-assist facilities. |

### Request Status

| Status | Meaning |
| --- | --- |
| **PENDING** | The request needs resources, but no shipment is currently active. The row should explain the blocker. |
| **IN TRANSIT** | A logistics cycle, relay leg, pending stock-planner job, or return-sensitive delivery is active. |
| **SATISFIED** | The requested amount, or minimum amount for minimum-mode requests, is present. |
| **FAILED** | Reserved for requests that cannot be completed safely. Most temporary blockers stay PENDING and retry. |

## Routing Model

The planner supports these v1 route shapes:

- Direct spacecraft delivery.
- Direct surface launch using a launch vehicle plus a spacecraft or orbital payload container.
- Source-surface-to-source-orbit staging, followed by source-orbit-to-destination delivery with a normal spacecraft.

The relay depth is intentionally limited to one source-side orbital handoff. There is no arbitrary graph search and no destination-side staging layer in this version.

### Source Ranking

For surface destinations, the planner prefers sources roughly in this order:

1. Destination body's orbit.
2. Sibling moon orbits in the same local system.
3. Parent planet orbit.
4. Nearby local surfaces.
5. External planets and orbits.

For orbit destinations, the planner prefers:

1. Exact destination orbit.
2. Same-body surface.
3. Sibling moon orbits.
4. Parent planet orbit.
5. Broader external sources.

Within a tier, it prefers routes that avoid launch vehicles, then fewer hops, then more available stock, then stable object ordering.

## Planner Details

`LogisticsObserver.OnDayChange()` is the main daily loop. It:

- attempts return-home planning for idle logistics ships
- scans all logistics requests
- reconciles active, pending, and in-flight deliveries
- advances staged relay requests
- computes outstanding demand
- asks the route planner to create exactly one best shipment when needed

`TryCreateDeliveries()` builds route candidates from all usable providers, scores them, logs the candidate table, and executes the best route that can be handed to the stock mission planner.

Actual travel is still delegated to the game's cyclical mission system with `ETransferType.Optimal`. Logistics chooses the source, vehicle, launch support, cargo manifest, and relay shape; the stock planner computes the flight.

## Save/Load Behavior

Logistics data is stored per save under the BepInEx saves folder. On load, the mod clears:

- all in-memory logistics network data
- observer runtime state
- pending delivery bookkeeping
- return-home tracking
- time-controller runtime flags

After clearing, it reloads the save's logistics JSON and reconciles existing `[LOGI]` cyclical missions back onto matching requests.

## Diagnostics

The BepInEx log includes planner rationale and runtime state transitions. Useful strings to search for:

```text
LOGI_ROUTE
ROUTE_CANDIDATE
RETURNHOME
RETURNFUEL
BLOCKER
UISTYLE_DUMP
```

Diagnostic config is available in `BepInEx/config/LogisticsMod.cfg`, including:

| Key | Purpose |
| --- | --- |
| `CyclePlanningGraceDays` | Grace window for newly created cycles before stale-cycle cleanup can consider them abandoned. |
| `VerboseLogging` | Enables extra route and state logging. |

## Architecture

```text
ObjectInfoWindow
  -> LogisticsUI
      -> GET requests
      -> SEND providers
      -> spacecraft quotas
      -> launch vehicle / launch-assist toggles

LogisticsNetwork
  -> in-memory logistics data keyed by ObjectInfo.id

LogisticsPersistence
  -> per-save JSON save/load
  -> load-time network clearing

LogisticsObserver
  -> daily logistics loop
  -> route enumeration and ranking
  -> relay-stage tracking
  -> mission creation
  -> fuel bootstrap
  -> return-home handling

SpaceCraftCyclicalMissionControllerPatches
  -> stock planner integration
  -> logistics mission safety hooks
```

## Known Limitations

- Only one internal orbital relay hop is supported.
- Destination-side relay staging is not implemented.
- The planner relies on the stock cyclical mission system for actual flight planning, so stock planner limitations still apply.
- Return fuel behavior is conservative and may need further tuning for edge cases.
- UI styling is still being matched to the stock game and may not be pixel-perfect yet.
- This is an experimental mod. Keep backup saves.
