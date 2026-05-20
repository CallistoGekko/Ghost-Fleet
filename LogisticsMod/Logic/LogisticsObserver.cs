using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomUpdate;
using Data.ScriptableObject;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.UI.Windows.Elements.PlanMissionElements;
using Game.VisualizationScripts;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;

namespace LogisticsMod.Logic;

public static class LogisticsObserver
{
    private static StreamWriter _logWriter;
    private static int _logSession;
    private const double PrePlanReturnFuelFractionOfTank = 0.1;
    private const double MaxReturnFuelCargoDisplacementFraction = 0.25;
    private const double ReservePropellantMultiplier = 1.1;
    private static bool VerboseLogging => LogisticsMod.Plugin.VerboseLogging?.Value ?? false;
    public static bool VerboseLoggingEnabled => VerboseLogging;
    private static double CyclePlanningGraceDays => LogisticsMod.Plugin.CyclePlanningGraceDays?.Value ?? 3.0;
    private static double EffectiveCyclePlanningGraceDays => Math.Max(CyclePlanningGraceDays, 30.0);
    private static double BlockedMissionRetryCooldownDays => Math.Max(30.0, LogisticsMod.Plugin.BlockedMissionRetryCooldownDays?.Value ?? 30.0);
    private const double ReturnCycleBlockedCooldownDays = 30.0;
    private const double ReturnCycleEscalatedCooldownDays = 180.0;
    private const int ReturnCycleEscalationFailureThreshold = 3;
    private const int MaxReturnFuelProbeCacheEntries = 256;
    private static readonly TimeSpan ReturnCycleWallClockThrottle = TimeSpan.FromSeconds(10);
    private static readonly Dictionary<CycleMissionsData, DateTime> _cycleCreatedAt = new Dictionary<CycleMissionsData, DateTime>();
    private static readonly Dictionary<CycleMissionsData, int> _cyclePlanningFailures = new Dictionary<CycleMissionsData, int>();
    private const int MaxCyclePlanningFailures = 3;
    private static readonly Dictionary<string, ReturnFuelProbeState> _returnFuelProbeCache = new Dictionary<string, ReturnFuelProbeState>();
    private static readonly Queue<string> _returnFuelProbeCacheOrder = new Queue<string>();
    private static readonly Dictionary<string, DateTime> _routePlanningLocks = new Dictionary<string, DateTime>();
    private static readonly Dictionary<string, double> _committedStock = new Dictionary<string, double>();
    private static readonly Dictionary<int, string> _cycleNameByShipId = new Dictionary<int, string>();
    private static readonly Dictionary<string, string> _cycleNameByRouteKey = new Dictionary<string, string>();
    private static Spacecraft[] _cachedSpacecraft;
    private static float _cachedSpacecraftTime;
    private static DateTime _committedStockWallClock;
    private static DateTime _nextCompletedTrajectoryScan;
    private static DateTime _nextOrphanTrajectoryScan;
    private const double CommittedStockWindowSeconds = 1.0;
    private const double CompletedTrajectoryScanDays = 30.0;
    private const double OrphanTrajectoryScanDays = 180.0;
    private const double RequestPlanThrottleDays = 3.0;
    private const int MaxPrecalculateRouteCacheEntries = 128;
    private static readonly Dictionary<string, RequestPlanThrottleState> _requestPlanThrottle = new Dictionary<string, RequestPlanThrottleState>();
    private static readonly Dictionary<string, PMMissionParameter.PrecalculateDataToShortFly> _precalculateRouteCache = new Dictionary<string, PMMissionParameter.PrecalculateDataToShortFly>();
    private static readonly Queue<string> _precalculateRouteCacheOrder = new Queue<string>();

    private sealed class PlannerLaunchVehicleInfo : ILaunchVehicleInfo
    {
        private readonly LaunchVehicleType _type;
        private readonly ObjectInfo _objectInfo;
        private readonly Company _company;

        public PlannerLaunchVehicleInfo(LaunchVehicleType type, ObjectInfo objectInfo, Company company)
        {
            _type = type;
            _objectInfo = objectInfo;
            _company = company;
        }

        public LaunchVehicleType GetLaunchVehicleType() => _type;
        public ObjectInfo GetActualPosition() => _objectInfo;
        public Company GetCompany() => _company;
        public ObjectInfo GetObjectInfo() => _objectInfo;
        public bool CheckMaximumPayload(CargoAll cargo, ISpacecraftInfo spacecraft) => _type != null && _type.CheckMaximumPayload(cargo, spacecraft);
        public bool CheckMaximumPayloadFuel(float fuelNeed, ISpacecraftInfo spacecraft) => _type != null && _type.CheckMaximumPayloadFuel(fuelNeed, spacecraft);
    }

    private sealed class PlannerSpacecraftInfo : ISpacecraftInfo
    {
        private readonly SpacecraftType _type;
        private readonly Company _company;
        private readonly ObjectInfo _position;
        private readonly string _name;

        public PlannerSpacecraftInfo(Spacecraft source, ObjectInfo position)
        {
            _type = source?.GetTypeSpaceCraft();
            _company = source?.GetCompany();
            _position = position;
            _name = source?.GetSpacecraftName() ?? _type?.NameRocketType ?? "Probe spacecraft";
        }

        public string GetSpacecraftName() => _name;
        public ObjectInfo GetActualPosition() => _position;
        public MissionInfo GetMissionInfo() => null;
        public Company GetCompany() => _company;
        public float GetMass() => _type?.Mass ?? 0f;
        public SpacecraftType GetTypeSpaceCraft() => _type;
        public int GetLifeSupportCurrentWhenFly(float? lerpTime = null) => 0;
        public ObjectInfo GetObjectInfoPlan() => _position;
    }

    private sealed class ReturnFuelProbeState
    {
        public bool Pending;
        public bool Complete;
        public DateTime RequestedAt;
        public DateTime CompletedAt;
        public ResourceDefinition FuelType;
        public double FuelNeed;
        public double MinFuelCost;
        public double AllFuelNeed;
        public double LeftOverFuel;
        public double RequiredReserve;
        public PMMissionParameter.EPlanMissionResult Result;
        public string FailureReason;
    }

    private sealed class RequestPlanThrottleState
    {
        public DateTime NextEvaluation;
        public string Signature;
    }

    private static readonly Dictionary<string, DateTime> _pendingPlanningDeliveries = new Dictionary<string, DateTime>();
    private static readonly Dictionary<string, BlockedRetryState> _blockedPlanningRetries = new Dictionary<string, BlockedRetryState>();
    private static readonly Dictionary<int, ReturnHomeState> _returnHomeByShipId = new Dictionary<int, ReturnHomeState>();

    private sealed class ReturnHomeState
    {
        public ObjectInfo Home;
        public ObjectInfo Destination;
        public ResourceDefinition Resource;
        public bool HasLeftHome;
        public string LastBlockedReason;
        public string LastBlockedStatusNote;
        public DateTime LastBlockedDate = DateTime.MinValue;
        public string PendingPlanKey;
        public PMMissionParameter PendingPlanParameter;
        public GameManager.PlanFlyCodeResult PendingPlanResult;
        public string ResolvedPlanKey;
        public bool HasResolvedPlanResult;
        public ResourceDefinition ResolvedFuelType;
        public double ResolvedFuelNeed;
        public DateTime ResolvedPlanDate = DateTime.MinValue;
        public DateTime ReturnRetryAfter = DateTime.MinValue;
        public DateTime ReturnRetryWallClockAfterUtc = DateTime.MinValue;
        public int ConsecutiveReturnCycleFailures;
    }

    private sealed class BlockedRetryState
    {
        public DateTime RetryAfter;
        public string Reason;
    }

    private sealed class PlannerSnapshot
    {
        public List<ObjectInfo> Objects = new List<ObjectInfo>();
        public List<CycleMissionsData> Cycles = new List<CycleMissionsData>();
        public List<MissionInfo> Missions = new List<MissionInfo>();
        public List<Spacecraft> Ships = new List<Spacecraft>();
        public Dictionary<string, int> ScActive = new Dictionary<string, int>();
        public Dictionary<string, int> LvActive = new Dictionary<string, int>();
        public HashSet<int> CommittedShipIds = new HashSet<int>();
        public Dictionary<int, List<LaunchSupportOption>> LaunchSupportByObjectId = new Dictionary<int, List<LaunchSupportOption>>();
    }

    private enum RouteKind
    {
        DirectSpacecraft,
        DirectSurfaceLaunch,
        StageSourceSurfaceToOrbit
    }

    private sealed class RouteCandidate
    {
        public RouteKind Kind;
        public ObjectInfo Provider;
        public ObjectInfo EffectiveSource;
        public ObjectInfo StageOrbit;
        public Spacecraft Spacecraft;
        public Spacecraft StageCarrier;
        public Spacecraft FinalCarrier;
        public LaunchVehicleType LaunchVehicleType;
        public double Amount;
        public double Available;
        public int Tier;
        public int HopCount;
        public bool UsesLV;
        public string Label;
        public string ScoreBreakdown;
    }

    private sealed class PlannerBlocker
    {
        public int Tier = int.MaxValue;
        public int Priority = int.MaxValue;
        public string Reason;
    }

    private sealed class LaunchSupportOption
    {
        public LaunchVehicle Vehicle;
        public LaunchVehicleType Type;
        public Facility Facility;
        public string Category;
        public string Label;
        public bool IsFacilityBacked;
        public int TierAdjustment;
    }

    public static void Log(string msg)
    {
        if (VerboseLogging)
            WriteLog("", msg);
    }

    public static void LogVerbose(string msg)
    {
        if (VerboseLogging)
            Log(msg);
    }

    public static void LogWarning(string msg)
    {
        if (!VerboseLogging)
            return;

        WriteLog("[WARN] ", msg);
        Debug.LogWarning("[LogisticsMod] " + msg);
    }

    public static void LogError(string msg)
    {
        WriteLog("[ERROR] ", msg);
        Debug.LogError("[LogisticsMod] " + msg);
    }

    public static void LogBepInEx(string msg)
    {
        if (VerboseLogging)
        {
            WriteLog("", msg);
            Debug.Log("[LogisticsMod] " + msg);
        }
    }

    private static PlannerSnapshot BuildPlannerSnapshot(Company player)
    {
        var snapshot = new PlannerSnapshot();
        if (player == null) return snapshot;

        snapshot.Objects = Data.LogisticsNetwork.GetAllObjects();

        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm != null)
            snapshot.Cycles = cm.GetAllCycleMission(player);

        var mm = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        if (mm?.ListMissionInfo != null)
            snapshot.Missions = mm.ListMissionInfo;

        snapshot.Ships = MonoBehaviourSingleton<ShipManager>.Instance?.ListAllSpaceShip
            ?? UnityEngine.Object.FindObjectsOfType<Spacecraft>().ToList();

        return snapshot;
    }

    public static void ApplyCachedPrecalculateData(PMMissionParameter pmp)
    {
        var key = BuildPrecalculateRouteKey(pmp);
        if (key == null) return;
        if (!_precalculateRouteCache.TryGetValue(key, out var cached)) return;

        pmp.SetPrecalculateDataToShortFly(ClonePrecalculateData(cached));
        LogVerbose($"PRECACHE apply: key={key} moonCase={cached.moonCase}");
    }

    public static void CachePrecalculateData(PMMissionParameter pmp, string context)
    {
        if (pmp == null || !pmp.MoonCase)
            return;

        var key = BuildPrecalculateRouteKey(pmp);
        if (key == null) return;

        var data = new PMMissionParameter.PrecalculateDataToShortFly
        {
            moonCase = pmp.MoonCase,
            moonCaseCostMax = pmp.MoonCaseCostMax,
            moonCaseCostMin = pmp.MoonCaseCostMin,
            minDeltaVMoonCase = pmp.MinDeltaVMoonCase
        };

        if (!_precalculateRouteCache.ContainsKey(key))
            _precalculateRouteCacheOrder.Enqueue(key);
        _precalculateRouteCache[key] = data;

        while (_precalculateRouteCacheOrder.Count > MaxPrecalculateRouteCacheEntries)
        {
            var evict = _precalculateRouteCacheOrder.Dequeue();
            _precalculateRouteCache.Remove(evict);
        }

        LogVerbose($"PRECACHE store: context={context} key={key} minDV={data.minDeltaVMoonCase:0.#}");
    }

    private static PMMissionParameter.PrecalculateDataToShortFly ClonePrecalculateData(PMMissionParameter.PrecalculateDataToShortFly source)
    {
        if (source == null) return null;
        return new PMMissionParameter.PrecalculateDataToShortFly
        {
            moonCase = source.moonCase,
            moonCaseCostMax = source.moonCaseCostMax,
            moonCaseCostMin = source.moonCaseCostMin,
            minDeltaVMoonCase = source.minDeltaVMoonCase
        };
    }

    private static string BuildPrecalculateRouteKey(PMMissionParameter pmp)
    {
        if (pmp == null || pmp.FlyCompany == null)
            return null;

        var source = pmp.Start;
        var target = pmp.Target;
        if (source == null || target == null)
            return null;

        var scType = pmp.SC?.GetTypeSpaceCraft();
        var lvType = pmp.LV?.GetLaunchVehicleType();
        var scKey = scType?.ID.ToString() ?? scType?.NameRocketType ?? "no-sc";
        var lvKey = lvType?.ID.ToString() ?? lvType?.Name ?? "no-lv";
        return $"{pmp.FlyCompany.ID}|{source.id}->{target.id}|{pmp.TransferTypeMoonCase}|fast={pmp.TryFastAsPossible}|sc={scKey}|lv={lvKey}";
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

    public static void ResetRuntimeState()
    {
        var cycleCount = _cycleCreatedAt.Count;
        var pendingCount = _pendingPlanningDeliveries.Count;
        var returnCount = _returnHomeByShipId.Count;
        var failCount = _cyclePlanningFailures.Count;
        var fuelProbeCount = _returnFuelProbeCache.Count;
        var routeLockCount = _routePlanningLocks.Count;
        var committedCount = _committedStock.Count;
        var throttleCount = _requestPlanThrottle.Count;
        var precalcCount = _precalculateRouteCache.Count;
        _cycleCreatedAt.Clear();
        _cyclePlanningFailures.Clear();
        _pendingPlanningDeliveries.Clear();
        _returnHomeByShipId.Clear();
        _returnFuelProbeCache.Clear();
        _returnFuelProbeCacheOrder.Clear();
        _routePlanningLocks.Clear();
        _committedStock.Clear();
        _requestPlanThrottle.Clear();
        _precalculateRouteCache.Clear();
        _precalculateRouteCacheOrder.Clear();
        _cycleNameByShipId.Clear();
        _cycleNameByRouteKey.Clear();
        _cachedSpacecraft = null;
        _nextCompletedTrajectoryScan = default;
        _nextOrphanTrajectoryScan = default;
        Log($"RESET runtime-state: cycles={cycleCount} pending={pendingCount} returns={returnCount} failures={failCount} fuelProbes={fuelProbeCount} routeLocks={routeLockCount} committed={committedCount} throttles={throttleCount} precalc={precalcCount}");
    }

    public static bool IsLogisticsMissionInfo(MissionInfo mi)
    {
        return mi?.missionName != null
            && mi.missionName.StartsWith("[LOGI", StringComparison.Ordinal);
    }

    public static void CleanupCompletedLogisticsMissionTrajectories(Company player = null)
    {
        CleanupCompletedLogisticsMissionTrajectories(player, null);
    }

    private static void CleanupCompletedLogisticsMissionTrajectories(Company player, PlannerSnapshot snapshot)
    {
        var mm = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        var missions = snapshot?.Missions ?? mm?.ListMissionInfo;
        if (missions == null) return;

        foreach (var mi in missions.ToList())
        {
            if (mi == null || !mi.complete || mi.cancel) continue;
            if (player != null && mi.company != player) continue;
            CleanupLogisticsMissionTrajectory(mi, "completed-scan");
        }
    }

    public static void CleanupLogisticsMissionTrajectory(MissionInfo mi, string reason)
    {
        if (!IsLogisticsMissionInfo(mi)) return;

        var trajectory = mi.trajectoryObject;
        if (trajectory == null) return;

        LogVerbose($"CLEANUP completed LOGI trajectory: mission={mi.id} name=\"{mi.missionName}\" reason={reason} arrive={mi.DateArrive:yyyy-MM-dd}");
        UnityEngine.Object.Destroy(trajectory.gameObject);
    }

    private static void CleanupOrphanLogisticsTrajectories(Company player, PlannerSnapshot snapshot)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (player == null || cm == null) return;

        var missionTrajectories = new HashSet<TrajectoryObject>();
        foreach (var mi in snapshot?.Missions ?? new List<MissionInfo>())
        {
            if (mi?.trajectoryObject != null)
                missionTrajectories.Add(mi.trajectoryObject);
        }

        var cycles = snapshot?.Cycles ?? cm.GetAllCycleMission(player);
        foreach (var trajectory in UnityEngine.Object.FindObjectsOfType<TrajectoryObject>())
        {
            if (trajectory == null || missionTrajectories.Contains(trajectory)) continue;
            var start = trajectory.StartObjectInfo;
            var target = trajectory.EndObjectInfo;
            if (start == null || target == null) continue;
            if (!MatchesActiveLogisticsCycle(cycles, start, target)) continue;

            LogWarning($"CLEANUP orphan LOGI trajectory: {start.ObjectName}->{target.ObjectName} launch={trajectory.StartDate:yyyy-MM-dd} arrive={trajectory.EndDate:yyyy-MM-dd}");
            UnityEngine.Object.Destroy(trajectory.gameObject);
        }
    }

    private static void CleanupStaleUnlaunchedLogisticsMissions(Company player, PlannerSnapshot snapshot)
    {
        var missions = snapshot?.Missions;
        if (player == null || missions == null) return;

        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        foreach (var mi in missions.ToList())
        {
            if (mi == null || mi.complete || mi.cancel) continue;
            if (mi.company != player || !IsLogisticsMissionInfo(mi)) continue;
            if (mi.DateLaunch == default || mi.DateLaunch.AddDays(1.0) > now) continue;

            var sc = mi.spacecraftInfo2 as Spacecraft;
            if (sc == null) continue;
            if (sc.CurrentPhase != Spacecraft.EPhase.None && sc.CurrentPhase != Spacecraft.EPhase.PlanedMission)
                continue;

            LogWarning($"CLEANUP stale unlaunched LOGI mission: mission={mi.id} name=\"{mi.missionName}\" ship={sc.GetSpacecraftName()} id={sc.ID} phase={sc.CurrentPhase} launch={mi.DateLaunch:yyyy-MM-dd} now={now:yyyy-MM-dd}");
            mi.cancelFromRocketLauncher = true;
            sc.CancelMission(mi);
            mi.cancelFromRocketLauncher = false;
        }
    }

    private static bool MatchesActiveLogisticsCycle(IEnumerable<CycleMissionsData> cycles, ObjectInfo start, ObjectInfo target)
    {
        if (cycles == null || start == null || target == null) return false;
        foreach (var cmd in cycles)
        {
            if (!IsLogisticsMission(cmd) || cmd.CheckComplete()) continue;
            if ((cmd.A == start && cmd.B == target) || (cmd.B == start && cmd.A == target))
                return true;
        }
        return false;
    }

    private static void HandOffCycleToStockPlanner(Spacecraft sc, CycleMissionsData cmd, string context, string routeLockKey = null)
    {
        if (sc == null || cmd == null) return;

        var ctrl = sc.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
        if (ctrl == null)
            ctrl = sc.gameObject.AddComponent<SpaceCraftCyclicalMissionController>();
        ctrl.CycleMissionPlanFlyWas = false;
        ctrl.SetSC(sc);
        ctrl.TryPlanCycleMission(null, _ =>
        {
            ReleaseRoutePlanningLock(routeLockKey, $"{context}-callback");
            if (!IsLogisticsMission(cmd))
                return;

            var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
            if (cm == null)
                return;

            LogVerbose($"CYCLE one-shot-complete: context={context} route={cmd.A?.ObjectName}->{cmd.B?.ObjectName} ship={sc.GetSpacecraftName()} id={sc.ID}");
            foreach (var tabRes in cmd.cargoAllStart?.Tab ?? Array.Empty<ResourceDefinition>())
                ClearPendingPlanningDelivery(cmd.B, tabRes);
            _cycleCreatedAt.Remove(cmd);
            _cyclePlanningFailures.Remove(cmd);
            RemoveLogisticsCycle(cm, cmd);
        });

        if (!cmd.wasSetPMParameterForCodeJobSystem && !ctrl.CycleMissionPlanFlyWas)
        {
            ReleaseRoutePlanningLock(routeLockKey, $"{context}-not-started");
            if (!string.IsNullOrEmpty(routeLockKey) && IsLogisticsDeliveryMission(cmd))
                RemoveUnstartedOneShotCycle(cmd, context);
        }
    }

    private static void RemoveUnstartedOneShotCycle(CycleMissionsData cmd, string context)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cmd == null || cm == null) return;

        foreach (var tabRes in cmd.cargoAllStart?.Tab ?? Array.Empty<ResourceDefinition>())
            ClearPendingPlanningDelivery(cmd.B, tabRes);

        _cycleCreatedAt.Remove(cmd);
        _cyclePlanningFailures.Remove(cmd);
        LogWarning($"CYCLE one-shot-not-started: context={context} route={cmd.A?.ObjectName}->{cmd.B?.ObjectName} name={cmd.customNameFromPlanMission}; removed instead of waiting for partial scraps");
        RemoveLogisticsCycle(cm, cmd);
    }

    private static void ClearRelayState(Data.LogisticsRequest req)
    {
        if (req == null) return;
        req.relayStage = Data.RelayStage.None;
        req.relaySourceObjectId = -1;
        req.relayOrbitObjectId = -1;
        req.relayFinalTargetObjectId = -1;
    }

    private static void SetRelayState(Data.LogisticsRequest req, Data.RelayStage stage,
        ObjectInfo source, ObjectInfo orbit, ObjectInfo finalTarget)
    {
        if (req == null) return;
        req.relayStage = stage;
        req.relaySourceObjectId = source?.id ?? -1;
        req.relayOrbitObjectId = orbit?.id ?? -1;
        req.relayFinalTargetObjectId = finalTarget?.id ?? -1;
    }

    private static ObjectInfo ResolveObject(int objectId)
    {
        if (objectId <= 0) return null;
        return MonoBehaviourSingleton<ObjectInfoManager>.Instance?.GetByID(objectId);
    }

    public static void OnDayChange(double days)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (player == null) return;
        var snapshot = BuildPlannerSnapshot(player);
        CountActiveLogisticsCycles(player, snapshot.Cycles, out var scActive, out var lvActive, out var committedShipIds);
        snapshot.ScActive = scActive;
        snapshot.LvActive = lvActive;
        snapshot.CommittedShipIds = committedShipIds;
        CleanupStaleUnlaunchedLogisticsMissions(player, snapshot);
        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        if (_nextCompletedTrajectoryScan == default || now >= _nextCompletedTrajectoryScan)
        {
            CleanupCompletedLogisticsMissionTrajectories(player, snapshot);
            _nextCompletedTrajectoryScan = now.AddDays(CompletedTrajectoryScanDays);
        }
        if (_nextOrphanTrajectoryScan == default || now >= _nextOrphanTrajectoryScan)
        {
            CleanupOrphanLogisticsTrajectories(player, snapshot);
            _nextOrphanTrajectoryScan = now.AddDays(OrphanTrajectoryScanDays);
        }

        TryReturnIdleLogisticsShips(player, snapshot);

        var networkResources = Data.LogisticsNetwork.GetNetworkResourcesSet(player, snapshot.Objects);

        foreach (var requesterOI in snapshot.Objects)
        {
            var reqData = Data.LogisticsNetwork.Get(requesterOI);
            if (reqData == null) continue;

            foreach (var req in reqData.requests)
            {
                var rd = req.ResourceDefinition;

                if (req.status == Data.LogisticsRequestStatus.Satisfied
                    || req.status == Data.LogisticsRequestStatus.Failed)
                {
                    if (rd != null)
                    {
                        var currentCount = requesterOI.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
                        if (currentCount < RequestMinimum(req))
                        {
                            Log($"REOPEN: {rd.ID} on {requesterOI?.ObjectName} stock={currentCount:0.#} minimum={RequestMinimum(req):0.#} target={RequestTarget(req):0.#}");
                            req.status = Data.LogisticsRequestStatus.Pending;
                            ClearRelayState(req);
                        }
                    }
                    if (req.status == Data.LogisticsRequestStatus.Satisfied
                        || req.status == Data.LogisticsRequestStatus.Failed)
                    {
                        var blockedSatisfiedReturnNote = rd != null
                            ? GetReturnBlockedStatusNote(requesterOI, rd, player, snapshot)
                            : null;
                        if (!string.IsNullOrEmpty(blockedSatisfiedReturnNote))
                        {
                            req.status = Data.LogisticsRequestStatus.InProgress;
                            req.statusNote = blockedSatisfiedReturnNote;
                            LogVerbose($"REQ keep-satisfied-return-blocked: target={requesterOI?.ObjectName} rd={rd?.ID} note={blockedSatisfiedReturnNote}");
                            continue;
                        }
                        if (rd != null)
                            CleanupLogisticsCyclesForRequest(requesterOI, rd, player, $"request-{req.status.ToString().ToLowerInvariant()}", snapshot);
                        req.statusNote = null;
                        continue;
                    }
                }

                if (req.status == Data.LogisticsRequestStatus.Pending)
                    req.statusNote = (rd != null && networkResources.Contains(rd)) ? null : LogisticsStrings.NoProviderInNetwork();
                else
                    req.statusNote = null;
                if (rd == null) continue;

                if (req.relayFinalTargetObjectId <= 0)
                    req.relayFinalTargetObjectId = requesterOI?.id ?? -1;

                var alreadyThere = requesterOI.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
                var requestTarget = RequestTarget(req);
                var requestMinimum = RequestMinimum(req);
                var blockedReturnNote = GetReturnBlockedStatusNote(requesterOI, rd, player, snapshot);
                LogVerbose($"REQ eval: target={requesterOI?.ObjectName} rd={rd.ID} fillTarget={requestTarget:0.#} minimum={requestMinimum:0.#} stock={alreadyThere:0.#} status={req.status}");
                if (alreadyThere >= requestTarget)
                {
                    if (!string.IsNullOrEmpty(blockedReturnNote))
                    {
                        req.status = Data.LogisticsRequestStatus.InProgress;
                        req.statusNote = blockedReturnNote;
                        LogVerbose($"REQ hold-fulfilled-return-blocked: target={requesterOI?.ObjectName} rd={rd.ID} note={blockedReturnNote}");
                        continue;
                    }
                    if (req.status != Data.LogisticsRequestStatus.Satisfied)
                        Log($"SATISFIED: {rd.ID} on {requesterOI?.ObjectName} stock={alreadyThere:0.#} target={requestTarget:0.#}");
                    req.status = Data.LogisticsRequestStatus.Satisfied;
                    CleanupLogisticsCyclesForRequest(requesterOI, rd, player, "request-fulfilled", snapshot);
                    continue;
                }

                if (HandleRelayProgress(req, requesterOI, rd, requestTarget, alreadyThere, player, snapshot))
                    continue;

                bool hasActiveDelivery = HasActiveCycleDelivering(requesterOI, rd, player, snapshot);
                if (alreadyThere >= requestMinimum && !hasActiveDelivery && string.IsNullOrEmpty(blockedReturnNote))
                {
                    req.status = Data.LogisticsRequestStatus.Satisfied;
                    CleanupLogisticsCyclesForRequest(requesterOI, rd, player, "above-minimum", snapshot);
                    LogVerbose($"REQ hold-above-minimum: target={requesterOI?.ObjectName} rd={rd.ID} stock={alreadyThere:0.#} minimum={requestMinimum:0.#} fillTarget={requestTarget:0.#}");
                    continue;
                }

                if (hasActiveDelivery)
                {
                    req.status = Data.LogisticsRequestStatus.InProgress;
                    if (IsTransientPlanningStatus(req.statusNote))
                        req.statusNote = null;
                    LogVerbose($"REQ active-cycle-present: target={requesterOI?.ObjectName} rd={rd.ID}; checking whether additional cargo is still needed");
                }

                if (!hasActiveDelivery && !string.IsNullOrEmpty(blockedReturnNote))
                {
                    req.status = Data.LogisticsRequestStatus.InProgress;
                    req.statusNote = blockedReturnNote;
                    LogVerbose($"REQ return-blocked-note-present: target={requesterOI?.ObjectName} rd={rd.ID} note={blockedReturnNote}; continuing outbound planning");
                }

                if (HasPendingPlanningDelivery(requesterOI, rd))
                {
                    req.status = Data.LogisticsRequestStatus.InProgress;
                    LogVerbose($"REQ wait-pending-plan: target={requesterOI?.ObjectName} rd={rd.ID}");
                    continue;
                }

                if (HasBlockedPlanningRetryCooldown(requesterOI, rd, out var cooldownStatus))
                {
                    req.status = hasActiveDelivery || !string.IsNullOrEmpty(blockedReturnNote)
                        ? Data.LogisticsRequestStatus.InProgress
                        : Data.LogisticsRequestStatus.Pending;
                    req.statusNote = !string.IsNullOrEmpty(blockedReturnNote)
                        ? $"{blockedReturnNote}; {cooldownStatus}"
                        : cooldownStatus;
                    continue;
                }

                var inFlight = GetInFlightDeliveryAmount(requesterOI, rd, player, snapshot);
                double remaining = requestTarget - alreadyThere - inFlight;
                LogVerbose($"REQ remaining: target={requesterOI?.ObjectName} rd={rd.ID} fillTarget={requestTarget:0.#} minimum={requestMinimum:0.#} stock={alreadyThere:0.#} inFlight={inFlight:0.#} remaining={remaining:0.#}");
                if (remaining <= 0)
                {
                    req.status = Data.LogisticsRequestStatus.InProgress;
                    if (!string.IsNullOrEmpty(blockedReturnNote))
                        req.statusNote = blockedReturnNote;
                    LogVerbose($"WAIT IN-FLIGHT: {rd.ID} on {requesterOI?.ObjectName} alreadyThere={alreadyThere:0.#} inFlight={inFlight:0.#} fillTarget={requestTarget:0.#}");
                    continue;
                }

                var planningSignature = BuildRequestPlanSignature(requesterOI, rd, requestTarget,
                    alreadyThere, inFlight, hasActiveDelivery, blockedReturnNote, snapshot);
                if (ShouldDeferRequestPlanning(requesterOI, rd, planningSignature, out var throttleStatus))
                {
                    req.status = hasActiveDelivery || !string.IsNullOrEmpty(blockedReturnNote)
                        ? Data.LogisticsRequestStatus.InProgress
                        : Data.LogisticsRequestStatus.Pending;
                    req.statusNote = !string.IsNullOrEmpty(blockedReturnNote)
                        ? $"{blockedReturnNote}; {throttleStatus}"
                        : throttleStatus;
                    continue;
                }

                req.status = hasActiveDelivery
                    ? Data.LogisticsRequestStatus.InProgress
                    : Data.LogisticsRequestStatus.Pending;
                var pendingReason = TryCreateDeliveries(req, requesterOI, rd, remaining, player, snapshot);
                if (string.IsNullOrEmpty(pendingReason))
                    ClearRequestPlanningThrottle(requesterOI, rd);
                else
                    MarkRequestPlanningEvaluated(requesterOI, rd, planningSignature);
                if ((req.status == Data.LogisticsRequestStatus.Pending || hasActiveDelivery) && !string.IsNullOrEmpty(pendingReason))
                    req.statusNote = !string.IsNullOrEmpty(blockedReturnNote)
                        ? $"{blockedReturnNote}; {pendingReason}"
                        : pendingReason;
                else if (!string.IsNullOrEmpty(blockedReturnNote) && req.status == Data.LogisticsRequestStatus.InProgress)
                    req.statusNote = blockedReturnNote;
            }
        }
    }

    private static bool HandleRelayProgress(Data.LogisticsRequest req, ObjectInfo requesterOI,
        ResourceDefinition rd, double requestTarget, double alreadyThere, Company player, PlannerSnapshot snapshot = null)
    {
        if (req == null || requesterOI == null || rd == null || player == null)
            return false;
        if (req.relayStage == Data.RelayStage.None)
            return false;

        var sourceOI = ResolveObject(req.relaySourceObjectId);
        var orbitOI = ResolveObject(req.relayOrbitObjectId);
        var finalTargetOI = ResolveObject(req.relayFinalTargetObjectId) ?? requesterOI;
        if (sourceOI == null || orbitOI == null || finalTargetOI == null)
        {
            ClearRelayState(req);
            return false;
        }

        if (req.relayStage == Data.RelayStage.WaitingForSourceOrbitStock)
        {
            if (HasActiveCycleDelivering(orbitOI, rd, player, snapshot) || HasPendingPlanningDelivery(orbitOI, rd))
            {
                req.status = Data.LogisticsRequestStatus.InProgress;
                req.statusNote = LogisticsStrings.StagingTo(orbitOI);
                return true;
            }

            var orbitStock = orbitOI.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
            if (orbitStock > 0)
            {
                req.relayStage = Data.RelayStage.WaitingForFinalLeg;
                req.status = Data.LogisticsRequestStatus.InProgress;
                req.statusNote = LogisticsStrings.StagedAt(orbitOI);
                Log($"RELAY staged-stock-ready: rd={rd.ID} source={sourceOI.ObjectName} orbit={orbitOI.ObjectName} target={finalTargetOI.ObjectName} stock={orbitStock:0.#}");
                return true;
            }

            ClearRelayState(req);
            return false;
        }

        var hasActiveFinalDelivery = HasActiveCycleDelivering(finalTargetOI, rd, player, snapshot);
        if (HasPendingPlanningDelivery(finalTargetOI, rd))
        {
            req.status = Data.LogisticsRequestStatus.InProgress;
            req.statusNote = LogisticsStrings.ShippingFrom(orbitOI);
            return true;
        }
        if (hasActiveFinalDelivery)
            LogVerbose($"RELAY final-leg-active: target={finalTargetOI.ObjectName} rd={rd.ID}; checking whether additional staged cargo is still needed");

        var committedFromOrbit = GetCommittedStock(orbitOI, rd);
        var rawStagedStock = orbitOI.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
        var stagedStock = rawStagedStock - committedFromOrbit;

        if (committedFromOrbit > 0 && stagedStock <= 0)
        {
            req.status = Data.LogisticsRequestStatus.InProgress;
            req.statusNote = $"Waiting for prior shipment from {orbitOI.ObjectName}";
            LogVerbose($"RELAY serialized-wait: rd={rd.ID} orbit={orbitOI.ObjectName} target={finalTargetOI.ObjectName} rawStaged={rawStagedStock:0.#} committed={committedFromOrbit:0.#}");
            return true;
        }

        if (stagedStock <= 0)
        {
            ClearRelayState(req);
            return false;
        }

        var inFlight = GetInFlightDeliveryAmount(finalTargetOI, rd, player);
        var remaining = requestTarget - alreadyThere - inFlight;
        if (remaining <= 0)
        {
            req.status = Data.LogisticsRequestStatus.InProgress;
            req.statusNote = LogisticsStrings.ShippingFrom(orbitOI);
            return true;
        }

        var usefulFinalLoad = GetUsefulRelayFinalLoad(orbitOI, rd, remaining, player, snapshot);
        if (committedFromOrbit > 0 && stagedStock < usefulFinalLoad)
        {
            req.status = Data.LogisticsRequestStatus.InProgress;
            req.statusNote = $"Waiting for prior shipment from {orbitOI.ObjectName}";
            LogVerbose($"RELAY serialized-wait: rd={rd.ID} orbit={orbitOI.ObjectName} target={finalTargetOI.ObjectName} staged={stagedStock:0.#} committed={committedFromOrbit:0.#} usefulLoad={usefulFinalLoad:0.#}");
            return true;
        }
        if (usefulFinalLoad > 0 && stagedStock < usefulFinalLoad && stagedStock < remaining)
        {
            Log($"RELAY restage-needed: rd={rd.ID} orbit={orbitOI.ObjectName} target={finalTargetOI.ObjectName} staged={stagedStock:0.#} usefulLoad={usefulFinalLoad:0.#} remaining={remaining:0.#}");
            ClearRelayState(req);
            return false;
        }

        if (TryCreateRelayFinalDelivery(req, finalTargetOI, orbitOI, rd, Math.Min(remaining, stagedStock), player, snapshot))
            return true;

        req.status = Data.LogisticsRequestStatus.InProgress;
        req.statusNote = LogisticsStrings.WaitingForSpacecraftAt(orbitOI);
        return true;
    }

    private static double GetUsefulRelayFinalLoad(ObjectInfo sourceOrbit, ResourceDefinition rd, double remaining, Company player, PlannerSnapshot snapshot = null)
    {
        if (sourceOrbit == null || rd == null || player == null || remaining <= 0)
            return 0;

        var scActive = snapshot?.ScActive ?? new Dictionary<string, int>();
        var carrier = FindBestIdleSpacecraft(sourceOrbit, player, scActive, requireNonContainer: true, out _, snapshot);
        var capacity = carrier?.spacecraftType?.GetCargoCapacity(player) ?? 0;
        if (capacity <= 0)
            return 0;

        return Math.Min(remaining, capacity);
    }

    private static bool HasActiveCycleDelivering(ObjectInfo requester, ResourceDefinition rd, Company player, PlannerSnapshot snapshot = null)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm == null) return false;

        foreach (var cmd in (snapshot?.Cycles ?? cm.GetAllCycleMission(player)).ToList())
        {
            if (!IsLogisticsMission(cmd)) continue;
            if (cmd.B != requester) continue;
            if (cmd.CheckComplete()) continue;
            if (cmd.cargoAllStart?.Tab == null) continue;

            foreach (var tabRes in cmd.cargoAllStart.Tab)
            {
                if (tabRes == rd)
                {
                    if (IsCycleWaitingOrPlanned(cmd, cm))
                        return true;

                    LogWarning($"CLEANUP stale LOGI cycle: {cmd.A?.ObjectName}->{cmd.B?.ObjectName} rd={rd.ID} reason=not waiting and no planned flight");
                    _cyclePlanningFailures.Remove(cmd);
                    RemoveLogisticsCycle(cm, cmd);
                    break;
                }
            }
        }

        if (HasActiveLogisticsMissionDelivering(requester, rd, player, snapshot))
        {
            ClearPendingPlanningDelivery(requester, rd);
            return true;
        }

        return false;
    }

    private static bool HasActiveLogisticsMissionDelivering(ObjectInfo requester, ResourceDefinition rd, Company player, PlannerSnapshot snapshot = null)
    {
        var mm = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        var missions = snapshot?.Missions ?? mm?.ListMissionInfo;
        if (missions == null || requester == null || rd == null || player == null)
            return false;

        foreach (var mi in missions)
        {
            if (mi == null || mi.complete || mi.cancel) continue;
            if (mi.company != player) continue;
            if (mi.target != requester) continue;
            if (mi.cargoAll == null) continue;
            if (string.IsNullOrEmpty(mi.missionName) || !mi.missionName.StartsWith("[LOGI]", StringComparison.Ordinal))
                continue;

            var cargoAmount = CargoAmountFor(mi.cargoAll.listCargo, rd)
                + CargoAmountFor(mi.cargoAll.listCargoToOrbit, rd);
            if (cargoAmount <= 0) continue;

            LogVerbose($"REQ active-mission-present: target={requester.ObjectName} rd={rd.ID} mission={mi.id} name=\"{mi.missionName}\" launch={mi.DateLaunch:yyyy-MM-dd} amount={cargoAmount:0.#}");
            return true;
        }

        return false;
    }

    private static double RequestTarget(Data.LogisticsRequest req)
    {
        return Math.Max(0, req?.requestedAmount ?? 0);
    }

    private static double RequestMinimum(Data.LogisticsRequest req)
    {
        if (req == null) return 0;
        if (!req.useMinimumAmount)
            return RequestTarget(req);
        return Math.Max(0, Math.Min(req.minimumAmount, RequestTarget(req)));
    }

    private static void CleanupLogisticsCyclesForRequest(ObjectInfo requester, ResourceDefinition rd, Company player, string reason, PlannerSnapshot snapshot = null)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm == null || requester == null || rd == null || player == null) return;

        ClearPendingPlanningDelivery(requester, rd);
        var reqData = Data.LogisticsNetwork.Get(requester);
        if (reqData != null)
        {
            foreach (var req in reqData.requests.Where(r => r.ResourceDefinition == rd))
                ClearRelayState(req);
        }

        foreach (var cmd in (snapshot?.Cycles ?? cm.GetAllCycleMission(player)).ToList())
        {
            if (!IsLogisticsDeliveryMission(cmd)) continue;
            if (cmd.B != requester) continue;
            if (!CargoContainsResource(cmd.cargoAllStart, rd) && !CargoContainsResource(cmd.cargoAllEnd, rd)) continue;
            ClearReturnStatesForCycle(cmd, requester, rd, player, reason);
            if (ShouldPreserveLandedDeliveryCycle(cmd, requester, rd, player))
            {
                LogVerbose($"CLEANUP preserve-landed LOGI cycle: {cmd.A?.ObjectName}->{cmd.B?.ObjectName} rd={rd.ID} reason={reason}");
                continue;
            }

            _cycleCreatedAt.Remove(cmd);
            _cyclePlanningFailures.Remove(cmd);
            LogWarning($"CLEANUP fulfilled LOGI cycle: {cmd.A?.ObjectName}->{cmd.B?.ObjectName} rd={rd.ID} reason={reason}");
            RemoveLogisticsCycle(cm, cmd);
        }
    }

    private static void ClearReturnStatesForCycle(CycleMissionsData cmd, ObjectInfo requester,
        ResourceDefinition rd, Company player, string reason)
    {
        if (cmd?.ListSC == null || requester == null || rd == null || player == null)
            return;

        foreach (var sci in cmd.ListSC)
        {
            if (sci is not Spacecraft sc || sc.GetCompany() != player)
                continue;
            if (!_returnHomeByShipId.TryGetValue(sc.ID, out var state) || state == null)
                continue;
            if (state.Destination != requester || state.Resource != rd)
                continue;

            ResetReturnPlanState(state);
            _returnHomeByShipId.Remove(sc.ID);
            Log($"RETURNHOME clear-owned: ship={sc.GetSpacecraftName()} id={sc.ID} destination={requester.ObjectName} rd={rd.ID} reason={reason}");
        }
    }

    private static bool IsLogisticsDeliveryMission(CycleMissionsData cmd)
    {
        return cmd?.customNameFromPlanMission != null
            && cmd.customNameFromPlanMission.StartsWith("[LOGI]", StringComparison.Ordinal);
    }

    private static bool IsLogisticsReturnMission(CycleMissionsData cmd)
    {
        return cmd?.customNameFromPlanMission != null
            && cmd.customNameFromPlanMission.StartsWith("[LOGI-RETURN]", StringComparison.Ordinal);
    }

    public static string BuildLogisticsMissionName(ObjectInfo from, ObjectInfo to, ResourceDefinition rd, bool isReturn = false)
    {
        var prefix = isReturn ? "[LOGI-RETURN]" : "[LOGI]";
        var icon = rd?.IconString;
        var iconPart = string.IsNullOrWhiteSpace(icon) ? string.Empty : $" {icon}";
        return $"{prefix}{iconPart} {from?.ObjectName ?? "UNKNOWN"} -> {to?.ObjectName ?? "UNKNOWN"}";
    }

    private static string PendingDeliveryKey(ObjectInfo requester, ResourceDefinition rd)
    {
        return $"{requester?.id ?? -1}:{rd?.ID ?? "null"}";
    }

    private static string BlockedRetryKey(ObjectInfo requester, ResourceDefinition rd)
    {
        return PendingDeliveryKey(requester, rd);
    }

    private static string BuildRequestPlanSignature(ObjectInfo requester, ResourceDefinition rd,
        double requestTarget, double alreadyThere, double inFlight, bool hasActiveDelivery,
        string blockedReturnNote, PlannerSnapshot snapshot)
    {
        var cycleCount = snapshot?.Cycles?.Count ?? -1;
        var missionCount = snapshot?.Missions?.Count ?? -1;
        return $"{requester?.id ?? -1}:{rd?.ID ?? "null"}:" +
               $"target={Math.Round(requestTarget, 1)}:" +
               $"stock={Math.Round(alreadyThere, 1)}:" +
               $"inflight={Math.Round(inFlight, 1)}:" +
               $"active={hasActiveDelivery}:" +
               $"blocked={!string.IsNullOrEmpty(blockedReturnNote)}:" +
               $"cycles={cycleCount}:missions={missionCount}";
    }

    private static bool ShouldDeferRequestPlanning(ObjectInfo requester, ResourceDefinition rd,
        string signature, out string statusNote)
    {
        statusNote = null;
        var key = PendingDeliveryKey(requester, rd);
        if (!_requestPlanThrottle.TryGetValue(key, out var state) || state == null)
            return false;

        if (!string.Equals(state.Signature, signature, StringComparison.Ordinal))
        {
            _requestPlanThrottle.Remove(key);
            return false;
        }

        var currentTime = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        if (currentTime >= state.NextEvaluation)
        {
            _requestPlanThrottle.Remove(key);
            return false;
        }

        var days = Math.Max(0.0, (state.NextEvaluation - currentTime).TotalDays);
        statusNote = $"Waiting to re-check logistics options ({days:0.#}d)";
        LogVerbose($"REQ throttle-skip: target={requester?.ObjectName} rd={rd?.ID} next={state.NextEvaluation:yyyy-MM-dd} days={days:0.#}");
        return true;
    }

    private static void MarkRequestPlanningEvaluated(ObjectInfo requester, ResourceDefinition rd, string signature)
    {
        if (requester == null || rd == null || string.IsNullOrEmpty(signature)) return;

        var currentTime = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        _requestPlanThrottle[PendingDeliveryKey(requester, rd)] = new RequestPlanThrottleState
        {
            Signature = signature,
            NextEvaluation = currentTime.AddDays(RequestPlanThrottleDays)
        };
    }

    private static void ClearRequestPlanningThrottle(ObjectInfo requester, ResourceDefinition rd)
    {
        _requestPlanThrottle.Remove(PendingDeliveryKey(requester, rd));
    }

    private static bool IsTransientPlanningStatus(string statusNote)
    {
        return !string.IsNullOrEmpty(statusNote)
            && (statusNote.StartsWith("Planning mission", StringComparison.Ordinal)
                || statusNote.StartsWith("Waiting to re-check logistics options", StringComparison.Ordinal));
    }

    private static string RoutePlanningLockKey(ObjectInfo source, ObjectInfo target, ResourceDefinition rd, Company player)
    {
        return $"{player?.name ?? "null"}:{source?.id ?? -1}->{target?.id ?? -1}:{rd?.ID ?? "null"}";
    }

    private static bool HasRoutePlanningLock(ObjectInfo source, ObjectInfo target, ResourceDefinition rd,
        Company player, out string statusNote)
    {
        statusNote = null;
        var key = RoutePlanningLockKey(source, target, rd, player);
        if (!_routePlanningLocks.TryGetValue(key, out var createdAt))
            return false;

        var currentTime = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        var ageDays = (currentTime - createdAt).TotalDays;
        if (ageDays < EffectiveCyclePlanningGraceDays)
        {
            statusNote = $"Planning mission for {source?.ObjectName ?? "UNKNOWN"} -> {target?.ObjectName ?? "UNKNOWN"}";
            LogVerbose($"PLAN route-lock-wait: key={key} age={ageDays:0.#}d rd={rd?.ID}");
            return true;
        }

        _routePlanningLocks.Remove(key);
        LogWarning($"PLAN route-lock-stale: key={key} age={ageDays:0.#}d expired after {EffectiveCyclePlanningGraceDays:0.#}d");
        return false;
    }

    private static bool TryAcquireRoutePlanningLock(ObjectInfo source, ObjectInfo target, ResourceDefinition rd,
        Company player, out string routeLockKey)
    {
        routeLockKey = RoutePlanningLockKey(source, target, rd, player);
        if (HasRoutePlanningLock(source, target, rd, player, out _))
            return false;

        _routePlanningLocks[routeLockKey] =
            MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        LogVerbose($"PLAN route-lock-acquire: key={routeLockKey} route={source?.ObjectName}->{target?.ObjectName} rd={rd?.ID}");
        return true;
    }

    private static void ReleaseRoutePlanningLock(string routeLockKey, string reason)
    {
        if (string.IsNullOrWhiteSpace(routeLockKey))
            return;

        if (_routePlanningLocks.Remove(routeLockKey))
            LogVerbose($"PLAN route-lock-release: key={routeLockKey} reason={reason}");
    }

    private static string CommittedStockKey(ObjectInfo source, ResourceDefinition rd)
    {
        return $"{source?.id ?? -1}:{rd?.ID ?? "null"}";
    }

    private static void ResetCommittedStockIfStale()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _committedStockWallClock).TotalSeconds;
        if (elapsed > CommittedStockWindowSeconds && _committedStock.Count > 0)
        {
            LogVerbose($"STOCK committed-window-reset: cleared {_committedStock.Count} entries after {elapsed:0.#}s");
            _committedStock.Clear();
        }
    }

    private static void CommitStock(ObjectInfo source, ResourceDefinition rd, double amount)
    {
        if (source == null || rd == null || amount <= 0) return;
        ResetCommittedStockIfStale();
        var key = CommittedStockKey(source, rd);
        _committedStock.TryGetValue(key, out var existing);
        _committedStock[key] = existing + amount;
        _committedStockWallClock = DateTime.UtcNow;
        Log($"STOCK committed: source={source.ObjectName} rd={rd.ID} amount={amount:0.#} totalThisWindow={existing + amount:0.#}");
    }

    private static double GetCommittedStock(ObjectInfo source, ResourceDefinition rd)
    {
        if (source == null || rd == null) return 0;
        ResetCommittedStockIfStale();
        var key = CommittedStockKey(source, rd);
        _committedStock.TryGetValue(key, out var val);
        return val;
    }

    private static string FormatCooldownStatus(BlockedRetryState state)
    {
        if (state == null) return null;
        var currentTime = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        var days = Math.Max(0, (state.RetryAfter - currentTime).TotalDays);
        var reason = string.IsNullOrWhiteSpace(state.Reason) ? "last attempt was blocked" : state.Reason;
        return $"Retrying in {days:0.#} days: {reason}";
    }

    private static bool HasBlockedPlanningRetryCooldown(ObjectInfo requester, ResourceDefinition rd, out string statusNote)
    {
        statusNote = null;
        var key = BlockedRetryKey(requester, rd);
        if (!_blockedPlanningRetries.TryGetValue(key, out var state) || state == null)
            return false;

        var currentTime = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        if (currentTime < state.RetryAfter)
        {
            statusNote = FormatCooldownStatus(state);
            LogVerbose($"DISPATCH cooldown: target={requester?.ObjectName} rd={rd?.ID} retryAfter={state.RetryAfter:yyyy-MM-dd} reason={state.Reason}");
            return true;
        }

        _blockedPlanningRetries.Remove(key);
        LogVerbose($"DISPATCH cooldown-expired: target={requester?.ObjectName} rd={rd?.ID}");
        return false;
    }

    private static void MarkBlockedPlanningRetryCooldown(ObjectInfo requester, ResourceDefinition rd, string reason)
    {
        if (requester == null || rd == null)
            return;

        var cooldownDays = Math.Max(0, BlockedMissionRetryCooldownDays);
        if (cooldownDays <= 0)
            return;

        var currentTime = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        var retryAfter = currentTime.AddDays(cooldownDays);
        var key = BlockedRetryKey(requester, rd);
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "dispatch blocked" : reason;
        if (_blockedPlanningRetries.TryGetValue(key, out var existing)
            && existing != null
            && existing.RetryAfter >= retryAfter
            && existing.Reason == normalizedReason)
        {
            return;
        }

        _blockedPlanningRetries[key] = new BlockedRetryState
        {
            RetryAfter = retryAfter,
            Reason = normalizedReason
        };
        LogWarning($"DISPATCH cooldown-set: target={requester.ObjectName} rd={rd.ID} days={cooldownDays:0.#} reason={normalizedReason}");
    }

    private static void ClearBlockedPlanningRetryCooldown(ObjectInfo requester, ResourceDefinition rd)
    {
        _blockedPlanningRetries.Remove(BlockedRetryKey(requester, rd));
    }

    private static bool ShouldPreserveLandedDeliveryCycle(CycleMissionsData cmd, ObjectInfo requester,
        ResourceDefinition rd, Company player)
    {
        if (cmd?.ListSC == null || requester == null || rd == null || player == null)
            return false;

        foreach (var sci in cmd.ListSC)
        {
            if (sci is not Spacecraft sc || sc.GetCompany() != player)
                continue;
            if (!_returnHomeByShipId.TryGetValue(sc.ID, out var state) || state == null)
                continue;
            if (state.Destination != requester || state.Resource != rd)
                continue;
            if (sc.CurrentPhase != Spacecraft.EPhase.None)
                continue;
            if (sc.CurrentlyOnThisObject != requester)
                continue;
            return true;
        }

        return false;
    }

    private static bool HasPendingPlanningDelivery(ObjectInfo requester, ResourceDefinition rd)
    {
        var key = PendingDeliveryKey(requester, rd);
        if (!_pendingPlanningDeliveries.TryGetValue(key, out var createdAt))
            return false;

        var currentTime = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        if ((currentTime - createdAt).TotalDays < EffectiveCyclePlanningGraceDays)
            return true;

        _pendingPlanningDeliveries.Remove(key);
        var reason = $"pending plan stale after {EffectiveCyclePlanningGraceDays:0.#} days";
        LogWarning($"PENDING stale: target={requester?.ObjectName} rd={rd?.ID} expired after {EffectiveCyclePlanningGraceDays:0.#} days");
        MarkBlockedPlanningRetryCooldown(requester, rd, reason);
        return false;
    }

    private static void MarkPendingPlanningDelivery(ObjectInfo requester, ResourceDefinition rd)
    {
        if (requester == null || rd == null) return;
        ClearBlockedPlanningRetryCooldown(requester, rd);
        var key = PendingDeliveryKey(requester, rd);
        _requestPlanThrottle.Remove(key);
        _pendingPlanningDeliveries[key] =
            MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
    }

    private static void ClearPendingPlanningDelivery(ObjectInfo requester, ResourceDefinition rd)
    {
        if (requester == null || rd == null) return;
        var key = PendingDeliveryKey(requester, rd);
        _pendingPlanningDeliveries.Remove(key);
        _requestPlanThrottle.Remove(key);
    }

    private static bool IsCyclePastPlanningGrace(CycleMissionsData cmd)
    {
        if (cmd == null || !_cycleCreatedAt.TryGetValue(cmd, out var createdAt))
            return false;

        var currentTime = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        return (currentTime - createdAt).TotalDays >= EffectiveCyclePlanningGraceDays;
    }

    private static bool HasCycleActuallyLaunched(Spacecraft sc, CycleMissionsData cmd, CycleMissionManager cm)
    {
        if (sc == null || cmd == null)
            return false;
        if (sc.CurrentPhase != Spacecraft.EPhase.None)
            return true;
        if (cmd.wasSetPMParameterForCodeJobSystem)
            return true;

        var ctrl = sc.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
        return ctrl != null && ctrl.CycleMissionPlanFlyWas;
    }

    private static double GetReturnRetryCooldownDays(ReturnHomeState state)
    {
        if (state != null && state.ConsecutiveReturnCycleFailures > ReturnCycleEscalationFailureThreshold)
            return ReturnCycleEscalatedCooldownDays;
        return ReturnCycleBlockedCooldownDays;
    }

    private static void SetReturnRetryCooldown(ReturnHomeState state, Spacecraft sc, ObjectInfo current, ObjectInfo home, string reason)
    {
        if (state == null)
            return;

        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        state.ConsecutiveReturnCycleFailures++;
        var cooldownDays = GetReturnRetryCooldownDays(state);
        state.ReturnRetryAfter = now.AddDays(cooldownDays);
        state.ReturnRetryWallClockAfterUtc = DateTime.UtcNow.Add(ReturnCycleWallClockThrottle);
        state.LastBlockedReason = reason;
        state.LastBlockedStatusNote = LogisticsStrings.ReturnRetryCooldown(cooldownDays);
        state.LastBlockedDate = now.Date;
        LogWarning($"RETURNHOME cooldown-set: ship={sc?.GetSpacecraftName() ?? "null"} id={sc?.ID ?? -1} current={current?.ObjectName ?? "null"} home={home?.ObjectName ?? "null"} days={cooldownDays:0.#} failures={state.ConsecutiveReturnCycleFailures} reason={reason}");
    }

    private static void MarkReturnAttemptCooldown(ReturnHomeState state, Spacecraft sc, ObjectInfo current, ObjectInfo home, string reason)
    {
        if (state == null)
            return;

        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        state.ReturnRetryAfter = now.AddDays(ReturnCycleBlockedCooldownDays);
        state.ReturnRetryWallClockAfterUtc = DateTime.UtcNow.Add(ReturnCycleWallClockThrottle);
        state.LastBlockedStatusNote = LogisticsStrings.AwaitingReturnFrom(current);
        LogVerbose($"RETURNHOME attempt-cooldown: ship={sc?.GetSpacecraftName() ?? "null"} id={sc?.ID ?? -1} current={current?.ObjectName ?? "null"} home={home?.ObjectName ?? "null"} days={ReturnCycleBlockedCooldownDays:0.#} reason={reason}");
    }

    private static bool IsReturnRetryCoolingDown(ReturnHomeState state, out string statusNote)
    {
        statusNote = null;
        if (state == null)
            return false;

        var nowGame = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        var nowReal = DateTime.UtcNow;
        var gameRemaining = Math.Max(0, (state.ReturnRetryAfter - nowGame).TotalDays);
        var realRemaining = Math.Max(0, (state.ReturnRetryWallClockAfterUtc - nowReal).TotalSeconds);
        if (gameRemaining <= 0 && realRemaining <= 0)
            return false;

        statusNote = gameRemaining > 0
            ? LogisticsStrings.ReturnRetryCooldown(gameRemaining)
            : $"Return launch blocked; retrying shortly ({realRemaining:0.#}s)";
        return true;
    }

    private static bool IsCycleWaitingOrPlanned(CycleMissionsData cmd, CycleMissionManager cm)
    {
        if (cmd == null || cm == null) return false;
        var withinGrace = false;
        if (_cycleCreatedAt.TryGetValue(cmd, out var createdAt))
        {
            var currentTime = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
            if ((currentTime - createdAt).TotalDays < EffectiveCyclePlanningGraceDays)
                withinGrace = true;
            else
                _cycleCreatedAt.Remove(cmd);
        }

        var hasEverFlown = false;
        var now = Time.unscaledTime;
        if (_cachedSpacecraft == null || now - _cachedSpacecraftTime > 0.5f)
        {
            _cachedSpacecraft = UnityEngine.Object.FindObjectsOfType<Spacecraft>();
            _cachedSpacecraftTime = now;
        }
        foreach (var sc in _cachedSpacecraft)
        {
            if (sc == null) continue;
            if (cm.GetCycleMission(sc) != cmd) continue;

            var ctrl = sc.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
            if (ctrl != null && ctrl.CycleMissionPlanFlyWas)
            {
                hasEverFlown = true;
                _cycleCreatedAt.Remove(cmd);
                foreach (var tabRes in cmd.cargoAllStart?.Tab ?? Array.Empty<ResourceDefinition>())
                    ClearPendingPlanningDelivery(cmd.B, tabRes);
                return true;
            }

            if (_returnHomeByShipId.TryGetValue(sc.ID, out var returnState)
                && returnState != null
                && !returnState.HasLeftHome)
            {
                return true;
            }
        }

        if (withinGrace)
            return true;

        if (cmd.wasSetPMParameterForCodeJobSystem && !hasEverFlown)
        {
            _cyclePlanningFailures.TryGetValue(cmd, out var failures);
            _cyclePlanningFailures[cmd] = failures + 1;
            if (failures + 1 >= MaxCyclePlanningFailures)
            {
                LogWarning($"CLEANUP stuck-planning LOGI cycle: {cmd.A?.ObjectName}->{cmd.B?.ObjectName} name={cmd.customNameFromPlanMission} failures={failures + 1} (job system active but ship never flew)");
                _cyclePlanningFailures.Remove(cmd);
                _cycleCreatedAt.Remove(cmd);
                return false;
            }
            return true;
        }

        return false;
    }

    private static double GetInFlightDeliveryAmount(ObjectInfo requester, ResourceDefinition rd, Company player, PlannerSnapshot snapshot = null)
    {
        var mm = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        var missions = snapshot?.Missions ?? mm?.ListMissionInfo;
        if (missions == null || requester == null || rd == null || player == null)
            return 0;

        double result = 0;
        foreach (var mi in missions)
        {
            if (mi == null || mi.complete || mi.cancel) continue;
            if (mi.company != player) continue;
            if (mi.target != requester) continue;
            if (mi.cargoAll == null) continue;

            result += CargoAmountFor(mi.cargoAll.listCargo, rd);
            result += CargoAmountFor(mi.cargoAll.listCargoToOrbit, rd);
        }

        return result;
    }

    private static double CargoAmountFor(IEnumerable<Cargo> cargoList, ResourceDefinition rd)
    {
        if (cargoList == null || rd == null) return 0;
        return cargoList
            .Where(c => c != null
                && c.resourceTypeType == EResourceTypeType.resorces
                && c.resourceType == rd)
            .Sum(c => c.cargoMass);
    }

    public static void GetActiveCycleCounts(Company player,
        out Dictionary<string, int> scActive, out Dictionary<string, int> lvActive)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        var cycles = cm?.GetAllCycleMission(player);
        CountActiveLogisticsCycles(player, cycles, out scActive, out lvActive, out _);
    }

    private static void CountActiveLogisticsCycles(Company player,
        IEnumerable<CycleMissionsData> cycles,
        out Dictionary<string, int> scActive, out Dictionary<string, int> lvActive,
        out HashSet<int> committedShipIds)
    {
        scActive = new Dictionary<string, int>();
        lvActive = new Dictionary<string, int>();
        committedShipIds = new HashSet<int>();
        if (cycles == null) return;

        foreach (var cmd in cycles)
        {
            if (cmd == null || cmd.CheckComplete()) continue;
            if (!IsLogisticsMission(cmd)) continue;
            if (cmd.ListSC == null) continue;

            foreach (var sci in cmd.ListSC)
            {
                var sc = sci as Spacecraft;
                if (sc == null || sc.spacecraftType == null) continue;
                var tn = Data.LogisticsNetwork.TypeKey(sc.spacecraftType.ID, sc.spacecraftType.NameRocketType ?? "SC");
                if (!scActive.ContainsKey(tn)) scActive[tn] = 0;
                scActive[tn]++;
                committedShipIds.Add(sc.ID);
            }

            if (cmd.LvTypeA != null)
            {
                var tn = Data.LogisticsNetwork.TypeKey(cmd.LvTypeA.ID, cmd.LvTypeA.Name ?? "LV");
                if (!lvActive.ContainsKey(tn)) lvActive[tn] = 0;
                lvActive[tn]++;
            }
            if (cmd.LvTypeB != null)
            {
                var tn = Data.LogisticsNetwork.TypeKey(cmd.LvTypeB.ID, cmd.LvTypeB.Name ?? "LV");
                if (!lvActive.ContainsKey(tn)) lvActive[tn] = 0;
                lvActive[tn]++;
            }
        }
    }

    private static void RecordDispatchInSnapshot(PlannerSnapshot snapshot, Spacecraft sc, LaunchVehicleType lvType)
    {
        if (snapshot == null) return;
        if (sc?.spacecraftType != null)
        {
            var tn = Data.LogisticsNetwork.TypeKey(sc.spacecraftType.ID, sc.spacecraftType.NameRocketType ?? "SC");
            if (!snapshot.ScActive.ContainsKey(tn)) snapshot.ScActive[tn] = 0;
            snapshot.ScActive[tn]++;
            snapshot.CommittedShipIds.Add(sc.ID);
        }
        if (lvType != null)
        {
            var tn = Data.LogisticsNetwork.TypeKey(lvType.ID, lvType.Name ?? "LV");
            if (!snapshot.LvActive.ContainsKey(tn)) snapshot.LvActive[tn] = 0;
            snapshot.LvActive[tn]++;
        }
    }

    private static bool IsLogisticsMission(CycleMissionsData cmd)
    {
        return cmd?.customNameFromPlanMission != null
            && cmd.customNameFromPlanMission.StartsWith("[LOGI", StringComparison.Ordinal);
    }

    public static void RegisterLogisticsCycleName(CycleMissionsData cmd)
    {
        if (!IsLogisticsMission(cmd)) return;

        var name = cmd.customNameFromPlanMission;
        if (cmd.ListSC != null)
        {
            foreach (var sci in cmd.ListSC)
            {
                if (sci is Spacecraft sc && sc.ID >= 0)
                    _cycleNameByShipId[sc.ID] = name;
            }
        }

        var routeKey = MakeCycleRouteKey(cmd.A, cmd.B, cmd.Company);
        if (routeKey != null)
            _cycleNameByRouteKey[routeKey] = name;
    }

    public static void UnregisterLogisticsCycleName(CycleMissionsData cmd)
    {
        if (!IsLogisticsMission(cmd)) return;

        if (cmd.ListSC != null)
        {
            foreach (var sci in cmd.ListSC)
            {
                if (sci is Spacecraft sc && sc.ID >= 0)
                    _cycleNameByShipId.Remove(sc.ID);
            }
        }

        var routeKey = MakeCycleRouteKey(cmd.A, cmd.B, cmd.Company);
        if (routeKey != null)
            _cycleNameByRouteKey.Remove(routeKey);
    }

    public static void RemoveLogisticsCycle(CycleMissionManager cm, CycleMissionsData cmd)
    {
        if (cm == null || cmd == null) return;
        UnregisterLogisticsCycleName(cmd);
        cm.RemoveCycleMission(cmd);
    }

    private static string MakeCycleRouteKey(ObjectInfo a, ObjectInfo b, Company company)
    {
        if (a == null || b == null || company == null) return null;
        var first = Math.Min(a.id, b.id);
        var second = Math.Max(a.id, b.id);
        return $"{company.ID}|{first}|{second}";
    }

    private static string DescribeSpacecraft(Spacecraft sc)
    {
        if (sc == null) return "null";
        return $"{sc.GetSpacecraftName() ?? sc.spacecraftName ?? sc.spacecraftType?.NameRocketType ?? "SC"}#{sc.ID}";
    }

    private static bool IsSameSpacecraftIdentity(Spacecraft a, Spacecraft b)
    {
        if (a == null || b == null) return false;
        if (ReferenceEquals(a, b)) return true;
        return a.ID >= 0 && b.ID >= 0 && a.ID == b.ID;
    }

    private static bool IsReservedForLogisticsReturn(Spacecraft sc)
    {
        if (sc == null || sc.ID < 0) return false;
        if (!_returnHomeByShipId.TryGetValue(sc.ID, out var state) || state == null)
            return false;

        // Once logistics assigns a ship to an outbound delivery, keep it owned until the
        // return-home state is explicitly cleared. Stock can briefly detach failed cycles
        // while the ship is still visible at home; treating that ship as available here
        // causes duplicate outbound/return cycles.
        return true;
    }

    private static bool IsSpacecraftAlreadyCommitted(Spacecraft sc, Company player, out string reason,
        bool includeReturnReservation = true, HashSet<int> committedShipIds = null)
    {
        reason = null;
        if (sc == null)
        {
            reason = "ship is null";
            return true;
        }

        if (sc.spacecraftType == null)
        {
            reason = $"{DescribeSpacecraft(sc)} has no spacecraft type";
            return true;
        }

        if (player != null && sc.GetCompany() != player)
        {
            reason = $"{DescribeSpacecraft(sc)} is not owned by player";
            return true;
        }

        if (sc.CurrentPhase != Spacecraft.EPhase.None)
        {
            reason = $"{DescribeSpacecraft(sc)} phase={sc.CurrentPhase}";
            return true;
        }

        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        var attached = cm?.GetCycleMission(sc);
        if (attached != null && !attached.CheckComplete())
        {
            reason = $"{DescribeSpacecraft(sc)} already has cycle {attached.customNameFromPlanMission ?? "unnamed"}";
            return true;
        }

        var controllerCycle = sc.CraftCyclicalMissionController?.CycleMissionsData;
        if (controllerCycle != null && !controllerCycle.CheckComplete())
        {
            reason = $"{DescribeSpacecraft(sc)} controller already has cycle {controllerCycle.customNameFromPlanMission ?? "unnamed"}";
            return true;
        }

        if (includeReturnReservation && IsReservedForLogisticsReturn(sc))
        {
            reason = $"{DescribeSpacecraft(sc)} is reserved for logistics return";
            return true;
        }

        // Use pre-built committed set when available (O(1) lookup),
        // fall back to full cycle scan otherwise.
        if (committedShipIds != null)
        {
            if (sc.ID >= 0 && committedShipIds.Contains(sc.ID))
            {
                reason = $"{DescribeSpacecraft(sc)} identity in committed-ship set";
                return true;
            }
        }
        else if (cm != null && player != null)
        {
            foreach (var cmd in cm.GetAllCycleMission(player))
            {
                if (cmd == null || cmd.CheckComplete() || cmd.ListSC == null)
                    continue;

                foreach (var sci in cmd.ListSC)
                {
                    if (sci is not Spacecraft other || !IsSameSpacecraftIdentity(sc, other))
                        continue;

                    reason = $"{DescribeSpacecraft(sc)} identity already appears in active cycle {cmd.customNameFromPlanMission ?? "unnamed"} as {DescribeSpacecraft(other)}";
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsSpacecraftAvailableForLogistics(Spacecraft sc, Company player, HashSet<int> committedShipIds = null)
    {
        return !IsSpacecraftAlreadyCommitted(sc, player, out _, committedShipIds: committedShipIds);
    }

    private static bool ValidateSpacecraftForCycleCreation(Spacecraft sc, Company player, string context)
    {
        if (!IsSpacecraftAlreadyCommitted(sc, player, out var reason))
            return true;

        LogWarning($"SKIP cycle: spacecraft already in use context={context} reason={reason}");
        return false;
    }

    private static bool ValidateSpacecraftForReturnCycleCreation(Spacecraft sc, Company player, string context)
    {
        if (!IsSpacecraftAlreadyCommitted(sc, player, out var reason, includeReturnReservation: false))
            return true;

        LogWarning($"SKIP cycle: spacecraft already in use context={context} reason={reason}");
        return false;
    }

    public static bool IsLogisticsPlan(PMMissionParameter pmp)
    {
        return !string.IsNullOrEmpty(FindLogisticsCycleName(pmp));
    }

    public static string FindLogisticsCycleName(PMMissionParameter pmp)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (pmp == null || player == null || cm == null)
            return null;

        if (pmp.FlyCompany != null && pmp.FlyCompany != player)
            return null;

        if (pmp.SC is Spacecraft pmpSc)
        {
            if (pmpSc.ID >= 0 && _cycleNameByShipId.TryGetValue(pmpSc.ID, out var cachedShipName))
                return cachedShipName;

            var scCmd = cm.GetCycleMission(pmpSc);
            if (scCmd != null && IsLogisticsMission(scCmd) && !string.IsNullOrEmpty(scCmd.customNameFromPlanMission))
            {
                RegisterLogisticsCycleName(scCmd);
                return scCmd.customNameFromPlanMission;
            }
        }

        if (pmp.Start != null && pmp.Target != null)
        {
            var routeKey = MakeCycleRouteKey(pmp.Start, pmp.Target, player);
            if (routeKey != null && _cycleNameByRouteKey.TryGetValue(routeKey, out var cachedRouteName))
                return cachedRouteName;

            var allCycles = cm.GetAllCycleMission(player);
            foreach (var cmd in allCycles)
            {
                if (!IsLogisticsMission(cmd)) continue;
                if (string.IsNullOrEmpty(cmd.customNameFromPlanMission)) continue;

                var sameDirection = cmd.A == pmp.Start && cmd.B == pmp.Target;
                var reverseDirection = cmd.B == pmp.Start && cmd.A == pmp.Target;
                if (sameDirection || reverseDirection)
                {
                    RegisterLogisticsCycleName(cmd);
                    return cmd.customNameFromPlanMission;
                }
            }
        }

        return null;
    }

    public static string FindLogisticsCycleName(ObjectInfo start, ObjectInfo target, Company company,
        IEnumerable<ISpacecraftInfo> spacecraftInfos, CargoAll cargoAll)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (start == null || target == null || company == null || cm == null) return null;

        var routeKey = MakeCycleRouteKey(start, target, company);
        if (routeKey != null && _cycleNameByRouteKey.TryGetValue(routeKey, out var cachedRouteName))
            return cachedRouteName;

        var spacecraftSet = new HashSet<ISpacecraftInfo>();
        if (spacecraftInfos != null)
        {
            foreach (var sci in spacecraftInfos)
            {
                if (sci == null) continue;
                spacecraftSet.Add(sci);
                if (sci is Spacecraft sc && sc.ID >= 0 && _cycleNameByShipId.TryGetValue(sc.ID, out var cachedShipName))
                    return cachedShipName;
            }
        }

        foreach (var cmd in cm.GetAllCycleMission(company))
        {
            if (!IsLogisticsMission(cmd)) continue;
            if (string.IsNullOrEmpty(cmd.customNameFromPlanMission)) continue;

            if (spacecraftSet.Count > 0 && cmd.ListSC != null)
            {
                foreach (var sci in cmd.ListSC)
                {
                    if (sci != null && spacecraftSet.Contains(sci))
                    {
                        RegisterLogisticsCycleName(cmd);
                        return cmd.customNameFromPlanMission;
                    }
                }
            }

            var sameDirection = cmd.A == start && cmd.B == target;
            var reverseDirection = cmd.B == start && cmd.A == target;
            if (!sameDirection && !reverseDirection) continue;

            if (cargoAll == null || CargoOverlaps(cmd.cargoAllStart, cargoAll) || CargoOverlaps(cmd.cargoAllEnd, cargoAll))
            {
                RegisterLogisticsCycleName(cmd);
                return cmd.customNameFromPlanMission;
            }
        }

        return null;
    }

    private static bool CargoOverlaps(InfoCargoCyclicalMission cycleCargo, CargoAll missionCargo)
    {
        if (cycleCargo?.Tab == null || missionCargo == null) return false;
        foreach (var rd in cycleCargo.Tab)
        {
            if (rd == null) continue;
            if (CargoContainsResource(missionCargo, rd))
                return true;
        }
        return false;
    }

    public static void CapLogisticsCargoForPlannerLimits(PMMissionParameter pmp)
    {
        if (!IsLogisticsPlan(pmp) || pmp.CargoAll == null) return;
        if (CanSkipPlannerCapCheckForSimpleLocLaunch(pmp))
            return;

        var result = pmp.CheckCanPlanMission().planMissionResult;
        if (ApplySmallReservePropellant(pmp))
            result = pmp.CheckCanPlanMission().planMissionResult;

        if (VerboseLoggingEnabled)
        {
            var cargoStart = pmp.CargoAll.CargoCurrent;
            var capacity = (pmp.SC?.GetTypeSpaceCraft()?.GetCargoCapacity(pmp.FlyCompany) ?? 0) * Math.Max(1, pmp.SCCount);
            Log($"LOGI-CAP before: {pmp.Start?.ObjectName}->{pmp.Target?.ObjectName} result={result} cargo={cargoStart:0.#}/{capacity:0.#} propellant={pmp.CargoAll?.cargoFuel?.cargoMassPotencjal:0.#} sc={pmp.SC?.GetSpacecraftName()} scType={pmp.SC?.GetTypeSpaceCraft()?.NameRocketType} lv={pmp.LV?.GetLaunchVehicleType()?.Name} manifest={FormatCargo(pmp.CargoAll)}");
        }
        if (result == PMMissionParameter.EPlanMissionResult.AllOk) return;
        if (result.HasFlag(PMMissionParameter.EPlanMissionResult.WrongLV) && pmp.LV == null)
        {
            LogWarning($"PLAN invalid: {pmp.Start?.ObjectName}->{pmp.Target?.ObjectName} needs an LV but none was assigned; leaving cargo unchanged");
            return;
        }

        var limitingFailure =
            result.HasFlag(PMMissionParameter.EPlanMissionResult.WrongThrust)
            || result.HasFlag(PMMissionParameter.EPlanMissionResult.WrongMaxCapacityFuelOk)
            || result.HasFlag(PMMissionParameter.EPlanMissionResult.WrongLV)
            || result.HasFlag(PMMissionParameter.EPlanMissionResult.WrongResourcesCargoLoadLimit);

        if (!limitingFailure) return;

        var cargoItems = GetResourceCargoItems(pmp.CargoAll);
        if (cargoItems.Count == 0) return;

        var original = cargoItems.Select(c => c.cargoMass).ToArray();
        var originalTotal = original.Sum();
        if (originalTotal <= 0) return;

        double bestScale = -1;
        double low = 0;
        double high = 1;
        var bestResult = result;

        for (var i = 0; i < 6; i++)
        {
            var scale = (low + high) / 2;
            ApplyCargoScale(cargoItems, original, scale);

            var check = pmp.CheckCanPlanMission().planMissionResult;
            if (check == PMMissionParameter.EPlanMissionResult.AllOk)
            {
                bestScale = scale;
                bestResult = check;
                low = scale;
            }
            else
            {
                high = scale;
            }
        }

        if (bestScale >= 0)
        {
            ApplyCargoScale(cargoItems, original, bestScale);
            if (VerboseLoggingEnabled)
            {
                var cappedTotal = cargoItems.Sum(c => c.cargoMass);
                var capacity = (pmp.SC?.GetTypeSpaceCraft()?.GetCargoCapacity(pmp.FlyCompany) ?? 0) * Math.Max(1, pmp.SCCount);
                Log($"LOGI-CAP scaled: {pmp.Start?.ObjectName}->{pmp.Target?.ObjectName} cargo={originalTotal:0.#}->{cappedTotal:0.#}/{capacity:0.#} scale={bestScale:0.###} dueTo={result} after={bestResult} manifest={FormatCargo(pmp.CargoAll)}");
            }
        }
        else
        {
            ApplyCargoScale(cargoItems, original, 0);
            LogWarning($"CAP planner cargo: no valid cargo amount found for {pmp.Start?.ObjectName} -> {pmp.Target?.ObjectName}; original={originalTotal:0.#}, result={result} - aborting cycle");
            AbortLogisticsCycle(pmp);
        }
    }

    private static bool CanSkipPlannerCapCheckForSimpleLocLaunch(PMMissionParameter pmp)
    {
        if (pmp == null || pmp.CargoAll == null || pmp.SC == null || pmp.Start == null || pmp.Target == null || pmp.FlyCompany == null)
            return false;

        var scType = pmp.SC.GetTypeSpaceCraft();
        if (scType?.LowOrbitContainer != true)
            return false;
        if (pmp.LV == null)
            return false;
        if (!pmp.Start.NeedVehicleToLaunch() || !IsOrbitOf(pmp.Target, pmp.Start))
            return false;

        var capacity = scType.GetCargoCapacity(pmp.FlyCompany) * Math.Max(1, pmp.SCCount);
        if (pmp.CargoAll.CargoCurrent > capacity + 0.001)
            return false;

        try
        {
            if (!pmp.LV.CheckMaximumPayload(pmp.CargoAll, pmp.SC))
                return false;
        }
        catch
        {
            return false;
        }

        LogVerbose($"LOGI-CAP skip-simple-loc: {pmp.Start.ObjectName}->{pmp.Target.ObjectName} cargo={pmp.CargoAll.CargoCurrent:0.#}/{capacity:0.#} lv={pmp.LV.GetLaunchVehicleType()?.Name ?? "none"}");
        return true;
    }

    private static void AbortLogisticsCycle(PMMissionParameter pmp)
    {
        var player = pmp?.FlyCompany ?? MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (pmp == null || player == null || cm == null) return;

        foreach (var cmd in cm.GetAllCycleMission(player).ToList())
        {
            if (!IsLogisticsMission(cmd)) continue;
            var sameDirection = cmd.A == pmp.Start && cmd.B == pmp.Target;
            var reverseDirection = cmd.B == pmp.Start && cmd.A == pmp.Target;
            if (!sameDirection && !reverseDirection) continue;

            if (cmd.ListSC != null)
            {
                foreach (var sci in cmd.ListSC)
                {
                    if (sci is Spacecraft sc && sc.GetCompany() == player)
                    {
                        _returnHomeByShipId.Remove(sc.ID);
                    }
                }
            }

            foreach (var tabRes in cmd.cargoAllStart?.Tab ?? Array.Empty<ResourceDefinition>())
                ClearPendingPlanningDelivery(cmd.B, tabRes);

            _cycleCreatedAt.Remove(cmd);
            _cyclePlanningFailures.Remove(cmd);
            var reason = "zero cargo — ship cannot carry any payload on this route";
            if (cmd.B != null)
            {
                foreach (var tabRes in cmd.cargoAllStart?.Tab ?? Array.Empty<ResourceDefinition>())
                    MarkBlockedPlanningRetryCooldown(cmd.B, tabRes, reason);
            }
            LogWarning($"ABORT LOGI cycle: {cmd.A?.ObjectName}->{cmd.B?.ObjectName} name={cmd.customNameFromPlanMission} reason={reason}");
            RemoveLogisticsCycle(cm, cmd);
            return;
        }
    }

    private static bool ApplySmallReservePropellant(PMMissionParameter pmp)
    {
        if (!ReturnFuelEnabled() || pmp?.CargoAll?.cargoFuel == null || pmp.SC == null || pmp.FlyCompany == null)
            return false;

        var fuelType = pmp.FuelNeedToStart;
        var scType = pmp.SC.GetTypeSpaceCraft();
        if (fuelType == null || scType == null || scType.SolarSC)
            return false;

        var minFuel = pmp.MINFuelCost > 0 ? pmp.MINFuelCost : pmp.AllFuelNeed;
        if (minFuel <= 0)
            return false;

        var tankCapacity = scType.GetFuelCapacity(pmp.FlyCompany) * Math.Max(1, pmp.SCCount);
        var targetPropellant = Math.Min(tankCapacity, Math.Ceiling(minFuel * ReservePropellantMultiplier));
        if (targetPropellant <= 0)
            return false;

        var currentTarget = pmp.CargoAll.cargoFuel.cargoMassPotencjal;
        if (currentTarget >= targetPropellant)
            return false;

        pmp.CargoAll.cargoFuel.objectInfo = pmp.Start;
        pmp.CargoAll.cargoFuel.resourceTypeType = EResourceTypeType.resorces;
        pmp.CargoAll.cargoFuel.resourceType = fuelType;
        pmp.CargoAll.cargoFuel.cargoMassPotencjal = targetPropellant;
        pmp.ReduceFuelToMinimum = false;
        if (pmp.CargoAll.cargoFuel.cargoMass > targetPropellant)
            pmp.CargoAll.cargoFuel.cargoMass = targetPropellant;

        LogVerbose($"RETURNFUEL reserve-propellant: route={pmp.Start?.ObjectName}->{pmp.Target?.ObjectName} ship={scType.NameRocketType} fuel={fuelType.ID} allFuel={pmp.AllFuelNeed:0.#} minFuel={pmp.MINFuelCost:0.#} targetPropellant={targetPropellant:0.#} tank={tankCapacity:0.#} normalCargo={pmp.CargoAll.CargoCurrent:0.#} reduceFuelToMinimum={pmp.ReduceFuelToMinimum}");
        return true;
    }

    private static List<Cargo> GetResourceCargoItems(CargoAll cargoAll)
    {
        var result = new List<Cargo>();
        if (cargoAll?.listCargo != null)
            result.AddRange(cargoAll.listCargo.Where(IsResourceCargo));
        if (cargoAll?.listCargoToOrbit != null)
            result.AddRange(cargoAll.listCargoToOrbit.Where(IsResourceCargo));
        return result;
    }

    private static bool IsResourceCargo(Cargo cargo)
    {
        return cargo != null
            && cargo.resourceTypeType == EResourceTypeType.resorces
            && cargo.resourceType != null;
    }

    private static void ApplyCargoScale(List<Cargo> cargoItems, double[] original, double scale)
    {
        for (var i = 0; i < cargoItems.Count; i++)
            cargoItems[i].cargoMass = Math.Floor(original[i] * scale);
    }

    private static string FormatCounts(Dictionary<string, int> counts)
    {
        if (counts == null || counts.Count == 0) return "none";
        return string.Join(",", counts.Select(kv => $"{kv.Key}:{kv.Value}"));
    }

    private static bool ReturnFuelEnabled()
    {
        return LogisticsMod.Plugin.ReturnFuelEnabled?.Value ?? true;
    }

    private static double ReturnFuelSafetyMultiplier()
    {
        var value = LogisticsMod.Plugin.ReturnFuelSafetyMultiplier?.Value ?? 1.5;
        return Math.Max(1, value);
    }

    private static bool ReserveCargoFirst()
    {
        return LogisticsMod.Plugin.ReturnFuelReserveCargoFirst?.Value ?? true;
    }

    private static double GetFuelStock(ObjectInfo oi, Company player, ResourceDefinition fuelType)
    {
        if (oi == null || player == null || fuelType == null) return 0;
        return oi.GetObjectInfoData(player)?.CheckResources(fuelType) ?? 0;
    }

    private static double GetProviderAvailableAfterMinimum(ObjectInfo providerOI, ResourceDefinition rd, Company player)
    {
        if (providerOI == null || rd == null || player == null) return 0;
        var data = Data.LogisticsNetwork.Get(providerOI);
        var oid = providerOI.GetObjectInfoData(player);
        if (data == null || oid == null) return 0;

        var available = oid.CheckResources(rd);
        var minKeep = data.providers
            .Where(p => p.isActive && p.ResourceDefinition == rd)
            .Sum(p => p.minimumKeep);
        var committed = GetCommittedStock(providerOI, rd);
        return Math.Max(0, available - minKeep - committed);
    }

    private static bool NetworkHasProviderForFuel(ResourceDefinition fuelType, Company player)
    {
        if (fuelType == null || player == null) return false;
        foreach (var oi in Data.LogisticsNetwork.GetAllObjects())
        {
            var data = Data.LogisticsNetwork.Get(oi);
            if (data == null) continue;
            if (!data.providers.Any(p => p.isActive && p.ResourceDefinition == fuelType)) continue;
            if (GetProviderAvailableAfterMinimum(oi, fuelType, player) > 0)
                return true;
        }
        return false;
    }

    private static double EstimatePrePlanReturnFuel(Spacecraft sc, Company player)
    {
        var type = sc?.spacecraftType;
        if (type == null || player == null || type.SolarSC) return 0;
        return Math.Ceiling(type.GetFuelCapacity(player) * PrePlanReturnFuelFractionOfTank);
    }

    private static Cargo FindResourceCargo(CargoAll cargoAll, ResourceDefinition rd)
    {
        if (cargoAll?.listCargo == null || rd == null) return null;
        return cargoAll.listCargo.FirstOrDefault(c => IsResourceCargo(c) && c.resourceType == rd);
    }

    private static void AddOrIncreaseResourceCargo(CargoAll cargoAll, ResourceDefinition rd, double amount)
    {
        if (cargoAll == null || rd == null || amount <= 0) return;
        var cargo = FindResourceCargo(cargoAll, rd);
        if (cargo == null)
        {
            cargo = new Cargo(cargoAll)
            {
                resourceType = rd,
                cargoMass = 0,
                resourceTypeType = EResourceTypeType.resorces
            };
            cargoAll.listCargo.Add(cargo);
        }
        cargo.cargoMass += amount;
    }

    private static double CargoAmountFor(CargoAll cargoAll, ResourceDefinition rd)
    {
        if (cargoAll == null || rd == null) return 0;
        return CargoAmountFor(cargoAll.listCargo, rd) + CargoAmountFor(cargoAll.listCargoToOrbit, rd);
    }

    private static bool CargoContainsResource(CargoAll cargoAll, ResourceDefinition rd)
    {
        return CargoAmountFor(cargoAll, rd) > 0;
    }

    private static bool CargoContainsResource(InfoCargoCyclicalMission cargoInfo, ResourceDefinition rd)
    {
        return cargoInfo?.Tab != null && cargoInfo.Tab.Any(tabRd => tabRd == rd);
    }

    private static double ReduceNonFuelCargo(CargoAll cargoAll, ResourceDefinition fuelType, double amountToRemove)
    {
        if (cargoAll?.listCargo == null || amountToRemove <= 0) return 0;
        double removed = 0;
        foreach (var cargo in cargoAll.listCargo.ToList())
        {
            if (removed >= amountToRemove) break;
            if (!IsResourceCargo(cargo) || cargo.resourceType == fuelType) continue;

            var take = Math.Min(cargo.cargoMass, amountToRemove - removed);
            cargo.cargoMass -= take;
            removed += take;
            if (cargo.cargoMass <= 0)
                cargoAll.listCargo.Remove(cargo);
        }
        return removed;
    }

    private static bool BuildCargoManifestWithReturnFuel(Data.LogisticsRequest req, ResourceDefinition rd,
        double amount, ObjectInfo requesterOI, ObjectInfo providerOI, Spacecraft sc, Company player,
        double capacity, LaunchVehicleType lvType, out CargoAll cargoAll, out double normalCargo, out double reserveFuelCargo,
        out ResourceDefinition blockedFuelType, out double blockedFuelShortfall, out bool waitingForFuelProbe)
    {
        cargoAll = CargoAll.CreateCargoEmpty();
        normalCargo = Math.Min(amount, capacity);
        reserveFuelCargo = 0;
        blockedFuelType = null;
        blockedFuelShortfall = 0;
        waitingForFuelProbe = false;

        if (rd == null || normalCargo <= 0 || capacity <= 0)
            return false;

        AddOrIncreaseResourceCargo(cargoAll, rd, normalCargo);
        if (!ShouldReserveReturnFuel(providerOI, requesterOI, sc, player))
        {
            normalCargo = CargoAmountFor(cargoAll, rd);
            LogVerbose($"RETURNFUEL reserve-skipped: route={providerOI?.ObjectName}->{requesterOI?.ObjectName} ship={sc?.GetSpacecraftName() ?? "null"} scType={sc?.spacecraftType?.NameRocketType ?? "null"} lv={lvType?.Name ?? "none"} reason=no-return-fuel-required manifest={FormatCargo(cargoAll)}");
            return cargoAll.CargoCurrent > 0;
        }

        if (!TryEstimateReturnFuelRequirement(providerOI, requesterOI, sc, player, cargoAll, lvType,
                out var waitingForProbe,
                out var fuelType, out var requiredReserve, out var destinationStock))
        {
            waitingForFuelProbe = waitingForProbe;
            if (waitingForFuelProbe)
            {
                LogVerbose($"RETURNFUEL estimate-pending: route={providerOI?.ObjectName}->{requesterOI?.ObjectName} ship={sc?.GetSpacecraftName() ?? "null"} scType={sc?.spacecraftType?.NameRocketType ?? "null"} rd={rd.ID} cargo={normalCargo:0.#} lv={lvType?.Name ?? "none"} manifest={FormatCargo(cargoAll)}");
                return false;
            }
            if (VerboseLoggingEnabled)
                LogWarning($"RETURNFUEL estimate-skipped: route={providerOI?.ObjectName}->{requesterOI?.ObjectName} ship={sc?.GetSpacecraftName() ?? "null"} scType={sc?.spacecraftType?.NameRocketType ?? "null"} rd={rd.ID} cargo={normalCargo:0.#} lv={lvType?.Name ?? "none"} manifest={FormatCargo(cargoAll)}");
            normalCargo = CargoAmountFor(cargoAll, rd);
            return cargoAll.CargoCurrent > 0;
        }

        var existingFuelCargo = CargoAmountFor(cargoAll, fuelType);
        var shortfall = Math.Max(0, requiredReserve - destinationStock - existingFuelCargo);
        if (shortfall <= 0)
        {
            normalCargo = CargoAmountFor(cargoAll, rd);
            LogVerbose($"RETURNFUEL trust-domestic-stockpile: route={providerOI?.ObjectName}->{requesterOI?.ObjectName} ship={sc?.GetSpacecraftName() ?? "null"} scType={sc?.spacecraftType?.NameRocketType} fuel={fuelType.ID} reserve={requiredReserve:0.#} destStock={destinationStock:0.#} existingFuelCargo={existingFuelCargo:0.#} manifest={FormatCargo(cargoAll)}");
            return cargoAll.CargoCurrent > 0;
        }

        var providerFuelAvailable = GetProviderAvailableAfterMinimum(providerOI, fuelType, player);
        var maxFuelCargo = capacity * MaxReturnFuelCargoDisplacementFraction;
        var maxAdditionalFuelCargo = Math.Max(0, maxFuelCargo - existingFuelCargo);
        var fuelToAdd = Math.Min(shortfall, Math.Min(providerFuelAvailable, maxAdditionalFuelCargo));
        LogVerbose($"RETURNFUEL manifest-calc: route={providerOI?.ObjectName}->{requesterOI?.ObjectName} ship={sc?.GetSpacecraftName() ?? "null"} fuel={fuelType.ID} reserve={requiredReserve:0.#} destStock={destinationStock:0.#} existingFuelCargo={existingFuelCargo:0.#} shortfall={shortfall:0.#} providerFuel={providerFuelAvailable:0.#} capacity={capacity:0.#} maxFuelCargo={maxFuelCargo:0.#} plannedFuelAdd={fuelToAdd:0.#} before={FormatCargo(cargoAll)}");
        double reduced = 0;

        var freeCapacity = Math.Max(0, capacity - cargoAll.CargoCurrent);
        if (fuelToAdd > freeCapacity)
        {
            var displacementNeeded = fuelToAdd - freeCapacity;
            reduced = ReduceNonFuelCargo(cargoAll, fuelType, displacementNeeded);
        }

        freeCapacity = Math.Max(0, capacity - cargoAll.CargoCurrent);
        fuelToAdd = Math.Min(fuelToAdd, freeCapacity);
        if (fuelToAdd > 0)
        {
            AddOrIncreaseResourceCargo(cargoAll, fuelType, fuelToAdd);
            reserveFuelCargo = fuelToAdd;
        }

        existingFuelCargo = CargoAmountFor(cargoAll, fuelType);
        var remainingShortfall = Math.Max(0, requiredReserve - destinationStock - existingFuelCargo);
        if (remainingShortfall > 0)
        {
            blockedFuelType = fuelType;
            blockedFuelShortfall = remainingShortfall;
            if (VerboseLoggingEnabled)
                LogWarning($"RETURNFUEL plan-shortfall: route={providerOI?.ObjectName}->{requesterOI?.ObjectName} ship={sc?.spacecraftType?.NameRocketType} fuel={fuelType.ID} reserve={requiredReserve:0.#} destStock={destinationStock:0.#} providerFuel={providerFuelAvailable:0.#} fuelAdded={reserveFuelCargo:0.#} shortfall={remainingShortfall:0.#} manifest={FormatCargo(cargoAll)}");
            return false;
        }

        normalCargo = CargoAmountFor(cargoAll, rd);
        if (normalCargo <= 0)
        {
            if (VerboseLoggingEnabled)
                LogWarning($"RETURNFUEL no-request-cargo-left: route={providerOI?.ObjectName}->{requesterOI?.ObjectName} rd={rd.ID} fuel={fuelType.ID} fuelAdded={reserveFuelCargo:0.#} reducedCargo={reduced:0.#} manifest={FormatCargo(cargoAll)}");
            return false;
        }
        LogVerbose($"RETURNFUEL ship-reserve-manifest: route={providerOI?.ObjectName}->{requesterOI?.ObjectName} ship={sc?.GetSpacecraftName() ?? "null"} scType={sc?.spacecraftType?.NameRocketType} fuel={fuelType.ID} reserve={requiredReserve:0.#} destStock={destinationStock:0.#} fuelAdded={reserveFuelCargo:0.#} reducedCargo={reduced:0.#} normalCargo={normalCargo:0.#} manifest={FormatCargo(cargoAll)}");
        return cargoAll.CargoCurrent > 0;
    }

    private static bool ShouldReserveReturnFuel(ObjectInfo providerOI, ObjectInfo requesterOI, Spacecraft sc, Company player)
    {
        var scType = sc?.GetTypeSpaceCraft();
        if (!ReturnFuelEnabled() || providerOI == null || requesterOI == null || sc == null || player == null || scType == null)
            return false;

        if (scType.SolarSC || scType.LowOrbitContainer || scType.MagneticCatapult)
            return false;

        if (scType.GetFuelCapacity(player) <= 0)
            return false;

        return true;
    }

    private static bool TryEstimateReturnFuelRequirement(ObjectInfo providerOI, ObjectInfo requesterOI,
        Spacecraft sc, Company player, CargoAll cargoAll, LaunchVehicleType lvType,
        out bool waitingForProbe,
        out ResourceDefinition fuelType, out double requiredReserve, out double destinationStock)
    {
        waitingForProbe = false;
        fuelType = null;
        requiredReserve = 0;
        destinationStock = 0;
        if (!ReturnFuelEnabled())
        {
            LogVerbose($"RETURNFUEL probe-skip: disabled route={providerOI?.ObjectName}->{requesterOI?.ObjectName}");
            return false;
        }

        if (providerOI == null || requesterOI == null || sc == null || player == null || cargoAll == null)
        {
            if (VerboseLoggingEnabled)
                LogWarning($"RETURNFUEL probe-skip: missing-input provider={providerOI?.ObjectName ?? "null"} requester={requesterOI?.ObjectName ?? "null"} ship={sc?.GetSpacecraftName() ?? "null"} player={player?.name ?? "null"} cargo={(cargoAll == null ? "null" : FormatCargo(cargoAll))}");
            return false;
        }

        var scType = sc.GetTypeSpaceCraft();
        if (scType == null || scType.SolarSC)
        {
            LogVerbose($"RETURNFUEL probe-skip: unsupported-ship route={providerOI.ObjectName}->{requesterOI.ObjectName} ship={sc.GetSpacecraftName()} scType={scType?.NameRocketType ?? "null"} solar={scType?.SolarSC.ToString() ?? "null"}");
            return false;
        }

        var probeKey = BuildReturnFuelProbeKey(providerOI, requesterOI, sc, player, lvType);
        if (!_returnFuelProbeCache.TryGetValue(probeKey, out var probe) || (!probe.Pending && !probe.Complete))
        {
            StartAsyncReturnFuelProbe(probeKey, providerOI, requesterOI, sc, player, lvType);
            waitingForProbe = true;
            return false;
        }

        if (probe.Pending)
        {
            waitingForProbe = true;
            return false;
        }

        if (probe.FuelType == null)
        {
            if (VerboseLoggingEnabled)
                LogWarning($"RETURNFUEL probe-no-fueltype-cached: returnRoute={requesterOI.ObjectName}->{providerOI.ObjectName} ship={sc.GetSpacecraftName()} scType={scType.NameRocketType} lv={lvType?.Name ?? "none"} result={probe.Result} failure={probe.FailureReason ?? "none"}");
            return false;
        }

        fuelType = probe.FuelType;
        requiredReserve = probe.RequiredReserve;
        destinationStock = GetFuelStock(requesterOI, player, fuelType);
        LogVerbose($"RETURNFUEL probe-cache-hit: outbound={providerOI.ObjectName}->{requesterOI.ObjectName} return={requesterOI.ObjectName}->{providerOI.ObjectName} ship={sc.GetSpacecraftName()} scType={scType.NameRocketType} lv={lvType?.Name ?? "none"} result={probe.Result} fuel={fuelType.ID} allFuel={probe.AllFuelNeed:0.#} minFuel={probe.MinFuelCost:0.#} fuelNeed={probe.FuelNeed:0.#} leftOver={probe.LeftOverFuel:0.#} reserve={requiredReserve:0.#} destStock={destinationStock:0.#} tank={scType.GetFuelCapacity(player):0.#} cargo={FormatCargo(cargoAll)}");
        if (requiredReserve <= 0)
        {
            var fallbackReserve = Math.Ceiling(scType.GetCargoCapacity(player) * MaxReturnFuelCargoDisplacementFraction);
            requiredReserve = fallbackReserve;
            destinationStock = 0;
            if (VerboseLoggingEnabled)
                LogWarning($"RETURNFUEL probe-zero-reserve-fallback: returnRoute={requesterOI.ObjectName}->{providerOI.ObjectName} ship={sc.GetSpacecraftName()} scType={scType.NameRocketType} lv={lvType?.Name ?? "none"} result={probe.Result} fuel={fuelType.ID} allFuel={probe.AllFuelNeed:0.#} minFuel={probe.MinFuelCost:0.#} fallbackReserve={fallbackReserve:0.#}");
        }
        return requiredReserve > 0;
    }

    private static string BuildReturnFuelProbeKey(ObjectInfo providerOI, ObjectInfo requesterOI,
        Spacecraft sc, Company player, LaunchVehicleType lvType)
    {
        var transfer = GetTransferTypeForSpacecraft(providerOI, sc);
        var scType = sc?.spacecraftType ?? sc?.GetTypeSpaceCraft();
        var fuelCapacity = scType == null || player == null ? 0 : scType.GetFuelCapacity(player);
        var cargoCapacity = scType == null || player == null ? 0 : scType.GetCargoCapacity(player);
        return string.Join("|",
            player?.ID ?? "company",
            providerOI?.id.ToString() ?? "provider",
            requesterOI?.id.ToString() ?? "requester",
            scType?.ID ?? scType?.NameRocketType ?? "sc",
            $"tank={Math.Round(fuelCapacity, 1)}",
            $"cargo={Math.Round(cargoCapacity, 1)}",
            lvType?.ID ?? lvType?.Name ?? "no-lv",
            transfer.ToString(),
            $"margin={ReturnFuelSafetyMultiplier():0.###}");
    }

    private static void StoreReturnFuelProbe(string key, ReturnFuelProbeState probe)
    {
        if (string.IsNullOrEmpty(key) || probe == null)
            return;

        if (!_returnFuelProbeCache.ContainsKey(key))
            _returnFuelProbeCacheOrder.Enqueue(key);
        _returnFuelProbeCache[key] = probe;

        var attempts = 0;
        while (_returnFuelProbeCache.Count > MaxReturnFuelProbeCacheEntries
            && _returnFuelProbeCacheOrder.Count > 0
            && attempts++ < MaxReturnFuelProbeCacheEntries * 2)
        {
            var evict = _returnFuelProbeCacheOrder.Dequeue();
            if (!_returnFuelProbeCache.TryGetValue(evict, out var existing))
                continue;
            if (existing.Pending)
            {
                _returnFuelProbeCacheOrder.Enqueue(evict);
                continue;
            }

            _returnFuelProbeCache.Remove(evict);
            LogVerbose($"RETURNFUEL probe-cache-evict: key={evict}");
        }
    }

    private static void StartAsyncReturnFuelProbe(string key, ObjectInfo providerOI, ObjectInfo requesterOI,
        Spacecraft sc, Company player, LaunchVehicleType lvType)
    {
        if (string.IsNullOrEmpty(key) || providerOI == null || requesterOI == null || sc == null || player == null)
            return;
        if (_returnFuelProbeCache.TryGetValue(key, out var existing) && existing.Pending)
            return;

        var scType = sc.GetTypeSpaceCraft();
        var fuelType = scType?.GetFuelType();
        var probe = new ReturnFuelProbeState
        {
            Pending = true,
            Complete = false,
            RequestedAt = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now,
            FuelType = fuelType
        };
        StoreReturnFuelProbe(key, probe);

        var probeCargo = CargoAll.CreateCargoEmpty();
        var probeSpacecraft = new PlannerSpacecraftInfo(sc, requesterOI);
        var pmp = new PMMissionParameter();
        pmp.SetCompany(player);
        pmp.SetTabDestination(requesterOI, providerOI);
        pmp.SetTabCargo(probeCargo);
        pmp.SetTabSC(probeSpacecraft);
        pmp.SetTabLV(new List<ILaunchVehicleInfo>(), 0);
        pmp.ForCyclicalMission = true;
        pmp.ReduceFuelToMinimum = false;
        pmp.TryFixWrongThrust = true;
        pmp.TrajectoryColor = Color.blue;
        pmp.SetMissionOrigin(MissionInfo.EMissionCreator.Other);
        var transfer = GetTransferTypeForSpacecraft(providerOI, sc);
        pmp.TryFastAsPossible = transfer == ETransferType.Fastest;
        pmp.ClickFastestButton = transfer == ETransferType.Fastest;
        ApplyCachedPrecalculateData(pmp);

        if (VerboseLoggingEnabled)
            Log($"RETURNFUEL async-probe-start: key={key} returnRoute={requesterOI.ObjectName}->{providerOI.ObjectName} ship={sc.GetSpacecraftName()} scType={scType?.NameRocketType ?? "null"} probePos={probeSpacecraft.GetActualPosition()?.ObjectName ?? "null"} transfer={transfer} fuel={fuelType?.ID ?? "null"}");
        MonoBehaviourSingleton<GameManager>.Instance.SetPMParameterForCodeJobSystem(pmp, () =>
        {
            var result = pmp.CheckCanPlanMission().planMissionResult;
            var callbackFuelType = pmp.FuelNeedToStart ?? fuelType;
            var planFuelNeed = Math.Max(pmp.AllFuelNeed, pmp.MINFuelCost);
            var tankCapacity = scType?.GetFuelCapacity(player) ?? 0;
            var estimatedReturnFuel = Math.Min(Math.Max(0, planFuelNeed), tankCapacity * Math.Max(1, pmp.SCCount));
            var requiredReserve = Math.Ceiling(estimatedReturnFuel * ReturnFuelSafetyMultiplier());

            probe.Pending = false;
            probe.Complete = true;
            probe.CompletedAt = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
            probe.FuelType = callbackFuelType;
            probe.FuelNeed = pmp.FuelNeed;
            probe.MinFuelCost = pmp.MINFuelCost;
            probe.AllFuelNeed = pmp.AllFuelNeed;
            probe.LeftOverFuel = pmp.LeftOverFuel;
            probe.RequiredReserve = requiredReserve;
            probe.Result = result;
            probe.FailureReason = result == PMMissionParameter.EPlanMissionResult.AllOk ? null : result.ToString();
            CachePrecalculateData(pmp, "return-fuel-probe");

            if (VerboseLoggingEnabled)
                Log($"RETURNFUEL async-probe-result: key={key} returnRoute={requesterOI.ObjectName}->{providerOI.ObjectName} ship={sc.GetSpacecraftName()} result={result} fuel={callbackFuelType?.ID ?? "null"} allFuel={pmp.AllFuelNeed:0.#} minFuel={pmp.MINFuelCost:0.#} fuelNeed={pmp.FuelNeed:0.#} leftOver={pmp.LeftOverFuel:0.#} reserve={requiredReserve:0.#} depart={pmp.DepartureTimeDate:yyyy-MM-dd} arrive={pmp.Arrival:yyyy-MM-dd}");
        });
    }

    private static void EnsureReturnFuelReserveFromPlan(PMMissionParameter pmp)
    {
        if (!ReturnFuelEnabled() || pmp?.CargoAll == null || pmp.SC == null || pmp.FlyCompany == null || pmp.Target == null)
            return;

        var fuelType = pmp.FuelNeedToStart;
        var scType = pmp.SC.GetTypeSpaceCraft();
        if (fuelType == null || scType == null || scType.SolarSC)
            return;

        var planFuelNeed = pmp.MINFuelCost > 0 ? Math.Min(pmp.AllFuelNeed, pmp.MINFuelCost) : pmp.AllFuelNeed;
        var estimatedReturnFuel = Math.Min(Math.Max(0, planFuelNeed), scType.GetFuelCapacity(pmp.FlyCompany) * Math.Max(1, pmp.SCCount));
        var requiredReserve = Math.Ceiling(estimatedReturnFuel * ReturnFuelSafetyMultiplier());
        var destinationStock = GetFuelStock(pmp.Target, pmp.FlyCompany, fuelType);
        var existingFuelCargo = CargoAmountFor(pmp.CargoAll, fuelType);
        var shortfall = Math.Max(0, requiredReserve - destinationStock - existingFuelCargo);

        if (shortfall <= 0)
        {
            LogVerbose($"RETURNFUEL trust-domestic-stockpile-plan: route={pmp.Start?.ObjectName}->{pmp.Target?.ObjectName} ship={scType.NameRocketType} fuel={fuelType.ID} allFuel={pmp.AllFuelNeed:0.#} minFuel={pmp.MINFuelCost:0.#} estimated={estimatedReturnFuel:0.#} reserve={requiredReserve:0.#} destStock={destinationStock:0.#} existingFuelCargo={existingFuelCargo:0.#} manifest={FormatCargo(pmp.CargoAll)}");
            return;
        }

        var capacity = scType.GetCargoCapacity(pmp.FlyCompany) * Math.Max(1, pmp.SCCount);
        var providerFuelAvailable = GetProviderAvailableAfterMinimum(pmp.Start, fuelType, pmp.FlyCompany);
        var fuelToAdd = Math.Min(shortfall, providerFuelAvailable);
        if (fuelToAdd <= 0)
        {
            if (VerboseLoggingEnabled)
                LogWarning($"RETURNFUEL plan-shortfall: route={pmp.Start?.ObjectName}->{pmp.Target?.ObjectName} ship={scType.NameRocketType} fuel={fuelType.ID} allFuel={pmp.AllFuelNeed:0.#} minFuel={pmp.MINFuelCost:0.#} estimated={estimatedReturnFuel:0.#} reserve={requiredReserve:0.#} destStock={destinationStock:0.#} shortfall={shortfall:0.#} providerFuel={providerFuelAvailable:0.#}");
            return;
        }

        var maxFuelCargo = capacity * MaxReturnFuelCargoDisplacementFraction;
        var maxAdditionalFuelCargo = Math.Max(0, maxFuelCargo - existingFuelCargo);
        fuelToAdd = Math.Min(fuelToAdd, maxAdditionalFuelCargo);
        if (fuelToAdd <= 0)
        {
            if (VerboseLoggingEnabled)
                LogWarning($"RETURNFUEL plan-cap-reached: route={pmp.Start?.ObjectName}->{pmp.Target?.ObjectName} ship={scType.NameRocketType} fuel={fuelType.ID} reserve={requiredReserve:0.#} maxFuelCargo={maxFuelCargo:0.#} existingFuelCargo={existingFuelCargo:0.#} manifest={FormatCargo(pmp.CargoAll)}");
            return;
        }

        var freeCapacity = Math.Max(0, capacity - pmp.CargoAll.CargoCurrent);
        double reduced = 0;
        if (fuelToAdd > freeCapacity)
        {
            var displacementNeeded = fuelToAdd - freeCapacity;
            reduced = ReduceNonFuelCargo(pmp.CargoAll, fuelType, displacementNeeded);
        }

        freeCapacity = Math.Max(0, capacity - pmp.CargoAll.CargoCurrent);
        fuelToAdd = Math.Min(fuelToAdd, freeCapacity);
        if (fuelToAdd <= 0)
        {
            if (VerboseLoggingEnabled)
                LogWarning($"RETURNFUEL plan-defer-no-room: route={pmp.Start?.ObjectName}->{pmp.Target?.ObjectName} ship={scType.NameRocketType} fuel={fuelType.ID} reserve={requiredReserve:0.#} capacity={capacity:0.#} reducedCargo={reduced:0.#} manifest={FormatCargo(pmp.CargoAll)}");
            return;
        }

        AddOrIncreaseResourceCargo(pmp.CargoAll, fuelType, fuelToAdd);
        LogVerbose($"RETURNFUEL ship-reserve-plan: route={pmp.Start?.ObjectName}->{pmp.Target?.ObjectName} ship={scType.NameRocketType} fuel={fuelType.ID} allFuel={pmp.AllFuelNeed:0.#} minFuel={pmp.MINFuelCost:0.#} estimated={estimatedReturnFuel:0.#} reserve={requiredReserve:0.#} destStock={destinationStock:0.#} shortfall={shortfall:0.#} maxFuelCargo={maxFuelCargo:0.#} fuelAdded={fuelToAdd:0.#} reducedCargo={reduced:0.#} manifest={FormatCargo(pmp.CargoAll)}");
    }

    private static string FormatCargo(CargoAll cargoAll)
    {
        if (cargoAll == null) return "none";
        var entries = new List<string>();
        if (cargoAll.listCargo != null)
        {
            foreach (var cargo in cargoAll.listCargo)
            {
                if (IsResourceCargo(cargo))
                    entries.Add($"{cargo.resourceType.ID}:{cargo.cargoMass:0.#}");
            }
        }
        if (cargoAll.listCargoToOrbit != null)
        {
            foreach (var cargo in cargoAll.listCargoToOrbit)
            {
                if (IsResourceCargo(cargo))
                    entries.Add($"{cargo.resourceType.ID}:orbit:{cargo.cargoMass:0.#}");
            }
        }
        return entries.Count == 0 ? "none" : string.Join(",", entries);
    }

    private static void MarkShipForReturn(Spacecraft sc, ObjectInfo home, ObjectInfo destination, ResourceDefinition rd)
    {
        if (sc == null || home == null || sc.ID < 0) return;
        _returnHomeByShipId[sc.ID] = new ReturnHomeState
        {
            Home = home,
            Destination = destination,
            Resource = rd,
            HasLeftHome = false
        };
        Log($"RETURNHOME mark: ship={sc.GetSpacecraftName()} id={sc.ID} home={home.ObjectName} destination={destination?.ObjectName ?? "null"} rd={rd?.ID ?? "null"}");
    }

    private static void TryReturnIdleLogisticsShips(Company player, PlannerSnapshot snapshot = null)
    {
        if (player == null || _returnHomeByShipId.Count == 0) return;

        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm == null) return;

        var ships = snapshot?.Ships
            ?? MonoBehaviourSingleton<ShipManager>.Instance?.ListAllSpaceShip
            ?? UnityEngine.Object.FindObjectsOfType<Spacecraft>().ToList();
        foreach (var sc in ships)
        {
            if (sc == null || sc.spacecraftType == null) continue;
            if (sc.GetCompany() != player) continue;
            if (!_returnHomeByShipId.TryGetValue(sc.ID, out var state)) continue;
            var home = state.Home;
            if (home == null)
            {
                _returnHomeByShipId.Remove(sc.ID);
                continue;
            }
            if (sc.CurrentPhase != Spacecraft.EPhase.None) continue;
            var current = sc.CurrentlyOnThisObject;
            if (current == null) continue;

            var currentPlanKey = $"{sc.ID}:{current.id}:{home.id}";
            if (state.ResolvedPlanKey != null && !state.ResolvedPlanKey.StartsWith(currentPlanKey, StringComparison.Ordinal))
                ResetReturnPlanState(state);
            if (state.PendingPlanKey != null && !state.PendingPlanKey.StartsWith(currentPlanKey, StringComparison.Ordinal))
                ResetReturnPlanState(state);

            if (current == home)
            {
                var attachedCycleAtHome = cm.GetCycleMission(sc);
                if (IsLogisticsReturnMission(attachedCycleAtHome))
                {
                    _cycleCreatedAt.Remove(attachedCycleAtHome);
                    _cyclePlanningFailures.Remove(attachedCycleAtHome);
                    RemoveLogisticsCycle(cm, attachedCycleAtHome);
                    Log($"RETURNHOME remove-complete-cycle: ship={sc.GetSpacecraftName()} id={sc.ID} cycle={attachedCycleAtHome.customNameFromPlanMission}");
                }

                if (state.HasLeftHome)
                {
                    ResetReturnPlanState(state);
                    ResetReturnFailureState(state);
                    _returnHomeByShipId.Remove(sc.ID);
                    Log($"RETURNHOME arrived: ship={sc.GetSpacecraftName()} id={sc.ID} home={home.ObjectName}");
                }
                continue;
            }

            var attachedCycle = cm.GetCycleMission(sc);
            if (attachedCycle != null)
            {
                if (IsLogisticsReturnMission(attachedCycle))
                {
                    if (IsCyclePastPlanningGrace(attachedCycle)
                        && !HasCycleActuallyLaunched(sc, attachedCycle, cm))
                    {
                        _cycleCreatedAt.Remove(attachedCycle);
                        _cyclePlanningFailures.Remove(attachedCycle);
                        RemoveLogisticsCycle(cm, attachedCycle);
                        SetReturnRetryCooldown(state, sc, current, home, $"return cycle did not launch within {EffectiveCyclePlanningGraceDays:0.#} days");
                        LogWarning($"RETURNHOME break-unlaunched-cycle: ship={sc.GetSpacecraftName()} id={sc.ID} current={current.ObjectName} home={home.ObjectName} cooldownDays={ReturnCycleBlockedCooldownDays:0.#} cycle={attachedCycle.customNameFromPlanMission}");
                    }
                    else
                    {
                        LogVerbose($"RETURNHOME wait-attached-return-cycle: ship={sc.GetSpacecraftName()} id={sc.ID} cycle={attachedCycle.customNameFromPlanMission}");
                    }
                    continue;
                }

                if (IsLogisticsDeliveryMission(attachedCycle))
                {
                    LogVerbose($"RETURNHOME wait-delivery-detach: ship={sc.GetSpacecraftName()} id={sc.ID} current={current.ObjectName} home={home.ObjectName} cycle={attachedCycle.customNameFromPlanMission}");
                }
                else
                {
                    LogVerbose($"RETURNHOME wait-attached-cycle: ship={sc.GetSpacecraftName()} id={sc.ID} cycle={attachedCycle.customNameFromPlanMission}");
                }
                continue;
            }

            if (IsReturnRetryCoolingDown(state, out var returnCooldownNote))
            {
                state.LastBlockedStatusNote = returnCooldownNote;
                LogVerbose($"RETURNHOME cooldown: ship={sc.GetSpacecraftName()} id={sc.ID} current={current.ObjectName} home={home.ObjectName} note={returnCooldownNote}");
                continue;
            }
            state.ReturnRetryAfter = DateTime.MinValue;
            state.ReturnRetryWallClockAfterUtc = DateTime.MinValue;

            state.HasLeftHome = true;
            if (TrySetupReturnCycle(sc, current, home, player, state, snapshot))
                continue;
        }
    }

    private static string GetReturnBlockedStatusNote(ObjectInfo requester, ResourceDefinition rd, Company player, PlannerSnapshot snapshot = null)
    {
        if (requester == null || rd == null || player == null || _returnHomeByShipId.Count == 0)
            return null;

        var ships = snapshot?.Ships
            ?? MonoBehaviourSingleton<ShipManager>.Instance?.ListAllSpaceShip
            ?? UnityEngine.Object.FindObjectsOfType<Spacecraft>().ToList();
        var returning = new List<string>();
        var blockedByReason = new Dictionary<string, List<string>>();

        foreach (var sc in ships)
        {
            if (sc == null || sc.spacecraftType == null) continue;
            if (sc.GetCompany() != player) continue;
            if (!_returnHomeByShipId.TryGetValue(sc.ID, out var state)) continue;
            if (state?.Destination != requester || state.Resource != rd) continue;
            if (sc.CurrentlyOnThisObject != requester) continue;

            var shipName = sc.GetSpacecraftName();
            var note = state.LastBlockedStatusNote;
            if (string.IsNullOrWhiteSpace(note))
                note = LogisticsStrings.AwaitingReturnFrom(sc.CurrentlyOnThisObject);

            if (note == LogisticsStrings.AwaitingReturnFrom(sc.CurrentlyOnThisObject))
            {
                returning.Add(shipName);
            }
            else
            {
                if (!blockedByReason.TryGetValue(note, out var list))
                {
                    list = new List<string>();
                    blockedByReason[note] = list;
                }
                list.Add(shipName);
            }
        }

        if (returning.Count == 0 && blockedByReason.Count == 0)
            return null;

        var parts = new List<string>();
        if (returning.Count > 0)
            parts.Add(FormatReturnShipGroup(returning.Count, "returning", returning));
        foreach (var kv in blockedByReason.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key))
            parts.Add(FormatReturnShipGroup(kv.Value.Count, $"blocked: {kv.Key}", kv.Value));
        return string.Join("; ", parts);
    }

    private static string FormatReturnShipGroup(int count, string label, List<string> details)
    {
        if (details == null || details.Count == 0)
            return $"{count} ship{(count == 1 ? "" : "s")} {label}";
        if (count == 1)
            return $"{details[0]} {label}";

        var shown = string.Join(", ", details.Take(3));
        var suffix = details.Count > 3 ? $", +{details.Count - 3} more" : "";
        return $"{count} ships {label}: {shown}{suffix}";
    }

    private static void ResetReturnPlanState(ReturnHomeState state)
    {
        if (state == null) return;
        state.PendingPlanKey = null;
        state.PendingPlanParameter = null;
        state.PendingPlanResult = null;
        state.ResolvedPlanKey = null;
        state.HasResolvedPlanResult = false;
        state.ResolvedFuelType = null;
        state.ResolvedFuelNeed = 0;
        state.ResolvedPlanDate = DateTime.MinValue;
    }

    private static void ResetReturnFailureState(ReturnHomeState state)
    {
        if (state == null) return;
        state.ConsecutiveReturnCycleFailures = 0;
        state.ReturnRetryAfter = DateTime.MinValue;
        state.ReturnRetryWallClockAfterUtc = DateTime.MinValue;
    }

    private static bool TrySetupReturnCycle(Spacecraft sc, ObjectInfo current, ObjectInfo home, Company player, ReturnHomeState state, PlannerSnapshot snapshot = null)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (sc == null || current == null || home == null || player == null || cm == null) return false;
        if (!ValidateSpacecraftForReturnCycleCreation(sc, player, "return-home-create"))
            return false;
        if (IsReturnRetryCoolingDown(state, out var returnCooldownNote))
        {
            state.LastBlockedStatusNote = returnCooldownNote;
            LogVerbose($"RETURNHOME skip-create-cooldown: ship={sc.GetSpacecraftName()} id={sc.ID} current={current.ObjectName} home={home.ObjectName} note={returnCooldownNote}");
            return false;
        }

        LaunchVehicleType returnLvType = null;
        LaunchVehicle returnLv = null;
        var scType = sc.spacecraftType;
        var currentIsOrbit = current.objectTypes == global::Data.EObjectTypes.Orbit;
        var needsLaunchVehicle = !currentIsOrbit && RequiresLaunchVehicleForSpacecraft(current, sc, player, 0);
        if (needsLaunchVehicle)
        {
            var launchSupport = GetAvailableLaunchSupport(current, player, snapshot);
            returnLv = launchSupport
                .Select(option => option.Vehicle)
                .FirstOrDefault(lv => lv != null
                    && lv.launchVehicleType != null
                    && lv.GetCompany() == player
                    && (!lv.launchTime.HasValue || lv.launchVehicleType.reusability > 0f));
            if (returnLv == null)
            {
                var details = string.Join("; ", launchSupport
                    .Where(option => option?.Vehicle != null)
                    .Take(6)
                    .Select(option =>
                    {
                        var lv = option.Vehicle;
                        var typeName = lv.launchVehicleType?.Name ?? "null";
                        var owner = lv.GetCompany()?.Definition?.ID ?? lv.company?.Definition?.ID ?? "null";
                        var atBody = lv.objectInfo?.ObjectName ?? "null";
                        var launched = lv.launchTime.HasValue ? "launched" : "ground";
                        var reusable = lv.launchVehicleType != null ? lv.launchVehicleType.reusability.ToString("0.##") : "null";
                        return $"{typeName}/owner={owner}/at={atBody}/{launched}/reuse={reusable}/support={option.Label}";
                    }));
                LogReturnBlockedOnce(
                    state,
                    $"ship={sc.GetSpacecraftName()} current={current.ObjectName} home={home.ObjectName} reason=current body requires LV and none is ready lvCount={launchSupport.Count} lv=[{details}]",
                    LogisticsStrings.WaitingForLaunchVehicleAt(current));
                return false;
            }
            returnLvType = returnLv.launchVehicleType;
        }
        else
        {
            LogVerbose($"RETURNHOME no-LV-needed: ship={sc.GetSpacecraftName()} current={current.ObjectName} home={home.ObjectName} main={player.mainObjectInfo?.ObjectName} needMoonLV={scType?.needLaunchVehicleToGoToMoon}");
        }

        var scList = new List<ISpacecraftInfo> { sc as ISpacecraftInfo };
        var cmdData = new CycleMissionsDataData
        {
            A = home, B = current, Company = player,
            CargoStart = ECargoStart.FlyWithWhatIsAvailable, CargoEnd = ECargoStart.FlyWithWhatIsAvailable,
            CargoAllStart = CargoAll.CreateCargoEmpty(), CargoAllEnd = CargoAll.CreateCargoEmpty(),
            LvTypeA = null, LvTypeB = returnLvType, TransferType = GetTransferTypeForSpacecraft(home, sc),
            Ends = EEnds.ThisManyTimes,
            EndsObjectThisManyTimes = 1,
            ListSC = scList
        };

        var cmd = new CycleMissionsData(cmdData);
        cmd.customNameFromPlanMission = BuildLogisticsMissionName(current, home, state.Resource, isReturn: true);
        _cycleCreatedAt[cmd] = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        ResetReturnPlanState(state);
        MarkReturnAttemptCooldown(state, sc, current, home, "return cycle handed to stock planner");
        state.LastBlockedReason = null;
        state.LastBlockedStatusNote = LogisticsStrings.AwaitingReturnFrom(current);
        state.LastBlockedDate = DateTime.MinValue;
        RegisterLogisticsCycleName(cmd);
        cm.AddCycleMission(sc, cmd, scList);

        HandOffCycleToStockPlanner(sc, cmd, "return-home");

        if (cm.GetCycleMission(sc) != cmd
            && sc.CurrentPhase == Spacecraft.EPhase.None
            && sc.CurrentlyOnThisObject == current)
        {
            _cycleCreatedAt.Remove(cmd);
            _cyclePlanningFailures.Remove(cmd);
            SetReturnRetryCooldown(state, sc, current, home, "return cycle detached before ship launched");
            LogWarning($"RETURNHOME detached-before-launch: ship={sc.GetSpacecraftName()} id={sc.ID} current={current.ObjectName} home={home.ObjectName}");
            return false;
        }

        Log($"RETURNHOME cycle: ship={sc.GetSpacecraftName()} id={sc.ID} {current.ObjectName}->{home.ObjectName} lv={(returnLvType?.Name ?? "none")}");
        return true;
    }

    private static void LogReturnBlockedOnce(ReturnHomeState state, string reason, string statusNote = null)
    {
        if (state == null)
        {
            LogWarning($"RETURNHOME blocked: {reason}");
            return;
        }

        var currentDate = (MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now).Date;
        if (state.LastBlockedReason == reason && state.LastBlockedDate == currentDate)
            return;

        state.LastBlockedReason = reason;
        state.LastBlockedStatusNote = statusNote;
        state.LastBlockedDate = currentDate;
        LogWarning($"RETURNHOME blocked: {reason}");
    }

    private static bool IsOrbitOf(ObjectInfo orbit, ObjectInfo body)
    {
        if (orbit == null || body == null) return false;
        if (body.LowOrbitCustom != null && body.LowOrbitCustom.GetObjectInfo() == orbit)
            return true;
        return orbit.objectTypes == global::Data.EObjectTypes.Orbit && orbit.parentObjectInfo == body;
    }

    private static string TryCreateDeliveries(Data.LogisticsRequest req, ObjectInfo requester,
        ResourceDefinition rd, double remaining, Company player, PlannerSnapshot snapshot = null)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm == null) return null;

        if (HasBlockedPlanningRetryCooldown(requester, rd, out var cooldownStatus))
            return cooldownStatus;

        var scActive = snapshot?.ScActive ?? new Dictionary<string, int>();
        var lvActive = snapshot?.LvActive ?? new Dictionary<string, int>();
        LogVerbose($"DISPATCH begin: target={requester?.ObjectName} rd={rd.ID} remaining={remaining:0.#} activeSC={FormatCounts(scActive)} activeLV={FormatCounts(lvActive)}");
        var bestBlocker = new PlannerBlocker();
        var candidates = BuildRouteCandidates(req, requester, rd, remaining, player, scActive, lvActive, bestBlocker, snapshot);
        if (candidates.Count == 0)
        {
            var allObjects = snapshot?.Objects ?? Data.LogisticsNetwork.GetAllObjects();
            if (!allObjects.Any(oi =>
            {
                var data = Data.LogisticsNetwork.Get(oi);
                return oi != requester && data != null && data.providers.Any(p => p.isActive && p.ResourceDefinition == rd);
            }))
                LogVerbose($"DISPATCH none: target={requester?.ObjectName} rd={rd.ID} reason=no active provider with matching resource");
            else
                LogVerbose($"DISPATCH none: target={requester?.ObjectName} rd={rd.ID} reason={bestBlocker.Reason ?? "no usable ship/LV/provider this tick"}");
            MarkBlockedPlanningRetryCooldown(requester, rd, bestBlocker.Reason ?? "no usable ship/LV/provider this tick");
            return bestBlocker.Reason;
        }

        var orderedCandidates = candidates
            .OrderBy(c => c.Tier)
            .ThenBy(c => c.UsesLV ? 1 : 0)
            .ThenBy(c => c.HopCount)
            .ThenByDescending(c => c.Available)
            .ThenBy(c => c.EffectiveSource?.id ?? int.MaxValue)
            .ThenBy(c => c.Provider?.id ?? int.MaxValue)
            .ToList();

        if (VerboseLoggingEnabled)
            LogBepInEx($"ROUTE request: target={requester?.ObjectName} rd={rd.ID} remaining={remaining:0.#} candidates={orderedCandidates.Count}");
        foreach (var candidate in orderedCandidates)
        {
            if (VerboseLoggingEnabled)
                LogBepInEx($"ROUTE candidate: rd={rd.ID} kind={candidate.Kind} label={candidate.Label} score={candidate.Tier} usesLV={candidate.UsesLV} hops={candidate.HopCount} available={candidate.Available:0.#} amount={candidate.Amount:0.#} detail={candidate.ScoreBreakdown}");
            if (ExecuteRouteCandidate(candidate, req, requester, rd, player, snapshot))
                return null;
        }
        var executeReason = bestBlocker.Reason ?? "all candidates failed during execution";
        if (VerboseLoggingEnabled)
            LogBepInEx($"ROUTE no-execute: target={requester?.ObjectName} rd={rd.ID} reason={executeReason}");
        MarkBlockedPlanningRetryCooldown(requester, rd, executeReason);
        return executeReason;
    }

    private static List<RouteCandidate> BuildRouteCandidates(Data.LogisticsRequest req, ObjectInfo requester,
        ResourceDefinition rd, double remaining, Company player,
        Dictionary<string, int> scActive, Dictionary<string, int> lvActive, PlannerBlocker bestBlocker,
        PlannerSnapshot snapshot = null)
    {
        var result = new List<RouteCandidate>();
        foreach (var providerOI in snapshot?.Objects ?? Data.LogisticsNetwork.GetAllObjects())
        {
            if (providerOI == requester) continue;

            var provData = Data.LogisticsNetwork.Get(providerOI);
            if (provData == null) continue;
            if (!provData.providers.Any(p => p.isActive && p.ResourceDefinition == rd))
                continue;

            var available = GetProviderAvailableAfterMinimum(providerOI, rd, player);
            LogVerbose($"DISPATCH provider: provider={providerOI?.ObjectName} rd={rd.ID} availableAfterMin={available:0.#}");
            if (available <= 0)
            {
                var noSurplusTier = GetRouteTier(providerOI, requester);
                var noSurplusDetail = DescribeRouteScore(providerOI, requester, noSurplusTier);
                var noSurplusReason = LogisticsStrings.NoSurplusAt(rd, providerOI);
                if (VerboseLoggingEnabled)
                    LogBepInEx($"ROUTE provider-skip: provider={providerOI?.ObjectName} rd={rd.ID} score={noSurplusTier} detail={noSurplusDetail} reason={noSurplusReason}");
                TrackPlannerBlocker(bestBlocker, noSurplusTier, 6, noSurplusReason);
                continue;
            }

            AddDirectRouteCandidates(result, providerOI, requester, rd, remaining, available, player, scActive, lvActive, bestBlocker, snapshot);
            AddStagedRouteCandidate(result, providerOI, requester, rd, remaining, available, player, scActive, lvActive, bestBlocker, snapshot);
        }
        return result;
    }

    private static void AddDirectRouteCandidates(List<RouteCandidate> result, ObjectInfo providerOI,
        ObjectInfo requester, ResourceDefinition rd, double remaining, double available, Company player,
        Dictionary<string, int> scActive, Dictionary<string, int> lvActive, PlannerBlocker bestBlocker, PlannerSnapshot snapshot = null)
    {
        var amount = Math.Min(available, remaining);
        if (amount <= 0) return;
        var routeTier = GetRouteTier(providerOI, requester);
        var routeDetail = VerboseLoggingEnabled ? DescribeRouteScore(providerOI, requester, routeTier) : null;

        var isSurfaceToOwnOrbit = IsOrbitOf(requester, providerOI);
        if (providerOI.NeedVehicleToLaunch() && !isSurfaceToOwnOrbit)
        {
            var directSurfaceShip = FindBestIdleSpacecraft(providerOI, player, scActive,
                requireNonContainer: true, out var directSurfaceShipReason);
            var directSurfaceCapacity = directSurfaceShip?.spacecraftType?.GetCargoCapacity(player) ?? 0;
            var directSurfaceAmount = Math.Min(amount, directSurfaceCapacity);
            if (directSurfaceShip != null && directSurfaceCapacity > 0)
                directSurfaceAmount = Math.Min(directSurfaceAmount, GetSelfLaunchPayloadLimit(providerOI, directSurfaceShip, player));
            if (directSurfaceShip != null
                && directSurfaceAmount > 0
                && !RequiresLaunchVehicleForSpacecraft(providerOI, directSurfaceShip, player, directSurfaceAmount))
            {
                result.Add(new RouteCandidate
                {
                    Kind = RouteKind.DirectSpacecraft,
                    Provider = providerOI,
                    EffectiveSource = providerOI,
                    Spacecraft = directSurfaceShip,
                    Amount = directSurfaceAmount,
                    Available = available,
                    Tier = routeTier,
                    HopCount = 1,
                    UsesLV = false,
                    Label = $"{providerOI.ObjectName} -> {requester.ObjectName}",
                    ScoreBreakdown = routeDetail + $";surfaceBypassLV=true;selfLaunchLimit={directSurfaceAmount:0.#}"
                });
                return;
            }
            if (directSurfaceShip == null && !string.IsNullOrEmpty(directSurfaceShipReason))
                LogVerbose($"DISPATCH no-direct-surface-bypass: provider={providerOI.ObjectName} requester={requester.ObjectName} reason={directSurfaceShipReason}");
        }

        if (!providerOI.NeedVehicleToLaunch())
        {
            var sc = FindBestIdleSpacecraft(providerOI, player, scActive, requireNonContainer: false,
                out var spacecraftReason);
            var capacity = sc?.spacecraftType?.GetCargoCapacity(player) ?? 0;
            if (sc != null && capacity > 0)
            {
                result.Add(new RouteCandidate
                {
                    Kind = RouteKind.DirectSpacecraft,
                    Provider = providerOI,
                    EffectiveSource = providerOI,
                    Spacecraft = sc,
                    Amount = Math.Min(amount, capacity),
                    Available = available,
                    Tier = routeTier,
                    HopCount = 1,
                    UsesLV = false,
                    Label = $"{providerOI.ObjectName} -> {requester.ObjectName}",
                    ScoreBreakdown = routeDetail
                });
            }
            else if (!string.IsNullOrEmpty(spacecraftReason))
            {
                if (VerboseLoggingEnabled)
                    LogBepInEx($"ROUTE candidate-blocked: rd={rd.ID} kind={RouteKind.DirectSpacecraft} label={providerOI.ObjectName} -> {requester.ObjectName} score={routeTier} detail={routeDetail} reason={spacecraftReason}");
                TrackPlannerBlocker(bestBlocker, routeTier, 3, spacecraftReason);
            }
            return;
        }

        if (!TryFindSurfaceLaunch(providerOI, requester, player, scActive, lvActive, requireContainerOnly: isSurfaceToOwnOrbit,
                requireRegularSC: !isSurfaceToOwnOrbit, out var lvType, out var carrier, out var launchReason, out var launchSupportDetail, out var launchSupportAdjustment, snapshot))
        {
            if (VerboseLoggingEnabled)
                LogBepInEx($"ROUTE candidate-blocked: rd={rd.ID} kind={RouteKind.DirectSurfaceLaunch} label={providerOI.ObjectName} -> {requester.ObjectName} score={routeTier} detail={routeDetail} reason={launchReason}");
            TrackPlannerBlocker(bestBlocker, routeTier, 2, launchReason);
            return;
        }

        routeTier += launchSupportAdjustment;
        routeDetail = VerboseLoggingEnabled ? DescribeRouteScore(providerOI, requester, routeTier, launchSupportAdjustment) : null;

        var scCapacity = carrier?.spacecraftType?.GetCargoCapacity(player) ?? 0;
        if (scCapacity <= 0)
        {
            var capacityReason = LogisticsStrings.NoCargoCapacityFrom(providerOI);
            if (VerboseLoggingEnabled)
                LogBepInEx($"ROUTE candidate-blocked: rd={rd.ID} kind={RouteKind.DirectSurfaceLaunch} label={providerOI.ObjectName} -> {requester.ObjectName} score={routeTier} detail={routeDetail} reason={capacityReason}");
            TrackPlannerBlocker(bestBlocker, routeTier, 4, capacityReason);
            return;
        }

        result.Add(new RouteCandidate
        {
            Kind = RouteKind.DirectSurfaceLaunch,
            Provider = providerOI,
            EffectiveSource = providerOI,
            LaunchVehicleType = lvType,
            Spacecraft = carrier,
            Amount = Math.Min(amount, scCapacity),
            Available = available,
            Tier = routeTier,
            HopCount = 1,
            UsesLV = true,
            Label = $"{providerOI.ObjectName} -> {requester.ObjectName}",
            ScoreBreakdown = string.IsNullOrWhiteSpace(launchSupportDetail) ? routeDetail : $"{routeDetail};launchSupport={launchSupportDetail}"
        });
    }

    private static void AddStagedRouteCandidate(List<RouteCandidate> result, ObjectInfo providerOI,
        ObjectInfo requester, ResourceDefinition rd, double remaining, double available, Company player,
        Dictionary<string, int> scActive, Dictionary<string, int> lvActive, PlannerBlocker bestBlocker, PlannerSnapshot snapshot = null)
    {
        if (!providerOI.NeedVehicleToLaunch()) return;
        if (requester == null || providerOI == null) return;
        if (IsOrbitOf(requester, providerOI)) return;

        var sourceOrbit = providerOI.LowOrbitCustom?.GetObjectInfo();
        if (sourceOrbit == null)
        {
            var noOrbitReason = LogisticsStrings.NoSourceOrbitAt(providerOI);
            if (VerboseLoggingEnabled)
                LogBepInEx($"ROUTE candidate-blocked: rd={rd.ID} kind={RouteKind.StageSourceSurfaceToOrbit} label={providerOI.ObjectName} -> [orbit missing] -> {requester.ObjectName} score=5 detail=no-source-orbit reason={noOrbitReason}");
            TrackPlannerBlocker(bestBlocker, 5, 5, noOrbitReason);
            return;
        }
        var routeTier = GetRouteTier(sourceOrbit, requester);
        var routeDetail = VerboseLoggingEnabled ? DescribeRouteScore(sourceOrbit, requester, routeTier) : null;

        if (!TryFindSurfaceLaunch(providerOI, sourceOrbit, player, scActive, lvActive, requireContainerOnly: true,
                requireRegularSC: false, out var stageLvType, out var stageCarrier, out var stageReason, out var stageSupportDetail, out var stageSupportAdjustment, snapshot))
        {
            if (VerboseLoggingEnabled)
                LogBepInEx($"ROUTE candidate-blocked: rd={rd.ID} kind={RouteKind.StageSourceSurfaceToOrbit} label={providerOI.ObjectName} -> {sourceOrbit.ObjectName} -> {requester.ObjectName} score={routeTier} detail={routeDetail} reason={stageReason}");
            TrackPlannerBlocker(bestBlocker, routeTier, 2, stageReason);
            return;
        }

        routeTier += stageSupportAdjustment;
        routeDetail = VerboseLoggingEnabled ? DescribeRouteScore(sourceOrbit, requester, routeTier, stageSupportAdjustment) : null;

        var finalCarrier = FindBestIdleSpacecraft(sourceOrbit, player, scActive, requireNonContainer: true,
            out var finalCarrierReason);
        var stageCapacity = stageCarrier?.spacecraftType?.GetCargoCapacity(player) ?? 0;
        var finalCapacity = finalCarrier?.spacecraftType?.GetCargoCapacity(player) ?? 0;
        if (stageCapacity <= 0)
        {
            var stageCapacityReason = LogisticsStrings.NoOrbitalPayloadCapacityFrom(providerOI);
            if (VerboseLoggingEnabled)
                LogBepInEx($"ROUTE candidate-blocked: rd={rd.ID} kind={RouteKind.StageSourceSurfaceToOrbit} label={providerOI.ObjectName} -> {sourceOrbit.ObjectName} -> {requester.ObjectName} score={routeTier} detail={routeDetail} reason={stageCapacityReason}");
            TrackPlannerBlocker(bestBlocker, routeTier, 4, stageCapacityReason);
            return;
        }
        if (finalCapacity <= 0)
        {
            var finalReason = finalCarrierReason ?? LogisticsStrings.NoSpacecraftAvailableAt(sourceOrbit);
            if (VerboseLoggingEnabled)
                LogBepInEx($"ROUTE candidate-blocked: rd={rd.ID} kind={RouteKind.StageSourceSurfaceToOrbit} label={providerOI.ObjectName} -> {sourceOrbit.ObjectName} -> {requester.ObjectName} score={routeTier} detail={routeDetail} reason={finalReason}");
            var priority = IsNoLogisticsDataReason(finalReason) ? 9 : 3;
            TrackPlannerBlocker(bestBlocker, routeTier, priority, finalReason);
            return;
        }

        var amount = Math.Min(Math.Min(available, remaining), Math.Min(stageCapacity, finalCapacity));
        if (amount <= 0) return;

        result.Add(new RouteCandidate
        {
            Kind = RouteKind.StageSourceSurfaceToOrbit,
            Provider = providerOI,
            EffectiveSource = sourceOrbit,
            StageOrbit = sourceOrbit,
            StageCarrier = stageCarrier,
            FinalCarrier = finalCarrier,
            LaunchVehicleType = stageLvType,
            Amount = amount,
            Available = available,
            Tier = routeTier,
            HopCount = 2,
            UsesLV = true,
            Label = $"{providerOI.ObjectName} -> {sourceOrbit.ObjectName} -> {requester.ObjectName}",
            ScoreBreakdown = string.IsNullOrWhiteSpace(stageSupportDetail) ? routeDetail : $"{routeDetail};launchSupport={stageSupportDetail}"
        });
    }

    private static bool ExecuteRouteCandidate(RouteCandidate candidate, Data.LogisticsRequest req,
        ObjectInfo requester, ResourceDefinition rd, Company player, PlannerSnapshot snapshot = null)
    {
        if (candidate == null || req == null || requester == null || rd == null || player == null)
            return false;

        switch (candidate.Kind)
        {
            case RouteKind.DirectSpacecraft:
                if (SetupDirectCycleMission(req, candidate.Spacecraft, rd, candidate.Amount, requester, candidate.Provider,
                        out var blockedFuelType, out var blockedFuelShortfall))
                {
                    RecordDispatchInSnapshot(snapshot, candidate.Spacecraft, null);
                    ClearRelayState(req);
                    if (VerboseLoggingEnabled)
                    {
                        Log($"PROC ranked: {rd.ID} x{candidate.Amount:0.#} {candidate.Label} kind={candidate.Kind}");
                        LogBepInEx($"ROUTE chosen: rd={rd.ID} kind={candidate.Kind} label={candidate.Label} score={candidate.Tier} detail={candidate.ScoreBreakdown}");
                    }
                    return true;
                }
                if (IsWaitingForReturnFuelProbe(req))
                    return true;
                return TryCreateFuelBootstrapDelivery(req, requester, rd, blockedFuelType, blockedFuelShortfall, player);

            case RouteKind.DirectSurfaceLaunch:
                if (candidate.Spacecraft == null && IsOrbitOf(requester, candidate.Provider))
                    candidate.Spacecraft = GetCyclicalOrbitalContainer(player);
                if (candidate.Spacecraft == null)
                    return false;
                if (SetupCycleMission(req, candidate.Spacecraft, rd, candidate.Amount, requester, candidate.Provider,
                        candidate.LaunchVehicleType, out blockedFuelType, out blockedFuelShortfall))
                {
                    RecordDispatchInSnapshot(snapshot, candidate.Spacecraft, candidate.LaunchVehicleType);
                    ClearRelayState(req);
                    if (VerboseLoggingEnabled)
                    {
                        Log($"PROC ranked: {rd.ID} x{candidate.Amount:0.#} {candidate.Label} kind={candidate.Kind}");
                        LogBepInEx($"ROUTE chosen: rd={rd.ID} kind={candidate.Kind} label={candidate.Label} score={candidate.Tier} detail={candidate.ScoreBreakdown}");
                    }
                    return true;
                }
                if (IsWaitingForReturnFuelProbe(req))
                    return true;
                return TryCreateFuelBootstrapDelivery(req, requester, rd, blockedFuelType, blockedFuelShortfall, player);

            case RouteKind.StageSourceSurfaceToOrbit:
                if (candidate.StageCarrier == null)
                    candidate.StageCarrier = GetCyclicalOrbitalContainer(player);
                if (candidate.StageCarrier == null)
                    return false;
                SetRelayState(req, Data.RelayStage.WaitingForSourceOrbitStock, candidate.Provider, candidate.StageOrbit, requester);
                if (SetupCycleMission(req, candidate.StageCarrier, rd, candidate.Amount, candidate.StageOrbit, candidate.Provider,
                        candidate.LaunchVehicleType, out blockedFuelType, out blockedFuelShortfall,
                        accountingTargetOI: requester, pendingTargetOI: candidate.StageOrbit))
                {
                    RecordDispatchInSnapshot(snapshot, candidate.StageCarrier, candidate.LaunchVehicleType);
                    req.status = Data.LogisticsRequestStatus.InProgress;
                    req.statusNote = LogisticsStrings.StagingTo(candidate.StageOrbit);
                    if (VerboseLoggingEnabled)
                    {
                        Log($"PROC ranked: {rd.ID} x{candidate.Amount:0.#} {candidate.Label} kind={candidate.Kind}");
                        LogBepInEx($"ROUTE chosen: rd={rd.ID} kind={candidate.Kind} label={candidate.Label} score={candidate.Tier} detail={candidate.ScoreBreakdown}");
                    }
                    return true;
                }

                if (IsWaitingForReturnFuelProbe(req))
                    return true;
                ClearRelayState(req);
                return TryCreateFuelBootstrapDelivery(req, requester, rd, blockedFuelType, blockedFuelShortfall, player);
        }

        return false;
    }

    private static bool TryCreateRelayFinalDelivery(Data.LogisticsRequest req, ObjectInfo requester,
        ObjectInfo sourceOrbit, ResourceDefinition rd, double remaining, Company player, PlannerSnapshot snapshot = null)
    {
        if (HasRoutePlanningLock(sourceOrbit, requester, rd, player, out var lockStatus))
        {
            req.status = Data.LogisticsRequestStatus.InProgress;
            req.statusNote = lockStatus;
            return true;
        }

        var scActive = snapshot?.ScActive ?? new Dictionary<string, int>();
        var carrier = FindBestIdleSpacecraft(sourceOrbit, player, scActive, requireNonContainer: true, out _, snapshot);
        var cap = carrier?.spacecraftType?.GetCargoCapacity(player) ?? 0;
        if (carrier == null || cap <= 0)
            return false;

        var amount = Math.Min(remaining, cap);
        if (amount <= 0)
            return false;

        if (!SetupDirectCycleMission(req, carrier, rd, amount, requester, sourceOrbit,
                out var blockedFuelType, out var blockedFuelShortfall,
                lvTypeA: null, accountingTargetOI: requester, pendingTargetOI: requester))
        {
            if (IsWaitingForReturnFuelProbe(req))
                return true;
            return TryCreateFuelBootstrapDelivery(req, sourceOrbit, rd, blockedFuelType, blockedFuelShortfall, player);
        }

        RecordDispatchInSnapshot(snapshot, carrier, null);
        req.relayStage = Data.RelayStage.WaitingForFinalLeg;
        req.status = Data.LogisticsRequestStatus.InProgress;
        req.statusNote = LogisticsStrings.ShippingFrom(sourceOrbit);
        Log($"RELAY final-leg-dispatch: rd={rd.ID} sourceOrbit={sourceOrbit.ObjectName} target={requester.ObjectName} amount={amount:0.#}");
        return true;
    }

    private static bool IsWaitingForReturnFuelProbe(Data.LogisticsRequest req)
    {
        return req != null
            && req.status == Data.LogisticsRequestStatus.InProgress
            && string.Equals(req.statusNote, "Calculating return fuel reserve", StringComparison.Ordinal);
    }

    private static Spacecraft FindBestIdleSpacecraft(ObjectInfo location, Company player,
        Dictionary<string, int> scActive, bool requireNonContainer, out string reason, PlannerSnapshot snapshot = null)
    {
        reason = null;
        if (location == null || player == null) return null;
        var data = Data.LogisticsNetwork.Get(location);
        if (data == null)
        {
            reason = LogisticsStrings.NoLogisticsDataAt(location);
            return null;
        }

        var quotas = data.spacecraftQuota.Where(q => q.count > 0).ToList();
        if (quotas.Count == 0)
        {
            reason = LogisticsStrings.NoSpacecraftQuotaAt(location);
            return null;
        }

        var ships = snapshot?.Ships
            ?? MonoBehaviourSingleton<ShipManager>.Instance?.ListAllSpaceShip
            ?? UnityEngine.Object.FindObjectsOfType<Spacecraft>().ToList();
        var allShips = ships
            .Where(sc => sc != null && sc.spacecraftType != null
                && sc.GetCompany() == player
                && sc.CurrentlyOnThisObject == location
                && (!requireNonContainer || !sc.spacecraftType.LowOrbitContainer))
            .ToList();
        if (allShips.Count == 0)
        {
            reason = LogisticsStrings.NoSpacecraftPresentAt(location);
            return null;
        }

        var committedIds = snapshot?.CommittedShipIds;
        var quotaExhausted = false;
        var matchingPresent = false;
        var idleMatchingPresent = false;

        foreach (var quota in quotas)
        {
            var matchingShips = allShips
                .Where(sc => Data.LogisticsNetwork.QuotaMatches(quota, sc.spacecraftType.ID, sc.spacecraftType.NameRocketType ?? "SC"))
                .ToList();
            if (matchingShips.Count == 0)
                continue;

            matchingPresent = true;
            var committedAtLocation = matchingShips.Count(sc =>
                IsSpacecraftAlreadyCommitted(sc, player, out _, committedShipIds: committedIds));
            var canUse = quota.count - committedAtLocation;
            if (canUse <= 0)
            {
                quotaExhausted = true;
                continue;
            }

            var idleShips = matchingShips
                .Where(sc => IsSpacecraftAvailableForLogistics(sc, player, committedIds))
                .OrderByDescending(sc => sc.spacecraftType.GetCargoCapacity(player))
                .ToList();
            if (idleShips.Count == 0)
                continue;

            idleMatchingPresent = true;
            var ship = idleShips.FirstOrDefault();
            if (ship != null)
                return ship;
        }

        if (!matchingPresent)
            reason = LogisticsStrings.NoMatchingSpacecraftAt(location);
        else if (quotaExhausted)
            reason = LogisticsStrings.AllSpacecraftQuotaInUseAt(location);
        else if (!idleMatchingPresent)
            reason = LogisticsStrings.NoIdleSpacecraftAt(location);
        else
            reason = LogisticsStrings.NoSpacecraftAvailableAt(location);
        return null;
    }

    private static bool TryFindSurfaceLaunch(ObjectInfo providerOI, ObjectInfo targetOI, Company player,
        Dictionary<string, int> scActive, Dictionary<string, int> lvActive, bool requireContainerOnly, bool requireRegularSC,
        out LaunchVehicleType lvType, out Spacecraft carrier, out string reason, out string supportDetail,
        out int supportTierAdjustment, PlannerSnapshot snapshot = null)
    {
        lvType = null;
        carrier = null;
        reason = null;
        supportDetail = null;
        supportTierAdjustment = 0;
        if (providerOI == null || player == null || !providerOI.NeedVehicleToLaunch())
        {
            reason = providerOI == null ? LogisticsStrings.NoProviderSelected() : LogisticsStrings.NoSurfaceLaunchPathFrom(providerOI);
            return false;
        }

        var provData = Data.LogisticsNetwork.Get(providerOI);
        if (provData == null)
        {
            reason = LogisticsStrings.NoLogisticsDataAt(providerOI);
            return false;
        }

        var lvQuotas = provData.launchVehicleQuota.Where(q => q.count > 0).ToList();
        if (lvQuotas.Count == 0)
        {
            reason = LogisticsStrings.NoLvQuotaAt(providerOI, DescribeAvailableLaunchSupport(providerOI, player, snapshot));
            return false;
        }

        var allReadyLV = GetAvailableLaunchSupport(providerOI, player, snapshot)
            .Where(option => option?.Vehicle != null
                && option.Type != null
                && option.Vehicle.GetCompany() == player
                && option.Vehicle.objectInfo == providerOI
                && option.Vehicle.IsReadyToLaunchReusable())
            .ToList();
        if (allReadyLV.Count == 0)
        {
            reason = LogisticsStrings.NoReadyLvAt(providerOI, DescribeAvailableLaunchSupport(providerOI, player, snapshot));
            return false;
        }

        var matchingReadyLV = allReadyLV
            .Where(option => lvQuotas.Any(q => Data.LogisticsNetwork.QuotaMatches(q, option.Type.ID, option.Type.Name ?? "LV")))
            .ToList();
        if (matchingReadyLV.Count == 0)
        {
            reason = LogisticsStrings.NoMatchingLvQuotaAt(providerOI, DescribeAvailableLaunchSupport(providerOI, player, snapshot));
            return false;
        }

        var quotaExhausted = false;

        var availableLV = matchingReadyLV
            .Where(option =>
            {
                var allowed = lvQuotas
                    .Where(q => Data.LogisticsNetwork.QuotaMatches(q, option.Type.ID, option.Type.Name ?? "LV"))
                    .Sum(q => q.count);
                var active = Data.LogisticsNetwork.ActiveCountFor(lvActive, option.Type.ID, option.Type.Name ?? "LV");
                if (active >= allowed)
                    quotaExhausted = true;
                return active < allowed;
            })
            .OrderBy(option => option.TierAdjustment)
            .ThenBy(option => option.IsFacilityBacked ? 0 : 1)
            .ThenBy(option => option.Type?.Name ?? "LV", StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (availableLV.Count == 0)
        {
            reason = quotaExhausted
                ? LogisticsStrings.AllLvQuotaInUseAt(providerOI)
                : LogisticsStrings.NoLvAvailableAt(providerOI);
            return false;
        }

        lvType = availableLV[0].Type;
        supportDetail = availableLV[0].Label;
        supportTierAdjustment = availableLV[0].TierAdjustment;
        if (requireContainerOnly)
        {
            carrier = PeekCyclicalOrbitalContainer(player);
            if (carrier == null)
                reason = LogisticsStrings.NoOrbitalContainerAt(providerOI);
            return true;
        }

        carrier = FindBestIdleSpacecraft(providerOI, player, scActive, requireNonContainer: requireRegularSC, out var carrierReason);
        if (carrier == null)
            reason = carrierReason ?? LogisticsStrings.NoIdleSpacecraftAt(providerOI);
        return carrier != null;
    }

    private static List<LaunchSupportOption> GetAvailableLaunchSupport(ObjectInfo providerOI, Company player, PlannerSnapshot snapshot = null)
    {
        if (providerOI == null || player == null)
            return new List<LaunchSupportOption>();

        if (snapshot != null && providerOI.id > 0)
        {
            if (snapshot.LaunchSupportByObjectId.TryGetValue(providerOI.id, out var cached))
                return cached;

            var computed = BuildAvailableLaunchSupport(providerOI, player);
            snapshot.LaunchSupportByObjectId[providerOI.id] = computed;
            return computed;
        }

        return BuildAvailableLaunchSupport(providerOI, player);
    }

    private static List<LaunchSupportOption> BuildAvailableLaunchSupport(ObjectInfo providerOI, Company player)
    {
        var objectData = providerOI.GetObjectInfoData(player);
        var seen = new HashSet<int>();
        var result = new List<LaunchSupportOption>();

        // Primary: stock GetListLaunchVehicle (includes most standard LVs)
        var rows = providerOI.GetListLaunchVehicle(player);
        if (rows != null)
        {
            foreach (var row in rows)
            {
                if (row?.launchVehicle == null || row.launchVehicle.launchVehicleType == null) continue;
                if (!seen.Add(row.launchVehicle.ID)) continue;
                var facility = objectData?.GetFakeLVFromFacilityReverse(row.launchVehicle);
                var category = GetLaunchSupportCategory(providerOI, row.launchVehicle, facility);
                result.Add(new LaunchSupportOption
                {
                    Vehicle = row.launchVehicle,
                    Type = row.launchVehicle.launchVehicleType,
                    Facility = facility,
                    Category = category,
                    IsFacilityBacked = facility != null,
                    Label = BuildLaunchSupportLabel(row.launchVehicle, facility, category),
                    TierAdjustment = GetLaunchSupportTierAdjustment(category)
                });
            }
        }

        // Fallback: inspect the body's own LV list instead of scanning the whole scene.
        // Stock facility LVs are inserted into ObjectInfo.ListLaunchVehicle when their fake LV is created.
        foreach (var lv in providerOI.ListLaunchVehicle)
        {
            if (lv == null || lv.launchVehicleType == null) continue;
            if (lv.GetCompany() != player) continue;
            if (lv.objectInfo != providerOI) continue;
            if (!seen.Add(lv.ID)) continue;
            var facility = objectData?.GetFakeLVFromFacilityReverse(lv);
            var category = GetLaunchSupportCategory(providerOI, lv, facility);
            result.Add(new LaunchSupportOption
            {
                Vehicle = lv,
                Type = lv.launchVehicleType,
                Facility = facility,
                Category = category,
                IsFacilityBacked = facility != null,
                Label = BuildLaunchSupportLabel(lv, facility, category),
                TierAdjustment = GetLaunchSupportTierAdjustment(category)
            });
        }

        return result;
    }

    private static string DescribeAvailableLaunchSupport(ObjectInfo providerOI, Company player, PlannerSnapshot snapshot = null)
    {
        var support = GetAvailableLaunchSupport(providerOI, player, snapshot);
        if (support.Count == 0)
        {
            if (providerOI?.IsUseInSpaceElevator == true && providerOI.parentObjectInfo?.LowOrbitCustom != null)
                return $"; special-launch=space-elevator->{providerOI.parentObjectInfo.LowOrbitCustom.GetObjectInfo()?.ObjectName}";
            return string.Empty;
        }

        var labels = string.Join(", ", support
            .Select(option => option.Label)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct()
            .Take(6));

        var elevator = providerOI?.IsUseInSpaceElevator == true && providerOI.parentObjectInfo?.LowOrbitCustom != null
            ? $", space-elevator->{providerOI.parentObjectInfo.LowOrbitCustom.GetObjectInfo()?.ObjectName}"
            : string.Empty;

        return string.IsNullOrWhiteSpace(labels)
            ? string.Empty
            : $"; available launch support={labels}{elevator}";
    }

    private static string BuildLaunchSupportLabel(LaunchVehicle lv, Facility facility, string category)
    {
        var lvName = lv?.launchVehicleType?.Name ?? "LV";
        if (facility != null)
        {
            var facilityName = facility.facilityDescriptor?.GetText(longText: false) ?? facility.GetType().Name;
            return $"{lvName} via {facilityName} [{category}]";
        }

        if (!string.IsNullOrWhiteSpace(category) && category != "standard-launch")
            return $"{lvName} [{category}]";

        return lvName;
    }

    private static string GetLaunchSupportCategory(ObjectInfo providerOI, LaunchVehicle lv, Facility facility)
    {
        if (facility != null)
        {
            var facilityName = facility.facilityDescriptor?.GetText(longText: false) ?? facility.GetType().Name;
            return ClassifyLaunchSupport(facilityName, lv?.launchVehicleType?.Name ?? "LV");
        }

        if (providerOI?.IsUseInSpaceElevator == true && providerOI.parentObjectInfo?.LowOrbitCustom != null)
            return "space-elevator";

        return "standard-launch";
    }

    private static string ClassifyLaunchSupport(string facilityName, string lvName)
    {
        var text = $"{facilityName} {lvName}".ToLowerInvariant();
        if (text.Contains("elevator"))
            return "space-elevator";
        if (text.Contains("spin"))
            return "spin-launch";
        if (text.Contains("magnetic") || text.Contains("rail") || text.Contains("catapult") || text.Contains("mass driver"))
            return "magnetic-rail";
        return "facility-launch";
    }

    private static int GetLaunchSupportTierAdjustment(string category)
    {
        switch (category)
        {
            case "space-elevator":
                return -45;
            case "spin-launch":
                return -40;
            case "magnetic-rail":
                return -38;
            case "facility-launch":
                return -24;
            default:
                return 0;
        }
    }

    private static void TrackPlannerBlocker(PlannerBlocker bestBlocker, int tier, int priority, string reason)
    {
        if (bestBlocker == null || string.IsNullOrEmpty(reason))
            return;
        if (tier < bestBlocker.Tier
            || (tier == bestBlocker.Tier && priority < bestBlocker.Priority)
            || (tier == bestBlocker.Tier && priority == bestBlocker.Priority && string.IsNullOrEmpty(bestBlocker.Reason)))
        {
            bestBlocker.Tier = tier;
            bestBlocker.Priority = priority;
            bestBlocker.Reason = reason;
        }
    }

    private static bool IsNoLogisticsDataReason(string reason)
    {
        return !string.IsNullOrWhiteSpace(reason)
            && reason.IndexOf("No logistics data", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Spacecraft PeekCyclicalOrbitalContainer(Company player)
    {
        var carrier = MonoBehaviourSingleton<ShipManager>.Instance?.GetLowOrbitContainer(player);
        if (carrier != null && !IsSpacecraftAvailableForLogistics(carrier, player))
            carrier = null;
        return carrier;
    }

    private static Spacecraft GetCyclicalOrbitalContainer(Company player)
    {
        var carrier = PeekCyclicalOrbitalContainer(player);
        if (carrier != null)
            return carrier;
        carrier = MonoBehaviourSingleton<ShipManager>.Instance?.AddOrbitalContainerForCyclicalMission(player);
        if (carrier != null && IsSpacecraftAvailableForLogistics(carrier, player))
            return carrier;
        return carrier;
    }

    private static int GetRouteTier(ObjectInfo effectiveSource, ObjectInfo target)
    {
        if (effectiveSource == null || target == null)
            return int.MaxValue / 2;
        var sourcePenalty = GetSourceWellPenalty(effectiveSource);
        var relationPenalty = target.objectTypes == global::Data.EObjectTypes.Orbit
            ? GetOrbitTargetTier(effectiveSource, target)
            : GetSurfaceTargetTier(effectiveSource, target);
        return sourcePenalty + relationPenalty;
    }

    private static string DescribeRouteScore(ObjectInfo effectiveSource, ObjectInfo target, int totalTier, int launchSupportAdjustment = 0)
    {
        if (effectiveSource == null || target == null)
            return $"total={totalTier}";
        var sourcePenalty = GetSourceWellPenalty(effectiveSource);
        var relationPenalty = target.objectTypes == global::Data.EObjectTypes.Orbit
            ? GetOrbitTargetTier(effectiveSource, target)
            : GetSurfaceTargetTier(effectiveSource, target);
        var sourceType = effectiveSource.objectTypes.ToString();
        var targetType = target.objectTypes.ToString();
        var sourceBody = GetCanonicalBody(effectiveSource)?.ObjectName ?? "null";
        var targetBody = GetCanonicalBody(target)?.ObjectName ?? "null";
        return $"total={totalTier};sourcePenalty={sourcePenalty};relationPenalty={relationPenalty};launchSupportAdjustment={launchSupportAdjustment};sourceType={sourceType};targetType={targetType};sourceBody={sourceBody};targetBody={targetBody}";
    }

    private static int GetSurfaceTargetTier(ObjectInfo source, ObjectInfo target)
    {
        if (IsOrbitOf(source, target))
            return 0;
        if (source == target)
            return 4;

        var sourceBody = GetCanonicalBody(source);
        var targetBody = GetCanonicalBody(target);
        if (sourceBody == null || targetBody == null)
            return 200;

        if (sourceBody == targetBody)
            return source.objectTypes == global::Data.EObjectTypes.Orbit ? 1 : 6;

        if (AreSiblingBodies(sourceBody, targetBody))
            return 14;

        if (IsDirectParentChildBody(sourceBody, targetBody))
            return 18;

        return 30 + GetSystemDistancePenalty(sourceBody, targetBody);
    }

    private static int GetOrbitTargetTier(ObjectInfo source, ObjectInfo target)
    {
        if (source == target)
            return 0;
        if (target.parentObjectInfo != null && source == target.parentObjectInfo)
            return 5;

        var sourceBody = GetCanonicalBody(source);
        var targetBody = GetCanonicalBody(target);
        if (sourceBody == null || targetBody == null)
            return 200;

        if (sourceBody == targetBody)
            return source.objectTypes == global::Data.EObjectTypes.Orbit ? 1 : 5;

        if (AreSiblingBodies(sourceBody, targetBody))
            return 12;

        if (IsDirectParentChildBody(sourceBody, targetBody))
            return 14;

        return 25 + GetSystemDistancePenalty(sourceBody, targetBody);
    }

    private static ObjectInfo GetCanonicalBody(ObjectInfo oi)
    {
        if (oi == null) return null;
        return oi.objectTypes == global::Data.EObjectTypes.Orbit ? oi.parentObjectInfo : oi;
    }

    private static bool AreSiblingBodies(ObjectInfo a, ObjectInfo b)
    {
        return a != null && b != null
            && a != b
            && a.parentObjectInfo != null
            && a.parentObjectInfo == b.parentObjectInfo;
    }

    private static bool IsDirectParentChildBody(ObjectInfo a, ObjectInfo b)
    {
        return a != null && b != null
            && (a.parentObjectInfo == b || b.parentObjectInfo == a);
    }

    private static int GetSystemDistancePenalty(ObjectInfo a, ObjectInfo b)
    {
        return Mathf.RoundToInt(Mathf.Abs(a.DistanceToSunInAU - b.DistanceToSunInAU) * 100f);
    }

    private static int GetSourceWellPenalty(ObjectInfo source)
    {
        if (source == null)
            return 200;
        if (source.objectTypes == global::Data.EObjectTypes.Orbit
            || source.objectTypes == global::Data.EObjectTypes.SolarOrbit)
            return 0;

        var body = GetCanonicalBody(source);
        if (body == null)
            return 100;

        switch (body.objectTypes)
        {
            case global::Data.EObjectTypes.Asteroid:
            case global::Data.EObjectTypes.Comet:
                return 8;
            case global::Data.EObjectTypes.Moons:
                return 15;
            case global::Data.EObjectTypes.DwarfPlanet:
                return 30;
            case global::Data.EObjectTypes.Protoplanet:
                return 45;
            case global::Data.EObjectTypes.Planet:
                return 60;
            default:
                return 40;
        }
    }

    private static bool RequiresLaunchVehicleForSpacecraft(ObjectInfo from, Spacecraft sc, Company player, double cargoAmount)
    {
        var scType = sc?.spacecraftType ?? sc?.GetTypeSpaceCraft();
        if (from == null || scType == null || player == null)
            return false;
        if (from.objectTypes == global::Data.EObjectTypes.Orbit)
            return false;
        if (!from.NeedVehicleToLaunch())
            return false;

        if (CanSelfLaunchFromSurface(from, sc, player, cargoAmount, out var acceleration, out var gravity, out var payloadLimit))
        {
            LogVerbose($"SELF-LAUNCH allowed: body={from.ObjectName} ship={sc.GetSpacecraftName()} scType={scType.NameRocketType} cargo={cargoAmount:0.#} limit={payloadLimit:0.#} accel={acceleration:0.#####} surfaceG={gravity:0.#####}");
            return false;
        }

        LogVerbose($"SELF-LAUNCH blocked: body={from.ObjectName} ship={sc?.GetSpacecraftName() ?? "null"} scType={scType.NameRocketType} cargo={cargoAmount:0.#} limit={payloadLimit:0.#} accel={acceleration:0.#####} surfaceG={gravity:0.#####} main={player.mainObjectInfo?.ObjectName} needMoonLV={scType.needLaunchVehicleToGoToMoon}");
        return true;
    }

    private static bool RequiresLaunchVehicleForSpacecraft(ObjectInfo from, SpacecraftType scType, Company player)
    {
        if (from == null || scType == null || player == null)
            return false;
        if (from.objectTypes == global::Data.EObjectTypes.Orbit)
            return false;
        if (!from.NeedVehicleToLaunch())
            return false;
        return from.Equals(player.mainObjectInfo) || scType.needLaunchVehicleToGoToMoon;
    }

    private static double GetSelfLaunchPayloadLimit(ObjectInfo from, Spacecraft sc, Company player)
    {
        if (from == null || sc == null || player == null)
            return 0;
        var scType = sc.spacecraftType ?? sc.GetTypeSpaceCraft();
        if (scType == null)
            return 0;
        if (from.objectTypes == global::Data.EObjectTypes.Orbit || !from.NeedVehicleToLaunch())
            return scType.GetCargoCapacity(player);
        if (scType.LowOrbitContainer)
            return 0;

        var gravity = from.GravitationalAcceleration;
        if (gravity <= 0)
            return scType.GetCargoCapacity(player);

        var payloadLimit = scType.GetThrust(player) / (gravity * 1000.0) - sc.GetMass() - scType.GetFuelCapacity(player);
        return Math.Max(0, Math.Min(scType.GetCargoCapacity(player), Math.Floor(payloadLimit)));
    }

    private static bool CanSelfLaunchFromSurface(ObjectInfo from, Spacecraft sc, Company player, double cargoAmount,
        out double acceleration, out double gravity, out double payloadLimit)
    {
        acceleration = 0;
        gravity = from?.GravitationalAcceleration ?? 0;
        payloadLimit = GetSelfLaunchPayloadLimit(from, sc, player);

        if (from == null || sc == null || player == null)
            return false;
        var scType = sc.spacecraftType ?? sc.GetTypeSpaceCraft();
        if (scType == null)
            return false;
        if (from.objectTypes == global::Data.EObjectTypes.Orbit || !from.NeedVehicleToLaunch())
            return true;
        if (scType.LowOrbitContainer)
            return false;

        var payload = Math.Max(0, cargoAmount);
        var mass = sc.GetMass() + payload + scType.GetFuelCapacity(player);
        if (mass <= 0)
            return false;
        acceleration = scType.GetThrust(player) / (mass * 1000.0);
        return acceleration > gravity;
    }

    public static bool TryOverrideLogisticsSelfLaunchCheck(PMMissionParameter pmp, out bool requiresFullLaunchVehicleList)
    {
        requiresFullLaunchVehicleList = false;
        if (!IsLogisticsPlan(pmp) || pmp?.SC is not Spacecraft sc || pmp.Start == null || pmp.FlyCompany == null)
            return false;

        var start = pmp.Start;
        var scType = sc.spacecraftType ?? sc.GetTypeSpaceCraft();
        if (scType == null)
            return false;
        if (scType.MagneticCatapult)
            return true;

        if (start.objectTypes != global::Data.EObjectTypes.Orbit
            && start.objectTypes != global::Data.EObjectTypes.Asteroid
            && start.objectTypes != global::Data.EObjectTypes.Comet
            && start.objectTypes != global::Data.EObjectTypes.SolarOrbit)
        {
            if (start.parentObjectInfo != null && pmp.Target != null && pmp.Start != pmp.StartHermesCase)
                return true;
            if (scType.LowOrbitContainer)
            {
                requiresFullLaunchVehicleList = true;
                return true;
            }

            var cargo = pmp.CargoAll?.CargoCurrent ?? 0;
            var canSelfLaunch = CanSelfLaunchFromSurface(start, sc, pmp.FlyCompany, cargo,
                out var acceleration, out var gravity, out var payloadLimit);
            requiresFullLaunchVehicleList = !canSelfLaunch;
            LogVerbose($"SELF-LAUNCH stock-override: route={pmp.Start?.ObjectName}->{pmp.Target?.ObjectName} ship={sc.GetSpacecraftName()} scType={scType.NameRocketType} cargo={cargo:0.#} limit={payloadLimit:0.#} accel={acceleration:0.#####} surfaceG={gravity:0.#####} requiresLV={requiresFullLaunchVehicleList}");
            return true;
        }

        requiresFullLaunchVehicleList = scType.LowOrbitContainer;
        return true;
    }

    private static bool TryCreateFuelBootstrapDelivery(Data.LogisticsRequest blockedReq, ObjectInfo requesterOI,
        ResourceDefinition blockedResource, ResourceDefinition fuelType, double fuelShortfall, Company player)
    {
        if (blockedReq == null || requesterOI == null || blockedResource == null || fuelType == null || player == null)
            return false;
        if (blockedResource == fuelType || fuelShortfall <= 0)
            return false;

        var current = GetFuelStock(requesterOI, player, fuelType);
        var inFlight = GetInFlightDeliveryAmount(requesterOI, fuelType, player);
        var fakeFuelReq = new Data.LogisticsRequest
        {
            ResourceDefinition = fuelType,
            resourceDef = fuelType,
            requestedAmount = current + inFlight + fuelShortfall,
            status = Data.LogisticsRequestStatus.Pending
        };

        Log($"RETURNFUEL bootstrap-dispatch: blockedResource={blockedResource.ID} target={requesterOI.ObjectName} fuel={fuelType.ID} shortfall={fuelShortfall:0.#} current={current:0.#} inFlight={inFlight:0.#}");
        TryCreateDeliveries(fakeFuelReq, requesterOI, fuelType, fuelShortfall, player);
        blockedReq.status = Data.LogisticsRequestStatus.InProgress;
        blockedReq.statusNote = LogisticsStrings.WaitingForReturnFuel(fuelType, requesterOI);
        return true;
    }

    private static ETransferType GetTransferTypeForSpacecraft(ObjectInfo quotaLocation, Spacecraft sc)
    {
        if (quotaLocation == null || sc?.spacecraftType == null)
            return ETransferType.Optimal;

        var data = Data.LogisticsNetwork.Get(quotaLocation);
        var quota = data?.spacecraftQuota?
            .FirstOrDefault(q => Data.LogisticsNetwork.QuotaMatches(q, sc.spacecraftType.ID, sc.spacecraftType.NameRocketType ?? "SC"));
        return quota?.useFastestTransfer == true ? ETransferType.Fastest : ETransferType.Optimal;
    }

    private static bool SetupDirectCycleMission(Data.LogisticsRequest req, Spacecraft sc,
        ResourceDefinition rd, double amount, ObjectInfo requesterOI, ObjectInfo providerOI,
        out ResourceDefinition blockedFuelType, out double blockedFuelShortfall,
        LaunchVehicleType lvTypeA = null, ObjectInfo accountingTargetOI = null, ObjectInfo pendingTargetOI = null)
    {
        blockedFuelType = null;
        blockedFuelShortfall = 0;
        var player = MonoBehaviourSingleton<GameManager>.Instance.Player;
        if (sc == null || player == null) return false;
        if (sc.GetCompany() != player)
        {
            LogWarning($"SKIP cycle: spacecraft company is not player for {sc.spacecraftType?.NameRocketType ?? "SC"}");
            return false;
        }
        if (!ValidateSpacecraftForCycleCreation(sc, player, "direct-create"))
            return false;

        var realProvider = sc.CurrentlyOnThisObject;
        if (realProvider == null) return false;

        amount = ClampToOutstandingRequest(req, accountingTargetOI ?? requesterOI, rd, player, amount);
        var capacity = sc.spacecraftType?.GetCargoCapacity(player) ?? 0;
        if (lvTypeA == null && realProvider.NeedVehicleToLaunch())
        {
            var selfLaunchLimit = GetSelfLaunchPayloadLimit(realProvider, sc, player);
            capacity = Math.Min(capacity, selfLaunchLimit);
            LogVerbose($"SELF-LAUNCH manifest-cap: route={realProvider.ObjectName}->{requesterOI?.ObjectName ?? "null"} ship={sc.GetSpacecraftName()} scType={sc.spacecraftType?.NameRocketType} payloadLimit={selfLaunchLimit:0.#} effectiveCapacity={capacity:0.#}");
        }
        amount = Math.Min(amount, capacity);
        if (amount <= 0) return false;

        var scList = new List<ISpacecraftInfo> { sc as ISpacecraftInfo };
        if (!BuildCargoManifestWithReturnFuel(req, rd, amount, requesterOI, realProvider, sc, player,
                capacity, lvTypeA, out var cargoToB, out var normalCargo, out var reserveFuelCargo,
                out blockedFuelType, out blockedFuelShortfall, out var waitingForFuelProbe))
        {
            if (waitingForFuelProbe)
            {
                req.status = Data.LogisticsRequestStatus.InProgress;
                req.statusNote = "Calculating return fuel reserve";
            }
            LogWarning($"SKIP cycle: return fuel reserve could not be manifested for {realProvider?.ObjectName}->{requesterOI?.ObjectName} rd={rd.ID} requested={amount:0.#}");
            return false;
        }
        amount = normalCargo;
        var transferType = GetTransferTypeForSpacecraft(realProvider, sc);
        if (!TryAcquireRoutePlanningLock(realProvider, requesterOI, rd, player, out var routeLockKey))
        {
            req.status = Data.LogisticsRequestStatus.InProgress;
            req.statusNote = $"Planning mission for {realProvider.ObjectName} -> {requesterOI.ObjectName}";
            return true;
        }

        var cmdData = new CycleMissionsDataData
        {
            A = realProvider, B = requesterOI, Company = player,
            CargoStart = ECargoStart.FlyWithWhatIsAvailable, CargoEnd = ECargoStart.FlyWithWhatIsAvailable,
            CargoAllStart = cargoToB, CargoAllEnd = CargoAll.CreateCargoEmpty(),
            LvTypeA = lvTypeA, LvTypeB = null, TransferType = transferType,
            Ends = EEnds.ResourceCount,
            EndsResourceCountDataA = new EndsResourceCountData(),
            EndsResourceCountMaxA = MakeResourceCount(cargoToB, amount > 0 ? rd : sc.spacecraftType.GetFuelType(), amount > 0 ? amount : reserveFuelCargo),
            EndsResourceCountDataB = new EndsResourceCountData(),
            EndsResourceCountMaxB = new EndsResourceCountData(),
            EndsObjectThisManyTimes = 1,
            ListSC = scList
        };
        LogVerbose($"RESOURCECOUNT build: route={realProvider?.ObjectName}->{requesterOI?.ObjectName} rd={rd.ID} manifest={FormatCargo(cargoToB)} endsA={FormatResourceCount(cmdData.EndsResourceCountMaxA)} endsB={FormatResourceCount(cmdData.EndsResourceCountMaxB)} reserveFuel={reserveFuelCargo:0.#}");
        var cmd = new CycleMissionsData(cmdData);
        cmd.customNameFromPlanMission = BuildLogisticsMissionName(realProvider, requesterOI, rd);
        _cycleCreatedAt[cmd] = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        MarkPendingPlanningDelivery(pendingTargetOI ?? requesterOI, rd);
        MarkShipForReturn(sc, realProvider, requesterOI, rd);
        RegisterLogisticsCycleName(cmd);
        MonoBehaviourSingleton<CycleMissionManager>.Instance.AddCycleMission(sc, cmd, scList);

        CommitStock(realProvider, rd, amount);

        var label = lvTypeA != null
            ? $"LV+Container: A={realProvider.ObjectName} B={requesterOI.ObjectName} lv={lvTypeA.Name}"
            : $"SC: A={realProvider.ObjectName} B={requesterOI.ObjectName} ship=1";
        if (VerboseLoggingEnabled)
        {
            Log($"LOGI-MANIFEST direct: route={realProvider.ObjectName}->{requesterOI.ObjectName} rd={rd.ID} ship={sc.GetSpacecraftName()} scType={sc.spacecraftType?.NameRocketType} capacity={capacity:0.#} targetCargo={amount:0.#} reserveFuel={reserveFuelCargo:0.#} totalPayload={cargoToB.CargoCurrent:0.#} transfer={transferType} manifest={FormatCargo(cargoToB)}");
            Log($"Cycle: {label} rd={rd.ID} transfer={transferType} targetAmount={amount} reserveFuel={reserveFuelCargo:0.#} manifest={FormatCargo(cargoToB)}");
        }

        req.status = Data.LogisticsRequestStatus.InProgress;

        HandOffCycleToStockPlanner(sc, cmd, "direct-delivery", routeLockKey);
        return true;

    }

    private static bool SetupCycleMission(Data.LogisticsRequest req, Spacecraft container,
        ResourceDefinition rd, double amount, ObjectInfo requesterOI, ObjectInfo providerOI,
        LaunchVehicleType lvTypeA, out ResourceDefinition blockedFuelType, out double blockedFuelShortfall,
        ObjectInfo accountingTargetOI = null, ObjectInfo pendingTargetOI = null)
    {
        blockedFuelType = null;
        blockedFuelShortfall = 0;
        var player = MonoBehaviourSingleton<GameManager>.Instance.Player;
        if (container == null || player == null) return false;
        if (container.GetCompany() != player)
        {
            LogWarning($"SKIP LV cycle: spacecraft/container company is not player for {container.spacecraftType?.NameRocketType ?? "SC"}");
            return false;
        }
        if (!ValidateSpacecraftForCycleCreation(container, player, "lv-create"))
            return false;

        var realProvider = providerOI;
        if (realProvider == null) return false;

        amount = ClampToOutstandingRequest(req, accountingTargetOI ?? requesterOI, rd, player, amount);
        var scCapacity = container.spacecraftType?.GetCargoCapacity(player) ?? 0;
        amount = Math.Min(amount, scCapacity);
        if (amount <= 0) return false;

        var scList = new List<ISpacecraftInfo> { container as ISpacecraftInfo };
        if (!BuildCargoManifestWithReturnFuel(req, rd, amount, requesterOI, realProvider, container, player,
                scCapacity, lvTypeA, out var cargoToB, out var normalCargo, out var reserveFuelCargo,
                out blockedFuelType, out blockedFuelShortfall, out var waitingForFuelProbe))
        {
            if (waitingForFuelProbe)
            {
                req.status = Data.LogisticsRequestStatus.InProgress;
                req.statusNote = "Calculating return fuel reserve";
            }
            LogWarning($"SKIP LV cycle: return fuel reserve could not be manifested for {realProvider?.ObjectName}->{requesterOI?.ObjectName} rd={rd.ID} requested={amount:0.#}");
            return false;
        }
        amount = normalCargo;

        var isLOC = container.spacecraftType?.LowOrbitContainer == true;
        var transferType = isLOC
            ? ETransferType.Optimal
            : GetTransferTypeForSpacecraft(realProvider, container);
        if (!TryAcquireRoutePlanningLock(realProvider, requesterOI, rd, player, out var routeLockKey))
        {
            req.status = Data.LogisticsRequestStatus.InProgress;
            req.statusNote = $"Planning mission for {realProvider.ObjectName} -> {requesterOI.ObjectName}";
            return true;
        }

        var cmdData = new CycleMissionsDataData
        {
            A = realProvider, B = requesterOI, Company = player,
            CargoStart = ECargoStart.FlyWithWhatIsAvailable, CargoEnd = ECargoStart.FlyWithWhatIsAvailable,
            CargoAllStart = cargoToB, CargoAllEnd = CargoAll.CreateCargoEmpty(),
            LvTypeA = lvTypeA, LvTypeB = null, TransferType = transferType,
            Ends = EEnds.ResourceCount,
            EndsResourceCountDataA = new EndsResourceCountData(),
            EndsResourceCountMaxA = MakeResourceCount(cargoToB, amount > 0 ? rd : container.spacecraftType.GetFuelType(), amount > 0 ? amount : reserveFuelCargo),
            EndsResourceCountDataB = new EndsResourceCountData(),
            EndsResourceCountMaxB = new EndsResourceCountData(),
            EndsObjectThisManyTimes = 1,
            ListSC = scList
        };
        LogVerbose($"RESOURCECOUNT build: route={realProvider?.ObjectName}->{requesterOI?.ObjectName} rd={rd.ID} manifest={FormatCargo(cargoToB)} endsA={FormatResourceCount(cmdData.EndsResourceCountMaxA)} endsB={FormatResourceCount(cmdData.EndsResourceCountMaxB)} reserveFuel={reserveFuelCargo:0.#}");
        var cmd = new CycleMissionsData(cmdData);
        cmd.customNameFromPlanMission = BuildLogisticsMissionName(realProvider, requesterOI, rd);
        _cycleCreatedAt[cmd] = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        MarkPendingPlanningDelivery(pendingTargetOI ?? requesterOI, rd);
        MarkShipForReturn(container, realProvider, requesterOI, rd);
        RegisterLogisticsCycleName(cmd);
        MonoBehaviourSingleton<CycleMissionManager>.Instance.AddCycleMission(container, cmd, scList);
        CommitStock(realProvider, rd, amount);

        var label = $"LV+{(isLOC?"Container":"SC")} Cycle: A={realProvider.ObjectName} B={requesterOI.ObjectName} lv={lvTypeA.Name} transfer={transferType}";
        if (VerboseLoggingEnabled)
        {
            Log($"LOGI-MANIFEST lv: route={realProvider.ObjectName}->{requesterOI.ObjectName} rd={rd.ID} carrier={container.GetSpacecraftName()} scType={container.spacecraftType?.NameRocketType} capacity={scCapacity:0.#} targetCargo={amount:0.#} reserveFuel={reserveFuelCargo:0.#} totalPayload={cargoToB.CargoCurrent:0.#} lv={lvTypeA?.Name ?? "none"} transfer={transferType} manifest={FormatCargo(cargoToB)}");
            Log($"Cycle: {label} rd={rd.ID} targetAmount={amount} reserveFuel={reserveFuelCargo:0.#} manifest={FormatCargo(cargoToB)}");
        }

        req.status = Data.LogisticsRequestStatus.InProgress;

        HandOffCycleToStockPlanner(container, cmd, "lv-delivery", routeLockKey);
        return true;

    }

    private static EndsResourceCountData MakeResourceCount(ResourceDefinition rd, double amount)
    {
        var data = new EndsResourceCountData();
        data.listData.Add(new EndsResourceCountDataPart { rd = rd, count = amount });
        return data;
    }

    private static EndsResourceCountData MakeResourceCount(CargoAll cargoAll, ResourceDefinition fallbackRd, double fallbackAmount)
    {
        var data = new EndsResourceCountData();
        if (cargoAll != null)
        {
            foreach (var cargo in GetResourceCargoItems(cargoAll))
            {
                if (cargo.resourceType == null || cargo.cargoMass <= 0) continue;
                var existing = data.listData.FirstOrDefault(part => part.rd == cargo.resourceType);
                if (existing != null)
                {
                    existing.count += cargo.cargoMass;
                }
                else
                {
                    data.listData.Add(new EndsResourceCountDataPart { rd = cargo.resourceType, count = cargo.cargoMass });
                }
            }
        }

        if (data.listData.Count == 0 && fallbackRd != null && fallbackAmount > 0)
            data.listData.Add(new EndsResourceCountDataPart { rd = fallbackRd, count = fallbackAmount });

        LogVerbose($"RESOURCECOUNT from-manifest: manifest={FormatCargo(cargoAll)} fallback={fallbackRd?.ID ?? "null"}:{fallbackAmount:0.#} result={FormatResourceCount(data)}");
        return data;
    }

    private static string FormatResourceCount(EndsResourceCountData data)
    {
        if (data?.listData == null || data.listData.Count == 0) return "empty";
        return string.Join(", ", data.listData
            .Where(part => part?.rd != null)
            .Select(part => $"{part.rd.ID}:{part.count:0.#}"));
    }

    private static double ClampToOutstandingRequest(Data.LogisticsRequest req, ObjectInfo requesterOI,
        ResourceDefinition rd, Company player, double amount)
    {
        if (req == null || requesterOI == null || rd == null || player == null)
            return amount;

        var current = requesterOI.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
        var inFlight = GetInFlightDeliveryAmount(requesterOI, rd, player);
        var outstanding = Math.Max(0, RequestTarget(req) - current - inFlight);
        return Math.Min(amount, outstanding);
    }
}
