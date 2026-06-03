# Logistics Mod Changes - 2026-05-18

This note summarizes the recent source-code changes made during the logistics routing, return-fuel, quota, naming, cleanup, and diagnostics debugging pass.

## Routing And Dispatch

- Added crew supply reservation for ghost logistics flights: human shipments now consume trip supplies at dispatch and count each person as 2 tons plus supplies for capacity, fuel, and launch payload planning.
- Removed remaining normal-mission and cycle-mission reads from logistics adoption and ghost route timing.
- Added route resource priority, pause controls, and route health summaries for route-managed shipments.
- Added a lightweight ghost-flight calculator that mirrors the cheap vanilla route branches for soonest-optimal logistics timing and fuel, including Moon-orbit Earth-Moon routes.
- Changed route ghost dispatch so resources that cannot fill the assigned ship's cargo capacity are skipped instead of launching partial loads.
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

- Reworked ghost route launches around first-class convoy records: route dispatch now batches compatible craft into manifest-based convoy flights instead of creating solo flight records and relying on UI-side merging.
- Compacted ghost flight rows to use icon-count ships, icon-amount cargo manifests, and numeric arrival dates.
- Moved ghost flight rows out of the main logistics screen and into each route editor, filtered to that route's active flights.
- Split route ghost flight rows into route/date and manifest lines, with cargo shown as comma-separated `iconxamount` entries.
- Wrapped route ghost flight rows in their own collapsible route traffic subsection below the route controls.
- Moved the route traffic subsection to the bottom of the route editor stack.
- Cleared the default collapse-section placeholder from the inline route traffic subsection.
- Removed the `return` cargo label from return flight rows while keeping route traffic rows at a consistent height.
- Added an invisible second line to return flight rows so route traffic labels align consistently.
- Reverted the logistics popup layout changes from the theme pass and kept the styling focused on the logistics palette rather than earlier cyan/navy styling.
- Replaced noisy route text like `LUNA [ORBIT] -> EARTH` in route titles, status notes, and flight rows with compact lane notation such as `LUNA ◉─● EARTH`.
- Normalized all-caps endpoint labels in the logistics popup and added smoothed mouse-wheel scrolling for long popup lists.
- Reworked logistics popup color tokens to the requested void/panel/card ramp, bone-white text scale, engine-fire accent, and amber/red/green functional states.
- Reworked route resource and route traffic rows into compact table-style layouts while preserving the two-line cargo manifest in flight rows.
- Changed logistics popup buttons to hollow bone-white bordered controls and moved route resource priority into a `Prio` column near the resource name.
- Refined logistics popup button tones so default controls use secondary outlines, hover states brighten to primary, actions use restrained engine-fire/nominal/amber accents, and close/remove controls use dark red fills.
- Shortened route status display for idle-vessel shortages to `No idle vessels`.
- Swapped standalone route endpoint labels to marker-first format, such as `● Earth`.
- Added vanilla `ObjectInfo.ImagePlanetUI` body/orbit icons to route header labels while leaving compact text markers in table/status values.
- Tightened route editor headers so icon/name route labels stay inline instead of stretching like table columns.
- Removed the top-level route collapse header from the logistics popup so route list and route editor content sit directly in the body.
- Replaced duplicate route-list ship counts with a leading status dot that shows fulfilled, shipping-limited, or source-shortage state.
- Retuned popup button colors so normal controls stay grey, add/confirm actions use borderless green fills, and close/remove buttons are borderless dark red fills.
- Converted assigned spacecraft and launch vehicle summaries from sentence rows into compact Icon/Asset/Ready/Qty table rows with Ready/Qty kept near the asset name.
- Muted resting button outlines to tertiary grey while keeping primary bone-white hover/press borders.
- Added an opaque card background to the object-info `Logistics` launcher button so underlying panel lines do not show through.
- Made the popup top bar full-width with zero left/top/right panel gap while keeping body content padded.
- Grouped each main route entry into a subtle vertical bundle so route rows no longer run together.
- Indented main route detail rows beneath the full-width route title, aligned detail text to the destination icon, darkened the indent gutter, and added more separation between route bundles.
- Matched the popup outer panel and scroll body surface colors, moved top breathing room into the inner content, kept a small outer bottom cushion, and added a right inset to indented route details.
- Made main route header rows collapse and expand their indented detail blocks while preserving Open/Pause/remove button actions.
- Added a slim themed popup scrollbar and hold-to-autoscroll middle mouse behavior for long logistics lists.
- Split live selected body state from popup-focused body state, so panel selection changes do not steal focus but clicking the selected panel's Logistics launcher switches the popup.
- Retuned route overview tab geometry so the detail panel starts between the status dot and body icon, row content aligns with the body name, and asset ready/qty columns sit closer to the asset name.
- Removed the extra row wrapper around the route editor Pause/Resume Route command so it matches the other full-width buttons.
- Flipped main route status-dot rules so no-ready-ship demand is warning amber, in-transit/covered demand is nominal green, and critical red is reserved for ready ships with demand but nothing available to load.
- Added a matching bottom inset to route overview detail tabs so the final asset row has the same black gutter treatment as the right edge.
- Reduced the route overview detail bottom inset so the final-row gutter is subtler.
- Normalized add-like buttons so all `+ Add` and `Assign` commands use the same muted green style instead of confirm green.
- Moved route editor Back and Pause/Resume into a single top inline action row so the route manifest content starts cleaner.
- Added collapsed-route resource icon summaries and persisted each route's open/closed overview state with the logistics save data.
- Realigned spacecraft/launch vehicle `Asset` table headers with the asset name text instead of the icon gutter.
- Some Nike missions are still attempting incorrect cargo payloads.
- Current diagnostics are intended to show the full path from planner manifest to stock launch attempt:
  - intended cargo and fuel manifest,
  - post-cap cargo amount,
  - total payload versus ship capacity,
  - stock planner result,
  - final `CreateFly` manifest.
- The next log review should compare `LOGI-MANIFEST`, `LOGI-CAP`, `LOGI-SCHEDULE`, and `LOGI-LAUNCH` entries for the same route and ship.
