using HarmonyLib;
using Manager;

namespace LogisticsMod.Patches;

[HarmonyPatch(typeof(TimeController), "Update")]
internal static class TimeControllerPatches
{
    private static bool _subscribed;

    public static void ResetRuntimeFlags()
    {
        _subscribed = false;
    }

    [HarmonyPrefix]
    private static void Prefix(TimeController __instance)
    {
        if (!_subscribed)
        {
            _subscribed = true;
            __instance.onEachDayChange += Logic.LogisticsObserver.OnDayChange;
        }

        Logic.LogisticsObserver.UpdateGhostFlightVisuals();
    }
}
