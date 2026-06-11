using System;
using System.Collections.Generic;
using Data.ScriptableObject;
using Game;
using Game.Info;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;
using LogisticsFlightPlanMode = LogisticsMod.Data.LogisticsFlightPlanMode;

namespace LogisticsMod.Logic;

internal enum LogisticsFlightRouteKind
{
    SameObject,
    LocalOrbit,
    EarthMoon,
    SameParent,
    ParentChild,
    ConstantAcceleration,
    Interplanetary
}

internal sealed class LogisticsFlightVehicleSnapshot
{
    public SpacecraftType Type;
    public double DryMass;
    public double CargoCapacity;
    public double FuelCapacity;
    public double AvailableFuel;
    public double DesignAvailableDeltaV;
    public double Thrust;
    public double ExhaustVelocity;
    public ResourceDefinition FuelType;
    public bool SolarPowered;

    public static LogisticsFlightVehicleSnapshot FromGhostCraft(
        Data.GhostCraftRecord craft,
        SpacecraftType type,
        Company company)
    {
        if (type == null || company == null)
            return null;

        return new LogisticsFlightVehicleSnapshot
        {
            Type = type,
            DryMass = Math.Max(1.0, type.GetMass(company)),
            CargoCapacity = Math.Max(0.0, type.GetCargoCapacity(company)),
            FuelCapacity = Math.Max(0.0, craft != null && craft.tankFuelCapacity > 0 ? craft.tankFuelCapacity : type.GetFuelCapacity(company)),
            AvailableFuel = Math.Max(0.0, craft != null ? craft.tankFuel : 0.0),
            DesignAvailableDeltaV = Math.Max(0.0, type.AvailableDeltaV),
            Thrust = Math.Max(0.0, type.GetThrust(company)),
            ExhaustVelocity = Math.Max(0.001, type.GetExhaustV(company)),
            FuelType = type.GetFuelType(),
            SolarPowered = type.SolarSC
        };
    }
}

internal sealed class LogisticsFlightCargoSnapshot
{
    public double CargoMass;
    public ResourceDefinition Resource;
    public double Amount;
}

internal sealed class LogisticsCalculatedFlight
{
    public bool Success;
    public string Reason;
    public LogisticsFlightRouteKind RouteKind;
    public DateTime Departure;
    public DateTime Arrival;
    public double TravelDays;
    public double EstimatedDeltaV;
    public double AvailableDeltaV;
    public double FlightFuel;
    public double LaunchFuel;
    public ResourceDefinition FuelType;
    public LogisticsFlightPlanMode FlightPlanMode;
}

internal static class LogisticsFlightCalculator
{
    private const double DefaultEarthMoonTravelDays = 7.0;
    private const double DefaultEarthMoonDeltaV = 3.2;
    private const double GravitationalConstant = 6.674080038626684E-11;
    private const double AstronomicalUnitMeters = 149599993856.0;
    private const double MetersPerKilometer = 1000.0;
    private const double TwoPi = Math.PI * 2.0;
    private const double RadiansPerDegree = Math.PI / 180.0;
    private const double FastHighEnergyExponent = 1.55;
    private const double FastRouteMinimumTransferFactor = 0.55;
    private const double TransferWindowNowToleranceRadians = 10.0 * RadiansPerDegree;
    private static readonly double[] FastTransferFactors = { 0.85, 0.70, 0.55 };

    private readonly struct LogisticsFlightTimingPlan
    {
        public LogisticsFlightTimingPlan(double departureDelayDays, double travelDays, double deltaV)
        {
            DepartureDelayDays = Math.Max(0.0, departureDelayDays);
            TravelDays = Math.Max(0.1, travelDays);
            DeltaV = Math.Max(0.0, deltaV);
        }

        public double DepartureDelayDays { get; }
        public double TravelDays { get; }
        public double DeltaV { get; }
    }

    private readonly struct LogisticsTransferWindowState
    {
        public LogisticsTransferWindowState(
            double departureDelayDays,
            double phaseErrorRadians,
            double synodicDays,
            double sourceAngleRadians,
            double targetAngleRadians,
            double targetAngularVelocityRadiansPerPhysicsSecond,
            double targetOrbitRadiusMeters)
        {
            DepartureDelayDays = Math.Max(0.0, departureDelayDays);
            PhaseErrorRadians = phaseErrorRadians;
            SynodicDays = Math.Max(0.0, synodicDays);
            SourceAngleRadians = sourceAngleRadians;
            TargetAngleRadians = targetAngleRadians;
            TargetAngularVelocityRadiansPerPhysicsSecond = targetAngularVelocityRadiansPerPhysicsSecond;
            TargetOrbitRadiusMeters = Math.Max(0.0, targetOrbitRadiusMeters);
        }

        public double DepartureDelayDays { get; }
        public double PhaseErrorRadians { get; }
        public double SynodicDays { get; }
        public double SourceAngleRadians { get; }
        public double TargetAngleRadians { get; }
        public double TargetAngularVelocityRadiansPerPhysicsSecond { get; }
        public double TargetOrbitRadiusMeters { get; }
    }

    public static LogisticsCalculatedFlight CalculateSoonestOptimalFlight(
        ObjectInfo start,
        ObjectInfo target,
        LogisticsFlightVehicleSnapshot vehicle,
        LogisticsFlightCargoSnapshot cargo,
        Company company,
        LogisticsFlightPlanMode flightPlanMode = LogisticsFlightPlanMode.Optimal,
        double maxFlightFuel = double.PositiveInfinity)
    {
        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        var result = new LogisticsCalculatedFlight
        {
            Success = false,
            Reason = null,
            Departure = now,
            Arrival = now,
            FuelType = vehicle?.FuelType,
            RouteKind = LogisticsFlightRouteKind.Interplanetary
        };

        if (start == null || target == null)
        {
            result.Reason = "Missing flight endpoints";
            return result;
        }

        if (vehicle == null || vehicle.Type == null || company == null)
        {
            result.Reason = "Missing flight vehicle";
            return result;
        }

        cargo ??= new LogisticsFlightCargoSnapshot();

        var effectiveFlightPlanMode = ResolveEffectiveFlightPlanMode(vehicle.Type, flightPlanMode);
        result.RouteKind = ClassifyRoute(start, target, vehicle);
        result.FlightPlanMode = effectiveFlightPlanMode;
        var timingPlan = EstimateFlightTimingPlan(start, target, vehicle, cargo, company, result.RouteKind,
            effectiveFlightPlanMode, maxFlightFuel);
        var departure = now.AddDays(Math.Max(0.0, timingPlan.DepartureDelayDays));
        var arrival = departure.AddDays(Math.Max(0.1, timingPlan.TravelDays));

        result.TravelDays = Math.Max(0.1, (arrival - departure).TotalDays);
        result.Departure = departure;
        result.Arrival = arrival > departure ? arrival : departure.AddDays(result.TravelDays);
        result.EstimatedDeltaV = timingPlan.DeltaV;
        result.AvailableDeltaV = EstimateAvailableDeltaV(vehicle, cargo, company);
        result.FlightFuel = EstimateFlightFuel(vehicle, cargo, company, result.EstimatedDeltaV, result.RouteKind);
        result.LaunchFuel = 0.0;

        var flightFuelBudget = GetEffectiveFlightFuelBudget(vehicle, maxFlightFuel);
        if (!vehicle.SolarPowered && result.FlightFuel > flightFuelBudget + 0.001)
        {
            result.Reason = flightFuelBudget + 0.001 < Math.Max(0.0, vehicle.FuelCapacity)
                ? "Flight fuel exceeds reserved tank budget"
                : "Flight fuel exceeds tank capacity";
            return result;
        }

        result.Success = true;
        return result;
    }

    public static double EstimateSoonestOptimalTravelDays(ObjectInfo start, ObjectInfo target)
    {
        return EstimateSoonestOptimalTravelDays(start, target, null, null, null, ClassifyRoute(start, target, null));
    }

    public static LogisticsFlightPlanMode NormalizeFlightPlanMode(LogisticsFlightPlanMode mode)
    {
        return mode == LogisticsFlightPlanMode.Fast
            ? LogisticsFlightPlanMode.Fast
            : LogisticsFlightPlanMode.Optimal;
    }

    public static LogisticsFlightPlanMode ResolveEffectiveFlightPlanMode(SpacecraftType type, LogisticsFlightPlanMode requested)
    {
        requested = NormalizeFlightPlanMode(requested);
        if (type?.SolarSC == true)
            return LogisticsFlightPlanMode.Optimal;
        return requested == LogisticsFlightPlanMode.Fast
            ? LogisticsFlightPlanMode.Fast
            : LogisticsFlightPlanMode.Optimal;
    }

    private static LogisticsFlightRouteKind ClassifyRoute(ObjectInfo start, ObjectInfo target, LogisticsFlightVehicleSnapshot vehicle)
    {
        if (start == null || target == null)
            return LogisticsFlightRouteKind.Interplanetary;
        if (start == target)
            return LogisticsFlightRouteKind.SameObject;
        if (IsOrbitOf(start, target) || IsOrbitOf(target, start) || SafeCheckOrbitCase(start, target))
            return LogisticsFlightRouteKind.LocalOrbit;
        if (IsEarthMoonRoute(start, target))
            return LogisticsFlightRouteKind.EarthMoon;
        if (vehicle?.Type != null && vehicle.Type.NotUsePorkchope)
            return LogisticsFlightRouteKind.ConstantAcceleration;

        var startBody = GetCanonicalBody(start);
        var targetBody = GetCanonicalBody(target);
        if (startBody != null && targetBody != null)
        {
            if (startBody.parentObjectInfo != null && startBody.parentObjectInfo == targetBody.parentObjectInfo)
                return LogisticsFlightRouteKind.SameParent;
            if (startBody.parentObjectInfo == targetBody || targetBody.parentObjectInfo == startBody)
                return LogisticsFlightRouteKind.ParentChild;
        }

        return LogisticsFlightRouteKind.Interplanetary;
    }

    private static double EstimateSoonestOptimalTravelDays(
        ObjectInfo start,
        ObjectInfo target,
        LogisticsFlightVehicleSnapshot vehicle,
        LogisticsFlightCargoSnapshot cargo,
        Company company,
        LogisticsFlightRouteKind routeKind)
    {
        switch (routeKind)
        {
            case LogisticsFlightRouteKind.SameObject:
                return 1.0;
            case LogisticsFlightRouteKind.LocalOrbit:
                return 0.5;
            case LogisticsFlightRouteKind.EarthMoon:
                return GetEarthMoonOptimalTravelDays();
            case LogisticsFlightRouteKind.SameParent:
                if (TryEstimateHohmannDeltaV(start, target, out _, out var sameParentDays))
                    return sameParentDays;
                return 20.0;
            case LogisticsFlightRouteKind.ParentChild:
                if (TryGetMoonCaseDeltaV(start, target, out _))
                    return GetEarthMoonOptimalTravelDays();
                return 10.0;
            case LogisticsFlightRouteKind.ConstantAcceleration:
                if (TryEstimateConstantAccelerationTravelDays(start, target, vehicle, cargo, company, out var constantDays))
                    return constantDays;
                return EstimateHohmannLikeDays(start, target);
            default:
                if (TryEstimateHohmannDeltaV(start, target, out _, out var interplanetaryDays))
                    return interplanetaryDays;
                return EstimateHohmannLikeDays(start, target);
        }
    }

    private static LogisticsFlightTimingPlan EstimateFlightTimingPlan(
        ObjectInfo start,
        ObjectInfo target,
        LogisticsFlightVehicleSnapshot vehicle,
        LogisticsFlightCargoSnapshot cargo,
        Company company,
        LogisticsFlightRouteKind routeKind,
        LogisticsFlightPlanMode flightPlanMode,
        double maxFlightFuel)
    {
        var optimalDays = EstimateSoonestOptimalTravelDays(start, target, vehicle, cargo, company, routeKind);
        var optimalDeltaV = EstimateOptimalDeltaV(start, target, vehicle, routeKind);
        var selectedDays = optimalDays;
        var selectedDeltaV = optimalDeltaV;
        var departureDelayDays = 0.0;
        var hasWindow = TryGetTransferWindowState(start, target, routeKind, optimalDays, out var transferWindow);

        if (flightPlanMode != LogisticsFlightPlanMode.Fast || vehicle?.SolarPowered == true)
        {
            if (hasWindow)
                departureDelayDays = transferWindow.DepartureDelayDays;
            return new LogisticsFlightTimingPlan(departureDelayDays, selectedDays, selectedDeltaV);
        }

        if (TrySelectFastTransferCandidate(start, target, vehicle, cargo, company, routeKind,
                optimalDays, selectedDeltaV, maxFlightFuel, hasWindow, transferWindow,
                out var fastDays, out var fastDeltaV))
        {
            selectedDays = fastDays;
            selectedDeltaV = fastDeltaV;
        }

        return new LogisticsFlightTimingPlan(0.0, selectedDays, selectedDeltaV);
    }

    private static double EstimateOptimalDeltaV(ObjectInfo start, ObjectInfo target, LogisticsFlightVehicleSnapshot vehicle, LogisticsFlightRouteKind routeKind)
    {
        switch (routeKind)
        {
            case LogisticsFlightRouteKind.SameObject:
                return 0.0;
            case LogisticsFlightRouteKind.LocalOrbit:
                return EstimateLocalOrbitDeltaV(start, target);
            case LogisticsFlightRouteKind.EarthMoon:
                return EstimateEarthMoonDeltaV(start, target);
            case LogisticsFlightRouteKind.SameParent:
                if (TryEstimateHohmannDeltaV(start, target, out var sameParentDeltaV, out _))
                    return sameParentDeltaV;
                return 2.5;
            case LogisticsFlightRouteKind.ParentChild:
                if (TryGetMoonCaseDeltaV(start, target, out var moonDeltaV))
                    return moonDeltaV;
                return 2.0;
            case LogisticsFlightRouteKind.ConstantAcceleration:
                return Math.Max(1.0, EstimateInterplanetaryDeltaV(start, target) * 0.75);
            default:
                if (TryEstimateHohmannDeltaV(start, target, out var deltaV, out _))
                    return deltaV;
                return EstimateInterplanetaryDeltaV(start, target);
        }
    }

    private static bool TrySelectFastTransferCandidate(
        ObjectInfo start,
        ObjectInfo target,
        LogisticsFlightVehicleSnapshot vehicle,
        LogisticsFlightCargoSnapshot cargo,
        Company company,
        LogisticsFlightRouteKind routeKind,
        double optimalDays,
        double optimalDeltaV,
        double maxFlightFuel,
        bool hasTransferWindow,
        LogisticsTransferWindowState transferWindow,
        out double selectedDays,
        out double selectedDeltaV)
    {
        selectedDays = optimalDays;
        selectedDeltaV = optimalDeltaV;

        if (vehicle == null || routeKind == LogisticsFlightRouteKind.SameObject
            || routeKind == LogisticsFlightRouteKind.LocalOrbit
            || routeKind == LogisticsFlightRouteKind.EarthMoon
            || routeKind == LogisticsFlightRouteKind.ConstantAcceleration
            || optimalDays <= 0.1 || optimalDeltaV <= 0.001)
        {
            return false;
        }

        var availableDeltaV = EstimateAvailableDeltaV(vehicle, cargo, company);
        var selectedFuel = double.MaxValue;
        var found = false;

        foreach (var factor in BuildFastTransferFactors(vehicle))
        {
            var candidateDays = Clamp(optimalDays * factor, 1.0, Math.Max(1.0, optimalDays));
            var compressedDeltaV = LogisticsVanillaMissionMath.EstimateHighEnergyDeltaV(
                optimalDeltaV,
                optimalDays,
                candidateDays,
                FastHighEnergyExponent);
            var compressionExtraDeltaV = Math.Max(0.0, compressedDeltaV - optimalDeltaV);
            var badWindowChaseDeltaV = hasTransferWindow
                ? EstimateBadWindowChaseDeltaV(transferWindow, candidateDays)
                : 0.0;
            var candidateDeltaV = Clamp(
                optimalDeltaV + compressionExtraDeltaV + badWindowChaseDeltaV,
                optimalDeltaV,
                120.0);
            var candidateFuel = EstimateFlightFuelValue(vehicle, cargo, company, candidateDeltaV);
            if (!IsFeasibleCandidate(vehicle, candidateDeltaV, candidateFuel, availableDeltaV, maxFlightFuel))
                continue;

            var closeArrival = found && Math.Abs(candidateDays - selectedDays) <= Math.Max(1.0, optimalDays * 0.03);
            var better = !found
                || (closeArrival
                    ? candidateFuel < selectedFuel
                    : candidateDays < selectedDays);
            if (!better)
                continue;

            selectedDays = candidateDays;
            selectedDeltaV = candidateDeltaV;
            selectedFuel = candidateFuel;
            found = true;
        }

        return found;
    }

    private static IEnumerable<double> BuildFastTransferFactors(LogisticsFlightVehicleSnapshot vehicle)
    {
        var minimumFactor = vehicle?.Type != null
            ? Clamp(vehicle.Type.MinFlightTimeHohRel, FastRouteMinimumTransferFactor, 0.95)
            : FastRouteMinimumTransferFactor;
        yield return 1.0;

        var emittedMinimum = false;
        foreach (var factor in FastTransferFactors)
        {
            if (factor + 0.001 < minimumFactor)
                continue;
            if (Math.Abs(factor - minimumFactor) <= 0.001)
                emittedMinimum = true;
            yield return factor;
        }

        if (!emittedMinimum)
            yield return minimumFactor;
    }

    private static bool IsFeasibleCandidate(LogisticsFlightVehicleSnapshot vehicle, double deltaV, double fuel,
        double availableDeltaV, double maxFlightFuel)
    {
        if (vehicle == null || deltaV <= 0.001 || double.IsNaN(deltaV) || double.IsInfinity(deltaV))
            return false;
        if (vehicle.SolarPowered)
            return true;
        if (deltaV > availableDeltaV + 0.001)
            return false;
        if (fuel > GetEffectiveFlightFuelBudget(vehicle, maxFlightFuel) + 0.001)
            return false;
        return vehicle.FuelCapacity <= 0.001 || fuel <= vehicle.FuelCapacity + 0.001;
    }

    private static double GetEffectiveFlightFuelBudget(LogisticsFlightVehicleSnapshot vehicle, double maxFlightFuel)
    {
        if (vehicle == null || vehicle.SolarPowered)
            return double.PositiveInfinity;

        var tankBudget = vehicle.FuelCapacity > 0.001
            ? vehicle.FuelCapacity
            : double.PositiveInfinity;
        if (double.IsNaN(maxFlightFuel))
            return tankBudget;
        return Math.Min(tankBudget, Math.Max(0.0, maxFlightFuel));
    }

    private static double EstimateFlightFuel(
        LogisticsFlightVehicleSnapshot vehicle,
        LogisticsFlightCargoSnapshot cargo,
        Company company,
        double deltaV,
        LogisticsFlightRouteKind routeKind)
    {
        if (vehicle == null || vehicle.SolarPowered || vehicle.FuelType == null || deltaV <= 0.001)
        {
            LogRouteMission(
                $"step=fuel-single result=0 reason=free-or-missing route={routeKind} solar={vehicle?.SolarPowered} fuelType={vehicle?.FuelType?.ID ?? "null"} dV={deltaV:0.###}");
            return 0.0;
        }

        var mass = CalculateSingleCraftMassToFuel(vehicle, cargo);
        var fuel = EstimateFlightFuelValue(vehicle, cargo, company, deltaV);
        LogRouteMission(
            $"step=fuel-single ships=1 dry={vehicle.DryMass:0.###} cargo={cargo?.CargoMass ?? 0.0:0.###} mass={mass:0.###} dV={deltaV:0.###} exhaust={vehicle.ExhaustVelocity:0.###} pow={GetPowVariable():0.###} fuel={fuel:0.###} route={routeKind}");
        return fuel;
    }

    private static double EstimateFlightFuelValue(
        LogisticsFlightVehicleSnapshot vehicle,
        LogisticsFlightCargoSnapshot cargo,
        Company company,
        double deltaV)
    {
        if (vehicle == null || vehicle.SolarPowered || vehicle.FuelType == null || deltaV <= 0.001)
            return 0.0;

        return LogisticsVanillaMissionMath.CalculateTotalPropellantNeeded(
            CalculateSingleCraftMassToFuel(vehicle, cargo),
            deltaV,
            vehicle.ExhaustVelocity,
            GetPowVariable());
    }

    private static double CalculateSingleCraftMassToFuel(
        LogisticsFlightVehicleSnapshot vehicle,
        LogisticsFlightCargoSnapshot cargo)
    {
        return LogisticsVanillaMissionMath.CalculateMassToFuel(
            vehicle?.DryMass ?? 0.0,
            cargo?.CargoMass ?? 0.0,
            1);
    }

    private static double EstimateAvailableDeltaV(LogisticsFlightVehicleSnapshot vehicle, LogisticsFlightCargoSnapshot cargo, Company company)
    {
        if (vehicle == null)
            return 0.0;

        if (vehicle.SolarPowered)
            return double.PositiveInfinity;

        var loadedFuelDeltaV = LogisticsVanillaMissionMath.CalculateLoadedPropellantEffectiveDeltaV(
            vehicle.DryMass,
            cargo?.CargoMass ?? 0.0,
            vehicle.FuelCapacity,
            vehicle.ExhaustVelocity);
        return loadedFuelDeltaV > 0.001
            ? loadedFuelDeltaV
            : Math.Max(0.0, vehicle.DesignAvailableDeltaV);
    }

    private static double EstimateTankFraction(
        LogisticsFlightRouteKind routeKind,
        LogisticsFlightCargoSnapshot cargo,
        LogisticsFlightVehicleSnapshot vehicle)
    {
        double fraction;
        switch (routeKind)
        {
            case LogisticsFlightRouteKind.SameObject:
                fraction = 0.0;
                break;
            case LogisticsFlightRouteKind.LocalOrbit:
                fraction = 0.04;
                break;
            case LogisticsFlightRouteKind.EarthMoon:
            case LogisticsFlightRouteKind.ParentChild:
                fraction = 0.08;
                break;
            case LogisticsFlightRouteKind.SameParent:
                fraction = 0.12;
                break;
            default:
                fraction = 0.20;
                break;
        }

        var cargoLoad = vehicle != null && vehicle.CargoCapacity > 0.001
            ? Math.Max(0.0, cargo?.CargoMass ?? 0.0) / vehicle.CargoCapacity
            : 0.0;
        return fraction * (1.0 + Math.Min(1.0, cargoLoad) * 0.35);
    }

    private static bool TryEstimateConstantAccelerationTravelDays(
        ObjectInfo start,
        ObjectInfo target,
        LogisticsFlightVehicleSnapshot vehicle,
        LogisticsFlightCargoSnapshot cargo,
        Company company,
        out double days)
    {
        days = 0.0;
        if (start == null || target == null || vehicle == null || vehicle.Thrust <= 0.0)
            return false;

        var distance = EstimateDistanceMeters(start, target);
        var mass = Math.Max(1.0, vehicle.DryMass + Math.Max(0.0, cargo?.CargoMass ?? 0.0));
        var acceleration = vehicle.Thrust / (mass * 1000.0);
        if (acceleration <= 0.0001)
            return false;

        acceleration = Math.Min(acceleration, GetConstantAccelerationMax());
        var seconds = 2.0 * Math.Sqrt(distance / Math.Max(GetConstantAccelerationMin(), acceleration));
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0.0)
            return false;

        days = Clamp(seconds / 86400.0, 1.0, 600.0);
        return true;
    }

    private static double EstimateHohmannLikeDays(ObjectInfo start, ObjectInfo target)
    {
        if (TryEstimateHohmannDeltaV(start, target, out _, out var hohmannDays))
            return hohmannDays;

        var startBody = GetCanonicalBody(start);
        var targetBody = GetCanonicalBody(target);
        var r1 = Math.Max(0.01, startBody?.DistanceToSunInAU ?? 0.0);
        var r2 = Math.Max(0.01, targetBody?.DistanceToSunInAU ?? 0.0);
        var semiMajorAxis = Math.Max(0.01, (r1 + r2) / 2.0);
        var days = 365.25 * Math.Pow(semiMajorAxis, 1.5) / 2.0;

        if (Math.Abs(r1 - r2) < 0.03)
            days = 90.0;

        return Clamp(days, 30.0, 600.0);
    }

    private static double EstimateInterplanetaryDeltaV(ObjectInfo start, ObjectInfo target)
    {
        if (TryEstimateHohmannDeltaV(start, target, out var deltaV, out _))
            return deltaV;

        var startBody = GetCanonicalBody(start);
        var targetBody = GetCanonicalBody(target);
        var r1 = Math.Max(0.01, startBody?.DistanceToSunInAU ?? 1.0);
        var r2 = Math.Max(0.01, targetBody?.DistanceToSunInAU ?? 1.0);
        return Clamp(3.0 + Math.Abs(r1 - r2) * 6.5, 3.0, 90.0);
    }

    private static bool TryEstimateHohmannDeltaV(ObjectInfo start, ObjectInfo target, out double deltaV,
        out double travelDays)
    {
        deltaV = 0.0;
        travelDays = 0.0;
        if (!TryGetHohmannInputs(start, target, out var r1, out var r2, out var mu))
            return false;

        var transfer = LogisticsVanillaMissionMath.CalculateHohmannTransfer(r1, r2, mu);
        var deltaVKmS = transfer.DeltaV / MetersPerKilometer;
        if (deltaVKmS <= 0.001 || transfer.TravelDays <= 0.001
            || double.IsNaN(deltaVKmS) || double.IsInfinity(deltaVKmS))
        {
            return false;
        }

        deltaV = Clamp(deltaVKmS, 0.1, 120.0);
        travelDays = Clamp(transfer.TravelDays, 1.0, 600.0);
        return true;
    }

    private static bool TryGetHohmannInputs(ObjectInfo start, ObjectInfo target, out double r1, out double r2,
        out double mu)
    {
        r1 = 0.0;
        r2 = 0.0;
        mu = 0.0;
        var startBody = GetCanonicalBody(start);
        var targetBody = GetCanonicalBody(target);
        if (startBody == null || targetBody == null || startBody == targetBody)
            return false;

        var center = startBody.parentObjectInfo != null && startBody.parentObjectInfo == targetBody.parentObjectInfo
            ? startBody.parentObjectInfo
            : null;
        if (center == null || center.Mass <= 0.0)
            return false;

        if (!TryGetOrbitalRadiusMeters(startBody, center, out r1)
            || !TryGetOrbitalRadiusMeters(targetBody, center, out r2))
        {
            return false;
        }

        var larger = Math.Max(r1, r2);
        if (larger <= 0.0 || Math.Abs(r1 - r2) / larger < 0.001)
            return false;

        mu = GravitationalConstant * center.Mass;
        return mu > 0.001;
    }

    private static bool TryGetTransferWindowState(
        ObjectInfo start,
        ObjectInfo target,
        LogisticsFlightRouteKind routeKind,
        double transferDays,
        out LogisticsTransferWindowState state)
    {
        state = default;
        if (!IsTransferWindowRoute(routeKind) || transferDays <= 0.001)
            return false;

        var startBody = GetCanonicalBody(start);
        var targetBody = GetCanonicalBody(target);
        if (startBody == null || targetBody == null || startBody == targetBody
            || startBody.parentObjectInfo == null
            || startBody.parentObjectInfo != targetBody.parentObjectInfo)
        {
            return false;
        }

        if (!TryGetHohmannInputs(start, target, out _, out var targetOrbitRadiusMeters, out _))
            return false;

        var startOrbit = TryGetOrbitUniversal(startBody);
        var targetOrbit = TryGetOrbitUniversal(targetBody);
        if (startOrbit == null || targetOrbit == null)
            return false;

        var startAngularVelocity = TryGetAngularVelocity(startOrbit);
        var targetAngularVelocity = TryGetAngularVelocity(targetOrbit);
        var relativeAngularVelocity = targetAngularVelocity - startAngularVelocity;
        if (Math.Abs(relativeAngularVelocity) <= 1E-12)
            return false;

        var startAngle = GetCircularPhaseRadians(startOrbit);
        var targetAngle = GetCircularPhaseRadians(targetOrbit);
        var currentPhase = WrapPositiveRadians(targetAngle - startAngle);
        var transferPhysicsSeconds = WorldDaysToPhysicsSeconds(transferDays);
        if (transferPhysicsSeconds <= 0.001)
            return false;

        var idealPhase = WrapPositiveRadians(Math.PI - targetAngularVelocity * transferPhysicsSeconds);
        var phaseError = WrapSignedRadians(currentPhase - idealPhase);
        var waitPhysicsSeconds = Math.Abs(phaseError) <= TransferWindowNowToleranceRadians
            ? 0.0
            : relativeAngularVelocity > 0.0
                ? WrapPositiveRadians(0.0 - phaseError) / relativeAngularVelocity
                : WrapPositiveRadians(phaseError) / (0.0 - relativeAngularVelocity);
        var synodicPhysicsSeconds = TwoPi / Math.Abs(relativeAngularVelocity);
        if (double.IsNaN(waitPhysicsSeconds) || double.IsInfinity(waitPhysicsSeconds)
            || double.IsNaN(synodicPhysicsSeconds) || double.IsInfinity(synodicPhysicsSeconds))
        {
            return false;
        }

        var waitDays = Clamp(PhysicsSecondsToWorldDays(waitPhysicsSeconds), 0.0,
            Math.Max(0.0, PhysicsSecondsToWorldDays(synodicPhysicsSeconds)));
        var synodicDays = Math.Max(0.0, PhysicsSecondsToWorldDays(synodicPhysicsSeconds));
        state = new LogisticsTransferWindowState(
            waitDays,
            phaseError,
            synodicDays,
            startAngle,
            targetAngle,
            targetAngularVelocity,
            targetOrbitRadiusMeters);
        return true;
    }

    private static bool IsTransferWindowRoute(LogisticsFlightRouteKind routeKind)
    {
        return routeKind == LogisticsFlightRouteKind.SameParent
            || routeKind == LogisticsFlightRouteKind.Interplanetary;
    }

    private static double EstimateBadWindowChaseDeltaV(LogisticsTransferWindowState transferWindow,
        double candidateTravelDays)
    {
        var candidatePhysicsSeconds = WorldDaysToPhysicsSeconds(candidateTravelDays);
        if (candidatePhysicsSeconds <= 0.001)
            return 0.0;

        var transferArrivalAngle = WrapPositiveRadians(transferWindow.SourceAngleRadians + Math.PI);
        var targetArrivalAngle = WrapPositiveRadians(
            transferWindow.TargetAngleRadians
            + transferWindow.TargetAngularVelocityRadiansPerPhysicsSecond * candidatePhysicsSeconds);
        var phaseMiss = WrapSignedRadians(targetArrivalAngle - transferArrivalAngle);
        return LogisticsVanillaMissionMath.CalculateBadWindowChaseDeltaV(
            transferWindow.TargetOrbitRadiusMeters,
            phaseMiss,
            candidateTravelDays);
    }

    private static OrbitUniversal TryGetOrbitUniversal(ObjectInfo body)
    {
        if (body == null)
            return null;

        try
        {
            return body.OrbitUniversal;
        }
        catch
        {
            try
            {
                return body.NBody != null ? body.NBody.GetComponent<OrbitUniversal>() : null;
            }
            catch
            {
                return null;
            }
        }
    }

    private static double TryGetAngularVelocity(OrbitUniversal orbit)
    {
        if (orbit == null)
            return 0.0;

        try
        {
            var angularVelocity = orbit.GetAngularVelocity();
            if (angularVelocity > 1E-12 && !double.IsNaN(angularVelocity) && !double.IsInfinity(angularVelocity))
                return angularVelocity;
        }
        catch
        {
            // Fall through to the period-based estimate below.
        }

        try
        {
            var period = orbit.GetPeriod();
            if (period > 1E-9 && !double.IsNaN(period) && !double.IsInfinity(period))
                return TwoPi / period;
        }
        catch
        {
            return 0.0;
        }

        return 0.0;
    }

    private static double GetCircularPhaseRadians(OrbitUniversal orbit)
    {
        if (orbit == null)
            return 0.0;

        try
        {
            return WrapPositiveRadians((orbit.GetCurrentPhase() + orbit.omega_lc + orbit.omega_uc) * RadiansPerDegree);
        }
        catch
        {
            return WrapPositiveRadians((orbit.phase + orbit.omega_lc + orbit.omega_uc) * RadiansPerDegree);
        }
    }

    private static double WorldDaysToPhysicsSeconds(double days)
    {
        var worldSeconds = Math.Max(0.0, days) * 86400.0;
        try
        {
            return GravityScaler.WorldSecsToPhysTime(worldSeconds);
        }
        catch
        {
            var scale = GravityScaler.game_sec_per_phys_sec;
            return scale > 0.0 ? worldSeconds / scale : worldSeconds;
        }
    }

    private static double PhysicsSecondsToWorldDays(double physicsSeconds)
    {
        physicsSeconds = Math.Max(0.0, physicsSeconds);
        double worldSeconds;
        try
        {
            worldSeconds = GravityScaler.GetWorldTimeSeconds(physicsSeconds);
        }
        catch
        {
            var scale = GravityScaler.game_sec_per_phys_sec;
            worldSeconds = physicsSeconds * (scale > 0.0 ? scale : 1.0);
        }

        return worldSeconds / 86400.0;
    }

    private static double WrapPositiveRadians(double radians)
    {
        radians %= TwoPi;
        if (radians < 0.0)
            radians += TwoPi;
        return radians;
    }

    private static double WrapSignedRadians(double radians)
    {
        radians = WrapPositiveRadians(radians);
        return radians > Math.PI ? radians - TwoPi : radians;
    }

    private static bool TryGetOrbitalRadiusMeters(ObjectInfo body, ObjectInfo center, out double radiusMeters)
    {
        radiusMeters = 0.0;
        if (body == null || center == null)
            return false;

        var radiusAu = body.DistanceToCentralObjectAu > 0.000001
            ? body.DistanceToCentralObjectAu
            : body.parentObjectInfo == center && body.DistanceToSunInAU > 0.000001 && center.DistanceToSunInAU > 0.000001
                ? Math.Abs(body.DistanceToSunInAU - center.DistanceToSunInAU)
                : 0.0;
        if (radiusAu <= 0.000001)
            return false;

        radiusMeters = radiusAu * AstronomicalUnitMeters;
        return radiusMeters > 1.0;
    }

    private static double EstimateFastHeliocentricDeltaV(ObjectInfo start, ObjectInfo target, double travelDays)
    {
        var radiusDeltaAu = EstimateHeliocentricRadiusDeltaAu(start, target);
        if (radiusDeltaAu > 0.001)
            return Clamp(radiusDeltaAu * 162.5 - 2.5, 12.0, 120.0);

        var transferDays = Math.Max(1.0, travelDays);
        var radialKmPerSecond = EstimateDistanceMeters(start, target) / 1000.0 / (transferDays * 86400.0);
        var circularDelta = Math.Abs(EstimateCircularVelocityKmS(start) - EstimateCircularVelocityKmS(target));
        return Clamp(radialKmPerSecond * 1.9 + circularDelta * 2.0, 12.0, 120.0);
    }

    private static double EstimateFastHeliocentricTravelDays(ObjectInfo start, ObjectInfo target)
    {
        var radiusDeltaAu = EstimateHeliocentricRadiusDeltaAu(start, target);
        if (radiusDeltaAu > 0.001)
            return Clamp(18.67 + radiusDeltaAu * 33.33, 14.0, 120.0);

        var distanceAu = EstimateDistanceMeters(start, target) / AstronomicalUnitMeters;
        return Clamp(distanceAu * 100.0, 14.0, 120.0);
    }

    private static double EstimateCircularVelocityKmS(ObjectInfo objectInfo)
    {
        var body = GetCanonicalBody(objectInfo);
        var parent = body?.parentObjectInfo;
        if (body == null || parent == null)
            return 0.0;

        var radiusMeters = body.DistanceToCentralObjectAu > 0.000001
            ? body.DistanceToCentralObjectAu * AstronomicalUnitMeters
            : Math.Max(0.000001, body.DistanceToSunInAU) * AstronomicalUnitMeters;
        if (radiusMeters <= 0.0 || parent.Mass <= 0.0)
            return 0.0;

        return Math.Sqrt(GravitationalConstant * parent.Mass / radiusMeters) / 1000.0;
    }

    private static void LogRouteMission(string message)
    {
        if (LogisticsObserver.VerboseLoggingEnabled)
            LogisticsObserver.LogVerbose("ROUTE-MISSION " + message);
    }

    private static string DescribeRouteObject(ObjectInfo objectInfo)
    {
        if (objectInfo == null)
            return "null";

        return $"{objectInfo.ObjectName}#{objectInfo.id}({objectInfo.objectTypes})";
    }

    private static bool IsHeliocentricRoute(ObjectInfo start, ObjectInfo target)
    {
        var sun = MonoBehaviourSingleton<ObjectInfoManager>.Instance?.mainObjectInfoSun;
        if (sun == null)
            return false;

        var startBody = GetCanonicalBody(start);
        var targetBody = GetCanonicalBody(target);
        return startBody?.parentObjectInfo == sun
            && targetBody?.parentObjectInfo == sun
            && startBody != targetBody;
    }

    private static double EstimateHeliocentricRadiusDeltaAu(ObjectInfo start, ObjectInfo target)
    {
        if (!IsHeliocentricRoute(start, target))
            return 0.0;

        return Math.Abs(EstimateHeliocentricRadiusAu(start) - EstimateHeliocentricRadiusAu(target));
    }

    private static double EstimateHeliocentricRadiusAu(ObjectInfo objectInfo)
    {
        var body = GetCanonicalBody(objectInfo);
        if (body == null)
            return 0.0;

        if (body.DistanceToCentralObjectAu > 0.000001)
            return body.DistanceToCentralObjectAu;
        return Math.Max(0.0, body.DistanceToSunInAU);
    }

    private static ObjectInfo SafeLowOrbit(ObjectInfo endpoint)
    {
        try
        {
            return endpoint?.LowOrbitCustom?.GetObjectInfo();
        }
        catch
        {
            return null;
        }
    }

    private static double EstimateLocalOrbitDeltaV(ObjectInfo start, ObjectInfo target)
    {
        var body = start?.objectTypes == global::Data.EObjectTypes.Orbit ? start.parentObjectInfo : start;
        body ??= target?.objectTypes == global::Data.EObjectTypes.Orbit ? target.parentObjectInfo : target;
        if (body == null)
            return 0.5;

        if (start != null && start.objectTypes == global::Data.EObjectTypes.Orbit && IsOrbitOf(start, target))
            return 0.0;

        var deltaV = Math.Max(0.0, body.DV1Orbit + body.DV2Orbit);
        if (deltaV > 0.001)
            return Clamp(deltaV, 0.1, 8.0);

        return 0.5;
    }

    private static double EstimateEarthMoonDeltaV(ObjectInfo start, ObjectInfo target)
    {
        if (TryGetMoonCaseDeltaV(start, target, out var deltaV))
            return deltaV;
        return DefaultEarthMoonDeltaV;
    }

    private static bool TryGetMoonCaseDeltaV(ObjectInfo start, ObjectInfo target, out double deltaV)
    {
        deltaV = 0.0;
        var center = GetMoonCaseTableOwner(start, target);
        var table = center?.startTargetDVMinForMoonMooon;
        if (table == null || table.Count == 0)
            return false;

        var startBody = GetCanonicalBody(start);
        var targetBody = GetCanonicalBody(target);
        foreach (var row in table)
        {
            if (row == null)
                continue;

            if (MatchesMoonCaseRow(row.Item1, row.Item2, start, target)
                || MatchesMoonCaseRow(row.Item1, row.Item2, startBody, targetBody))
            {
                deltaV = Clamp(row.Item3, 0.1, 12.0) * GetEarthMoonDeltaVMultiplier();
                return true;
            }
        }

        return false;
    }

    private static bool MatchesMoonCaseRow(ObjectInfo rowStart, ObjectInfo rowTarget, ObjectInfo start, ObjectInfo target)
    {
        if (rowStart == null || rowTarget == null || start == null || target == null)
            return false;

        return (rowStart == start && rowTarget == target)
            || (rowStart == target && rowTarget == start)
            || (start.parentObjectInfo != null && rowStart == start.parentObjectInfo && rowTarget == target)
            || (target.parentObjectInfo != null && rowStart == start && rowTarget == target.parentObjectInfo)
            || (start.parentObjectInfo != null && target.parentObjectInfo != null && rowStart == start.parentObjectInfo && rowTarget == target.parentObjectInfo)
            || (start.parentObjectInfo != null && rowStart == target && rowTarget == start.parentObjectInfo)
            || (target.parentObjectInfo != null && rowStart == target.parentObjectInfo && rowTarget == start);
    }

    private static ObjectInfo GetMoonCaseTableOwner(ObjectInfo start, ObjectInfo target)
    {
        var startBody = GetCanonicalBody(start);
        var targetBody = GetCanonicalBody(target);
        if (startBody?.parentObjectInfo != null && startBody.parentObjectInfo == targetBody)
            return targetBody;
        if (targetBody?.parentObjectInfo != null && targetBody.parentObjectInfo == startBody)
            return startBody;
        if (startBody?.parentObjectInfo != null && startBody.parentObjectInfo == targetBody?.parentObjectInfo)
            return startBody.parentObjectInfo;
        return startBody?.parentObjectInfo ?? targetBody?.parentObjectInfo;
    }

    private static bool IsEarthMoonRoute(ObjectInfo start, ObjectInfo target)
    {
        if (start == null || target == null || start == target)
            return false;

        if (SafeCheckEarthMoonCase(start, target))
            return true;

        var startBody = GetCanonicalBody(start);
        var targetBody = GetCanonicalBody(target);
        return (startBody != start || targetBody != target) && SafeCheckEarthMoonCase(startBody, targetBody);
    }

    private static bool SafeCheckEarthMoonCase(ObjectInfo start, ObjectInfo target)
    {
        if (start == null || target == null || start == target)
            return false;

        try
        {
            return ObjectInfo.CheckEarthMoonCase(start, target);
        }
        catch (Exception exception)
        {
            LogisticsObserver.LogWarning($"FLIGHT-CALC earth-moon route check failed: {start.ObjectName}->{target.ObjectName} reason={exception.Message}");
            return false;
        }
    }

    private static bool SafeCheckOrbitCase(ObjectInfo start, ObjectInfo target)
    {
        if (start == null || target == null || start == target)
            return false;

        try
        {
            return ObjectInfo.CheckOrbitCase(start, target);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsOrbitOf(ObjectInfo orbit, ObjectInfo body)
    {
        if (orbit == null || body == null)
            return false;
        if (body.LowOrbitCustom != null && body.LowOrbitCustom.GetObjectInfo() == orbit)
            return true;
        return orbit.objectTypes == global::Data.EObjectTypes.Orbit && orbit.parentObjectInfo == body;
    }

    private static ObjectInfo GetCanonicalBody(ObjectInfo objectInfo)
    {
        if (objectInfo == null)
            return null;
        return objectInfo.objectTypes == global::Data.EObjectTypes.Orbit ? objectInfo.parentObjectInfo : objectInfo;
    }

    private static double EstimateDistanceMeters(ObjectInfo start, ObjectInfo target)
    {
        var startBody = GetCanonicalBody(start);
        var targetBody = GetCanonicalBody(target);
        if (startBody == null || targetBody == null)
            return 0.1 * AstronomicalUnitMeters;

        if (startBody.parentObjectInfo == targetBody)
            return Math.Max(0.001, startBody.DistanceToCentralObjectAu) * AstronomicalUnitMeters;
        if (targetBody.parentObjectInfo == startBody)
            return Math.Max(0.001, targetBody.DistanceToCentralObjectAu) * AstronomicalUnitMeters;
        if (startBody.parentObjectInfo != null && startBody.parentObjectInfo == targetBody.parentObjectInfo)
            return Math.Max(0.001, Math.Abs(startBody.DistanceToCentralObjectAu - targetBody.DistanceToCentralObjectAu)) * AstronomicalUnitMeters;

        return Math.Max(0.01, Math.Abs(startBody.DistanceToSunInAU - targetBody.DistanceToSunInAU)) * AstronomicalUnitMeters;
    }

    private static double GetEarthMoonOptimalTravelDays()
    {
        var economic = MonoBehaviourSingleton<GameManager>.Instance?.Economic;
        if (economic != null && economic.EarthMoonCaseDaysToAdd > 0f)
            return economic.EarthMoonCaseDaysToAdd;
        return DefaultEarthMoonTravelDays;
    }

    private static double GetEarthMoonDeltaVMultiplier()
    {
        var economic = MonoBehaviourSingleton<GameManager>.Instance?.Economic;
        if (economic != null && economic.EarthMoonCaseMultiDeltaVAfterChange > 0f)
            return economic.EarthMoonCaseMultiDeltaVAfterChange;
        return 1.0;
    }

    private static double GetPowVariable()
    {
        var economic = MonoBehaviourSingleton<GameManager>.Instance?.Economic;
        if (economic != null && economic.PowVariable > 1.0f)
            return economic.PowVariable;
        return Math.E;
    }

    private static double GetConstantAccelerationMin()
    {
        var economic = MonoBehaviourSingleton<GameManager>.Instance?.Economic;
        return economic != null && economic.ConstanceAccelerationAmin > 0f ? economic.ConstanceAccelerationAmin : 0.1;
    }

    private static double GetConstantAccelerationMax()
    {
        var economic = MonoBehaviourSingleton<GameManager>.Instance?.Economic;
        return economic != null && economic.ConstanceAccelerationAmaxLimit > 0f ? economic.ConstanceAccelerationAmaxLimit : 9.81;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }
}
