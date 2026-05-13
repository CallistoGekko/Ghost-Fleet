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
    private static double CyclePlanningGraceDays => LogisticsMod.Plugin.CyclePlanningGraceDays?.Value ?? 3.0;
    private static readonly Dictionary<CycleMissionsData, DateTime> _cycleCreatedAt = new Dictionary<CycleMissionsData, DateTime>();
    private static readonly Dictionary<string, DateTime> _pendingPlanningDeliveries = new Dictionary<string, DateTime>();
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
        WriteLog("", msg);
    }

    public static void LogVerbose(string msg)
    {
        if (VerboseLogging)
            Log(msg);
    }

    public static void LogWarning(string msg)
    {
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
        Debug.Log("[LogisticsMod] " + msg);
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
        _cycleCreatedAt.Clear();
        _pendingPlanningDeliveries.Clear();
        _returnHomeByShipId.Clear();
        Log($"RESET runtime-state: cycles={cycleCount} pending={pendingCount} returns={returnCount}");
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

        TryReturnIdleLogisticsShips(player);

        var networkResources = Data.LogisticsNetwork.GetNetworkResourcesSet(player);

        foreach (var requesterOI in Data.LogisticsNetwork.GetAllObjects())
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
                            ? GetReturnBlockedStatusNote(requesterOI, rd, player)
                            : null;
                        if (!string.IsNullOrEmpty(blockedSatisfiedReturnNote))
                        {
                            req.status = Data.LogisticsRequestStatus.InProgress;
                            req.statusNote = blockedSatisfiedReturnNote;
                            LogVerbose($"REQ keep-satisfied-return-blocked: target={requesterOI?.ObjectName} rd={rd?.ID} note={blockedSatisfiedReturnNote}");
                            continue;
                        }
                        if (rd != null)
                            CleanupLogisticsCyclesForRequest(requesterOI, rd, player, $"request-{req.status.ToString().ToLowerInvariant()}");
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
                var blockedReturnNote = GetReturnBlockedStatusNote(requesterOI, rd, player);
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
                    CleanupLogisticsCyclesForRequest(requesterOI, rd, player, "request-fulfilled");
                    continue;
                }

                if (HandleRelayProgress(req, requesterOI, rd, requestTarget, alreadyThere, player))
                    continue;

                bool hasActiveDelivery = HasActiveCycleDelivering(requesterOI, rd, player);
                if (alreadyThere >= requestMinimum && !hasActiveDelivery && string.IsNullOrEmpty(blockedReturnNote))
                {
                    req.status = Data.LogisticsRequestStatus.Satisfied;
                    CleanupLogisticsCyclesForRequest(requesterOI, rd, player, "above-minimum");
                    LogVerbose($"REQ hold-above-minimum: target={requesterOI?.ObjectName} rd={rd.ID} stock={alreadyThere:0.#} minimum={requestMinimum:0.#} fillTarget={requestTarget:0.#}");
                    continue;
                }

                if (hasActiveDelivery)
                {
                    req.status = Data.LogisticsRequestStatus.InProgress;
                    LogVerbose($"REQ wait-active-cycle: target={requesterOI?.ObjectName} rd={rd.ID}");
                    continue;
                }

                if (!string.IsNullOrEmpty(blockedReturnNote))
                {
                    req.status = Data.LogisticsRequestStatus.InProgress;
                    req.statusNote = blockedReturnNote;
                    LogVerbose($"REQ wait-return-blocked: target={requesterOI?.ObjectName} rd={rd.ID} note={blockedReturnNote}");
                    continue;
                }

                if (HasPendingPlanningDelivery(requesterOI, rd))
                {
                    req.status = Data.LogisticsRequestStatus.InProgress;
                    LogVerbose($"REQ wait-pending-plan: target={requesterOI?.ObjectName} rd={rd.ID}");
                    continue;
                }

                var inFlight = GetInFlightDeliveryAmount(requesterOI, rd, player);
                double remaining = requestTarget - alreadyThere - inFlight;
                LogVerbose($"REQ remaining: target={requesterOI?.ObjectName} rd={rd.ID} fillTarget={requestTarget:0.#} minimum={requestMinimum:0.#} stock={alreadyThere:0.#} inFlight={inFlight:0.#} remaining={remaining:0.#}");
                if (remaining <= 0)
                {
                    req.status = Data.LogisticsRequestStatus.InProgress;
                    LogVerbose($"WAIT IN-FLIGHT: {rd.ID} on {requesterOI?.ObjectName} alreadyThere={alreadyThere:0.#} inFlight={inFlight:0.#} fillTarget={requestTarget:0.#}");
                    continue;
                }

                req.status = Data.LogisticsRequestStatus.Pending;
                var pendingReason = TryCreateDeliveries(req, requesterOI, rd, remaining, player);
                if (req.status == Data.LogisticsRequestStatus.Pending && !string.IsNullOrEmpty(pendingReason))
                    req.statusNote = pendingReason;
            }
        }
    }

    private static bool HandleRelayProgress(Data.LogisticsRequest req, ObjectInfo requesterOI,
        ResourceDefinition rd, double requestTarget, double alreadyThere, Company player)
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
            if (HasActiveCycleDelivering(orbitOI, rd, player) || HasPendingPlanningDelivery(orbitOI, rd))
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

        if (HasActiveCycleDelivering(finalTargetOI, rd, player) || HasPendingPlanningDelivery(finalTargetOI, rd))
        {
            req.status = Data.LogisticsRequestStatus.InProgress;
            req.statusNote = LogisticsStrings.ShippingFrom(orbitOI);
            return true;
        }

        var stagedStock = orbitOI.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
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

        if (TryCreateRelayFinalDelivery(req, finalTargetOI, orbitOI, rd, Math.Min(remaining, stagedStock), player))
            return true;

        req.status = Data.LogisticsRequestStatus.InProgress;
        req.statusNote = LogisticsStrings.WaitingForSpacecraftAt(orbitOI);
        return true;
    }

    private static bool HasActiveCycleDelivering(ObjectInfo requester, ResourceDefinition rd, Company player)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm == null) return false;

        foreach (var cmd in cm.GetAllCycleMission(player).ToList())
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
                    cm.RemoveCycleMission(cmd);
                    break;
                }
            }
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

    private static void CleanupLogisticsCyclesForRequest(ObjectInfo requester, ResourceDefinition rd, Company player, string reason)
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

        foreach (var cmd in cm.GetAllCycleMission(player).ToList())
        {
            if (!IsLogisticsDeliveryMission(cmd)) continue;
            if (cmd.B != requester) continue;
            if (!CargoContainsResource(cmd.cargoAllStart, rd) && !CargoContainsResource(cmd.cargoAllEnd, rd)) continue;
            if (ShouldPreserveLandedDeliveryCycle(cmd, requester, rd, player))
            {
                LogVerbose($"CLEANUP preserve-landed LOGI cycle: {cmd.A?.ObjectName}->{cmd.B?.ObjectName} rd={rd.ID} reason={reason}");
                continue;
            }

            _cycleCreatedAt.Remove(cmd);
            LogWarning($"CLEANUP fulfilled LOGI cycle: {cmd.A?.ObjectName}->{cmd.B?.ObjectName} rd={rd.ID} reason={reason}");
            cm.RemoveCycleMission(cmd);
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

    private static string PendingDeliveryKey(ObjectInfo requester, ResourceDefinition rd)
    {
        return $"{requester?.id ?? -1}:{rd?.ID ?? "null"}";
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
        if ((currentTime - createdAt).TotalDays < CyclePlanningGraceDays)
            return true;

        _pendingPlanningDeliveries.Remove(key);
        LogWarning($"PENDING stale: target={requester?.ObjectName} rd={rd?.ID} expired after {CyclePlanningGraceDays:0.#} days");
        return false;
    }

    private static void MarkPendingPlanningDelivery(ObjectInfo requester, ResourceDefinition rd)
    {
        if (requester == null || rd == null) return;
        _pendingPlanningDeliveries[PendingDeliveryKey(requester, rd)] =
            MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
    }

    private static void ClearPendingPlanningDelivery(ObjectInfo requester, ResourceDefinition rd)
    {
        _pendingPlanningDeliveries.Remove(PendingDeliveryKey(requester, rd));
    }

    private static bool IsCycleWaitingOrPlanned(CycleMissionsData cmd, CycleMissionManager cm)
    {
        if (cmd == null || cm == null) return false;
        if (_cycleCreatedAt.TryGetValue(cmd, out var createdAt))
        {
            var currentTime = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
            if ((currentTime - createdAt).TotalDays < CyclePlanningGraceDays)
                return true;
            _cycleCreatedAt.Remove(cmd);
        }
        if (cmd.wasSetPMParameterForCodeJobSystem)
            return true;

        foreach (var sc in UnityEngine.Object.FindObjectsOfType<Spacecraft>())
        {
            if (sc == null) continue;
            if (cm.GetCycleMission(sc) != cmd) continue;

            var ctrl = sc.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
            if (ctrl != null && ctrl.CycleMissionPlanFlyWas)
            {
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

        return false;
    }

    private static double GetInFlightDeliveryAmount(ObjectInfo requester, ResourceDefinition rd, Company player)
    {
        var mm = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        if (mm?.ListMissionInfo == null || requester == null || rd == null || player == null)
            return 0;

        double result = 0;
        foreach (var mi in mm.ListMissionInfo)
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
        CountActiveLogisticsCycles(player, out scActive, out lvActive);
    }

    private static void CountActiveLogisticsCycles(Company player,
        out Dictionary<string, int> scActive, out Dictionary<string, int> lvActive)
    {
        scActive = new Dictionary<string, int>();
        lvActive = new Dictionary<string, int>();
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm == null) return;

        foreach (var cmd in cm.GetAllCycleMission(player))
        {
            if (cmd.CheckComplete()) continue;
            if (!IsLogisticsMission(cmd)) continue;
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

    private static bool IsLogisticsMission(CycleMissionsData cmd)
    {
        return cmd?.customNameFromPlanMission != null
            && cmd.customNameFromPlanMission.StartsWith("[LOGI", StringComparison.Ordinal);
    }

    public static bool IsLogisticsPlan(PMMissionParameter pmp)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (pmp == null || player == null || cm == null) return false;
        if (!pmp.ForCyclicalMission || pmp.FlyCompany != player) return false;

        foreach (var cmd in cm.GetAllCycleMission(player))
        {
            if (!IsLogisticsMission(cmd)) continue;
            var sameDirection = cmd.A == pmp.Start && cmd.B == pmp.Target;
            var reverseDirection = cmd.B == pmp.Start && cmd.A == pmp.Target;
            if (sameDirection || reverseDirection)
                return true;
        }

        return false;
    }

    public static void CapLogisticsCargoForPlannerLimits(PMMissionParameter pmp)
    {
        if (!IsLogisticsPlan(pmp) || pmp.CargoAll == null) return;

        var result = pmp.CheckCanPlanMission().planMissionResult;
        if (ApplySmallReservePropellant(pmp))
            result = pmp.CheckCanPlanMission().planMissionResult;

        var cargoStart = pmp.CargoAll.CargoCurrent;
        LogVerbose($"PLAN result-before-cap: {pmp.Start?.ObjectName}->{pmp.Target?.ObjectName} result={result} cargo={cargoStart:0.#} propellant={pmp.CargoAll?.cargoFuel?.cargoMassPotencjal:0.#} sc={pmp.SC?.GetSpacecraftName()} scType={pmp.SC?.GetTypeSpaceCraft()?.NameRocketType} lv={pmp.LV?.GetLaunchVehicleType()?.Name}");
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

        for (var i = 0; i < 10; i++)
        {
            var scale = (low + high) / 2;
            ApplyCargoScale(cargoItems, original, scale);

            var check = pmp.CheckCanPlanMission().planMissionResult;
            if (check == PMMissionParameter.EPlanMissionResult.AllOk)
            {
                bestScale = scale;
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
            var cappedTotal = cargoItems.Sum(c => c.cargoMass);
            var afterResult = pmp.CheckCanPlanMission().planMissionResult;
            Log($"CAP planner cargo: {originalTotal:0.#} -> {cappedTotal:0.#} dueTo={result} after={afterResult}");
        }
        else
        {
            ApplyCargoScale(cargoItems, original, 0);
            var afterResult = pmp.CheckCanPlanMission().planMissionResult;
            LogWarning($"CAP planner cargo: no valid cargo amount found for {pmp.Start?.ObjectName} -> {pmp.Target?.ObjectName}; original={originalTotal:0.#}, result={result}, afterZero={afterResult}");
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
        return Math.Max(0, available - minKeep);
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
        double capacity, out CargoAll cargoAll, out double normalCargo, out double reserveFuelCargo,
        out ResourceDefinition blockedFuelType, out double blockedFuelShortfall)
    {
        cargoAll = CargoAll.CreateCargoEmpty();
        normalCargo = Math.Min(amount, capacity);
        reserveFuelCargo = 0;
        blockedFuelType = null;
        blockedFuelShortfall = 0;

        if (rd == null || normalCargo <= 0 || capacity <= 0)
            return false;

        AddOrIncreaseResourceCargo(cargoAll, rd, normalCargo);
        if (!TryEstimateReturnFuelRequirement(providerOI, requesterOI, sc, player, cargoAll,
                out var fuelType, out var requiredReserve, out var destinationStock))
        {
            LogVerbose($"RETURNFUEL estimate-skipped: route={providerOI?.ObjectName}->{requesterOI?.ObjectName} ship={sc?.spacecraftType?.NameRocketType} rd={rd.ID} cargo={normalCargo:0.#} manifest={FormatCargo(cargoAll)}");
            return cargoAll.CargoCurrent > 0;
        }

        var existingFuelCargo = CargoAmountFor(cargoAll, fuelType);
        var shortfall = Math.Max(0, requiredReserve - destinationStock - existingFuelCargo);
        if (shortfall <= 0)
        {
            LogVerbose($"RETURNFUEL trust-domestic-stockpile: route={providerOI?.ObjectName}->{requesterOI?.ObjectName} ship={sc?.spacecraftType?.NameRocketType} fuel={fuelType.ID} reserve={requiredReserve:0.#} destStock={destinationStock:0.#} existingFuelCargo={existingFuelCargo:0.#} manifest={FormatCargo(cargoAll)}");
            return cargoAll.CargoCurrent > 0;
        }

        var providerFuelAvailable = GetProviderAvailableAfterMinimum(providerOI, fuelType, player);
        var maxFuelCargo = capacity * MaxReturnFuelCargoDisplacementFraction;
        var maxAdditionalFuelCargo = Math.Max(0, maxFuelCargo - existingFuelCargo);
        var fuelToAdd = Math.Min(shortfall, Math.Min(providerFuelAvailable, maxAdditionalFuelCargo));
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
            LogWarning($"RETURNFUEL plan-shortfall: route={providerOI?.ObjectName}->{requesterOI?.ObjectName} ship={sc?.spacecraftType?.NameRocketType} fuel={fuelType.ID} reserve={requiredReserve:0.#} destStock={destinationStock:0.#} providerFuel={providerFuelAvailable:0.#} fuelAdded={reserveFuelCargo:0.#} shortfall={remainingShortfall:0.#} manifest={FormatCargo(cargoAll)}");
            return false;
        }

        LogVerbose($"RETURNFUEL ship-reserve-manifest: route={providerOI?.ObjectName}->{requesterOI?.ObjectName} ship={sc?.spacecraftType?.NameRocketType} fuel={fuelType.ID} reserve={requiredReserve:0.#} destStock={destinationStock:0.#} fuelAdded={reserveFuelCargo:0.#} reducedCargo={reduced:0.#} manifest={FormatCargo(cargoAll)}");
        return cargoAll.CargoCurrent > 0;
    }

    private static bool TryEstimateReturnFuelRequirement(ObjectInfo providerOI, ObjectInfo requesterOI,
        Spacecraft sc, Company player, CargoAll cargoAll,
        out ResourceDefinition fuelType, out double requiredReserve, out double destinationStock)
    {
        fuelType = null;
        requiredReserve = 0;
        destinationStock = 0;
        if (!ReturnFuelEnabled() || providerOI == null || requesterOI == null || sc == null || player == null || cargoAll == null)
            return false;

        var scType = sc.GetTypeSpaceCraft();
        if (scType == null || scType.SolarSC)
            return false;

        var pmp = new PMMissionParameter();
        pmp.SetCompany(player);
        pmp.SetTabDestination(providerOI, requesterOI);
        pmp.SetTabCargo(cargoAll);
        pmp.SetTabSC(sc);
        pmp.ForCyclicalMission = true;
        pmp.TrajectoryColor = Color.blue;
        pmp.SetMissionOrigin(MissionInfo.EMissionCreator.Other);
        pmp.CheckCanPlanMission();

        fuelType = pmp.FuelNeedToStart;
        if (fuelType == null)
            return false;

        var planFuelNeed = pmp.MINFuelCost > 0 ? Math.Min(pmp.AllFuelNeed, pmp.MINFuelCost) : pmp.AllFuelNeed;
        var estimatedReturnFuel = Math.Min(Math.Max(0, planFuelNeed), scType.GetFuelCapacity(player) * Math.Max(1, pmp.SCCount));
        requiredReserve = Math.Ceiling(estimatedReturnFuel * ReturnFuelSafetyMultiplier());
        destinationStock = GetFuelStock(requesterOI, player, fuelType);
        return requiredReserve > 0;
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
            LogWarning($"RETURNFUEL plan-shortfall: route={pmp.Start?.ObjectName}->{pmp.Target?.ObjectName} ship={scType.NameRocketType} fuel={fuelType.ID} allFuel={pmp.AllFuelNeed:0.#} minFuel={pmp.MINFuelCost:0.#} estimated={estimatedReturnFuel:0.#} reserve={requiredReserve:0.#} destStock={destinationStock:0.#} shortfall={shortfall:0.#} providerFuel={providerFuelAvailable:0.#}");
            return;
        }

        var maxFuelCargo = capacity * MaxReturnFuelCargoDisplacementFraction;
        var maxAdditionalFuelCargo = Math.Max(0, maxFuelCargo - existingFuelCargo);
        fuelToAdd = Math.Min(fuelToAdd, maxAdditionalFuelCargo);
        if (fuelToAdd <= 0)
        {
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

    private static void TryReturnIdleLogisticsShips(Company player)
    {
        if (player == null || _returnHomeByShipId.Count == 0) return;

        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm == null) return;

        var ships = MonoBehaviourSingleton<ShipManager>.Instance?.ListAllSpaceShip
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
                    cm.RemoveCycleMission(attachedCycleAtHome);
                    Log($"RETURNHOME remove-complete-cycle: ship={sc.GetSpacecraftName()} id={sc.ID} cycle={attachedCycleAtHome.customNameFromPlanMission}");
                }

                if (state.HasLeftHome)
                {
                    ResetReturnPlanState(state);
                    _returnHomeByShipId.Remove(sc.ID);
                    Log($"RETURNHOME arrived: ship={sc.GetSpacecraftName()} id={sc.ID} home={home.ObjectName}");
                }
                continue;
            }

            var attachedCycle = cm.GetCycleMission(sc);
            if (attachedCycle != null)
            {
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

            state.HasLeftHome = true;
            if (TrySetupReturnCycle(sc, current, home, player, state))
                return;
        }
    }

    private static string GetReturnBlockedStatusNote(ObjectInfo requester, ResourceDefinition rd, Company player)
    {
        if (requester == null || rd == null || player == null || _returnHomeByShipId.Count == 0)
            return null;

        var ships = MonoBehaviourSingleton<ShipManager>.Instance?.ListAllSpaceShip
            ?? UnityEngine.Object.FindObjectsOfType<Spacecraft>().ToList();
        foreach (var sc in ships)
        {
            if (sc == null || sc.spacecraftType == null) continue;
            if (sc.GetCompany() != player) continue;
            if (!_returnHomeByShipId.TryGetValue(sc.ID, out var state)) continue;
            if (state?.Destination != requester || state.Resource != rd) continue;
            if (sc.CurrentlyOnThisObject != requester) continue;
            if (string.IsNullOrEmpty(state.LastBlockedStatusNote)) continue;
            return LogisticsStrings.ReturnBlockedSuffix(state.LastBlockedStatusNote, sc.GetSpacecraftName());
        }

        return null;
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

    private static bool TrySetupReturnCycle(Spacecraft sc, ObjectInfo current, ObjectInfo home, Company player, ReturnHomeState state)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (sc == null || current == null || home == null || player == null || cm == null) return false;
        LaunchVehicleType returnLvType = null;
        LaunchVehicle returnLv = null;
        var scType = sc.spacecraftType;
        var currentIsOrbit = current.objectTypes == global::Data.EObjectTypes.Orbit;
        var needsLaunchVehicle = !currentIsOrbit && RequiresLaunchVehicleForSpacecraft(current, scType, player);
        if (needsLaunchVehicle)
        {
            var launchSupport = GetAvailableLaunchSupport(current, player);
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
            LvTypeA = null, LvTypeB = returnLvType, TransferType = ETransferType.Optimal,
            Ends = EEnds.ThisManyTimes,
            EndsObjectThisManyTimes = 1,
            ListSC = scList
        };

        var cmd = new CycleMissionsData(cmdData);
        cmd.customNameFromPlanMission = $"[LOGI-RETURN] {current.ObjectName} -> {home.ObjectName}";
        _cycleCreatedAt[cmd] = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        ResetReturnPlanState(state);
        state.LastBlockedReason = null;
        state.LastBlockedStatusNote = LogisticsStrings.AwaitingReturnFrom(current);
        state.LastBlockedDate = DateTime.MinValue;
        cm.AddCycleMission(sc, cmd, scList);

        var ctrl = sc.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
        if (ctrl == null)
            ctrl = sc.gameObject.AddComponent<SpaceCraftCyclicalMissionController>();
        ctrl.CycleMissionPlanFlyWas = false;
        ctrl.SetSC(sc);
        ctrl.TryPlanCycleMission();
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
        ResourceDefinition rd, double remaining, Company player)
    {
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        if (cm == null) return null;

        CountActiveLogisticsCycles(player, out var scActive, out var lvActive);
        LogVerbose($"DISPATCH begin: target={requester?.ObjectName} rd={rd.ID} remaining={remaining:0.#} activeSC={FormatCounts(scActive)} activeLV={FormatCounts(lvActive)}");
        var bestBlocker = new PlannerBlocker();
        var candidates = BuildRouteCandidates(req, requester, rd, remaining, player, scActive, lvActive, bestBlocker);
        if (candidates.Count == 0)
        {
            if (!Data.LogisticsNetwork.GetAllObjects().Any(oi =>
            {
                var data = Data.LogisticsNetwork.Get(oi);
                return oi != requester && data != null && data.providers.Any(p => p.isActive && p.ResourceDefinition == rd);
            }))
                LogVerbose($"DISPATCH none: target={requester?.ObjectName} rd={rd.ID} reason=no active provider with matching resource");
            else
                LogVerbose($"DISPATCH none: target={requester?.ObjectName} rd={rd.ID} reason={bestBlocker.Reason ?? "no usable ship/LV/provider this tick"}");
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

        LogBepInEx($"ROUTE request: target={requester?.ObjectName} rd={rd.ID} remaining={remaining:0.#} candidates={orderedCandidates.Count}");
        foreach (var candidate in orderedCandidates)
        {
            LogBepInEx($"ROUTE candidate: rd={rd.ID} kind={candidate.Kind} label={candidate.Label} score={candidate.Tier} usesLV={candidate.UsesLV} hops={candidate.HopCount} available={candidate.Available:0.#} amount={candidate.Amount:0.#} detail={candidate.ScoreBreakdown}");
            if (ExecuteRouteCandidate(candidate, req, requester, rd, player))
                return null;
        }
        LogBepInEx($"ROUTE no-execute: target={requester?.ObjectName} rd={rd.ID} reason={bestBlocker.Reason ?? "all candidates failed during execution"}");
        return bestBlocker.Reason;
    }

    private static List<RouteCandidate> BuildRouteCandidates(Data.LogisticsRequest req, ObjectInfo requester,
        ResourceDefinition rd, double remaining, Company player,
        Dictionary<string, int> scActive, Dictionary<string, int> lvActive, PlannerBlocker bestBlocker)
    {
        var result = new List<RouteCandidate>();
        foreach (var providerOI in Data.LogisticsNetwork.GetAllObjects())
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
                LogBepInEx($"ROUTE provider-skip: provider={providerOI?.ObjectName} rd={rd.ID} score={noSurplusTier} detail={noSurplusDetail} reason={noSurplusReason}");
                TrackPlannerBlocker(bestBlocker, noSurplusTier, 6, noSurplusReason);
                continue;
            }

            AddDirectRouteCandidates(result, providerOI, requester, rd, remaining, available, player, scActive, lvActive, bestBlocker);
            AddStagedRouteCandidate(result, providerOI, requester, rd, remaining, available, player, scActive, lvActive, bestBlocker);
        }
        return result;
    }

    private static void AddDirectRouteCandidates(List<RouteCandidate> result, ObjectInfo providerOI,
        ObjectInfo requester, ResourceDefinition rd, double remaining, double available, Company player,
        Dictionary<string, int> scActive, Dictionary<string, int> lvActive, PlannerBlocker bestBlocker)
    {
        var amount = Math.Min(available, remaining);
        if (amount <= 0) return;
        var routeTier = GetRouteTier(providerOI, requester);
        var routeDetail = DescribeRouteScore(providerOI, requester, routeTier);

        if (providerOI.NeedVehicleToLaunch())
        {
            var directSurfaceShip = FindBestIdleSpacecraft(providerOI, player, scActive,
                requireNonContainer: !IsOrbitOf(requester, providerOI), out var directSurfaceShipReason);
            var directSurfaceCapacity = directSurfaceShip?.spacecraftType?.GetCargoCapacity(player) ?? 0;
            if (directSurfaceShip != null
                && directSurfaceCapacity > 0
                && !RequiresLaunchVehicleForSpacecraft(providerOI, directSurfaceShip.spacecraftType, player))
            {
                result.Add(new RouteCandidate
                {
                    Kind = RouteKind.DirectSpacecraft,
                    Provider = providerOI,
                    EffectiveSource = providerOI,
                    Spacecraft = directSurfaceShip,
                    Amount = Math.Min(amount, directSurfaceCapacity),
                    Available = available,
                    Tier = routeTier,
                    HopCount = 1,
                    UsesLV = false,
                    Label = $"{providerOI.ObjectName} -> {requester.ObjectName}",
                    ScoreBreakdown = routeDetail + ";surfaceBypassLV=true"
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
                LogBepInEx($"ROUTE candidate-blocked: rd={rd.ID} kind={RouteKind.DirectSpacecraft} label={providerOI.ObjectName} -> {requester.ObjectName} score={routeTier} detail={routeDetail} reason={spacecraftReason}");
                TrackPlannerBlocker(bestBlocker, routeTier, 3, spacecraftReason);
            }
            return;
        }

        if (!TryFindSurfaceLaunch(providerOI, requester, player, lvActive, requireContainerOnly: IsOrbitOf(requester, providerOI),
                requireRegularSC: !IsOrbitOf(requester, providerOI), out var lvType, out var carrier, out var launchReason, out var launchSupportDetail, out var launchSupportAdjustment))
        {
            LogBepInEx($"ROUTE candidate-blocked: rd={rd.ID} kind={RouteKind.DirectSurfaceLaunch} label={providerOI.ObjectName} -> {requester.ObjectName} score={routeTier} detail={routeDetail} reason={launchReason}");
            TrackPlannerBlocker(bestBlocker, routeTier, 2, launchReason);
            return;
        }

        routeTier += launchSupportAdjustment;
        routeDetail = DescribeRouteScore(providerOI, requester, routeTier, launchSupportAdjustment);

        var scCapacity = carrier?.spacecraftType?.GetCargoCapacity(player) ?? 0;
        if (scCapacity <= 0)
        {
            var capacityReason = LogisticsStrings.NoCargoCapacityFrom(providerOI);
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
        Dictionary<string, int> scActive, Dictionary<string, int> lvActive, PlannerBlocker bestBlocker)
    {
        if (!providerOI.NeedVehicleToLaunch()) return;
        if (requester == null || providerOI == null) return;
        if (IsOrbitOf(requester, providerOI)) return;

        var sourceOrbit = providerOI.LowOrbitCustom?.GetObjectInfo();
        if (sourceOrbit == null)
        {
            var noOrbitReason = LogisticsStrings.NoSourceOrbitAt(providerOI);
            LogBepInEx($"ROUTE candidate-blocked: rd={rd.ID} kind={RouteKind.StageSourceSurfaceToOrbit} label={providerOI.ObjectName} -> [orbit missing] -> {requester.ObjectName} score=5 detail=no-source-orbit reason={noOrbitReason}");
            TrackPlannerBlocker(bestBlocker, 5, 5, noOrbitReason);
            return;
        }
        var routeTier = GetRouteTier(sourceOrbit, requester);
        var routeDetail = DescribeRouteScore(sourceOrbit, requester, routeTier);

        if (!TryFindSurfaceLaunch(providerOI, sourceOrbit, player, lvActive, requireContainerOnly: true,
                requireRegularSC: false, out var stageLvType, out var stageCarrier, out var stageReason, out var stageSupportDetail, out var stageSupportAdjustment))
        {
            LogBepInEx($"ROUTE candidate-blocked: rd={rd.ID} kind={RouteKind.StageSourceSurfaceToOrbit} label={providerOI.ObjectName} -> {sourceOrbit.ObjectName} -> {requester.ObjectName} score={routeTier} detail={routeDetail} reason={stageReason}");
            TrackPlannerBlocker(bestBlocker, routeTier, 2, stageReason);
            return;
        }

        routeTier += stageSupportAdjustment;
        routeDetail = DescribeRouteScore(sourceOrbit, requester, routeTier, stageSupportAdjustment);

        var finalCarrier = FindBestIdleSpacecraft(sourceOrbit, player, scActive, requireNonContainer: true,
            out var finalCarrierReason);
        var stageCapacity = stageCarrier?.spacecraftType?.GetCargoCapacity(player) ?? 0;
        var finalCapacity = finalCarrier?.spacecraftType?.GetCargoCapacity(player) ?? 0;
        if (stageCapacity <= 0)
        {
            var stageCapacityReason = LogisticsStrings.NoOrbitalPayloadCapacityFrom(providerOI);
            LogBepInEx($"ROUTE candidate-blocked: rd={rd.ID} kind={RouteKind.StageSourceSurfaceToOrbit} label={providerOI.ObjectName} -> {sourceOrbit.ObjectName} -> {requester.ObjectName} score={routeTier} detail={routeDetail} reason={stageCapacityReason}");
            TrackPlannerBlocker(bestBlocker, routeTier, 4, stageCapacityReason);
            return;
        }
        if (finalCapacity <= 0)
        {
            var finalReason = finalCarrierReason ?? LogisticsStrings.NoSpacecraftAvailableAt(sourceOrbit);
            LogBepInEx($"ROUTE candidate-blocked: rd={rd.ID} kind={RouteKind.StageSourceSurfaceToOrbit} label={providerOI.ObjectName} -> {sourceOrbit.ObjectName} -> {requester.ObjectName} score={routeTier} detail={routeDetail} reason={finalReason}");
            TrackPlannerBlocker(bestBlocker, routeTier, 3, finalReason);
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
        ObjectInfo requester, ResourceDefinition rd, Company player)
    {
        if (candidate == null || req == null || requester == null || rd == null || player == null)
            return false;

        switch (candidate.Kind)
        {
            case RouteKind.DirectSpacecraft:
                if (SetupDirectCycleMission(req, candidate.Spacecraft, rd, candidate.Amount, requester, candidate.Provider,
                        out var blockedFuelType, out var blockedFuelShortfall))
                {
                    ClearRelayState(req);
                    Log($"PROC ranked: {rd.ID} x{candidate.Amount:0.#} {candidate.Label} kind={candidate.Kind}");
                    LogBepInEx($"ROUTE chosen: rd={rd.ID} kind={candidate.Kind} label={candidate.Label} score={candidate.Tier} detail={candidate.ScoreBreakdown}");
                    return true;
                }
                return TryCreateFuelBootstrapDelivery(req, requester, rd, blockedFuelType, blockedFuelShortfall, player);

            case RouteKind.DirectSurfaceLaunch:
                if (candidate.Spacecraft == null && IsOrbitOf(requester, candidate.Provider))
                    candidate.Spacecraft = GetCyclicalOrbitalContainer(player);
                if (candidate.Spacecraft == null)
                    return false;
                if (SetupCycleMission(req, candidate.Spacecraft, rd, candidate.Amount, requester, candidate.Provider,
                        candidate.LaunchVehicleType, out blockedFuelType, out blockedFuelShortfall))
                {
                    ClearRelayState(req);
                    Log($"PROC ranked: {rd.ID} x{candidate.Amount:0.#} {candidate.Label} kind={candidate.Kind}");
                    LogBepInEx($"ROUTE chosen: rd={rd.ID} kind={candidate.Kind} label={candidate.Label} score={candidate.Tier} detail={candidate.ScoreBreakdown}");
                    return true;
                }
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
                    req.status = Data.LogisticsRequestStatus.InProgress;
                    req.statusNote = LogisticsStrings.StagingTo(candidate.StageOrbit);
                    Log($"PROC ranked: {rd.ID} x{candidate.Amount:0.#} {candidate.Label} kind={candidate.Kind}");
                    LogBepInEx($"ROUTE chosen: rd={rd.ID} kind={candidate.Kind} label={candidate.Label} score={candidate.Tier} detail={candidate.ScoreBreakdown}");
                    return true;
                }

                ClearRelayState(req);
                return TryCreateFuelBootstrapDelivery(req, requester, rd, blockedFuelType, blockedFuelShortfall, player);
        }

        return false;
    }

    private static bool TryCreateRelayFinalDelivery(Data.LogisticsRequest req, ObjectInfo requester,
        ObjectInfo sourceOrbit, ResourceDefinition rd, double remaining, Company player)
    {
        CountActiveLogisticsCycles(player, out var scActive, out _);
        var carrier = FindBestIdleSpacecraft(sourceOrbit, player, scActive, requireNonContainer: true, out _);
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
            return TryCreateFuelBootstrapDelivery(req, sourceOrbit, rd, blockedFuelType, blockedFuelShortfall, player);
        }

        req.relayStage = Data.RelayStage.WaitingForFinalLeg;
        req.status = Data.LogisticsRequestStatus.InProgress;
        req.statusNote = LogisticsStrings.ShippingFrom(sourceOrbit);
        Log($"RELAY final-leg-dispatch: rd={rd.ID} sourceOrbit={sourceOrbit.ObjectName} target={requester.ObjectName} amount={amount:0.#}");
        return true;
    }

    private static Spacecraft FindBestIdleSpacecraft(ObjectInfo location, Company player,
        Dictionary<string, int> scActive, bool requireNonContainer, out string reason)
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

        var allShips = (MonoBehaviourSingleton<ShipManager>.Instance?.ListAllSpaceShip
                ?? UnityEngine.Object.FindObjectsOfType<Spacecraft>().ToList())
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
            var activeOfType = scActive.TryGetValue(quota.typeName, out var quotaActive) ? quotaActive : 0;
            var canUse = quota.count - activeOfType;
            if (canUse <= 0)
            {
                quotaExhausted = true;
                continue;
            }

            var idleShips = matchingShips
                .Where(sc => sc.CurrentPhase == Spacecraft.EPhase.None
                    && MonoBehaviourSingleton<CycleMissionManager>.Instance.GetCycleMission(sc) == null)
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
        Dictionary<string, int> lvActive, bool requireContainerOnly, bool requireRegularSC,
        out LaunchVehicleType lvType, out Spacecraft carrier, out string reason, out string supportDetail,
        out int supportTierAdjustment)
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
            reason = LogisticsStrings.NoLvQuotaAt(providerOI, DescribeAvailableLaunchSupport(providerOI, player));
            return false;
        }

        var allReadyLV = GetAvailableLaunchSupport(providerOI, player)
            .Where(option => option?.Vehicle != null
                && option.Type != null
                && option.Vehicle.GetCompany() == player
                && option.Vehicle.objectInfo == providerOI
                && option.Vehicle.IsReadyToLaunchReusable())
            .ToList();
        if (allReadyLV.Count == 0)
        {
            reason = LogisticsStrings.NoReadyLvAt(providerOI, DescribeAvailableLaunchSupport(providerOI, player));
            return false;
        }

        var matchingReadyLV = allReadyLV
            .Where(option => lvQuotas.Any(q => Data.LogisticsNetwork.QuotaMatches(q, option.Type.ID, option.Type.Name ?? "LV")))
            .ToList();
        if (matchingReadyLV.Count == 0)
        {
            reason = LogisticsStrings.NoMatchingLvQuotaAt(providerOI, DescribeAvailableLaunchSupport(providerOI, player));
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

        carrier = (MonoBehaviourSingleton<ShipManager>.Instance?.ListAllSpaceShip
                ?? UnityEngine.Object.FindObjectsOfType<Spacecraft>().ToList())
            .Where(sc => sc != null && sc.spacecraftType != null
                && sc.GetCompany() == player
                && sc.CurrentlyOnThisObject == providerOI
                && sc.CurrentPhase == Spacecraft.EPhase.None
                && (!requireRegularSC || !sc.spacecraftType.LowOrbitContainer)
                && MonoBehaviourSingleton<CycleMissionManager>.Instance.GetCycleMission(sc) == null)
            .OrderByDescending(sc => sc.spacecraftType.GetCargoCapacity(player))
            .FirstOrDefault();
        if (carrier == null)
            reason = LogisticsStrings.NoIdleSpacecraftAt(providerOI);
        return carrier != null;
    }

    private static List<LaunchSupportOption> GetAvailableLaunchSupport(ObjectInfo providerOI, Company player)
    {
        if (providerOI == null || player == null)
            return new List<LaunchSupportOption>();

        var objectData = providerOI.GetObjectInfoData(player);
        var rows = providerOI.GetListLaunchVehicle(player);
        if (rows == null)
            return new List<LaunchSupportOption>();
        return rows
            .Where(row => row?.launchVehicle != null && row.launchVehicle.launchVehicleType != null)
            .Select(row =>
            {
                var facility = objectData?.GetFakeLVFromFacilityReverse(row.launchVehicle);
                var category = GetLaunchSupportCategory(providerOI, row.launchVehicle, facility);
                return new LaunchSupportOption
                {
                    Vehicle = row.launchVehicle,
                    Type = row.launchVehicle.launchVehicleType,
                    Facility = facility,
                    Category = category,
                    IsFacilityBacked = facility != null,
                    Label = BuildLaunchSupportLabel(row.launchVehicle, facility, category),
                    TierAdjustment = GetLaunchSupportTierAdjustment(category)
                };
            })
            .GroupBy(option => option.Vehicle?.ID ?? int.MinValue)
            .Select(group => group.First())
            .ToList();
    }

    private static string DescribeAvailableLaunchSupport(ObjectInfo providerOI, Company player)
    {
        var support = GetAvailableLaunchSupport(providerOI, player);
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

    private static Spacecraft PeekCyclicalOrbitalContainer(Company player)
    {
        var carrier = MonoBehaviourSingleton<ShipManager>.Instance?.GetLowOrbitContainer(player);
        if (carrier != null && (carrier.CurrentPhase != Spacecraft.EPhase.None
                || MonoBehaviourSingleton<CycleMissionManager>.Instance.GetCycleMission(carrier) != null))
            carrier = null;
        return carrier;
    }

    private static Spacecraft GetCyclicalOrbitalContainer(Company player)
    {
        var carrier = PeekCyclicalOrbitalContainer(player);
        if (carrier != null)
            return carrier;
        carrier = MonoBehaviourSingleton<ShipManager>.Instance?.AddOrbitalContainerForCyclicalMission(player);
        if (carrier != null && carrier.CurrentPhase == Spacecraft.EPhase.None
            && MonoBehaviourSingleton<CycleMissionManager>.Instance.GetCycleMission(carrier) == null)
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

        var realProvider = sc.CurrentlyOnThisObject;
        if (realProvider == null) return false;

        amount = ClampToOutstandingRequest(req, accountingTargetOI ?? requesterOI, rd, player, amount);
        var capacity = sc.spacecraftType?.GetCargoCapacity(player) ?? 0;
        amount = Math.Min(amount, capacity);
        if (amount <= 0) return false;

        var scList = new List<ISpacecraftInfo> { sc as ISpacecraftInfo };
        if (!BuildCargoManifestWithReturnFuel(req, rd, amount, requesterOI, realProvider, sc, player,
                capacity, out var cargoToB, out var normalCargo, out var reserveFuelCargo,
                out blockedFuelType, out blockedFuelShortfall))
        {
            LogWarning($"SKIP cycle: return fuel reserve could not be manifested for {realProvider?.ObjectName}->{requesterOI?.ObjectName} rd={rd.ID} requested={amount:0.#}");
            return false;
        }
        amount = normalCargo;

        var cmdData = new CycleMissionsDataData
        {
            A = realProvider, B = requesterOI, Company = player,
            CargoStart = ECargoStart.FlyWithWhatIsAvailable, CargoEnd = ECargoStart.FlyWithWhatIsAvailable,
            CargoAllStart = cargoToB, CargoAllEnd = CargoAll.CreateCargoEmpty(),
            LvTypeA = lvTypeA, LvTypeB = null, TransferType = ETransferType.Optimal,
            Ends = EEnds.ResourceCount,
            EndsResourceCountDataA = new EndsResourceCountData(),
            EndsResourceCountMaxA = MakeResourceCount(amount > 0 ? rd : sc.spacecraftType.GetFuelType(), amount > 0 ? amount : reserveFuelCargo),
            EndsResourceCountDataB = new EndsResourceCountData(),
            EndsResourceCountMaxB = new EndsResourceCountData(),
            EndsObjectThisManyTimes = 1,
            ListSC = scList
        };
        var cmd = new CycleMissionsData(cmdData);
        cmd.customNameFromPlanMission = $"[LOGI] {realProvider.ObjectName} -> {requesterOI.ObjectName}";
        _cycleCreatedAt[cmd] = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        MarkPendingPlanningDelivery(pendingTargetOI ?? requesterOI, rd);
        MarkShipForReturn(sc, realProvider, requesterOI, rd);
        MonoBehaviourSingleton<CycleMissionManager>.Instance.AddCycleMission(sc, cmd, scList);

        var label = lvTypeA != null
            ? $"LV+Container: A={realProvider.ObjectName} B={requesterOI.ObjectName} lv={lvTypeA.Name}"
            : $"SC: A={realProvider.ObjectName} B={requesterOI.ObjectName} ship=1";
        Log($"Cycle: {label} rd={rd.ID} targetAmount={amount} reserveFuel={reserveFuelCargo:0.#} manifest={FormatCargo(cargoToB)}");

        req.status = Data.LogisticsRequestStatus.InProgress;

        var ctrl = sc.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
        if (ctrl == null)
            ctrl = sc.gameObject.AddComponent<SpaceCraftCyclicalMissionController>();
        ctrl.CycleMissionPlanFlyWas = false;
        ctrl.SetSC(sc);
        ctrl.TryPlanCycleMission();
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

        var realProvider = providerOI;
        if (realProvider == null) return false;

        amount = ClampToOutstandingRequest(req, accountingTargetOI ?? requesterOI, rd, player, amount);
        var scCapacity = container.spacecraftType?.GetCargoCapacity(player) ?? 0;
        amount = Math.Min(amount, scCapacity);
        if (amount <= 0) return false;

        var scList = new List<ISpacecraftInfo> { container as ISpacecraftInfo };
        if (!BuildCargoManifestWithReturnFuel(req, rd, amount, requesterOI, realProvider, container, player,
                scCapacity, out var cargoToB, out var normalCargo, out var reserveFuelCargo,
                out blockedFuelType, out blockedFuelShortfall))
        {
            LogWarning($"SKIP LV cycle: return fuel reserve could not be manifested for {realProvider?.ObjectName}->{requesterOI?.ObjectName} rd={rd.ID} requested={amount:0.#}");
            return false;
        }
        amount = normalCargo;

        var cmdData = new CycleMissionsDataData
        {
            A = realProvider, B = requesterOI, Company = player,
            CargoStart = ECargoStart.FlyWithWhatIsAvailable, CargoEnd = ECargoStart.FlyWithWhatIsAvailable,
            CargoAllStart = cargoToB, CargoAllEnd = CargoAll.CreateCargoEmpty(),
            LvTypeA = lvTypeA, LvTypeB = null, TransferType = ETransferType.Optimal,
            Ends = EEnds.ResourceCount,
            EndsResourceCountDataA = new EndsResourceCountData(),
            EndsResourceCountMaxA = MakeResourceCount(amount > 0 ? rd : container.spacecraftType.GetFuelType(), amount > 0 ? amount : reserveFuelCargo),
            EndsResourceCountDataB = new EndsResourceCountData(),
            EndsResourceCountMaxB = new EndsResourceCountData(),
            EndsObjectThisManyTimes = 1,
            ListSC = scList
        };
        var cmd = new CycleMissionsData(cmdData);
        cmd.customNameFromPlanMission = $"[LOGI] {realProvider.ObjectName} -> {requesterOI.ObjectName}";
        _cycleCreatedAt[cmd] = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        MarkPendingPlanningDelivery(pendingTargetOI ?? requesterOI, rd);
        MarkShipForReturn(container, realProvider, requesterOI, rd);
        MonoBehaviourSingleton<CycleMissionManager>.Instance.AddCycleMission(container, cmd, scList);

        var isLOC = container.spacecraftType?.LowOrbitContainer == true;
        var label = $"LV+{(isLOC?"Container":"SC")} Cycle: A={realProvider.ObjectName} B={requesterOI.ObjectName} lv={lvTypeA.Name}";
        Log($"Cycle: {label} rd={rd.ID} targetAmount={amount} reserveFuel={reserveFuelCargo:0.#} manifest={FormatCargo(cargoToB)}");

        req.status = Data.LogisticsRequestStatus.InProgress;

        var ctrl = container.gameObject.GetComponent<SpaceCraftCyclicalMissionController>();
        if (ctrl == null)
            ctrl = container.gameObject.AddComponent<SpaceCraftCyclicalMissionController>();
        ctrl.CycleMissionPlanFlyWas = false;
        ctrl.SetSC(container);
        ctrl.TryPlanCycleMission();
        return true;

    }

    private static EndsResourceCountData MakeResourceCount(ResourceDefinition rd, double amount)
    {
        var data = new EndsResourceCountData();
        data.listData.Add(new EndsResourceCountDataPart { rd = rd, count = amount });
        return data;
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
