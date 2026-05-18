using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LogisticsMod.Logic;
using System.IO;

namespace LogisticsMod;

[BepInPlugin("com.logisticsmod", "Logistics Tab", "0.2.0")]
public class Plugin : BaseUnityPlugin
{
    public static Plugin Instance { get; private set; }
    public static ConfigEntry<bool> ReturnFuelEnabled { get; private set; }
    public static ConfigEntry<double> ReturnFuelSafetyMultiplier { get; private set; }
    public static ConfigEntry<bool> ReturnFuelReserveCargoFirst { get; private set; }
    public static ConfigEntry<bool> ReturnFuelTrustDomesticOnlyAfterStockpile { get; private set; }
    public static ConfigEntry<int> ReturnFuelMinimumDomesticReserveDays { get; private set; }
    public static ConfigEntry<double> CyclePlanningGraceDays { get; private set; }
    public static ConfigEntry<double> BlockedMissionRetryCooldownDays { get; private set; }
    public static ConfigEntry<bool> VerboseLogging { get; private set; }
    private static ConfigFile _pluginConfig;

    private void Awake()
    {
        Instance = this;
        var pluginConfigPath = Path.Combine(Paths.PluginPath, "logisticsmod", "LogisticsMod.cfg");
        _pluginConfig = new ConfigFile(pluginConfigPath, saveOnInit: true);
        ReturnFuelEnabled = _pluginConfig.Bind("ReturnFuel", "Enabled", true,
            "When enabled, logistics missions try to stage enough fuel at the destination for the logistics vessel to return.");
        ReturnFuelSafetyMultiplier = _pluginConfig.Bind("ReturnFuel", "SafetyMultiplier", 1.5,
            "Multiplier applied to the estimated return fuel reserve.");
        ReturnFuelReserveCargoFirst = _pluginConfig.Bind("ReturnFuel", "ReserveCargoFirst", true,
            "When enabled, return-fuel reserve cargo is prioritized over the requested logistics cargo.");
        ReturnFuelTrustDomesticOnlyAfterStockpile = _pluginConfig.Bind("ReturnFuel", "TrustDomesticOnlyAfterStockpile", true,
            "When enabled, local/domestic fuel production is trusted only after the destination already has the estimated reserve stockpile.");
        ReturnFuelMinimumDomesticReserveDays = _pluginConfig.Bind("ReturnFuel", "MinimumDomesticReserveDays", 0,
            "Reserved for a later production-rate policy. The current first pass uses stockpile only.");
        CyclePlanningGraceDays = _pluginConfig.Bind("Diagnostics", "CyclePlanningGraceDays", 3.0,
            "In-game days a freshly created LOGI cycle is considered 'still being planned' before being treated as stale. The async code job system normally fires inside this window; raise if you see spurious CLEANUP warnings under heavy time acceleration.");
        BlockedMissionRetryCooldownDays = _pluginConfig.Bind("Diagnostics", "BlockedMissionRetryCooldownDays", 30.0,
            "In-game days to wait before retrying the same blocked or stale logistics dispatch attempt.");
        VerboseLogging = _pluginConfig.Bind("Diagnostics", "VerboseLogging", false,
            "When enabled, per-request route and dispatch diagnostics are written to BepInEx/LogisticsMod_*.log.");
        _pluginConfig.Save();
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, "com.logisticsmod");
        LogisticsObserver.Log($"Plugin loaded! build=diagnostic-2026-05-11 source=Documents/SolarExpanseMods/LogisticsMod config={pluginConfigPath} returnFuel={ReturnFuelEnabled.Value} margin={ReturnFuelSafetyMultiplier.Value:0.##}");
    }
}
