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
        Run("pykep Hohmann equations match reference values", PykepHohmannEquationsMatchReferenceValues);
        Run("high energy estimate grows as travel time shrinks", HighEnergyEstimateGrowsAsTravelTimeShrinks);
        Run("bad window chase delta-v follows phase miss geometry", BadWindowChaseDeltaVFollowsPhaseMissGeometry);
        Run("MIMA equations reproduce acceleration and mass", MimaEquationsReproduceAccelerationAndMass);
        Run("loaded propellant effective delta-v uses tank capacity", LoadedPropellantEffectiveDeltaVUsesTankCapacity);
        Run("route calculator uses loaded propellant effective delta-v gate", RouteCalculatorUsesLoadedPropellantEffectiveDeltaVGate);
        Run("route traffic render does not refresh frozen fuel", RouteTrafficRenderDoesNotRefreshFrozenFuel);
        Run("route traffic opens flight detail page", RouteTrafficOpensFlightDetailPage);
        Run("route flight records freeze diagnostic inputs", RouteFlightRecordsFreezeDiagnosticInputs);
        Run("route traffic live refresh skips unchanged rows", RouteTrafficLiveRefreshSkipsUnchangedRows);
        Run("time tick keeps ghost flight visuals alive", TimeTickKeepsGhostFlightVisualsAlive);
        Run("save load does not dispatch routes", SaveLoadDoesNotDispatchRoutes);
        Run("return flights use frozen launch fuel and dates", ReturnFlightsUseFrozenLaunchFuelAndDates);
        Run("routes carry return fuel when destination cannot refuel", RoutesCarryReturnFuelWhenDestinationCannotRefuel);
        Run("route launch payload includes loaded spacecraft fuel", RouteLaunchPayloadIncludesLoadedSpacecraftFuel);
        Run("same-fuel routes deliver surplus tank fuel", SameFuelRoutesDeliverSurplusTankFuel);
        Run("logistics arrivals notify vanilla delivery objectives", LogisticsArrivalsNotifyVanillaDeliveryObjectives);
        Run("blocked stale return craft are recovered visibly", BlockedStaleReturnCraftAreRecoveredVisibly);
        Run("route dispatch uses grouped convoy fuel", RouteDispatchUsesGroupedConvoyFuel);
        Run("route dispatch builds balanced mixed manifests", RouteDispatchBuildsBalancedMixedManifests);
        Run("route convoy failures propagate resource status", RouteConvoyFailuresPropagateResourceStatus);
        Run("human route launch support respects crew-safe facilities", HumanRouteLaunchSupportRespectsCrewSafeFacilities);
        Run("launch pad facility support stays passive without fake LV", LaunchPadFacilitySupportStaysPassiveWithoutFakeLv);
        Run("built facility route lift is not shadowed by fake launch vehicles", BuiltFacilityRouteLiftIsNotShadowedByFakeLaunchVehicles);
        Run("mission planner diagnostics are opt-in", MissionPlannerDiagnosticsAreOptIn);
        Run("route mission diagnostics stay low-volume", RouteMissionDiagnosticsStayLowVolume);
        Run("route planner avoids porkchop grid in normal path", RoutePlannerAvoidsPorkchopGridInNormalPath);
        Run("route planner preserves estimated transfer dates", RoutePlannerPreservesEstimatedTransferDates);
        Run("route planner removes porkchop implementation", RoutePlannerRemovesPorkchopImplementation);
        Run("body moon routes use vanilla moon case data", BodyMoonRoutesUseVanillaMoonCaseData);
        Run("fast route planning protects return fuel budget", FastRoutePlanningProtectsReturnFuelBudget);
        Run("route plan controls are ship-type Fast/Optimal only", RoutePlanControlsAreShipTypeFastOptimalOnly);
        Run("route plan selection flows into dispatch", RoutePlanSelectionFlowsIntoDispatch);
        Run("route final partial loads can dispatch", RouteFinalPartialLoadsCanDispatch);
        Run("route human partial loads can dispatch when useful", RouteHumanPartialLoadsCanDispatchWhenUseful);
        Run("route priority partial loads can dispatch", RoutePriorityPartialLoadsCanDispatch);
        Run("route resource editor defaults to target", RouteResourceEditorDefaultsToTarget);
        Run("route status resource ids render as labels", RouteStatusResourceIdsRenderAsLabels);
        Run("logistics popup stays below vanilla alerts", LogisticsPopupStaysBelowVanillaAlerts);
        Run("spacecraft table orders Ready and Qty before Plan", SpacecraftTableOrdersReadyQtyBeforePlan);
        Run("route module cargo drag/drop ships as ghost inventory", RouteModuleCargoDragDropShipsAsGhostInventory);
        Run("logistics custom icons are normalized in shared UI paths", LogisticsCustomIconsAreNormalizedInSharedUiPaths);

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

    private static void PykepHohmannEquationsMatchReferenceValues()
    {
        var transfer = LogisticsVanillaMissionMath.CalculateHohmannTransfer(1.0, 2.0, 1.0);
        AssertClose(0.28445705, transfer.DeltaV, 0.000001,
            "Hohmann total delta-v should match PyKEP basic_transfers reference");
        AssertClose(0.15470054, transfer.FirstBurnDeltaV, 0.000001,
            "Hohmann first burn should match PyKEP basic_transfers reference");
        AssertClose(0.1297565, transfer.SecondBurnDeltaV, 0.000001,
            "Hohmann second burn should match PyKEP basic_transfers reference");
        AssertTrue(transfer.TravelSeconds > 0.0,
            "Hohmann transfer should return a positive time of flight");

        var scaled = LogisticsVanillaMissionMath.CalculateHohmannTransfer(1.1, 2.2, 1.3);
        AssertClose(0.3092374, scaled.DeltaV, 0.000001,
            "scaled Hohmann total delta-v should match PyKEP reference");
    }

    private static void HighEnergyEstimateGrowsAsTravelTimeShrinks()
    {
        var baseline = LogisticsVanillaMissionMath.EstimateHighEnergyDeltaV(6.0, 100.0, 100.0);
        var faster = LogisticsVanillaMissionMath.EstimateHighEnergyDeltaV(6.0, 100.0, 60.0);
        var fastest = LogisticsVanillaMissionMath.EstimateHighEnergyDeltaV(6.0, 100.0, 30.0);

        AssertClose(6.0, baseline, 0.0001,
            "baseline high-energy estimate should not inflate the optimal candidate");
        AssertTrue(faster > baseline,
            "compressing travel time should cost extra delta-v");
        AssertTrue(fastest > faster,
            "more aggressive compression should cost still more delta-v");
    }

    private static void BadWindowChaseDeltaVFollowsPhaseMissGeometry()
    {
        const double radiusMeters = 1_000_000.0;

        AssertClose(0.0,
            LogisticsVanillaMissionMath.CalculateBadWindowChaseDeltaV(radiusMeters, 0.0, 1.0),
            0.000001,
            "zero phase miss should not add bad-window chase delta-v");
        AssertClose(0.016368,
            LogisticsVanillaMissionMath.CalculateBadWindowChaseDeltaV(radiusMeters, Math.PI / 2.0, 1.0),
            0.000001,
            "ninety-degree miss should use chord distance over travel time");
        AssertClose(0.023148,
            LogisticsVanillaMissionMath.CalculateBadWindowChaseDeltaV(radiusMeters, Math.PI, 1.0),
            0.000001,
            "opposite-side miss should cost the target orbit diameter over travel time");
        AssertClose(0.032736,
            LogisticsVanillaMissionMath.CalculateBadWindowChaseDeltaV(radiusMeters, Math.PI / 2.0, 0.5),
            0.000001,
            "shorter candidate travel time should increase chase delta-v");
        AssertClose(0.016368,
            LogisticsVanillaMissionMath.CalculateBadWindowChaseDeltaV(radiusMeters, Math.PI * 1.5, 1.0),
            0.000001,
            "phase miss should normalize to the shortest angular separation");
    }

    private static void MimaEquationsReproduceAccelerationAndMass()
    {
        var acceleration = LogisticsVanillaMissionMath.CalculateMimaRequiredAcceleration(
            new[] { 10.0, 0.0, 0.0 },
            new[] { 0.0, 10.0, 0.0 },
            100.0);
        AssertClose(0.316227766, acceleration, 0.000001,
            "MIMA required acceleration should follow PyKEP dv/dv_diff equation");

        var maximumInitialMass = LogisticsVanillaMissionMath.CalculateMimaMaximumInitialMass(
            acceleration,
            100.0,
            maxThrust: 2.0,
            effectiveExhaustVelocity: 30.0);
        AssertClose(9.38, maximumInitialMass, 0.02,
            "MIMA maximum initial mass should follow PyKEP thrust/exhaust equation");
    }

    private static void LoadedPropellantEffectiveDeltaVUsesTankCapacity()
    {
        var effectiveDeltaV = LogisticsVanillaMissionMath.CalculateLoadedPropellantEffectiveDeltaV(
            dryMass: 1000.0,
            cargoMass: 20000.0,
            fuelCapacity: 5000.0,
            exhaustVelocity: 13750.0);
        AssertTrue(effectiveDeltaV > 180.0,
            "loaded-propellant effective delta-v should come from dry mass, cargo mass, tank capacity, and exhaust velocity");
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

    private static void RouteTrafficOpensFlightDetailPage()
    {
        var ui = ReadRepoFile("LogisticsMod", "UI", "LogisticsUI.cs");
        var sectionBuilder = ExtractMethod(ui, "private void AddRouteGhostFlightsSection(");
        var virtualRows = ExtractMethod(ui, "private void AddVirtualGhostFlightTableRows(");
        var row = ExtractMethod(ui, "private void PopulateGhostFlightTableRow(");
        var detail = ExtractMethod(ui, "private void ShowRouteFlightDetail(");
        var refresh = ExtractMethod(ui, "private void RefreshAllSections()");
        var fuelLabel = ExtractMethod(ui, "private static string BuildGhostFlightFuelLabel(");
        var cargoLabel = ExtractMethod(ui, "private static string BuildGhostFlightCargoLabel(");

        AssertTrue(sectionBuilder.Contains("AddVirtualGhostFlightTableRows(flightsSection, section, route, ghostFlights)"),
            "flight rows should receive the outer route page as their navigation target");
        AssertTrue(virtualRows.Contains("routePageSection ?? section"),
            "flight row clicks should fall back to the row section only when no outer page section is available");
        AssertTrue(row.Contains("MakeRowButton(row)"),
            "route traffic rows should be clickable");
        AssertTrue(row.Contains("ShowRouteFlightDetail(section, route, flight)"),
            "clicking a route traffic row should open the flight detail page");
        AssertTrue(detail.Contains("Outbound ship fuel")
                && detail.Contains("Launch fuel")
                && detail.Contains("Return ship fuel")
                && detail.Contains("Round-trip fuel plan"),
            "flight detail page should show outbound, launch, return, and total fuel separately");
        AssertTrue(detail.Contains("Outbound delta-v")
                && detail.Contains("Outbound mass-to-fuel")
                && detail.Contains("Exhaust velocity")
                && detail.Contains("Tank at departure"),
            "flight detail page should expose calculation inputs and tank state");
        AssertTrue(refresh.Contains("TryGetActiveRouteFlightDetail("),
            "popup refresh should preserve the currently open flight detail page");
        AssertTrue(fuelLabel.Contains("GetGhostFlightRoundTripFuel(flight)"),
            "route traffic fuel summary should include more than just outbound ship fuel");
        AssertTrue(row.Contains("BuildGhostFlightCargoLabel(flight, compactModules: true)"),
            "route traffic rows should use compact module cargo stacks");
        AssertTrue(cargoLabel.Contains("bool compactModules = false")
                   && cargoLabel.Contains("BuildRouteModuleDisplayGroups(flight?.moduleManifest)")
                   && cargoLabel.Contains("? $\"{group.Icon}x{group.Count}\"")
                   && cargoLabel.Contains(": $\"{group.Icon} {group.Name} x{group.Count}\""),
            "route traffic cargo labels should stack repeated modules as icon-counts while detail labels keep names");
    }

    private static void RouteFlightRecordsFreezeDiagnosticInputs()
    {
        var data = ReadRepoFile("LogisticsMod", "Data", "LogisticsTypes.cs");
        var observer = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var flightRecord = ExtractClass(data, "public class GhostFlightRecord");
        var commit = ExtractMethod(observer, "private static bool TryCommitGhostDeliveryConvoy(");

        AssertTrue(flightRecord.Contains("launchFuelManifest")
                && flightRecord.Contains("launchSupportLabels"),
            "frozen flight records should keep launch fuel resource details and launch support labels");
        AssertTrue(flightRecord.Contains("outboundDeltaV")
                && flightRecord.Contains("returnDeltaV")
                && flightRecord.Contains("outboundMassToFuel")
                && flightRecord.Contains("returnMassToFuel")
                && flightRecord.Contains("exhaustVelocity")
                && flightRecord.Contains("fuelPowVariable"),
            "frozen flight records should store the numbers used by the fuel formula");
        AssertTrue(flightRecord.Contains("tankFuelBeforeLaunch")
                && flightRecord.Contains("originFuelTopUp")
                && flightRecord.Contains("tankFuelAtDeparture")
                && flightRecord.Contains("tankFuelAfterOutbound"),
            "frozen flight records should store tank state around dispatch");
        AssertTrue(commit.Contains("launchFuelManifest = BuildGhostFlightLaunchFuelManifest(plans)")
                && commit.Contains("outboundDeltaV = outboundDeltaV")
                && commit.Contains("outboundMassToFuel ="),
            "route dispatch should populate the diagnostic fields when the flight is frozen");
        AssertTrue(observer.Contains("plan.AvailableDeltaV = flight.AvailableDeltaV")
                && observer.Contains("plan.RouteKind = flight.RouteKind.ToString()")
                && observer.Contains("plan.FlightPlanMode = flight.FlightPlanMode"),
            "leg planning should carry planner diagnostics into the frozen flight");
    }

    private static void RouteCalculatorUsesLoadedPropellantEffectiveDeltaVGate()
    {
        var source = ReadRepoFile("LogisticsMod", "Logic", "LogisticsFlightCalculator.cs");
        var method = ExtractMethod(source, "private static double EstimateAvailableDeltaV(");
        AssertTrue(method.Contains("CalculateLoadedPropellantEffectiveDeltaV"),
            "route flight planning should use loaded-propellant effective delta-v as the feasibility gate");
        AssertTrue(method.Contains("vehicle.FuelCapacity"),
            "route flight planning should derive the gate from the craft tank capacity");
        AssertFalse(method.Contains("GetPorkchopEffectiveDeltaV"),
            "route flight planning must not fall back to the static SpacecraftType.AvailableDeltaV gate");

        AssertNoRoutePorkchopImplementation(source);
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
        AssertTrue(groupedFuel.Contains("GetCarriedReturnFuel(destinationRefuel, plan.ReturnLeg)")
                && groupedFuel.Contains(": Math.Max(0.0, plan.Outbound?.Fuel ?? 0.0) + carriedReturnFuel"),
            "when destination refuel is unavailable, the launch tank requirement should include both outbound and return fuel");
        AssertTrue(groupedFuel.Contains("plan.OriginFuelTopUp = Math.Max(0.0, requiredTankAtDeparture - plan.Craft.tankFuel)"),
            "origin top-up should load missing round-trip fuel before launch");

        var singlePlan = ExtractMethod(source, "private static bool TryBuildGhostDeliveryPlan(");
        AssertTrue(singlePlan.Contains("out var returnLeg, Data.LogisticsFlightPlanMode.Optimal"),
            "return reserve should be estimated with Optimal mode even when outbound is Fast");
        AssertTrue(singlePlan.Contains("var maxOutboundFuel = fuelType == null || scType.SolarSC"),
            "single-craft planning should compute the outbound fuel budget after return reserve is known");
        AssertTrue(singlePlan.Contains("Math.Max(0.0, craft.tankFuelCapacity - carriedReturnFuel)"),
            "when destination refuel is unavailable, outbound Fast planning should protect return fuel capacity");
        AssertTrue(singlePlan.Contains("maxOutboundFuel")
                && singlePlan.Contains("out outbound, null, maxOutboundFuel"),
            "outbound planning should receive the return-protected fuel budget");
        AssertTrue(singlePlan.Contains("var requiredTankAtDeparture = tankerPlan")
                && singlePlan.Contains(": outbound.Fuel + carriedReturnFuel"),
            "single-craft planning should use the same destination-refuel branch before convoy normalization");
        AssertTrue(singlePlan.Contains(": outbound.Fuel + carriedReturnFuel"),
            "single-craft planning should also carry return fuel when the destination cannot refuel");
    }

    private static void RouteLaunchPayloadIncludesLoadedSpacecraftFuel()
    {
        var source = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var helper = ExtractMethod(source, "private static double CalculateLoadedLaunchPayload(");
        var refresh = ExtractMethod(source, "private static bool RefreshGhostLaunchPlanForLoadedPayload(");
        var launchPlan = ExtractMethod(source, "private static bool TryBuildGhostLaunchPlan(");
        var groupedFuel = ExtractMethod(source, "private static bool NormalizeConvoyPlanFuelToVanillaGroup(");
        var singlePlan = ExtractMethod(source, "private static bool TryBuildGhostDeliveryPlan(");
        var mixedPlan = ExtractMethod(source, "private static bool TryBuildMixedGhostDeliveryPlan(");

        AssertTrue(helper.Contains("var tankFuelAtDeparture = plan.TankFuelAtDeparture > 0.001"),
            "launch payload should use the planned tank fuel loaded at departure");
        AssertTrue(helper.Contains("GetCarriedReturnFuel(plan.DestinationRefuel, plan.ReturnLeg)"),
            "launch payload fallback should only include return propellant when it is carried from origin");
        AssertTrue(helper.Contains("return dryMass + cargoMass + tankFuelAtDeparture;"),
            "loaded launch payload should be dry mass plus cargo plus loaded tank propellant");
        AssertTrue(refresh.Contains("TryBuildGhostLaunchPlan(provider, plan.LaunchPayload"),
            "launch support should be sized from the loaded launch payload");
        AssertTrue(singlePlan.Contains("RefreshGhostLaunchPlanForLoadedPayload(plan, player, snapshot, out reason)"),
            "single-resource routes should refresh launch payload after leg fuel is known");
        AssertTrue(mixedPlan.Contains("RefreshGhostLaunchPlanForLoadedPayload(plan, player, snapshot, out reason)"),
            "mixed-manifest routes should refresh launch payload after leg fuel is known");
        AssertTrue(groupedFuel.Contains("RefreshGhostLaunchPlanForLoadedPayload(plan, player, null, out reason)"),
            "final grouped convoy fuel should refresh the frozen launch plan before commit");
        AssertTrue(launchPlan.Contains("if (payloadMass > singlePayload + 0.05) continue;")
                   && launchPlan.Contains("if (payloadMass > optionCapacity + 0.05) continue;")
                   && launchPlan.Contains("return true;")
                   && launchPlan.Contains("return false;"),
            "a loaded spacecraft launch should select one support option that can lift the whole payload");
        AssertFalse(launchPlan.Contains("var remaining = payloadMass")
                    || launchPlan.Contains("remaining -= chunk"),
            "spacecraft launch support must not split one loaded launch payload across multiple support types");
        AssertFalse(singlePlan.Contains("var launchPayload = scType.GetMass(player) + payloadCargoMass"),
            "single-resource route launch payload must not be dry-plus-cargo only");
        AssertFalse(mixedPlan.Contains("var launchPayload = scType.GetMass(player) + payloadCargoMass"),
            "mixed route launch payload must not be dry-plus-cargo only");
    }

    private static void SameFuelRoutesDeliverSurplusTankFuel()
    {
        var observer = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var data = ReadRepoFile("LogisticsMod", "Data", "LogisticsTypes.cs");
        var ui = ReadRepoFile("LogisticsMod", "UI", "LogisticsUI.cs");
        var manifestBuilder = ExtractMethod(observer, "private static List<RouteManifestCargoItem> BuildBalancedRouteCargoManifest(");
        var tanker = ExtractMethod(observer, "private static bool TryCalculateTankerOutboundLeg(");
        var flightManifest = ExtractMethod(observer, "private static List<Data.GhostFlightCargoRecord> BuildGhostFlightCargoManifest(");
        var complete = ExtractMethod(observer, "private static void CompleteGhostOutboundFlight(");
        var detail = ExtractMethod(ui, "private void ShowRouteFlightDetail(");

        AssertTrue(observer.Contains("EnsureRouteFuelTankManifestMarker(manifest, states, scType)"),
            "same-fuel route demand should be available to the tank planner even when cargo space is full");
        AssertTrue(manifestBuilder.Contains("!IsSpacecraftFuelResource(scType, state.Item.Resource)"),
            "fuel cargo should use leftover hold space after non-fuel route resources");
        AssertTrue(tanker.Contains("tankPayloadEstimate = tankCapacity"),
            "tanker planning should model a full tank at departure");
        AssertTrue(tanker.Contains("deliveryLimit - nextTankDelivered"),
            "cargo hold fuel should only cover demand left after tank delivery");
        AssertTrue(flightManifest.Contains("plan.TankFuelDeliveryResource")
                && flightManifest.Contains("plan.TankFuelDelivered"),
            "tank-delivered fuel should be part of the frozen arrival manifest");
        AssertTrue(complete.Contains("flight.tankFuelDelivered")
                && complete.Contains("craft.tankFuel = Math.Max(0.0, craft.tankFuel - tankFuelDeliveredPerCraft)"),
            "arrival should unload tank-delivered fuel before starting the return leg");
        AssertTrue(data.Contains("tankFuelDeliveryResourceId")
                && data.Contains("tankFuelAtArrivalAfterUnload"),
            "flight records should persist tanker accounting for the detail page");
        AssertTrue(detail.Contains("Tank fuel delivered")
                && detail.Contains("Cargo hold fuel")
                && detail.Contains("Tank after unload"),
            "flight detail should expose tank and cargo fuel delivery buckets");
    }

    private static void LogisticsArrivalsNotifyVanillaDeliveryObjectives()
    {
        var observer = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var complete = ExtractMethod(observer, "private static void CompleteGhostOutboundFlight(");
        var lift = ExtractMethod(observer, "private static bool ApplyVirtualLiftResourceChanges(");
        var direct = ExtractMethod(observer, "private static bool ApplyDirectResourceTransfer(");
        var notify = ExtractMethod(observer, "private static void NotifyVanillaDeliveryObjectives(ObjectInfo source,");
        var apply = ExtractMethod(observer, "private static bool TryApplyGhostDeliveryToVanillaObjective(");
        var match = ExtractMethod(observer, "private static bool MatchesVanillaDeliveryEndpoint(");
        var delivered = ExtractMethod(observer, "private static double GetDeliveredAmountForVanillaObjective(");

        AssertTrue(complete.Contains("NotifyVanillaDeliveryObjectives(flight, destination, player, manifest)"),
            "completed ghost deliveries should notify vanilla contract delivery objectives after cargo is added");
        AssertTrue(lift.Contains("NotifyVanillaDeliveryObjectives(providerOI, requester, player, BuildSingleCargoManifest(rd, plan.PayloadAmount))"),
            "launch-vehicle and facility lift deliveries should notify vanilla contract delivery objectives after cargo is added");
        AssertTrue(direct.Contains("NotifyVanillaDeliveryObjectives(providerOI, requester, player, BuildSingleCargoManifest(rd, amount))"),
            "direct logistics stock transfers should notify vanilla contract delivery objectives after cargo is added");
        AssertTrue(notify.Contains("contractManager.ActiveContracts"),
            "delivery notification should scan active vanilla contracts");
        AssertTrue(apply.Contains("objectiveData.howMuchCurrent += delivered"),
            "ghost delivery notification should advance vanilla delivery progress");
        AssertTrue(apply.Contains("objectiveData.MarkAsComplete()"),
            "ghost delivery notification should complete vanilla objectives once enough cargo has arrived");
        AssertTrue(apply.Contains("RaiseVanillaObjectiveProgress(objectiveData)"),
            "partial ghost deliveries should refresh vanilla objective progress rows");
        AssertTrue(match.Contains("CompanyObjectiveData.CheckIsOkAdvance"),
            "advanced delivery objectives should use vanilla endpoint matching");
        AssertTrue(delivered.Contains("EResourceTypeType.resorces")
                && delivered.Contains("EResourceTypeType.crew")
                && delivered.Contains("id_resource_human"),
            "delivery notification should handle resource and crew delivery objectives");
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

    private static void RouteDispatchBuildsBalancedMixedManifests()
    {
        var source = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var convoy = ExtractMethod(source, "private static string TryCreateRouteGhostConvoys(");
        var manifestBuilder = ExtractMethod(source, "private static List<RouteManifestCargoItem> BuildBalancedRouteCargoManifest(");
        var itemBuilder = ExtractMethod(source, "private static bool TryBuildRouteManifestCargoItem(");
        var filter = ExtractMethod(source, "private static void FilterRouteStatesForFullLoads(");
        var commit = ExtractMethod(source, "private static bool TryCommitGhostDeliveryConvoy(");
        var removals = ExtractMethod(source, "private static List<ResourceRemoval> BuildGhostDeliveryPlanRemovals(");
        var flightManifest = ExtractMethod(source, "private static List<Data.GhostFlightCargoRecord> BuildGhostFlightCargoManifest(");

        AssertTrue(convoy.Contains("TryBuildRouteGhostManifestPlan("),
            "route convoy dispatch should build one balanced manifest per craft instead of selecting one resource");
        AssertTrue(manifestBuilder.Contains("var shareMass = capacityLeft / priorityGroup.Count"),
            "equal-priority route resources should split remaining craft capacity evenly");
        AssertTrue(itemBuilder.Contains("state.Remaining - manifestPlanned"),
            "a mixed manifest must not allocate more of a resource than its remaining route demand");
        AssertTrue(manifestBuilder.Contains("priority = cargoActive.Max(state => state.Item.Priority)"),
            "mixed manifests should still respect route priority buckets");
        AssertTrue(filter.Contains("CanBuildMixedFullRouteLoad("),
            "full-load filtering should keep partial resource states when the combined manifest can fill a craft");
        AssertTrue(commit.Contains("foreach (var cargo in GetGhostDeliveryCargoItems(plan))"),
            "committing a mixed plan should apply every cargo item in the manifest");
        AssertTrue(removals.Contains("foreach (var cargo in GetGhostDeliveryCargoItems(plan))"),
            "resource removals should cover every cargo item in a mixed manifest");
        AssertTrue(flightManifest.Contains("GetGhostDeliveryCargoItems(plan)"),
            "flight rows should be built from the mixed cargo manifest");
        AssertTrue(commit.Contains("plan.Craft.tankFuel = Math.Max(0, plan.Craft.tankFuel - plan.Outbound.Fuel);"),
            "a mixed manifest should spend flight fuel once for the craft, not once per cargo resource");
    }

    private static void HumanRouteLaunchSupportRespectsCrewSafeFacilities()
    {
        var source = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var canLift = ExtractMethod(source, "private static bool CanLiftResourceWithSupport(");
        var crewSafe = ExtractMethod(source, "private static bool IsCrewSafeLaunchSupport(");
        var violent = ExtractMethod(source, "private static bool IsViolentCrewLaunchSupportCategory(");
        var preferElevator = ExtractMethod(source, "private static List<LaunchSupportOption> PreferSpaceElevatorLaunchSupport(");
        var ghostSupport = ExtractMethod(source, "private static List<LaunchSupportOption> GetGhostLaunchSupport(");

        AssertFalse(canLift.Contains("IsSharedFacilityLiftSupport(option) && !IsSpaceElevatorSupport(option)"),
            "human launch support must not block every shared facility except space elevators");
        AssertTrue(crewSafe.Contains("\"launch-pad\"")
                   && crewSafe.Contains("\"space-elevator\"")
                   && crewSafe.Contains("\"reserved-launch-vehicle\"")
                   && crewSafe.Contains("\"standard-launch\""),
            "humans should be allowed on launch pads, space elevators, and real launch vehicles");
        AssertTrue(violent.Contains("\"magnetic-launch-rails\"")
                   && violent.Contains("\"rotary-launcher\"")
                   && violent.Contains("\"electromagnetic-catapult\"")
                   && violent.Contains("\"stationary-mass-driver\""),
            "humans should be blocked only from violent launch support categories");
        AssertTrue(preferElevator.Contains("support.Where(IsSpaceElevatorSupport)")
                   && preferElevator.Contains("elevatorSupport.Count > 0 ? elevatorSupport : support"),
            "space elevator support should supersede fuel-burning launch options when available");
        AssertTrue(ghostSupport.Contains("preferSpaceElevator: false")
                   && ghostSupport.IndexOf("IsRouteFacilityLaunchAllowed", StringComparison.Ordinal) <
                   ghostSupport.IndexOf("PreferSpaceElevatorLaunchSupport(result)", StringComparison.Ordinal),
            "route-disabled space elevators must be filtered before space elevator preference removes other route-enabled facilities");
    }

    private static void RouteConvoyFailuresPropagateResourceStatus()
    {
        var source = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var method = ExtractMethod(source, "private static string TryCreateRouteGhostConvoys(");

        AssertTrue(method.Contains("var failureReason = bestReason ?? \"No route convoy dispatched\"")
                   && method.Contains("rule.statusNote = failureReason"),
            "failed route convoy planning should put the best rejection reason on affected resource rows");
        AssertTrue(method.Contains("GHOST route-convoy-blocked"),
            "failed route convoy planning should be visible in verbose diagnostics");
    }

    private static void LaunchPadFacilitySupportStaysPassiveWithoutFakeLv()
    {
        var source = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var detector = ExtractMethod(source, "private static bool TryGetBuiltEnabledLaunchSupportCategory(");
        var passive = ExtractMethod(source, "private static bool IsPassiveLaunchPadFacility(");

        AssertTrue(detector.Contains("IsPassiveLaunchPadFacility(facility.facilityDescriptor, category)"),
            "plain launch pads should not be added as standalone virtual launch support");
        AssertTrue(passive.Contains("\"launch-pad\"")
                   && passive.Contains("ResolveFakeLaunchSupportType(descriptor as GroundFacilityDescriptor) == null"),
            "launch pads without vanilla fake launch vehicle types should stay passive launch-cost bonuses");
    }

    private static void BuiltFacilityRouteLiftIsNotShadowedByFakeLaunchVehicles()
    {
        var source = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var built = ExtractMethod(source, "private static void AddBuiltFacilityLaunchSupport(");
        var replace = ExtractMethod(source, "private static void ReplaceVehicleBackedLaunchSupportForBuiltFacility(");
        var hasBuilt = ExtractMethod(source, "private static bool HasBuiltFacilityLaunchSupport(");
        var virtualSupport = ExtractMethod(source, "private static List<LaunchSupportOption> GetVirtualSurfaceLiftSupport(");

        AssertTrue(built.Contains("ReplaceVehicleBackedLaunchSupportForBuiltFacility(result, facility, category)")
                   && built.Contains("HasBuiltFacilityLaunchSupport(result, category)"),
            "built launch facilities should replace their own fake launch vehicle support before duplicate checks");
        AssertTrue(replace.Contains("option.Vehicle != null")
                   && replace.Contains("ReferenceEquals(option.Facility, facility)")
                   && replace.Contains("NormalizeLaunchSupportCategory(option.Category)"),
            "fake vehicle-backed support from the same built facility should not shadow direct route lift support");
        AssertTrue(hasBuilt.Contains("option.Vehicle == null")
                   && hasBuilt.Contains("option.Facility != null")
                   && hasBuilt.Contains("NormalizeLaunchSupportCategory(option.Category)"),
            "duplicate built facility checks should only count direct built-facility support entries");
        AssertTrue(virtualSupport.Contains("option.Vehicle.IsReadyToLaunchReusable()")
                   && virtualSupport.Contains("IsBuiltEnabledLaunchSupportFacility(option.Facility, option.Category)"),
            "route lift support should still validate real vehicles and built facilities through their proper readiness paths");
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
        AssertTrue(calculator.Contains("step=fuel-single"),
            "route diagnostics should log single-flight fuel inputs without reintroducing route search logs");
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

    private static void RoutePlannerAvoidsPorkchopGridInNormalPath()
    {
        var calculator = ReadRepoFile("LogisticsMod", "Logic", "LogisticsFlightCalculator.cs");
        var calculate = ExtractMethod(calculator, "public static LogisticsCalculatedFlight CalculateSoonestOptimalFlight(");
        AssertTrue(calculate.Contains("EstimateFlightTimingPlan("),
            "normal route flight calculation should use one cheap planner for dates and delta-v");
        AssertFalse(calculate.Contains("TryCalculateInstantPorkchop("),
            "normal route flight calculation must not call the 201x201 porkchop scan");
        AssertTrue(calculator.Contains("CalculateHohmannTransfer("),
            "route planner should use the PyKEP-style Hohmann equation");
        AssertTrue(calculator.Contains("TryGetTransferWindowState("),
            "route planner should measure current phase against the next transfer window");
        AssertTrue(calculator.Contains("EstimateBadWindowChaseDeltaV(")
                && calculator.Contains("CalculateBadWindowChaseDeltaV("),
            "Fast route planning should add geometric chase delta-v when departing outside a window");
        AssertFalse(calculator.Contains("ApplyTransferWindowPenalty("),
            "Fast route planning should not use the old small phase multiplier");
        AssertTrue(calculator.Contains("FastTransferFactors"),
            "Fast route planning should use a small fixed candidate set");
        AssertTrue(calculator.Contains("FastRouteMinimumTransferFactor")
                && calculator.Contains("Clamp(vehicle.Type.MinFlightTimeHohRel, FastRouteMinimumTransferFactor, 0.95)"),
            "Fast route planning should not let extreme ship minimum time-of-flight collapse logistics routes");
        AssertNoRoutePorkchopImplementation(calculator);
    }

    private static void RoutePlannerPreservesEstimatedTransferDates()
    {
        var calculator = ReadRepoFile("LogisticsMod", "Logic", "LogisticsFlightCalculator.cs");
        var calculate = ExtractMethod(calculator, "public static LogisticsCalculatedFlight CalculateSoonestOptimalFlight(");

        AssertTrue(calculate.Contains("var departure = now.AddDays(Math.Max(0.0, timingPlan.DepartureDelayDays))"),
            "route flight calculation should keep a concrete estimated departure date");
        AssertTrue(calculate.Contains("var arrival = departure.AddDays(Math.Max(0.1, timingPlan.TravelDays))"),
            "route flight calculation should derive arrival from the selected estimate");
        AssertTrue(calculate.Contains("result.Departure = departure"),
            "route flight records should preserve the selected estimated departure date");
        AssertTrue(calculate.Contains("result.Arrival = arrival"),
            "route flight records should preserve the selected estimated arrival date");
        AssertTrue(calculator.Contains("departureDelayDays = transferWindow.DepartureDelayDays"),
            "Optimal window-aware routes should preserve the wait before departure");
    }

    private static void RoutePlannerRemovesPorkchopImplementation()
    {
        var calculator = ReadRepoFile("LogisticsMod", "Logic", "LogisticsFlightCalculator.cs");
        var math = ReadRepoFile("LogisticsMod", "Logic", "LogisticsVanillaMissionMath.cs");

        AssertNoRoutePorkchopImplementation(calculator);
        AssertFalse(math.Contains("LogisticsPorkchopCandidate"),
            "vanilla math helper should not keep route porkchop candidate DTOs");
        AssertFalse(math.Contains("TrySelectPorkchopCandidate"),
            "vanilla math helper should not keep route porkchop candidate selection");
        AssertFalse(math.Contains("GetPorkchopEffectiveDeltaV"),
            "vanilla math helper should not keep the static porkchop effective-delta-v gate");
        AssertFalse(math.Contains("IsBetterPorkchopCandidate"),
            "vanilla math helper should not keep route porkchop tie-break rules");
        AssertFalse(calculator.Contains("LogisticsRouteFlightPlanCache"),
            "implementation should not add route-plan caching in the first pass");
    }

    private static void BodyMoonRoutesUseVanillaMoonCaseData()
    {
        var calculator = ReadRepoFile("LogisticsMod", "Logic", "LogisticsFlightCalculator.cs");
        var earthMoon = ExtractMethod(calculator, "private static double EstimateEarthMoonDeltaV(");
        var moonCase = ExtractMethod(calculator, "private static bool TryGetMoonCaseDeltaV(");
        var optimal = ExtractMethod(calculator, "private static double EstimateOptimalDeltaV(");

        AssertTrue(earthMoon.Contains("TryGetMoonCaseDeltaV(start, target, out var deltaV)"),
            "Earth-Moon estimates should first use vanilla moon case data when present");
        AssertTrue(moonCase.Contains("startTargetDVMinForMoonMooon"),
            "body-moon estimates should read the vanilla ObjectInfo moon-case table instead of invented constants");
        AssertTrue(moonCase.Contains("GetEarthMoonDeltaVMultiplier()"),
            "moon-case delta-v should keep vanilla economic multiplier behavior");
        AssertTrue(optimal.Contains("case LogisticsFlightRouteKind.ParentChild:")
                && optimal.Contains("TryGetMoonCaseDeltaV(start, target, out var moonDeltaV)"),
            "parent-child routes should preserve confirmed moon-case values before falling back");
    }

    private static void FastRoutePlanningProtectsReturnFuelBudget()
    {
        var calculator = ReadRepoFile("LogisticsMod", "Logic", "LogisticsFlightCalculator.cs");
        var calculate = ExtractMethod(calculator, "public static LogisticsCalculatedFlight CalculateSoonestOptimalFlight(");
        var fastSelector = ExtractMethod(calculator, "private static bool TrySelectFastTransferCandidate(");
        var feasibility = ExtractMethod(calculator, "private static bool IsFeasibleCandidate(");

        AssertTrue(calculate.Contains("double maxFlightFuel = double.PositiveInfinity"),
            "route flight calculation should accept an optional per-leg fuel budget");
        AssertTrue(calculate.Contains("GetEffectiveFlightFuelBudget(vehicle, maxFlightFuel)"),
            "final fuel acceptance should respect the per-leg fuel budget");
        AssertTrue(fastSelector.Contains("maxFlightFuel"),
            "Fast candidate selection should receive the per-leg fuel budget");
        AssertTrue(feasibility.Contains("fuel > GetEffectiveFlightFuelBudget(vehicle, maxFlightFuel)"),
            "Fast candidates should be rejected before they consume protected return reserve");
        AssertTrue(calculate.Contains("Flight fuel exceeds reserved tank budget"),
            "blocked reasons should distinguish return-reserve budget failures from raw tank failures");
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
                || observer.Contains("CalculateSoonestOptimalFlight(from, to, vehicle, cargo, player,\r\n            requestedFlightPlanMode, maxFlightFuel)")
                || observer.Contains("CalculateSoonestOptimalFlight(from, to, vehicle, cargo, player,\n            requestedFlightPlanMode, maxFlightFuel)"),
            "dispatch should pass the selected Fast/Optimal mode into the route flight planner");
        AssertTrue(observer.Contains("Data.LogisticsNetwork.GetRouteSpacecraftFlightPlanMode(route, craft?.shipTypeId)"),
            "dispatch should read the type-level route plan, not a per-craft setting");
    }

    private static void RouteFinalPartialLoadsCanDispatch()
    {
        var observer = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var filter = ExtractMethod(observer, "private static void FilterRouteStatesForFullLoads(");
        var finalPartial = ExtractMethod(observer, "private static bool IsFinalPartialRouteLoad(");
        var builder = ExtractMethod(observer, "private static bool TryBuildGhostDeliveryPlan(");

        AssertTrue(filter.Contains("IsFinalPartialRouteLoad(state, minimumFullLoad)"),
            "route full-load filtering should allow final partial route loads");
        AssertTrue(filter.Contains("state.AllowPartialLoad = true"),
            "route final partial state should flow into dispatch planning");
        AssertTrue(finalPartial.Contains("state.Item.Outstanding + 0.001 < minimumFullLoad"),
            "final partial loads should only apply when the remaining route target is smaller than one full shipload");
        AssertTrue(finalPartial.Contains("state.Item.Available + 0.001 >= state.Item.Outstanding"),
            "final partial loads should wait until the source can satisfy the remaining route target");
        AssertTrue(builder.Contains("allowPartialRouteLoad"),
            "route flight building should receive the final-partial exception");
        AssertTrue(builder.Contains("!allowPartialRouteLoad"),
            "route flight building should keep the normal full-load gate outside final partial loads");
    }

    private static void RouteHumanPartialLoadsCanDispatchWhenUseful()
    {
        var observer = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var manifestPlan = ExtractMethod(observer, "private static bool TryBuildRouteGhostManifestPlan(");
        var humanPartial = ExtractMethod(observer, "private static bool IsUsefulHumanRoutePartialLoad(");

        AssertTrue(observer.Contains("HumanRoutePartialLoadMinimumFillRatio = 0.5"),
            "human partial route loads should require a useful fraction of ship capacity");
        AssertTrue(manifestPlan.Contains("!IsUsefulHumanRoutePartialLoad(manifest, capacity)"),
            "mixed route planning should allow useful human partial manifests through the full-load gate");
        AssertTrue(humanPartial.Contains("IsHumanResource(item?.Resource)")
                   && humanPartial.Contains("capacity * HumanRoutePartialLoadMinimumFillRatio"),
            "the human partial exception should require human cargo and a minimum fill ratio");
    }

    private static void RoutePriorityPartialLoadsCanDispatch()
    {
        var observer = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var manifestPlan = ExtractMethod(observer, "private static bool TryBuildRouteGhostManifestPlan(");
        var filter = ExtractMethod(observer, "private static void FilterRouteStatesForFullLoads(");
        var priorityGate = ExtractMethod(observer, "private static bool IsPriorityAllowedRoutePartialLoad(");
        var priorityOpen = ExtractMethod(observer, "private static bool CanOpenPriorityPartialRouteLoad(");
        var priorityBuild = ExtractMethod(observer, "private static bool CanBuildPriorityPartialRouteLoad(");
        var payloadEstimate = ExtractMethod(observer, "private static double EstimateRouteStatePotentialPayloadMass(");

        AssertTrue(observer.Contains("HighRoutePartialLoadMinimumFillRatio = 0.1"),
            "high priority route cargo should have a ten-percent partial-load threshold");
        AssertTrue(manifestPlan.Contains("!IsPriorityAllowedRoutePartialLoad(manifest, capacity)")
                   && manifestPlan.IndexOf("!IsPriorityAllowedRoutePartialLoad(manifest, capacity)", StringComparison.Ordinal)
                   < manifestPlan.IndexOf("!IsUsefulHumanRoutePartialLoad(manifest, capacity)", StringComparison.Ordinal),
            "route manifest validation should allow priority partial cargo before falling back to the human partial exception");
        AssertTrue(priorityGate.Contains("highestPriority >= 2")
                   && priorityGate.Contains("return true")
                   && priorityGate.Contains("highestPriority >= 1")
                   && priorityGate.Contains("capacity * HighRoutePartialLoadMinimumFillRatio"),
            "critical cargo should dispatch with any payload, while high cargo should require ten percent capacity");
        AssertTrue(filter.Contains("CanBuildPriorityPartialRouteLoad(")
                   && filter.Contains("CanOpenPriorityPartialRouteLoad(state")
                   && filter.Contains("state.AllowPartialLoad = true")
                   && filter.Contains("if (allowPriorityPartialLoads)")
                   && filter.Contains("continue;"),
            "the full-load prefilter should preserve urgent partial cargo and lower-priority piggyback cargo");
        AssertTrue(priorityBuild.Contains("totalPotentialPayloadMass")
                   && priorityBuild.Contains("EstimateRouteStatesPotentialPayloadMass")
                   && priorityBuild.Contains("CanOpenPriorityPartialRouteLoad"),
            "high-priority partial launch decisions should consider the total manifest that can ride with the urgent item");
        AssertTrue(priorityOpen.Contains("priority >= 2")
                   && priorityOpen.Contains("priority < 1")
                   && priorityOpen.Contains("ownPotentialPayloadMass")
                   && priorityOpen.Contains("totalPotentialPayloadMass")
                   && priorityOpen.Contains("HighRoutePartialLoadMinimumFillRatio"),
            "only high and critical route priorities should open partial dispatches");
        AssertTrue(payloadEstimate.Contains("GetRouteSourceAvailableAfterKeep")
                   && payloadEstimate.Contains("state.Remaining")
                   && payloadEstimate.Contains("GetPayloadMassPerResourceUnit"),
            "priority partial decisions should be based on dispatchable payload mass, not raw resource units");
    }

    private static void RouteTrafficLiveRefreshSkipsUnchangedRows()
    {
        var ui = ReadRepoFile("LogisticsMod", "UI", "LogisticsUI.cs");
        var refresh = ExtractMethod(ui, "private void RefreshOpenRouteEditorOnTimeAdvance(");
        var signature = ExtractMethod(ui, "private string BuildRouteEditorLiveRefreshSignature(");

        AssertTrue(ui.Contains("private string _lastRouteEditorRefreshSignature;"),
            "route editor should remember a live refresh signature");
        AssertTrue(refresh.Contains("BuildRouteEditorLiveRefreshSignature(route)"),
            "route editor live refresh should compare the visible route data before rebuilding");
        AssertTrue(refresh.Contains("string.Equals(refreshSignature, _lastRouteEditorRefreshSignature"),
            "route editor live refresh should skip unchanged route rows during time advance");
        AssertTrue(ui.Contains("RememberRouteEditorLiveRefreshState(route);"),
            "route editor should capture the current signature after rebuilding");
        AssertTrue(signature.Contains("data?.ghostFlights"),
            "route traffic signature should include visible ghost flight rows");
        AssertTrue(signature.Contains("data?.ghostCraft"),
            "route traffic signature should include assigned craft rows");
        AssertTrue(signature.Contains("module-drag|") && refresh.Contains("moduleDragHintChanged"),
            "route editor live refresh should rebuild when the module drop hint appears or disappears");
    }

    private static void RouteResourceEditorDefaultsToTarget()
    {
        var ui = ReadRepoFile("LogisticsMod", "UI", "LogisticsUI.cs");
        var editor = ExtractMethod(ui, "private void ShowRouteResourceInput(");

        AssertTrue(editor.Contains("var editingKeep = false;"),
            "route resource editor should default to editing Target");
        var targetIndex = editor.IndexOf("targetButton = AddBigButtonInline", StringComparison.Ordinal);
        var keepIndex = editor.IndexOf("keepButton = AddBigButtonInline", StringComparison.Ordinal);
        AssertTrue(targetIndex >= 0 && keepIndex > targetIndex,
            "route resource editor should render Target before Keep");
    }

    private static void RouteStatusResourceIdsRenderAsLabels()
    {
        var ui = ReadRepoFile("LogisticsMod", "UI", "LogisticsUI.cs");
        var clean = ExtractMethod(ui, "private static string CleanRouteStatus(");
        var replace = ExtractMethod(ui, "private static string ReplaceResourceIdsInStatus(");
        var tokenStart = ExtractMethod(ui, "private static bool IsResourceIdTokenStart(");
        var tokenChar = ExtractMethod(ui, "private static bool IsResourceIdTokenChar(");

        AssertTrue(clean.Contains("ReplaceResourceIdsInStatus(status.Trim())"),
            "route status cleanup should replace raw resource ids before displaying status text");
        AssertTrue(replace.Contains("LooksLikeResourceId(token)")
                   && replace.Contains("ResourceLabel(ResolveResource(token), token)")
                   && replace.Contains("builder.Append(replacement)")
                   && replace.Contains("builder.Append(text, lastAppend, text.Length - lastAppend)"),
            "route status resource-id replacement should preserve surrounding text while using the normal icon label");
        AssertTrue(tokenStart.Contains("index > 0 && IsResourceIdTokenChar(text[index - 1])")
                   && tokenStart.Contains("char.IsLetter(text[index])"),
            "resource-id replacement should start only at whole token boundaries");
        AssertTrue(tokenChar.Contains("char.IsLetterOrDigit(value) || value == '_'"),
            "resource-id replacement should keep underscores in ids such as id_resource_fuel");
    }

    private static void LogisticsPopupStaysBelowVanillaAlerts()
    {
        var ui = ReadRepoFile("LogisticsMod", "UI", "LogisticsUI.cs");
        var patches = ReadRepoFile("LogisticsMod", "Patches", "ObjectInfoWindowPatches.cs");
        var openPopup = ExtractMethod(ui, "private void OpenPopup(");
        var swapPopup = ExtractMethod(ui, "private void SwapPopupToObject(");
        var placePopup = ExtractMethod(ui, "private void PlacePopupOnWindowLayer(");
        var findAlert = ExtractMethod(ui, "private static Transform FindActiveVanillaAlertWindow(");
        var isAlert = ExtractMethod(ui, "private static bool IsVanillaAlertWindow(");

        AssertTrue(openPopup.Contains("PlacePopupOnWindowLayer()")
                   && swapPopup.Contains("PlacePopupOnWindowLayer()"),
            "logistics popup open/swap should use the managed window layer instead of blindly becoming last sibling");
        AssertFalse(openPopup.Contains("_popupRoot.transform.SetAsLastSibling()")
                    || swapPopup.Contains("_popupRoot.transform.SetAsLastSibling()"),
            "logistics popup should not force itself above vanilla alerts when opened");
        AssertTrue(placePopup.Contains("FindActiveVanillaAlertWindow(parent)")
                   && placePopup.Contains("popupTransform.SetSiblingIndex(alertWindow.GetSiblingIndex())")
                   && placePopup.Contains("popupTransform.SetAsLastSibling()"),
            "logistics popup should sit just below active alerts and above normal windows otherwise");
        AssertTrue(findAlert.Contains("SerializedMonoBehaviourSingleton<UIManager>.Instance?.Current2")
                   && findAlert.Contains("currentAlert.Open")
                   && findAlert.Contains("IsVanillaAlertWindow(currentAlert.transform)"),
            "logistics popup layer lookup should respect UIManager's active popup window track");
        AssertTrue(isAlert.Contains("global::PopUpWindowYesNo")
                   && isAlert.Contains("global::TriviaWindow")
                   && isAlert.Contains("EWindowType.PopUpWindowYESNO")
                   && isAlert.Contains("EWindowType.TriviaWindow"),
            "logistics popup layer lookup should classify vanilla alert and trivia windows");
        AssertTrue(patches.Contains("[HarmonyPatch(typeof(UIManager), \"Open\")]")
                   && patches.Contains("PlaceOpenPopupsOnWindowLayer()"),
            "vanilla UIManager opens should nudge logistics below alerts that appear after it");
    }

    private static void TimeTickKeepsGhostFlightVisualsAlive()
    {
        var patch = ReadRepoFile("LogisticsMod", "Patches", "TimeControllerPatches.cs");
        var prefix = ExtractMethod(patch, "private static void Prefix(");

        AssertFalse(prefix.Contains("DisableGhostFlightVisuals("),
            "time controller updates must not destroy and recreate ghost flight visuals every frame");
        AssertTrue(prefix.Contains("UpdateGhostFlightVisuals()"),
            "time controller updates should refresh existing ghost flight visuals in place");
    }

    private static void SaveLoadDoesNotDispatchRoutes()
    {
        var saveLoad = ReadRepoFile("LogisticsMod", "Patches", "SaveLoadPatches.cs");
        var timePatch = ReadRepoFile("LogisticsMod", "Patches", "TimeControllerPatches.cs");
        var observer = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var onDayChange = ExtractMethod(observer, "public static void OnDayChange(");

        AssertFalse(saveLoad.Contains("QueuePostLoadPlanning")
                    || saveLoad.Contains("PendingPostLoadTrigger"),
            "loading a save should not queue route planning or dispatch");
        AssertFalse(timePatch.Contains("OnDayChange(0)")
                    || timePatch.Contains("PendingPostLoadTrigger"),
            "the time update patch should not convert save load into a zero-day logistics tick");
        AssertTrue(onDayChange.Contains("if (days <= 0)")
                   && onDayChange.Contains("return;"),
            "zero-day logistics calls should not dispatch routes");
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

    private static void LogisticsCustomIconsAreNormalizedInSharedUiPaths()
    {
        var ui = ReadRepoFile("LogisticsMod", "UI", "LogisticsUI.cs");
        var fixedIcon = ExtractMethod(ui, "private static Image AddFixedIconImage(");
        var objectIcon = ExtractMethod(ui, "private Image AddObjectIcon(");
        var tableIcon = ExtractMethod(ui, "private TextMeshProUGUI AddTableIconCell(");
        var normalize = ExtractMethod(ui, "private static string NormalizeLogisticsIconText(");
        var resourceLabel = ExtractMethod(ui, "private static string ResourceLabel(");
        var compactResourceLabel = ExtractMethod(ui, "private static string CompactResourceLabel(");
        var shipIcon = ExtractMethod(ui, "private static string ShipIcon(");
        var moduleRow = ExtractMethod(ui, "private void AddRoutePendingModuleRow(");
        var moduleIcon = ExtractMethod(ui, "private void AddRouteModuleIconCell(");
        var collapsedIcon = ExtractMethod(ui, "private TextMeshProUGUI AddCollapsedRouteResourceIcon(");
        var iconButton = ExtractMethod(ui, "private void AddRouteResourceIconButton(");
        var launchTile = ExtractMethod(ui, "private Button AddRouteFacilityLaunchTile(");

        AssertTrue(ui.Contains("LogisticsInlineIconSpritePercent")
                   && ui.Contains("LogisticsCompactIconSpritePercent")
                   && ui.Contains("LogisticsTableIconSpritePercent"),
            "logistics UI should define shared icon size percentages instead of one-off custom icon fixes");
        AssertTrue(fixedIcon.Contains("layout.minWidth = width")
                   && fixedIcon.Contains("layout.minHeight = height")
                   && fixedIcon.Contains("image.preserveAspect = true")
                   && fixedIcon.Contains("image.raycastTarget = false"),
            "sprite icons should render in fixed layout slots while preserving aspect ratio");
        AssertTrue(objectIcon.Contains("AddFixedIconImage(parent, \"ObjectIcon\""),
            "object/location icons should use the fixed icon slot helper");
        AssertTrue(normalize.Contains("text.IndexOf(\"<sprite\"")
                   && normalize.Contains("text.IndexOf(\"<size=\"")
                   && normalize.Contains("Mathf.Clamp(sizePercent")
                   && normalize.Contains("<size={percent}%>"),
            "TMP sprite icons should be wrapped with a bounded size tag once");
        AssertTrue(tableIcon.Contains("NormalizeLogisticsIconText(icon, iconSizePercent)")
                   && tableIcon.Contains("label.margin = Vector4.zero"),
            "table icon cells should normalize TMP sprite markup before display");
        AssertTrue(resourceLabel.Contains("NormalizeLogisticsIconText(rd?.IconString")
                   && compactResourceLabel.Contains("NormalizeLogisticsIconText(rd?.IconString"),
            "resource labels should normalize custom resource icons before combining them with text");
        AssertTrue(shipIcon.Contains("NormalizeLogisticsIconText(objManager.spriteTextStart5.MyFormat"),
            "spacecraft, launch vehicle, and launch-support TMP sprite icons should normalize at the shared ShipIcon source");
        AssertTrue(moduleRow.Contains("AddRouteModuleIconCell(row.transform, module)")
                   && moduleIcon.Contains("RouteFacilityLaunchDescriptorSprite(descriptor)")
                   && moduleIcon.Contains("AddFixedIconImage(parent, \"ModuleIcon\"")
                   && moduleIcon.Contains("Color.white")
                   && moduleIcon.Contains("AddTableIconCell(parent, RouteModuleIcon(module)"),
            "queued module cargo should prefer fixed sprite slots and fall back to normalized TMP icons");
        AssertTrue(collapsedIcon.Contains("NormalizeLogisticsIconText(icon, LogisticsCompactIconSpritePercent)")
                   && iconButton.Contains("NormalizeLogisticsIconText(icon, LogisticsTableIconSpritePercent)"),
            "compact route icon strips and icon buttons should normalize resource/module TMP icons");
        AssertTrue(launchTile.Contains("AddFixedIconImage(btnGo.transform, \"Icon\", option.IconSprite")
                   && launchTile.Contains("NormalizeLogisticsIconText(iconText, LogisticsTableIconSpritePercent)"),
            "facility launch tiles should bound both sprite and TMP fallback icons");
    }

    private static void RouteModuleCargoDragDropShipsAsGhostInventory()
    {
        var types = ReadRepoFile("LogisticsMod", "Data", "LogisticsTypes.cs");
        var persistence = ReadRepoFile("LogisticsMod", "Data", "LogisticsPersistence.cs");
        var network = ReadRepoFile("LogisticsMod", "Data", "LogisticsNetwork.cs");
        var observer = ReadRepoFile("LogisticsMod", "Logic", "LogisticsObserver.cs");
        var ui = ReadRepoFile("LogisticsMod", "UI", "LogisticsUI.cs");
        var moduleRow = ExtractMethod(ui, "private void AddRoutePendingModuleRow(");
        var moduleEditStep = ExtractMethod(ui, "private static int RouteModuleEditStep(");
        var moduleName = ExtractMethod(ui, "private static string RouteModuleDisplayName(");
        var moduleGroups = ExtractMethod(ui, "private static List<RouteModuleDisplayGroup> BuildRouteModuleDisplayGroups(");
        var routeLift = ExtractMethod(observer, "private static void ApplyBalancedRouteVirtualSurfaceLift(");
        var moduleLift = ExtractMethod(observer, "private static void TryApplyRouteModuleSurfaceLift(");
        var moduleLiftPlan = ExtractMethod(observer, "private static bool TryBuildRouteModuleSurfaceLiftPlan(");
        var moduleLiftApply = ExtractMethod(observer, "private static bool ApplyVirtualLiftModuleChanges(");

        AssertTrue(types.Contains("public List<GhostFlightModuleRecord> pendingModules"),
            "routes should persist queued module cargo before dispatch");
        AssertTrue(types.Contains("public List<GhostFlightModuleRecord> moduleManifest"),
            "ghost flights should persist module cargo after dispatch");
        AssertTrue(persistence.Contains("pendingModules = (route.pendingModules"),
            "route save data should write pending module cargo");
        AssertTrue(network.Contains("TryAddPendingRouteModule")
                   && network.Contains("module.Scrap(queuedCount, addResourceOnScrap: false)")
                   && network.Contains("requestedCount")
                   && network.Contains("No more matching modules are available at the route source"),
            "dropping or row-adding modules should remove the requested real source module count into ghost inventory");

        AssertTrue(ui.Contains("IDragAndDropTarget")
                   && ui.Contains("HandleRouteModuleDrop")
                   && ui.Contains("AddRouteModuleCargoSection")
                   && ui.Contains("AddRoutePendingModules")
                   && ui.Contains("AddHorizontalDottedDropLines")
                   && ui.Contains("\"Drop modules here\"")
                   && ui.Contains("var showDropHint = IsRouteModuleDragActive()")
                   && ui.Contains("if (showDropHint)")
                   && ui.Contains("AddHorizontalDottedDropLines(dropRow, TertiaryTextColor)")
                   && ui.Contains("\"Drop modules here\", 0f, 14f, SecondaryTextColor")
                   && ui.Contains("TryAddPendingRouteModule(route, module, player, out var reason)")
                   && moduleRow.Contains("AddRoutePendingModules(section, route, module?.moduleId, RouteModuleEditStep())")
                   && moduleEditStep.Contains("KeyCode.LeftShift")
                   && moduleEditStep.Contains("KeyCode.LeftControl"),
            "route editor should expose an apparent vanilla drag/drop target and row editor for module cargo");
        AssertFalse(moduleRow.Contains("\"+10\"")
                    || moduleRow.Contains("\"+100\""),
            "module row bulk editing should use Shift/Ctrl modifiers instead of extra visible buttons");
        AssertFalse(ui.Contains("Next module drop")
                    || ui.Contains("Next drop")
                    || ui.Contains("\"Module cargo\"")
                    || ui.Contains("\"DROP MODULES HERE\""),
            "module cargo should use drag-first, edit-row-after flow without a separate global drop modifier section");
        AssertTrue(ui.Contains("item.helpObj is SpaceModule || item.item is SpaceModuleDescriptor"),
            "drop hint should appear only for active module drags");
        AssertTrue(observer.Contains("ModuleItems")
                   && observer.Contains("BuildGhostFlightModuleManifest")
                   && observer.Contains("TryDeliverGhostFlightModules")
                   && observer.Contains("AddResourcesAndModules(cargoAll, cancelationFly: false, cyclicalMission: false)"),
            "route dispatch should carry modules on ghost flights and install them on arrival");
        AssertTrue(moduleName.Contains("NormalizeAssetDisplayName(name)"),
            "queued and flight module cargo names should soften all-caps vanilla descriptor names");
        AssertTrue(ui.Contains("private sealed class RouteModuleDisplayGroup")
                   && moduleGroups.Contains("group.Count++")
                   && moduleGroups.Contains("RouteModuleIcon(module)")
                   && moduleGroups.Contains("RouteModuleMass(module)"),
            "flight module cargo should be grouped by display identity before rendering counts");
        AssertTrue(routeLift.Contains("TryApplyRouteModuleSurfaceLift(route, source, destination, support, capacityState, player, ref capacityLeft)")
                   && routeLift.Contains("MarkRouteReservedLaunchVehiclesUsed(source, player, capacityState.ReservedCapacityUsed.Keys)"),
            "surface-to-orbit route lift should process queued modules before falling back to spacecraft convoys");
        AssertTrue(moduleLift.Contains("GetDispatchableRouteModules(route, player)")
                   && moduleLift.Contains("TryBuildRouteModuleSurfaceLiftPlan")
                   && moduleLift.Contains("ApplyVirtualLiftModuleChanges")
                   && moduleLift.Contains("route.pendingModules.Remove(module)"),
            "queued route modules should be removable by launch-vehicle-only surface lift");
        AssertTrue(moduleLiftPlan.Contains("ReservedLaunchCapacityByVehicle")
                   && moduleLiftPlan.Contains("GetVirtualLiftFuelAvailable")
                   && moduleLiftPlan.Contains("SharedFacilityCapacityUsed"),
            "module surface lift should share the same reserved/shared launch capacity accounting as resource lift");
        AssertTrue(moduleLiftApply.Contains("sourceData.RemoveResource")
                   && moduleLiftApply.Contains("AddResourcesAndModules(cargoAll, cancelationFly: false, cyclicalMission: false)"),
            "module surface lift should spend launch fuel and install real module cargo at the orbit target");
    }

    private static double TotalFuel(double massToFuel, double deltaV, double exhaustVelocity)
    {
        return LogisticsVanillaMissionMath.CalculateTotalPropellantNeeded(massToFuel, deltaV, exhaustVelocity, 2.0);
    }

    private static void AssertNoRoutePorkchopImplementation(string source)
    {
        AssertFalse(source.Contains("TryCalculateInstantPorkchop"),
            "route flight calculation must not keep the old instant porkchop implementation");
        AssertFalse(source.Contains("LambertPorkchop"),
            "route flight calculation must not reference the vanilla Lambert porkchop type");
        AssertFalse(source.Contains("ComputeLambert2"),
            "route flight calculation must not call the vanilla Lambert grid method");
        AssertFalse(source.Contains("InstantPorkchopCache"),
            "route flight calculation must not rely on same-tick porkchop cache reuse");
        AssertFalse(source.Contains("PorkchopIntervals"),
            "route flight calculation must not define a 201x201 porkchop scan size");
        AssertFalse(source.Contains("RoutePhysDate"),
            "route flight calculation must not convert visible route dates into vanilla porkchop physical dates");
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

    private static string ExtractClass(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException("Could not find class " + signature);

        var openBrace = source.IndexOf('{', start);
        if (openBrace < 0)
            throw new InvalidOperationException("Could not find class body for " + signature);

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

        throw new InvalidOperationException("Could not parse class body for " + signature);
    }
}
}
