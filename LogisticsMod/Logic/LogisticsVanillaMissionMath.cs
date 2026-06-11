using System;
using System.Collections.Generic;

namespace LogisticsMod.Logic
{

internal readonly struct LogisticsTransferEstimate
{
    public LogisticsTransferEstimate(double deltaV, double travelSeconds, double firstBurnDeltaV, double secondBurnDeltaV)
    {
        DeltaV = deltaV;
        TravelSeconds = travelSeconds;
        FirstBurnDeltaV = firstBurnDeltaV;
        SecondBurnDeltaV = secondBurnDeltaV;
    }

    public double DeltaV { get; }
    public double TravelSeconds { get; }
    public double TravelDays => TravelSeconds / 86400.0;
    public double FirstBurnDeltaV { get; }
    public double SecondBurnDeltaV { get; }
}

internal static class LogisticsVanillaMissionMath
{
    private const double TwoPi = Math.PI * 2.0;

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

    public static LogisticsTransferEstimate CalculateHohmannTransfer(double r1, double r2, double mu)
    {
        r1 = Math.Max(0.001, r1);
        r2 = Math.Max(0.001, r2);
        mu = Math.Max(0.001, mu);

        var v1 = Math.Sqrt(mu / r1);
        var v2 = Math.Sqrt(mu / r2);
        var vt1 = Math.Sqrt(mu / r1 * (2.0 * r2 / (r1 + r2)));
        var vt2 = Math.Sqrt(mu / r2 * (2.0 * r1 / (r1 + r2)));
        var dv1 = Math.Abs(vt1 - v1);
        var dv2 = Math.Abs(v2 - vt2);
        var transferTime = Math.PI * Math.Sqrt(Math.Pow(r1 + r2, 3.0) / (8.0 * mu));
        return new LogisticsTransferEstimate(dv1 + dv2, transferTime, dv1, dv2);
    }

    public static LogisticsTransferEstimate CalculateBiEllipticTransfer(double r1, double r2, double rb, double mu)
    {
        r1 = Math.Max(0.001, r1);
        r2 = Math.Max(0.001, r2);
        rb = Math.Max(Math.Max(r1, r2), rb);
        mu = Math.Max(0.001, mu);

        var v1 = Math.Sqrt(mu / r1);
        var v2 = Math.Sqrt(mu / r2);
        var vt1 = Math.Sqrt(mu / r1 * (2.0 * rb / (r1 + rb)));
        var vt1a = Math.Sqrt(mu / rb * (2.0 * r1 / (r1 + rb)));
        var vt2 = Math.Sqrt(mu / rb * (2.0 * r2 / (rb + r2)));
        var vt2a = Math.Sqrt(mu / r2 * (2.0 * rb / (rb + r2)));
        var dv1 = Math.Abs(vt1 - v1);
        var dv2 = Math.Abs(vt2 - vt1a);
        var dv3 = Math.Abs(v2 - vt2a);
        var transferTime = Math.PI * Math.Sqrt(Math.Pow(r1 + rb, 3.0) / (8.0 * mu))
                           + Math.PI * Math.Sqrt(Math.Pow(rb + r2, 3.0) / (8.0 * mu));
        return new LogisticsTransferEstimate(dv1 + dv2 + dv3, transferTime, dv1, dv2 + dv3);
    }

    public static double EstimateHighEnergyDeltaV(double baselineDeltaV, double baselineTravelDays,
        double candidateTravelDays, double exponent = 1.55)
    {
        baselineDeltaV = Math.Max(0.0, baselineDeltaV);
        baselineTravelDays = Math.Max(0.1, baselineTravelDays);
        candidateTravelDays = Math.Max(0.1, candidateTravelDays);
        if (candidateTravelDays >= baselineTravelDays)
            return baselineDeltaV;

        var compression = baselineTravelDays / candidateTravelDays;
        var deltaV = baselineDeltaV * Math.Pow(compression, Math.Max(1.0, exponent));
        if (double.IsNaN(deltaV) || double.IsInfinity(deltaV))
            return baselineDeltaV;
        return Math.Max(baselineDeltaV, deltaV);
    }

    public static double CalculateBadWindowChaseDeltaV(double targetOrbitRadiusMeters, double phaseMissRadians,
        double candidateTravelDays)
    {
        targetOrbitRadiusMeters = Math.Max(0.0, targetOrbitRadiusMeters);
        candidateTravelDays = Math.Max(0.0, candidateTravelDays);
        if (targetOrbitRadiusMeters <= 0.001 || candidateTravelDays <= 0.001)
            return 0.0;

        var normalizedMiss = Math.Abs(phaseMissRadians % TwoPi);
        if (normalizedMiss > Math.PI)
            normalizedMiss = TwoPi - normalizedMiss;

        var missDistanceMeters = targetOrbitRadiusMeters * 2.0 * Math.Sin(normalizedMiss / 2.0);
        var travelSeconds = candidateTravelDays * 86400.0;
        var chaseDeltaVMetersPerSecond = missDistanceMeters / travelSeconds;
        if (double.IsNaN(chaseDeltaVMetersPerSecond) || double.IsInfinity(chaseDeltaVMetersPerSecond))
            return 0.0;
        return Math.Max(0.0, chaseDeltaVMetersPerSecond / 1000.0);
    }

    public static double CalculateMimaRequiredAcceleration(IReadOnlyList<double> dv1, IReadOnlyList<double> dv2,
        double timeOfFlightSeconds)
    {
        if (dv1 == null || dv2 == null || dv1.Count < 3 || dv2.Count < 3 || timeOfFlightSeconds <= 0.001)
            return 0.0;

        var dvx = dv1[0] + dv2[0];
        var dvy = dv1[1] + dv2[1];
        var dvz = dv1[2] + dv2[2];
        var diffX = -dv1[0] + dv2[0];
        var diffY = -dv1[1] + dv2[1];
        var diffZ = -dv1[2] + dv2[2];

        var ab = dvx * diffX + dvy * diffY + dvz * diffZ;
        var aa = dvx * dvx + dvy * dvy + dvz * dvz;
        var bb = diffX * diffX + diffY * diffY + diffZ * diffZ;
        var acceleration = Math.Sqrt(aa + 2.0 * bb + 2.0 * Math.Sqrt(ab * ab + bb * bb))
                           / timeOfFlightSeconds;
        if (double.IsNaN(acceleration) || double.IsInfinity(acceleration))
            return 0.0;
        return Math.Max(0.0, acceleration);
    }

    public static double CalculateMimaMaximumInitialMass(double requiredAcceleration, double timeOfFlightSeconds,
        double maxThrust, double effectiveExhaustVelocity)
    {
        if (requiredAcceleration <= 0.0 || timeOfFlightSeconds <= 0.001
            || maxThrust <= 0.0 || effectiveExhaustVelocity <= 0.001)
            return 0.0;

        var mass = 2.0 * maxThrust / requiredAcceleration
                   / (1.0 + Math.Exp(-requiredAcceleration * timeOfFlightSeconds / effectiveExhaustVelocity));
        if (double.IsNaN(mass) || double.IsInfinity(mass))
            return 0.0;
        return Math.Max(0.0, mass);
    }
}
}
