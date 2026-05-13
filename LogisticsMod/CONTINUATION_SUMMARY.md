# LogisticsMod Continuation Summary

## Current focus

We are debugging and polishing the player logistics system in `LogisticsMod` for Solar Expanse.

Primary active areas:

- automatic return trips for logistics spacecraft
- stale GET / SEND UI state when switching between bodies and orbits
- reducing false blockers and noisy logging

## Current source of truth

Project folder:

- `C:\Users\parft\Documents\SolarExpanseMods\LogisticsMod`

Built DLL target:

- `C:\Program Files (x86)\Steam\steamapps\common\Solar Expanse\BepInEx\plugins\logisticsmod\LogisticsMod.dll`

Main files touched recently:

- `C:\Users\parft\Documents\SolarExpanseMods\LogisticsMod\Logic\LogisticsObserver.cs`
- `C:\Users\parft\Documents\SolarExpanseMods\LogisticsMod\UI\LogisticsUI.cs`
- `C:\Users\parft\Documents\SolarExpanseMods\LogisticsMod\Patches\ObjectInfoWindowPatches.cs`
- `C:\Users\parft\Documents\SolarExpanseMods\LogisticsMod\Data\LogisticsNetwork.cs`

## Recent fixes already implemented

### 1. Return-home launch logic

Problem:

- ships sent to Luna were getting marked for return, but return scheduling blocked on "body requires LV and none is ready"
- this was wrong for `Stratos`, which does not require an LV from low-gravity worlds like Luna

Fix applied in `LogisticsObserver.cs`:

- return planning no longer uses body-level `NeedVehicleToLaunch()` alone
- it now mirrors vanilla AI logic:
  - no LV if launching from orbit
  - on surface, LV required only if:
    - the body requires one, and
    - either it is the main world, or the spacecraft type has `needLaunchVehicleToGoToMoon = true`

This should allow `Stratos` to return directly from Luna without an LV.

### 2. Return LV selection relaxed

If an LV is required, the selection logic was also loosened to match vanilla behavior better:

- removed strict `lv.objectInfo == current`
- accepts player-owned LVs where:
  - `!lv.launchTime.HasValue || lv.launchVehicleType.reusability > 0f`

### 3. Return blocked log spam reduced

Problem:

- failed return attempts were spamming `RETURNHOME blocked` many times in a row and likely contributing to lag

Fix:

- blocked-return warnings are now throttled to once per in-game day per ship/reason

### 4. GET / SEND UI stale state

Problem:

- when switching between bodies like `EARTH` and `EARTH [ORBIT]`, the logistics panel sometimes kept old GET/SEND data until a later refresh
- logs showed `ObjectInfoWindow.SetData(...)` was firing, but sometimes while `LogisticsUI` was disabled

Fix applied in `LogisticsUI.cs`:

- the logistics UI now resyncs directly from `ObjectInfoWindow.ObjectInfoDataCurrent`
- sync happens:
  - on `Start`
  - on `OnEnable`
  - in `LateUpdate`, but only when the live object/company differs from the cached one

This should make the logistics panel refresh immediately on body/orbit switches.

## Important current logging

Log file:

- `C:\Program Files (x86)\Steam\steamapps\common\Solar Expanse\BepInEx\LogisticsMod_1.log`

Useful log lines:

- `RETURNHOME mark`
- `RETURNHOME cycle`
- `RETURNHOME blocked`
- `UI sync-from-window`
- `RefreshData:`
- `BuildGet for`

## What should be tested next

### Return testing

Verify with a fresh in-game run:

1. Send a `Stratos` from Earth to Luna with a logistics delivery.
2. Let the delivery complete.
3. Confirm a return mission is created automatically.
4. Check logs for:
   - `RETURNHOME cycle`
   - absence of repeated `RETURNHOME blocked` for the Stratos/Luna case

If it still fails:

- inspect whether `RETURNHOME cycle` appears but the game fails to execute it
- inspect whether the ship remains attached to an old `[LOGI]` cycle incorrectly

### UI testing

Switch repeatedly between:

- `EARTH`
- `EARTH [ORBIT]`
- `LUNA`
- `LUNA [ORBIT]` if applicable

Expected behavior:

- GET / SEND / spacecraft / LV sections should reflect the current body immediately
- no need to wait for a periodic refresh

## Known assumptions

- The current return fix assumes spacecraft-level `needLaunchVehicleToGoToMoon` is the right discriminator for low-gravity launch behavior.
- The UI refresh fix assumes `ObjectInfoWindow.ObjectInfoDataCurrent` is always the authoritative current target after window updates.

## Build note

Recent builds succeeded with:

- `dotnet build`

from:

- `C:\Users\parft\Documents\SolarExpanseMods\LogisticsMod`

## Suggested next step for the next session

Run the game with the latest DLL and verify:

1. `Stratos` returns from Luna automatically
2. GET / SEND updates instantly when switching between body and orbit views

If either still fails, inspect the new log lines first before making further structural changes.
