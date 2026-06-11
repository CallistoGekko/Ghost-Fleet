using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LogisticsMod.Logic;
using LogisticsMod.Patches;
using System;
using System.IO;

namespace LogisticsMod;

[BepInPlugin("com.logisticsmod", "Logistics Tab", "0.4.2.4")]
public class Plugin : BaseUnityPlugin
{
    private const string BuildLabel = "ghost-fleet-2026-06-11-logistics-launcher-hotfix";
    private const string BuildFeatures = "popup-editor,panel-bound-click-guard,escape-closes-logistics,route-ledger,route-destination-search,route-picker-minus-stack-step,balanced-route-resource-lift,route-balanced-craft-dispatch,route-resource-priority,route-pause-controls,route-health-summary,route-lv-cargo-lift,crew-safe-human-lift,crew-supply-reservation,crew-virtual-capsule-mass,ghost-estimated-flight-timing,ghost-upkeep-accounting,route-owned-cleanup,route-release-to-vanilla,route-launch-vehicle-assignment,route-owned-launch-vehicles,no-route-scopes,no-orphan-craft-launch-ui,route-resource-icon-summary,route-ship-count-summary,route-owned-spacecraft,route-resource-keep-target,route-first-dispatch,batched-ghost-dispatch,ghost-convoy-flights,virtual-orbit-drop,virtual-surface-lift,shared-facility-lift,ghost-spacecraft-ledger,reserved-launch-vehicles,batch-ghost-adoption,ghost-craft-release,ghost-flight-dispatch,ghost-flight-craft-label,ghost-fuel-reservation,ambient-ghost-traffic,ghost-trail-refresh";

    public static Plugin Instance { get; private set; }
    public static ConfigEntry<bool> VirtualSurfaceLiftEnabled { get; private set; }
    public static ConfigEntry<double> VirtualSurfaceLiftPayloadsPerDay { get; private set; }
    public static ConfigEntry<bool> VerboseLogging { get; private set; }
    public static ConfigEntry<bool> VanillaMissionDiagnosticsEnabled { get; private set; }
    private static ConfigFile _pluginConfig;

    private void Awake()
    {
        Instance = this;
        var pluginConfigPath = Path.Combine(Paths.PluginPath, "logisticsmod", "LogisticsMod.cfg");
        _pluginConfig = new ConfigFile(pluginConfigPath, saveOnInit: true);
        VirtualSurfaceLiftEnabled = _pluginConfig.Bind("SurfaceLift", "Enabled", true,
            "When enabled, same-body surface-to-orbit logistics use facility launch capacity as direct daily stock movement. Physical logistics missions are not created.");
        VirtualSurfaceLiftPayloadsPerDay = _pluginConfig.Bind("SurfaceLift", "PayloadsPerFacilityPerDay", 1.0,
            "How many full facility-backed launch payloads each enabled launch facility can move to its own orbit per in-game day.");
        VerboseLogging = _pluginConfig.Bind("Diagnostics", "VerboseLogging", false,
            "When enabled, per-request route and dispatch diagnostics are written to BepInEx/LogisticsMod_*.log.");
        VanillaMissionDiagnosticsEnabled = _pluginConfig.Bind("Diagnostics", "VanillaMissionPlannerDiagnostics", false,
            "Experimental: logs vanilla Plan Mission internals. Leave disabled unless actively comparing a single mission planner case.");
        _pluginConfig.Save();
        try
        {
            Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, "com.logisticsmod");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Harmony patching failed: {ex}");
            LogisticsObserver.LogError($"Plugin Harmony patching failed: {ex.GetType().Name}:{ex.Message}");
            throw;
        }

        if (VanillaMissionDiagnosticsEnabled.Value)
        {
            try
            {
                PlanMissionDiagnosticsPatches.LogPatchStatus();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Mission diagnostics patch status failed: {ex}");
                LogisticsObserver.LogError($"Mission diagnostics patch status failed: {ex.GetType().Name}:{ex.Message}");
            }
        }

        Logger.LogInfo($"Build {BuildLabel} loaded; features={BuildFeatures}; surfaceLift={VirtualSurfaceLiftEnabled.Value}; payloadsPerFacilityPerDay={VirtualSurfaceLiftPayloadsPerDay.Value:0.##}");
        LogisticsObserver.Log($"Plugin loaded! build={BuildLabel} source=LogisticsModTeddFork features={BuildFeatures} config={pluginConfigPath} surfaceLift={VirtualSurfaceLiftEnabled.Value} payloadsPerFacilityPerDay={VirtualSurfaceLiftPayloadsPerDay.Value:0.##} vanillaMissionDiagnostics={VanillaMissionDiagnosticsEnabled.Value}");
    }
}
