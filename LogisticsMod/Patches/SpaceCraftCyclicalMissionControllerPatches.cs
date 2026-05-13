using HarmonyLib;
using Game.UI.Windows.Elements.PlanMissionElements;
using LogisticsMod.Logic;
using Manager;
using System;

namespace LogisticsMod.Patches;

[HarmonyPatch]
internal static class SpaceCraftCyclicalMissionControllerPatches
{
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.SetPMParameterForCodeJobSystem))]
    [HarmonyPrefix]
    private static void SetPMParameterForCodeJobSystemPrefix(PMMissionParameter _pmMissionParameter, ref Action result)
    {
        if (!LogisticsObserver.IsLogisticsPlan(_pmMissionParameter)) return;

        _pmMissionParameter.TryFixWrongThrust = true;

        var original = result;
        result = () =>
        {
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
            LogisticsObserver.Log($"SKIP LOGI replanning: {cmd.customNameFromPlanMission}");
            return false;
        }
        return true;
    }
}
