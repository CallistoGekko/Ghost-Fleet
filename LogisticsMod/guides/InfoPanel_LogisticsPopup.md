# InfoPanel Logistics Popup

## Title Body Switcher

- The popup title bar can show icon-only black buttons that swap the open logistics popup to nearby bodies.
- Normal body context should show the local family: parent body/orbit, current body/orbit, and direct child bodies/orbits.
- Crowded moon families should keep the current planet's orbit visible, but show direct child bodies without their orbit duplicates. Clicking the child body can then reveal that child's own orbit.
- Skip asteroid/comet bodies and their orbit entries in the title body switcher, including pushed/destroyed asteroids. Keep `Moons` targets such as Phobos and Deimos.
- Solar root context is special. The Sun/Solar Orbit has too many descendants, so the switcher should show only vanilla-style primary solar-system clusters, matching the Objects browser grouping rather than every child and low orbit.
- Sort switcher targets by distance from their parent. Use `DistanceToCentralObjectAu` first, then `DistanceToSunInAU`, with vanilla object list order as a stable fallback.
- Icon-only switcher buttons should show a small hover tooltip with the resolved target name. Orbit targets must remain distinguishable from body targets, for example `Earth` versus `Earth Orbit`.

## Popup Layering

- Vanilla `UIManager.Open(...)` keeps normal windows on `Current` and alert/trivia/popup windows on `Current2`. The logistics popup should live above normal object-info/mission windows, but below active vanilla alert windows.
- Do not call `SetAsLastSibling()` directly when opening or swapping the logistics popup. Use the logistics popup layer helper so the root moves immediately before the active `Current2` alert/trivia transform when one exists, and otherwise rises to the top of the normal UI stack.
- Patch `UIManager.Open(...)` with a postfix that re-applies the logistics popup layer while it is open. This catches alerts opened after the logistics popup, so warning/confirmation blockers are not hidden behind the logistics panel.

## Icon Sizing

- Logistics UI should bound custom icons at the render site, not by editing source sprites or mod assets.
- When a real `Sprite` is available, render it through a fixed layout slot with `Image.preserveAspect = true`. Use this for object/location icons, queued module cargo, and facility launch tiles.
- When only TMP sprite markup is available, wrap the sprite markup with the logistics icon-size helper before combining it with names, counts, or button text. This covers resources, spacecraft, launch vehicles, compact route strips, and flight cargo labels.
- Route status text may contain raw resource ids from planner failure reasons, such as `id_resource_fuel`. Clean status text through the resource-label helper before display so these render as the normal icon/name label.
- Do not wrap an icon string more than once. The helper should leave already-sized TMP markup alone.

## Route Destination Picker

- Build the route destination list from vanilla-search-visible, player-logistics objects: discovered, not destroyed, not hidden-to-discover, not the Sun, not proxy click targets, and with player `ObjectInfoData`.
- Include routeable body/orbit types only: planets, moons, dwarf planets, protoplanets, live asteroid/comet bodies, and normal orbit targets.
- Skip destroyed/pushed/space-elevator-consumed asteroid/comet entries, asteroid/comet orbit helper rows, orbit-of-orbit helper targets, and duplicate `ObjectInfo` records with the same id.
- Keep primary interstellar construction objects as possible logistics destinations, but hide their helper orbit rows.

## Route Flight Fuel

- Ghost logistics flights use `LogisticsFlightCalculator` for route timing/fuel estimates. Normal route scheduling should use the logistics route estimator, not the vanilla default-transfer cache and not a 201x201 vanilla porkchop grid.
- Route records do not have a single route-wide flight-plan mode. Each route stores a Fast/Optimal default per assigned spacecraft type, with Optimal as the fallback for missing values. Solar-sail craft are forced to Optimal.
- Assigned spacecraft rows in the route editor may show inline Fast/Optimal buttons for stack-wide changes by spacecraft type. The ship count editor should show one `Default plan` row for that spacecraft type, not per-craft plan controls.
- Optimal mode should use the fuel-biased logistics estimate: local special cases first, Earth-Moon/body-moon special cases when available, and Hohmann-style minimum-energy estimates for suitable shared-center routes.
- Fast mode should evaluate a small fixed set of faster candidates from the Optimal baseline, then choose a time-biased candidate only when the fuel cost buys meaningful time savings. Do not treat Fast as "burn all fuel." Do not gate candidates with `SpacecraftType.AvailableDeltaV`; use the loaded-propellant effective delta-v from `exhaustV * ln((dry+cargo+fuel)/(dry+cargo))`, then reject candidates that cannot fit in the tank or the return-protected per-leg fuel budget.
- Return fuel should be planned before accepting an outbound Fast candidate. Return reserve uses Optimal by default. If the destination has enough same-type fuel for the return leg, reserve that fuel at the destination; otherwise, protect enough tank capacity at launch for both outbound and return fuel.
- Freeze ghost-flight fuel, arrival, and duration records when the flight is dispatched. Route traffic rows display stored flight records; UI refreshes, time advancement, and Fast/Optimal plan changes must not recalculate active or planned traffic rows after dispatch.
- Verbose route diagnostics should log the selected route leg input and the grouped vanilla convoy fuel side by side. Candidate diagnostics should include `fuel`, `tank`, and any return-protected `maxFlightFuel` budget so a mismatch can be separated into route estimate selection versus tank/fuel rejection. Normal logistics estimates are allowed to be approximate and should be tuned against captured cases from opt-in vanilla mission diagnostics.
- When falling back to local math, deep-space, same-parent, and parent-child routes must not be capped to a small tank fraction. Keep the small tank-fraction budget only for local/short-hop heuristics where logistics is intentionally approximating.
- Route ghost dispatch should still prefer full cargo loads for ordinary cargo, but human-containing mixed manifests may launch once they are at least half full by mass. This lets source-limited crew routes move useful groups of people plus small material loads without waiting for a perfect 100% cargo fill.
- Route resource priority controls dispatch urgency as well as ordering. Normal and Low cargo should wait for a full load or final shortfall. High cargo may launch a partial convoy once the actual manifest reaches 10% of ship cargo capacity. Critical cargo may launch as soon as any positive amount can fit; other available cargo can ride along, but it should not open a partial launch by itself.
- Loading a save must not create new route dispatches. Save load may restore ledger data and visuals, but new logistics flights should wait for a real in-game day tick so loading an autosave does not mutate the expected save state.
- Route traffic rows should compact repeated module cargo into icon-count stacks, while queued module rows and detailed flight manifests should soften all-caps vanilla module names for readability.
- Same-body surface-to-orbit routes may lift queued module cargo directly with route launch support or assigned launch vehicles. Do not require an assigned spacecraft for satellite/module deployment to the local orbit target; non-local module routes still need spacecraft convoy handling.

## Route Facility Launch Options

- Route editors should show a wrapped 3x2 facility launch control without a separate `Facility Launch` heading. Always render every vanilla launch facility type as a labeled option: magnetic launch rails, launch pad, rotary launcher, space elevator, electromagnetic catapult, and stationary mass driver.
- Do not show the facility launch section for orbit route sources; orbit routes do not have surface facility launch policy.
- Do not add decorative top/bottom rule lines or a section background behind the facility launch option grid; the tile borders already define the control and should float on the route editor background. Keep the grid horizontally flush with the other route editor rows, with only vertical spacing around the tile rows.
- Prefer the source's actual facility `Sprite` for categories that exist at the route source, falling back to the fake launch vehicle TMP sprite only when needed. Categories missing at the source should still show a representative vanilla facility image when one resolves, falling back to a launch sprite or plain ASCII initials only if no safe image is available.
- Do not rely on fake launch vehicle rows alone to detect availability. Check the source body's finished, enabled launch facilities directly, then merge those categories with any fake launch vehicle rows so Launch Pad, Space Elevator, and similar buildings remain available when vanilla omits a fake row.
- For route surface lift support, direct built facility entries should replace their own fake launch vehicle entries. The fake row may be on cooldown or otherwise not `ready`, but an enabled facility such as Rotary Launcher should still provide route lift support through the built-facility path.
- Directly detected launch facility categories should reuse the category representative art; the built facility descriptor's own `Sprite` may point at unrelated UI art.
- For representative missing-option icons, search all vanilla launch facility descriptors in `AllFacility.ListNotEmpty`, not only fake-LV bonus descriptors. Classify them from descriptor `Name`, `ID`, and Unity object name with `_`/`-` treated as spaces; do not classify from `GetText(false)`, because vanilla returns sprite/link markup there.
- Space Elevator is the exception to the descriptor-image rule: use the launch-support/fake-LV TMP sprite path because its facility descriptor image may be a placeholder.
- Active available facility launch options should use the logistics theme's nominal green tint/border. Route-disabled available options should use critical red. Enabled-but-missing source categories should be greyed out and remain clickable so the route can be preconfigured before the facility exists; disabled missing categories should label themselves `Missing Disabled` and use warning amber. Keep the three-column rows stretched to the section width with equal outer padding, and give icons enough fixed box height/width so wide vanilla launch icons remain visible inside the compact options.
- Store disabled facility launch categories on the route record so save/load preserves route launch policy.
- Disabled facility categories filter only shared facility-backed route launch support. Assigned/reserved route launch vehicles remain controlled by the launch vehicle assignment UI.
- Human cargo is crew-safe on launch pads, space elevators, standard launch vehicles, and assigned/reserved launch vehicles. Only violent facility launch categories should block human cargo: magnetic launch rails, rotary launchers, electromagnetic catapults, and stationary mass drivers.
- Space elevators supersede other surface launch support when available and route-enabled because they provide zero-fuel surface lift. Plain launch pads remain passive launch-cost support through the normal rocket bonus path; only vanilla fake-LV launch-pad support should be treated as standalone route launch support.
- Launching a loaded spacecraft should choose one launch support option that can lift the whole payload. Do not split a single spacecraft launch across a reserved launch vehicle plus a facility launcher; multiple labels in a flight record should only mean multiple spacecraft in that grouped flight used different support options.

## Route Sticky Subheader

- Route editor controls that must remain available while scrolling belong between the popup title bar and scroll body.
- The sticky route subheader should use the middle theme step between the popup title header and panel background, lead with the route status dot and icon-rich route header, then dock Back and Pause/Resume Route on the right.
- Passive popup refreshes from object-info/time updates should preserve the active route editor by route id. Explicit Back, popup close, object switch, route picker, or missing route fallback should clear that route context and return to the route list.
- While the main route editor is open, game-time changes should refresh that route editor in place on a short throttle and preserve scroll position. Do not run this live refresh while a route picker, count editor, or resource amount editor is open.
- Pause Route should use a warning/amber treatment; Resume Route should use the positive/green treatment.
- Do not repeat the route header or route status/resource summary inside the scroll body; the route editor should start with the editable resource actions/table.
- Hide the route subheader on the main route overview and non-route popup states.
- Empty spacecraft and launch vehicle route editor sections should render a `None assigned` row using the same table row background and primary text as normal asset rows, with icon/name/ready/qty slots but no header or zero values.
