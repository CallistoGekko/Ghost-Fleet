# Route Fast Bad-Window Cost Plan

## Goal

Make `Fast` route planning mean "arrive as soon as this craft can reasonably arrive from near-now," while keeping `Optimal` as "wait for the low-cost transfer window."

This plan intentionally does not calibrate to vanilla as an exact target. Vanilla values are sanity checks only. The model should be explainable from route geometry, travel time, tank limits, and the existing fuel formula.

## Non-Goals

- Do not change the fuel formula.
- Do not change `Optimal` beyond the current window-wait behavior.
- Do not reintroduce the full vanilla porkchop grid into normal logistics dispatch.
- Do not make Fast spend fuel just because fuel exists; tank capacity, protected return reserve, and useful time savings still matter.

## Current Problem

The current Fast correction mostly multiplies the Hohmann-style delta-v by small phase and high-energy factors. That underprices bad windows because a bad window is not merely "Hohmann but slightly worse."

For a near-now departure, the target may be far away from the point where a cheap transfer would naturally arrive. Fast should pay a chase/intercept cost to erase that phase miss.

## Model

For Hohmann-suitable same-parent routes:

```text
idealDeltaV = Hohmann-style baseline delta-v
idealTravelDays = Hohmann-style baseline travel time
candidateTravelDays = immediate-departure candidate travel time

departureAngle = source circular phase at near-now departure
transferArrivalAngle = departureAngle + pi
targetArrivalAngle = target circular phase at near-now departure
                   + targetAngularVelocity * candidateTravelTime

phaseMiss = signed_angle(targetArrivalAngle - transferArrivalAngle)
missDistance = targetOrbitRadius * 2 * sin(abs(phaseMiss) / 2)
chaseDeltaV = missDistance / candidateTravelTime
```

Convert `chaseDeltaV` to km/s and add it to the selected Fast candidate cost.

The resulting Fast candidate delta-v should be:

```text
candidateDeltaV =
    idealDeltaV
  + highEnergyCompressionExtraDeltaV
  + badWindowChaseDeltaV
```

Where:

```text
highEnergyCompressionExtraDeltaV =
    max(0, EstimateHighEnergyDeltaV(idealDeltaV, idealTravelDays, candidateTravelDays) - idealDeltaV)
```

The important change is that bad-window cost is based on orbital miss distance over available travel time. Shorter arrivals and worse phase misses naturally become expensive.

## Candidate Selection

Fast should depart near-now. Use the existing small candidate set:

```text
candidateTravelDays = [
  1.00 * idealTravelDays,
  0.85 * idealTravelDays,
  0.70 * idealTravelDays,
  0.55 * idealTravelDays,
  max(ship.MinFlightTimeHohRel, 0.55) * idealTravelDays
]
```

For each candidate:

1. Compute the candidate arrival date from near-now departure.
2. Compute phase miss at that candidate arrival date.
3. Compute bad-window chase delta-v from phase miss.
4. Add high-energy compression extra delta-v.
5. Convert total delta-v to fuel with the existing fuel formula.
6. Reject candidates that exceed loaded available delta-v, tank capacity, or protected return fuel budget.
7. Prefer the earliest feasible arrival.
8. If two feasible candidates are close in arrival time, prefer the lower-fuel candidate.

This makes Fast time-biased without making it blindly wasteful.

## Near-Now Departure

Use immediate departure unless the route system already requires a small dispatch lead time. If a lead time is used, keep it small and deterministic, such as 0-2 days, and include that same delay in the phase prediction.

## Expected Behavior

- At a good transfer window, Fast should be only moderately more expensive than Optimal unless the selected arrival time is much shorter.
- At a bad transfer window, Fast should become expensive because the target is far from the natural transfer arrival point.
- For the Venus -> Earth Zeus 20KT case, a bad-window Fast route landing in the tens of km/s is reasonable. Vanilla's roughly 33-34 km/s is a useful sanity check, not an exact target.
- Route logistics Fast should not use extreme ship minimum time-of-flight values below 55% of the Hohmann baseline. Those values can be valid for manual mission sliders or special cases, but they make automated freight routes arrive absurdly fast when paired with very high exhaust velocity craft.
- If no Fast candidate fits the tank and return-reserve budget, the route should block instead of silently underfueling.

## Implementation Steps

1. Add a pure math helper for bad-window chase delta-v.
   - Inputs: target orbital radius in meters, phase miss radians, candidate travel days.
   - Output: chase delta-v in km/s.
   - Test zero phase, half-phase, opposite-side phase, and shorter-time cases.

2. Extend the transfer-window state used by the flight calculator.
   - Keep source angle, target angle, target angular velocity, and target radius available for candidate evaluation.
   - Keep the existing Optimal wait calculation intact.

3. Replace Fast's current small phase multiplier.
   - Remove the `ApplyTransferWindowPenalty` usage from Fast candidate selection.
   - Compute each candidate's phase miss at its candidate arrival time.
   - Add `badWindowChaseDeltaV` to the candidate delta-v.

4. Preserve existing fuel and feasibility checks.
   - Continue using loaded available delta-v, tank capacity, and `maxFlightFuel`.
   - Continue protecting return fuel reserve for outbound Fast planning.

5. Update tests.
   - Add direct math tests for chase delta-v.
   - Update source tests so Fast is required to use bad-window chase cost instead of a small phase multiplier.
   - Keep tests that assert normal logistics dispatch does not call the full porkchop grid.

6. Build and deploy.
   - Run formula/source tests.
   - Stop Solar Expanse.
   - Build the mod in Release with the game directory.
   - Restart Solar Expanse.

## Optional Diagnostics

If the first implementation is hard to reason about in the UI, add frozen flight-record diagnostics:

```text
Outbound phase miss
Outbound bad-window dV
Outbound fast-compression dV
Outbound baseline dV
```

These should be calculation breadcrumbs, not new gameplay rules.
