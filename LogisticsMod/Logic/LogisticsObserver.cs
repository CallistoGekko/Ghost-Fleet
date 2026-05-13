using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomUpdate;
using Data;
using Data.ScriptableObject;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.UI.Windows.Elements.PlanMissionElements;
using Game.UI.Windows.Windows;
using Game.VisualizationScripts;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;

namespace LogisticsMod.Logic;

public static class LogisticsObserver
{
    private static List<Spacecraft> _cachedSpacecraft;
    private static List<LaunchVehicle> _cachedLaunchVehicles;
    private static StreamWriter _logWriter;
    private static int _logSession;
    private static HashSet<int> _busyCatapultIds = new HashSet<int>();

    public static void ResetRuntimeState()
    {
        Log($"RESET runtime-state");
        _busyCatapultIds.Clear();
    }

    internal static void Log(string msg)
    {
        WriteLog("", msg);
        Debug.Log("[LogisticsMod] " + msg);
    }

    internal static void LogWarning(string msg)
    {
        WriteLog("[WARN] ", msg);
        Debug.LogWarning("[LogisticsMod] " + msg);
    }

    internal static void LogError(string msg)
    {
        WriteLog("[ERROR] ", msg);
        Debug.LogError("[LogisticsMod] " + msg);
    }

    private static void WriteLog(string level, string msg)
    {
        if (_logWriter == null)
        {
            _logSession++;
            var path = Path.Combine(Application.dataPath, "..", "BepInEx", $"LogisticsMod_{_logSession}.log");
            _logWriter = new StreamWriter(path, false) { AutoFlush = true };
            _logWriter.WriteLine($"=== {DateTime.Now} session={_logSession} ===");
        }
        var line = $"[{DateTime.Now:HH:mm:ss}] {level}{msg}";
        _logWriter.WriteLine(line);
    }

    public static void OnDayChange(double days)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (player == null) return;

        var networkResources = Data.LogisticsNetwork.GetNetworkResourcesSet(player);

        _cachedSpacecraft = UnityEngine.Object.FindObjectsOfType<Spacecraft>()
            .Where(sc => sc != null && sc.GetCompany() == player).ToList();
        _cachedLaunchVehicles = UnityEngine.Object.FindObjectsOfType<LaunchVehicle>()
            .Where(lv => lv != null && lv.GetCompany() == player).ToList();

        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        var allCycleMissions = cm?.GetAllCycleMission(player) ?? new List<CycleMissionsData>();
        var allObjects = Data.LogisticsNetwork.GetAllObjects();

        CountActiveLogisticsCycles(allCycleMissions, out var scActive, out var lvActive, out var _);

        var usedLvIds = new HashSet<int>();
        var usedCatapultIds = new HashSet<int>();

        Log($"OnDayChange: {allObjects.Count} objects, {allCycleMissions.Count} cycles");

        // Log all active LOGI missions from MissionInfoManager
        var mimDiag = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        if (mimDiag != null)
        {
            // Clear busy catapults whose async PlanFlyCode completed
            var toRemove = new List<int>();
            foreach (var cid in _busyCatapultIds)
            {
                bool hasMission = false;
            foreach (var mi in mimDiag.ListMissionInfo)
            {
                if (mi.complete || mi.cancel) continue;
                if (mi.company != player) continue;
                if (!mi.missionName.StartsWith("[LOGI]")) continue;
                var sc = mi.spacecraftInfo2 as Spacecraft;
                if (sc != null && sc.GetHashCode() == cid)
                    { hasMission = true; break; }
            }
                if (hasMission)
                    toRemove.Add(cid);
            }
            foreach (var cid in toRemove)
                _busyCatapultIds.Remove(cid);

            int logiCount = 0;
            foreach (var mi in mimDiag.ListMissionInfo)
            {
                if (mi.complete || mi.cancel) continue;
                if (mi.company != player) continue;
                if (!mi.missionName.StartsWith("[LOGI]")) continue;
                logiCount++;
                var sc = mi.spacecraftInfo2 as Spacecraft;
                Log($"[MCNT] LOGI mission: \"{mi.missionName}\" target={mi.target?.ObjectName} launch={mi.DateLaunch} phase={sc?.CurrentPhase} complete={mi.complete}");
            }
            Log($"[MCNT] Total active LOGI missions: {logiCount}");
        }

        foreach (var requesterOI in allObjects)
        {
            var reqData = Data.LogisticsNetwork.Get(requesterOI);
            if (reqData == null) continue;

            foreach (var req in reqData.requests)
            {
                var rd = req.ResourceDefinition;
                var rdName = rd?.Name ?? "NULL";

                Log($"[{requesterOI.ObjectName}] req={rdName} amt={req.requestedAmount} status={req.status}");

                if (req.status == Data.LogisticsRequestStatus.Satisfied
                    || req.status == Data.LogisticsRequestStatus.Failed)
                {
                    if (rd != null)
                    {
                        var currentCount = requesterOI.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
                        Log($"[{requesterOI.ObjectName}] {rdName}: Satisfied/Failed check — currentCount={currentCount} requested={req.requestedAmount}");
                        if (currentCount < req.requestedAmount)
                        {
                            req.status = Data.LogisticsRequestStatus.Pending;
                            Log($"[{requesterOI.ObjectName}] {rdName}: reopened to Pending (stock dropped)");
                        }
                    }
                    if (req.status == Data.LogisticsRequestStatus.Satisfied)
                    {
                        Log($"[{requesterOI.ObjectName}] {rdName}: still Satisfied, skip");
                        continue;
                    }
                    if (req.status == Data.LogisticsRequestStatus.Satisfied
                        || req.status == Data.LogisticsRequestStatus.Failed)
                    {
                        req.statusNote = null;
                        continue;
                    }
                }

                if (req.status == Data.LogisticsRequestStatus.Pending
                    && req.statusNote != null && req.statusNote.StartsWith("stuck_"))
                {
                    if (int.TryParse(req.statusNote.Substring(6), out var skipDays) && skipDays > 0)
                    {
                        req.statusNote = $"stuck_{skipDays - 1}";
                        Log($"[{requesterOI.ObjectName}] {rdName}: stuck skip {skipDays - 1} days left");
                        continue;
                    }
                    req.statusNote = null;
                }

                if (req.status == Data.LogisticsRequestStatus.InProgress)
                {
                    var allCycles = cm?.GetAllCycleMission(player) ?? new List<CycleMissionsData>();
                    bool hacdResult = HasActiveCycleDelivering(requesterOI, rd, allCycles);
                    if (hacdResult)
                    {
                        Log($"[{requesterOI.ObjectName}] {rdName}: InProgress, mission confirmed by HACD — skip");
                        continue;
                    }

                    int retries = 0;
                    if (req.statusNote != null && req.statusNote.StartsWith("retry_") && int.TryParse(req.statusNote.Substring(6), out var parsed))
                        retries = parsed;

                    const int maxRetries = 3;
                    if (retries >= maxRetries)
                    {
                        LogWarning($"[{requesterOI.ObjectName}] {rdName}: InProgress but no mission after {retries} retries — resetting to Pending");
                        req.status = Data.LogisticsRequestStatus.Pending;
                        req.statusNote = null;
                        continue;
                    }

                    req.statusNote = $"retry_{retries + 1}";
                    Log($"[{requesterOI.ObjectName}] {rdName}: InProgress, async mission pending (retry {retries + 1}/{maxRetries}) — skip");
                    continue;
                }

                if (req.status == Data.LogisticsRequestStatus.Pending)
                {
                    bool hasInNetwork = rd != null && networkResources.Contains(rd);
                    req.statusNote = hasInNetwork ? null : "No provider in network";
                    if (!hasInNetwork)
                        Log($"[{requesterOI.ObjectName}] {rdName}: No provider in network (skip)");
                }
                else
                    req.statusNote = null;
                if (rd == null) continue;

                var alreadyThere = requesterOI.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
                Log($"[{requesterOI.ObjectName}] {rdName}: alreadyThere={alreadyThere} requestedAmount={req.requestedAmount}");
                if (alreadyThere >= req.requestedAmount)
                {
                    req.status = Data.LogisticsRequestStatus.Satisfied;
                    Log($"[{requesterOI.ObjectName}] {rdName}: Satisfied (alreadyThere >= requested)");
                    continue;
                }

                bool hasActiveDelivery = HasActiveCycleDelivering(requesterOI, rd, allCycleMissions);
                Log($"[{requesterOI.ObjectName}] {rdName}: HasActiveCycleDelivering={hasActiveDelivery}");
                if (hasActiveDelivery)
                {
                    req.status = Data.LogisticsRequestStatus.InProgress;
                    continue;
                }

                double remaining = req.requestedAmount - alreadyThere;

                // subtract in-transit resources from active LOGI missions
                double inTransit = 0;
                var mim = MonoBehaviourSingleton<MissionInfoManager>.Instance;
                if (mim != null)
                {
                    foreach (var mi in mim.ListMissionInfo)
                    {
                        if (mi.complete || mi.cancel) continue;
                        if (mi.company != player) continue;
                        if (mi.target != requesterOI) continue;
                        if (!mi.missionName.StartsWith("[LOGI]")) continue;
                        if (mi.cargoAll?.listCargo == null) continue;
                        foreach (var c in mi.cargoAll.listCargo)
                        {
                            if (c.resourceType == rd)
                                inTransit += c.cargoMass;
                        }
                    }
                }
                Log($"[{requesterOI.ObjectName}] {rdName}: remaining={remaining} inTransit={inTransit}");
                remaining -= inTransit;
                if (remaining <= 0)
                {
                    req.status = Data.LogisticsRequestStatus.Satisfied;
                    Log($"[{requesterOI.ObjectName}] {rdName}: remaining<=0 after inTransit, Satisfied");
                    continue;
                }

                Log($"[{requesterOI.ObjectName}] {rdName}: creating delivery remaining={remaining}");
                TryCreateDeliveries(req, requesterOI, rd, remaining, player, scActive, lvActive, usedLvIds, usedCatapultIds);
            }
        }
        CleanupStuckMissions(player, cm);

        _cachedSpacecraft = null;
        _cachedLaunchVehicles = null;
    }

    private static bool HasActiveCycleDelivering(ObjectInfo requester, ResourceDefinition rd,
        List<CycleMissionsData> allCycleMissions)
    {
        var reqName = requester?.ObjectName ?? "?";
        var rdName = rd?.Name ?? "?";
        var reqHash = requester?.GetHashCode() ?? 0;

        Log($"[HACD] {reqName}/{rdName}: checking {allCycleMissions?.Count ?? 0} cycles (requester hash={reqHash})");

        int cyclesChecked = 0;
        foreach (var cmd in allCycleMissions)
        {
            if (cmd.B != requester) continue;
            if (!cmd.customNameFromPlanMission.StartsWith("[LOGI]")) continue;
            if (cmd.CheckComplete()) continue;
            if (cmd.cargoAllStart?.Tab == null) continue;
            cyclesChecked++;

            foreach (var tabRes in cmd.cargoAllStart.Tab)
            {
                if (tabRes == rd)
                {
                    Log($"[HACD] {reqName}/{rdName}: FOUND in cycle \"{cmd.customNameFromPlanMission}\" (cyclesChecked={cyclesChecked})");
                    return true;
                }
            }
        }

        var mim = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        int logiInMim = 0;
        int matchedTarget = 0;
        if (mim != null)
        {
            foreach (var mi in mim.ListMissionInfo)
            {
                if (mi.complete || mi.cancel) continue;
                if (!mi.missionName.StartsWith("[LOGI]")) continue;
                logiInMim++;
                if (mi.target != requester) continue;
                matchedTarget++;

                if (mi.cargoAll?.listCargo == null) continue;

                bool hasRD = false;
                foreach (var c in mi.cargoAll.listCargo)
                {
                    if (c.resourceType == rd) { hasRD = true; break; }
                }
                if (!hasRD) continue;

                if (mi.missionName.StartsWith("[LOGI]"))
                {
                    Log($"[HACD] {reqName}/{rdName}: FOUND in LOGI mission \"{mi.missionName}\" ID={mi.id} phase={((mi.spacecraftInfo2 as Spacecraft)?.CurrentPhase)} launch={mi.DateLaunch} (logiInMIM={logiInMim} matchedTarget={matchedTarget})");
                    return true;
                }

                if (mi.fromCyclicalMission && mi.spacecraftInfo2 is Spacecraft miSc)
                {
                    var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
                    var cmd = cm?.GetCycleMission(miSc);
                    if (cmd != null && cmd.customNameFromPlanMission.StartsWith("[LOGI]"))
                    {
                        Log($"[HACD] {reqName}/{rdName}: FOUND via cyclical mission \"{cmd.customNameFromPlanMission}\" (logiInMIM={logiInMim} matchedTarget={matchedTarget})");
                        return true;
                    }
                }
            }
        }

        Log($"[HACD] {reqName}/{rdName}: NOT FOUND (cyclesChecked={cyclesChecked} logiInMIM={logiInMim} matchedTarget={matchedTarget} mimNull={mim==null})");
        return false;
    }

    public static void GetActiveCycleCounts(Company player,
        out Dictionary<string, int> scActive, out Dictionary<string, int> lvActive)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        var all = cm?.GetAllCycleMission(player) ?? new List<CycleMissionsData>();
        CountActiveLogisticsCycles(all, out scActive, out lvActive, out var _);
    }

    private static void CountActiveLogisticsCycles(List<CycleMissionsData> allCycleMissions,
        out Dictionary<string, int> scActive, out Dictionary<string, int> lvActive,
        out Dictionary<string, int> mrActive)
    {
        scActive = new Dictionary<string, int>();
        lvActive = new Dictionary<string, int>();
        mrActive = new Dictionary<string, int>();

        foreach (var cmd in allCycleMissions)
        {
            if (cmd.CheckComplete()) continue;
            if (!cmd.customNameFromPlanMission.StartsWith("[LOGI]")) continue;
            if (cmd.ListSC == null) continue;

            foreach (var sci in cmd.ListSC)
            {
                var sc = sci as Spacecraft;
                if (sc == null || sc.spacecraftType == null) continue;
                var tn = Data.LogisticsNetwork.TypeKey(sc.spacecraftType.ID, sc.spacecraftType.NameRocketType ?? "SC");
                if (!scActive.ContainsKey(tn)) scActive[tn] = 0;
                scActive[tn]++;
            }

            if (cmd.LvTypeA != null)
            {
                var tnA = Data.LogisticsNetwork.TypeKey(cmd.LvTypeA.ID, cmd.LvTypeA.Name ?? "LV");
                if (!lvActive.ContainsKey(tnA)) lvActive[tnA] = 0;
                lvActive[tnA]++;
            }
            if (cmd.LvTypeB != null)
            {
                var tnB = Data.LogisticsNetwork.TypeKey(cmd.LvTypeB.ID, cmd.LvTypeB.Name ?? "LV");
                if (!lvActive.ContainsKey(tnB)) lvActive[tnB] = 0;
                lvActive[tnB]++;
            }
        }
    }

    private static double GetTargetDistanceFromSun(ObjectInfo target)
    {
        var oim = MonoBehaviourSingleton<ObjectInfoManager>.Instance;
        var parentObjectInfo = target;
        int maxSteps = 100;
        while (parentObjectInfo != null
               && parentObjectInfo.parentObjectInfo != null
               && parentObjectInfo.parentObjectInfo != oim.mainObjectInfoSun)
        {
            parentObjectInfo = parentObjectInfo.parentObjectInfo;
            if (--maxSteps <= 0) break;
        }
        var solarBody = parentObjectInfo?.SolarBody;
        if (solarBody != null) return solarBody.a;
        if (parentObjectInfo?.objectTypes == EObjectTypes.SolarOrbit) return 0.01f;
        return 1.0f;
    }

    private static bool CanSolarShipReach(Spacecraft sc, ObjectInfo target, Company player)
    {
        var scType = sc.spacecraftType;
        if (scType == null || !scType.SolarSC) return true;
        double solarRange = scType.GetSolarRange(player);
        double targetAu = GetTargetDistanceFromSun(target);
        return solarRange > targetAu;
    }

    private static bool HasFuelForReturn(Spacecraft sc, ObjectInfo destination, Company player)
    {
        var scType = sc.spacecraftType;
        if (scType == null || scType.SolarSC) return true;
        var fuelType = scType.GetFuelType();
        if (fuelType == null) return true;
        double fuelCapacity = scType.GetFuelCapacity(player);
        if (fuelCapacity <= 0) return true;
        var oid = destination.GetObjectInfoData(player);
        if (oid == null) return false;
        return oid.CheckResources(fuelType) >= fuelCapacity;
    }

    private static Spacecraft GetMagneticCatapultFromFacility(ObjectInfo providerOI, Company player,
        HashSet<int> usedCatapultIds)
    {
        var oid = providerOI.GetObjectInfoData(player);
        if (oid == null)
        {
            LogWarning($"[GMCF] {providerOI?.ObjectName}: oid is null");
            return null;
        }
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        int totalCatapults = 0;
        Log($"[GMCF] {providerOI.ObjectName}: scanning facility, usedCatapultIds=[{string.Join(",", usedCatapultIds)}]");
        foreach (var rr in oid.GetListSpacecraftFacility())
        {
            var sc = rr.spacecraft;
            if (sc == null || sc.spacecraftType == null || !sc.spacecraftType.MagneticCatapult) continue;
            if (sc.GetCompany() != player) continue;
            totalCatapults++;
            var scName = sc.spacecraftType.NameRocketType ?? "?";
            var scId = sc.GetHashCode();
            Log($"[GMCF] {providerOI.ObjectName}: found catapult {scName} id={scId}");
            if (usedCatapultIds.Contains(scId) || _busyCatapultIds.Contains(scId))
            {
                Log($"[GMCF] {providerOI.ObjectName}: catapult {scName} already used, skip");
                continue;
            }
            if (!sc.IsReadyToPlan())
            {
                Log($"[GMCF] {providerOI.ObjectName}: catapult {scName} NOT ready");
                continue;
            }
            var existingCycle = cm.GetCycleMission(sc);
            if (existingCycle != null)
            {
                if (!existingCycle.customNameFromPlanMission.StartsWith("[LOGI]"))
                {
                    Log($"[GMCF] {providerOI.ObjectName}: catapult {scName} has non-LOGI cycle, skip");
                    continue;
                }
                Log($"[GMCF] {providerOI.ObjectName}: catapult {scName} removing stale cycle \"{existingCycle.customNameFromPlanMission}\"");
                cm.RemoveCycleMission(sc);
                ResetCycleRequests(existingCycle);
            }

            if (HasCatapultActiveMission(sc, player))
            {
                Log($"[GMCF] {providerOI.ObjectName}: catapult {scName} has active one-shot LOGI mission, skip");
                continue;
            }

            Log($"[GMCF] {providerOI.ObjectName}: returning catapult {scName} id={scId}");
            return sc;
        }
        Log($"[GMCF] {providerOI.ObjectName}: {totalCatapults} catapults found, NONE available");
        return null;
    }

    private static bool HasCatapultActiveMission(Spacecraft sc, Company player)
    {
        var mim = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        if (mim == null) return false;
        foreach (var mi in mim.ListMissionInfo)
        {
            if (mi.complete || mi.cancel) continue;
            if (mi.company != player) continue;
            if (!mi.missionName.StartsWith("[LOGI]")) continue;
            if (mi.spacecraftInfo2 as Spacecraft == sc)
                return true;
        }
        return false;
    }

    private static List<ObjectInfo> GetBestProviderOrder(
        Data.LogisticsRequest req, ObjectInfo requester, ResourceDefinition rd,
        double remaining, Company player,
        Dictionary<string, int> scActive,
        CycleMissionManager cm)
    {
        var scored = new List<(ObjectInfo provider, double deliverable)>();

        foreach (var providerOI in Data.LogisticsNetwork.GetAllObjects())
        {
            if (providerOI == requester) continue;

            var provData = Data.LogisticsNetwork.Get(providerOI);
            if (provData == null) continue;
            if (!provData.providers.Any(p => p.isActive && p.ResourceDefinition == rd))
                continue;

            var oid = providerOI.GetObjectInfoData(player);
            if (oid == null) continue;

            var available = oid.CheckResources(rd);
            var minKeep = provData.providers.Where(p => p.isActive && p.ResourceDefinition == rd).Sum(p => p.minimumKeep);
            available -= minKeep;
            if (available <= 0) continue;

            double usableExcess = available - req.requestedAmount;
            double maxTake25 = Math.Floor(usableExcess * 0.25);
            double maxDeliver500 = req.requestedAmount * 5.0;
            double missionCap = Math.Min(maxTake25, maxDeliver500);
            double targetAmount = Math.Min(available, missionCap);
            if (targetAmount <= 0) continue;

            // Best single-ship SC capacity (closest from above, or largest if none)
            double bestSCCapacity = 0;
            foreach (var quota in provData.spacecraftQuota ?? new List<Data.ShipQuotaEntry>())
            {
                if (quota.count <= 0) continue;
                scActive.TryGetValue(quota.typeName, out var activeOfType);
                var canUse = quota.count - activeOfType;
                if (canUse <= 0) continue;

                var idleSC = _cachedSpacecraft
                    .Where(sc => sc != null && sc.spacecraftType != null
                        && !sc.spacecraftType.MagneticCatapult
                        && sc.CurrentlyOnThisObject == providerOI
                        && sc.CurrentPhase == Spacecraft.EPhase.None
                        && Data.LogisticsNetwork.QuotaMatches(quota, sc.spacecraftType.ID, sc.spacecraftType.NameRocketType ?? "SC")
                        && cm.GetCycleMission(sc) == null
                        && CanSolarShipReach(sc, requester, player)
                        && HasFuelForReturn(sc, requester, player))
                    .OrderByDescending(sc => sc.spacecraftType.GetCargoCapacity(player))
                    .ToList();

                if (idleSC.Count == 0) continue;

                var bestSingle = idleSC
                    .Where(sc => sc.spacecraftType.GetCargoCapacity(player) >= targetAmount)
                    .OrderBy(sc => sc.spacecraftType.GetCargoCapacity(player))
                    .FirstOrDefault();

                double typeBest = bestSingle != null
                    ? bestSingle.spacecraftType.GetCargoCapacity(player)
                    : idleSC[0].spacecraftType.GetCargoCapacity(player);

                if (typeBest > bestSCCapacity) bestSCCapacity = typeBest;
            }

            // Best LV payload (single launch)
            double bestLVCapacity = 0;
            var enabledLVTypes = (provData.launchVehicleQuota ?? new List<Data.ShipQuotaEntry>())
                .Where(q => q.count > 0)
                .Select(q => q.typeName)
                .ToHashSet();

            if (enabledLVTypes.Count > 0)
            {
                var readyLV = _cachedLaunchVehicles
                    .Where(lv => lv != null && lv.launchVehicleType != null
                        && lv.objectInfo == providerOI
                        && lv.IsReadyToLaunchReusable()
                        && enabledLVTypes.Contains(Data.LogisticsNetwork.TypeKey(lv.launchVehicleType.ID, lv.launchVehicleType.Name ?? "LV")))
                    .ToList();

                foreach (var lv in readyLV)
                {
                    double payload = lv.launchVehicleType.MaxPayloadOnThisObject(providerOI, player);
                    if (payload > bestLVCapacity) bestLVCapacity = payload;
                }
            }

            double bestCapacity = Math.Max(bestSCCapacity, bestLVCapacity);
            if (bestCapacity <= 0) continue;

            double deliverable = Math.Min(targetAmount, bestCapacity);
            scored.Add((providerOI, deliverable));
        }

        return scored
            .OrderBy(p => p.deliverable >= remaining ? 0 : 1)
            .ThenBy(p => p.deliverable >= remaining
                ? p.deliverable - remaining
                : remaining - p.deliverable)
            .Select(p => p.provider)
            .ToList();
    }

    private static void TryCreateDeliveries(Data.LogisticsRequest req, ObjectInfo requester,
        ResourceDefinition rd, double remaining, Company player,
        Dictionary<string, int> scActive, Dictionary<string, int> lvActive,
        HashSet<int> usedLvIds, HashSet<int> usedCatapultIds)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm == null) return;

        var reqName = requester?.ObjectName ?? "?";
        var rdName = rd?.Name ?? "?";

        Log($"[TCD] {reqName}/{rdName}: starting remaining={remaining}");

        if (req.status == Data.LogisticsRequestStatus.InProgress)
        {
            Log($"[TCD] {reqName}/{rdName}: req already InProgress — stop (prevents duplicates)");
            return;
        }

        var orderedProviders = GetBestProviderOrder(req, requester, rd, remaining, player, scActive, cm);
        foreach (var providerOI in orderedProviders)
        {
            if (remaining <= 0) break;

            var provName = providerOI?.ObjectName ?? "?";

            var provData = Data.LogisticsNetwork.Get(providerOI);
            if (provData == null) continue;

            if (!provData.providers.Any(p => p.isActive && p.ResourceDefinition == rd))
                continue;

            var oid = providerOI.GetObjectInfoData(player);
            if (oid == null) continue;

            var available = oid.CheckResources(rd);
            var minKeep = provData.providers.Where(p => p.isActive && p.ResourceDefinition == rd).Sum(p => p.minimumKeep);
            available -= minKeep;
            if (available <= 0)
            {
                Log($"[TCD] {reqName}/{rdName}: prov={provName} available={available} <= 0 -> skip");
                continue;
            }

            double toDeliver = available;

            double usableExcess = available - req.requestedAmount;
            double maxTake25 = Math.Floor(usableExcess * 0.25);
            double maxDeliver500 = req.requestedAmount * 5.0;
            double missionCap = Math.Min(maxTake25, maxDeliver500);
            double targetAmount = Math.Min(toDeliver, missionCap);
            if (targetAmount <= 0) continue;

            Log($"[TCD] {reqName}/{rdName}: prov={provName} available={available} minKeep={minKeep} toDeliver={toDeliver} targetAmount={targetAmount} missionCap={missionCap}");

            bool delivered = false;

            Log($"[TCD] {reqName}/{rdName}: SC quotas={provData.spacecraftQuota?.Count ?? 0} LV quotas={provData.launchVehicleQuota?.Count ?? 0} idleSC={_cachedSpacecraft?.Count ?? 0} idleLV={_cachedLaunchVehicles?.Count ?? 0}");

            // SC delivery
            foreach (var quota in provData.spacecraftQuota ?? new List<Data.ShipQuotaEntry>())
            {
                if (quota.count <= 0) continue;
                scActive.TryGetValue(quota.typeName, out var activeOfType);
                var canUse = quota.count - activeOfType;
                if (canUse <= 0) continue;

                var idleSC = _cachedSpacecraft
                    .Where(sc => sc != null && sc.spacecraftType != null
                        && !sc.spacecraftType.MagneticCatapult
                        && sc.CurrentlyOnThisObject == providerOI
                        && sc.CurrentPhase == Spacecraft.EPhase.None
                        && Data.LogisticsNetwork.QuotaMatches(quota, sc.spacecraftType.ID, sc.spacecraftType.NameRocketType ?? "SC")
                        && cm.GetCycleMission(sc) == null
                        && CanSolarShipReach(sc, requester, player)
                        && HasFuelForReturn(sc, requester, player))
                    .Take(canUse)
                    .OrderByDescending(sc => sc.spacecraftType.GetCargoCapacity(player))
                    .ToList();

                if (idleSC.Count == 0)
                {
                    Log($"[TCD] {reqName}/{rdName}: SC quota type={quota.typeName} quota={quota.count} active={activeOfType} canUse={canUse} — NO idle SC at {provName}");
                    continue;
                }

                var bestSingle = idleSC
                    .Where(sc => sc.spacecraftType.GetCargoCapacity(player) >= targetAmount)
                    .OrderBy(sc => sc.spacecraftType.GetCargoCapacity(player))
                    .FirstOrDefault();

                List<Spacecraft> selectedShips;
                double totalCapacity;

                if (bestSingle != null)
                {
                    selectedShips = new List<Spacecraft> { bestSingle };
                    totalCapacity = bestSingle.spacecraftType.GetCargoCapacity(player);
                }
                else
                {
                    selectedShips = new List<Spacecraft>();
                    totalCapacity = 0;
                    foreach (var sc in idleSC)
                    {
                        selectedShips.Add(sc);
                        totalCapacity += sc.spacecraftType.GetCargoCapacity(player);
                        if (totalCapacity >= targetAmount) break;
                    }
                }

                double actualAmount = Math.Min(targetAmount, totalCapacity);
                Log($"[TCD] {reqName}/{rdName}: SC delivery via {provName} type={quota.typeName} ships={selectedShips.Count} targetAmount={targetAmount} actual={actualAmount}");
                SetupCycleMission(req, selectedShips, rd, actualAmount, requester, providerOI);
                scActive.TryGetValue(quota.typeName, out var cur);
                scActive[quota.typeName] = cur + selectedShips.Count;
                remaining -= actualAmount;
                delivered = true;
                if (remaining <= 0) return;
                break;
            }

            if (delivered) continue;

            // LV delivery — enabled LV types only (quota > 0 means enabled)
            var enabledLVTypes = (provData.launchVehicleQuota ?? new List<Data.ShipQuotaEntry>())
                .Where(q => q.count > 0)
                .Select(q => q.typeName)
                .ToHashSet();

            if (enabledLVTypes.Count == 0)
            {
                Log($"[TCD] {reqName}/{rdName}: SKIP {provName} — no enabled LV types");
                continue;
            }

            var allLVAtProvider = _cachedLaunchVehicles
                .Where(lv => lv != null && lv.launchVehicleType != null
                    && lv.objectInfo == providerOI
                    && lv.IsReadyToLaunchReusable()
                    && enabledLVTypes.Contains(Data.LogisticsNetwork.TypeKey(lv.launchVehicleType.ID, lv.launchVehicleType.Name ?? "LV")))
                .ToList();

            Log($"[TCD] {reqName}/{rdName}: {allLVAtProvider.Count} LVs at {provName}, usedLvIds count={usedLvIds.Count}, content=[{string.Join(",", usedLvIds)}]");

            var availableLV = allLVAtProvider
                .Where(lv => !usedLvIds.Contains(lv.GetHashCode()))
                .ToList();

            if (availableLV.Count == 0)
            {
                Log($"[TCD] {reqName}/{rdName}: NO ready LV at {provName} (filtered by usedLvIds)");
                Log($"[TCD] {reqName}/{rdName}: All LV at provider: {string.Join(", ", allLVAtProvider.Select(lv => $"{lv.launchVehicleType.Name} id={lv.GetHashCode()}"))}");
                continue;
            }

            Log($"[TCD] {reqName}/{rdName}: {availableLV.Count} LVs available at {provName}");

            // Best-fit LV selection (same two-phase logic as SC)
            var sortedLV = availableLV
                .OrderByDescending(lv => lv.launchVehicleType.MaxPayloadOnThisObject(providerOI, player))
                .ToList();

            var bestLV = sortedLV
                .Where(lv => lv.launchVehicleType.MaxPayloadOnThisObject(providerOI, player) >= targetAmount)
                .OrderBy(lv => lv.launchVehicleType.MaxPayloadOnThisObject(providerOI, player))
                .FirstOrDefault() ?? sortedLV[0];

            var lvType = bestLV.launchVehicleType;
            Log($"[TCD] {reqName}/{rdName}: selected LV type={lvType.Name} payload={lvType.MaxPayloadOnThisObject(providerOI, player)} FakeForFacility={lvType.FakeForFacility}");
            // Only add to usedLvIds for regular LV (non-catapult) - catapult uses usedCatapultIds instead

            // Check if target is the low orbit of the provider (surface -> orbit case)
            var targetIsOrbitOfProvider = providerOI != null
                && providerOI.LowOrbitCustom != null
                && providerOI.LowOrbitCustom.GetObjectInfo() == requester;

            Spacecraft scOnOrbit = null;
            ObjectInfo lvA = providerOI;
            var providerOrbit = providerOI.LowOrbitCustom?.GetObjectInfo();

            // One-shot delivery via magnetic catapult (no cyclical binding).
            // Catapult stays at facility, launches payload via LV, mission completes once.
            if (bestLV.launchVehicleType.FakeForFacility)
            {
                Log($"[TCD] {reqName}/{rdName}: attempting catapult delivery, usedCatapultIds=[{string.Join(",", usedCatapultIds)}]");
                var magSc = GetMagneticCatapultFromFacility(providerOI, player, usedCatapultIds);
                if (magSc != null)
                {
                    double lvCapacity = bestLV.launchVehicleType.MaxPayloadOnThisObject(providerOI, player);
                    double magCapacity = magSc.spacecraftType?.GetCargoCapacity(player) ?? lvCapacity;
                    double lvActualAmount = Math.Min(targetAmount, Math.Min(lvCapacity, magCapacity));
                    Log($"[TCD] {reqName}/{rdName}: CATAPULT magSc={magSc.spacecraftType?.NameRocketType} id={magSc.GetHashCode()} lvCapacity={lvCapacity} magCapacity={magCapacity} lvActualAmount={lvActualAmount}");

                    CreateOneShotCatapultMission(req, magSc, rd, lvActualAmount, requester, providerOI, bestLV);
                    usedCatapultIds.Add(magSc.GetHashCode());
                    _busyCatapultIds.Add(magSc.GetHashCode());
                    var lvKey = Data.LogisticsNetwork.TypeKey(lvType.ID, lvType.Name ?? "LV");
                    lvActive.TryGetValue(lvKey, out var lvCur);
                    lvActive[lvKey] = lvCur + 1;
                    remaining -= lvActualAmount;
                    if (remaining <= 0) return;
                }
                else
                {
                    Log($"[TCD] {reqName}/{rdName}: catapult FAILED — GetMagneticCatapultFromFacility returned null at {provName}");
                }
            }
            else if (targetIsOrbitOfProvider)
            {
                Log($"[TCD] {reqName}/{rdName}: target IS orbit of provider {provName}");
                usedLvIds.Add(bestLV.GetHashCode());
                scOnOrbit = MonoBehaviourSingleton<ShipManager>.Instance?.GetLowOrbitContainer(player);
                lvA = providerOI;
            }
            else if (providerOrbit != null)
            {
                Log($"[TCD] {reqName}/{rdName}: looking for orbit SC at {providerOrbit.ObjectName}");
                usedLvIds.Add(bestLV.GetHashCode());
                scOnOrbit = _cachedSpacecraft
                    .Where(sc => sc != null && sc.spacecraftType != null
                        && sc.CurrentlyOnThisObject == providerOrbit
                        && sc.CurrentPhase == Spacecraft.EPhase.None
                        && !sc.spacecraftType.LowOrbitContainer
                        && cm.GetCycleMission(sc) == null
                        && CanSolarShipReach(sc, requester, player)
                        && HasFuelForReturn(sc, requester, player))
                    .FirstOrDefault();
                lvA = providerOrbit;
            }
            else
            {
                Log($"[TCD] {reqName}/{rdName}: no orbit available for {provName} — skip silently");
            }

            if (scOnOrbit != null)
            {
                double lvCapacity = bestLV.launchVehicleType.MaxPayloadOnThisObject(providerOI, player);
                double lvActualAmount = Math.Min(targetAmount, lvCapacity);
                Log($"[TCD] {reqName}/{rdName}: LV+SC delivery scOnOrbit={scOnOrbit.spacecraftType?.NameRocketType} lvCapacity={lvCapacity} actual={lvActualAmount}");
                SetupCycleMission(req, scOnOrbit, rd, lvActualAmount, requester, lvA, lvType);
                var lvKey = Data.LogisticsNetwork.TypeKey(lvType.ID, lvType.Name ?? "LV");
                lvActive.TryGetValue(lvKey, out var lvCur);
                lvActive[lvKey] = lvCur + 1;
                remaining -= lvActualAmount;
                if (remaining <= 0) return;
            }
        }
    }

    private static void SetupCycleMission(Data.LogisticsRequest req, List<Spacecraft> scs,
        ResourceDefinition rd, double amount, ObjectInfo requesterOI, ObjectInfo providerOI,
        LaunchVehicleType lvTypeA = null)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance.Player;
        var firstSc = scs[0];
        if (firstSc == null) return;

        var realProvider = firstSc.CurrentlyOnThisObject;
        if (realProvider == null) return;

        Log($"[SCM] SC mission: {realProvider.ObjectName}->{requesterOI.ObjectName} rd={rd?.Name} amount={amount} ships={scs.Count} lvType={(lvTypeA?.Name ?? "none")}");

        var scList = scs.Select(s => s as ISpacecraftInfo).ToList();
        var cargoToB = new CargoAll();
        var item = new Cargo(cargoToB) { resourceType = rd, cargoMass = amount, resourceTypeType = EResourceTypeType.resorces };
        cargoToB.listCargo.Add(item);

        var cmdData = new CycleMissionsDataData
        {
            A = realProvider, B = requesterOI, Company = player,
            CargoStart = ECargoStart.FlyWithWhatIsAvailable, CargoEnd = ECargoStart.FlyWithWhatIsAvailable,
            CargoAllStart = cargoToB, CargoAllEnd = new CargoAll(),
            LvTypeA = lvTypeA, LvTypeB = null, TransferType = ETransferType.Optimal,
            Ends = EEnds.ThisManyTimes, EndsObjectThisManyTimes = 1, ListSC = scList
        };
        var cmd = new CycleMissionsData(cmdData);
        MonoBehaviourSingleton<CycleMissionManager>.Instance.AddCycleMission(firstSc, cmd, scList);

        req.status = Data.LogisticsRequestStatus.InProgress;

        cmd.customNameFromPlanMission = $"[LOGI] {realProvider.ObjectName} → {requesterOI.ObjectName}";

        var ctrl = firstSc.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
        if (ctrl == null)
            ctrl = firstSc.gameObject.AddComponent<SpaceCraftCyclicalMissionController>();
        ctrl.CycleMissionPlanFlyWas = false;
        ctrl.SetSC(firstSc);
        ctrl.TryPlanCycleMission();
    }

    private static void SetupCycleMission(Data.LogisticsRequest req, Spacecraft container,
        ResourceDefinition rd, double amount, ObjectInfo requesterOI, ObjectInfo providerOI,
        LaunchVehicleType lvTypeA)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance.Player;
        if (container == null || player == null) return;

        var realProvider = providerOI;
        if (realProvider == null) return;

        var scList = new List<ISpacecraftInfo> { container as ISpacecraftInfo };
        var cargoToB = new CargoAll();
        var item = new Cargo(cargoToB) { resourceType = rd, cargoMass = amount, resourceTypeType = EResourceTypeType.resorces };
        cargoToB.listCargo.Add(item);

        var cmdData = new CycleMissionsDataData
        {
            A = realProvider, B = requesterOI, Company = player,
            CargoStart = ECargoStart.FlyWithWhatIsAvailable, CargoEnd = ECargoStart.FlyWithWhatIsAvailable,
            CargoAllStart = cargoToB, CargoAllEnd = new CargoAll(),
            LvTypeA = lvTypeA, LvTypeB = null, TransferType = ETransferType.Optimal,
            Ends = EEnds.ThisManyTimes, EndsObjectThisManyTimes = 1, ListSC = scList
        };
        var cmd = new CycleMissionsData(cmdData);
        MonoBehaviourSingleton<CycleMissionManager>.Instance.AddCycleMission(container, cmd, scList);

        req.status = Data.LogisticsRequestStatus.InProgress;

        cmd.customNameFromPlanMission = $"[LOGI] {realProvider.ObjectName} → {requesterOI.ObjectName}";

        var ctrl = container.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
        if (ctrl == null)
            ctrl = container.gameObject.AddComponent<SpaceCraftCyclicalMissionController>();
        ctrl.CycleMissionPlanFlyWas = false;
        ctrl.SetSC(container);
        ctrl.TryPlanCycleMission(loadLimit2: amount);
    }

    private static void CreateOneShotCatapultMission(Data.LogisticsRequest req, Spacecraft catapult,
        ResourceDefinition rd, double amount, ObjectInfo requesterOI, ObjectInfo providerOI,
        LaunchVehicle lv)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance.Player;
        if (catapult == null || player == null)
        {
            LogWarning($"[OCM] ABORT: catapult={(catapult!=null)}, player={(player!=null)}");
            return;
        }

        if (req.status == Data.LogisticsRequestStatus.InProgress)
        {
            LogWarning($"[OCM] ABORT: req already InProgress (prevents duplicate)");
            return;
        }

        Log($"[OCM] creating one-shot mission: {providerOI.ObjectName}->{requesterOI.ObjectName} rd={rd?.Name} amount={amount} lv={lv?.launchVehicleType?.Name} catapultType={catapult?.spacecraftType?.NameRocketType}");

        // Dedup: check if in-transit cargo already covers the request
        {
            var mimDedup = MonoBehaviourSingleton<MissionInfoManager>.Instance;
            double alreadyInTransit = 0;
            int totalLogiMissions = 0;
            int matchedTargetMissions = 0;
            int matchedCargoMissions = 0;
            if (mimDedup != null)
            {
                foreach (var mi in mimDedup.ListMissionInfo)
                {
                    if (mi.complete || mi.cancel) continue;
                    if (mi.company != player) continue;
                    if (!mi.missionName.StartsWith("[LOGI]")) continue;
                    totalLogiMissions++;
                    if (mi.target != requesterOI) continue;
                    matchedTargetMissions++;
                    if (mi.cargoAll?.listCargo == null) continue;
                    bool foundCargo = false;
                    foreach (var c in mi.cargoAll.listCargo)
                    {
                        if (c.resourceType == rd)
                        {
                            alreadyInTransit += c.cargoMass;
                            foundCargo = true;
                        }
                    }
                    if (foundCargo) matchedCargoMissions++;
                }
            }
            Log($"[OCM] dedup: totalLOGI={totalLogiMissions} matchedTarget={matchedTargetMissions} matchedCargo={matchedCargoMissions} inTransit={alreadyInTransit:F2} requesterHash={requesterOI?.GetHashCode()}");
            double alreadyThere = requesterOI.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
            double totalCovered = alreadyThere + alreadyInTransit;
            if (totalCovered >= req.requestedAmount)
            {
                Log($"[OCM] SKIP — totalCovered={totalCovered:F2} >= requested={req.requestedAmount:F2} (onSite={alreadyThere:F2} inTransit={alreadyInTransit:F2})");
                req.status = Data.LogisticsRequestStatus.Satisfied;
                return;
            }
            Log($"[OCM] totalCovered={totalCovered:F2} < requested={req.requestedAmount:F2} — proceeding with mission");
        }
        var scList = new List<ISpacecraftInfo> { catapult as ISpacecraftInfo };
        var lvList = new List<ILaunchVehicleInfo> { lv };

        var cargoToB = new CargoAll();
        var item = new Cargo(cargoToB) { resourceType = rd, cargoMass = amount, resourceTypeType = EResourceTypeType.resorces };
        cargoToB.listCargo.Add(item);

        var gm = MonoBehaviourSingleton<GameManager>.Instance;

        var ppm = new PMMissionParameter();
        ppm.ForCyclicalMission = true;
        ppm.ReduceFuelToMinimum = true;
        ppm.SetMissionOrigin(MissionInfo.EMissionCreator.Manual);
        ppm.TryFastAsPossible = true;
        ppm.SetCompany(player);
        ppm.SetTabDestination(providerOI, requesterOI);
        ppm.SetTabSC(scList, 1);
        ppm.ChangeMissionName($"[LOGI] {providerOI.ObjectName} → {requesterOI.ObjectName}", _manualChangeName: true);
        ppm.SetTabCargo(cargoToB);
        ppm.SetTabLV(lvList, 1);
        ppm.ChangeStage(PlanMissionWindow.EStageWindow.Schedule);
        ppm.TrajectoryColor = Color.blue;

        Log($"[OCM] PMMissionParameter ready: player={player.ID} start={providerOI?.ObjectName} target={requesterOI?.ObjectName} cargo={rd?.Name}={amount} SC={catapult?.spacecraftType?.NameRocketType} LV={lv?.launchVehicleType?.Name} ForCyclicalMission={ppm.ForCyclicalMission}");

        var checkResult = ppm.CheckCanPlanMission();
        Log($"[OCM] CheckCanPlanMission: result={checkResult.planMissionResult} fuelNeed={checkResult.allFuelNeed} cost={checkResult.allCostDollars} start={checkResult.dateStart} end={checkResult.dateEnd}");
        if (checkResult.planMissionResult != PMMissionParameter.EPlanMissionResult.AllOk)
            LogWarning($"[OCM] CheckCanPlanMission NOT AllOk: {checkResult.planMissionResult} — forcing PlanFlyCode anyway");

        gm.PlanFlyCode(ppm, silenceModeOn: true);

        Log($"[OCM] PlanFlyCode called, waiting for async porkchop...");
        req.status = Data.LogisticsRequestStatus.InProgress;
    }

    private static void CleanupStuckMissions(Company player, CycleMissionManager cm)
    {
        var mim = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        var tc = MonoBehaviourSingleton<TimeController>.Instance;
        if (mim == null || tc == null) return;

        var currentTime = tc.CurrentTime;
        var logiStuck = 0;

        foreach (var mi in mim.ListMissionInfo)
        {
            if (mi.complete || mi.cancel) continue;
            if (!mi.fromCyclicalMission && !mi.missionName.StartsWith("[LOGI]")) continue;

            var sc = mi.spacecraftInfo2 as Spacecraft;
            if (sc == null) continue;

            bool hasPastDate = mi.DateLaunch != default && mi.DateLaunch <= currentTime;
            bool hasNoDate = mi.DateLaunch == default;
            if (!hasPastDate && !hasNoDate) continue;

            if (hasPastDate && sc.CurrentPhase != Spacecraft.EPhase.None && sc.CurrentPhase != Spacecraft.EPhase.PlanedMission) continue;

            // For one-shot LOGI missions: if the SC is idle but cargo hasn't arrived,
            // the payload is in flight — don't cancel.
            if (!mi.fromCyclicalMission && hasPastDate && sc.CurrentPhase == Spacecraft.EPhase.None)
            {
                bool cargoNeeded = false;
                if (mi.target != null && mi.cargoAll?.listCargo != null)
                {
                    var oid = mi.target.GetObjectInfoData(mi.company);
                    if (oid != null)
                    {
                        foreach (var c in mi.cargoAll.listCargo)
                        {
                            if (c.resourceType != null && oid.CheckResources(c.resourceType) < c.cargoMass)
                                { cargoNeeded = true; break; }
                        }
                    }
                }
                if (cargoNeeded)
                {
                    Log($"[CSM] SKIP cancel — one-shot LOGI mission \"{mi.missionName}\" is in flight (cargo not yet delivered)");
                    continue;
                }
            }

            Log($"[CSM] stuck mission: \"{mi.missionName}\" target={mi.target?.ObjectName} cyclical={mi.fromCyclicalMission} dateLaunch={mi.DateLaunch} current={currentTime} phase={sc.CurrentPhase}");

            if (mi.fromCyclicalMission)
            {
                var cmd = cm.GetCycleMission(sc);
                if (cmd == null || !cmd.customNameFromPlanMission.StartsWith("[LOGI]")) continue;

                cm.RemoveCycleMission(sc);
                ResetCycleRequests(cmd);
                logiStuck++;
                Log($"[CSM] removed cyclical cycle \"{cmd.customNameFromPlanMission}\"");
            }
            else
            {
                sc.CancelMission(mi);
                Log($"[CSM] cancelled one-shot mission \"{mi.missionName}\"");

                var requester = mi.target;
                var firstCargo = mi.cargoAll?.listCargo?.FirstOrDefault();
                if (requester != null && firstCargo != null)
                {
                    var reqData = Data.LogisticsNetwork.Get(requester);
                    if (reqData != null)
                    {
                        foreach (var req in reqData.requests)
                        {
                            if (req.ResourceDefinition == firstCargo.resourceType && req.status == Data.LogisticsRequestStatus.InProgress)
                            {
                                req.status = Data.LogisticsRequestStatus.Pending;
                                req.statusNote = null;
                                Log($"[CSM] reset request {firstCargo.resourceType?.Name} on {requester.ObjectName} to Pending");
                            }
                        }
                    }
                }

                logiStuck++;
            }
        }

        if (logiStuck > 0)
            Log($"[CSM] total stuck cleaned: {logiStuck}");
    }

    private static void ResetCycleRequests(CycleMissionsData cmd)
    {
        var requester = cmd.B;
        if (requester == null) return;
        var reqData = Data.LogisticsNetwork.Get(requester);
        if (reqData == null || cmd.cargoAllStart?.Tab == null) return;
        foreach (var res in cmd.cargoAllStart.Tab)
        {
            foreach (var req in reqData.requests)
            {
                if (req.ResourceDefinition == res && req.status == Data.LogisticsRequestStatus.InProgress)
                    req.status = Data.LogisticsRequestStatus.Pending;
            }
        }
    }
}
