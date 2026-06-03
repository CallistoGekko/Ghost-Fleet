# Ghost Fleet Logistics Alpha Test Checklist

Use this checklist when testing an alpha release build. Please back up saves first.

## Install

1. Install BepInEx 5.x for Solar Expanse.
2. Extract the release zip into the Solar Expanse install folder.
3. Confirm the plugin exists at:

```text
BepInEx\plugins\logisticsmod\LogisticsMod.dll
```

4. Launch the game.
5. Load a copied test save.

## Smoke Test

- Open an owned object's info window.
- Click the Logistics button.
- Confirm the popup title matches the selected body or orbit.
- Confirm Escape closes the popup.
- Confirm clicking or scrolling inside the popup does not accidentally click the map or object panel behind it.
- Confirm non-player objects do not expose usable logistics controls.

## Route Creation

- Create a route from a source body to a destination body.
- Create a route from a body to its own orbit.
- Create a route from an orbit to its parent body.
- Create a route between two orbits.
- Use the destination search field with short partial names.
- Remove a route and confirm idle route-owned assets return to normal vanilla availability.

## Route Resources

For at least two resources on the same route:

- Add the resource to the route.
- Set a Source Keep amount.
- Set a Destination Target amount.
- Try each priority level.
- Pause and resume one resource.
- Pause and resume the whole route.
- Confirm status text makes sense:
  - `Target stocked`
  - `Waiting for source surplus above N`
  - `Waiting for convoy launch`
  - `No idle vessels`
  - `Paused`

## Spacecraft Assignment

- Assign one spacecraft type to a route.
- Assign multiple ships of the same type.
- Use the count editor to increase and decrease assigned ships.
- Confirm Ready and Qty counts update.
- Confirm assigned ships are not also available for unrelated vanilla use.
- Confirm idle or blocked ships can be released back to the vanilla game.
- Confirm busy in-flight ships are not released prematurely.

## Launch Vehicle Assignment

For surface-source routes:

- Assign launch vehicles to the route.
- Confirm Ready and Qty counts update.
- Confirm reusable launch vehicles enter cooldown after use when applicable.
- Confirm expendable or invalid launch vehicles are retired or removed cleanly.
- Try a route that requires launch support and confirm the route reports a useful blocker if none is available.

## Movement Paths

Test each movement path if the save supports it:

- **Orbit drop**: source orbit to its parent body.
- **Virtual surface lift**: body to its own orbit with surface lift enabled.
- **Ghost convoy flight**: source to a different body or orbit using assigned spacecraft.
- **Multi-resource convoy**: multiple resources competing for limited assigned craft.
- **Crew cargo**: human transfer, including crew supply consumption and payload mass.
- **Fuel-limited route**: route where outgoing, launch, or return fuel may constrain cargo.

## Route Traffic

- Open a route after a dispatch.
- Confirm the Route Traffic section appears.
- Confirm rows show ship count, compact route lane, arrival date, and cargo manifest.
- Confirm compatible flights merge into convoy rows where appropriate.
- Confirm completed or cancelled traffic disappears cleanly.
- Confirm map trajectory visuals appear, move over time, and disappear after arrival.

## Save And Load

Test saving and reloading at these moments:

- After creating routes but before dispatch.
- After assigning spacecraft and launch vehicles.
- While a ghost flight is in transit.
- After pausing a route.
- After collapsing and expanding route rows.
- After removing a route with assigned assets.

After load, confirm:

- routes still exist
- route resources still have the same keep, target, priority, and active state
- assigned assets still show correct Ready and Qty counts
- active route traffic resumes without duplicate flights
- orphaned assets are released or reported cleanly

## Economy

- Compare maintenance before and after assigning route-owned spacecraft.
- Compare maintenance before and after assigning route-owned launch vehicles.
- Confirm route-owned assets still cost upkeep.
- Confirm released assets no longer remain in the ghost ledger.

## UI Polish

- Try long route lists.
- Try long resource lists.
- Scroll with mouse wheel.
- Try middle-mouse autoscroll.
- Collapse and expand multiple routes.
- Check that route rows, status dots, icons, Ready/Qty tables, and buttons do not overlap.
- Check that popup focus does not jump when selecting another body in the underlying panel.

## Useful Logs

For bug reports, include:

```text
BepInEx\LogOutput.log
BepInEx\LogisticsMod_*.log
```

To enable the extra logistics log, set:

```text
BepInEx\plugins\logisticsmod\LogisticsMod.cfg

[Diagnostics]
VerboseLogging = true
```

## Bug Report Template

Please include:

- Solar Expanse version.
- Ghost Fleet Logistics release tag.
- Source body or orbit.
- Destination body or orbit.
- Route resources and their Source Keep, Destination Target, and Priority values.
- Assigned spacecraft and launch vehicles.
- What you expected to happen.
- What actually happened.
- Whether it happened before or after saving/loading.
- Relevant logs.
