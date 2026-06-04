# InfoPanel Logistics Popup

## Title Body Switcher

- The popup title bar can show icon-only black buttons that swap the open logistics popup to nearby bodies.
- Normal body context should show the local family: parent body/orbit, current body/orbit, and direct child bodies/orbits.
- Crowded moon families should keep the current planet's orbit visible, but show direct child bodies without their orbit duplicates. Clicking the child body can then reveal that child's own orbit.
- Skip asteroid/comet bodies and their orbit entries in the title body switcher, including pushed/destroyed asteroids. Keep `Moons` targets such as Phobos and Deimos.
- Solar root context is special. The Sun/Solar Orbit has too many descendants, so the switcher should show only vanilla-style primary solar-system clusters, matching the Objects browser grouping rather than every child and low orbit.
- Sort switcher targets by distance from their parent. Use `DistanceToCentralObjectAu` first, then `DistanceToSunInAU`, with vanilla object list order as a stable fallback.
- Icon-only switcher buttons should show a small hover tooltip with the resolved target name. Orbit targets must remain distinguishable from body targets, for example `Earth` versus `Earth Orbit`.

## Route Destination Picker

- Build the route destination list from vanilla-search-visible, player-logistics objects: discovered, not destroyed, not hidden-to-discover, not the Sun, not proxy click targets, and with player `ObjectInfoData`.
- Include routeable body/orbit types only: planets, moons, dwarf planets, protoplanets, live asteroid/comet bodies, and normal orbit targets.
- Skip destroyed/pushed/space-elevator-consumed asteroid/comet entries, asteroid/comet orbit helper rows, orbit-of-orbit helper targets, and duplicate `ObjectInfo` records with the same id.
- Keep primary interstellar construction objects as possible logistics destinations, but hide their helper orbit rows.

## Route Flight Fuel

- Ghost logistics flights use `LogisticsFlightCalculator` for route timing/fuel estimates. Do not use the vanilla default-transfer cache for route traffic fuel; build the transfer grid immediately from the current endpoints, current game time, spacecraft window limits, cargo mass, and selected flight-plan mode.
- Route records do not have a single route-wide flight-plan mode. Each route stores a Fast/Optimal default per assigned spacecraft type, with Optimal as the fallback for missing values. Solar-sail craft are forced to Optimal.
- Assigned spacecraft rows in the route editor may show inline Fast/Optimal buttons for stack-wide changes by spacecraft type. The ship count editor should show one `Default plan` row for that spacecraft type, not per-craft plan controls.
- Fast mode should choose the earliest viable porkchop arrival that fits the ship's loaded-propellant effective delta-v. Do not gate porkchop candidates with `SpacecraftType.AvailableDeltaV`; vanilla's schedule UI derives its live gate from `exhaustV * ln((dry+cargo+fuel)/(dry+cargo))`, then rejects candidates that cannot fit in the tank. Optimal mode should choose the lowest delta-v viable porkchop cell, using earliest arrival only as the tie-breaker.
- Match vanilla's porkchop grid shape before comparing fuel. Vanilla Plan Mission uses 200 intervals, producing a 201x201 grid, and a normal one-window departure span. A cheap 10x10 scan or doubled departure window can miss high-dV Fastest cells and feed the fuel formula a falsely low dV.
- Freeze ghost-flight fuel, arrival, and duration records when the flight is dispatched. Route traffic rows display stored flight records; UI refreshes, time advancement, and Fast/Optimal plan changes must not recalculate active or planned traffic rows after dispatch.
- Verbose route diagnostics should log the selected porkchop input and the grouped vanilla convoy fuel side by side. Candidate cell logs should include `fuel`, `tank`, and `tankOk` so a mismatch can be separated into date/window selection versus tank/fuel rejection. For example, a five-Zeus Venus orbit to Earth orbit Fastest mission with `groupedMass=105000`, `exhaust=13750`, `pow=2.718`, and vanilla `dV1+dV2=93.653+93.228` should reproduce about `1436.8T`; if a player expects a different fuel number, compare the selected dV/date input before touching the fuel formula.
- When falling back to local math, deep-space, same-parent, and parent-child routes must not be capped to a small tank fraction. Keep the small tank-fraction budget only for local/short-hop heuristics where logistics is intentionally approximating.

## Route Facility Launch Options

- Route editors should show a wrapped 3x2 facility launch control without a separate `Facility Launch` heading. Always render every vanilla launch facility type as a labeled option: magnetic launch rails, launch pad, rotary launcher, space elevator, electromagnetic catapult, and stationary mass driver.
- Do not show the facility launch section for orbit route sources; orbit routes do not have surface facility launch policy.
- Do not add decorative top/bottom rule lines or a section background behind the facility launch option grid; the tile borders already define the control and should float on the route editor background. Keep the grid horizontally flush with the other route editor rows, with only vertical spacing around the tile rows.
- Prefer the source's actual facility `Sprite` for categories that exist at the route source, falling back to the fake launch vehicle TMP sprite only when needed. Categories missing at the source should still show a representative vanilla facility image when one resolves, falling back to a launch sprite or plain ASCII initials only if no safe image is available.
- Do not rely on fake launch vehicle rows alone to detect availability. Check the source body's finished, enabled launch facilities directly, then merge those categories with any fake launch vehicle rows so Launch Pad, Space Elevator, and similar buildings remain available when vanilla omits a fake row.
- Directly detected launch facility categories should reuse the category representative art; the built facility descriptor's own `Sprite` may point at unrelated UI art.
- For representative missing-option icons, search all vanilla launch facility descriptors in `AllFacility.ListNotEmpty`, not only fake-LV bonus descriptors. Classify them from descriptor `Name`, `ID`, and Unity object name with `_`/`-` treated as spaces; do not classify from `GetText(false)`, because vanilla returns sprite/link markup there.
- Space Elevator is the exception to the descriptor-image rule: use the launch-support/fake-LV TMP sprite path because its facility descriptor image may be a placeholder.
- Active available facility launch options should use the logistics theme's nominal green tint/border. Route-disabled available options should use critical red. Enabled-but-missing source categories should be greyed out and remain clickable so the route can be preconfigured before the facility exists; disabled missing categories should label themselves `Missing Disabled` and use warning amber. Keep the three-column rows stretched to the section width with equal outer padding, and give icons enough fixed box height/width so wide vanilla launch icons remain visible inside the compact options.
- Store disabled facility launch categories on the route record so save/load preserves route launch policy.
- Disabled facility categories filter only shared facility-backed route launch support. Assigned/reserved route launch vehicles remain controlled by the launch vehicle assignment UI.

## Route Sticky Subheader

- Route editor controls that must remain available while scrolling belong between the popup title bar and scroll body.
- The sticky route subheader should use the middle theme step between the popup title header and panel background, lead with the route status dot and icon-rich route header, then dock Back and Pause/Resume Route on the right.
- Passive popup refreshes from object-info/time updates should preserve the active route editor by route id. Explicit Back, popup close, object switch, route picker, or missing route fallback should clear that route context and return to the route list.
- While the main route editor is open, game-time changes should refresh that route editor in place on a short throttle and preserve scroll position. Do not run this live refresh while a route picker, count editor, or resource amount editor is open.
- Pause Route should use a warning/amber treatment; Resume Route should use the positive/green treatment.
- Do not repeat the route header or route status/resource summary inside the scroll body; the route editor should start with the editable resource actions/table.
- Hide the route subheader on the main route overview and non-route popup states.
- Empty spacecraft and launch vehicle route editor sections should render a `None assigned` row using the same table row background and primary text as normal asset rows, with icon/name/ready/qty slots but no header or zero values.
