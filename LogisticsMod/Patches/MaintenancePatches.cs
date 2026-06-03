using System;
using Game;
using Game.ObjectInfoDataScripts;
using HarmonyLib;
using LogisticsMod.Data;
using LogisticsMod.Logic;
using Manager;

namespace LogisticsMod.Patches;

[HarmonyPatch(typeof(ObjectInfoData), "CalculateMaintenanceCostAndIncome")]
internal static class MaintenancePatches
{
    [HarmonyPostfix]
    private static void Postfix(ObjectInfoData __instance, double days,
        ref ValueTuple<double, double, double, double, double> __result)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (__instance?.ObjectInfo == null || player == null || __instance.company != player)
            return;

        var data = LogisticsNetwork.Get(__instance.ObjectInfo);
        if (data == null)
            return;

        var spacecraftCost = CalculateGhostSpacecraftMaintenance(data, player, days);
        var launchVehicleCost = CalculateGhostLaunchVehicleMaintenance(data, player, days);
        if (spacecraftCost <= 0 && launchVehicleCost <= 0)
            return;

        __result.Item3 += spacecraftCost;
        __result.Item4 += launchVehicleCost;
        LogisticsObserver.LogVerbose(
            $"UPKEEP ghost-ledger: object={__instance.ObjectInfo.ObjectName} days={days:0.###} sc={spacecraftCost:0.##} lv={launchVehicleCost:0.##}");
    }

    private static double CalculateGhostSpacecraftMaintenance(LogisticsObjectData data, Company player, double days)
    {
        if (data?.ghostCraft == null || player == null || days <= 0)
            return 0;

        var allTypes = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllSpacecraftType;
        if (allTypes == null)
            return 0;

        var total = 0.0;
        foreach (var craft in data.ghostCraft)
        {
            if (craft == null || craft.status == GhostCraftStatus.Retired)
                continue;

            var type = allTypes.GetByID(craft.shipTypeId);
            if (type == null)
                continue;

            total += days
                * (double)type.MaintenanceCostPerDay
                * player.BonusController.GetBonusMaintenanceReduce(type);
        }

        return total;
    }

    private static double CalculateGhostLaunchVehicleMaintenance(LogisticsObjectData data, Company player, double days)
    {
        if (data?.ghostLaunchVehicles == null || player == null || days <= 0)
            return 0;

        var allTypes = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllLaunchVehicleType;
        if (allTypes == null)
            return 0;

        var total = 0.0;
        foreach (var launchVehicle in data.ghostLaunchVehicles)
        {
            if (launchVehicle == null || launchVehicle.status == GhostLaunchVehicleStatus.Retired)
                continue;

            var type = allTypes.GetByID(launchVehicle.launchVehicleTypeId);
            if (type == null)
                continue;

            total += days
                * (double)type.MaintenanceCostPerDay
                * player.BonusController.GetBonusMaintenanceReduce(type);
        }

        return total;
    }
}
