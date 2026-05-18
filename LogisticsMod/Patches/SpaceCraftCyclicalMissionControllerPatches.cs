using HarmonyLib;
using CustomUpdate;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.UI.Windows.Elements.MissionsElements;
using Game.UI.Windows.Elements.PlanMissionElements;
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
        var isLogi = LogisticsObserver.IsLogisticsPlan(_pmMissionParameter);
        var cmdFromShip = (_pmMissionParameter?.SC as Spacecraft) == null
            ? null
            : MonoBehaviourSingleton<CycleMissionManager>.Instance?.GetCycleMission((Spacecraft)_pmMissionParameter.SC);
        if (!isLogi
            && cmdFromShip?.customNameFromPlanMission != null
            && cmdFromShip.customNameFromPlanMission.StartsWith("[LOGI", StringComparison.Ordinal))
        {
            isLogi = true;
            LogisticsObserver.LogWarning(
                $"LOGI-CODEJOB recovered-cycle-map: pmpName=\"{_pmMissionParameter?.MissionName ?? "null"}\" " +
                $"cmdName=\"{cmdFromShip.customNameFromPlanMission}\" sc={DescribeSpacecraft(_pmMissionParameter?.SC)} " +
                $"route={_pmMissionParameter?.Start?.ObjectName ?? "null"}->{_pmMissionParameter?.Target?.ObjectName ?? "null"}");
        }
        if (!isLogi) return;

        _pmMissionParameter.TryFixWrongThrust = true;
        if (cmdFromShip != null)
            cmdFromShip.wasSetPMParameterForCodeJobSystem = true;
        LogisticsObserver.LogWarning(
            $"LOGI-CODEJOB prefix: pmpName=\"{_pmMissionParameter.MissionName}\" " +
            $"cmdName=\"{cmdFromShip?.customNameFromPlanMission ?? "null"}\" " +
            $"wasSet={cmdFromShip?.wasSetPMParameterForCodeJobSystem.ToString() ?? "null"} " +
            $"sc={DescribeSpacecraft(_pmMissionParameter.SC)} " +
            $"route={_pmMissionParameter.Start?.ObjectName ?? "null"}->{_pmMissionParameter.Target?.ObjectName ?? "null"} " +
            $"{DescribePayload(_pmMissionParameter)}");
        LogisticsObserver.Log(
            $"NAMING TRACE codejob-prefix: pmpName=\"{_pmMissionParameter.MissionName}\" " +
            $"sc={DescribeSpacecraft(_pmMissionParameter.SC)} " +
            $"route={_pmMissionParameter.Start?.ObjectName ?? "null"}->{_pmMissionParameter.Target?.ObjectName ?? "null"} " +
            $"creator={_pmMissionParameter.MissionCreator}");

        var original = result;
        result = () =>
        {
            LogisticsObserver.LogWarning(
                $"LOGI-CODEJOB callback-before: pmpName=\"{_pmMissionParameter.MissionName}\" " +
                $"found=\"{LogisticsObserver.FindLogisticsCycleName(_pmMissionParameter) ?? "null"}\" " +
                $"cmdName=\"{cmdFromShip?.customNameFromPlanMission ?? "null"}\" " +
                $"result={_pmMissionParameter.CheckCanPlanMission().planMissionResult} " +
                $"{DescribePayload(_pmMissionParameter)}");
            LogisticsObserver.Log(
                $"NAMING TRACE codejob-callback-before: pmpName=\"{_pmMissionParameter.MissionName}\" " +
                $"found=\"{LogisticsObserver.FindLogisticsCycleName(_pmMissionParameter) ?? "null"}\" " +
                $"sc={DescribeSpacecraft(_pmMissionParameter.SC)}");
            RestoreLogisticsMissionName(_pmMissionParameter, "codejob");
            LogisticsObserver.CapLogisticsCargoForPlannerLimits(_pmMissionParameter);
            LogisticsObserver.Log($"LOGI-SCHEDULE selected-before-create: {_pmMissionParameter.Start?.ObjectName ?? "null"}->{_pmMissionParameter.Target?.ObjectName ?? "null"} depart={_pmMissionParameter.DepartureTimeDate:yyyy-MM-dd} arrive={_pmMissionParameter.Arrival:yyyy-MM-dd} result={_pmMissionParameter.CheckCanPlanMission().planMissionResult} {DescribePayload(_pmMissionParameter)} fastClick={_pmMissionParameter.ClickFastestButton} tryFast={_pmMissionParameter.TryFastAsPossible} tryFixWrongThrust={_pmMissionParameter.TryFixWrongThrust}");
            original?.Invoke();
            LogisticsObserver.LogWarning(
                $"LOGI-CODEJOB callback-after: pmpName=\"{_pmMissionParameter.MissionName}\" " +
                $"found=\"{LogisticsObserver.FindLogisticsCycleName(_pmMissionParameter) ?? "null"}\" " +
                $"cmdName=\"{cmdFromShip?.customNameFromPlanMission ?? "null"}\" " +
                $"result={_pmMissionParameter.CheckCanPlanMission().planMissionResult} " +
                $"{DescribePayload(_pmMissionParameter)}");
            LogisticsObserver.Log(
                $"NAMING TRACE codejob-callback-after: pmpName=\"{_pmMissionParameter.MissionName}\" " +
                $"found=\"{LogisticsObserver.FindLogisticsCycleName(_pmMissionParameter) ?? "null"}\" " +
                $"sc={DescribeSpacecraft(_pmMissionParameter.SC)}");
            LogisticsObserver.Log($"LOGI-SCHEDULE after-create: {_pmMissionParameter.Start?.ObjectName ?? "null"}->{_pmMissionParameter.Target?.ObjectName ?? "null"} depart={_pmMissionParameter.DepartureTimeDate:yyyy-MM-dd} arrive={_pmMissionParameter.Arrival:yyyy-MM-dd} result={_pmMissionParameter.CheckCanPlanMission().planMissionResult} {DescribePayload(_pmMissionParameter)} fastClick={_pmMissionParameter.ClickFastestButton}");
        };
    }

    [HarmonyPatch(typeof(SpaceCraftCyclicalMissionController), nameof(SpaceCraftCyclicalMissionController.TryPlanCycleMission))]
    [HarmonyPrefix]
    private static bool TryPlanCycleMissionPrefix(SpaceCraftCyclicalMissionController __instance)
    {
        var cmd = __instance.CycleMissionsData;
        if (cmd == null) return true;
        if (cmd.customNameFromPlanMission != null
            && cmd.customNameFromPlanMission.StartsWith("[LOGI", StringComparison.Ordinal))
        {
            var sc = Traverse.Create(__instance).Field("sc").GetValue<Spacecraft>();
            LogisticsObserver.LogWarning(
                $"LOGI-CYCLE tryplan-prefix: name=\"{cmd.customNameFromPlanMission}\" " +
                $"planFlyWas={__instance.CycleMissionPlanFlyWas} wasSet={cmd.wasSetPMParameterForCodeJobSystem} pause={cmd.Pause} " +
                $"route={cmd.A?.ObjectName ?? "null"}->{cmd.B?.ObjectName ?? "null"} " +
                $"sc={DescribeSpacecraft(sc)} current={sc?.CurrentlyOnThisObject?.ObjectName ?? "null"} " +
                $"phase={sc?.CurrentPhase.ToString() ?? "null"} countSC={cmd.CountSC} " +
                $"cargoStart={cmd.CargoStart} cargoEnd={cmd.CargoEnd} transfer={cmd.TransferType} ends={cmd.Ends} " +
                $"{DescribeCycleEnds(cmd)} {DescribeCycleCargoTabs(cmd)}");
        }
        if (cmd.customNameFromPlanMission != null
            && cmd.customNameFromPlanMission.StartsWith("[LOGI")
            && __instance.CycleMissionPlanFlyWas)
        {
            LogisticsObserver.LogWarning($"SKIP LOGI replanning: {cmd.customNameFromPlanMission}");
            return false;
        }
        return true;
    }

    [HarmonyPatch(typeof(ObjectInfoData), nameof(ObjectInfoData.CreatedCargoToTakeNormal))]
    [HarmonyPostfix]
    private static void CreatedCargoToTakeNormalPostfix(CargoAll __result, ECargoStart cargoStart,
        CycleMissionsData cycleMissionsData, ObjectInfo startObject, Spacecraft sc, LaunchVehicle lv,
        bool allResourceOnPlanet, double? loadLimit2, int countSC, bool addSupply, TimeSpan? missionLenght)
    {
        if (cycleMissionsData?.customNameFromPlanMission == null
            || !cycleMissionsData.customNameFromPlanMission.StartsWith("[LOGI", StringComparison.Ordinal))
        {
            return;
        }

        var company = sc?.GetCompany() ?? cycleMissionsData.Company;
        LogisticsObserver.LogWarning(
            $"LOGI-CARGO created: name=\"{cycleMissionsData.customNameFromPlanMission}\" " +
            $"cargoStart={cargoStart} start={startObject?.ObjectName ?? "null"} " +
            $"route={cycleMissionsData.A?.ObjectName ?? "null"}->{cycleMissionsData.B?.ObjectName ?? "null"} " +
            $"sc={DescribeSpacecraft(sc)} scCurrent={sc?.CurrentlyOnThisObject?.ObjectName ?? "null"} " +
            $"lv={lv?.GetLaunchVehicleType()?.Name ?? "null"} countSC={countSC} " +
            $"loadLimit={(loadLimit2.HasValue ? loadLimit2.Value.ToString("0.#") : "null")} " +
            $"addSupply={addSupply} missionDays={(missionLenght.HasValue ? missionLenght.Value.TotalDays.ToString("0.#") : "null")} " +
            $"allResourceOnPlanet={allResourceOnPlanet} cargo={DescribeCargo(__result)} " +
            $"cargoCurrent={__result?.CargoCurrent.ToString("0.#") ?? "null"} " +
            $"{DescribeCycleEnds(cycleMissionsData)} " +
            $"stockStart={DescribeStock(startObject, company, cycleMissionsData, startObject == cycleMissionsData.A)}");
    }

    [HarmonyPatch(typeof(PMTabSchedule), nameof(PMTabSchedule.OnClickScheduleButtonForCode))]
    [HarmonyPostfix]
    private static void OnClickScheduleButtonForCodePostfix(ref MissionInfo __result)
    {
        if (__result == null)
        {
            LogisticsObserver.Log("NAMING TRACE schedule-postfix: result=null");
            return;
        }
        if (__result.spacecraftInfo2 is not Spacecraft sc)
        {
            LogisticsObserver.Log(
                $"NAMING TRACE schedule-postfix: id={__result.id} name=\"{__result.missionName}\" " +
                $"creator={__result.missionCreator} sc={DescribeSpacecraft(__result.spacecraftInfo2)} no-spacecraft");
            return;
        }

        var cmd = MonoBehaviourSingleton<CycleMissionManager>.Instance?.GetCycleMission(sc);
        LogisticsObserver.Log(
            $"NAMING TRACE schedule-postfix: id={__result.id} name=\"{__result.missionName}\" " +
            $"creator={__result.missionCreator} sc={DescribeSpacecraft(sc)} " +
            $"cmd={cmd != null} cmdName=\"{cmd?.customNameFromPlanMission ?? "null"}\"");
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
        LogisticsObserver.Log(
            $"NAMING TRACE createinfo-prefix: incoming=\"{missionName}\" found=\"{name ?? "null"}\" " +
            $"sc={DescribeSpacecraft(spacecraftInfo)} listSC={listSC?.Count ?? 0} " +
            $"route={trajectoryObject?.StartObjectInfo?.ObjectName ?? "null"}->{trajectoryObject?.EndObjectInfo?.ObjectName ?? "null"} " +
            $"company={company?.name ?? "null"} cargo={DescribeCargo(cargoAll)}");
        if (string.IsNullOrEmpty(name))
            return;

        if (missionName != name)
            LogisticsObserver.Log($"PLAN mission-name-createinfo: \"{missionName}\" -> \"{name}\"");
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
        if (string.IsNullOrEmpty(name))
            name = LogisticsObserver.FindLogisticsCycleName(
                __result.start,
                __result.target,
                __result.company,
                __result.ListSpacecraftInfo2,
                __result.cargoAll);
        LogisticsObserver.Log(
            $"NAMING TRACE createinfo-postfix: id={__result.id} current=\"{__result.missionName}\" found=\"{name ?? "null"}\" " +
            $"fromCycle={__result.fromCyclicalMission} creator={__result.missionCreator} " +
            $"sc={DescribeSpacecraft(__result.spacecraftInfo2)} listSC={__result.ListSpacecraftInfo2?.Count ?? 0} " +
            $"route={__result.start?.ObjectName ?? "null"}->{__result.target?.ObjectName ?? "null"} cargo={DescribeCargo(__result.cargoAll)}");

        if (string.IsNullOrEmpty(name))
        {
            if (__result.missionName != null && __result.missionName.StartsWith("[LOGI", StringComparison.Ordinal))
                __result.fromCyclicalMission = true;
            return;
        }

        if (__result.missionName != name)
            LogisticsObserver.Log($"PLAN mission-name-postcreate: \"{__result.missionName}\" -> \"{name}\"");
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
        LogisticsObserver.Log(
            $"NAMING TRACE pmp-change-name: incoming=\"{_missionName}\" found=\"{name ?? "null"}\" " +
            $"current=\"{__instance.MissionName}\" sc={DescribeSpacecraft(__instance.SC)} " +
            $"route={__instance.Start?.ObjectName ?? "null"}->{__instance.Target?.ObjectName ?? "null"}");
        if (string.IsNullOrEmpty(name))
            return;

        if (_missionName != name)
            LogisticsObserver.Log($"PLAN mission-name-param: \"{_missionName}\" -> \"{name}\"");
        _missionName = name;
    }

    [HarmonyPatch(typeof(PMTabSchedule), "CreateFly")]
    [HarmonyPrefix]
    private static bool CreateFlyPrefix(PMTabSchedule __instance)
    {
        var pmw = Traverse.Create(__instance).Field("planMissionWindow").GetValue<PlanMissionWindow>();
        var pmp = pmw?.PMMissionParameter;
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
                MonoBehaviourSingleton<CycleMissionManager>.Instance?.RemoveCycleMission(cmd);
            return false;
        }
        if (pmp != null && !string.IsNullOrEmpty(LogisticsObserver.FindLogisticsCycleName(pmp)))
        {
            LogisticsObserver.Log(
                $"NAMING TRACE createfly-prefix: pmpName=\"{pmp.MissionName}\" found=\"{LogisticsObserver.FindLogisticsCycleName(pmp) ?? "null"}\" " +
                $"sc={DescribeSpacecraft(pmp.SC)} route={pmp.Start?.ObjectName ?? "null"}->{pmp.Target?.ObjectName ?? "null"}");
        }
        if (pmp != null && !string.IsNullOrEmpty(missionName) && missionName.StartsWith("[LOGI", StringComparison.Ordinal))
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
        var found = LogisticsObserver.FindLogisticsCycleName(pmp);
        if (string.IsNullOrEmpty(found) && (pmp.MissionName == null || !pmp.MissionName.StartsWith("[LOGI", StringComparison.Ordinal)))
            return;
        LogisticsObserver.Log(
            $"NAMING TRACE createfly-postfix: pmpName=\"{pmp.MissionName}\" found=\"{found ?? "null"}\" " +
            $"sc={DescribeSpacecraft(pmp.SC)} route={pmp.Start?.ObjectName ?? "null"}->{pmp.Target?.ObjectName ?? "null"}");
        LogisticsObserver.Log($"LOGI-LAUNCH createfly-postfix: pmpName=\"{pmp.MissionName}\" route={pmp.Start?.ObjectName ?? "null"}->{pmp.Target?.ObjectName ?? "null"} result={pmp.CheckCanPlanMission().planMissionResult} {DescribePayload(pmp)} sc={DescribeSpacecraft(pmp.SC)}");
    }

    [HarmonyPatch(typeof(PMTabSchedule), "CreatedTrajectory")]
    [HarmonyPrefix]
    private static bool CreatedTrajectoryPrefix(PMTabSchedule __instance)
    {
        var pmw = Traverse.Create(__instance).Field("planMissionWindow").GetValue<PlanMissionWindow>();
        var pmp = pmw?.PMMissionParameter;
        if (pmw?.ForCode == true && LogisticsObserver.IsLogisticsPlan(pmp))
        {
            LogisticsObserver.LogVerbose($"PLAN suppress-preview-trajectory: {pmp.Start?.ObjectName ?? "null"}->{pmp.Target?.ObjectName ?? "null"}");
            return false;
        }

        return true;
    }

    private static void RestoreLogisticsMissionName(PMMissionParameter pmp, string context)
    {
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
        var cmd = sc == null ? null : MonoBehaviourSingleton<CycleMissionManager>.Instance?.GetCycleMission(sc);
        if (!string.IsNullOrEmpty(mi.missionName) && mi.missionName.StartsWith("[LOGI", StringComparison.Ordinal))
        {
            var txtMissionName = Traverse.Create(__instance).Field("txtMissionName").GetValue<TextMeshProUGUI>();
            if (txtMissionName != null)
                txtMissionName.text = mi.missionName;
            mi.fromCyclicalMission = true;
        }

        LogisticsObserver.Log(
            $"NAMING TRACE flight-label-setdata: id={mi.id} miName=\"{mi.missionName}\" " +
            $"fromCycle={mi.fromCyclicalMission} sc={DescribeSpacecraft(sc)} " +
            $"cmd={cmd != null} cmdName=\"{cmd?.customNameFromPlanMission ?? "null"}\" " +
            $"route={mi.start?.ObjectName ?? "null"}->{mi.target?.ObjectName ?? "null"}");
    }

    [HarmonyPatch(typeof(MissionRow), "SetMissionInfo")]
    [HarmonyPostfix]
    private static void MissionRowSetMissionInfoPostfix(MissionInfo _missionInfo)
    {
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
