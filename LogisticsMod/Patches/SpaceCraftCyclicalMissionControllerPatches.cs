using HarmonyLib;
using CustomUpdate;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.UI.Windows.Elements.MissionsElements;
using Game.UI.Windows.Elements.PlanMissionElements;
using Game.UI.Windows.Elements.PlanMissionElements.PMScheduleElements;
using Game.UI.Windows.Windows;
using Game.VisualizationScripts;
using LogisticsMod.Logic;
using Manager;
using System;
using System.Collections.Generic;
using TMPro;

namespace LogisticsMod.Patches;

[HarmonyPatch]
internal static class SpaceCraftCyclicalMissionControllerPatches
{
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.SetPMParameterForCodeJobSystem))]
    [HarmonyPrefix]
    private static void SetPMParameterForCodeJobSystemPrefix(PMMissionParameter _pmMissionParameter, ref Action result)
    {
        var cmdFromShip = (_pmMissionParameter?.SC as Spacecraft) == null
            ? null
            : MonoBehaviourSingleton<CycleMissionManager>.Instance?.GetCycleMission((Spacecraft)_pmMissionParameter.SC);
        var isLogi = cmdFromShip?.customNameFromPlanMission != null
            && cmdFromShip.customNameFromPlanMission.StartsWith("[LOGI", StringComparison.Ordinal);
        if (!isLogi)
            isLogi = LogisticsObserver.IsLogisticsPlan(_pmMissionParameter);
        if (!isLogi
            && cmdFromShip?.customNameFromPlanMission != null
            && cmdFromShip.customNameFromPlanMission.StartsWith("[LOGI", StringComparison.Ordinal))
        {
            isLogi = true;
            if (LogisticsObserver.VerboseLoggingEnabled)
                LogisticsObserver.LogWarning(
                    $"LOGI-CODEJOB recovered-cycle-map: pmpName=\"{_pmMissionParameter?.MissionName ?? "null"}\" " +
                    $"cmdName=\"{cmdFromShip.customNameFromPlanMission}\" sc={DescribeSpacecraft(_pmMissionParameter?.SC)} " +
                    $"route={_pmMissionParameter?.Start?.ObjectName ?? "null"}->{_pmMissionParameter?.Target?.ObjectName ?? "null"}");
        }
        if (!isLogi) return;

        _pmMissionParameter.TryFixWrongThrust = true;
        if (cmdFromShip != null)
        {
            cmdFromShip.wasSetPMParameterForCodeJobSystem = true;

            // Stock bug: for MoonCase routes, TryPlanCycleMission hardcodes
            // TransferTypeMoonCase = Optimal and only sets ClickFastestButton
            // in the !MoonCase branch. Override all three flags here
            // unconditionally — MoonCase may not be set yet at prefix time
            // (it's computed later inside GravityEngineCalculate), but
            // TransferTypeMoonCase is read during trajectory selection.
            if (cmdFromShip.TransferType == ETransferType.Fastest)
            {
                _pmMissionParameter.ClickFastestButton = true;
                _pmMissionParameter.TryFastAsPossible = true;
                _pmMissionParameter.TransferTypeMoonCase = ETransferType.Fastest;
            }
        }
        if (LogisticsObserver.VerboseLoggingEnabled)
            LogisticsObserver.LogVerbose(
                $"LOGI-CODEJOB prefix: sc={DescribeSpacecraft(_pmMissionParameter.SC)} " +
                $"route={_pmMissionParameter.Start?.ObjectName ?? "null"}->{_pmMissionParameter.Target?.ObjectName ?? "null"}");

        var original = result;
        result = () =>
        {
            RestoreLogisticsMissionName(_pmMissionParameter, "codejob");
            LogisticsObserver.CapLogisticsCargoForPlannerLimits(_pmMissionParameter);
            original?.Invoke();
        };
    }

    [HarmonyPatch(typeof(SpaceCraftCyclicalMissionController), nameof(SpaceCraftCyclicalMissionController.TryPlanCycleMission))]
    [HarmonyPrefix]
    private static bool TryPlanCycleMissionPrefix(SpaceCraftCyclicalMissionController __instance)
    {
        var cmd = __instance.CycleMissionsData;
        if (cmd == null) return true;
        if (cmd.customNameFromPlanMission != null
            && cmd.customNameFromPlanMission.StartsWith("[LOGI")
            && __instance.CycleMissionPlanFlyWas)
        {
            LogisticsObserver.LogVerbose($"SKIP LOGI replanning: {cmd.customNameFromPlanMission}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Stock bug fix: PlanMissionWindow.Update() never calls SetEffectiveDeltaV for
    /// ForCode windows (guarded by <c>!forCode</c>), leaving EffectiveDeltaVOld at 0.
    /// ButtonFastestClickButton uses EffectiveDeltaVOld as a delta-V filter —
    /// with 0 every grid point is rejected and the fastest search silently fails,
    /// falling back to the initial (near-optimal) trajectory.
    ///
    /// Fix: before the fastest search, compute the ship's actual max delta-V
    /// (Tsiolkovsky equation with full fuel tank) and set it on the porkchop,
    /// then push the fuel slider to max so CheckScheduleFly can validate
    /// high-energy trajectories. ButtonFastestClickButton's own fuel-sweep loop
    /// will optimise fuel down to the minimum needed for the chosen trajectory.
    /// </summary>
    [HarmonyPatch(typeof(PMTabSchedule), nameof(PMTabSchedule.ButtonFastestClickButton))]
    [HarmonyPrefix]
    private static void ButtonFastestClickButtonPrefix(PMTabSchedule __instance)
    {
        var pmw = __instance.PlanMissionWindow;
        if (pmw == null || !pmw.ForCode)
            return;

        var pmp = pmw.PMMissionParameter;
        if (pmp?.SC == null || pmp.CargoAll?.cargoFuel == null)
            return;

        var scType = pmp.SC.GetTypeSpaceCraft();
        if (scType == null || scType.NotUsePorkchope || scType.SolarSC)
            return;

        // 1. Compute max effective delta-V from full fuel tank
        float exhaustV = scType.GetExhaustV(pmp.FlyCompany);
        double cargoMass = pmp.CargoAll.CargoCurrent;
        double dryMass = (double)pmp.SC.GetMass() + cargoMass;
        float maxFuelCapacity = scType.GetFuelCapacity(pmp.FlyCompany) * Math.Max(1, pmp.SCCount);
        double wetMass = dryMass + (double)maxFuelCapacity;

        if (wetMass <= dryMass || dryMass <= 0 || exhaustV <= 0)
            return;

        int effectiveDV = (int)((double)exhaustV * Math.Log(wetMass / dryMass, Math.E));
        __instance.porkchop.SetEffectiveDeltaV(effectiveDV);

        // 2. Push fuel slider to max so high-energy trajectories pass CheckScheduleFly.
        //    ButtonFastestClickButton has its own fuel-sweep loop that will reduce
        //    fuel to the minimum needed for the chosen trajectory.
        var fuelUI = Traverse.Create(__instance).Field("fuelSpaceCraftUI")
            .GetValue<FuelSpaceCraftUI>();
        if (fuelUI != null)
        {
            pmp.CargoAll.cargoFuel.cargoMassPotencjal = (double)maxFuelCapacity;
            fuelUI.SliderSetValue((float)fuelUI.MaxSlider);
        }
    }

    [HarmonyPatch(typeof(Spacecraft), "ShowNotificationLand")]
    [HarmonyPrefix]
    private static bool ShowNotificationLandPrefix(Spacecraft __instance)
    {
        return !ShouldSuppressCyclicalArrivalNotification(__instance, "land");
    }

    [HarmonyPatch(typeof(Spacecraft), "ShowNotificationAsteroidImpact")]
    [HarmonyPrefix]
    private static bool ShowNotificationAsteroidImpactPrefix(Spacecraft __instance)
    {
        return !ShouldSuppressCyclicalArrivalNotification(__instance, "asteroid-impact");
    }

    [HarmonyPatch(typeof(Spacecraft), "ShowNotificationSolarSystem")]
    [HarmonyPrefix]
    private static bool ShowNotificationSolarSystemPrefix(Spacecraft __instance)
    {
        return !ShouldSuppressCyclicalArrivalNotification(__instance, "solar-system");
    }

    [HarmonyPatch(typeof(Spacecraft), "ShowNotificationAsteroidPull")]
    [HarmonyPrefix]
    private static bool ShowNotificationAsteroidPullPrefix(Spacecraft __instance)
    {
        return !ShouldSuppressCyclicalArrivalNotification(__instance, "asteroid-pull");
    }

    [HarmonyPatch(typeof(PMMissionParameter), nameof(PMMissionParameter.CheckLVFullListOrNone))]
    [HarmonyPrefix]
    private static bool CheckLVFullListOrNonePrefix(PMMissionParameter __instance, ref bool __result)
    {
        if (!LogisticsObserver.TryOverrideLogisticsSelfLaunchCheck(__instance, out var requiresFullLaunchVehicleList))
            return true;

        __result = requiresFullLaunchVehicleList;
        return false;
    }

    [HarmonyPatch(typeof(ObjectInfoData), nameof(ObjectInfoData.CreatedCargoToTakeNormal))]
    [HarmonyPostfix]
    private static void CreatedCargoToTakeNormalPostfix(CargoAll __result, ECargoStart cargoStart,
        CycleMissionsData cycleMissionsData, ObjectInfo startObject, Spacecraft sc, LaunchVehicle lv,
        bool allResourceOnPlanet, double? loadLimit2, int countSC, bool addSupply, TimeSpan? missionLenght)
    {
        if (!LogisticsObserver.VerboseLoggingEnabled
            || cycleMissionsData?.customNameFromPlanMission == null
            || !cycleMissionsData.customNameFromPlanMission.StartsWith("[LOGI", StringComparison.Ordinal))
        {
            return;
        }

        var company = sc?.GetCompany() ?? cycleMissionsData.Company;
        LogisticsObserver.LogVerbose(
            $"LOGI-CARGO created: name=\"{cycleMissionsData.customNameFromPlanMission}\" " +
            $"start={startObject?.ObjectName ?? "null"} " +
            $"sc={DescribeSpacecraft(sc)} allOnPlanet={allResourceOnPlanet} " +
            $"cargo={DescribeCargo(__result)}");
    }

    [HarmonyPatch(typeof(PMTabSchedule), nameof(PMTabSchedule.OnClickScheduleButtonForCode))]
    [HarmonyPostfix]
    private static void OnClickScheduleButtonForCodePostfix(ref MissionInfo __result)
    {
        if (__result == null)
            return;
        if (__result.spacecraftInfo2 is not Spacecraft sc)
            return;

        var cmd = MonoBehaviourSingleton<CycleMissionManager>.Instance?.GetCycleMission(sc);
        if (cmd?.customNameFromPlanMission == null
            || !cmd.customNameFromPlanMission.StartsWith("[LOGI", StringComparison.Ordinal))
        {
            return;
        }

        __result.missionName = cmd.customNameFromPlanMission;
        __result.fromCyclicalMission = true;
        LogisticsObserver.LogVerbose($"PLAN mission-name-restored: id={__result.id} name=\"{__result.missionName}\"");
    }

    [HarmonyPatch(typeof(MissionInfoManager), nameof(MissionInfoManager.CreateMissionInfo))]
    [HarmonyPrefix]
    private static void CreateMissionInfoPrefix(ISpacecraftInfo spacecraftInfo, List<ISpacecraftInfo> listSC,
        MissionInfo.EMissionCreator missionCreator, CargoAll cargoAll, Company company,
        TrajectoryObject trajectoryObject, ref string missionName, ref string __state)
    {
        __state = null;
        if (missionCreator != MissionInfo.EMissionCreator.Cyclical)
            return;

        var name = FindLogisticsCycleNameFor(spacecraftInfo, listSC);
        if (string.IsNullOrEmpty(name) && trajectoryObject != null)
            name = LogisticsObserver.FindLogisticsCycleName(
                trajectoryObject.StartObjectInfo,
                trajectoryObject.EndObjectInfo,
                company,
                listSC,
                cargoAll);
        if (string.IsNullOrEmpty(name))
            return;

        missionName = name;
        __state = name;
    }

    [HarmonyPatch(typeof(MissionInfoManager), nameof(MissionInfoManager.CreateMissionInfo))]
    [HarmonyPostfix]
    private static void CreateMissionInfoPostfix(MissionInfo __result, string __state)
    {
        if (__result == null)
            return;

        var name = __state;
        if (string.IsNullOrEmpty(name)
            && __result.missionCreator != MissionInfo.EMissionCreator.Cyclical
            && (__result.missionName == null || !__result.missionName.StartsWith("[LOGI", StringComparison.Ordinal)))
        {
            return;
        }

        if (string.IsNullOrEmpty(name))
            name = LogisticsObserver.FindLogisticsCycleName(
                __result.start,
                __result.target,
                __result.company,
                __result.ListSpacecraftInfo2,
                __result.cargoAll);
        if (string.IsNullOrEmpty(name))
        {
            if (__result.missionName != null && __result.missionName.StartsWith("[LOGI", StringComparison.Ordinal))
                __result.fromCyclicalMission = true;
            return;
        }

        __result.missionName = name;
        __result.fromCyclicalMission = true;
    }

    [HarmonyPatch(typeof(MissionInfo), nameof(MissionInfo.Complete))]
    [HarmonyPostfix]
    private static void MissionInfoCompletePostfix(MissionInfo __instance)
    {
        LogisticsObserver.CleanupLogisticsMissionTrajectory(__instance, "complete");
    }

    [HarmonyPatch(typeof(PMTabDestination), nameof(PMTabDestination.ChangeMissionName), new Type[] { })]
    [HarmonyPrefix]
    private static bool ChangeMissionNamePrefix(PMTabDestination __instance)
    {
        var pmw = Traverse.Create(__instance).Field("planMissionWindow").GetValue<PlanMissionWindow>();
        if (!MightBeLogisticsPlan(pmw?.PMMissionParameter))
            return true;

        return pmw?.PMMissionParameter == null
            || string.IsNullOrEmpty(LogisticsObserver.FindLogisticsCycleName(pmw.PMMissionParameter));
    }

    [HarmonyPatch(typeof(PMMissionParameter), nameof(PMMissionParameter.ChangeMissionName))]
    [HarmonyPrefix]
    private static void PMMissionParameterChangeMissionNamePrefix(PMMissionParameter __instance, ref string _missionName)
    {
        if (string.IsNullOrEmpty(_missionName) || !_missionName.StartsWith("Cyclical missions", StringComparison.Ordinal))
            return;

        var name = LogisticsObserver.FindLogisticsCycleName(__instance);
        if (string.IsNullOrEmpty(name))
            return;

        _missionName = name;
    }

    [HarmonyPatch(typeof(PMTabSchedule), "CreateFly")]
    [HarmonyPrefix]
    private static bool CreateFlyPrefix(PMTabSchedule __instance)
    {
        var pmw = Traverse.Create(__instance).Field("planMissionWindow").GetValue<PlanMissionWindow>();
        var pmp = pmw?.PMMissionParameter;
        if (!MightBeLogisticsPlan(pmp))
            return true;

        var found = LogisticsObserver.FindLogisticsCycleName(pmp);
        var missionName = found ?? pmp?.MissionName;
        if (pmp != null
            && !string.IsNullOrEmpty(missionName)
            && missionName.StartsWith("[LOGI]", StringComparison.Ordinal)
            && !HasPositiveNormalResourceCargo(pmp.CargoAll))
        {
            var sc = pmp.SC as Spacecraft;
            var cmd = sc == null ? null : MonoBehaviourSingleton<CycleMissionManager>.Instance?.GetCycleMission(sc);
            LogisticsObserver.LogWarning(
                $"PLAN blocked-empty-logi-flight: name=\"{missionName}\" " +
                $"route={pmp.Start?.ObjectName ?? "null"}->{pmp.Target?.ObjectName ?? "null"} " +
                $"sc={DescribeSpacecraft(pmp.SC)} cmd={cmd != null} cargo={DescribeCargo(pmp.CargoAll)}");
            if (cmd != null)
                LogisticsObserver.RemoveLogisticsCycle(MonoBehaviourSingleton<CycleMissionManager>.Instance, cmd);
            return false;
        }
        if (LogisticsObserver.VerboseLoggingEnabled && pmp != null && !string.IsNullOrEmpty(found))
        {
            LogisticsObserver.Log(
                $"NAMING TRACE createfly-prefix: pmpName=\"{pmp.MissionName}\" found=\"{found ?? "null"}\" " +
                $"sc={DescribeSpacecraft(pmp.SC)} route={pmp.Start?.ObjectName ?? "null"}->{pmp.Target?.ObjectName ?? "null"}");
        }
        if (LogisticsObserver.VerboseLoggingEnabled && pmp != null && !string.IsNullOrEmpty(missionName) && missionName.StartsWith("[LOGI", StringComparison.Ordinal))
        {
            LogisticsObserver.Log($"LOGI-LAUNCH createfly-prefix: name=\"{missionName}\" route={pmp.Start?.ObjectName ?? "null"}->{pmp.Target?.ObjectName ?? "null"} result={pmp.CheckCanPlanMission().planMissionResult} {DescribePayload(pmp)} sc={DescribeSpacecraft(pmp.SC)}");
        }
        RestoreLogisticsMissionName(pmw?.PMMissionParameter, "createfly");
        return true;
    }

    [HarmonyPatch(typeof(PMTabSchedule), "CreateFly")]
    [HarmonyPostfix]
    private static void CreateFlyPostfix(PMTabSchedule __instance)
    {
        var pmw = Traverse.Create(__instance).Field("planMissionWindow").GetValue<PlanMissionWindow>();
        var pmp = pmw?.PMMissionParameter;
        if (pmp == null) return;
        if (!MightBeLogisticsPlan(pmp)
            && (pmp.MissionName == null || !pmp.MissionName.StartsWith("[LOGI", StringComparison.Ordinal)))
        {
            return;
        }

        var found = LogisticsObserver.FindLogisticsCycleName(pmp);
        if (string.IsNullOrEmpty(found) && (pmp.MissionName == null || !pmp.MissionName.StartsWith("[LOGI", StringComparison.Ordinal)))
            return;
        if (LogisticsObserver.VerboseLoggingEnabled)
        {
            var result = pmp.CheckCanPlanMission().planMissionResult;
            LogisticsObserver.Log(
                $"NAMING TRACE createfly-postfix: pmpName=\"{pmp.MissionName}\" found=\"{found ?? "null"}\" " +
                $"sc={DescribeSpacecraft(pmp.SC)} route={pmp.Start?.ObjectName ?? "null"}->{pmp.Target?.ObjectName ?? "null"}");
            LogisticsObserver.Log($"LOGI-LAUNCH createfly-postfix: pmpName=\"{pmp.MissionName}\" route={pmp.Start?.ObjectName ?? "null"}->{pmp.Target?.ObjectName ?? "null"} result={result} {DescribePayload(pmp)} sc={DescribeSpacecraft(pmp.SC)}");
        }
    }

    [HarmonyPatch(typeof(PMTabSchedule), "CreatedTrajectory")]
    [HarmonyPrefix]
    private static bool CreatedTrajectoryPrefix(PMTabSchedule __instance)
    {
        var pmw = Traverse.Create(__instance).Field("planMissionWindow").GetValue<PlanMissionWindow>();
        var pmp = pmw?.PMMissionParameter;
        if (pmw?.ForCode == true && MightBeLogisticsPlan(pmp) && LogisticsObserver.IsLogisticsPlan(pmp))
        {
            LogisticsObserver.LogVerbose($"PLAN suppress-preview-trajectory: {pmp.Start?.ObjectName ?? "null"}->{pmp.Target?.ObjectName ?? "null"}");
            return false;
        }

        return true;
    }

    private static void RestoreLogisticsMissionName(PMMissionParameter pmp, string context)
    {
        if (!MightBeLogisticsPlan(pmp))
            return;

        var name = LogisticsObserver.FindLogisticsCycleName(pmp);
        if (string.IsNullOrEmpty(name))
            return;

        pmp.ChangeMissionName(name, _manualChangeName: true);
        LogisticsObserver.LogVerbose($"PLAN mission-name-prep: context={context} name=\"{name}\"");
    }

    [HarmonyPatch(typeof(MissionsLabelsMainUI), nameof(MissionsLabelsMainUI.SetData))]
    [HarmonyPostfix]
    private static void MissionsLabelsSetDataPostfix(MissionsLabelsMainUI __instance, MissionInfo mi)
    {
        if (mi == null) return;
        var isInteresting = mi.missionCreator == MissionInfo.EMissionCreator.Cyclical
            || (mi.missionName != null && mi.missionName.IndexOf("LOGI", StringComparison.Ordinal) >= 0);
        if (!isInteresting) return;

        var sc = mi.spacecraftInfo2 as Spacecraft;
        if (!string.IsNullOrEmpty(mi.missionName) && mi.missionName.StartsWith("[LOGI", StringComparison.Ordinal))
        {
            var txtMissionName = Traverse.Create(__instance).Field("txtMissionName").GetValue<TextMeshProUGUI>();
            if (txtMissionName != null)
                txtMissionName.text = mi.missionName;
            mi.fromCyclicalMission = true;
        }

        if (LogisticsObserver.VerboseLoggingEnabled)
        {
            var cmd = sc == null ? null : MonoBehaviourSingleton<CycleMissionManager>.Instance?.GetCycleMission(sc);
            LogisticsObserver.Log(
                $"NAMING TRACE flight-label-setdata: id={mi.id} miName=\"{mi.missionName}\" " +
                $"fromCycle={mi.fromCyclicalMission} sc={DescribeSpacecraft(sc)} " +
                $"cmd={cmd != null} cmdName=\"{cmd?.customNameFromPlanMission ?? "null"}\" " +
                $"route={mi.start?.ObjectName ?? "null"}->{mi.target?.ObjectName ?? "null"}");
        }
    }

    [HarmonyPatch(typeof(MissionRow), "SetMissionInfo")]
    [HarmonyPostfix]
    private static void MissionRowSetMissionInfoPostfix(MissionInfo _missionInfo)
    {
        if (!LogisticsObserver.VerboseLoggingEnabled) return;
        if (_missionInfo == null) return;
        if (_missionInfo.missionCreator != MissionInfo.EMissionCreator.Cyclical
            && (_missionInfo.missionName == null || _missionInfo.missionName.IndexOf("LOGI", StringComparison.Ordinal) < 0))
            return;

        LogisticsObserver.Log(
            $"NAMING TRACE mission-row-set: id={_missionInfo.id} name=\"{_missionInfo.missionName}\" " +
            $"fromCycle={_missionInfo.fromCyclicalMission} sc={DescribeSpacecraft(_missionInfo.spacecraftInfo2)} " +
            $"route={_missionInfo.start?.ObjectName ?? "null"}->{_missionInfo.target?.ObjectName ?? "null"}");
    }

    [HarmonyPatch(typeof(MissionRowNew), "SetMissionInfo", new Type[] { typeof(MissionInfo), typeof(UnityEngine.Color), typeof(string), typeof(MissionListByType.EMissionType) })]
    [HarmonyPostfix]
    private static void MissionRowNewSetMissionInfoPostfix(MissionInfo _missionInfo, string stringActionText, MissionListByType.EMissionType _missionType)
    {
        if (!LogisticsObserver.VerboseLoggingEnabled) return;
        if (_missionInfo == null) return;
        if (_missionInfo.missionCreator != MissionInfo.EMissionCreator.Cyclical
            && (_missionInfo.missionName == null || _missionInfo.missionName.IndexOf("LOGI", StringComparison.Ordinal) < 0))
            return;

        LogisticsObserver.Log(
            $"NAMING TRACE mission-row-new-set: id={_missionInfo.id} name=\"{_missionInfo.missionName}\" " +
            $"type={_missionType} action=\"{stringActionText}\" fromCycle={_missionInfo.fromCyclicalMission} " +
            $"sc={DescribeSpacecraft(_missionInfo.spacecraftInfo2)} route={_missionInfo.start?.ObjectName ?? "null"}->{_missionInfo.target?.ObjectName ?? "null"}");
    }

    private static string FindLogisticsCycleNameFor(ISpacecraftInfo spacecraftInfo, List<ISpacecraftInfo> listSC)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm == null) return null;

        if (spacecraftInfo is Spacecraft sc)
        {
            var cmd = cm.GetCycleMission(sc);
            if (cmd?.customNameFromPlanMission != null
                && cmd.customNameFromPlanMission.StartsWith("[LOGI", StringComparison.Ordinal))
                return cmd.customNameFromPlanMission;
        }

        if (listSC == null) return null;
        foreach (var sci in listSC)
        {
            if (sci is not Spacecraft listShip) continue;
            var cmd = cm.GetCycleMission(listShip);
            if (cmd?.customNameFromPlanMission != null
                && cmd.customNameFromPlanMission.StartsWith("[LOGI", StringComparison.Ordinal))
                return cmd.customNameFromPlanMission;
        }

        return null;
    }

    private static bool MightBeLogisticsPlan(PMMissionParameter pmp)
    {
        if (pmp == null)
            return false;
        if (pmp.MissionName != null && pmp.MissionName.StartsWith("[LOGI", StringComparison.Ordinal))
            return true;
        if (pmp.SC is Spacecraft sc)
        {
            var cmd = MonoBehaviourSingleton<CycleMissionManager>.Instance?.GetCycleMission(sc);
            if (cmd?.customNameFromPlanMission != null
                && cmd.customNameFromPlanMission.StartsWith("[LOGI", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return pmp.ForCyclicalMission || pmp.MissionCreator == MissionInfo.EMissionCreator.Cyclical;
    }

    private static bool ShouldSuppressCyclicalArrivalNotification(Spacecraft sc, string context)
    {
        var mi = sc?.GetMissionInfo();
        if (mi == null)
            return false;

        var suppress = mi.fromCyclicalMission || LogisticsObserver.IsLogisticsMissionInfo(mi);
        if (suppress)
            LogisticsObserver.LogVerbose(
                $"NOTIFY suppress-cyclical-arrival: context={context} mission={mi.id} name=\"{mi.missionName}\" " +
                $"fromCycle={mi.fromCyclicalMission} sc={DescribeSpacecraft(sc)} route={mi.start?.ObjectName ?? "null"}->{mi.target?.ObjectName ?? "null"}");

        return suppress;
    }

    private static string DescribeSpacecraft(ISpacecraftInfo spacecraftInfo)
    {
        if (spacecraftInfo is not Spacecraft sc)
            return spacecraftInfo?.GetTypeSpaceCraft()?.NameRocketType ?? "null";

        return $"{sc.GetSpacecraftName() ?? sc.spacecraftName ?? sc.spacecraftType?.NameRocketType ?? "SC"}#{sc.ID}/phase={sc.CurrentPhase}";
    }

    private static string DescribeCargo(CargoAll cargoAll)
    {
        if (cargoAll == null)
            return "null";

        var parts = new List<string>();
        if (cargoAll.listCargo != null)
        {
            foreach (var cargo in cargoAll.listCargo)
            {
                if (cargo?.resourceType == null) continue;
                parts.Add($"{cargo.resourceType.ID}:{cargo.cargoMass:0.#}");
            }
        }

        if (cargoAll.cargoFuel?.resourceType != null && cargoAll.cargoFuel.cargoMass > 0)
            parts.Add($"fuel/{cargoAll.cargoFuel.resourceType.ID}:{cargoAll.cargoFuel.cargoMass:0.#}");

        return parts.Count == 0 ? "empty" : string.Join(",", parts);
    }

    private static string DescribePayload(PMMissionParameter pmp)
    {
        if (pmp == null)
            return "payload=null";

        var scType = pmp.SC?.GetTypeSpaceCraft();
        var capacity = (scType?.GetCargoCapacity(pmp.FlyCompany) ?? 0) * Math.Max(1, pmp.SCCount);
        var cargo = pmp.CargoAll?.CargoCurrent ?? 0;
        var propellantTarget = pmp.CargoAll?.cargoFuel?.cargoMassPotencjal ?? 0;
        var propellantActual = pmp.CargoAll?.cargoFuel?.cargoMass ?? 0;
        return $"payload={cargo:0.#}/{capacity:0.#} propellantTarget={propellantTarget:0.#} propellantActual={propellantActual:0.#} cargo={DescribeCargo(pmp.CargoAll)}";
    }

    private static string DescribeCycleCargoTabs(CycleMissionsData cmd)
    {
        if (cmd == null)
            return "tabs=null";

        return $"tabsA={DescribeTab(cmd.cargoAllStart)} tabsB={DescribeTab(cmd.cargoAllEnd)}";
    }

    private static string DescribeTab(InfoCargoCyclicalMission info)
    {
        if (info?.Tab == null || info.Tab.Length == 0)
            return "empty";

        var parts = new List<string>();
        foreach (var rd in info.Tab)
        {
            if (rd == null) continue;
            parts.Add(rd.ID);
        }

        return parts.Count == 0 ? "empty" : string.Join(",", parts);
    }

    private static string DescribeCycleEnds(CycleMissionsData cmd)
    {
        if (cmd == null)
            return "endsData=null";

        return $"endsMaxA={DescribeEndsData(cmd.EndsResourceCountMaxA)} " +
            $"endsDoneA={DescribeEndsData(cmd.EndsResourceCountDataA)} " +
            $"endsMaxB={DescribeEndsData(cmd.EndsResourceCountMaxB)} " +
            $"endsDoneB={DescribeEndsData(cmd.EndsResourceCountDataB)}";
    }

    private static string DescribeEndsData(EndsResourceCountData data)
    {
        if (data?.listData == null || data.listData.Count == 0)
            return "empty";

        var parts = new List<string>();
        foreach (var part in data.listData)
        {
            if (part?.rd == null) continue;
            parts.Add($"{part.rd.ID}:{part.count:0.#}");
        }

        return parts.Count == 0 ? "empty" : string.Join(",", parts);
    }

    private static string DescribeStock(ObjectInfo objectInfo, Company company, CycleMissionsData cmd, bool startIsA)
    {
        if (objectInfo == null || company == null || cmd == null)
            return "null";

        var data = startIsA ? cmd.EndsResourceCountMaxA : cmd.EndsResourceCountMaxB;
        if (data?.listData == null || data.listData.Count == 0)
            data = startIsA ? cmd.EndsResourceCountMaxB : cmd.EndsResourceCountMaxA;

        if (data?.listData == null || data.listData.Count == 0)
            return "no-ends-resources";

        var oid = objectInfo.GetObjectInfoData(company);
        if (oid == null)
            return "no-object-data";

        var parts = new List<string>();
        foreach (var part in data.listData)
        {
            if (part?.rd == null) continue;
            parts.Add($"{part.rd.ID}:{oid.CheckResources(part.rd):0.#}");
        }

        return parts.Count == 0 ? "empty" : string.Join(",", parts);
    }

    private static bool HasPositiveNormalResourceCargo(CargoAll cargoAll)
    {
        if (cargoAll?.listCargo == null)
            return false;

        foreach (var cargo in cargoAll.listCargo)
        {
            if (cargo == null) continue;
            if (cargo.resourceTypeType == EResourceTypeType.resorces
                && cargo.resourceType != null
                && cargo.cargoMass > 0)
                return true;
        }

        return false;
    }
}
