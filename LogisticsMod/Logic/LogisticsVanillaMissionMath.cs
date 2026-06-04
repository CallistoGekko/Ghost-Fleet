using System;
using System.Collections.Generic;

namespace LogisticsMod.Logic
{

internal enum LogisticsVanillaMissionPlanMode
{
    Fastest,
    Optimal
}

internal readonly struct LogisticsPorkchopCandidate
{
    public LogisticsPorkchopCandidate(int departureIndex, int arrivalIndex, double deltaV, DateTime arrival, bool scheduleAllowed = true)
    {
        DepartureIndex = departureIndex;
        ArrivalIndex = arrivalIndex;
        DeltaV = deltaV;
        Arrival = arrival;
        ScheduleAllowed = scheduleAllowed;
    }

    public int DepartureIndex { get; }
    public int ArrivalIndex { get; }
    public double DeltaV { get; }
    public DateTime Arrival { get; }
    public bool ScheduleAllowed { get; }
}

internal static class LogisticsVanillaMissionMath
{
    public static double GetPorkchopEffectiveDeltaV(bool solarPowered, double spacecraftTypeAvailableDeltaV)
    {
        return solarPowered
            ? double.PositiveInfinity
            : Math.Max(0.0, spacecraftTypeAvailableDeltaV);
    }

    public static double CalculateMassToFuel(double dryMassPerCraft, double cargoMass, int craftCount)
    {
        return Math.Max(1.0, Math.Max(0.0, dryMassPerCraft) * Math.Max(1, craftCount) + Math.Max(0.0, cargoMass));
    }

    public static double CalculateLoadedPropellantEffectiveDeltaV(double dryMass, double cargoMass, double fuelCapacity,
        double exhaustVelocity)
    {
        var dryPlusCargo = Math.Max(1.0, Math.Max(0.0, dryMass) + Math.Max(0.0, cargoMass));
        var loadedMass = dryPlusCargo + Math.Max(0.0, fuelCapacity);
        if (loadedMass <= dryPlusCargo || exhaustVelocity <= 0.001)
            return 0.0;

        var deltaV = exhaustVelocity * Math.Log(loadedMass / dryPlusCargo, Math.E);
        if (double.IsNaN(deltaV) || double.IsInfinity(deltaV))
            return 0.0;
        return Math.Max(0.0, deltaV);
    }

    public static double CalculateMinimumPropellantNeeded(double massToFuel, double deltaV, double exhaustVelocity, double powVariable)
    {
        if (deltaV <= 0.001)
            return 0.0;

        var fuel = Math.Max(1.0, massToFuel)
            * (Math.Pow(Math.Max(0.001, powVariable), deltaV / Math.Max(0.001, exhaustVelocity)) - 1.0);
        if (double.IsPositiveInfinity(fuel))
            fuel = 3.4028234663852886E+38;
        if (double.IsPositiveInfinity(fuel) || double.IsNaN(fuel))
            fuel = 0.0;
        if (fuel < 1.0)
            fuel = 1.0;
        return Math.Round(fuel);
    }

    public static double CalculateTotalPropellantNeeded(double massToFuel, double deltaV, double exhaustVelocity, double powVariable)
    {
        if (deltaV <= 0.001)
            return 0.0;

        massToFuel = Math.Max(1.0, massToFuel);
        var minimumPropellant = CalculateMinimumPropellantNeeded(massToFuel, deltaV, exhaustVelocity, powVariable);
        var massWithLoadedPropellant = massToFuel + minimumPropellant;
        var exponent = (0.0 - deltaV) / Math.Max(0.001, exhaustVelocity);
        if (double.IsPositiveInfinity(exponent) || double.IsNaN(exponent))
            exponent = double.NegativeInfinity;

        var massAfterBurn = massWithLoadedPropellant * Math.Pow(Math.Max(0.001, powVariable), exponent);
        if (massAfterBurn < massToFuel)
            massAfterBurn = massToFuel;

        var flightCost = massWithLoadedPropellant - massAfterBurn;
        var leftOverFuel = Math.Floor(massAfterBurn - massToFuel);
        return Math.Round((flightCost + leftOverFuel) * 10.0) / 10.0;
    }

    public static bool IsBetterPorkchopCandidate(LogisticsVanillaMissionPlanMode mode, double candidateDeltaV,
        DateTime candidateArrival, double bestDeltaV, DateTime bestArrival)
    {
        if (!IsUsableDeltaV(candidateDeltaV))
            return false;
        if (bestDeltaV <= 0.001 || bestArrival == DateTime.MaxValue)
            return true;

        if (mode == LogisticsVanillaMissionPlanMode.Optimal)
        {
            var deltaVDifference = candidateDeltaV - bestDeltaV;
            if (Math.Abs(deltaVDifference) > 0.001)
                return deltaVDifference < 0.0;
            return candidateArrival < bestArrival;
        }

        if (candidateArrival != bestArrival)
            return candidateArrival < bestArrival;
        return candidateDeltaV < bestDeltaV;
    }

    public static bool TrySelectPorkchopCandidate(IEnumerable<LogisticsPorkchopCandidate> candidates,
        LogisticsVanillaMissionPlanMode mode, double effectiveDeltaV, out LogisticsPorkchopCandidate selected)
    {
        selected = default;
        var bestDeltaV = 0.0;
        var bestArrival = DateTime.MaxValue;
        var found = false;

        if (candidates == null)
            return false;

        foreach (var candidate in candidates)
        {
            if (!candidate.ScheduleAllowed || !IsUsableDeltaV(candidate.DeltaV))
                continue;
            if (candidate.DeltaV > effectiveDeltaV + 0.001)
                continue;
            if (!IsBetterPorkchopCandidate(mode, candidate.DeltaV, candidate.Arrival, bestDeltaV, bestArrival))
                continue;

            selected = candidate;
            bestDeltaV = candidate.DeltaV;
            bestArrival = candidate.Arrival;
            found = true;
        }

        return found;
    }

    private static bool IsUsableDeltaV(double deltaV)
    {
        return deltaV > 0.001 && !double.IsNaN(deltaV) && !double.IsInfinity(deltaV);
    }
}
}
