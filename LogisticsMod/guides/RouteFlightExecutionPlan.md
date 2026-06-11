# Route Flight Execution Plan

## Purpose

This is the implementation checklist for `RouteFlightPerformance.md`. It breaks the performant logistics flight planner into small, verifiable steps.

The goal is not to build a perfect mission planner. The goal is to remove normal route scheduling's dependency on vanilla 201 by 201 porkchop scans while keeping believable Fast and Optimal route records.

## Ground Rules

- Normal route ticks must not build a vanilla porkchop grid.
- Routes default to `Optimal`; `Fast` is used only when the player chooses it.
- Solar-powered spacecraft always resolve to `Optimal`, matching vanilla behavior.
- `Fast` may prefer time, but it must not spend fuel that is needed for a required return reserve.
- Return reserve is required only when the destination does not have enough of the same fuel type for the ship to fly back.
- Return launch support is abstracted for the first implementation. Reserve flight fuel only.
- First pass should not add route-plan caching. The estimator should be O(1) or small-N without cache.
- Frozen dispatched route records remain frozen after UI refreshes, time advancement, and later mode changes.
- Expected estimates should generally land within about 10-20% of captured vanilla-like cases before deeper tuning.

## Current Implementation Status

The first implementation pass is in place:

- Hohmann, bi-elliptic, high-energy Fast, and MIMA helper equations exist in `LogisticsVanillaMissionMath`.
- Normal `CalculateSoonestOptimalFlight` routing uses the logistics estimator and has no route-side `TryCalculateInstantPorkchop` implementation.
- Earth-Moon/body-moon delta-v first checks vanilla `ObjectInfo.startTargetDVMinForMoonMooon` data and keeps the current economic moon-case multiplier.
- Fast candidate selection is small-N and receives a per-leg `maxFlightFuel` budget.
- Return legs are planned as `Optimal` first; outbound Fast planning receives the tank budget left after required return reserve when destination refuel is unavailable.
- The route-side legacy porkchop/Lambert grid implementation has been removed. Calibration should use captured vanilla mission diagnostics and formula cases.
- Formula tests cover the equation helpers and source-level route planner invariants.

## Research Output To Carry Into Code

Primary PyKEP/kep3 equations selected for the mod:

- Hohmann transfer from `basic_transfers.cpp`:
  `dv_total = abs(vt1 - v1) + abs(v2 - vt2)`,
  `transfer_seconds = pi * sqrt((r1 + r2)^3 / (8 * mu))`.
- Bi-elliptic transfer from `basic_transfers.cpp`:
  keep as future optional route estimate, not first-pass behavior.
- MIMA from `mima.cpp`:
  use for future low-thrust/constant-acceleration feasibility, with tests added now.
- Lambert from `lambert_problem`:
  keep as point-solve fallback only; never as a 2D grid during normal scheduling.

Local mod equations selected for first implementation:

- `Optimal`: Hohmann where endpoints share a central body; existing local/Earth-Moon/body-moon special cases otherwise.
- `Fast`: small fixed factors `[0.85, 0.70, 0.55, ship MinFlightTimeHohRel]` over the Optimal travel time, with `delta_v = optimal_delta_v * compression^1.55`.
- Fuel: keep the existing vanilla-compatible propellant formula.
- Feasibility: loaded propellant effective delta-v, tank capacity, loaded fuel, solar Optimal-only behavior, and return reserve.

## Step 1: Baseline Current Behavior

Goal:

Establish what the mod did before replacing the porkchop path, so tuning has a baseline.

Implementation:

- Run the existing formula test project.
- Capture the current status of the Venus-to-Earth Zeus cases, Mars-to-Earth Nike cases, Earth-Moon defaults, local-route behavior, and Fast/Optimal selection tests.
- Note any existing failures separately from new planner work.
- Confirm the current lag-prone route path reports checklist blockers without any route-side porkchop/Lambert grid implementation.

Confirmation:

- Existing formula tests either pass or have documented pre-existing failures.
- The captured numbers that will drive tuning are written down in the test output or comments.
- There is a known baseline before planner behavior changes.

## Step 2: Add Planner Request And Result Types

Goal:

Create a clean boundary where logistics flight planning receives one immutable input snapshot and returns one frozen output record.

Implementation:

- Add a request shape for route endpoints, resolved calculation endpoints, current game time, mode, ship values, cargo mass, fuel type, destination fuel amount, solar flag, and route kind.
- Add a result shape for success, block reason, departure, arrival, travel days, estimated delta-v, available loaded delta-v, outbound fuel, return reserve fuel, total reserved fuel, route kind, mode, and confidence/source.
- Add an optional return-reserve shape that records destination refuel availability, return fuel required, and whether the outbound tank must carry that reserve.
- Build the request once from route scheduling state before entering flight math.
- Do not let the planner read live UI state or mutable route editor state after the request is assembled.

Confirmation:

- Formula tests can construct planner requests without opening logistics UI.
- A dispatched route record can be populated from the result without recomputing flight math.
- The request contains enough information to decide return reserve without reaching back into route state.

## Step 3: Preserve Cheap Scheduling Blockers

Goal:

Avoid any flight math when the route is already blocked by checklist conditions.

Implementation:

- Keep route active/paused checks before planner calls.
- Validate source, destination, resource, amount, inventory, destination acceptance, assigned spacecraft, cargo capacity, and source launch support before planner calls.
- Read destination same-type fuel amount as a cheap data input, but do not perform return flight math until the route passes the basic blockers.
- Add diagnostics that can show a route was blocked before flight planning.

Confirmation:

- A route with missing source launch support reports the launch block without invoking Hohmann, Lambert, or old porkchop logic.
- A route with no source inventory reports the inventory block without invoking flight math.
- The original lag reproduction should no longer spend heavy work on a route that cannot dispatch for checklist reasons.

## Step 4: Route Classification And Special Cases

Goal:

Preserve the route families that should not need interplanetary planning.

Implementation:

- Reuse or extract the current route classification: same object, local orbit, Earth-Moon/body-moon, same parent, parent-child, constant acceleration, and interplanetary.
- Keep same-object routes trivial.
- Keep local surface/orbit and orbit/surface estimates close to current behavior.
- Keep constant-acceleration craft on their existing non-porkchop model.
- Force solar-powered craft to `Optimal` before candidate selection.

Confirmation:

- Same-object and local-route tests do not call Lambert or porkchop code.
- Solar-powered craft ignore `Fast` and return `Optimal`.
- Constant-acceleration craft still use the expected non-porkchop behavior.

## Step 5: Research Body-Moon Constants

Goal:

Decide whether body-to-moon and moon-to-body routes should use confirmed vanilla constants or a formula fallback.

Implementation:

- Inspect vanilla managed code and existing mission data for Earth-Moon and other body-moon hard-coded values.
- If stable values exist, create a small internal table for confirmed pairs only.
- If values are not confirmed, use the Hohmann-style estimate with route-kind clamps.
- Keep the table narrow; do not invent constants for unverified pairs.

First-pass finding:

- Vanilla exposes moon-case rows on `ObjectInfo.startTargetDVMinForMoonMooon`, so the mod should read those rows instead of maintaining its own duplicate constants.
- Use the current `Economic.EarthMoonCaseMultiDeltaVAfterChange` multiplier when a vanilla row is found.
- Keep a mod-owned precalc table out of the first implementation unless we find a stable route family that vanilla does not expose.

Confirmation:

- Earth-Moon keeps its existing expected travel time and fuel behavior.
- Any body-moon precalc entry has a documented vanilla source or captured formula case.
- Unknown body-moon pairs fall back cleanly instead of pretending to be confirmed.

## Step 6: Implement Optimal Hohmann Estimate

Goal:

Replace normal Optimal route planning with a cheap fuel-biased estimate.

Implementation:

- For same-parent, parent-child, and interplanetary routes, compute the Hohmann-style baseline from endpoint radii or semi-major axes and the central body's gravitational parameter.
- Use the PyKEP/kep3 Hohmann equations exactly:
  `v1 = sqrt(mu / r1)`,
  `v2 = sqrt(mu / r2)`,
  `vt1 = sqrt(mu / r1 * (2 * r2 / (r1 + r2)))`,
  `vt2 = sqrt(mu / r2 * (2 * r1 / (r1 + r2)))`,
  `transfer_seconds = pi * sqrt((r1 + r2)^3 / (8 * mu))`.
- Convert the baseline delta-v into fuel using the existing loaded mass and exhaust velocity fuel formula.
- Apply calibrated route-kind multipliers for endpoint quirks, inclination, capture, and insertion only where tests show they are needed.
- Respect loaded propellant effective delta-v, tank capacity, loaded fuel, cargo mass, and solar rules.

Confirmation:

- `Optimal` picks the lowest-fuel candidate available to its route family.
- Estimates for captured cases land within the 10-20% target or have a documented reason for additional calibration.
- Normal route scheduling has no `TryCalculateInstantPorkchop` path for an `Optimal` plan.

## Step 7: Implement Return Fuel Reserve

Goal:

Make round-trip feasibility part of the original dispatch decision.

Implementation:

- Estimate return fuel from destination back to source with zero return cargo unless a future route explicitly says otherwise.
- Use `Optimal` for the return reserve by default.
- Compare destination inventory of the same fuel type against the return fuel estimate.
- If destination same-type fuel is insufficient, require the outbound tank to reserve return fuel.
- Reject outbound candidates when outbound fuel plus required return reserve exceeds loaded fuel or tank capacity.
- Store outbound fuel, return reserve fuel, and total reserved fuel on the result.

Confirmation:

- A route where the destination has enough same-type fuel does not reserve return fuel in the outbound tank.
- A route where the destination lacks enough same-type fuel reserves return fuel before accepting the outbound plan.
- `Fast` candidates cannot consume required return reserve.
- The blocked reason clearly explains return reserve failure.

## Step 8: Implement Fast Small-N Candidate Planning

Goal:

Make Fast time-biased without turning it into "burn all fuel."

Implementation:

- Start from the Optimal baseline.
- Generate a small fixed set of faster time-of-flight candidates, such as 100%, 85%, 70%, 55%, and a ship/mode minimum.
- For first pass, use `[0.85, 0.70, 0.55, ship MinFlightTimeHohRel]`; the baseline Optimal candidate is already known.
- Estimate fast candidate delta-v with `candidate_delta_v = optimal_delta_v * (optimal_days / candidate_days)^1.55`.
- Estimate each candidate's delta-v and fuel from the high-energy transfer curve.
- Apply loaded delta-v, tank, loaded fuel, solar, and return-reserve gates.
- Rank viable candidates with a diminishing-return rule based on extra total reserved fuel per day saved.
- Prefer cheaper near-fast candidates when time savings are small or fuel penalties are extreme.

Confirmation:

- `Fast` normally arrives earlier than `Optimal` when the ship can afford it.
- `Fast` skips tiny time savings with huge fuel penalties.
- `Fast` never chooses a candidate only because it is the highest fuel burn that fits.
- Candidate count remains fixed and small.

## Step 9: Add Optional Small Lambert Refinement

Goal:

Keep a safety valve for unusual geometry without returning to porkchop grids.

Implementation:

- Add refinement only if the cheap estimate cannot confidently classify or rank a route.
- Limit refinement to a small fixed number of time-of-flight samples, such as 3 to 9.
- Treat Lambert as a point solve: start position, target position, time of flight, and central body gravity.
- Do not create a two-dimensional departure/arrival grid.
- Do not retain a route-side vanilla porkchop comparison path. Use opt-in vanilla mission diagnostics and captured cases for calibration.

Confirmation:

- The fallback never performs a 201 by 201 scan.
- Normal route ticks do not use fallback unless confidence rules require it.
- Diagnostics can distinguish cheap estimates and any future small Lambert refinement without reintroducing a route-side porkchop comparison.

## Step 10: Wire Planner Into Route Dispatch

Goal:

Use the new planner for normal logistics dispatch without changing frozen-record semantics.

Implementation:

- Keep normal route scheduler calls on the estimator-backed `CalculateSoonestOptimalFlight` path.
- Preserve launch fuel and route launch support behavior already handled outside flight math.
- Store planner output on the ghost flight record at dispatch.
- Keep active and planned route rows reading stored records instead of recalculating.
- Keep the old route-side porkchop code deleted; do not reintroduce it as a temporary comparison path.

Confirmation:

- Dispatch creates a frozen route flight record with departure, arrival, duration, outbound fuel, return reserve, total fuel, mode, and route kind.
- Changing a route's Fast/Optimal setting later does not mutate already-dispatched records.
- UI refresh and time advancement do not recalculate active flight records.

## Step 11: Update Formula Tests

Goal:

Turn the design rules into repeatable regression coverage.

Implementation:

- Add tests for request/result creation.
- Add direct equation tests for Hohmann, high-energy Fast scaling, and MIMA acceleration/mass.
- Add tests for default `Optimal` mode and player-selected `Fast`.
- Add tests proving solar ships always resolve to `Optimal`.
- Add tests for same-object, local, Earth-Moon, body-moon fallback/precalc, same-parent, parent-child, interplanetary, and constant-acceleration routes.
- Add return-reserve tests for destination has fuel, destination lacks fuel, and Fast cannot spend reserve.
- Replace old "route porkchop grid shape" tests with removal tests that fail if route-side porkchop/Lambert grid code returns.
- Add performance-shape tests that assert candidate counts remain small.

Confirmation:

- Formula tests pass without relying on a 201 by 201 grid for normal route planning.
- Captured Zeus and Nike scenarios are within the 10-20% acceptance target or marked for calibration.
- Return reserve, solar behavior, and frozen-record behavior are covered.

## Step 12: Tune Against Captured Cases

Goal:

Make the cheap model feel close enough to the current game while preserving performance.

Implementation:

- Tune route-kind multipliers and Fast diminishing-return knee against captured formula cases.
- Prioritize preserving relative behavior over exact vanilla porkchop output.
- Ensure `Optimal` remains fuel-biased and `Fast` remains time-biased.
- Keep tuning values centralized so future balance changes are obvious.

Confirmation:

- Venus-to-Earth five-Zeus Fast and Optimal estimates are within the target range or have a documented reason for exception.
- Mars-to-Earth one-Nike Fast and Optimal estimates are within the target range or have a documented reason for exception.
- Earth-Moon and local routes remain close to existing behavior.
- Fast/Optimal differences are visible and sensible to the player.

## Step 13: Profile The Original Lag Reproduction

Goal:

Verify the route scheduler no longer spikes when a route cannot dispatch.

Implementation:

- Recreate the reported logistics route scenario as closely as possible: carbon from Earth to Luna with a Stratos assigned.
- Test variants where the destination lacks return fuel, lacks return launch support, or source launch support is unavailable.
- Enable route diagnostics or profiler instrumentation around planner calls.
- Confirm the route reaches cheap block reasons before expensive math when it cannot dispatch.

Confirmation:

- The route can be paused, deleted, or unassigned without a noticeable performance cliff because it is not repeatedly running heavy planning work.
- A blocked route reports a stable block reason.
- Normal route tick planner work is O(1) or small-N.
- No vanilla porkchop grid is run during normal scheduling.

## Step 14: Documentation And Cleanup

Goal:

Leave the codebase understandable after the planner replacement.

Implementation:

- Update `InfoPanel_LogisticsPopup.md` if player-facing Fast/Optimal behavior text changes.
- Keep `RouteFlightPerformance.md` as the design reference.
- Keep this execution plan updated with any implementation order changes.
- Remove or clearly mark stale comments that still imply normal route traffic uses vanilla porkchop grids.
- Do not update Nexus files or publish packages as part of this work.

Confirmation:

- Internal docs match implemented behavior.
- User-facing guide text does not promise exact vanilla porkchop behavior if that is no longer true.
- Publishing files remain untouched.

## Final Definition Of Done

The route flight planner is considered complete when:

- Normal logistics scheduling never runs the vanilla 201 by 201 porkchop grid.
- Routes still default to `Optimal`, and solar ships always resolve to `Optimal`.
- `Fast` is time-biased but protects sane fuel economics and required return reserve.
- Destination same-type fuel determines whether return fuel must be carried from the source.
- Same/local/Earth-Moon/body-moon behavior remains close to current values.
- Captured Zeus and Nike cases land within about 10-20% or have documented calibration exceptions.
- Formula tests cover planner modes, route families, return reserve, solar ships, frozen records, and small-N performance shape.
- The reported lag scenario no longer causes repeated heavy flight planning while blocked.
