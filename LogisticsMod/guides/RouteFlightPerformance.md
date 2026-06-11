# Route Flight Performance

## Purpose

Logistics route planning needs believable frozen flight records. It does not need the full vanilla Plan Mission search every time a route scheduler asks whether a convoy can leave.

The legacy route planner could call the vanilla-style Lambert porkchop path during normal route scheduling. That path samples 200 departure intervals by 200 arrival intervals, which means 201 by 201 candidate cells before selection. That is useful when a player is actively choosing a mission in the vanilla UI, but it is too heavy for background logistics traffic, especially when a blocked route can retry on repeated ticks.

The target design is to replace normal route scheduling with small, deterministic flight estimators that are good enough for logistics:

- Keep local, same-object, same-body orbit, Earth-Moon, and constant-acceleration special cases.
- Preserve the visible route semantics: `Optimal` is fuel-biased, `Fast` is time-biased within sane fuel economics.
- Produce frozen records containing departure date, arrival date, duration, delta-v, fuel, route kind, mode, confidence/source, and any blocked reason.
- Avoid 201 by 201 porkchop scans during normal route ticks.

This document is internal design guidance. It is not Nexus copy and does not describe already-shipped behavior unless explicitly called out as current state.

## Legacy State

Before the logistics route estimator, `LogisticsFlightCalculator` started with cheap route-kind estimates, then attempted `TryCalculateInstantPorkchop` for porkchop-capable routes. That method mirrors vanilla Plan Mission behavior:

- It resolves the route to vanilla calculation endpoints through `PMMissionParameter`.
- It calls the private vanilla `LambertPorkchop.ComputeLambert2` method through reflection.
- It scans `PorkchopIntervals = 200` for departures and arrivals, producing a 201 by 201 grid.
- `Fast` maps to vanilla `Fastest`: choose the earliest feasible arrival under the loaded propellant gate.
- `Optimal` maps to vanilla `Optimal`: choose the lowest delta-v feasible cell, using arrival date only as a tie-breaker.
- The cache is useful only for duplicate same-tick scans because the key includes the exact current game ticks.

This is accurate enough to match captured vanilla cases, but it is the wrong performance shape for background logistics. Route planning should first answer "can a convoy ship a resource on this route?" and only then spend work proportional to actual mission complexity.

## First-Pass Implementation State

Normal route flight calculation now uses the logistics estimator path:

- `Optimal` uses local special cases, vanilla Earth-Moon/body-moon data when available, and Hohmann-style estimates where endpoints share a central body.
- `Fast` evaluates a small fixed candidate set from the Optimal baseline.
- Candidate selection respects loaded delta-v, tank capacity, and an optional per-leg fuel budget.
- Return legs are estimated as `Optimal` by default before outbound planning. If the destination cannot refuel the same fuel type, outbound `Fast` receives only the tank budget left after protecting return fuel.
- The route-side legacy porkchop/Lambert grid implementation has been removed. Calibration should use captured vanilla numbers and opt-in vanilla mission diagnostics, not a hidden route scheduler grid.

## References

These sources support the shape of the replacement, not a direct dependency:

- [ESA PyKEP/kep3](https://github.com/esa/pykep) is a trajectory-design library with efficient Keplerian propagation and Lambert primitives. It is a good reference for the kind of orbital operations we want conceptually, even though the mod should stay self-contained.
- [PyKEP/kep3 Lambert documentation](https://esa.github.io/kep3/lambert.html) exposes Lambert solving as a single problem: start position, target position, time of flight, gravitational parameter, direction, and revolution count.
- Dario Izzo's [Revisiting Lambert's Problem](https://arxiv.org/abs/1403.2705) is the practical reference behind the PyKEP Lambert solver family. The key lesson for logistics is that Lambert itself is a point solve; the expensive porkchop is the repeated grid search around that solve.
- PyKEP/kep3 [`basic_transfers.cpp`](https://github.com/esa/pykep/blob/master/src/core_astro/basic_transfers.cpp) provides the exact Hohmann and bi-elliptic equations we can port as local math.
- PyKEP/kep3 [`mima.cpp`](https://github.com/esa/pykep/blob/master/src/core_astro/mima.cpp) provides a fast low-thrust feasibility approximation: MIMA.
- NASA's [Basics of Space Flight: Trajectories](https://science.nasa.gov/learn/basics-of-space-flight/chapter4-1/) describes Hohmann-style interplanetary transfers as energy-conscious transfer orbits.
- NASA's [Dawn FAQ](https://science.nasa.gov/mission/dawn/faq/) is useful for the user-facing distinction: Hohmann transfers conserve propellant, but they are not the fastest transfers.

## Equation Set

These are the equations to keep in the mod. They are small, local, and testable without Unity.

### Hohmann Transfer

Use this for `Optimal` between two near-circular orbits around the same central body.

```text
v1 = sqrt(mu / r1)
v2 = sqrt(mu / r2)
vt1 = sqrt(mu / r1 * (2 * r2 / (r1 + r2)))
vt2 = sqrt(mu / r2 * (2 * r1 / (r1 + r2)))

dv1 = abs(vt1 - v1)
dv2 = abs(v2 - vt2)
dv_total = dv1 + dv2

transfer_seconds = pi * sqrt((r1 + r2)^3 / (8 * mu))
```

Inputs:

- `r1`, `r2`: orbital radii from the shared central body, in meters.
- `mu`: gravitational parameter of the shared central body, `G * central_body_mass`, in meters cubed per second squared.

Game conversion:

- Convert `dv_total` from meters per second to the game's current route delta-v scale by dividing by `1000`.
- Convert `transfer_seconds` to days with `seconds / 86400`.

### Bi-Elliptic Transfer

Keep this as a future optional estimate, not first-pass route behavior.

```text
v1 = sqrt(mu / r1)
v2 = sqrt(mu / r2)

vt1 = sqrt(mu / r1 * (2 * rb / (r1 + rb)))
vt1a = sqrt(mu / rb * (2 * r1 / (r1 + rb)))

vt2 = sqrt(mu / rb * (2 * r2 / (rb + r2)))
vt2a = sqrt(mu / r2 * (2 * rb / (rb + r2)))

dv1 = abs(vt1 - v1)
dv2 = abs(vt2 - vt1a)
dv3 = abs(v2 - vt2a)
dv_total = dv1 + dv2 + dv3

transfer_seconds =
  pi * sqrt((r1 + rb)^3 / (8 * mu))
  + pi * sqrt((rb + r2)^3 / (8 * mu))
```

### High-Energy Fast Estimate

Use this for a small fixed candidate set when the player chooses `Fast`.

```text
candidate_days = optimal_days * factor
factor in [0.85, 0.70, 0.55, ship_min_flight_time_hohmann_relative]

compression = optimal_days / candidate_days
candidate_delta_v = optimal_delta_v * compression^fast_exponent
```

Initial tuning:

- `fast_exponent = 1.55`.
- Reject candidates that exceed loaded delta-v or tank fuel.
- Score candidates with the diminishing-return Fast rule instead of blindly taking the earliest one.

This is not a perfect trajectory solution. It is the logistics approximation that turns "shorter time costs more energy" into a stable O(1) candidate set.

### MIMA Low-Thrust Feasibility

Use this later for constant-acceleration or low-thrust feasibility checks. It is useful when a two-impulse transfer estimate exists and we want to know whether a low-thrust ship is too heavy for that transfer.

```text
dv = dv1 + dv2
dv_diff = -dv1 + dv2

aa = dot(dv, dv)
bb = dot(dv_diff, dv_diff)
ab = dot(dv, dv_diff)

required_acceleration =
  sqrt(aa + 2 * bb + 2 * sqrt(ab^2 + bb^2)) / time_of_flight_seconds

maximum_initial_mass =
  2 * max_thrust / required_acceleration
  / (1 + exp(-required_acceleration * time_of_flight_seconds / effective_exhaust_velocity))
```

First-pass use:

- Add formula tests now.
- Do not wire this into normal rocket routes until constant-acceleration behavior is isolated.

### Existing Rocket Fuel Formula

Keep using the vanilla-compatible logistics fuel formula:

```text
dry_plus_cargo = dry_mass + cargo_mass
loaded_delta_v = exhaust_velocity * ln((dry_plus_cargo + fuel_capacity) / dry_plus_cargo)

minimum_propellant =
  mass_to_fuel * (pow_variable^(delta_v / exhaust_velocity) - 1)

total_propellant =
  rounded vanilla total after minimum-propellant and leftover-fuel adjustment
```

This stays separate from the transfer equations. Transfer equations choose `delta_v` and time; the vanilla-compatible fuel math turns that delta-v into route fuel.

## Design Principle

Do not replace the vanilla porkchop grid with a smaller porkchop grid as the primary algorithm. That only changes the constant.

Instead, logistics should use closed-form and small-N candidate planning:

- Classify the route.
- Compute one baseline efficient transfer.
- Compute a small number of faster candidates when `Fast` is requested.
- Apply tank, loaded-mass, thrust, solar, and diminishing-return economics.
- Fall back to a tiny Lambert refinement only when the cheap estimate cannot confidently classify or rank the route.

The normal route tick should be O(1), or a small fixed number of candidates. It should never depend on a 40k-cell scan.

## Flight Plan Modes

Routes should default to `Optimal` until the player explicitly chooses `Fast`.

Solar-powered spacecraft are the special vanilla case: they must always resolve to `Optimal`. Timing comparisons for `Fast` do not apply to solar ships, and any player-facing Fast control should be ignored or hidden for those craft.

## Inputs

Future route planning should collect a single immutable request before doing flight math:

```text
Route endpoints:
  source ObjectInfo
  destination ObjectInfo
  resolved calculation source, if different
  resolved calculation destination, if different

Schedule:
  current game time
  route flight-plan mode: Optimal or Fast

Spacecraft:
  spacecraft type id
  dry mass
  cargo mass for this dispatch
  tank capacity
  loaded fuel available
  fuel type
  return-fuel reserve required flag
  return cargo mass, normally zero unless the route explicitly carries cargo back
  return route mode, normally Optimal unless a future route option says otherwise
  destination fuel amount for this fuel type
  exhaust velocity
  thrust
  solar-powered flag
  vanilla MinFlightTimeHohRel and MaxFlightTimeHohRel if still used as tuning inputs

Game-derived orbital values:
  central body
  gravitational parameter
  source radius or semi-major axis
  target radius or semi-major axis
  orbital periods
  relative inclination if available
  source/target canonical body ids
  route kind
```

The planner should not read live mutable UI state after the request is assembled. This keeps dispatched route rows reproducible and makes formula tests much easier to write.

## Outputs

The planner should return a single result shape for all route kinds:

```text
success
blocked reason, if any
departure date
arrival date
travel days
estimated delta-v
available loaded delta-v
outbound flight fuel
return reserve fuel, if required
total reserved flight fuel
launch fuel, if relevant
fuel type
route kind
flight-plan mode
confidence/source:
  local-special-case
  hohmann-estimate
  high-energy-estimate
  small-lambert-refinement
  blocked-before-flight-math
```

This output should be frozen into the route flight record at dispatch time. UI refreshes, route editor changes, and time advancement should display the stored record instead of recalculating active traffic.

## Return Fuel Reserve

If a route expects the same spacecraft to return after delivery, the planner must decide whether return fuel has to be carried from the source. This should be a hard feasibility gate, not a later warning.

Return reserve is required when the destination does not have enough of the same fuel type for the ship to fly back. If the destination can refuel the same fuel type for the return leg, the outbound dispatch does not need to reserve that return fuel in the ship's loaded tank.

The return reserve should be calculated before accepting an outbound candidate:

```text
outbound_fuel = fuel_for(source -> destination, outbound cargo mass, selected outbound mode)
return_fuel = fuel_for(destination -> source, return cargo mass, return mode)
destination_refuel_available = destination_fuel_amount >= return_fuel
return_reserve_required = !destination_refuel_available
total_reserved_fuel = outbound_fuel + (return_reserve_required ? return_fuel : 0)
```

The route should be blocked when `total_reserved_fuel` exceeds the loaded tank capacity or loaded available fuel, unless the spacecraft is solar-powered and the current solar rules intentionally bypass propellant reservation.

Return planning should usually be conservative:

- Use zero return cargo mass unless the route explicitly models cargo coming back.
- Use `Optimal` for the return reserve by default, because the return leg is repositioning capacity rather than delivering faster service.
- Use the same route-kind special cases as the outbound leg.
- Freeze both outbound fuel and return reserve into the dispatched record so the row can explain why the ship could or could not leave.
- Abstract return launch support for now. The first version should reserve flight fuel only; destination launch capacity can become a separate rule after the fuel planner is stable.

For `Fast`, the outbound leg may spend extra fuel only after the return reserve is protected. A fast outbound candidate that fits the tank by itself must still be rejected if it strands the craft without the required return fuel.

## Route Families

### Same Object

Same-object transfers should stay as a trivial logistics case. They should not call Lambert, Hohmann, or porkchop code.

Expected planner source: `local-special-case`.

### Local Orbit

Surface-to-orbit, orbit-to-surface, and nearby local orbit transfers should preserve existing local-route behavior. These routes are where intentional approximation is acceptable, and where small tank-fraction limits can remain if they are already part of the gameplay balance.

Expected planner source: `local-special-case`.

### Earth-Moon

Earth-Moon logistics is important enough to keep stable. The existing default values, such as roughly seven days and the current Earth-Moon delta-v estimate, should remain close unless a separate balance pass changes them.

Body-to-moon and moon-to-body routes can use the same idea when vanilla exposes stable values for those families. The first-pass implementation reads the vanilla `ObjectInfo.startTargetDVMinForMoonMooon` rows through the relevant parent body and applies the current `Economic.EarthMoonCaseMultiDeltaVAfterChange` multiplier. Prefer that confirmed game data over an invented mod table. Fall back to the Hohmann-style estimate with route-kind clamps when there is no confirmed vanilla row.

Expected planner source: `local-special-case`, confirmed body-moon precalc, or `hohmann-estimate` with clamps.

### Same Parent And Parent-Child

Routes between bodies sharing a parent, or between a body and its child, are the first place where Hohmann-style estimates are useful. The baseline should use the common central body's gravitational parameter and the endpoints' orbital radii or semi-major axes.

The minimum-energy transfer time is half the transfer ellipse period:

```text
a_transfer = (r1 + r2) / 2
tof_hohmann = pi * sqrt(a_transfer^3 / mu)
```

The co-planar circular-orbit delta-v baseline is:

```text
v1 = sqrt(mu / r1)
v2 = sqrt(mu / r2)
v_transfer_periapsis = sqrt(mu * (2 / r1 - 1 / a_transfer))
v_transfer_apoapsis = sqrt(mu * (2 / r2 - 1 / a_transfer))
dv1 = abs(v_transfer_periapsis - v1)
dv2 = abs(v2 - v_transfer_apoapsis)
dv_hohmann = dv1 + dv2
```

Inclination, eccentricity, capture/insertion, and vanilla endpoint quirks should be represented by calibrated multipliers, not by reintroducing a grid. Those multipliers can be route-kind-specific and tuned against current captured tests.

Expected planner source for `Optimal`: `hohmann-estimate`.

### Interplanetary

Interplanetary route planning should still use a patched-conic estimate, but logistics does not need to search an entire porkchop surface on every scheduler pass.

For `Optimal`, use the Hohmann-style baseline as the fuel-biased candidate. It should be stable and cheap.

For `Fast`, evaluate a small near-now arrival family derived from the Hohmann transfer:

```text
tof_candidates = [
  1.00 * tof_hohmann,
  0.85 * tof_hohmann,
  0.70 * tof_hohmann,
  0.55 * tof_hohmann,
  max(mode_or_ship_minimum_tof, 0.55 * tof_hohmann)
]
```

Each candidate estimates delta-v from the baseline transfer, the high-energy compression needed for a shorter time of flight, and the bad-window chase cost needed to intercept the target from a near-now departure. The chase cost should come from the target's orbital miss distance over the candidate travel time:

```text
phase_miss = target_arrival_angle - transfer_arrival_angle
miss_distance = target_orbit_radius * 2 * sin(abs(phase_miss) / 2)
bad_window_chase_delta_v = miss_distance / candidate_travel_time
candidate_delta_v = ideal_delta_v + high_energy_extra_delta_v + bad_window_chase_delta_v
```

Then apply loaded tank feasibility including any required return reserve. This keeps the calculation bounded while preserving the player's expectation that Fast arrives sooner than Optimal when the spacecraft can afford it, and that bad windows are expensive because the ship must chase the moving target.

Expected planner source for `Fast`: `high-energy-estimate + bad-window-chase-estimate`.

### Constant Acceleration

Spacecraft marked `NotUsePorkchope` should stay outside the porkchop replacement. These ships already represent a different flight model, so their travel time and fuel estimates should continue through the constant-acceleration path.

Expected planner source: `local-special-case` or a future explicit `constant-acceleration-estimate`.

## Optimal Mode

`Optimal` means fuel-biased, not "run vanilla's full optimal porkchop."

For Hohmann-suitable routes, `Optimal` should:

- Use the route-family baseline transfer.
- Prefer lower delta-v over earlier arrival.
- Use earliest arrival only as a tie-breaker between near-equal fuel candidates.
- Respect loaded propellant effective delta-v, tank capacity, cargo mass, return-fuel reserve, solar rules, and route block reasons.
- Return a stable estimate suitable for frozen route records.

For local and Earth-Moon routes, keep existing special-case behavior close to current values.

## Fast Mode

`Fast` means time-biased near-now departure, not "use the same cheap transfer but shorten it a little."

Apply rules like:

- Reject candidates that exceed loaded tank capacity, loaded fuel, loaded delta-v, or required return reserve.
- Prefer the earliest feasible arrival.
- If two candidates are close in arrival date, choose the lower-fuel one.
- Never spend protected return fuel to make an outbound Fast candidate feasible.

The important part is behavioral: Fast should depart now or near-now, arrive as soon as the selected craft can feasibly arrive, and become expensive at bad windows because the ship must chase the moving target instead of waiting for geometry to line up.

## Fallback Lambert Refinement

A small Lambert refinement remains useful as a safety valve, but it should not be the primary route tick behavior.

Use it only when:

- The Hohmann/high-energy estimate cannot confidently determine feasibility.
- Endpoint orbit data is valid but route geometry is unusual.
- A calibration test shows a route family where the cheap estimate drifts too far from current expected behavior.
- A player-visible plan preview explicitly asks for higher fidelity.

The fallback should solve only a small set of candidate times of flight around the cheap estimate, for example 3 to 9 candidates. It should not create a two-dimensional departure/arrival grid. Conceptually this is closer to "sample a few likely mission plans" than to "build a porkchop graph."

If we eventually port an Izzo-style Lambert solver, keep it as a local primitive:

```text
solve_lambert(r_start, r_target, time_of_flight, mu) -> departure_velocity, arrival_velocity
```

Route planning should call that primitive a few times, not thousands of times.

## Deferred Caching

The first implementation should not add route-plan caching. If the estimator stays O(1), the simpler and safer answer is to recalculate the cheap estimate when a new dispatch decision is needed.

Caching can be revisited later if profiling shows route planning is still meaningful frame cost. The current cache key includes exact game ticks, which prevents useful route reuse; a future cache would need coarser keys that match logistics behavior:

```text
source id
destination id
resolved calculation source id
resolved calculation destination id
company or ruleset id
spacecraft type id
flight-plan mode
cargo mass band
fuel/tank band if variable
return reserve required flag
return cargo mass band
return mode
route kind
relevant tuning version
```

Possible future date buckets:

- Local and Earth-Moon: one day or current dispatch tick.
- Same-parent and parent-child: several days.
- Interplanetary: one week to one month, depending on how much visible drift the estimates show.

The cache must be invalidated when relevant formula tuning changes. A simple integer planner version in the key is enough.

## Scheduling Checklist

Normal route scheduling should check cheap blockers before flight math:

- Route is active.
- Source, destination, resource, and amount are valid.
- Source has available inventory.
- Destination can accept the resource.
- Assigned spacecraft exists and can carry the cargo.
- Required launch vehicles or facility launch capacity are available at the source.
- The route is not blocked by user policy, disabled facility categories, or missing reservations.
- If the ship must return and the destination lacks enough same-type fuel to refuel it, the outbound plan can reserve enough loaded fuel for the return leg.

Only after these checks pass should the route planner estimate the flight. This prevents blocked routes from repeatedly spending CPU on plans they cannot dispatch.

## Validation Scenarios

Use the captured cases in `LogisticsMod.FormulaTests` as the starting contract for calibration:

- Venus-to-Earth five-Zeus cases:
  - Captured vanilla Fastest fuel case around `1436.8T` from `dV1 + dV2 = 93.653 + 93.228`.
  - Live Fast trace around `208T` from `27.22` delta-v.
  - Live Optimal trace around `60T` from `7.91` delta-v.
- Mars-to-Earth one-Nike cases:
  - Fastest trace around `115T` from `110.952` delta-v.
  - Optimal trace around `6T` from `5.814` delta-v.
- Earth-Moon routes.
- Confirmed body-moon precalc routes, if vanilla hard-coded values are found.
- Same-body and same-object orbit routes.
- Surface-to-orbit and orbit-to-surface local routes.
- Constant-acceleration spacecraft marked `NotUsePorkchope`.
- Solar-powered spacecraft that must remain `Optimal`.

Acceptance targets:

- New estimates should generally land within about 10-20% of the captured vanilla-like fuel and timing cases before deeper tuning.
- Same-object, local, and Earth-Moon routes remain close to existing travel days and fuel values.
- `Optimal` stays fuel-minimizing for the candidate family.
- `Fast` is never "burn all fuel by default."
- `Fast` skips tiny time gains that require huge fuel penalties.
- Round-trip routes reject outbound plans that leave insufficient fuel reserved for the return leg when the destination cannot refuel the same fuel type.
- `Fast` protects return reserve before spending extra fuel on time savings.
- Solar-powered ships always resolve to `Optimal`.
- Fast/Optimal selection stays based on loaded propellant effective delta-v, not only static spacecraft design delta-v.
- Route scheduling performs O(1) or small-N candidate work.
- Normal route ticks use no vanilla porkchop grid.
- Dispatched route records keep their frozen dates and fuel after UI refreshes, time advancement, and later route mode changes.

## Implementation Shape

The future code should split route planning into clear layers:

```text
LogisticsFlightPlanRequest
  immutable input snapshot

LogisticsFlightPlanResult
  frozen output values and reason/confidence/source

LogisticsRoundTripReserve
  optional return leg estimate and reserved fuel

ILogisticsRouteFlightPlanner
  TryPlan(request, out result)

Route kind estimators
  SameObjectEstimator
  LocalOrbitEstimator
  EarthMoonEstimator
  HohmannEstimator
  HighEnergyFastEstimator
  ConstantAccelerationEstimator
  OptionalSmallLambertRefinement

Selection
  OptimalCandidateSelector
  FastCandidateSelector with diminishing-return economics
```

This does not require a large abstraction up front. The first implementation can be a small internal helper beside `LogisticsFlightCalculator`, as long as the request/result boundary exists and formula tests can call it without creating UI state.

## Migration Path

1. Add tests for the new request/result shape using captured formula cases.
2. Preserve current local, Earth-Moon, and constant-acceleration behavior.
3. Research vanilla body-moon constants before deciding which pairs deserve a precomputed table.
4. Implement Hohmann/minimum-energy estimates for `Optimal` route families.
5. Implement high-energy small-N candidates for `Fast`.
6. Add the diminishing-return selector and tune it against captured Fast cases.
7. Gate fallback Lambert refinement behind confidence checks or explicit diagnostics.
8. Remove the route-side legacy porkchop/Lambert grid implementation after the estimator is covered by tests.
9. Use captured vanilla mission diagnostics for calibration instead of retaining a route-side porkchop comparison path.
10. Revisit caching only if profiling shows the O(1) planner still needs it.

## Non-Goals

- Do not make logistics a perfect mission planner.
- Do not introduce PyKEP or a native dependency into the mod.
- Do not run a 2D porkchop grid during background scheduling.
- Do not change Fast/Optimal user-facing meanings.
- Do not recalculate active or already-dispatched route traffic just because a better estimate exists later.
