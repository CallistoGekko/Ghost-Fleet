using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Data.ScriptableObject;
using Game;
using Game.Info;
using Game.UI.Windows.Elements.PlanMissionElements;
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
    private const double AstronomicalUnitMeters = 149599993856.0;
    private const int PorkchopIntervals = 200;
    private static readonly MethodInfo ComputeLambert2Method = typeof(LambertPorkchop).GetMethod(
        "ComputeLambert2",
        BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly Dictionary<string, InstantPorkchopCacheEntry> InstantPorkchopCache =
        new Dictionary<string, InstantPorkchopCacheEntry>(StringComparer.Ordinal);

    private sealed class InstantPorkchopCacheEntry
    {
        public double DeltaV;
        public double TravelDays;
        public DateTime Departure;
        public DateTime Arrival;
    }

    public static LogisticsCalculatedFlight CalculateSoonestOptimalFlight(
        ObjectInfo start,
        ObjectInfo target,
        LogisticsFlightVehicleSnapshot vehicle,
        LogisticsFlightCargoSnapshot cargo,
        Company company,
        LogisticsFlightPlanMode flightPlanMode = LogisticsFlightPlanMode.Optimal)
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
        var travelDays = EstimateSoonestOptimalTravelDays(start, target, vehicle, cargo, company, result.RouteKind);
        var deltaV = EstimateOptimalDeltaV(start, target, vehicle, result.RouteKind);
        var departure = now;
        var arrival = now.AddDays(Math.Max(0.1, travelDays));
        if (TryCalculateInstantPorkchop(start, target, vehicle, cargo, company, effectiveFlightPlanMode,
                out var porkchopDeltaV, out var porkchopTravelDays, out var porkchopDeparture, out var porkchopArrival))
        {
            deltaV = porkchopDeltaV;
            travelDays = porkchopTravelDays;
            departure = porkchopDeparture;
            arrival = porkchopArrival;
        }

        result.TravelDays = Math.Max(0.1, (arrival - departure).TotalDays);
        result.Departure = departure;
        result.Arrival = arrival > departure ? arrival : departure.AddDays(result.TravelDays);
        result.EstimatedDeltaV = deltaV;
        result.AvailableDeltaV = EstimateAvailableDeltaV(vehicle, cargo, company);
        result.FlightFuel = EstimateFlightFuel(vehicle, cargo, company, result.EstimatedDeltaV, result.RouteKind);
        result.LaunchFuel = 0.0;

        if (!vehicle.SolarPowered && vehicle.FuelCapacity > 0.001 && result.FlightFuel > vehicle.FuelCapacity + 0.001)
        {
            result.Reason = "Flight fuel exceeds tank capacity";
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
                if (IsHeliocentricRoute(start, target))
                    return EstimateFastHeliocentricTravelDays(start, target);
                return 20.0;
            case LogisticsFlightRouteKind.ParentChild:
                if (IsHeliocentricRoute(start, target))
                    return EstimateFastHeliocentricTravelDays(start, target);
                return 10.0;
            case LogisticsFlightRouteKind.ConstantAcceleration:
                if (TryEstimateConstantAccelerationTravelDays(start, target, vehicle, cargo, company, out var constantDays))
                    return constantDays;
                return EstimateHohmannLikeDays(start, target);
            default:
                return EstimateHohmannLikeDays(start, target);
        }
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
                if (IsHeliocentricRoute(start, target))
                    return EstimateFastHeliocentricDeltaV(start, target, EstimateFastHeliocentricTravelDays(start, target));
                return 2.5;
            case LogisticsFlightRouteKind.ParentChild:
                if (IsHeliocentricRoute(start, target))
                    return EstimateFastHeliocentricDeltaV(start, target, EstimateFastHeliocentricTravelDays(start, target));
                return 2.0;
            case LogisticsFlightRouteKind.ConstantAcceleration:
                return Math.Max(1.0, EstimateInterplanetaryDeltaV(start, target) * 0.75);
            default:
                return EstimateInterplanetaryDeltaV(start, target);
        }
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
        var startBody = GetCanonicalBody(start);
        var targetBody = GetCanonicalBody(target);
        var r1 = Math.Max(0.01, startBody?.DistanceToSunInAU ?? 1.0);
        var r2 = Math.Max(0.01, targetBody?.DistanceToSunInAU ?? 1.0);
        return Clamp(3.0 + Math.Abs(r1 - r2) * 6.5, 3.0, 90.0);
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

        return Math.Sqrt(6.674080038626684E-11 * parent.Mass / radiusMeters) / 1000.0;
    }

    private static bool TryCalculateInstantPorkchop(
        ObjectInfo start,
        ObjectInfo target,
        LogisticsFlightVehicleSnapshot vehicle,
        LogisticsFlightCargoSnapshot cargo,
        Company company,
        LogisticsFlightPlanMode flightPlanMode,
        out double deltaV,
        out double travelDays,
        out DateTime departure,
        out DateTime arrival)
    {
        deltaV = 0.0;
        travelDays = 0.0;
        departure = default;
        arrival = default;
        if (start == null || target == null || vehicle?.Type == null || company == null || ComputeLambert2Method == null)
        {
            LogRouteMission(
                $"step=abort reason=missing-input start={DescribeRouteObject(start)} target={DescribeRouteObject(target)} vehicleType={vehicle?.Type?.ID ?? "null"} company={company?.ToString() ?? "null"} hasLambert={ComputeLambert2Method != null}");
            return false;
        }
        if (vehicle.Type.NotUsePorkchope)
        {
            LogRouteMission(
                $"step=abort reason=not-porkchop start={DescribeRouteObject(start)} target={DescribeRouteObject(target)} ship={vehicle.Type.ID}");
            return false;
        }

        try
        {
            var missionParameter = new PMMissionParameter()
                .SetCompany(company)
                .SetTabDestination(start, target);
            var calculationStart = missionParameter.ObjectInfoStartCalculation;
            var calculationTarget = missionParameter.ObjectInfoTargetCalculation;
            LogRouteMission(
                $"step=destination start={DescribeRouteObject(start)} target={DescribeRouteObject(target)} calcStart={DescribeRouteObject(calculationStart)} calcTarget={DescribeRouteObject(calculationTarget)} mode={flightPlanMode} ship={vehicle.Type.ID} cargo={cargo?.CargoMass ?? 0.0:0.###} designDV={vehicle.DesignAvailableDeltaV:0.###} effectiveGate={EstimateAvailableDeltaV(vehicle, cargo, company):0.###} minMaxRel={vehicle.Type.MinFlightTimeHohRel:0.###}/{vehicle.Type.MaxFlightTimeHohRel:0.###}");
            var fromOrbit = calculationStart?.NBody?.gameObject?.GetComponent<OrbitUniversal>();
            var toOrbit = calculationTarget?.NBody?.gameObject?.GetComponent<OrbitUniversal>();
            if (fromOrbit == null || toOrbit == null || fromOrbit.centerNbody != toOrbit.centerNbody)
            {
                LogRouteMission(
                    $"step=abort reason=orbit-mismatch startOrbit={fromOrbit != null} targetOrbit={toOrbit != null} sameCenter={fromOrbit != null && toOrbit != null && fromOrbit.centerNbody == toOrbit.centerNbody}");
                return false;
            }

            var cacheKey = BuildInstantPorkchopCacheKey(
                start,
                target,
                calculationStart,
                calculationTarget,
                vehicle,
                cargo,
                company,
                flightPlanMode);
            if (TryGetInstantPorkchopCache(cacheKey, out deltaV, out travelDays, out departure, out arrival))
            {
                LogRouteMission(
                    $"step=cache-hit mode={flightPlanMode} start={DescribeRouteObject(start)} target={DescribeRouteObject(target)} dV={deltaV:0.###} days={travelDays:0.###} depart={departure:yyyy-MM-dd} arrive={arrival:yyyy-MM-dd}");
                return true;
            }

            var physicalNow = GravityEngine.instance.GetPhysicalTimeDouble();
            var departureStart = physicalNow;
            departureStart += GetDefaultPlanMissionLeadTimeSeconds(vehicle);

            var departureWindow = CalculateInstantDepartureWindow(fromOrbit, toOrbit);
            var departureEnd = departureStart + departureWindow;
            var arrivalCenter = 0.5 * (toOrbit.GetPeriod() + departureWindow);
            if (arrivalCenter * Math.Max(0.1, vehicle.Type.MaxFlightTimeHohRel) > 600.0)
                arrivalCenter = 600.0 / Math.Max(0.1, vehicle.Type.MaxFlightTimeHohRel);

            var minFlightTime = arrivalCenter * Math.Max(0.001, vehicle.Type.MinFlightTimeHohRel);
            var maxFlightTime = arrivalCenter * Math.Max(vehicle.Type.MinFlightTimeHohRel, vehicle.Type.MaxFlightTimeHohRel);
            var arrivalStart = departureStart + minFlightTime;
            var arrivalEnd = departureEnd + maxFlightTime;
            if (arrivalStart <= departureStart || arrivalStart > arrivalEnd)
            {
                LogRouteMission(
                    $"step=abort reason=bad-window depart={RoutePhysDate(departureStart)}..{RoutePhysDate(departureEnd)} arrival={RoutePhysDate(arrivalStart)}..{RoutePhysDate(arrivalEnd)} minFlight={minFlightTime:0.###} maxFlight={maxFlightTime:0.###}");
                return false;
            }

            const int departureIntervals = PorkchopIntervals;
            const int arrivalIntervals = PorkchopIntervals;
            var departureStep = (departureEnd - departureStart) / departureIntervals;
            var arrivalStep = (arrivalEnd - arrivalStart) / arrivalIntervals;
            var mu = fromOrbit.GetMu();
            var fromPropagator = OrbitPropagator.GetPropagator(fromOrbit);
            var toPropagator = OrbitPropagator.GetPropagator(toOrbit);
            if (fromPropagator == null || toPropagator == null)
            {
                LogRouteMission(
                    $"step=abort reason=missing-propagator fromPropagator={fromPropagator != null} toPropagator={toPropagator != null}");
                return false;
            }

            LogRouteMission(
                $"step=window mode={flightPlanMode} depart={RoutePhysDate(departureStart)}..{RoutePhysDate(departureEnd)} arrival={RoutePhysDate(arrivalStart)}..{RoutePhysDate(arrivalEnd)} departStep={departureStep:0.###} arrivalStep={arrivalStep:0.###} periods={fromOrbit.GetPeriod():0.###}/{toOrbit.GetPeriod():0.###} departWindow={departureWindow:0.###} arrivalCenter={arrivalCenter:0.###} minFlight={minFlightTime:0.###} maxFlight={maxFlightTime:0.###} mu={mu:0.###}");

            var arrivalStates = new (Vector3d Position, Vector3d Velocity)[arrivalIntervals + 1];
            for (var arrivalIndex = 0; arrivalIndex <= arrivalIntervals; arrivalIndex++)
            {
                var arrivalTime = arrivalStart + arrivalIndex * arrivalStep;
                var propagated = toPropagator.PropagateToTime(arrivalTime);
                arrivalStates[arrivalIndex] = (propagated.Item1, propagated.Item2);
            }

            var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
            var availableDeltaV = EstimateAvailableDeltaV(vehicle, cargo, company);
            var bestArrival = DateTime.MaxValue;
            var bestDeparture = DateTime.MinValue;
            var bestDeltaV = 0.0;
            var bestDepartureIndex = -1;
            var bestArrivalIndex = -1;
            var maxDepartureIndex = departureIntervals;
            var maxArrivalIndex = arrivalIntervals;
            for (var departureIndex = 0; departureIndex <= maxDepartureIndex; departureIndex++)
            {
                var departureTime = departureStart + departureIndex * departureStep;
                var departureState = fromPropagator.PropagateToTime(departureTime);
                for (var arrivalIndex = 0; arrivalIndex <= maxArrivalIndex; arrivalIndex++)
                {
                    var arrivalTime = arrivalStart + arrivalIndex * arrivalStep;
                    if (arrivalTime <= departureTime + minFlightTime)
                        continue;

                    var arrivalState = arrivalStates[arrivalIndex];
                    if (!TryComputeLambertTotalDeltaV(
                            departureState.Item1,
                            arrivalState.Position,
                            mu,
                            arrivalTime - departureTime,
                            departureState.Item2,
                            arrivalState.Velocity,
                            out var candidateDeltaV))
                        continue;

                    var candidateArrival = GravityScaler.GetWorldTimeDateTime(arrivalTime, GravityScaler.Units.SOLAR);
                    var candidateDeparture = GravityScaler.GetWorldTimeDateTime(departureTime, GravityScaler.Units.SOLAR);
                    var candidateFuel = EstimateFlightFuelValue(vehicle, cargo, company, candidateDeltaV);
                    var tankOk = vehicle.SolarPowered
                        || vehicle.FuelCapacity <= 0.001
                        || candidateFuel <= vehicle.FuelCapacity + 0.001;
                    var underGate = vehicle.SolarPowered
                        || (candidateDeltaV <= availableDeltaV + 0.001 && tankOk);
                    var better = underGate && IsBetterPorkchopCandidate(
                        flightPlanMode,
                        candidateDeltaV,
                        candidateArrival,
                        bestDeltaV,
                        bestArrival);

                    if (!underGate)
                        continue;

                    if (!better)
                        continue;

                    bestArrival = candidateArrival;
                    bestDeparture = candidateDeparture;
                    bestDeltaV = candidateDeltaV;
                    bestDepartureIndex = departureIndex;
                    bestArrivalIndex = arrivalIndex;
                }
            }

            if (bestDeltaV <= 0.001 || bestArrival == DateTime.MaxValue)
            {
                LogRouteMission(
                    $"step=select result=fail reason=no-candidate mode={flightPlanMode} gate={availableDeltaV:0.###}");
                return false;
            }

            deltaV = bestDeltaV;
            var nowGame = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
            departure = ConvertPhysicalDateToGameDate(bestDeparture, physicalNow, nowGame);
            arrival = ConvertPhysicalDateToGameDate(bestArrival, physicalNow, nowGame);
            travelDays = Math.Max(0.1, (arrival - departure).TotalDays);
            LogRouteMission(
                $"step=select result=ok mode={flightPlanMode} cell={bestDepartureIndex},{bestArrivalIndex} dV={deltaV:0.###} days={travelDays:0.###} depart={departure:yyyy-MM-dd} arrive={arrival:yyyy-MM-dd} gate={availableDeltaV:0.###}");
            LogisticsObserver.LogVerbose(
                $"FLIGHT-CALC instant-porkchop: {start.ObjectName}->{target.ObjectName} calc={calculationStart.ObjectName}({calculationStart.objectTypes})->{calculationTarget.ObjectName}({calculationTarget.objectTypes}) mode={flightPlanMode} gate={availableDeltaV:0.##} cell={bestDepartureIndex},{bestArrivalIndex} dV={deltaV:0.##} days={travelDays:0.#} depart={departure:yyyy-MM-dd} arrive={arrival:yyyy-MM-dd} periods={fromOrbit.GetPeriod():0.###}/{toOrbit.GetPeriod():0.###} departWindow={departureWindow:0.###} minMaxRel={vehicle.Type.MinFlightTimeHohRel:0.###}/{vehicle.Type.MaxFlightTimeHohRel:0.###}");
            StoreInstantPorkchopCache(cacheKey, deltaV, travelDays, departure, arrival);
            return true;
        }
        catch (Exception exception)
        {
            LogisticsObserver.LogWarning($"FLIGHT-CALC instant porkchop failed: {start.ObjectName}->{target.ObjectName} reason={exception.Message}");
            deltaV = 0.0;
            travelDays = 0.0;
            departure = default;
            arrival = default;
            return false;
        }
    }

    private static DateTime ConvertPhysicalDateToGameDate(DateTime physicalDate, double physicalNow, DateTime gameNow)
    {
        try
        {
            var physicalNowDate = GravityScaler.GetWorldTimeDateTime(physicalNow, GravityScaler.Units.SOLAR);
            return gameNow.AddDays((physicalDate - physicalNowDate).TotalDays);
        }
        catch
        {
            return physicalDate;
        }
    }

    private static string BuildInstantPorkchopCacheKey(
        ObjectInfo start,
        ObjectInfo target,
        ObjectInfo calculationStart,
        ObjectInfo calculationTarget,
        LogisticsFlightVehicleSnapshot vehicle,
        LogisticsFlightCargoSnapshot cargo,
        Company company,
        LogisticsFlightPlanMode flightPlanMode)
    {
        var nowTicks = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime.Ticks ?? 0L;
        return string.Join("|",
            nowTicks.ToString(CultureInfo.InvariantCulture),
            DescribeRouteObjectForKey(start),
            DescribeRouteObjectForKey(target),
            DescribeRouteObjectForKey(calculationStart),
            DescribeRouteObjectForKey(calculationTarget),
            company?.GetHashCode().ToString(CultureInfo.InvariantCulture) ?? "0",
            vehicle?.Type?.ID ?? "",
            NormalizeFlightPlanMode(flightPlanMode).ToString(),
            FormatCacheNumber(cargo?.CargoMass ?? 0.0),
            FormatCacheNumber(vehicle?.DryMass ?? 0.0),
            FormatCacheNumber(vehicle?.FuelCapacity ?? 0.0),
            FormatCacheNumber(vehicle?.ExhaustVelocity ?? 0.0),
            FormatCacheNumber(vehicle?.DesignAvailableDeltaV ?? 0.0),
            FormatCacheNumber(vehicle?.Type?.MinFlightTimeHohRel ?? 0.0),
            FormatCacheNumber(vehicle?.Type?.MaxFlightTimeHohRel ?? 0.0));
    }

    private static bool TryGetInstantPorkchopCache(string key, out double deltaV, out double travelDays,
        out DateTime departure, out DateTime arrival)
    {
        deltaV = 0.0;
        travelDays = 0.0;
        departure = default;
        arrival = default;
        if (string.IsNullOrWhiteSpace(key) || !InstantPorkchopCache.TryGetValue(key, out var entry) || entry == null)
            return false;

        if (entry.DeltaV <= 0.001 || entry.TravelDays <= 0.001 || entry.Departure == default || entry.Arrival <= entry.Departure)
            return false;

        deltaV = entry.DeltaV;
        travelDays = entry.TravelDays;
        departure = entry.Departure;
        arrival = entry.Arrival;
        return true;
    }

    private static void StoreInstantPorkchopCache(string key, double deltaV, double travelDays,
        DateTime departure, DateTime arrival)
    {
        if (string.IsNullOrWhiteSpace(key) || deltaV <= 0.001 || travelDays <= 0.001
            || departure == default || arrival <= departure)
            return;

        if (InstantPorkchopCache.Count > 256)
            InstantPorkchopCache.Clear();

        InstantPorkchopCache[key] = new InstantPorkchopCacheEntry
        {
            DeltaV = deltaV,
            TravelDays = travelDays,
            Departure = departure,
            Arrival = arrival
        };
    }

    private static string DescribeRouteObjectForKey(ObjectInfo objectInfo)
    {
        return objectInfo == null
            ? "0:null"
            : $"{objectInfo.id.ToString(CultureInfo.InvariantCulture)}:{objectInfo.objectTypes}";
    }

    private static string FormatCacheNumber(double value)
    {
        return Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static bool IsBetterPorkchopCandidate(
        LogisticsFlightPlanMode flightPlanMode,
        double candidateDeltaV,
        DateTime candidateArrival,
        double bestDeltaV,
        DateTime bestArrival)
    {
        var mode = flightPlanMode == LogisticsFlightPlanMode.Fast
            ? LogisticsVanillaMissionPlanMode.Fastest
            : LogisticsVanillaMissionPlanMode.Optimal;
        return LogisticsVanillaMissionMath.IsBetterPorkchopCandidate(
            mode,
            candidateDeltaV,
            candidateArrival,
            bestDeltaV,
            bestArrival);
    }

    private static double GetDefaultPlanMissionLeadTimeSeconds(LogisticsFlightVehicleSnapshot vehicle)
    {
        var economic = MonoBehaviourSingleton<GameManager>.Instance?.Economic;
        var days = economic?.TimeAddToPlanMissionDays ?? 0f;
        days += vehicle?.Type?.timeAddToPlanMissionDays ?? 0f;
        return days * 86400.0 / GravityScaler.game_sec_per_phys_sec;
    }

    private static double CalculateInstantDepartureWindow(OrbitUniversal fromOrbit, OrbitUniversal toOrbit)
    {
        var departureWindow = 1.25 * fromOrbit.GetPeriod();
        if (fromOrbit != toOrbit
            && toOrbit.GetNBody().GetObjectInfo().objectTypes != global::Data.EObjectTypes.Orbit
            && fromOrbit.GetNBody().GetObjectInfo().objectTypes != global::Data.EObjectTypes.Orbit)
        {
            departureWindow = 1.25 * (1.0 / (1.0 / fromOrbit.GetPeriod() - 1.0 / toOrbit.GetPeriod()));
            if (departureWindow <= 0.0)
                departureWindow = 1.25 * (1.0 / (-1.0 / fromOrbit.GetPeriod() + 1.0 / toOrbit.GetPeriod()));
            if (departureWindow <= 0.0)
                departureWindow = 1.25 * fromOrbit.GetPeriod();

            var maxPeriod = Math.Max(fromOrbit.GetPeriod(), toOrbit.GetPeriod());
            if (departureWindow > maxPeriod * 3.0)
                departureWindow = maxPeriod;
        }
        else if (departureWindow > 100000.0)
        {
            departureWindow = 0.03999999910593033;
        }

        return Math.Max(0.001, departureWindow);
    }

    private static bool TryComputeLambertTotalDeltaV(
        Vector3d departurePosition,
        Vector3d arrivalPosition,
        double mu,
        double durationSeconds,
        Vector3d departureVelocity,
        Vector3d arrivalVelocity,
        out double deltaV)
    {
        deltaV = 0.0;
        var raw = ComputeLambert2Method.Invoke(null, new object[]
        {
            departurePosition,
            arrivalPosition,
            mu,
            durationSeconds,
            departureVelocity
        });
        var tuple = (ValueTuple<int, Vector3d, Vector3d>)raw;
        if (tuple.Item1 != 0)
            return false;

        var rawDeltaV = (tuple.Item2 - departureVelocity).magnitude + (tuple.Item3 - arrivalVelocity).magnitude;
        deltaV = ConvertPorkchopVelocity(rawDeltaV);
        return deltaV > 0.001 && !double.IsNaN(deltaV) && !double.IsInfinity(deltaV);
    }

    private static double ConvertPorkchopVelocity(double velocity)
    {
        var gravityEngine = GravityEngine.Instance();
        var scale = GravityScaler.PositionScaletoSIUnits() / Math.Max(0.001, gravityEngine.lengthScale);
        if (gravityEngine.units != 0)
            scale /= 1000.0;
        return velocity * scale / GravityScaler.GetGameSecondPerPhysicsSecond();
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

    private static string RoutePhysDate(double physicalTime)
    {
        if (double.IsNaN(physicalTime) || double.IsInfinity(physicalTime))
            return physicalTime.ToString("0.###");

        try
        {
            return GravityScaler.GetWorldTimeDateTime(physicalTime, GravityScaler.Units.SOLAR).ToString("yyyy-MM-dd");
        }
        catch
        {
            return physicalTime.ToString("0.###");
        }
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
