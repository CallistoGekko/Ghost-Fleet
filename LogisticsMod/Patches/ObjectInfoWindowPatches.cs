using HarmonyLib;
using Game.UI;
using Game.UI.Screens;
using Game.UI.Windows;
using Game.UI.Windows.Windows;
using LogisticsMod.Logic;
using Manager;
using UnityEngine;

namespace LogisticsMod.Patches;

[HarmonyPatch]
internal static class ObjectInfoWindowPatches
{
    [HarmonyPatch(typeof(ObjectInfoWindow), "Awake")]
    [HarmonyPostfix]
    private static void AwakePostfix(ObjectInfoWindow __instance)
    {
        if (__instance == null) return;
        if (__instance.GetComponent<UI.LogisticsUI>() != null) return;
        __instance.gameObject.AddComponent<UI.LogisticsUI>();
    }

    [HarmonyPatch(typeof(ObjectInfoWindow), "SetData", new[] { typeof(Game.ObjectInfoDataScripts.ObjectInfoData), typeof(bool) })]
    [HarmonyPostfix]
    private static void SetDataPostfix(ObjectInfoWindow __instance, Game.ObjectInfoDataScripts.ObjectInfoData objectInfoData, bool fromObjectName)
    {
        var oi = objectInfoData?.ObjectInfo;
        if (LogisticsObserver.VerboseLoggingEnabled)
        {
            var nameStr = oi?.ObjectName ?? "NULL";
            var idStr = oi?.id ?? -1;
            LogisticsObserver.Log($"DIAG SetData: OIW={__instance.GetInstanceID()} obj=\"{nameStr}\" id={idStr} fromObjectName={fromObjectName}");
        }

        var l = __instance.GetComponent<UI.LogisticsUI>();
        if (l != null && l.isActiveAndEnabled)
            l.RefreshData(objectInfoData);
        else if (LogisticsObserver.VerboseLoggingEnabled)
            LogisticsObserver.LogWarning($"DIAG SetData: LogisticsUI null or disabled on OIW={__instance.GetInstanceID()}");
    }

    [HarmonyPatch(typeof(ObjectInfoWindow), "RebuildLayout")]
    [HarmonyPostfix]
    private static void RebuildLayoutPostfix(ObjectInfoWindow __instance)
    {
        var l = __instance.GetComponent<UI.LogisticsUI>();
        if (l != null && l.isActiveAndEnabled)
            l.RebuildLayout();
    }
}

[HarmonyPatch(typeof(InputManager), "Update")]
internal static class InputManagerPopupBlockPatch
{
    [HarmonyPrefix]
    private static bool UpdatePrefix(InputManager __instance)
    {
        if (!UI.LogisticsUI.AnyPopupOpen || !Input.GetMouseButtonUp(0))
            return true;
        if (!UI.LogisticsUI.PointerIsOverPopupPanel())
            return true;

        __instance?.BlockInputForMoment();
        return false;
    }
}

[HarmonyPatch(typeof(UIManager), "Update")]
internal static class UIManagerEscapePatch
{
    [HarmonyPrefix]
    private static bool UpdatePrefix()
    {
        return !UI.LogisticsUI.ConsumeEscapeIfPopupOpen();
    }
}

[HarmonyPatch(typeof(UIManager), "Open")]
internal static class UIManagerLogisticsPopupLayerPatch
{
    [HarmonyPostfix]
    private static void OpenPostfix()
    {
        if (UI.LogisticsUI.AnyPopupOpen)
            UI.LogisticsUI.PlaceOpenPopupsOnWindowLayer();
    }
}

[HarmonyPatch(typeof(PauseScreen), "Update")]
internal static class PauseScreenEscapePatch
{
    [HarmonyPrefix]
    private static bool UpdatePrefix()
    {
        return !UI.LogisticsUI.ConsumeEscapeIfPopupOpen();
    }
}

[HarmonyPatch(typeof(SettingsWindow), "Update")]
internal static class SettingsWindowEscapePatch
{
    [HarmonyPrefix]
    private static bool UpdatePrefix()
    {
        return !UI.LogisticsUI.ConsumeEscapeIfPopupOpen();
    }
}

[HarmonyPatch(typeof(LoadSaveDialogWindow), "Update")]
internal static class LoadSaveDialogWindowEscapePatch
{
    [HarmonyPrefix]
    private static bool UpdatePrefix()
    {
        return !UI.LogisticsUI.ConsumeEscapeIfPopupOpen();
    }
}
