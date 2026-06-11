using HarmonyLib;
using LogisticsMod.Logic;
using Manager;

namespace LogisticsMod.Patches;

[HarmonyPatch]
internal static class SaveLoadPatches
{
    [HarmonyPatch(typeof(LoadSaveManager), "ExtractAllFromSaveData")]
    [HarmonyPrefix]
    private static void ExtractAllPrefix()
    {
        ResetLoadState();
    }

    [HarmonyPatch(typeof(LoadSaveManager), "SaveToFile", new[] { typeof(string) })]
    [HarmonyPostfix]
    private static void SaveToFilePostfix(string saveName)
    {
        Data.LogisticsPersistence.Save(saveName);
    }

    [HarmonyPatch(typeof(LoadSaveManager), "ExtractAllFromSaveData")]
    [HarmonyPostfix]
    private static void ExtractAllPostfix()
    {
        var saveName = SerializedMonoBehaviourSingleton<LoadSaveManager>.Instance?.LastSaveName;
        if (!string.IsNullOrEmpty(saveName))
            Data.LogisticsPersistence.Load(saveName);

        Data.LogisticsNetwork.ReleaseOrphanedRouteAssets();
    }

    private static void ResetLoadState()
    {
        Data.LogisticsNetwork.ClearAll();
        LogisticsObserver.ResetRuntimeState();
        TimeControllerPatches.ResetRuntimeFlags();
    }
}
