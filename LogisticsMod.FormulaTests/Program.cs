using System;
using System.Collections.Generic;
using System.IO;
using LogisticsMod.Logic;

namespace LogisticsMod.FormulaTests
{

internal static class Program
{
    private static readonly List<string> Failures = new List<string>();

    private static int Main()
    {
        Run("fuel formula matches vanilla rounded total propellant", FuelFormulaMatchesVanillaRoundedTotalPropellant);
        Run("fuel formula documents grouped convoy rounding", FuelFormulaDocumentsGroupedConvoyRounding);
        Run("logged route inputs reproduce grouped fuel", LoggedRouteInputsReproduceGroupedFuel);
        Run("logged Nike route inputs reproduce fast and optimal fuel", LoggedNikeRouteInputsReproduceFastAndOptimalFuel);
        Run("porkchop gate uses loaded propellant effective delta-v", PorkchopGateUsesLoadedPropellantEffectiveDeltaV);
        Run("optimal selects lowest delta-v", OptimalSelectsLowestDeltaV);
        Run("fastest selects earliest feasible arrival", FastestSelectsEarliestFeasibleArrival);
        Run("porkchop selection ignores impossible candidates", PorkchopSelectionIgnoresImpossibleCandidates);
        Run("route calculator uses vanilla effective delta-v gate", RouteCalculatorUsesVanillaEffectiveDeltaVGate);
        Run("route traffic render does not refresh frozen fuel", RouteTrafficRenderDoesNotRefreshFrozenFuel);
        Run("return flights use frozen launch fuel and dates", ReturnFlightsUseFrozenLaunchFuelAndDates);
        Run("routes carry return fuel when destination cannot refuel", RoutesCarryReturnFuelWhenDestinationCannotRefuel);
        Run("blocked stale return craft are recovered visibly", BlockedStaleReturnCraftAreRecoveredVisibly);
        Run("route dispatch uses grouped convoy fuel", RouteDispatchUsesGroupedConvoyFuel);
        Run("mission planner diagnostics are opt-in", MissionPlannerDiagnosticsAreOptIn);
        Run("route mission diagnostics stay low-volume", RouteMissionDiagnosticsStayLowVolume);
        Run("route porkchop scan matches vanilla grid shape", RoutePorkchopScanMatchesVanillaGridShape);
        Run("route porkchop preserves selected transfer dates", RoutePorkchopPreservesSelectedTransferDates);
        Run("route porkchop caches duplicate same-tick scans", RoutePorkchopCachesDuplicateSameTickScans);
        Run("route plan controls are ship-type Fast/Optimal only", RoutePlanControlsAreShipTypeFastOptimalOnly);
        Run("route plan selection flows into dispatch", RoutePlanSelectionFlowsIntoDispatch);
        Run("spacecraft table orders Ready and Qty before Plan", SpacecraftTableOrdersReadyQtyBeforePlan);

        if (Failures.Count == 0)
        {
            Console.WriteLine("Formula tests passed.");
            return 0;
        }

        Console.Error.WriteLine("Formula tests failed:");
        foreach (var failure in Failures)
            Console.Error.WriteLine(" - " + failure);
        return 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception exception)
        {
            Failures.Add(name + ": " + exception.Message);
        }
    }

    private static void FuelFormulaMatchesVanillaRoundedTotalPropellant()
    {
        AssertClose(778.0, TotalFuel(125.0, 101.0, 35.4), 0.0001, "Zeus-like five-ship fast case");
        AssertClose(250.0, TotalFuel(45.0, 76.0, 28.0), 0.0001, "Nike-like high delta-v case");
        AssertClose(104.0, TotalFuel(17.5, 12.3, 4.4), 0.0001, "chemical low exhaust case");
        AssertClose(0.8, TotalFuel(100.0, 0.5, 45.0), 0.0001, "vanilla total can be below rounded minimum");
        AssertClose(36.9, TotalFuel(25.0, 47.0, 36.0), 0.0001, "one-decimal total propellant case");

        AssertClose(37.0,
            LogisticsVanillaMissionMath.CalculateMinimumPropellantNeeded(25.0, 47.0, 36.0, 2.0),
            0.0001,
            "minimum propellant keeps vanilla whole-number rounding");
    }

    private static void FuelFormulaDocumentsGroupedConvoyRounding()
    {
        var groupedMass = LogisticsVanillaMissionMath.CalculateMassToFuel(5.0, 100.0, 5);
        var groupedFuel = TotalFuel(groupedMass, 101.0, 35.4);

        var perCraftMass = LogisticsVanillaMissionMath.CalculateMassToFuel(5.0, 20.0, 1);
        var summedFuel = TotalFuel(perCraftMass, 101.0, 35.4) * 5.0;

        AssertClose(778.0, groupedFuel, 0.0001, "one five-craft vanilla mission");
        AssertClose(780.0, summedFuel, 0.0001, "five separately rounded one-craft missions");
    }

    private static void LoggedRouteInputsReproduceGroupedFuel()
    {
        AssertClose(1436.8,
            LogisticsVanillaMissionMath.CalculateTotalPropellantNeeded(105000.0, 93.653 + 93.228, 13750.0, 2.718),
            0.1,
            "captured vanilla Venus-to-Earth five-Zeus Fastest mission uses dV1+dV2 for fuel");
        AssertClose(208.0,
            LogisticsVanillaMissionMath.CalculateTotalPropellantNeeded(105000.0, 27.22, 13750.0, 2.718),
            0.5,
            "live Venus-to-Earth five-Zeus Fast trace");
        AssertClose(60.0,
            LogisticsVanillaMissionMath.CalculateTotalPropellantNeeded(105000.0, 7.91, 13750.0, 2.718),
            0.5,
            "live Venus-to-Earth five-Zeus Optimal trace");
    }

    private static void LoggedNikeRouteInputsReproduceFastAndOptimalFuel()
    {
        var groupedMass = LogisticsVanillaMissionMath.CalculateMassToFuel(
            dryMassPerCraft: 400.0,
            cargoMass: 5000.0,
            craftCount: 1);
        AssertClose(5400.0, groupedMass, 0.0001, "one loaded Nike mass");
        AssertClose(115.0,
            LogisticsVanillaMissionMath.CalculateTotalPropellantNeeded(groupedMass, 110.952, 5250.0, 2.718),
            0.1,
            "Mars-to-Earth one-Nike Fastest trace should match vanilla 115T");
        AssertClose(6.0,
            LogisticsVanillaMissionMath.CalculateTotalPropellantNeeded(groupedMass, 5.814, 5250.0, 2.718),
            0.1,
            "Mars-to-Earth one-Nike Optimal trace explains the wrong 6T route row");
    }

    private static void PorkchopGateUsesLoadedPropellantEffectiveDeltaV()
    {
        var effectiveDeltaV = LogisticsVanillaMissionMath.CalculateLoadedPropellantEffectiveDeltaV(
            dryMass: 1000.0,
            cargoMass: 20000.0,
            fuelCapacity: 5000.0,
            exhaustVelocity: 13750.0);
        AssertTrue(effectiveDeltaV > 180.0,
            "vanilla porkchop gate should come from loaded propellant, not the static SpacecraftType.AvailableDeltaV");

        var now = new DateTime(2298, 1, 1);
        var candidates = new[]
        {
            new LogisticsPorkchopCandidate(0, 0, 181.626, now.AddDays(35)),
            new LogisticsPorkchopCandidate(0, 1, 43.0, now.AddDays(70))
        };

        AssertTrue(LogisticsVanillaMissionMath.TrySelectPorkchopCandidate(
            candidates,
            LogisticsVanillaMissionPlanMode.Fastest,
            effectiveDeltaV,
            out var selected), "fastest should have a usable candidate under the vanilla gate");
        AssertClose(181.626, selected.DeltaV, 0.0001,
            "fastest should not be forced down to a low-delta-v cell by the static ship-type availableDV");
    }

    private static void OptimalSelectsLowestDeltaV()
    {
        var now = new DateTime(2298, 1, 1);
        var candidates = new[]
        {
            new LogisticsPorkchopCandidate(0, 0, 40.0, now.AddDays(10)),
            new LogisticsPorkchopCandidate(0, 1, 25.0, now.AddDays(20)),
            new LogisticsPorkchopCandidate(0, 2, 25.0, now.AddDays(15)),
            new LogisticsPorkchopCandidate(0, 3, 30.0, now.AddDays(5))
        };

        AssertTrue(LogisticsVanillaMissionMath.TrySelectPorkchopCandidate(
            candidates,
            LogisticsVanillaMissionPlanMode.Optimal,
            100.0,
            out var selected), "optimal should select a candidate");
        AssertEqual(2, selected.ArrivalIndex, "optimal should choose the earlier arrival only after delta-v ties");
    }

    private static void FastestSelectsEarliestFeasibleArrival()
    {
        var now = new DateTime(2298, 1, 1);
        var candidates = new[]
        {
            new LogisticsPorkchopCandidate(0, 0, 25.0, now.AddDays(20)),
            new LogisticsPorkchopCandidate(0, 1, 55.0, now.AddDays(9)),
            new LogisticsPorkchopCandidate(0, 2, 40.0, now.AddDays(10))
        };

        AssertTrue(LogisticsVanillaMissionMath.TrySelectPorkchopCandidate(
            candidates,
            LogisticsVanillaMissionPlanMode.Fastest,
            60.0,
            out var selected), "fastest should select a candidate");
        AssertEqual(1, selected.ArrivalIndex, "fastest should choose earliest feasible arrival, not lowest delta-v");
    }

    private static void PorkchopSelectionIgnoresImpossibleCandidates()
    {
        var now = new DateTime(2298, 1, 1);
        var candidates = new[]
        {
            new LogisticsPorkchopCandidate(0, 0, 70.0, now.AddDays(3)),
            new LogisticsPorkchopCandidate(0, 1, 10.0, now.AddDays(4), scheduleAllowed: false),
            new LogisticsPorkchopCandidate(0, 2, 30.0, now.AddDays(6))
        };

        AssertTrue(LogisticsVanillaMissionMath.TrySelectPorkchopCandidate(
            candidates,
            LogisticsVanillaMissionPlanMode.Fastest,
            50.0,
            out var selected), "selection should skip over impossible rows");
        AssertEqual(2, selected.ArrivalIndex, "selection should ignore over-delta and schedule-blocked candidates");
    }

    private static void RouteTrafficRenderDoesNotRefreshFrozenFuel()
    {
        var observerSource = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var normalizeMethod = ExtractMethod(observerSource, "public static void NormalizeGhostConvoys()");
        AssertFalse(normalizeMethod.Contains("RefreshGhostFlightFuelEstimate"),
            "route row normalization must not recalculate stored ghost flight fuel");
        AssertFalse(observerSource.Contains("RefreshGhostFlightFuelEstimate("),
            "stored route traffic fuel should be created once, not refreshed from the UI path");
    }

    private static void RouteCalculatorUsesVanillaEffectiveDeltaVGate()
    {
        var source = ReadRepoFile("LogisticsMod", "Logic", "LogisticsFlightCalculator.cs");
        var method = ExtractMethod(source, "private static double EstimateAvailableDeltaV(");
        AssertTrue(method.Contains("CalculateLoadedPropellantEffectiveDeltaV"),
            "route flight planning should use vanilla loaded-propellant effective delta-v as the porkchop gate");
        AssertTrue(method.Contains("vehicle.FuelCapacity"),
            "route flight planning should derive the gate from the craft tank capacity");
        AssertFalse(method.Contains("GetPorkchopEffectiveDeltaV"),
            "route flight planning must not fall back to the static SpacecraftType.AvailableDeltaV gate");

        var porkchopMethod = ExtractMethod(source, "private static bool TryCalculateInstantPorkchop(");
        AssertTrue(porkchopMethod.Contains("candidateFuel"),
            "route porkchop selection should evaluate fuel for each candidate before accepting it");
        AssertTrue(porkchopMethod.Contains("tankOk"),
            "route porkchop selection should mirror vanilla schedule checks by rejecting over-tank candidates");
    }

    private static void ReturnFlightsUseFrozenLaunchFuelAndDates()
    {
        var source = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var method = ExtractMethod(source, "private static void StartGhostReturnFlight(");
        AssertTrue(method.Contains("outbound.returnFuel"),
            "return flight fuel should come from the outbound record frozen at launch");
        AssertTrue(method.Contains("outbound.returnTravelDays"),
            "return flight dates should come from the outbound record frozen at launch");
        AssertFalse(method.Contains("TryCalculateGhostLeg"),
            "return flight creation must not recalculate route fuel after the outbound ship arrives");
    }

    private static void RoutesCarryReturnFuelWhenDestinationCannotRefuel()
    {
        var source = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var groupedFuel = ExtractMethod(source, "private static bool NormalizeConvoyPlanFuelToVanillaGroup(");
        AssertTrue(groupedFuel.Contains("destinationData.CheckResources(fuelType)"),
            "route dispatch should only reserve destination return fuel when the destination actually has enough fuel");
        AssertTrue(groupedFuel.Contains("plan.ReservedReturnFuel = destinationRefuel ? Math.Max(0.0, plan.ReturnLeg?.Fuel ?? 0.0) : 0.0"),
            "destination return fuel should only be reserved when destination refuel is available");
        AssertTrue(groupedFuel.Contains(": Math.Max(0.0, plan.Outbound?.Fuel ?? 0.0) + Math.Max(0.0, plan.ReturnLeg?.Fuel ?? 0.0)"),
            "when destination refuel is unavailable, the launch tank requirement should include both outbound and return fuel");
        AssertTrue(groupedFuel.Contains("plan.OriginFuelTopUp = Math.Max(0.0, requiredTankAtDeparture - plan.Craft.tankFuel)"),
            "origin top-up should load missing round-trip fuel before launch");

        var singlePlan = ExtractMethod(source, "private static bool TryBuildGhostDeliveryPlan(");
        AssertTrue(singlePlan.Contains("var requiredTankAtDeparture = destinationRefuel"),
            "single-craft planning should use the same destination-refuel branch before convoy normalization");
        AssertTrue(singlePlan.Contains(": outbound.Fuel + returnLeg.Fuel"),
            "single-craft planning should also carry return fuel when the destination cannot refuel");
    }

    private static void BlockedStaleReturnCraftAreRecoveredVisibly()
    {
        var source = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var method = ExtractMethod(source, "private static void RecoverBlockedReturnFuelCraft(");
        var filter = ExtractMethod(source, "private static bool IsBlockedByStaleReturnFuel(");
        AssertTrue(method.Contains("Reserved return fuel missing")
                || filter.Contains("Reserved return fuel missing"),
            "recovery should only target the known stale return-fuel block");
        AssertTrue(method.Contains("activeFlightIds"),
            "recovery should not duplicate a craft that still has a visible active flight");
        AssertTrue(method.Contains("GhostFlightStatus.Returning"),
            "recovery should create a visible return flight instead of hiding the craft");
    }

    private static void RouteDispatchUsesGroupedConvoyFuel()
    {
        var source = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var method = ExtractMethod(source, "private static bool NormalizeConvoyPlanFuelToVanillaGroup(");
        AssertTrue(method.Contains("CalculateMassToFuel"),
            "dispatch should build the same grouped mass used by vanilla multi-craft missions");
        AssertTrue(method.Contains("CalculateTotalPropellantNeeded"),
            "dispatch should calculate grouped fuel through the vanilla formula");
        AssertTrue(method.Contains("DistributeGroupedLegFuel"),
            "dispatch should freeze the grouped fuel onto each leg before flight records are created");
        AssertFalse(source.Contains("LogRoutePlannerDiagnostics("),
            "investigation-only route planner diagnostics should not ship in production code");
        AssertFalse(source.Contains("ROUTE-CALC-DIAG"),
            "production route dispatch should not emit the old broad calculation dump");
        AssertFalse(source.Contains("COMPARE-MISSION side=logistics"),
            "logistics dispatch should not emit always-on vanilla comparison rows");
    }

    private static void MissionPlannerDiagnosticsAreOptIn()
    {
        var source = ReadRepoFile("LogisticsMod", "Patches", "PlanMissionDiagnosticsPatches.cs");
        AssertTrue(source.Contains("VANILLA-MISSION"),
            "vanilla mission diagnostics need a stable log prefix");
        AssertTrue(source.Contains("CheckScheduleFly"),
            "vanilla diagnostics should log schedule validation");
        AssertTrue(source.Contains("CalculateCostInFuel"),
            "vanilla diagnostics should log fuel calculation");
        AssertTrue(source.Contains("ButtonFastestClickButton"),
            "vanilla diagnostics should log explicit Fastest selection");
        AssertTrue(source.Contains("DeltaVPicker"),
            "vanilla diagnostics should log picker defaults and selected grid cells");
        AssertTrue(source.Contains("GridClickedCustom"),
            "vanilla diagnostics should log final Lambert grid-click delta-v splits");
        AssertTrue(source.Contains("PMTabScheduleCreateFlyPatch"),
            "vanilla diagnostics should log the final CreateFly commit");
        AssertTrue(source.Contains("PMTabScheduleDirectCommitPatch"),
            "vanilla diagnostics should hook direct schedule-button and save commit paths");
        AssertTrue(source.Contains("PMTabScheduleCodeCommitPatch"),
            "vanilla diagnostics should hook the code path that returns the queued MissionInfo");
        AssertTrue(source.Contains("OnClickScheduleButtonForCode"),
            "vanilla diagnostics should catch the queue path used by the mission planner Next button");
        AssertTrue(source.Contains("AddToSave"),
            "vanilla diagnostics should catch MissionInfo handed to the save-link path");
        AssertTrue(source.Contains("step=schedule-commit-exit"),
            "vanilla diagnostics should log direct schedule commits after vanilla fills the mission data");
        AssertTrue(source.Contains("step=schedule-code-commit-exit"),
            "vanilla diagnostics should log MissionInfo returned by the code commit path");
        AssertTrue(source.Contains("Manager.MissionInfoManager"),
            "vanilla diagnostics should resolve MissionInfoManager by full namespace");
        AssertTrue(source.Contains("step=create-missioninfo-enter"),
            "vanilla diagnostics should log exact CreateMissionInfo inputs");
        AssertTrue(source.Contains("MissionInfoManagerAddMissionInfoPatch"),
            "vanilla diagnostics should log missions when the vanilla manager adds them");
        AssertTrue(source.Contains("MissionInfoManagerLoadStatePatch"),
            "vanilla diagnostics should dump the manager mission list during save load");
        AssertTrue(source.Contains("MissionInfoLifecyclePatch"),
            "vanilla diagnostics should log mission object lifecycle updates");
        AssertTrue(source.Contains("MissionInfoRowDisplayPatch"),
            "vanilla diagnostics should log visible planned-mission rows");
        AssertTrue(source.Contains("step=mission-row-display"),
            "vanilla diagnostics should expose missions that are loaded or displayed without a fresh CreateMissionInfo call");
        AssertTrue(source.Contains("step=patch-status"),
            "vanilla diagnostics should confirm at startup which methods Harmony actually patched");
        AssertTrue(source.Contains("COMPARE-MISSION"),
            "vanilla diagnostics should emit a normalized comparison line for queued vanilla missions");
        AssertTrue(source.Contains("LogAlways"),
            "low-volume vanilla mission markers should not disappear when verbose route logs are disabled");
        AssertTrue(source.Contains("DateArrive"),
            "vanilla mission descriptions should use the game's actual arrival-date field");
        AssertTrue(source.Contains("costFuel"),
            "vanilla mission descriptions should use the game's actual stored fuel field");
        AssertTrue(source.Contains("listSpacecraftInfo2"),
            "vanilla mission descriptions should expose multi-craft mission count through the stored spacecraft list");
        AssertTrue(source.Contains("FormatCreateMissionInfoArgs"),
            "vanilla diagnostics should label mission creation arguments instead of relying on raw object dumps");
        AssertTrue(source.Contains("VanillaDiagnosticsEnabled"),
            "vanilla diagnostics should be gated before Harmony patches the mission planner");
        AssertFalse(source.Contains("if (!VanillaDiagnosticsEnabled())"),
            "optional vanilla diagnostics should skip with Prepare instead of returning zero Harmony targets");
        AssertFalse(source.Contains("GetMassToCalculateFuel"),
            "vanilla diagnostics must not call mission calculation methods while describing state");

        var plugin = ReadRepoFile("LogisticsMod", "Plugin.cs");
        AssertTrue(plugin.Contains("VanillaMissionPlannerDiagnostics\", false"),
            "vanilla mission diagnostics should be opt-in and disabled by default");
        AssertTrue(plugin.Contains("if (VanillaMissionDiagnosticsEnabled.Value)"),
            "vanilla mission diagnostics patch status should only run when explicitly enabled");
        AssertTrue(plugin.Contains("LogPatchStatus"),
            "opt-in diagnostics should still be able to report which vanilla hooks were patched");
        AssertFalse(plugin.Contains("PLUGIN-AWAKE step=enter"),
            "production startup should not create an always-on diagnostics log just to prove patch entry");
        AssertFalse(plugin.Contains("PLUGIN-AWAKE step=patchall-ok"),
            "production startup should not create an always-on diagnostics log just to prove patch completion");

        var observer = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        AssertTrue(observer.Contains("public static void LogAlways"),
            "opt-in vanilla diagnostics still need a low-volume logging path once enabled");
        AssertFalse(observer.Contains("COMPARE-MISSION side=logistics"),
            "logistics route dispatch should not emit comparison lines outside the vanilla diagnostics patch");
    }

    private static void RouteMissionDiagnosticsStayLowVolume()
    {
        var calculator = ReadRepoFile("LogisticsMod", "Logic", "LogisticsFlightCalculator.cs");
        AssertTrue(calculator.Contains("ROUTE-MISSION"),
            "route mission diagnostics need a stable log prefix");
        AssertTrue(calculator.Contains("step=destination"),
            "route diagnostics should log canonicalized endpoints");
        AssertTrue(calculator.Contains("step=window"),
            "route diagnostics should log porkchop time windows");
        AssertTrue(calculator.Contains("step=select"),
            "route diagnostics should log the selected candidate");
        AssertTrue(calculator.Contains("step=cache-hit"),
            "route diagnostics should identify duplicate same-tick porkchop cache hits");
        AssertFalse(calculator.Contains("step=grid-cell"),
            "route diagnostics should not log the 201x201 candidate grid during normal testing");
        AssertFalse(calculator.Contains("DetailedGridCellLogLimit"),
            "per-cell diagnostic throttling should disappear with the per-cell diagnostics");
        AssertFalse(calculator.Contains("physDepart"),
            "production diagnostics should not mix physical and visible game dates in the normal route log");

        var observer = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        AssertTrue(observer.Contains("step=leg-input"),
            "route diagnostics should log the exact route leg inputs");
        AssertTrue(observer.Contains("step=fuel-grouped"),
            "route diagnostics should log the grouped convoy fuel row value");
        AssertTrue(observer.Contains("requestedMode={requestedFlightPlanMode}"),
            "route diagnostics should log the exact ship-type Fast/Optimal mode used by dispatch");
    }

    private static void RoutePorkchopScanMatchesVanillaGridShape()
    {
        var calculator = ReadRepoFile("LogisticsMod", "Logic", "LogisticsFlightCalculator.cs");
        var method = ExtractMethod(calculator, "private static bool TryCalculateInstantPorkchop(");
        AssertTrue(calculator.Contains("private const int PorkchopIntervals = 200"),
            "route porkchop scan should use vanilla's 200 interval grid");
        AssertTrue(method.Contains("var departureEnd = departureStart + departureWindow;"),
            "route porkchop scan should use vanilla's normal departure window, not a doubled window");
        AssertFalse(method.Contains("2.0 * departureWindow"),
            "route porkchop scan must not stretch the departure window wider than vanilla");
        AssertTrue(method.Contains("var maxDepartureIndex = departureIntervals;"),
            "route porkchop scan should include the full vanilla grid bounds");
        AssertFalse(calculator.Contains("DetailedGridCellLogLimit"),
            "route porkchop scan should not carry vestigial per-cell diagnostic throttles");
        AssertFalse(method.Contains("step=grid-cell-summary"),
            "route porkchop scan should not emit per-grid-cell diagnostic summaries in production");
    }

    private static void RoutePorkchopPreservesSelectedTransferDates()
    {
        var calculator = ReadRepoFile("LogisticsMod", "Logic", "LogisticsFlightCalculator.cs");
        var calculate = ExtractMethod(calculator, "public static LogisticsCalculatedFlight CalculateSoonestOptimalFlight(");
        var porkchop = ExtractMethod(calculator, "private static bool TryCalculateInstantPorkchop(");
        var cacheStore = ExtractMethod(calculator, "private static void StoreInstantPorkchopCache(");
        var cacheGet = ExtractMethod(calculator, "private static bool TryGetInstantPorkchopCache(");

        AssertTrue(calculate.Contains("out var porkchopDeparture"),
            "route flight calculation should receive the selected porkchop departure date");
        AssertTrue(calculate.Contains("out var porkchopArrival"),
            "route flight calculation should receive the selected porkchop arrival date");
        AssertTrue(calculate.Contains("result.Departure = departure"),
            "route flight records should not default selected porkchop transfers to now");
        AssertTrue(calculate.Contains("result.Arrival = arrival"),
            "route flight records should preserve the selected porkchop arrival date");
        AssertTrue(porkchop.Contains("ConvertPhysicalDateToGameDate(bestDeparture"),
            "selected physical departure dates should be mapped onto the visible game calendar");
        AssertTrue(porkchop.Contains("ConvertPhysicalDateToGameDate(bestArrival"),
            "selected physical arrival dates should be mapped onto the visible game calendar");
        AssertFalse(porkchop.Contains("physDepart"),
            "production diagnostics should only show the visible selected transfer dates");
        AssertTrue(cacheStore.Contains("Departure = departure") && cacheStore.Contains("Arrival = arrival"),
            "cached porkchop results should include dates, not only delta-v and duration");
        AssertTrue(cacheGet.Contains("out DateTime departure") && cacheGet.Contains("out DateTime arrival"),
            "cache hits should return the stored selected transfer dates");
    }

    private static void RoutePorkchopCachesDuplicateSameTickScans()
    {
        var calculator = ReadRepoFile("LogisticsMod", "Logic", "LogisticsFlightCalculator.cs");
        var porkchopMethod = ExtractMethod(calculator, "private static bool TryCalculateInstantPorkchop(");
        AssertTrue(calculator.Contains("InstantPorkchopCache"),
            "route porkchop scans should keep a tiny duplicate-leg cache");
        AssertTrue(calculator.Contains("BuildInstantPorkchopCacheKey"),
            "cache keys should be explicit rather than hidden behind stale route state");
        AssertTrue(porkchopMethod.Contains("TryGetInstantPorkchopCache"),
            "the expensive 201x201 scan should be skipped for duplicate same-tick legs");
        AssertTrue(porkchopMethod.Contains("StoreInstantPorkchopCache"),
            "successful porkchop results should be reusable for identical convoy legs");
        AssertTrue(calculator.Contains("CurrentTime.Ticks"),
            "cache keys should include game time so advancing time cannot reuse stale answers");
        AssertTrue(calculator.Contains("NormalizeFlightPlanMode(flightPlanMode)"),
            "cache keys should separate Fast and Optimal route plans");
        AssertTrue(calculator.Contains("step=cache-hit"),
            "cache hits should be visible in route diagnostics");
    }

    private static void RoutePlanControlsAreShipTypeFastOptimalOnly()
    {
        var ui = ReadRepoFile("LogisticsMod", "UI", "LogisticsUI.cs");
        var addButtons = ExtractMethod(ui, "private void AddFlightPlanModeButtons(");
        var normalize = ExtractMethod(ui, "private static Data.LogisticsFlightPlanMode NormalizeUiFlightPlanMode(");
        AssertTrue(addButtons.Contains("Data.LogisticsFlightPlanMode.Fast"),
            "route plan controls should expose Fast");
        AssertTrue(addButtons.Contains("Data.LogisticsFlightPlanMode.Optimal"),
            "route plan controls should expose Optimal");
        AssertFalse(addButtons.Contains("Auto"),
            "route plan controls should not reintroduce Auto");
        AssertFalse(normalize.Contains("Auto"),
            "route plan normalization should not reintroduce Auto");

        var network = ReadRepoFile("LogisticsMod", "Data", "LogisticsNetwork.cs");
        var getter = ExtractMethod(network, "public static LogisticsFlightPlanMode GetRouteSpacecraftFlightPlanMode(");
        AssertTrue(getter.Contains("LogisticsFlightPlanMode.Optimal"),
            "ship-type route plan defaults should be Optimal");

        var countRow = ExtractMethod(ui, "private void AddRouteShipTypeFlightPlanRow(");
        AssertTrue(countRow.Contains("int desiredCount"),
            "ship-count plan controls should preserve the pending desired count");
        AssertTrue(countRow.Contains("ShowRouteShipCountEditor(section, route, typeId, desiredCount)"),
            "changing the plan in the count editor must not reset the pending count");
        AssertFalse(ui.Contains("ROUTE-MISSION step=plan-set"),
            "ship-type plan changes should not emit diagnostics when the value is already visible in the UI");
    }

    private static void RoutePlanSelectionFlowsIntoDispatch()
    {
        var observer = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        AssertTrue(observer.Contains("ResolveGhostCraftRequestedFlightPlanMode(craft, routeId)"),
            "dispatch should resolve the route's ship-type plan for the craft being launched");
        AssertTrue(observer.Contains("CalculateSoonestOptimalFlight(from, to, vehicle, cargo, player,\r\n            requestedFlightPlanMode)")
                || observer.Contains("CalculateSoonestOptimalFlight(from, to, vehicle, cargo, player,\n            requestedFlightPlanMode)"),
            "dispatch should pass the selected Fast/Optimal mode into the porkchop calculator");
        AssertTrue(observer.Contains("Data.LogisticsNetwork.GetRouteSpacecraftFlightPlanMode(route, craft?.shipTypeId)"),
            "dispatch should read the type-level route plan, not a per-craft setting");
    }

    private static void SpacecraftTableOrdersReadyQtyBeforePlan()
    {
        var ui = ReadRepoFile("LogisticsMod", "UI", "LogisticsUI.cs");
        var header = ExtractMethod(ui, "private void AddRouteAssetTableHeader(");
        var readyIndex = header.IndexOf("\"Ready\"", StringComparison.Ordinal);
        var qtyIndex = header.IndexOf("\"Qty\"", StringComparison.Ordinal);
        var planIndex = header.IndexOf("\"Plan\"", StringComparison.Ordinal);
        AssertTrue(readyIndex >= 0 && qtyIndex > readyIndex && planIndex > qtyIndex,
            "spacecraft table header should read Asset, Ready, Qty, Plan");
        AssertTrue(header.Contains("FlightPlanModeColumnGap"),
            "Plan header should have a gap after Qty so the labels do not run together");
    }

    private static double TotalFuel(double massToFuel, double deltaV, double exhaustVelocity)
    {
        return LogisticsVanillaMissionMath.CalculateTotalPropellantNeeded(massToFuel, deltaV, exhaustVelocity, 2.0);
    }

    private static void AssertClose(double expected, double actual, double tolerance, string message)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private static void AssertEqual(int expected, int actual, string message)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition)
            throw new InvalidOperationException(message);
    }

    private static string ReadRepoFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, Path.Combine(relativeParts));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repo file " + string.Join("/", relativeParts));
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException("Could not find method " + signature);

        var openBrace = source.IndexOf('{', start);
        if (openBrace < 0)
            throw new InvalidOperationException("Could not find method body for " + signature);

        var depth = 0;
        for (var i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source.Substring(start, i - start + 1);
            }
        }

        throw new InvalidOperationException("Could not parse method body for " + signature);
    }
}
}
