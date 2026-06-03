using System;
using Data.ScriptableObject;
using Game;
using Game.Info;
using Manager;
using ScriptableObjectScripts;

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
}

internal static class LogisticsFlightCalculator
{
    private const double DefaultEarthMoonTravelDays = 7.0;
    private const double DefaultEarthMoonDeltaV = 3.2;
    private const double AstronomicalUnitMeters = 149599993856.0;

    public static LogisticsCalculatedFlight CalculateSoonestOptimalFlight(
        ObjectInfo start,
        ObjectInfo target,
        LogisticsFlightVehicleSnapshot vehicle,
        LogisticsFlightCargoSnapshot cargo,
        Company company)
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

        result.RouteKind = ClassifyRoute(start, target, vehicle);
        result.TravelDays = EstimateSoonestOptimalTravelDays(start, target, vehicle, cargo, company, result.RouteKind);
        result.Departure = now;
        result.Arrival = now.AddDays(Math.Max(0.1, result.TravelDays));
        result.EstimatedDeltaV = EstimateOptimalDeltaV(start, target, vehicle, result.RouteKind);
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
                return 20.0;
            case LogisticsFlightRouteKind.ParentChild:
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
                return 2.5;
            case LogisticsFlightRouteKind.ParentChild:
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
            return 0.0;

        var mass = Math.Max(1.0, vehicle.DryMass + Math.Max(0.0, cargo?.CargoMass ?? 0.0));
        var powVariable = GetPowVariable();
        var rocketFuel = mass * (Math.Pow(powVariable, deltaV / Math.Max(0.001, vehicle.ExhaustVelocity)) - 1.0);
        if (double.IsNaN(rocketFuel) || double.IsInfinity(rocketFuel) || rocketFuel < 0.0)
            rocketFuel = 0.0;

        var routeBudget = vehicle.FuelCapacity * EstimateTankFraction(routeKind, cargo, vehicle);
        var fuel = routeBudget > 0.001 ? Math.Min(rocketFuel, routeBudget) : rocketFuel;
        if (deltaV > 0.001 && fuel < 1.0)
            fuel = 1.0;
        return Math.Ceiling(fuel);
    }

    private static double EstimateAvailableDeltaV(LogisticsFlightVehicleSnapshot vehicle, LogisticsFlightCargoSnapshot cargo, Company company)
    {
        if (vehicle == null || vehicle.SolarPowered)
            return double.PositiveInfinity;

        var fuel = Math.Max(0.0, vehicle.FuelCapacity);
        var mass = Math.Max(1.0, vehicle.DryMass + Math.Max(0.0, cargo?.CargoMass ?? 0.0));
        var powVariable = GetPowVariable();
        return Math.Log(1.0 + fuel / mass, powVariable) * Math.Max(0.001, vehicle.ExhaustVelocity);
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
        return Clamp(3.0 + Math.Abs(r1 - r2) * 3.0, 3.0, 12.0);
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
