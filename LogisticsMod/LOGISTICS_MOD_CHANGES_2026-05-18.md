# Logistics Mod Changes - 2026-05-18

This note summarizes the recent source-code changes made during the logistics routing, return-fuel, quota, naming, cleanup, and diagnostics debugging pass.

## 0.4.2 Alpha 2 Test Build

- Guarded vanilla mission diagnostics at patch time so disabled diagnostics cannot spam the player log or collapse frame rate.
- Disabled ghost-flight trajectory line visuals for this alpha because they were too costly in active saves; route traffic remains visible in the logistics table.

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
- Added route-level facility launch category toggles so routes can opt out of shared space elevator, spin launch, magnetic rail/mass driver, or generic facility launch support while keeping assigned launch vehicles separate.
- Styled route facility launch toggles as square icon tiles using the source launch-support sprite, with hover tooltips.
- Refined route facility launch tiles into a wrapped themed section with the label on its own line, nominal-green active tint, muted disabled opacity, no corner pip, clipped sprites, and a compact icon scale.
- Facility launch sections now always show every supported facility category, rendering missing source categories as 20% opaque placeholders instead of hiding them.
- Facility launch sections are hidden for orbit route sources because orbit routes have no surface facility launch policy.
- Removed the disabled-tile X overlay and added representative launch sprite lookup for missing facility launch categories.
- Tightened the facility launch tile row sizing so the section stays compact and wide facility sprites remain clipped inside their square tile.
- Added route-level planning locks so only one async stock planning job is submitted for a specific source -> target -> resource route at a time.
- Route locks are released after the stock planning callback completes, allowing the next load to begin planning immediately after the mission is created rather than waiting for arrival.
- Added cleanup for route locks when runtime state is reset between saves.
- Corrected deep-space ghost flight fuel estimates so cached vanilla optimal transfers can drive delta-v and interplanetary routes are no longer capped to a small tank fraction.
- Matched route ghost flight propellant estimates to vanilla cached optimal transfers by checking low-orbit endpoint variants, normalizing suspicious fractional deep-space delta-v values, and preventing same-parent/parent-child routes from using the short-hop tank-fraction cap.
- Tuned the heliocentric same-parent fallback to match the vanilla fastest impulse scale for Venus-to-Earth Zeus routes and allowed active ghost-flight rows to correct clearly mismatched stored fuel both upward and downward.
- Rebalanced the heliocentric fallback against the Mars-to-Earth Nike case, rejected stale slow cached transfers for fast logistics routes, trusted current high-delta-v vanilla transfers, and allowed active ghost-flight rows to repair clearly wrong arrival dates as well as fuel.
- Added a vanilla mission formula regression harness covering grouped spacecraft fuel rounding and fastest versus optimal porkchop selection rules.
- Reworked route facility launch controls into labeled options with distinct enabled, disabled, and missing states, and kept empty route spacecraft/launch vehicle sections aligned without misleading headers or zero counts.
- Changed route facility launch option states so disabled categories render red, while enabled categories missing at the current source stay clickable and render amber for preconfiguration.
- Stretched route facility launch option rows to equal section padding and restored representative icons for missing categories so all category icons remain visible.
- Expanded route facility launch options to the six vanilla launch facility types in a 3x2 grid.
- Preserved the active route editor across passive popup refreshes so advancing time no longer returns the popup to the route list.
- Refreshed the open route editor in place as game time advances so ready counts, route status, and route traffic update without leaving the route page.
- Rendered route facility launch icons from vanilla facility images when available, greyed enabled-missing facilities, and reserved warning amber for `Missing Disabled`.
- Darkened the sticky route subheader, removed decorative facility launch grid rules, and restyled empty asset rows as normal table rows.
- Fixed missing facility launch icon selection to classify all vanilla launch facility descriptors by real descriptor names/ids instead of `GetText(false)` sprite markup.
- Adjusted the sticky route subheader to a middle theme shade so it stays distinct from both the title header and panel body.
- Routed Space Elevator icons through the launch-support sprite path instead of its placeholder-prone facility descriptor image.
- Detected built Space Elevators from finished/enabled source facilities and exposed them to route launch support even when vanilla does not create a fake launch vehicle row.
- Detected all finished/enabled launch facility categories directly from source facilities so Launch Pad and similar buildings are available even when vanilla does not surface a fake launch vehicle row.
- Let direct built facility launch support replace its fake launch vehicle row for route surface lifts, preventing enabled Rotary Launchers from being hidden by fake-LV readiness/cooldown.
- Kept directly detected launch facility tiles on category representative art so Launch Pad availability does not pick up unrelated built-facility descriptor sprites.
- Added larger Keep/Target amount steps up to 1B in paired positive/negative rows in the logistics amount editors.
- Rendered empty route resource, spacecraft, and launch vehicle sections as matching table-color rows with lightly padded left-aligned primary text.
- Removed the facility launch grid section background so the launch facility tiles float on the route editor background.
- Removed the remaining horizontal padding from the floating facility launch grid so its tile edges align with the route editor rows.
- Switched logistics amount labels to compact mass postfix text such as `KT`, `MT`, and `BT` while preserving button theme colors.
- Fixed route resource priority controls so they refresh as a radio-button group with only the selected priority using the active state.
- Added balanced mixed-manifest route dispatch so one assigned ship can split cargo capacity across same-priority shippable resources instead of launching a one-resource partial load.
- Let route resource priority control partial-load dispatch urgency: High can launch at 10% manifest fill and Critical can launch with any positive carried amount.
- Added a clickable route flight detail page showing frozen cargo, launch fuel, outbound/return fuel, delta-v, mass, tank, mode, and route-kind calculation inputs.
- Softened all-caps module cargo names and compacted route traffic module manifests into icon-count stacks.
- Allowed queued modules on same-body surface-to-orbit routes to lift directly on route launch support or assigned launch vehicles without requiring assigned spacecraft.
- Kept the logistics popup above normal game windows but below active vanilla alert/trivia popups by re-layering it through `UIManager.Open(...)`.
- Prevented one loaded spacecraft launch from splitting across multiple launch supports, so reserved launch vehicles and facility launchers are selected as one-or-the-other per payload.
- Normalized logistics UI icon rendering so custom resource, module, asset, location, and launch-support icons stay inside fixed UI slots without modifying the source art.
- Replaced raw resource ids in route status text with the normal resource icon/name label, including missing fuel messages.
- Removed the route-side legacy porkchop/Lambert grid implementation; logistics flight planning is now estimator-only and guarded by formula tests.
- Logistics arrivals, including launch-vehicle/facility lift transfers, now advance matching vanilla `Deliver` contract objectives after cargo is added to the destination.

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

## Legacy Rule UI, Route Popup, And Status

- Updated legacy outbound rule wording to describe reserve behavior more accurately.
- Allowed legacy outbound rules with a reserve value of `0` instead of auto-deleting them.
- Added request status text for blocked pending states such as missing spacecraft, missing launch vehicle quota, missing fuel, cooldowns, and active planning.
- Added support for inbound in-transit status to include vehicle and arrival information where available.
- Matched more of the logistics UI text and section styling to the stock object-info UI.
- Reverted problematic button color/flash changes while preserving improved text styling work.
- Added small hover tooltips for the popup title body's quick-swap icon buttons.
- Skipped asteroid/comet bodies and their orbits in the popup title quick-swap menu while preserving moon targets.
- Tightened the route destination picker to hide destroyed, hidden, proxy, helper-orbit, duplicate-id, and dead/pushed asteroid/comet targets while keeping live asteroid/comet bodies.

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
  - `dotnet build .\LogisticsMod\LogisticsMod.csproj -c Release -p:GameDir="<Solar Expanse install>"`
- The built DLL is deployed to:
  - `<Solar Expanse install>\BepInEx\plugins\logisticsmod\LogisticsMod.dll`
  - `.\LogisticsMod.dll` for local manual testing

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
- Reduced popup scroll overhead by making decorative labels/row backgrounds non-raycast and letting the smooth-scroll helper sleep while idle.
- Restored compact bordered route editor action buttons and grouped the route lane, health status, and resource quick-click icons into a summary panel.
- Added fixed-height virtualized row pooling for route resource and route traffic tables, and restored stronger smooth wheel scrolling now that dense lists render fewer live rows.
- Normalized all-caps spacecraft and launch vehicle display names so assets such as `EAGLE` render as `Eagle`.
- Changed route convoy planning so resources waiting for a full load no longer reserve convoy capacity from other shippable resources, shortened route status text, and hardened orbit/body matching for direct drop routes.
- Added icon-only title-bar body switch buttons for nearby parent/child bodies and their orbits so the open Logistics popup can swap focus without closing.
- Added a compact fuel-used column to route traffic rows so each flight shows the fuel burned for its active leg separately from shipped cargo.
- Replaced route traffic fuel cache lookups with immediate transfer-grid calculation and added per-route spacecraft-type Fast/Optimal defaults, with Optimal as the fallback and solar-sail craft forced to Optimal.
- Added inline Fast/Optimal controls to assigned spacecraft rows and a single type-level `Default plan` row in the count editor so visible ship stacks switch together instead of per craft.
- Fixed active route traffic fuel refresh so it falls back to the flight cargo manifest when older/in-flight craft records do not carry their cargo amount, preventing fast flights from being recomputed as nearly empty.
- Prevented launched route traffic fuel, arrival, and duration from changing during later plan changes or convoy normalization; only future planned flights may be refreshed.
- Matched route Fast/Optimal porkchop gating to vanilla spacecraft type effective delta-v instead of rebuilding it from tank capacity and cargo mass.
- Limited the Sun/Solar Orbit title-bar switcher to vanilla-style primary solar-system clusters and sorted switcher bodies by orbital distance from their parent.
- Moved route editor Back and Pause/Resume controls into a sticky popup subheader outside the scroll area, with the compact route lane centered between them.
- Suppressed child orbit duplicates in crowded body switcher families while keeping the current body's own orbit visible.
- Enriched the sticky route subheader with body icons and removed the duplicate route header from the scroll area.
- Removed the redundant route editor status/resource summary block so route planners open directly into the editable resource table.
- Added the route status dot to the sticky route subheader, moved Back beside Pause/Resume on the right, and gave Pause an amber warning treatment.
- Refined human route launch safety so launch pads, space elevators, standard launch vehicles, and assigned/reserved launch vehicles are crew-safe, while magnetic rails, rotary launchers, electromagnetic catapults, and stationary mass drivers remain cargo-only; space elevators now supersede fuel-burning launch support when available.
- Some Nike missions are still attempting incorrect cargo payloads.
- Current diagnostics are intended to show the full path from planner manifest to stock launch attempt:
  - intended cargo and fuel manifest,
  - post-cap cargo amount,
  - total payload versus ship capacity,
  - stock planner result,
  - final `CreateFly` manifest.
- The next log review should compare `LOGI-MANIFEST`, `LOGI-CAP`, `LOGI-SCHEDULE`, and `LOGI-LAUNCH` entries for the same route and ship.
- Route launch planning now sizes launch support against the loaded spacecraft mass, including outbound fuel and any return fuel carried from origin.
- Clicking a route traffic flight now opens its readout as a full route subpage instead of embedding it inside the flight list.
- Same-fuel routes now treat assigned spacecraft as tankers: they can launch with a full tank, deliver surplus tank fuel, and use spare cargo capacity for extra fuel while preserving return reserves.
- Route resource editing now opens on Target by default and shows Target before Keep.
- Allowed human-containing route manifests to dispatch once they are at least half full by mass, while ordinary cargo routes continue to prefer full or final-partial loads.
- Kept plain Launch Pad facilities as passive launch-cost support unless vanilla exposes a fake launch vehicle for standalone route launch support.
- Removed post-load route planning so opening an autosave or manual save restores logistics state without immediately creating new dispatches.
