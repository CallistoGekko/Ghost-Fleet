# Logistics Mod Changes - 2026-05-18

This note summarizes the recent source-code changes made during the logistics routing, return-fuel, quota, naming, cleanup, and diagnostics debugging pass.

## Routing And Dispatch

- Added ranked logistics route selection in `LogisticsObserver`, replacing the older first-usable-provider behavior.
- Added support for one source-side orbital relay hop: surface provider -> provider orbit -> final destination.
- Kept relay legs internal to the planner so the player-facing UI still shows a single request.
- Added source ranking rules that prefer local orbital sources before more expensive surface or remote sources.
- Added support for launch-support scoring so magnetic rails, space elevators, spin launchers, and similar low-cost launch infrastructure can influence source choice.
- Added route-level planning locks so only one async stock planning job is submitted for a specific source -> target -> resource route at a time.
- Route locks are released after the stock planning callback completes, allowing the next load to begin planning immediately after the mission is created rather than waiting for arrival.
- Added cleanup for route locks when runtime state is reset between saves.

## Quotas And Vehicle Availability

- Reworked spacecraft quota handling to count actual ships at the quota location and report active/available quota usage.
- Added support for numeric launch vehicle quota entries instead of simple on/off toggles.
- Added quota checks that exclude ships already committed to active logistics flights, return-home state, or stock cyclical missions.
- Tightened spacecraft identity checks so ships already in use are not selected again for new logistics dispatch.
- Ensured spacecraft not present in the relevant logistics quota should not be selected for logistics missions.

## Return Home And Cycle Safety

- Restored `TryFixWrongThrust = true` for logistics planning.
- Changed return-home behavior to rely more on stock feasibility checks rather than disabling stock blocks.
- Added cooldowns for blocked logistics attempts so failed cycle creation does not spam logs or repeatedly put ships into bad transient states.
- Added longer cooldown escalation for repeated failed return-cycle attempts.
- Added cleanup for completed logistics mission trajectories and stale unlaunched logistics missions.
- Added protection against empty outbound `[LOGI]` flights by blocking stock `CreateFly` when no positive normal requested cargo remains.
- Added one-shot cycle cleanup when a stock planning job does not actually start, so stale cycles are not left around indefinitely.

## Return Fuel And Cargo Manifest Accounting

- Added async return-fuel probing through the stock planner to estimate return reserve requirements instead of relying only on handcrafted estimates.
- Added return-fuel reserve cargo to outgoing logistics manifests when the destination does not already have enough trusted fuel stock.
- Added fallback reserve behavior when the stock fuel probe reports zero reserve.
- Added logic to reduce requested cargo when return fuel must occupy cargo space.
- Fixed `normalCargo` accounting after fuel displacement so the recorded logistics cargo amount reflects the actual post-fuel metal/electronics/etc. in the manifest.
- Added a guard to block fuel-only `[LOGI]` deliveries when fuel displacement leaves zero requested cargo.
- Left `FlyWithWhatIsAvailable` enabled for logistics cycles so intentionally partial loads remain possible.

## SEND/GET UI And Status

- Updated SEND/export UI wording to describe reserve behavior more accurately.
- Allowed SEND/export rules with a reserve value of `0` instead of auto-deleting them.
- Added request status text for blocked pending states such as missing spacecraft, missing launch vehicle quota, missing fuel, cooldowns, and active planning.
- Added support for GET/in-transit status to include vehicle and arrival information where available.
- Matched more of the logistics UI text and section styling to the stock object-info UI.
- Reverted problematic button color/flash changes while preserving improved text styling work.

## Mission Naming And Icons

- Added naming patches so stock-created logistics missions keep `[LOGI]` and `[LOGI-RETURN]` names.
- Added resource icon labels to logistics mission names where possible.
- Added patches around stock mission creation, parameter naming, mission rows, and map labels so logistics names survive more stock UI refresh paths.
- Fixed duplicate async planning by ensuring stock cycle planning recognizes when a logistics planning job is already active.

## Diagnostics Added

- Added route-choice diagnostics for candidate scoring and blocker reasons.
- Added return-fuel probe and manifest diagnostics.
- Added cargo cap diagnostics:
  - `LOGI-CAP before`
  - `LOGI-CAP scaled`
- Added manifest diagnostics:
  - `LOGI-MANIFEST direct`
  - `LOGI-MANIFEST lv`
- Added stock schedule diagnostics:
  - `LOGI-SCHEDULE selected-before-create`
  - `LOGI-SCHEDULE after-create`
- Added stock launch-attempt diagnostics:
  - `LOGI-LAUNCH createfly-prefix`
  - `LOGI-LAUNCH createfly-postfix`
- Added payload summaries showing attempted cargo, spacecraft capacity, propellant target, propellant actual, and full manifest contents.

## Save/Load And Runtime State

- Added stronger runtime-state reset behavior to reduce save contamination.
- Cleared pending deliveries, blocked retries, return-home ownership, return-fuel probe cache, cycle-created timestamps, cycle-planning failure state, and route planning locks when runtime state resets.
- Extended reconciliation so active logistics missions and return states are considered after load.

## Build And Deployment

- The mod has been rebuilt repeatedly with:
  - `dotnet build C:\Users\parft\Documents\SolarExpanseMods\LogisticsMod\LogisticsMod.csproj -c Release`
- The built DLL is deployed to:
  - `C:\Program Files (x86)\Steam\steamapps\common\Solar Expanse\BepInEx\plugins\logisticsmod\LogisticsMod.dll`
  - `C:\Users\parft\Documents\SolarExpanseMods\LogisticsModGit\LogisticsModTeddFork\LogisticsMod.dll`

## Current Debug Focus

- Some Nike missions are still attempting incorrect cargo payloads.
- Current diagnostics are intended to show the full path from planner manifest to stock launch attempt:
  - intended cargo and fuel manifest,
  - post-cap cargo amount,
  - total payload versus ship capacity,
  - stock planner result,
  - final `CreateFly` manifest.
- The next log review should compare `LOGI-MANIFEST`, `LOGI-CAP`, `LOGI-SCHEDULE`, and `LOGI-LAUNCH` entries for the same route and ship.
