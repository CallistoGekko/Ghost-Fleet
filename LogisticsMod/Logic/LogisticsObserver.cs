using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomUpdate;
using Data.ScriptableObject;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.VisualizationScripts;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;

namespace LogisticsMod.Logic;

public static class LogisticsObserver
{
    private static StreamWriter _logWriter;
    private static int _logSession;
    private static bool VerboseLogging => LogisticsMod.Plugin.VerboseLogging?.Value ?? false;
    public static bool VerboseLoggingEnabled => VerboseLogging;
    private static readonly Dictionary<string, double> _committedStock = new Dictionary<string, double>();
    private static DateTime _committedStockWallClock;
    private const double HumanLogisticsPayloadMass = 2.0;
    private const double CommittedStockWindowSeconds = 1.0;
    private const double RequestPlanThrottleDays = 3.0;
    private static readonly Dictionary<string, RequestPlanThrottleState> _requestPlanThrottle = new Dictionary<string, RequestPlanThrottleState>();
    private static readonly Dictionary<string, VirtualLiftUsageState> _virtualLiftUsage = new Dictionary<string, VirtualLiftUsageState>();
    private static readonly Dictionary<string, TrajectoryObject> _ghostFlightVisuals = new Dictionary<string, TrajectoryObject>();

    private sealed class GhostLegPlan
    {
        public double Fuel;
        public double TravelDays;
        public double DeltaV;
        public DateTime Departure;
        public DateTime Arrival;
        public ResourceDefinition FuelType;
        public string Reason;
    }

    private sealed class GhostLaunchPlan
    {
        public double PayloadMass;
        public double FacilityCapacityUsed;
        public readonly Dictionary<ResourceDefinition, double> FuelByResource = new Dictionary<ResourceDefinition, double>();
        public readonly List<string> SupportLabels = new List<string>();
        public readonly List<Data.GhostLaunchVehicleRecord> ReservedLaunchVehiclesUsed = new List<Data.GhostLaunchVehicleRecord>();
    }

    private sealed class ResourceRemoval
    {
        public ObjectInfoData Data;
        public ResourceDefinition Resource;
        public double Amount;
    }

    private sealed class GhostDeliveryPlan
    {
        public int RouteId = -1;
        public Data.GhostCraftRecord Craft;
        public SpacecraftType SpacecraftType;
        public ObjectInfo Provider;
        public ObjectInfo Requester;
        public ResourceDefinition Resource;
        public double Amount;
        public ResourceDefinition SupplyResource;
        public double SupplyConsumed;
        public double PayloadCargoMass;
        public GhostLegPlan Outbound;
        public GhostLegPlan ReturnLeg;
        public ResourceDefinition FuelType;
        public GhostLaunchPlan LaunchPlan;
        public double LaunchPayload;
        public bool DestinationRefuel;
        public double ReservedReturnFuel;
        public double OriginFuelTopUp;
        public ObjectInfoData OriginData;
        public ObjectInfoData DestinationData;
    }

    private sealed class RequestPlanThrottleState
    {
        public DateTime NextEvaluation;
        public string Signature;
    }

    private sealed class VirtualLiftUsageState
    {
        public DateTime Date;
        public double UsedPayloadMass;
    }

    private sealed class VirtualLiftPlan
    {
        public double PayloadAmount;
        public ResourceDefinition SupplyResource;
        public double SupplyConsumed;
        public double FacilityCapacityUsed;
        public double SharedFacilityCapacityUsed;
        public readonly Dictionary<ResourceDefinition, double> FuelByResource = new Dictionary<ResourceDefinition, double>();
        public readonly List<string> SupportLabels = new List<string>();
        public readonly Dictionary<Data.GhostLaunchVehicleRecord, double> ReservedLaunchCapacityByVehicle = new Dictionary<Data.GhostLaunchVehicleRecord, double>();
    }

    private sealed class VirtualLiftDemand
    {
        public Data.LogisticsRequest Request;
        public ObjectInfo Provider;
        public ObjectInfo Requester;
        public ResourceDefinition Resource;
        public double Remaining;
    }

    private sealed class RouteVirtualLiftDemand
    {
        public Data.LogisticsRouteRecord Route;
        public Data.LogisticsRouteResourceRule Rule;
        public ObjectInfo Source;
        public ObjectInfo Destination;
        public ResourceDefinition Resource;
        public double Remaining;
    }

    private sealed class RouteResourceDispatchOrderItem
    {
        public Data.LogisticsRouteResourceRule Rule;
        public ResourceDefinition Resource;
        public string ResourceName;
        public int Priority;
        public double FillRatio = 1;
        public double Outstanding;
        public double Available;
        public int MaxGhostDispatches = int.MaxValue;

        public bool HasDispatchableDemand =>
            Rule != null && Resource != null && Outstanding > 0.001 && Available > 0.001;
    }

    private sealed class RouteConvoyResourceState
    {
        public RouteResourceDispatchOrderItem Item;
        public double Remaining;
        public int DispatchCount;
    }

    private sealed class RouteLiftCapacityState
    {
        public readonly Dictionary<Data.GhostLaunchVehicleRecord, double> ReservedCapacityUsed = new Dictionary<Data.GhostLaunchVehicleRecord, double>();

        public double GetReservedUsed(Data.GhostLaunchVehicleRecord record)
        {
            if (record == null)
                return 0;
            ReservedCapacityUsed.TryGetValue(record, out var used);
            return used;
        }

        public void Commit(VirtualLiftPlan plan)
        {
            if (plan == null || plan.ReservedLaunchCapacityByVehicle.Count == 0)
                return;

            foreach (var kv in plan.ReservedLaunchCapacityByVehicle)
            {
                if (kv.Key == null || kv.Value <= 0)
                    continue;
                ReservedCapacityUsed.TryGetValue(kv.Key, out var existing);
                ReservedCapacityUsed[kv.Key] = existing + kv.Value;
            }
        }
    }

    private sealed class PlannerSnapshot
    {
        public List<ObjectInfo> Objects = new List<ObjectInfo>();
        public Dictionary<int, List<LaunchSupportOption>> LaunchSupportByObjectId = new Dictionary<int, List<LaunchSupportOption>>();
    }

    private sealed class LaunchSupportOption
    {
        public LaunchVehicle Vehicle;
        public LaunchVehicleType Type;
        public Facility Facility;
        public string Category;
        public string Label;
        public bool IsFacilityBacked;
        public Data.GhostLaunchVehicleRecord ReservedLaunchVehicle;
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

    public static void LogAlways(string msg)
    {
        WriteLog("", msg);
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

        return snapshot;
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
        var committedCount = _committedStock.Count;
        var throttleCount = _requestPlanThrottle.Count;
        var virtualLiftCount = _virtualLiftUsage.Count;
        var ghostVisualCount = _ghostFlightVisuals.Count;
        _committedStock.Clear();
        _requestPlanThrottle.Clear();
        _virtualLiftUsage.Clear();
        foreach (var visual in _ghostFlightVisuals.Values.ToList())
        {
            if (visual != null)
                UnityEngine.Object.Destroy(visual.gameObject);
        }
        _ghostFlightVisuals.Clear();
        Log($"RESET runtime-state: committed={committedCount} throttles={throttleCount} virtualLift={virtualLiftCount} ghostVisuals={ghostVisualCount}");
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
        Data.LogisticsNetwork.ReleaseOrphanedRouteAssets();
        RecoverBlockedReturnFuelCraft(player);
        ProcessGhostFlights(player, snapshot);
        ProcessRoutes(player, snapshot);
    }

    private static void ProcessRoutes(Company player, PlannerSnapshot snapshot)
    {
        if (player == null)
            return;

        foreach (var route in Data.LogisticsNetwork.GetAllRoutes()
                     .Where(route => route != null && route.isActive)
                     .OrderBy(route => route.sourceObjectId)
                     .ThenBy(route => route.destinationObjectId)
                     .ThenBy(route => route.routeId))
        {
            var source = ResolveObject(route.sourceObjectId);
            var destination = ResolveObject(route.destinationObjectId);
            if (source == null || destination == null)
            {
                route.statusNote = "Route body unavailable";
                continue;
            }

            route.statusNote = null;
            if (route.resources == null || route.resources.Count == 0)
            {
                route.statusNote = "No resources on route";
                continue;
            }

            var useBalancedRouteLift = VirtualSurfaceLiftEnabled() && IsOrbitOf(destination, source);
            if (useBalancedRouteLift)
                ApplyBalancedRouteVirtualSurfaceLift(route, source, destination, player, snapshot);

            var orderedResources = BuildRouteResourceDispatchOrder(route, source, destination, player);
            foreach (var item in orderedResources)
            {
                var rule = item.Rule;
                var rd = item.Resource;
                rule.ResourceDefinition = rd;
                if (rd == null)
                {
                    rule.statusNote = "Resource unavailable";
                    continue;
                }

                ProcessRouteResource(route, rule, source, destination, rd, player, snapshot,
                    allowVirtualSurfaceLift: !useBalancedRouteLift);
            }

            var convoyReason = TryCreateRouteGhostConvoys(route, source, destination, orderedResources, player, snapshot);
            route.statusNote = string.IsNullOrWhiteSpace(convoyReason) ? null : convoyReason;
        }
    }

    private static List<RouteResourceDispatchOrderItem> BuildRouteResourceDispatchOrder(Data.LogisticsRouteRecord route,
        ObjectInfo source, ObjectInfo destination, Company player)
    {
        var sourceData = source?.GetObjectInfoData(player);
        var destinationData = destination?.GetObjectInfoData(player);
        var result = new List<RouteResourceDispatchOrderItem>();

        foreach (var rule in route.resources.Where(rule => rule != null && rule.isActive))
        {
            var rd = rule.ResourceDefinition ?? ResolveResource(rule.resourceDef?.id);
            rule.ResourceDefinition = rd;
            var item = new RouteResourceDispatchOrderItem
            {
                Rule = rule,
                Resource = rd,
                ResourceName = ResourceName(rd),
                Priority = NormalizeRoutePriority(rule.priority)
            };

            if (rd != null && sourceData != null && destinationData != null)
            {
                var target = Math.Max(0, rule.destinationTarget);
                var destinationStock = destinationData.CheckResources(rd);
                var inFlight = GetRouteInFlightDeliveryAmount(route, rd);
                item.Outstanding = Math.Max(0, target - destinationStock - inFlight);
                item.Available = GetRouteSourceAvailableAfterKeep(source, rd, rule.sourceKeep, player);
                item.FillRatio = target <= 0.001
                    ? 1
                    : Math.Max(0, Math.Min(1, (destinationStock + inFlight) / target));
            }

            result.Add(item);
        }

        return result
            .OrderBy(item => item.Resource == null ? 1 : 0)
            .ThenBy(item => item.HasDispatchableDemand ? 0 : 1)
            .ThenByDescending(item => item.Priority)
            .ThenBy(item => item.FillRatio)
            .ThenByDescending(item => item.Outstanding)
            .ThenBy(item => item.ResourceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int NormalizeRoutePriority(int priority)
    {
        return Math.Max(-1, Math.Min(2, priority));
    }

    private static double GetRoutePriorityDispatchWeight(int priority)
    {
        switch (NormalizeRoutePriority(priority))
        {
            case 2: return 4;
            case 1: return 2;
            case -1: return 0.5;
            default: return 1;
        }
    }

    private static void ProcessRouteResource(Data.LogisticsRouteRecord route, Data.LogisticsRouteResourceRule rule,
        ObjectInfo source, ObjectInfo destination, ResourceDefinition rd, Company player, PlannerSnapshot snapshot,
        bool allowVirtualSurfaceLift = true)
    {
        var sourceData = source?.GetObjectInfoData(player);
        var destinationData = destination?.GetObjectInfoData(player);
        if (sourceData == null || destinationData == null)
        {
            rule.statusNote = "Route stockpile unavailable";
            return;
        }

        var destinationStock = destinationData.CheckResources(rd);
        var inFlight = GetRouteInFlightDeliveryAmount(route, rd);
        var remaining = Math.Max(0, rule.destinationTarget - destinationStock - inFlight);
        if (remaining <= 0.001)
        {
            rule.statusNote = "Target stocked";
            return;
        }

        var available = GetRouteSourceAvailableAfterKeep(source, rd, rule.sourceKeep, player);
        if (available <= 0.001)
        {
            rule.statusNote = "Waiting for surplus";
            return;
        }

        var desired = Math.Min(remaining, available);

        if (IsOrbitOf(source, destination))
        {
            var amount = Math.Min(desired, available);
            if (amount > 0 && ApplyDirectResourceTransfer(source, destination, rd, amount, player, "ROUTE-DROP"))
            {
                CommitStock(source, rd, amount);
                rule.statusNote = $"Dropped {amount:0.#}";
                Log($"ROUTE-DROP: route={route.routeId} {source.ObjectName}->{destination.ObjectName} rd={rd.ID} amount={amount:0.#}");
                return;
            }
        }

        if (allowVirtualSurfaceLift && VirtualSurfaceLiftEnabled() && IsOrbitOf(destination, source))
        {
            var support = GetVirtualSurfaceLiftSupport(source, player, snapshot);
            if (support.Count > 0
                && TryBuildVirtualLiftPlan(source, destination, rd, desired, available, player, support, out var liftPlan)
                && liftPlan.PayloadAmount > 0
                && ApplyVirtualLiftResourceChanges(source, destination, rd, player, liftPlan))
            {
                CommitVirtualLiftUsage(source, player, liftPlan.FacilityCapacityUsed);
                CommitStock(source, rd, liftPlan.PayloadAmount);
                if (liftPlan.SupplyResource != null && liftPlan.SupplyConsumed > 0)
                    CommitStock(source, liftPlan.SupplyResource, liftPlan.SupplyConsumed);
                rule.statusNote = $"Lifted {liftPlan.PayloadAmount:0.#}";
                var fuelText = FormatVirtualLiftFuel(liftPlan);
                Log($"ROUTE-LIFT: route={route.routeId} {source.ObjectName}->{destination.ObjectName} rd={rd.ID} amount={liftPlan.PayloadAmount:0.#}{FormatVirtualLiftSupply(liftPlan)} fuel={fuelText}");
                return;
            }
        }

        rule.statusNote = "Waiting for convoy launch";
    }

    private static void ApplyBalancedRouteVirtualSurfaceLift(Data.LogisticsRouteRecord route,
        ObjectInfo source, ObjectInfo destination, Company player, PlannerSnapshot snapshot)
    {
        if (route == null || source == null || destination == null || player == null)
            return;

        var support = GetRouteSurfaceLiftSupport(route, source, player, snapshot);
        if (support.Count == 0)
            return;

        var capacityState = new RouteLiftCapacityState();
        var capacityLeft = GetRouteSurfaceLiftCapacityLeft(source, player, support, capacityState);
        if (capacityLeft <= 0.001)
            return;

        var sourceData = source.GetObjectInfoData(player);
        var destinationData = destination.GetObjectInfoData(player);
        if (sourceData == null || destinationData == null)
            return;

        var demands = new List<RouteVirtualLiftDemand>();
        foreach (var rule in route.resources ?? new List<Data.LogisticsRouteResourceRule>())
        {
            if (rule == null || !rule.isActive)
                continue;

            var rd = rule.ResourceDefinition ?? ResolveResource(rule.resourceDef?.id);
            rule.ResourceDefinition = rd;
            if (rd == null)
                continue;

            var destinationStock = destinationData.CheckResources(rd);
            var inFlight = GetRouteInFlightDeliveryAmount(route, rd);
            var remaining = Math.Max(0, rule.destinationTarget - destinationStock - inFlight);
            if (remaining <= 0.001)
                continue;

            var available = GetRouteSourceAvailableAfterKeep(source, rd, rule.sourceKeep, player);
            if (available <= 0.001)
                continue;
            if (!HasEligibleRouteSurfaceLiftSupport(support, source, player, rd))
            {
                rule.statusNote = IsHumanResource(rd)
                    ? "Needs crew-safe launch vehicle or spacecraft"
                    : "No eligible launch capacity";
                continue;
            }

            demands.Add(new RouteVirtualLiftDemand
            {
                Route = route,
                Rule = rule,
                Source = source,
                Destination = destination,
                Resource = rd,
                Remaining = Math.Min(remaining, available)
            });
        }

        if (demands.Count == 0)
            return;

        ApplyBalancedRouteVirtualSurfaceLiftGroup(demands, capacityLeft, support, capacityState, player);
    }

    private static void ApplyBalancedRouteVirtualSurfaceLiftGroup(List<RouteVirtualLiftDemand> demands,
        double capacityLeft, List<LaunchSupportOption> support, RouteLiftCapacityState capacityState, Company player)
    {
        if (demands == null || demands.Count == 0 || capacityLeft <= 0.001 || support == null || support.Count == 0)
            return;
        capacityState ??= new RouteLiftCapacityState();

        var liftedByRule = new Dictionary<Data.LogisticsRouteResourceRule, double>();
        var guard = 0;
        while (capacityLeft > 0.001 && guard++ < 16)
        {
            var active = demands
                .Where(d => d != null
                    && d.Remaining > 0.001
                    && GetRouteSurfaceLiftCapacityLeftForResource(d.Source, player, support, capacityState, d.Resource) > 0.001
                    && GetRouteSourceAvailableAfterKeep(d.Source, d.Resource, d.Rule.sourceKeep, player) > 0.001)
                .ToList();
            if (active.Count == 0)
                break;

            var share = capacityLeft / active.Count;
            var movedThisRound = 0.0;
            foreach (var demand in active)
            {
                if (capacityLeft <= 0.001)
                    break;

                var sourceAvailable = GetRouteSourceAvailableAfterKeep(demand.Source, demand.Resource, demand.Rule.sourceKeep, player);
                var desired = Math.Min(Math.Min(share, demand.Remaining), sourceAvailable);
                if (desired <= 0.001)
                    continue;

                if (!TryBuildRouteSurfaceLiftPlan(demand.Source, demand.Destination, demand.Resource,
                        desired, sourceAvailable, player, support, capacityState, out var plan)
                    || plan.PayloadAmount <= 0)
                    continue;

                if (!ApplyVirtualLiftResourceChanges(demand.Source, demand.Destination, demand.Resource, player, plan))
                    continue;

                if (plan.SharedFacilityCapacityUsed > 0.001)
                    CommitVirtualLiftUsage(demand.Source, player, plan.SharedFacilityCapacityUsed);
                capacityState.Commit(plan);
                demand.Remaining = Math.Max(0, demand.Remaining - plan.PayloadAmount);
                capacityLeft = Math.Max(0, capacityLeft - plan.FacilityCapacityUsed);
                movedThisRound += plan.PayloadAmount;

                liftedByRule.TryGetValue(demand.Rule, out var existingLifted);
                liftedByRule[demand.Rule] = existingLifted + plan.PayloadAmount;
                demand.Rule.statusNote = $"Lifted {liftedByRule[demand.Rule]:0.#}";

                var fuelText = FormatVirtualLiftFuel(plan);
                Log($"ROUTE-LIFT balanced: route={demand.Route.routeId} {demand.Source.ObjectName}->{demand.Destination.ObjectName} rd={demand.Resource.ID} amount={plan.PayloadAmount:0.#}{FormatVirtualLiftSupply(plan)} fuel={fuelText} capacityUsed={plan.FacilityCapacityUsed:0.#}");
            }

            if (movedThisRound <= 0.001)
                break;
        }

        var source = demands.FirstOrDefault(d => d?.Source != null)?.Source;
        MarkRouteReservedLaunchVehiclesUsed(source, player, capacityState.ReservedCapacityUsed.Keys);
    }

    private static double GetRouteInFlightDeliveryAmount(Data.LogisticsRouteRecord route, ResourceDefinition rd)
    {
        if (route == null || rd == null)
            return 0;

        return Data.LogisticsNetwork.GetAllGhostFlights()
            .Where(f => f != null
                && !f.isReturnFlight
                && f.routeId == route.routeId
                && (f.status == Data.GhostFlightStatus.Outbound || f.status == Data.GhostFlightStatus.Planned))
            .Sum(f => GetGhostFlightCargoAmount(f, rd));
    }

    private static double GetRouteSourceAvailableAfterKeep(ObjectInfo source, ResourceDefinition rd,
        double sourceKeep, Company player)
    {
        var sourceData = source?.GetObjectInfoData(player);
        if (sourceData == null || rd == null)
            return 0;

        return Math.Max(0, sourceData.CheckResources(rd) - Math.Max(0, sourceKeep) - GetCommittedStock(source, rd));
    }

    private static string TryCreateRouteGhostConvoys(Data.LogisticsRouteRecord route, ObjectInfo source,
        ObjectInfo destination, List<RouteResourceDispatchOrderItem> orderedResources, Company player,
        PlannerSnapshot snapshot)
    {
        if (route == null || source == null || destination == null || player == null)
            return "Route unavailable";

        var states = (orderedResources ?? new List<RouteResourceDispatchOrderItem>())
            .Where(item => item != null && item.HasDispatchableDemand)
            .Select(item => new RouteConvoyResourceState
            {
                Item = item,
                Remaining = Math.Min(item.Outstanding, item.Available)
            })
            .Where(state => state.Remaining > 0.001)
            .ToList();
        if (states.Count == 0)
            return null;

        var craftCandidates = FindIdleRouteGhostCraftCandidates(route, source, destination, player, out var craftReason);
        if (craftCandidates.Count == 0)
        {
            foreach (var state in states)
                state.Item.Rule.statusNote = craftReason;
            return craftReason;
        }

        FilterRouteStatesForFullLoads(states, craftCandidates, source, destination, player);
        if (states.Count == 0)
            return "Waiting for full load";

        AllocateRouteDispatchCapacity(states, craftCandidates.Count);

        var plans = new List<GhostDeliveryPlan>();
        var plannedCargoByResource = new Dictionary<string, double>();
        string bestReason = null;

        foreach (var craft in craftCandidates)
        {
            if (states.All(state => state.Remaining <= 0.001
                    || state.DispatchCount >= state.Item.MaxGhostDispatches))
                break;

            GhostDeliveryPlan selectedPlan = null;
            RouteConvoyResourceState selectedState = null;
            foreach (var state in states)
            {
                if (state.Remaining <= 0.001 || state.DispatchCount >= state.Item.MaxGhostDispatches)
                    continue;

                var rd = state.Item.Resource;
                var rule = state.Item.Rule;
                if (rd == null || rule == null)
                    continue;

                plannedCargoByResource.TryGetValue(rd.ID, out var plannedCargo);
                var sourceAvailable = Math.Max(0,
                    GetRouteSourceAvailableAfterKeep(source, rd, rule.sourceKeep, player) - plannedCargo);
                var desired = Math.Min(state.Remaining, sourceAvailable);
                if (desired <= 0.001)
                    continue;

                if (TryBuildGhostDeliveryPlan(craft, source, destination, rd, desired, player, snapshot,
                        out var plan, out var reason, route.routeId))
                {
                    selectedPlan = plan;
                    selectedState = state;
                    break;
                }

                if (!string.IsNullOrWhiteSpace(reason))
                {
                    bestReason = reason;
                    rule.statusNote = reason;
                }
            }

            if (selectedPlan == null || selectedState == null)
                continue;

            plans.Add(selectedPlan);
            selectedState.Remaining = Math.Max(0, selectedState.Remaining - selectedPlan.Amount);
            selectedState.DispatchCount++;
            plannedCargoByResource.TryGetValue(selectedPlan.Resource.ID, out var existingCargo);
            plannedCargoByResource[selectedPlan.Resource.ID] = existingCargo + selectedPlan.Amount;
        }

        if (plans.Count == 0)
            return bestReason ?? "No route convoy dispatched";

        if (!TryCommitGhostDeliveryConvoys(plans, player, out var commitReason))
            return commitReason ?? bestReason ?? "Could not commit route convoy";

        foreach (var state in states)
        {
            if (state.DispatchCount > 0)
                state.Item.Rule.statusNote = state.DispatchCount == 1
                    ? "Convoy scheduled"
                    : $"Convoy scheduled ({state.DispatchCount} ships)";
            else if (string.IsNullOrWhiteSpace(state.Item.Rule.statusNote)
                || state.Item.Rule.statusNote == "Waiting for convoy launch")
                state.Item.Rule.statusNote = bestReason ?? "Waiting for convoy capacity";
        }

        Log($"GHOST route-convoy: route={route.routeId} {source.ObjectName}->{destination.ObjectName} ships={plans.Count} manifest={FormatGhostPlanManifestForLog(plans)}");
        return null;
    }

    private static void FilterRouteStatesForFullLoads(List<RouteConvoyResourceState> states,
        List<Data.GhostCraftRecord> craftCandidates, ObjectInfo source, ObjectInfo destination, Company player)
    {
        if (states == null || states.Count == 0)
            return;

        for (var i = states.Count - 1; i >= 0; i--)
        {
            var state = states[i];
            if (state?.Item?.Rule == null || state.Item.Resource == null)
            {
                states.RemoveAt(i);
                continue;
            }

            if (!TryGetMinimumRouteFullLoadAmount(state.Item.Resource, craftCandidates, source, destination, player,
                    out var minimumFullLoad))
                continue;

            var desired = Math.Min(state.Remaining, state.Item.Available);
            if (desired + 0.001 >= minimumFullLoad)
                continue;

            state.Item.Rule.statusNote = "Waiting for full load";
            states.RemoveAt(i);
        }
    }

    private static bool TryGetMinimumRouteFullLoadAmount(ResourceDefinition rd, List<Data.GhostCraftRecord> craftCandidates,
        ObjectInfo source, ObjectInfo destination, Company player, out double minimumFullLoad)
    {
        minimumFullLoad = double.MaxValue;
        if (rd == null || craftCandidates == null || craftCandidates.Count == 0)
            return false;

        var travelDays = EstimateGhostTravelDays(source, destination);
        var supplyResource = IsHumanResource(rd) ? ResolveSupplyResource() : null;
        var supplyPerHuman = IsHumanResource(rd) && supplyResource != null
            ? EstimateCrewSupplyNeed(1, travelDays, player)
            : 0;
        var payloadMassPerUnit = GetPayloadMassPerResourceUnit(rd, supplyPerHuman);
        if (payloadMassPerUnit <= 0.001)
            return false;

        foreach (var craft in craftCandidates)
        {
            var scType = ResolveSpacecraftType(craft?.shipTypeId);
            if (scType == null)
                continue;

            var capacity = scType.GetCargoCapacity(player);
            if (capacity <= 0.001)
                continue;

            var fullLoad = capacity / payloadMassPerUnit;
            if (IsHumanResource(rd))
                fullLoad = Math.Floor(fullLoad);
            if (fullLoad > 0.001)
                minimumFullLoad = Math.Min(minimumFullLoad, fullLoad);
        }

        return minimumFullLoad < double.MaxValue;
    }

    private static void AllocateRouteDispatchCapacity(List<RouteConvoyResourceState> states, int idleCraftCount)
    {
        if (states == null || states.Count == 0)
            return;

        foreach (var state in states)
            if (state?.Item != null)
                state.Item.MaxGhostDispatches = int.MaxValue;

        if (states.Count <= 1 || idleCraftCount <= 0)
            return;

        var totalWeight = states.Sum(state => GetRoutePriorityDispatchWeight(state.Item.Priority));
        foreach (var state in states)
        {
            var share = totalWeight <= 0
                ? (double)idleCraftCount / states.Count
                : idleCraftCount * GetRoutePriorityDispatchWeight(state.Item.Priority) / totalWeight;
            state.Item.MaxGhostDispatches = Math.Max(1, (int)Math.Ceiling(share));
        }
    }

    private static bool TryCommitGhostDeliveryConvoys(List<GhostDeliveryPlan> plans, Company player, out string reason)
    {
        reason = null;
        if (plans == null || plans.Count == 0)
        {
            reason = "No convoy plans";
            return false;
        }

        var committedAny = false;
        foreach (var group in plans
                     .Where(plan => plan?.Craft != null && plan.Provider != null && plan.Requester != null)
                     .GroupBy(plan =>
                     {
                         var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
                         var departure = GetGhostLegDeparture(plan.Outbound, now);
                         var arrival = GetGhostLegArrival(plan.Outbound, departure);
                         return new
                         {
                             plan.RouteId,
                             HomeObjectId = plan.Craft.homeObjectId,
                             FromObjectId = plan.Provider.id,
                             ToObjectId = plan.Requester.id,
                             ShipTypeId = plan.Craft.shipTypeId ?? "",
                             FuelResourceId = plan.FuelType?.ID ?? "",
                             plan.DestinationRefuel,
                             Departure = departure,
                             Arrival = arrival
                         };
                     }))
        {
            if (TryCommitGhostDeliveryConvoy(group.ToList(), player, out reason))
                committedAny = true;
        }

        return committedAny;
    }

    private static bool TryCommitGhostDeliveryConvoy(List<GhostDeliveryPlan> plans, Company player, out string reason)
    {
        reason = null;
        plans = plans?.Where(plan => plan != null).ToList();
        if (plans == null || plans.Count == 0)
        {
            reason = "Convoy plan is unavailable";
            return false;
        }

        var first = plans[0];
        if (first.Craft == null || first.Provider == null || first.Requester == null || first.Resource == null)
        {
            reason = "Convoy plan is incomplete";
            return false;
        }

        if (!NormalizeConvoyPlanFuelToVanillaGroup(plans, player, out reason))
            return false;

        var removals = plans.SelectMany(BuildGhostDeliveryPlanRemovals).ToList();
        if (!TryApplyResourceRemovals(removals, out reason))
            return false;

        foreach (var plan in plans)
        {
            plan.Craft.tankFuel = Math.Min(plan.Craft.tankFuelCapacity, plan.Craft.tankFuel + plan.OriginFuelTopUp);
            plan.Craft.tankFuel = Math.Max(0, plan.Craft.tankFuel - plan.Outbound.Fuel);
            CommitStock(plan.Provider, plan.Resource, plan.Amount);
            if (plan.SupplyResource != null && plan.SupplyConsumed > 0)
                CommitStock(plan.Provider, plan.SupplyResource, plan.SupplyConsumed);
            if (plan.LaunchPlan != null)
            {
                CommitVirtualLiftUsage(plan.Provider, player, plan.LaunchPlan.FacilityCapacityUsed);
                MarkReservedLaunchVehiclesUsed(plan.Provider, player, plan.LaunchPlan);
            }
        }

        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        var outboundDeparture = GetGhostLegDeparture(first.Outbound, now);
        var outboundArrival = GetGhostLegArrival(first.Outbound, outboundDeparture);
        var outboundTravelDays = Math.Max(1, (outboundArrival - outboundDeparture).TotalDays);
        var ownerOI = ResolveObject(first.Craft.homeObjectId) ?? first.Provider;
        var ownerData = Data.LogisticsNetwork.GetOrCreate(ownerOI);
        var flight = new Data.GhostFlightRecord
        {
            flightId = Guid.NewGuid().ToString("N"),
            routeId = first.RouteId,
            craftLedgerIds = plans
                .Select(plan => plan.Craft?.ledgerId ?? 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList(),
            homeObjectId = first.Craft.homeObjectId,
            fromObjectId = first.Provider.id,
            toObjectId = first.Requester.id,
            cargoManifest = BuildGhostFlightCargoManifest(plans),
            outboundFuel = plans.Sum(plan => Math.Max(0, plan.Outbound?.Fuel ?? 0)),
            returnFuel = plans.Sum(plan => Math.Max(0, plan.ReturnLeg?.Fuel ?? 0)),
            launchFuel = plans.Sum(plan => plan.LaunchPlan?.FuelByResource.Values.Sum() ?? 0),
            reservedReturnFuel = plans.Sum(plan => Math.Max(0, plan.ReservedReturnFuel)),
            fuelResourceId = first.FuelType?.ID,
            destinationRefuel = first.DestinationRefuel,
            launchPayloadMass = plans.Sum(plan => Math.Max(0, plan.LaunchPayload)),
            outboundTravelDays = outboundTravelDays,
            returnTravelDays = Math.Max(1, plans.Max(plan => plan.ReturnLeg?.TravelDays ?? 1)),
            departureDate = outboundDeparture,
            arrivalDate = outboundArrival,
            status = outboundDeparture > now ? Data.GhostFlightStatus.Planned : Data.GhostFlightStatus.Outbound,
            isReturnFlight = false
        };
        ownerData.ghostFlights.Add(flight);

        foreach (var plan in plans)
        {
            plan.Craft.status = outboundDeparture > now
                ? Data.GhostCraftStatus.PlanningOutbound
                : Data.GhostCraftStatus.Outbound;
            plan.Craft.currentFlightId = flight.flightId;
            plan.Craft.routeFromObjectId = plan.Provider.id;
            plan.Craft.routeToObjectId = plan.Requester.id;
            plan.Craft.departureDate = flight.departureDate;
            plan.Craft.arrivalDate = flight.arrivalDate;
            plan.Craft.cargoResourceId = plan.Resource.ID;
            plan.Craft.cargoAmount = plan.Amount;
            plan.Craft.blockedReason = null;
        }

        flight = MergeCompatibleGhostFlight(ownerData, flight);
        EnsureGhostFlightVisual(flight, player);

        var returnSource = first.DestinationRefuel ? $"reserved at {first.Requester.ObjectName}" : "tank round trip";
        Log($"GHOST convoy-dispatch: ships={plans.Count} {first.Provider.ObjectName}->{first.Requester.ObjectName} manifest={FormatGhostFlightManifestForLog(flight)} fuelOut={flight.outboundFuel:0.#} fuelBack={flight.returnFuel:0.#} returnFuel={returnSource} launchPayload={flight.launchPayloadMass:0.#} arrive={flight.arrivalDate:yyyy-MM-dd}");
        return true;
    }

    private static bool NormalizeConvoyPlanFuelToVanillaGroup(List<GhostDeliveryPlan> plans, Company player,
        out string reason)
    {
        reason = null;
        plans = plans?.Where(plan => plan != null).ToList();
        if (plans == null || plans.Count == 0)
        {
            reason = "Convoy plan is unavailable";
            return false;
        }

        var first = plans[0];
        var scType = first.SpacecraftType ?? ResolveSpacecraftType(first.Craft?.shipTypeId);
        if (scType == null || player == null || scType.SolarSC)
            return true;

        var craftCount = plans
            .Select(plan => plan.Craft?.ledgerId ?? 0)
            .Where(id => id > 0)
            .Distinct()
            .Count();
        if (craftCount <= 0)
            craftCount = plans.Count;

        var outboundDeltaV = plans
            .Select(plan => Math.Max(0.0, plan.Outbound?.DeltaV ?? 0.0))
            .DefaultIfEmpty(0.0)
            .Max();
        var returnDeltaV = plans
            .Select(plan => Math.Max(0.0, plan.ReturnLeg?.DeltaV ?? 0.0))
            .DefaultIfEmpty(0.0)
            .Max();
        if (outboundDeltaV <= 0.001 && returnDeltaV <= 0.001)
            return true;

        var dryMass = Math.Max(1.0, scType.GetMass(player));
        var exhaustVelocity = Math.Max(0.001, scType.GetExhaustV(player));
        var powVariable = GetMissionFuelPowVariable();
        var outboundMass = LogisticsVanillaMissionMath.CalculateMassToFuel(
            dryMass,
            plans.Sum(plan => Math.Max(0.0, plan.PayloadCargoMass)),
            craftCount);
        var returnMass = LogisticsVanillaMissionMath.CalculateMassToFuel(dryMass, 0.0, craftCount);
        var groupedOutboundFuel = LogisticsVanillaMissionMath.CalculateTotalPropellantNeeded(
            outboundMass,
            outboundDeltaV,
            exhaustVelocity,
            powVariable);
        var groupedReturnFuel = LogisticsVanillaMissionMath.CalculateTotalPropellantNeeded(
            returnMass,
            returnDeltaV,
            exhaustVelocity,
            powVariable);
        LogVerbose($"ROUTE-MISSION step=fuel-grouped route={first.RouteId} ships={craftCount} ship={scType.ID} dry={dryMass:0.###} cargo={plans.Sum(plan => Math.Max(0.0, plan.PayloadCargoMass)):0.###} outboundMass={outboundMass:0.###} returnMass={returnMass:0.###} dVOut={outboundDeltaV:0.###} dVBack={returnDeltaV:0.###} exhaust={exhaustVelocity:0.###} pow={powVariable:0.###} fuelOut={groupedOutboundFuel:0.###} fuelBack={groupedReturnFuel:0.###}");

        DistributeGroupedLegFuel(plans, plan => plan.Outbound, groupedOutboundFuel);
        DistributeGroupedLegFuel(plans, plan => plan.ReturnLeg, groupedReturnFuel);

        var fuelType = first.FuelType ?? scType.GetFuelType();
        var destinationData = first.DestinationData ?? first.Requester?.GetObjectInfoData(player);
        var destinationRefuel = fuelType != null
            && groupedReturnFuel > 0.001
            && destinationData != null
            && destinationData.CheckResources(fuelType) + 0.001 >= groupedReturnFuel;

        foreach (var plan in plans)
        {
            plan.FuelType = fuelType;
            plan.DestinationRefuel = destinationRefuel;
            plan.ReservedReturnFuel = destinationRefuel ? Math.Max(0.0, plan.ReturnLeg?.Fuel ?? 0.0) : 0.0;

            var requiredTankAtDeparture = destinationRefuel
                ? Math.Max(0.0, plan.Outbound?.Fuel ?? 0.0)
                : Math.Max(0.0, plan.Outbound?.Fuel ?? 0.0) + Math.Max(0.0, plan.ReturnLeg?.Fuel ?? 0.0);
            if (requiredTankAtDeparture > plan.Craft.tankFuelCapacity + 0.001)
            {
                reason = $"Ghost craft tank too small for {plan.Provider.ObjectName}->{plan.Requester.ObjectName}->{plan.Provider.ObjectName}";
                return false;
            }

            plan.OriginFuelTopUp = Math.Max(0.0, requiredTankAtDeparture - plan.Craft.tankFuel);
        }

        if (plans.Count > 1)
            LogVerbose($"GHOST convoy-fuel-grouped: ships={craftCount} dry={dryMass:0.###} cargo={plans.Sum(plan => Math.Max(0.0, plan.PayloadCargoMass)):0.#} massOut={outboundMass:0.###} exhaust={exhaustVelocity:0.###} pow={powVariable:0.###} fuelOut={groupedOutboundFuel:0.#} fuelBack={groupedReturnFuel:0.#} dVOut={outboundDeltaV:0.##} dVBack={returnDeltaV:0.##}");
        return true;
    }

    private static void DistributeGroupedLegFuel(List<GhostDeliveryPlan> plans, Func<GhostDeliveryPlan, GhostLegPlan> legSelector,
        double groupedFuel)
    {
        if (plans == null || plans.Count == 0 || legSelector == null)
            return;

        groupedFuel = Math.Max(0.0, groupedFuel);
        var currentTotal = plans.Sum(plan => Math.Max(0.0, legSelector(plan)?.Fuel ?? 0.0));
        var remaining = groupedFuel;
        for (var i = 0; i < plans.Count; i++)
        {
            var leg = legSelector(plans[i]);
            if (leg == null)
                continue;

            var share = i == plans.Count - 1
                ? remaining
                : (currentTotal > 0.001
                    ? groupedFuel * Math.Max(0.0, leg.Fuel) / currentTotal
                    : groupedFuel / plans.Count);
            share = Math.Max(0.0, share);
            leg.Fuel = share;
            remaining -= share;
        }
    }

    private static double GetMissionFuelPowVariable()
    {
        var economic = MonoBehaviourSingleton<GameManager>.Instance?.Economic;
        return Math.Max(0.001, economic?.PowVariable ?? 2.0);
    }

    private static List<ResourceRemoval> BuildGhostDeliveryPlanRemovals(GhostDeliveryPlan plan)
    {
        var removals = new List<ResourceRemoval>();
        if (plan == null)
            return removals;

        removals.Add(new ResourceRemoval { Data = plan.OriginData, Resource = plan.Resource, Amount = plan.Amount });
        if (plan.SupplyResource != null && plan.SupplyConsumed > 0)
            removals.Add(new ResourceRemoval { Data = plan.OriginData, Resource = plan.SupplyResource, Amount = plan.SupplyConsumed });
        if (plan.FuelType != null && plan.OriginFuelTopUp > 0)
            removals.Add(new ResourceRemoval { Data = plan.OriginData, Resource = plan.FuelType, Amount = plan.OriginFuelTopUp });
        if (plan.DestinationRefuel && plan.ReservedReturnFuel > 0)
            removals.Add(new ResourceRemoval { Data = plan.DestinationData, Resource = plan.FuelType, Amount = plan.ReservedReturnFuel });
        if (plan.LaunchPlan != null)
        {
            foreach (var kv in plan.LaunchPlan.FuelByResource)
            {
                if (kv.Key != null && kv.Value > 0)
                    removals.Add(new ResourceRemoval { Data = plan.OriginData, Resource = kv.Key, Amount = kv.Value });
            }
        }

        return removals;
    }

    private static List<Data.GhostFlightCargoRecord> BuildGhostFlightCargoManifest(IEnumerable<GhostDeliveryPlan> plans)
    {
        var manifest = new List<Data.GhostFlightCargoRecord>();
        foreach (var plan in plans ?? Enumerable.Empty<GhostDeliveryPlan>())
        {
            if (plan?.Resource == null || plan.Amount <= 0)
                continue;
            AddGhostFlightCargo(manifest, plan.Resource.ID, plan.Amount, plan.SupplyConsumed);
        }
        return manifest;
    }

    private static void AddGhostFlightCargo(List<Data.GhostFlightCargoRecord> manifest, string resourceId,
        double amount, double supplyConsumed)
    {
        if (manifest == null || string.IsNullOrWhiteSpace(resourceId) || amount <= 0)
            return;

        var existing = manifest.FirstOrDefault(item => item != null
            && string.Equals(item.resourceId, resourceId, StringComparison.Ordinal));
        if (existing == null)
        {
            manifest.Add(new Data.GhostFlightCargoRecord
            {
                resourceId = resourceId,
                cargoAmount = amount,
                supplyConsumed = Math.Max(0, supplyConsumed)
            });
            return;
        }

        existing.cargoAmount += amount;
        existing.supplyConsumed += Math.Max(0, supplyConsumed);
    }

    private static IEnumerable<Data.GhostFlightCargoRecord> GetGhostFlightCargoManifest(Data.GhostFlightRecord flight)
    {
        return flight?.cargoManifest?
            .Where(item => item != null
                && !string.IsNullOrWhiteSpace(item.resourceId)
                && item.cargoAmount > 0) ?? Enumerable.Empty<Data.GhostFlightCargoRecord>();
    }

    private static double GetGhostFlightCargoAmount(Data.GhostFlightRecord flight, ResourceDefinition rd)
    {
        if (flight == null || rd == null)
            return 0;

        return GetGhostFlightCargoManifest(flight)
            .Where(item => string.Equals(item.resourceId, rd.ID, StringComparison.Ordinal))
            .Sum(item => Math.Max(0, item.cargoAmount));
    }

    private static string FormatGhostPlanManifestForLog(IEnumerable<GhostDeliveryPlan> plans)
    {
        var parts = (plans ?? Enumerable.Empty<GhostDeliveryPlan>())
            .Where(plan => plan?.Resource != null && plan.Amount > 0)
            .GroupBy(plan => plan.Resource.ID)
            .Select(group => $"{group.Key}:{group.Sum(plan => plan.Amount):0.#}")
            .ToList();
        return parts.Count == 0 ? "empty" : string.Join(", ", parts);
    }

    private static string FormatGhostFlightManifestForLog(Data.GhostFlightRecord flight)
    {
        var parts = GetGhostFlightCargoManifest(flight)
            .Select(item => $"{item.resourceId}:{item.cargoAmount:0.#}")
            .ToList();
        return parts.Count == 0 ? "empty" : string.Join(", ", parts);
    }

    private static void ProcessGhostFlights(Company player, PlannerSnapshot snapshot)
    {
        if (player == null) return;
        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        foreach (var ownerOI in snapshot?.Objects ?? Data.LogisticsNetwork.GetAllObjects())
        {
            var ownerData = Data.LogisticsNetwork.Get(ownerOI);
            if (ownerData?.ghostFlights == null || ownerData.ghostFlights.Count == 0)
                continue;

            MergeCompatibleGhostFlights(ownerData);
            foreach (var flight in ownerData.ghostFlights.ToList())
            {
                if (flight == null
                    || flight.status == Data.GhostFlightStatus.Complete
                    || flight.status == Data.GhostFlightStatus.Cancelled
                    || flight.status == Data.GhostFlightStatus.Blocked)
                    continue;
                UpdateGhostFlightTransitStatus(flight, now);
                if (flight.arrivalDate > now)
                {
                    EnsureGhostFlightVisual(flight, player);
                    continue;
                }

                if (flight.isReturnFlight)
                {
                    CompleteGhostReturnFlight(ownerData, flight);
                    continue;
                }

                CompleteGhostOutboundFlight(ownerData, flight, player);
            }

            ownerData.ghostFlights.RemoveAll(f => f == null
                || f.status == Data.GhostFlightStatus.Complete
                || f.status == Data.GhostFlightStatus.Cancelled);
        }
    }

    private static void CompleteGhostOutboundFlight(Data.LogisticsObjectData ownerData, Data.GhostFlightRecord flight, Company player)
    {
        var craftList = GetGhostFlightCraft(flight);
        var destination = ResolveObject(flight.toObjectId);
        var home = ResolveObject(flight.homeObjectId);
        if (craftList.Count == 0 || destination == null || home == null)
        {
            flight.status = Data.GhostFlightStatus.Blocked;
            flight.blockedReason = "Ghost flight could not resolve craft or destination";
            DestroyGhostFlightVisual(flight.flightId);
            return;
        }

        var manifest = GetGhostFlightCargoManifest(flight).ToList();
        if (manifest.Count > 0)
        {
            var destData = destination.GetObjectInfoData(player);
            if (destData == null)
            {
                flight.status = Data.GhostFlightStatus.Blocked;
                flight.blockedReason = $"Could not deliver convoy cargo to {destination.ObjectName}";
                foreach (var craft in craftList)
                {
                    craft.status = Data.GhostCraftStatus.Blocked;
                    craft.blockedReason = flight.blockedReason;
                }
                DestroyGhostFlightVisual(flight.flightId);
                LogWarning($"GHOST deliver-blocked: flight={flight.flightId} ships={craftList.Count} reason={flight.blockedReason}");
                return;
            }

            foreach (var cargo in manifest)
            {
                var rd = ResolveResource(cargo.resourceId);
                if (rd == null || cargo.cargoAmount <= 0)
                    continue;

                if (!destData.AddResources(rd, cargo.cargoAmount))
                {
                    flight.status = Data.GhostFlightStatus.Blocked;
                    flight.blockedReason = $"Could not deliver {rd.ID} to {destination.ObjectName}";
                    foreach (var craft in craftList)
                    {
                        craft.status = Data.GhostCraftStatus.Blocked;
                        craft.blockedReason = flight.blockedReason;
                    }
                    DestroyGhostFlightVisual(flight.flightId);
                    LogWarning($"GHOST deliver-blocked: flight={flight.flightId} ships={craftList.Count} reason={flight.blockedReason}");
                    return;
                }
            }
        }

        foreach (var craft in craftList)
        {
            craft.currentObjectId = destination.id;
            craft.status = Data.GhostCraftStatus.AtDestination;
        }
        flight.status = Data.GhostFlightStatus.Complete;
        DestroyGhostFlightVisual(flight.flightId);
        Log($"GHOST arrived: ships={craftList.Count} at={destination.ObjectName} manifest={FormatGhostFlightManifestForLog(flight)}");

        StartGhostReturnFlight(ownerData, craftList, flight, destination, home, player);
    }

    private static void StartGhostReturnFlight(Data.LogisticsObjectData ownerData, List<Data.GhostCraftRecord> craftList,
        Data.GhostFlightRecord outbound, ObjectInfo current, ObjectInfo home, Company player)
    {
        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        if (craftList == null || craftList.Count == 0 || outbound == null || current == null || home == null)
            return;

        var primaryCraft = craftList[0];
        var craftCount = Math.Max(1, craftList.Count);
        var returnFuel = Math.Max(0, outbound.returnFuel) / craftCount;
        var returnDeparture = now;
        var returnArrival = now.AddDays(Math.Max(1, outbound.returnTravelDays));
        var reservedReturnFuelPerCraft = Math.Max(0, outbound.reservedReturnFuel) / craftCount;
        if (outbound.destinationRefuel)
        {
            foreach (var craft in craftList)
                craft.tankFuel = Math.Min(craft.tankFuelCapacity, craft.tankFuel + reservedReturnFuelPerCraft);
        }

        var blockedCraft = craftList.FirstOrDefault(craft => craft.tankFuel + 0.001 < returnFuel);
        if (blockedCraft != null)
        {
            foreach (var craft in craftList)
            {
                craft.status = Data.GhostCraftStatus.Blocked;
                craft.blockedReason = $"Reserved return fuel missing at {current.ObjectName}";
            }
            LogWarning($"GHOST return-blocked: ships={craftList.Count} current={current.ObjectName} home={home.ObjectName} tank={blockedCraft.tankFuel:0.#} need={returnFuel:0.#}");
            return;
        }

        foreach (var craft in craftList)
            craft.tankFuel = Math.Max(0, craft.tankFuel - returnFuel);
        var returnFlight = new Data.GhostFlightRecord
        {
            flightId = Guid.NewGuid().ToString("N"),
            routeId = outbound.routeId,
            craftLedgerIds = craftList.Select(craft => craft.ledgerId).ToList(),
            homeObjectId = primaryCraft.homeObjectId,
            fromObjectId = current.id,
            toObjectId = home.id,
            fuelResourceId = outbound.fuelResourceId,
            outboundFuel = returnFuel * craftCount,
            returnFuel = 0,
            departureDate = returnDeparture,
            arrivalDate = returnArrival,
            outboundTravelDays = Math.Max(1, (returnArrival - returnDeparture).TotalDays),
            returnTravelDays = 0,
            status = returnDeparture > now ? Data.GhostFlightStatus.Planned : Data.GhostFlightStatus.Returning,
            isReturnFlight = true
        };

        ownerData.ghostFlights.Add(returnFlight);
        foreach (var craft in craftList)
        {
            craft.status = returnDeparture > now ? Data.GhostCraftStatus.PlanningReturn : Data.GhostCraftStatus.ReturningHome;
            craft.currentFlightId = returnFlight.flightId;
            craft.routeFromObjectId = current.id;
            craft.routeToObjectId = home.id;
            craft.departureDate = returnFlight.departureDate;
            craft.arrivalDate = returnFlight.arrivalDate;
            craft.cargoResourceId = null;
            craft.cargoAmount = 0;
        }
        EnsureGhostFlightVisual(returnFlight, player);
        Log($"GHOST return-start: ships={craftList.Count} {current.ObjectName}->{home.ObjectName} fuel={returnFuel * craftCount:0.#} arrive={returnFlight.arrivalDate:yyyy-MM-dd}");
    }

    private static void CompleteGhostReturnFlight(Data.LogisticsObjectData ownerData, Data.GhostFlightRecord flight)
    {
        var craftList = GetGhostFlightCraft(flight);
        var home = ResolveObject(flight.homeObjectId);
        if (craftList.Count == 0 || home == null)
        {
            flight.status = Data.GhostFlightStatus.Blocked;
            flight.blockedReason = "Ghost return could not resolve craft or home";
            DestroyGhostFlightVisual(flight.flightId);
            return;
        }

        foreach (var craft in craftList)
        {
            craft.currentObjectId = home.id;
            craft.status = Data.GhostCraftStatus.IdleAtHome;
            craft.currentFlightId = null;
            craft.routeFromObjectId = -1;
            craft.routeToObjectId = -1;
            craft.blockedReason = null;
        }
        flight.status = Data.GhostFlightStatus.Complete;
        DestroyGhostFlightVisual(flight.flightId);
        Log($"GHOST returned: ships={craftList.Count} home={home.ObjectName}");
    }

    private static bool HasActiveGhostDelivery(ObjectInfo requester, ResourceDefinition rd)
    {
        if (requester == null || rd == null) return false;
        return Data.LogisticsNetwork.GetAllGhostFlights().Any(f =>
            f != null
            && !f.isReturnFlight
            && f.toObjectId == requester.id
            && GetGhostFlightCargoAmount(f, rd) > 0
            && (f.status == Data.GhostFlightStatus.Outbound || f.status == Data.GhostFlightStatus.Planned));
    }

    private static double GetGhostInFlightDeliveryAmount(ObjectInfo requester, ResourceDefinition rd)
    {
        if (requester == null || rd == null) return 0;
        return Data.LogisticsNetwork.GetAllGhostFlights()
            .Where(f => f != null
                && !f.isReturnFlight
                && f.toObjectId == requester.id
                && (f.status == Data.GhostFlightStatus.Outbound || f.status == Data.GhostFlightStatus.Planned))
            .Sum(f => GetGhostFlightCargoAmount(f, rd));
    }

    private static string TryCreateGhostDelivery(Data.LogisticsRequest req, ObjectInfo requester,
        ResourceDefinition rd, double remaining, Company player, PlannerSnapshot snapshot)
    {
        if (req == null || requester == null || rd == null || player == null)
            return "No ghost delivery context";

        var bestReason = "No idle ghost logistics craft available";
        ObjectInfo bestReasonProvider = null;
        Data.GhostCraftRecord bestReasonCraft = null;
        double bestReasonAmount = remaining;
        var candidates = new List<(ObjectInfo Provider, Data.GhostCraftRecord Craft, double Available, int Tier, double Score, double Amount)>();
        foreach (var providerOI in snapshot?.Objects ?? Data.LogisticsNetwork.GetAllObjects())
        {
            if (providerOI == null || providerOI == requester) continue;
            if (IsOrbitOf(providerOI, requester))
            {
                LogVerbose($"GHOST provider-skip-orbit-drop-route: provider={providerOI.ObjectName} target={requester.ObjectName} rd={rd.ID}");
                continue;
            }

            var provData = Data.LogisticsNetwork.Get(providerOI);
            if (IsRouteBlockedByCurrentRules(providerOI, requester, rd, out var currentRuleReason))
            {
                bestReason = currentRuleReason;
                bestReasonProvider = providerOI;
                bestReasonCraft = null;
                bestReasonAmount = remaining;
                continue;
            }

            if (FindAllowedProviderRule(provData, req, providerOI, requester, rd, out var providerReason) == null)
            {
                if (!string.IsNullOrEmpty(providerReason))
                {
                    bestReason = providerReason;
                    bestReasonProvider = providerOI;
                    bestReasonCraft = null;
                    bestReasonAmount = remaining;
                }
                continue;
            }

            var available = GetProviderAvailableAfterMinimum(providerOI, rd, player);
            if (available <= 0)
            {
                bestReason = LogisticsStrings.NoSurplusAt(rd, providerOI);
                bestReasonProvider = providerOI;
                bestReasonCraft = null;
                bestReasonAmount = remaining;
                continue;
            }

            var craftCandidates = FindIdleGhostCraftCandidates(providerOI, requester, player, out var craftReason);
            if (craftCandidates.Count == 0)
            {
                if (!string.IsNullOrEmpty(craftReason))
                {
                    bestReason = craftReason;
                    bestReasonProvider = providerOI;
                    bestReasonCraft = null;
                    bestReasonAmount = Math.Min(remaining, available);
                }
                continue;
            }

            foreach (var craft in craftCandidates)
            {
                var desired = Math.Min(remaining, available);
                if (TryScoreGhostDeliveryCandidate(craft, providerOI, requester, rd, desired, player, snapshot,
                        out var score, out var amount, out var scoreReason))
                {
                    candidates.Add((providerOI, craft, available, GetRouteTier(providerOI, requester), score, amount));
                    continue;
                }

                if (!string.IsNullOrEmpty(scoreReason))
                {
                    bestReason = scoreReason;
                    bestReasonProvider = providerOI;
                    bestReasonCraft = craft;
                    bestReasonAmount = amount > 0 ? amount : Math.Min(remaining, available);
                }
            }
        }

        var remainingToDispatch = remaining;
        var dispatchedAny = false;
        foreach (var candidate in candidates
            .OrderBy(c => c.Tier)
            .ThenBy(c => c.Score)
            .ThenByDescending(c => c.Amount)
            .ThenBy(c => c.Provider.id))
        {
            if (remainingToDispatch <= 0.001)
                break;
            if (candidate.Craft == null || candidate.Craft.status != Data.GhostCraftStatus.IdleAtHome)
                continue;

            var availableNow = GetProviderAvailableAfterMinimum(candidate.Provider, rd, player);
            var desiredNow = Math.Min(remainingToDispatch, availableNow);
            if (desiredNow <= 0.001)
                continue;

            var started = TryStartGhostDelivery(candidate.Craft, candidate.Provider, requester, rd,
                desiredNow, player, snapshot, out var reason, out var dispatchedAmount);
            if (started)
            {
                dispatchedAny = true;
                remainingToDispatch = Math.Max(0, remainingToDispatch - dispatchedAmount);
                req.status = Data.LogisticsRequestStatus.InProgress;
                req.statusNote = "Ghost logistics flight outbound";
                continue;
            }

            if (!string.IsNullOrEmpty(reason))
            {
                bestReason = reason;
                bestReasonProvider = candidate.Provider;
                bestReasonCraft = candidate.Craft;
                bestReasonAmount = Math.Min(desiredNow, candidate.Amount);
            }
        }

        if (dispatchedAny)
            return null;

        return bestReason;
    }

    private static List<Data.GhostCraftRecord> FindIdleGhostCraftCandidates(ObjectInfo location, ObjectInfo requester, Company player, out string reason)
    {
        reason = null;
        if (location == null || player == null)
        {
            reason = "No logistics location selected";
            return new List<Data.GhostCraftRecord>();
        }

        var candidates = Data.LogisticsNetwork.GetAllGhostCraft()
            .Where(c => c != null
                && c.status == Data.GhostCraftStatus.IdleAtHome
                && c.currentObjectId == location.id
                && c.homeObjectId == location.id)
            .ToList();
        if (candidates.Count == 0)
        {
            reason = $"No idle ghost logistics craft at {location.ObjectName}";
            return candidates;
        }

        return candidates
            .OrderBy(c =>
            {
                var type = ResolveSpacecraftType(c.shipTypeId);
                return type?.NameRocketType ?? c.shipTypeId ?? "";
            })
            .ThenBy(c => c.ledgerId)
            .ToList();
    }

    private static List<Data.GhostCraftRecord> FindIdleRouteGhostCraftCandidates(Data.LogisticsRouteRecord route,
        ObjectInfo source, ObjectInfo destination, Company player, out string reason)
    {
        reason = null;
        if (route == null || source == null || player == null)
        {
            reason = "No logistics route selected";
            return new List<Data.GhostCraftRecord>();
        }

        var candidates = Data.LogisticsNetwork.GetAllGhostCraft()
            .Where(c => c != null
                && c.assignedRouteId == route.routeId
                && c.status == Data.GhostCraftStatus.IdleAtHome
                && c.currentObjectId == source.id
                && c.homeObjectId == source.id)
            .ToList();

        if (candidates.Count == 0)
        {
            reason = $"No idle logistics vessel on {source.ObjectName}->{destination?.ObjectName ?? "destination"}";
            return candidates;
        }

        return candidates
            .OrderBy(c =>
            {
                var type = ResolveSpacecraftType(c.shipTypeId);
                return type?.NameRocketType ?? c.shipTypeId ?? "";
            })
            .ThenBy(c => c.ledgerId)
            .ToList();
    }

    private static bool TryStartGhostDelivery(Data.GhostCraftRecord craft, ObjectInfo provider, ObjectInfo requester,
        ResourceDefinition rd, double desiredAmount, Company player, PlannerSnapshot snapshot, out string reason,
        out double dispatchedAmount, int routeId = -1)
    {
        dispatchedAmount = 0;
        if (!TryBuildGhostDeliveryPlan(craft, provider, requester, rd, desiredAmount, player, snapshot, out var plan, out reason, routeId))
            return false;

        if (!TryCommitGhostDeliveryPlan(plan, player, out reason))
            return false;

        dispatchedAmount = plan.Amount;
        return true;
    }

    private static bool TryScoreGhostDeliveryCandidate(Data.GhostCraftRecord craft, ObjectInfo provider, ObjectInfo requester,
        ResourceDefinition rd, double desiredAmount, Company player, PlannerSnapshot snapshot,
        out double score, out double amount, out string reason, int routeId = -1)
    {
        score = double.MaxValue;
        amount = 0;
        if (!TryBuildGhostDeliveryPlan(craft, provider, requester, rd, desiredAmount, player, snapshot, out var plan, out reason, routeId))
        {
            amount = Math.Max(0, desiredAmount);
            return false;
        }

        amount = plan.Amount;
        var logisticsFuel = Math.Max(0, plan.Outbound?.Fuel ?? 0) + Math.Max(0, plan.ReturnLeg?.Fuel ?? 0);
        var launchFuel = plan.LaunchPlan?.FuelByResource.Values.Sum() ?? 0;
        score = (logisticsFuel + launchFuel) / Math.Max(1, plan.Amount);
        return true;
    }

    private static bool TryBuildGhostDeliveryPlan(Data.GhostCraftRecord craft, ObjectInfo provider, ObjectInfo requester,
        ResourceDefinition rd, double desiredAmount, Company player, PlannerSnapshot snapshot,
        out GhostDeliveryPlan plan, out string reason, int routeId = -1)
    {
        plan = null;
        reason = null;
        var scType = ResolveSpacecraftType(craft?.shipTypeId);
        if (craft == null || scType == null || provider == null || requester == null || rd == null || player == null)
        {
            reason = "Ghost craft or route is unavailable";
            return false;
        }

        var originData = provider.GetObjectInfoData(player);
        var destinationData = requester.GetObjectInfoData(player);
        if (originData == null || destinationData == null)
        {
            reason = "Route stockpile is unavailable";
            return false;
        }

        var capacity = scType.GetCargoCapacity(player);
        var isHumanPayload = IsHumanResource(rd);
        var outboundTravelDays = EstimateGhostTravelDays(provider, requester);
        var supplyResource = isHumanPayload ? ResolveSupplyResource() : null;
        if (isHumanPayload && supplyResource == null)
        {
            reason = "Supply resource is unavailable for crew flight";
            return false;
        }

        var supplyPerHuman = isHumanPayload ? EstimateCrewSupplyNeed(1, outboundTravelDays, player) : 0;
        var payloadMassPerUnit = GetPayloadMassPerResourceUnit(rd, supplyPerHuman);
        var amount = Math.Min(Math.Max(0, desiredAmount), capacity / Math.Max(0.001, payloadMassPerUnit));
        if (isHumanPayload)
            amount = Math.Floor(amount);
        if (amount <= 0)
        {
            reason = "Ghost craft has no cargo capacity";
            return false;
        }

        if (routeId >= 0 && !IsFullRouteGhostLoad(amount, capacity, payloadMassPerUnit, isHumanPayload, out var fullLoadAmount))
        {
            reason = "Waiting for full load";
            return false;
        }

        var supplyConsumed = isHumanPayload ? EstimateCrewSupplyNeed(amount, outboundTravelDays, player) : 0;
        var payloadCargoMass = GetPayloadCargoMass(rd, amount, supplyConsumed);

        if (!TryCalculateGhostLeg(craft, scType, provider, requester, rd, payloadCargoMass, player, routeId,
                out var outbound))
        {
            reason = outbound?.Reason ?? "Could not calculate outbound fuel";
            return false;
        }

        if (!TryCalculateGhostLeg(craft, scType, requester, provider, null, 0, player, routeId,
                out var returnLeg))
        {
            reason = returnLeg?.Reason ?? "Could not calculate return fuel";
            return false;
        }

        var launchPlan = default(GhostLaunchPlan);
        var launchPayload = scType.GetMass(player) + payloadCargoMass;
        if (provider.NeedVehicleToLaunch())
        {
            if (!TryBuildGhostLaunchPlan(provider, launchPayload, player,
                    GetGhostLaunchSupport(provider, player, snapshot, routeId), rd, out launchPlan))
            {
                reason = $"No reserved launch vehicle or facility launch capacity left at {provider.ObjectName}";
                return false;
            }
        }

        var fuelType = outbound.FuelType ?? returnLeg.FuelType ?? scType.GetFuelType();
        var destinationRefuel = false;
        double reservedReturnFuel = 0;
        var destinationFuelAvailable = fuelType == null ? 0 : destinationData.CheckResources(fuelType);
        if (fuelType == null || scType.SolarSC || returnLeg.Fuel <= 0)
        {
            destinationRefuel = false;
        }
        else if (destinationFuelAvailable + 0.001 >= returnLeg.Fuel)
        {
            destinationRefuel = true;
            reservedReturnFuel = returnLeg.Fuel;
        }

        var requiredTankAtDeparture = destinationRefuel
            ? outbound.Fuel
            : outbound.Fuel + returnLeg.Fuel;
        if (requiredTankAtDeparture > craft.tankFuelCapacity + 0.001)
        {
            reason = $"Ghost craft tank too small for {provider.ObjectName}->{requester.ObjectName}->{provider.ObjectName}";
            return false;
        }

        var originFuelTopUp = Math.Max(0, requiredTankAtDeparture - craft.tankFuel);
        var removals = new List<ResourceRemoval>
        {
            new ResourceRemoval { Data = originData, Resource = rd, Amount = amount }
        };
        if (supplyResource != null && supplyConsumed > 0)
            removals.Add(new ResourceRemoval { Data = originData, Resource = supplyResource, Amount = supplyConsumed });
        if (fuelType != null && originFuelTopUp > 0)
            removals.Add(new ResourceRemoval { Data = originData, Resource = fuelType, Amount = originFuelTopUp });
        if (destinationRefuel && reservedReturnFuel > 0)
            removals.Add(new ResourceRemoval { Data = destinationData, Resource = fuelType, Amount = reservedReturnFuel });
        if (launchPlan != null)
        {
            foreach (var kv in launchPlan.FuelByResource)
            {
                if (kv.Key != null && kv.Value > 0)
                    removals.Add(new ResourceRemoval { Data = originData, Resource = kv.Key, Amount = kv.Value });
            }
        }

        if (!CanApplyResourceRemovals(removals, out reason))
            return false;

        plan = new GhostDeliveryPlan
        {
            RouteId = routeId,
            Craft = craft,
            SpacecraftType = scType,
            Provider = provider,
            Requester = requester,
            Resource = rd,
            Amount = amount,
            SupplyResource = supplyResource,
            SupplyConsumed = supplyConsumed,
            PayloadCargoMass = payloadCargoMass,
            Outbound = outbound,
            ReturnLeg = returnLeg,
            FuelType = fuelType,
            LaunchPlan = launchPlan,
            LaunchPayload = launchPayload,
            DestinationRefuel = destinationRefuel,
            ReservedReturnFuel = reservedReturnFuel,
            OriginFuelTopUp = originFuelTopUp,
            OriginData = originData,
            DestinationData = destinationData
        };
        return true;
    }

    private static bool TryCommitGhostDeliveryPlan(GhostDeliveryPlan plan, Company player, out string reason)
    {
        return TryCommitGhostDeliveryConvoy(new List<GhostDeliveryPlan> { plan }, player, out reason);
    }

    private static bool TryCalculateGhostLeg(Data.GhostCraftRecord craft, SpacecraftType scType, ObjectInfo from,
        ObjectInfo to, ResourceDefinition cargoResource, double cargoAmount, Company player, out GhostLegPlan plan)
    {
        return TryCalculateGhostLeg(craft, scType, from, to, cargoResource, cargoAmount, player, -1, out plan);
    }

    private static bool TryCalculateGhostLeg(Data.GhostCraftRecord craft, SpacecraftType scType, ObjectInfo from,
        ObjectInfo to, ResourceDefinition cargoResource, double cargoAmount, Company player, int routeId,
        out GhostLegPlan plan)
    {
        plan = new GhostLegPlan();
        if (craft == null || scType == null || from == null || to == null || player == null)
        {
            plan.Reason = "Missing ghost route data";
            return false;
        }

        plan.FuelType = scType.GetFuelType();
        var vehicle = LogisticsFlightVehicleSnapshot.FromGhostCraft(craft, scType, player);
        var cargo = new LogisticsFlightCargoSnapshot
        {
            CargoMass = Math.Max(0, cargoAmount),
            Resource = cargoResource,
            Amount = cargoAmount
        };
        var requestedFlightPlanMode = ResolveGhostCraftRequestedFlightPlanMode(craft, routeId);
        LogVerbose($"ROUTE-MISSION step=leg-input route={routeId} from={from.ObjectName}#{from.id}({from.objectTypes}) to={to.ObjectName}#{to.id}({to.objectTypes}) craftLedger={craft.ledgerId} ship={scType.ID} requestedMode={requestedFlightPlanMode} cargoResource={cargoResource?.ID ?? "none"} cargoAmount={cargoAmount:0.###} cargoMass={cargo.CargoMass:0.###} tank={craft.tankFuel:0.###}/{craft.tankFuelCapacity:0.###} designDV={scType.AvailableDeltaV:0.###} minMaxRel={scType.MinFlightTimeHohRel:0.###}/{scType.MaxFlightTimeHohRel:0.###}");
        var flight = LogisticsFlightCalculator.CalculateSoonestOptimalFlight(from, to, vehicle, cargo, player,
            requestedFlightPlanMode);
        if (flight == null || !flight.Success)
        {
            plan.Reason = flight?.Reason ?? "Could not calculate flight";
            return false;
        }

        plan.FuelType = flight.FuelType ?? plan.FuelType;
        plan.TravelDays = flight.TravelDays;
        plan.DeltaV = flight.EstimatedDeltaV;
        plan.Departure = flight.Departure;
        plan.Arrival = flight.Arrival;
        plan.Fuel = flight.FlightFuel;
        LogVerbose($"GHOST estimate-leg: {from.ObjectName}->{to.ObjectName} ship={craft.shipName} cargo={cargoResource?.ID ?? "none"}:{cargoAmount:0.#} fuel={plan.Fuel:0.#} days={flight.TravelDays:0.#} dV={flight.EstimatedDeltaV:0.##} route={flight.RouteKind} plan={flight.FlightPlanMode}");
        return true;
    }

    private static Data.LogisticsFlightPlanMode ResolveGhostCraftRequestedFlightPlanMode(
        Data.GhostCraftRecord craft,
        int routeId)
    {
        var route = routeId > 0
            ? Data.LogisticsNetwork.FindRoute(routeId)
            : Data.LogisticsNetwork.FindRoute(craft?.assignedRouteId ?? -1);
        if (route != null)
            return Data.LogisticsNetwork.GetRouteSpacecraftFlightPlanMode(route, craft?.shipTypeId);

        return Data.LogisticsFlightPlanMode.Optimal;
    }

    public static void NormalizeGhostConvoys()
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (player != null)
            RecoverBlockedReturnFuelCraft(player);

        foreach (var ownerOI in Data.LogisticsNetwork.GetAllObjects())
        {
            var ownerData = Data.LogisticsNetwork.Get(ownerOI);
            if (ownerData?.ghostFlights == null || ownerData.ghostFlights.Count == 0)
                continue;

            MergeCompatibleGhostFlights(ownerData);
        }
    }

    private static void RecoverBlockedReturnFuelCraft(Company player)
    {
        if (player == null)
            return;

        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        var activeFlightIds = new HashSet<string>(
            Data.LogisticsNetwork.GetAllGhostFlights()
                .Where(f => f != null
                    && f.status != Data.GhostFlightStatus.Complete
                    && f.status != Data.GhostFlightStatus.Cancelled)
                .Select(f => f.flightId)
                .Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);

        foreach (var ownerOI in Data.LogisticsNetwork.GetAllObjects().ToList())
        {
            var ownerData = Data.LogisticsNetwork.Get(ownerOI);
            if (ownerOI == null || ownerData?.ghostCraft == null)
                continue;

            var groups = ownerData.ghostCraft
                .Where(craft => IsBlockedByStaleReturnFuel(craft, activeFlightIds))
                .GroupBy(craft => new
                {
                    craft.assignedRouteId,
                    craft.currentObjectId,
                    craft.homeObjectId,
                    FlightId = craft.currentFlightId ?? ""
                })
                .ToList();

            foreach (var group in groups)
            {
                var craftList = group.ToList();
                var first = craftList.FirstOrDefault();
                var current = ResolveObject(group.Key.currentObjectId);
                var home = ResolveObject(group.Key.homeObjectId);
                if (first == null || current == null || home == null || current == home)
                    continue;

                var travelDays = craftList
                    .Select(craft => (craft.arrivalDate - craft.departureDate).TotalDays)
                    .Where(days => days > 0.1)
                    .DefaultIfEmpty(EstimateGhostTravelDays(current, home))
                    .Max();
                travelDays = Math.Max(1, travelDays);
                var returnFlight = new Data.GhostFlightRecord
                {
                    flightId = Guid.NewGuid().ToString("N"),
                    routeId = group.Key.assignedRouteId,
                    craftLedgerIds = craftList.Select(craft => craft.ledgerId).Where(id => id > 0).Distinct().ToList(),
                    homeObjectId = first.homeObjectId,
                    fromObjectId = current.id,
                    toObjectId = home.id,
                    fuelResourceId = ResolveSpacecraftType(first.shipTypeId)?.GetFuelType()?.ID,
                    outboundFuel = craftList.Sum(craft => Math.Max(0, craft.tankFuel)),
                    returnFuel = 0,
                    departureDate = now,
                    arrivalDate = now.AddDays(travelDays),
                    outboundTravelDays = travelDays,
                    returnTravelDays = 0,
                    status = Data.GhostFlightStatus.Returning,
                    isReturnFlight = true
                };

                ownerData.ghostFlights ??= new List<Data.GhostFlightRecord>();
                ownerData.ghostFlights.Add(returnFlight);
                foreach (var craft in craftList)
                {
                    craft.tankFuel = 0;
                    craft.status = Data.GhostCraftStatus.ReturningHome;
                    craft.currentFlightId = returnFlight.flightId;
                    craft.routeFromObjectId = current.id;
                    craft.routeToObjectId = home.id;
                    craft.departureDate = returnFlight.departureDate;
                    craft.arrivalDate = returnFlight.arrivalDate;
                    craft.cargoResourceId = null;
                    craft.cargoAmount = 0;
                    craft.blockedReason = null;
                }

                EnsureGhostFlightVisual(returnFlight, player);
                LogWarning($"GHOST return-recovered: ships={craftList.Count} {current.ObjectName}->{home.ObjectName} fuel={returnFlight.outboundFuel:0.#} arrive={returnFlight.arrivalDate:yyyy-MM-dd}");
            }
        }
    }

    private static bool IsBlockedByStaleReturnFuel(Data.GhostCraftRecord craft, HashSet<string> activeFlightIds)
    {
        if (craft == null || craft.status != Data.GhostCraftStatus.Blocked)
            return false;
        if (craft.currentObjectId <= 0 || craft.homeObjectId <= 0 || craft.currentObjectId == craft.homeObjectId)
            return false;
        if (string.IsNullOrWhiteSpace(craft.blockedReason)
            || !craft.blockedReason.StartsWith("Reserved return fuel missing", StringComparison.Ordinal))
            return false;
        return string.IsNullOrWhiteSpace(craft.currentFlightId)
            || activeFlightIds == null
            || !activeFlightIds.Contains(craft.currentFlightId);
    }

    private static double EstimateGhostTravelDays(ObjectInfo from, ObjectInfo to)
    {
        return LogisticsFlightCalculator.EstimateSoonestOptimalTravelDays(from, to);
    }

    private static DateTime GetGhostLegDeparture(GhostLegPlan plan, DateTime now)
    {
        if (plan == null || plan.Departure == default)
            return now;
        return plan.Departure < now ? now : plan.Departure;
    }

    private static DateTime GetGhostLegArrival(GhostLegPlan plan, DateTime departure)
    {
        if (plan != null && plan.Arrival != default && plan.Arrival > departure)
            return plan.Arrival;
        var days = Math.Max(1, plan?.TravelDays ?? 1);
        return departure.AddDays(days);
    }

    private static void UpdateGhostFlightTransitStatus(Data.GhostFlightRecord flight, DateTime now)
    {
        if (flight == null || flight.status == Data.GhostFlightStatus.Blocked)
            return;

        flight.status = now < flight.departureDate
            ? Data.GhostFlightStatus.Planned
            : flight.isReturnFlight
                ? Data.GhostFlightStatus.Returning
                : Data.GhostFlightStatus.Outbound;

        foreach (var craft in GetGhostFlightCraft(flight))
        {
            if (craft.currentFlightId != flight.flightId)
                continue;

            craft.status = now < flight.departureDate
                ? flight.isReturnFlight ? Data.GhostCraftStatus.PlanningReturn : Data.GhostCraftStatus.PlanningOutbound
                : flight.isReturnFlight ? Data.GhostCraftStatus.ReturningHome : Data.GhostCraftStatus.Outbound;
            craft.departureDate = flight.departureDate;
            craft.arrivalDate = flight.arrivalDate;
        }
    }

    private static bool TryBuildGhostLaunchPlan(ObjectInfo providerOI, double payloadMass, Company player,
        List<LaunchSupportOption> support, ResourceDefinition payloadResource, out GhostLaunchPlan plan)
    {
        plan = new GhostLaunchPlan { PayloadMass = payloadMass };
        if (providerOI == null || player == null || payloadMass <= 0 || support == null || support.Count == 0)
            return false;

        var remaining = payloadMass;
        var facilitySupport = support.Where(option => option != null && option.ReservedLaunchVehicle == null).ToList();
        var facilityCapacityLeft = GetVirtualLiftCapacityLeft(providerOI, player, facilitySupport);
        var plannedFuel = new Dictionary<ResourceDefinition, double>();
        foreach (var option in support)
        {
            if (remaining <= 0) break;
            if (option == null || option.Type == null) continue;
            if (!CanLiftResourceWithSupport(option, payloadResource)) continue;
            if (option.ReservedLaunchVehicle != null && !IsReservedLaunchVehicleReady(option.ReservedLaunchVehicle))
                continue;

            var singlePayload = GetVirtualLiftSinglePayloadCapacity(option, providerOI, player);
            if (singlePayload <= 0) continue;

            var siteCount = GetVirtualLiftSiteCount(option);
            if (siteCount <= 0) continue;

            var optionCapacity = option.ReservedLaunchVehicle != null
                ? singlePayload
                : singlePayload * siteCount * VirtualSurfaceLiftPayloadsPerDay();
            if (option.ReservedLaunchVehicle == null)
                optionCapacity = Math.Min(optionCapacity, Math.Max(0, facilityCapacityLeft - plan.FacilityCapacityUsed));
            if (optionCapacity <= 0) continue;

            var chunk = Math.Min(remaining, optionCapacity);
            var fuelType = option.Type?.FuelTypeOnStart;
            if (fuelType != null)
            {
                var fuelPerPayloadTon = GetVirtualLiftFuelPerPayloadTon(option, singlePayload, providerOI, player);
                var fuelNeeded = Math.Ceiling(chunk * fuelPerPayloadTon);
                plannedFuel.TryGetValue(fuelType, out var already);
                var fuelAvailable = GetVirtualLiftFuelAvailable(providerOI, fuelType, player) - already;
                if (fuelAvailable + 0.001 < fuelNeeded)
                    continue;
                plannedFuel[fuelType] = already + fuelNeeded;
            }

            if (option.ReservedLaunchVehicle != null)
            {
                if (!plan.ReservedLaunchVehiclesUsed.Contains(option.ReservedLaunchVehicle))
                    plan.ReservedLaunchVehiclesUsed.Add(option.ReservedLaunchVehicle);
            }
            else
            {
                plan.FacilityCapacityUsed += chunk;
            }
            remaining -= chunk;
            if (!string.IsNullOrWhiteSpace(option.Label) && !plan.SupportLabels.Contains(option.Label))
                plan.SupportLabels.Add(option.Label);
        }

        if (remaining > 0.001)
            return false;
        foreach (var kv in plannedFuel)
            plan.FuelByResource[kv.Key] = kv.Value;
        return true;
    }

    private static bool TryApplyResourceRemovals(List<ResourceRemoval> removals, out string reason)
    {
        reason = null;
        var grouped = removals
            .Where(r => r?.Data != null && r.Resource != null && r.Amount > 0)
            .GroupBy(r => new { r.Data, r.Resource })
            .Select(g => new ResourceRemoval { Data = g.Key.Data, Resource = g.Key.Resource, Amount = g.Sum(x => x.Amount) })
            .ToList();

        foreach (var removal in grouped)
        {
            var have = removal.Data.CheckResources(removal.Resource);
            if (have + 0.001 < removal.Amount)
            {
                reason = $"Missing {removal.Resource.ID}: need {removal.Amount:0.#}, have {have:0.#}";
                return false;
            }
        }

        var removed = new List<ResourceRemoval>();
        foreach (var removal in grouped)
        {
            if (!removal.Data.RemoveResource(removal.Resource, removal.Amount))
            {
                foreach (var rollback in removed)
                    rollback.Data.AddResources(rollback.Resource, rollback.Amount);
                reason = $"Could not remove {removal.Resource.ID}";
                return false;
            }
            removed.Add(removal);
        }
        return true;
    }

    private static ResourceDefinition ResolveResource(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId)) return null;
        return SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllResourceDefinitions?.GetByID(resourceId);
    }

    private static string ResourceName(ResourceDefinition rd)
    {
        if (rd == null)
            return "";
        return string.IsNullOrWhiteSpace(rd.name) ? rd.ID ?? "" : rd.name;
    }

    private static SpacecraftType ResolveSpacecraftType(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId)) return null;
        return SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllSpacecraftType?.GetByID(typeId);
    }

    private static LaunchVehicleType ResolveLaunchVehicleType(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId)) return null;
        return SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllLaunchVehicleType?.GetByID(typeId);
    }

    private static List<int> GetGhostFlightCraftIds(Data.GhostFlightRecord flight)
    {
        if (flight == null)
            return new List<int>();

        var ids = flight.craftLedgerIds?
            .Where(id => id > 0)
            .Distinct()
            .ToList() ?? new List<int>();
        return ids;
    }

    private static List<Data.GhostCraftRecord> GetGhostFlightCraft(Data.GhostFlightRecord flight)
    {
        return GetGhostFlightCraftIds(flight)
            .Select(Data.LogisticsNetwork.FindGhostCraft)
            .Where(craft => craft != null)
            .ToList();
    }

    private static string GetGhostFlightShipTypeId(Data.GhostFlightRecord flight)
    {
        return GetGhostFlightCraft(flight)
            .Select(craft => craft.shipTypeId)
            .FirstOrDefault(typeId => !string.IsNullOrWhiteSpace(typeId));
    }

    private static Data.GhostFlightRecord MergeCompatibleGhostFlight(Data.LogisticsObjectData ownerData,
        Data.GhostFlightRecord flight)
    {
        if (ownerData?.ghostFlights == null || flight == null || flight.isReturnFlight)
            return flight;

        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        if (flight.status != Data.GhostFlightStatus.Planned || now >= flight.departureDate)
            return flight;

        flight.craftLedgerIds = GetGhostFlightCraftIds(flight);
        var shipTypeId = GetGhostFlightShipTypeId(flight);
        var existing = ownerData.ghostFlights.FirstOrDefault(other =>
            other != null
            && !ReferenceEquals(other, flight)
            && !other.isReturnFlight
            && other.status == Data.GhostFlightStatus.Planned
            && now < other.departureDate
            && other.routeId == flight.routeId
            && other.homeObjectId == flight.homeObjectId
            && other.fromObjectId == flight.fromObjectId
            && other.toObjectId == flight.toObjectId
            && string.Equals(other.fuelResourceId, flight.fuelResourceId, StringComparison.Ordinal)
            && other.destinationRefuel == flight.destinationRefuel
            && other.departureDate == flight.departureDate
            && other.arrivalDate == flight.arrivalDate
            && string.Equals(GetGhostFlightShipTypeId(other), shipTypeId, StringComparison.Ordinal));
        if (existing == null)
            return flight;

        existing.craftLedgerIds = GetGhostFlightCraftIds(existing)
            .Concat(flight.craftLedgerIds)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        existing.cargoManifest ??= new List<Data.GhostFlightCargoRecord>();
        foreach (var cargo in GetGhostFlightCargoManifest(flight))
            AddGhostFlightCargo(existing.cargoManifest, cargo.resourceId, cargo.cargoAmount, cargo.supplyConsumed);

        existing.outboundFuel += flight.outboundFuel;
        existing.returnFuel += flight.returnFuel;
        existing.launchFuel += flight.launchFuel;
        existing.reservedReturnFuel += flight.reservedReturnFuel;
        existing.launchPayloadMass += flight.launchPayloadMass;

        foreach (var craft in GetGhostFlightCraft(flight))
            craft.currentFlightId = existing.flightId;

        ownerData.ghostFlights.Remove(flight);
        DestroyGhostFlightVisual(flight.flightId);
        LogVerbose($"GHOST convoy-merge: flight={existing.flightId} ships={existing.craftLedgerIds.Count} {existing.fromObjectId}->{existing.toObjectId} manifest={FormatGhostFlightManifestForLog(existing)}");
        return existing;
    }

    private static void MergeCompatibleGhostFlights(Data.LogisticsObjectData ownerData)
    {
        if (ownerData?.ghostFlights == null || ownerData.ghostFlights.Count <= 1)
            return;

        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        foreach (var flight in ownerData.ghostFlights.ToList())
        {
            if (flight == null
                || flight.isReturnFlight
                || flight.status != Data.GhostFlightStatus.Planned
                || now >= flight.departureDate
                || flight.status == Data.GhostFlightStatus.Complete
                || flight.status == Data.GhostFlightStatus.Cancelled
                || flight.status == Data.GhostFlightStatus.Blocked)
                continue;
            MergeCompatibleGhostFlight(ownerData, flight);
        }
    }

    private static void EnsureGhostFlightVisual(Data.GhostFlightRecord flight, Company player)
    {
        if (flight == null || string.IsNullOrEmpty(flight.flightId))
            return;
        if (_ghostFlightVisuals.TryGetValue(flight.flightId, out var existing))
        {
            if (existing != null)
                return;
            _ghostFlightVisuals.Remove(flight.flightId);
        }

        var from = ResolveObject(flight.fromObjectId);
        var to = ResolveObject(flight.toObjectId);
        if (from == null || to == null)
            return;

        try
        {
            var visual = SerializedMonoBehaviourSingleton<TrajectoryManager>.Instance?.SpawnTrajectoryForSave();
            if (visual == null)
                return;

            visual.gameObject.name = $"LogisticsGhostFlight_{flight.flightId}";
            visual.gameObject.SetActive(true);
            visual.LayerLabel = LabelsManager.ELayerLabel.TrajectoryMission;
            if (visual.lineRender != null)
                visual.lineRender.material = SerializedMonoBehaviourSingleton<ReferenceController>.Instance.trajektoryMission;
            visual.SetTrajectory2ConstantAccelerationFlight(from, to, flight.departureDate, flight.arrivalDate);
            visual.SetTrajectory2Data(from, to, flight.departureDate, flight.arrivalDate);
            ApplyGhostFlightVisualMaterial(visual, new Color(1f, 1f, 1f, 0.55f));
            HideGhostFlightVisualLabels(visual);
            _ghostFlightVisuals[flight.flightId] = visual;
            UpdateGhostFlightVisual(flight, visual);
        }
        catch (Exception ex)
        {
            LogWarning($"GHOST visual-create-failed: flight={flight.flightId} error={ex.GetType().Name}");
        }
    }

    private static void DestroyGhostFlightVisual(string flightId)
    {
        if (string.IsNullOrEmpty(flightId)) return;
        if (!_ghostFlightVisuals.TryGetValue(flightId, out var visual))
            return;
        _ghostFlightVisuals.Remove(flightId);
        if (visual != null)
            UnityEngine.Object.Destroy(visual.gameObject);
    }

    public static void UpdateGhostFlightVisuals()
    {
        try
        {
            var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
            var tc = MonoBehaviourSingleton<TimeController>.Instance;
            if (player == null || tc == null)
                return;

            var now = tc.CurrentTime;
            var activeFlightIds = new HashSet<string>();
            foreach (var flight in Data.LogisticsNetwork.GetAllGhostFlights())
            {
                if (flight == null
                    || string.IsNullOrEmpty(flight.flightId)
                    || flight.status == Data.GhostFlightStatus.Complete
                    || flight.status == Data.GhostFlightStatus.Cancelled
                    || flight.status == Data.GhostFlightStatus.Blocked)
                    continue;

                if (flight.arrivalDate <= now)
                {
                    DestroyGhostFlightVisual(flight.flightId);
                    continue;
                }

                activeFlightIds.Add(flight.flightId);
                EnsureGhostFlightVisual(flight, player);
                if (_ghostFlightVisuals.TryGetValue(flight.flightId, out var visual) && visual != null)
                    UpdateGhostFlightVisual(flight, visual);
            }

            foreach (var flightId in _ghostFlightVisuals.Keys.ToList())
            {
                if (!activeFlightIds.Contains(flightId))
                    DestroyGhostFlightVisual(flightId);
            }
        }
        catch (Exception ex)
        {
            LogWarning($"GHOST visual-refresh-failed: error={ex.GetType().Name}");
        }
    }

    private static void UpdateGhostFlightVisual(Data.GhostFlightRecord flight, TrajectoryObject visual)
    {
        if (flight == null || visual == null)
            return;

        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        if (now < flight.departureDate || now >= flight.arrivalDate)
            return;

        try
        {
            if (!visual.gameObject.activeSelf)
                visual.gameObject.SetActive(true);
            HideGhostFlightVisualLabels(visual);
            visual.GetPositionFly();
        }
        catch (Exception ex)
        {
            LogWarning($"GHOST visual-update-failed: flight={flight.flightId} error={ex.GetType().Name}");
            DestroyGhostFlightVisual(flight.flightId);
        }
    }

    private static void HideGhostFlightVisualLabels(TrajectoryObject visual)
    {
        if (visual == null)
            return;
        if (visual.start != null)
        {
            visual.start.TurnOffVisible();
            visual.start.gameObject.SetActive(false);
        }
        if (visual.target != null)
        {
            visual.target.TurnOffVisible();
            visual.target.gameObject.SetActive(false);
        }
        foreach (var canvas in visual.GetComponentsInChildren<Canvas>(true))
            canvas.enabled = false;
    }

    private static void ApplyGhostFlightVisualMaterial(TrajectoryObject visual, Color color)
    {
        var trajectoryManager = SerializedMonoBehaviourSingleton<TrajectoryManager>.Instance;
        if (visual == null || trajectoryManager == null || visual.LineMeshOrbitPredictorMeshRender == null)
            return;

        if (!trajectoryManager.trajectoryObjectMaterials.TryGetValue(color, out var material) || material == null)
        {
            material = UnityEngine.Object.Instantiate(visual.LineMeshOrbitPredictorMeshRender.material);
            material.color = color;
            material.SetFloat("_UseGradient", 0f);
            trajectoryManager.trajectoryObjectMaterials[color] = material;
        }
        visual.LineMeshOrbitPredictorMeshRender.sharedMaterial = material;
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

    private static string PendingDeliveryKey(ObjectInfo requester, ResourceDefinition rd)
    {
        return $"{requester?.id ?? -1}:{rd?.ID ?? "null"}";
    }

    private static string BuildRequestPlanSignature(ObjectInfo requester, ResourceDefinition rd,
        double requestTarget, double alreadyThere, double inFlight, bool hasActiveDelivery)
    {
        return $"{requester?.id ?? -1}:{rd?.ID ?? "null"}:" +
               $"target={Math.Round(requestTarget, 1)}:" +
               $"stock={Math.Round(alreadyThere, 1)}:" +
               $"inflight={Math.Round(inFlight, 1)}:" +
               $"active={hasActiveDelivery}";
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
            && statusNote.StartsWith("Waiting to re-check logistics options", StringComparison.Ordinal);
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

    private static double GetInFlightDeliveryAmount(ObjectInfo requester, ResourceDefinition rd, Company player, PlannerSnapshot snapshot = null)
    {
        if (requester == null || rd == null || player == null)
            return 0;
        return GetGhostInFlightDeliveryAmount(requester, rd);
    }

    private static bool IsResourceCargo(Cargo cargo)
    {
        return cargo != null
            && cargo.resourceTypeType == EResourceTypeType.resorces
            && cargo.resourceType != null;
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

    private static bool VirtualSurfaceLiftEnabled()
    {
        return LogisticsMod.Plugin.VirtualSurfaceLiftEnabled?.Value ?? true;
    }

    private static double VirtualSurfaceLiftPayloadsPerDay()
    {
        var value = LogisticsMod.Plugin.VirtualSurfaceLiftPayloadsPerDay?.Value ?? 1.0;
        return Math.Max(0.0, value);
    }

    private static string VirtualLiftUsageKey(ObjectInfo providerOI, Company player)
    {
        return $"{player?.ID ?? "null"}:{providerOI?.id ?? -1}";
    }

    private static double GetVirtualLiftUsedToday(ObjectInfo providerOI, Company player)
    {
        var key = VirtualLiftUsageKey(providerOI, player);
        var today = (MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now).Date;
        if (!_virtualLiftUsage.TryGetValue(key, out var state) || state == null || state.Date != today)
        {
            state = new VirtualLiftUsageState { Date = today, UsedPayloadMass = 0 };
            _virtualLiftUsage[key] = state;
        }
        return state.UsedPayloadMass;
    }

    private static void CommitVirtualLiftUsage(ObjectInfo providerOI, Company player, double payloadMass)
    {
        if (payloadMass <= 0) return;
        var key = VirtualLiftUsageKey(providerOI, player);
        var today = (MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now).Date;
        if (!_virtualLiftUsage.TryGetValue(key, out var state) || state == null || state.Date != today)
        {
            state = new VirtualLiftUsageState { Date = today, UsedPayloadMass = 0 };
            _virtualLiftUsage[key] = state;
        }
        state.UsedPayloadMass += payloadMass;
    }

    private static double TryApplyVirtualSurfaceLift(Data.LogisticsRequest req, ObjectInfo requester,
        ResourceDefinition rd, double remaining, Company player, PlannerSnapshot snapshot)
    {
        if (!VirtualSurfaceLiftEnabled() || req == null || requester == null || rd == null || player == null || remaining <= 0)
            return 0;

        var providerOI = requester.parentObjectInfo;
        if (!IsOrbitOf(requester, providerOI))
            return 0;

        var providerData = Data.LogisticsNetwork.Get(providerOI);
        var providerRule = FindAllowedProviderRule(providerData, req, providerOI, requester, rd, out var providerReason);
        if (providerData == null || providerRule == null)
        {
            if (!string.IsNullOrEmpty(providerReason))
                LogVerbose($"VIRTUAL-LIFT provider-skip: {providerOI?.ObjectName ?? "null"}->{requester.ObjectName} rd={rd.ID} reason={providerReason}");
            return 0;
        }

        var providerAvailable = GetProviderAvailableAfterMinimum(providerOI, rd, player);
        if (providerAvailable <= 0)
            return 0;

        var support = GetVirtualSurfaceLiftSupport(providerOI, player, snapshot);
        if (support.Count == 0)
            return 0;

        var desired = Math.Min(remaining, providerAvailable);
        if (!TryBuildVirtualLiftPlan(providerOI, requester, rd, desired, providerAvailable, player, support, out var plan))
            return 0;

        if (plan.PayloadAmount <= 0)
            return 0;

        if (!ApplyVirtualLiftResourceChanges(providerOI, requester, rd, player, plan))
            return 0;

        CommitVirtualLiftUsage(providerOI, player, plan.FacilityCapacityUsed);

        var fuelText = FormatVirtualLiftFuel(plan);
        var supportText = string.Join(", ", plan.SupportLabels.Distinct().Take(4));
        Log($"VIRTUAL-LIFT: {providerOI.ObjectName}->{requester.ObjectName} rd={rd.ID} amount={plan.PayloadAmount:0.#}{FormatVirtualLiftSupply(plan)} fuel={fuelText} capacityUsed={plan.FacilityCapacityUsed:0.#} support={supportText}");
        return plan.PayloadAmount;
    }

    private static double TryApplyVirtualOrbitDrop(Data.LogisticsRequest req, ObjectInfo requester,
        ResourceDefinition rd, double remaining, Company player)
    {
        if (req == null || requester == null || rd == null || player == null || remaining <= 0)
            return 0;

        var providerOI = requester.LowOrbitCustom?.GetObjectInfo();
        if (!IsOrbitOf(providerOI, requester))
            return 0;

        var providerData = Data.LogisticsNetwork.Get(providerOI);
        var providerRule = FindAllowedProviderRule(providerData, req, providerOI, requester, rd, out var providerReason);
        if (providerData == null || providerRule == null)
        {
            if (!string.IsNullOrEmpty(providerReason))
                LogVerbose($"ORBIT-DROP provider-skip: {providerOI?.ObjectName ?? "null"}->{requester.ObjectName} rd={rd.ID} reason={providerReason}");
            return 0;
        }

        var providerAvailable = GetProviderAvailableAfterMinimum(providerOI, rd, player);
        var amount = Math.Min(remaining, providerAvailable);
        if (amount <= 0)
            return 0;

        if (!ApplyDirectResourceTransfer(providerOI, requester, rd, amount, player, "ORBIT-DROP"))
            return 0;

        Log($"ORBIT-DROP: {providerOI.ObjectName}->{requester.ObjectName} rd={rd.ID} amount={amount:0.#}");
        return amount;
    }

    private static void ApplyBalancedVirtualSurfaceLift(Company player, PlannerSnapshot snapshot)
    {
        if (!VirtualSurfaceLiftEnabled() || player == null || snapshot?.Objects == null)
            return;

        foreach (var requesterOI in snapshot.Objects)
        {
            var providerOI = requesterOI?.parentObjectInfo;
            if (!IsOrbitOf(requesterOI, providerOI))
                continue;

            var reqData = Data.LogisticsNetwork.Get(requesterOI);
            var providerData = Data.LogisticsNetwork.Get(providerOI);
            if (reqData == null || providerData == null || reqData.requests.Count == 0)
                continue;

            var support = GetVirtualSurfaceLiftSupport(providerOI, player, snapshot);
            if (support.Count == 0)
                continue;

            var capacityLeft = GetVirtualLiftCapacityLeft(providerOI, player, support);
            if (capacityLeft <= 0.001)
                continue;

            var demands = new List<VirtualLiftDemand>();
            foreach (var req in reqData.requests)
            {
                var rd = req?.ResourceDefinition;
                if (rd == null) continue;
                if (FindAllowedProviderRule(providerData, req, providerOI, requesterOI, rd, out _) == null)
                    continue;

                var alreadyThere = requesterOI.GetObjectInfoData(player)?.CheckResources(rd) ?? 0;
                var requestTarget = RequestTarget(req);
                if (alreadyThere >= requestTarget)
                    continue;

                var requestMinimum = RequestMinimum(req);
                if (alreadyThere >= requestMinimum && !HasActiveGhostDelivery(requesterOI, rd))
                    continue;

                var providerAvailable = GetProviderAvailableAfterMinimum(providerOI, rd, player);
                if (providerAvailable <= 0)
                    continue;

                var inFlight = GetInFlightDeliveryAmount(requesterOI, rd, player, snapshot);
                var remaining = Math.Min(requestTarget - alreadyThere - inFlight, providerAvailable);
                if (remaining <= 0)
                    continue;

                demands.Add(new VirtualLiftDemand
                {
                    Request = req,
                    Provider = providerOI,
                    Requester = requesterOI,
                    Resource = rd,
                    Remaining = remaining
                });
            }

            if (demands.Count > 1)
                ApplyBalancedVirtualSurfaceLiftGroup(demands, capacityLeft, support, player);
        }
    }

    private static void ApplyBalancedVirtualSurfaceLiftGroup(List<VirtualLiftDemand> demands,
        double capacityLeft, List<LaunchSupportOption> support, Company player)
    {
        if (demands == null || demands.Count == 0 || capacityLeft <= 0 || support == null || support.Count == 0)
            return;

        var guard = 0;
        while (capacityLeft > 0.001 && guard++ < 16)
        {
            var active = demands
                .Where(d => d != null
                    && d.Remaining > 0.001
                    && GetProviderAvailableAfterMinimum(d.Provider, d.Resource, player) > 0.001)
                .ToList();
            if (active.Count == 0)
                break;

            var share = capacityLeft / active.Count;
            var movedThisRound = 0.0;
            foreach (var demand in active)
            {
                if (capacityLeft <= 0.001)
                    break;

                var providerAvailable = GetProviderAvailableAfterMinimum(demand.Provider, demand.Resource, player);
                var desired = Math.Min(Math.Min(share, demand.Remaining), providerAvailable);
                if (desired <= 0.001)
                    continue;

                if (!TryBuildVirtualLiftPlan(demand.Provider, demand.Requester, demand.Resource,
                        desired, providerAvailable, player, support, out var plan)
                    || plan.PayloadAmount <= 0)
                    continue;

                if (!ApplyVirtualLiftResourceChanges(demand.Provider, demand.Requester, demand.Resource, player, plan))
                    continue;

                CommitVirtualLiftUsage(demand.Provider, player, plan.FacilityCapacityUsed);
                demand.Request.status = Data.LogisticsRequestStatus.InProgress;
                demand.Remaining = Math.Max(0, demand.Remaining - plan.PayloadAmount);
                capacityLeft = Math.Max(0, capacityLeft - plan.FacilityCapacityUsed);
                movedThisRound += plan.PayloadAmount;

                var fuelText = FormatVirtualLiftFuel(plan);
                var supportText = string.Join(", ", plan.SupportLabels.Distinct().Take(4));
                Log($"VIRTUAL-LIFT balanced: {demand.Provider.ObjectName}->{demand.Requester.ObjectName} rd={demand.Resource.ID} amount={plan.PayloadAmount:0.#}{FormatVirtualLiftSupply(plan)} fuel={fuelText} capacityUsed={plan.FacilityCapacityUsed:0.#} support={supportText}");
            }

            if (movedThisRound <= 0.001)
                break;
        }
    }

    private static List<LaunchSupportOption> GetVirtualSurfaceLiftSupport(ObjectInfo providerOI, Company player, PlannerSnapshot snapshot)
    {
        var result = new List<LaunchSupportOption>();
        if (providerOI == null || player == null)
            return result;

        foreach (var option in GetAvailableLaunchSupport(providerOI, player, snapshot)
                     .Where(IsVirtualSurfaceLiftSupport)
                     .OrderBy(option => option.TierAdjustment)
                     .ThenBy(option => option.Type?.Name ?? "LV", StringComparer.OrdinalIgnoreCase))
        {
            if (option.Type == null) continue;
            if (option.Vehicle != null)
            {
                if (option.Vehicle.GetCompany() != player || option.Vehicle.objectInfo != providerOI) continue;
                if (!option.Vehicle.IsReadyToLaunchReusable()) continue;
            }
            else if (!IsBuiltEnabledLaunchSupportFacility(option.Facility, option.Category))
            {
                continue;
            }

            result.Add(option);
        }

        return result;
    }

    private static List<LaunchSupportOption> GetRouteSurfaceLiftSupport(Data.LogisticsRouteRecord route,
        ObjectInfo providerOI, Company player, PlannerSnapshot snapshot)
    {
        if (route == null)
            return GetVirtualSurfaceLiftSupport(providerOI, player, snapshot);
        return GetGhostLaunchSupport(providerOI, player, snapshot, route.routeId);
    }

    private static List<LaunchSupportOption> GetGhostLaunchSupport(ObjectInfo providerOI, Company player, PlannerSnapshot snapshot, int routeId = -1)
    {
        var result = new List<LaunchSupportOption>();
        if (providerOI == null || player == null)
            return result;

        var route = routeId > 0 ? Data.LogisticsNetwork.FindRoute(routeId) : null;
        result.AddRange(GetVirtualSurfaceLiftSupport(providerOI, player, snapshot)
            .Where(option => IsRouteFacilityLaunchAllowed(route, option)));

        var data = Data.LogisticsNetwork.Get(providerOI);
        Data.LogisticsNetwork.RefreshReservedLaunchVehicles(data);
        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        foreach (var record in data?.ghostLaunchVehicles ?? Enumerable.Empty<Data.GhostLaunchVehicleRecord>())
        {
            if (record == null || record.currentObjectId != providerOI.id || !Data.LogisticsNetwork.IsGhostLaunchVehicleReady(record, now))
                continue;
            if (routeId > 0 && record.assignedRouteId != routeId)
                continue;

            var type = ResolveLaunchVehicleType(record.launchVehicleTypeId);
            if (type == null)
                continue;

            result.Add(new LaunchSupportOption
            {
                Type = type,
                Category = "reserved-launch-vehicle",
                Label = $"{type.Name} [reserved]",
                IsFacilityBacked = false,
                ReservedLaunchVehicle = record,
                TierAdjustment = -8
            });
        }

        return result
            .OrderBy(option => option.TierAdjustment)
            .ThenBy(option => option.Type?.Name ?? "LV", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsVirtualSurfaceLiftSupport(LaunchSupportOption option)
    {
        if (option?.Type == null)
            return false;
        return option.IsFacilityBacked || string.Equals(option.Category, "space-elevator", StringComparison.Ordinal);
    }

    private static bool IsRouteFacilityLaunchAllowed(Data.LogisticsRouteRecord route, LaunchSupportOption option)
    {
        if (route == null || option == null || option.ReservedLaunchVehicle != null || !IsVirtualSurfaceLiftSupport(option))
            return true;

        var disabled = route.disabledFacilityLaunchCategories;
        if (disabled == null || disabled.Count == 0)
            return true;

        var category = NormalizeLaunchSupportCategory(option.Category);
        if (string.IsNullOrWhiteSpace(category))
            return true;

        return !disabled.Any(disabledCategory =>
        {
            var disabledCategoryName = NormalizeLaunchSupportCategory(disabledCategory);
            return !string.IsNullOrWhiteSpace(disabledCategoryName)
                && string.Equals(disabledCategoryName, category, StringComparison.Ordinal);
        });
    }

    private static string NormalizeLaunchSupportCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return "";

        switch (category.Trim().ToLowerInvariant())
        {
            case "magnetic-launch-rails":
            case "launch-pad":
            case "rotary-launcher":
            case "space-elevator":
            case "electromagnetic-catapult":
            case "stationary-mass-driver":
            case "reserved-launch-vehicle":
            case "standard-launch":
                return category.Trim().ToLowerInvariant();
            default:
                return "";
        }
    }

    private static bool TryBuildVirtualLiftPlan(ObjectInfo providerOI, ObjectInfo requester,
        ResourceDefinition rd, double desiredPayload, double providerAvailable, Company player,
        List<LaunchSupportOption> support, out VirtualLiftPlan plan)
    {
        plan = new VirtualLiftPlan();
        if (desiredPayload <= 0 || providerAvailable <= 0 || support == null || support.Count == 0)
            return false;

        var usedToday = GetVirtualLiftUsedToday(providerOI, player);
        var skipCapacity = usedToday;
        var payloadsPerDay = VirtualSurfaceLiftPayloadsPerDay();
        if (payloadsPerDay <= 0)
            return false;

        var isHumanPayload = IsHumanResource(rd);
        var supplyResource = isHumanPayload ? ResolveSupplyResource() : null;
        if (isHumanPayload && supplyResource == null)
            return false;
        var supplyPerUnit = isHumanPayload ? EstimateCrewSupplyNeed(1, EstimateGhostTravelDays(providerOI, requester), player) : 0;
        var payloadMassPerUnit = GetPayloadMassPerResourceUnit(rd, supplyPerUnit);
        var sourceData = isHumanPayload ? providerOI?.GetObjectInfoData(player) : null;
        var supplyAvailable = supplyResource == null || sourceData == null ? 0 : sourceData.CheckResources(supplyResource);

        var cargoAvailable = providerAvailable;
        var desiredRemaining = desiredPayload;
        var fuelPlanned = new Dictionary<ResourceDefinition, double>();

        foreach (var option in support)
        {
            if (!CanLiftResourceWithSupport(option, rd))
                continue;

            var singlePayload = GetVirtualLiftSinglePayloadCapacity(option, providerOI, player);
            if (singlePayload <= 0)
                continue;

            var siteCount = GetVirtualLiftSiteCount(option);
            if (siteCount <= 0)
                continue;

            var optionCapacity = singlePayload * siteCount * payloadsPerDay;
            if (optionCapacity <= 0)
                continue;

            if (skipCapacity >= optionCapacity)
            {
                skipCapacity -= optionCapacity;
                continue;
            }

            var remainingOptionCapacity = optionCapacity - skipCapacity;
            skipCapacity = 0;

            var fuelType = option.Type.FuelTypeOnStart;
            var fuelPerPayloadTon = GetVirtualLiftFuelPerPayloadTon(option, singlePayload, providerOI, player);

            while (remainingOptionCapacity > 0.001 && desiredRemaining > 0.001 && cargoAvailable > 0.001)
            {
                var maxChunk = Math.Min(remainingOptionCapacity / Math.Max(0.001, payloadMassPerUnit), desiredRemaining);
                if (fuelType == rd && fuelPerPayloadTon > 0)
                    maxChunk = Math.Min(maxChunk, cargoAvailable / (1.0 + fuelPerPayloadTon));
                else
                    maxChunk = Math.Min(maxChunk, cargoAvailable);

                if (isHumanPayload)
                {
                    var supplyLeft = supplyAvailable - plan.SupplyConsumed;
                    maxChunk = supplyPerUnit > 0 ? Math.Min(maxChunk, supplyLeft / supplyPerUnit) : maxChunk;
                    maxChunk = Math.Floor(maxChunk);
                }

                if (fuelType != null && fuelType != rd && fuelPerPayloadTon > 0)
                {
                    fuelPlanned.TryGetValue(fuelType, out var alreadyPlannedFuel);
                    var fuelAvailable = GetVirtualLiftFuelAvailable(providerOI, fuelType, player) - alreadyPlannedFuel;
                    maxChunk = Math.Min(maxChunk, fuelAvailable / (fuelPerPayloadTon * Math.Max(0.001, payloadMassPerUnit)));
                }

                if (isHumanPayload)
                    maxChunk = Math.Floor(maxChunk);

                if (maxChunk <= 0.001)
                    break;

                var chunk = maxChunk;
                var capacityUsed = chunk * payloadMassPerUnit;
                var supplyUsed = isHumanPayload ? chunk * supplyPerUnit : 0;
                var fuelAmount = fuelPerPayloadTon > 0 ? capacityUsed * fuelPerPayloadTon : 0;
                plan.PayloadAmount += chunk;
                if (supplyResource != null && supplyUsed > 0)
                {
                    plan.SupplyResource = supplyResource;
                    plan.SupplyConsumed += supplyUsed;
                }
                plan.FacilityCapacityUsed += capacityUsed;
                plan.SharedFacilityCapacityUsed += capacityUsed;
                desiredRemaining -= chunk;
                remainingOptionCapacity -= capacityUsed;
                cargoAvailable -= chunk;
                if (fuelType == rd)
                    cargoAvailable -= fuelAmount;

                if (fuelType != null && fuelAmount > 0)
                {
                    fuelPlanned.TryGetValue(fuelType, out var existingFuel);
                    fuelPlanned[fuelType] = existingFuel + fuelAmount;
                }

                if (!string.IsNullOrWhiteSpace(option.Label))
                    plan.SupportLabels.Add(option.Label);
            }

            if (desiredRemaining <= 0.001)
                break;
        }

        foreach (var kv in fuelPlanned)
            plan.FuelByResource[kv.Key] = kv.Value;

        return plan.PayloadAmount > 0;
    }

    private static bool TryBuildRouteSurfaceLiftPlan(ObjectInfo providerOI, ObjectInfo requester,
        ResourceDefinition rd, double desiredPayload, double providerAvailable, Company player,
        List<LaunchSupportOption> support, RouteLiftCapacityState capacityState, out VirtualLiftPlan plan)
    {
        plan = new VirtualLiftPlan();
        if (desiredPayload <= 0 || providerAvailable <= 0 || support == null || support.Count == 0)
            return false;
        capacityState ??= new RouteLiftCapacityState();

        var cargoAvailable = providerAvailable;
        var desiredRemaining = desiredPayload;
        var fuelPlanned = new Dictionary<ResourceDefinition, double>();
        var sharedSkipCapacity = GetVirtualLiftUsedToday(providerOI, player);
        var isHumanPayload = IsHumanResource(rd);
        var supplyResource = isHumanPayload ? ResolveSupplyResource() : null;
        if (isHumanPayload && supplyResource == null)
            return false;
        var supplyPerUnit = isHumanPayload ? EstimateCrewSupplyNeed(1, EstimateGhostTravelDays(providerOI, requester), player) : 0;
        var payloadMassPerUnit = GetPayloadMassPerResourceUnit(rd, supplyPerUnit);
        var sourceData = isHumanPayload ? providerOI?.GetObjectInfoData(player) : null;
        var supplyAvailable = supplyResource == null || sourceData == null ? 0 : sourceData.CheckResources(supplyResource);

        foreach (var option in support)
        {
            if (!CanLiftResourceWithSupport(option, rd))
                continue;

            var optionCapacity = GetRouteSurfaceLiftOptionCapacity(option, providerOI, player);
            if (optionCapacity <= 0)
                continue;

            var remainingOptionCapacity = optionCapacity;
            var reservedRecord = option.ReservedLaunchVehicle;
            if (reservedRecord != null)
            {
                remainingOptionCapacity = Math.Max(0, optionCapacity - capacityState.GetReservedUsed(reservedRecord));
            }
            else
            {
                if (sharedSkipCapacity >= optionCapacity)
                {
                    sharedSkipCapacity -= optionCapacity;
                    continue;
                }
                remainingOptionCapacity = optionCapacity - sharedSkipCapacity;
                sharedSkipCapacity = 0;
            }

            var fuelType = option.Type.FuelTypeOnStart;
            var singlePayload = GetVirtualLiftSinglePayloadCapacity(option, providerOI, player);
            var fuelPerPayloadTon = GetVirtualLiftFuelPerPayloadTon(option, singlePayload, providerOI, player);

            while (remainingOptionCapacity > 0.001 && desiredRemaining > 0.001 && cargoAvailable > 0.001)
            {
                var maxChunk = Math.Min(remainingOptionCapacity / Math.Max(0.001, payloadMassPerUnit), desiredRemaining);
                if (fuelType == rd && fuelPerPayloadTon > 0)
                    maxChunk = Math.Min(maxChunk, cargoAvailable / (1.0 + fuelPerPayloadTon));
                else
                    maxChunk = Math.Min(maxChunk, cargoAvailable);

                if (isHumanPayload)
                {
                    var supplyLeft = supplyAvailable - plan.SupplyConsumed;
                    maxChunk = supplyPerUnit > 0 ? Math.Min(maxChunk, supplyLeft / supplyPerUnit) : maxChunk;
                    maxChunk = Math.Floor(maxChunk);
                }

                if (fuelType != null && fuelType != rd && fuelPerPayloadTon > 0)
                {
                    fuelPlanned.TryGetValue(fuelType, out var alreadyPlannedFuel);
                    var fuelAvailable = GetVirtualLiftFuelAvailable(providerOI, fuelType, player) - alreadyPlannedFuel;
                    maxChunk = Math.Min(maxChunk, fuelAvailable / (fuelPerPayloadTon * Math.Max(0.001, payloadMassPerUnit)));
                }

                if (isHumanPayload)
                    maxChunk = Math.Floor(maxChunk);

                if (maxChunk <= 0.001)
                    break;

                var chunk = maxChunk;
                var capacityUsed = chunk * payloadMassPerUnit;
                var supplyUsed = isHumanPayload ? chunk * supplyPerUnit : 0;
                var fuelAmount = fuelPerPayloadTon > 0 ? capacityUsed * fuelPerPayloadTon : 0;
                plan.PayloadAmount += chunk;
                if (supplyResource != null && supplyUsed > 0)
                {
                    plan.SupplyResource = supplyResource;
                    plan.SupplyConsumed += supplyUsed;
                }
                plan.FacilityCapacityUsed += capacityUsed;
                if (reservedRecord == null)
                    plan.SharedFacilityCapacityUsed += capacityUsed;
                else
                {
                    plan.ReservedLaunchCapacityByVehicle.TryGetValue(reservedRecord, out var existingUsed);
                    plan.ReservedLaunchCapacityByVehicle[reservedRecord] = existingUsed + capacityUsed;
                }

                desiredRemaining -= chunk;
                remainingOptionCapacity -= capacityUsed;
                cargoAvailable -= chunk;
                if (fuelType == rd)
                    cargoAvailable -= fuelAmount;

                if (fuelType != null && fuelAmount > 0)
                {
                    fuelPlanned.TryGetValue(fuelType, out var existingFuel);
                    fuelPlanned[fuelType] = existingFuel + fuelAmount;
                }

                if (!string.IsNullOrWhiteSpace(option.Label))
                    plan.SupportLabels.Add(option.Label);
            }

            if (desiredRemaining <= 0.001)
                break;
        }

        foreach (var kv in fuelPlanned)
            plan.FuelByResource[kv.Key] = kv.Value;

        return plan.PayloadAmount > 0;
    }

    private static double GetRouteSurfaceLiftCapacityLeft(ObjectInfo providerOI, Company player,
        List<LaunchSupportOption> support, RouteLiftCapacityState capacityState)
    {
        if (support == null || support.Count == 0)
            return 0;
        capacityState ??= new RouteLiftCapacityState();

        var sharedTotal = 0.0;
        var reservedTotal = 0.0;
        foreach (var option in support)
        {
            var capacity = GetRouteSurfaceLiftOptionCapacity(option, providerOI, player);
            if (capacity <= 0)
                continue;

            if (option.ReservedLaunchVehicle != null)
                reservedTotal += Math.Max(0, capacity - capacityState.GetReservedUsed(option.ReservedLaunchVehicle));
            else
                sharedTotal += capacity;
        }

        var sharedLeft = Math.Max(0, sharedTotal - GetVirtualLiftUsedToday(providerOI, player));
        return sharedLeft + reservedTotal;
    }

    private static double GetRouteSurfaceLiftCapacityLeftForResource(ObjectInfo providerOI, Company player,
        List<LaunchSupportOption> support, RouteLiftCapacityState capacityState, ResourceDefinition rd)
    {
        if (support == null || support.Count == 0)
            return 0;
        capacityState ??= new RouteLiftCapacityState();

        var sharedTotal = 0.0;
        var reservedTotal = 0.0;
        foreach (var option in support)
        {
            if (!CanLiftResourceWithSupport(option, rd))
                continue;

            var capacity = GetRouteSurfaceLiftOptionCapacity(option, providerOI, player);
            if (capacity <= 0)
                continue;

            if (option.ReservedLaunchVehicle != null)
                reservedTotal += Math.Max(0, capacity - capacityState.GetReservedUsed(option.ReservedLaunchVehicle));
            else
                sharedTotal += capacity;
        }

        var sharedLeft = Math.Max(0, sharedTotal - GetVirtualLiftUsedToday(providerOI, player));
        return sharedLeft + reservedTotal;
    }

    private static bool HasEligibleRouteSurfaceLiftSupport(List<LaunchSupportOption> support,
        ObjectInfo providerOI, Company player, ResourceDefinition rd)
    {
        return GetRouteSurfaceLiftCapacityLeftForResource(providerOI, player, support, new RouteLiftCapacityState(), rd) > 0.001;
    }

    private static bool CanLiftResourceWithSupport(LaunchSupportOption option, ResourceDefinition rd)
    {
        if (option == null)
            return false;
        if (IsHumanResource(rd) && IsSharedFacilityLiftSupport(option) && !IsSpaceElevatorSupport(option))
            return false;
        return true;
    }

    private static bool IsSharedFacilityLiftSupport(LaunchSupportOption option)
    {
        return option != null && option.ReservedLaunchVehicle == null && IsVirtualSurfaceLiftSupport(option);
    }

    private static bool IsSpaceElevatorSupport(LaunchSupportOption option)
    {
        return string.Equals(option?.Category, "space-elevator", StringComparison.Ordinal);
    }

    private static bool IsHumanResource(ResourceDefinition rd)
    {
        return rd != null && rd.ResourceType == ResourceDefinition.EResourceType.Human;
    }

    private static ResourceDefinition ResolveSupplyResource()
    {
        var allResources = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllResourceDefinitions;
        return allResources?.Supply ?? allResources?.GetByID("id_resource_supply");
    }

    private static double GetPayloadMassPerResourceUnit(ResourceDefinition rd, double supplyPerHuman)
    {
        if (IsHumanResource(rd))
            return HumanLogisticsPayloadMass + Math.Max(0, supplyPerHuman);
        return 1.0;
    }

    private static bool IsFullRouteGhostLoad(double amount, double capacity, double payloadMassPerUnit, bool discreteUnits, out double fullLoadAmount)
    {
        fullLoadAmount = 0;
        if (capacity <= 0.001 || payloadMassPerUnit <= 0.001)
            return false;

        fullLoadAmount = capacity / payloadMassPerUnit;
        if (discreteUnits)
        {
            fullLoadAmount = Math.Floor(fullLoadAmount);
            return fullLoadAmount <= 0.001 || amount + 0.001 >= fullLoadAmount;
        }

        return amount * payloadMassPerUnit + 0.05 >= capacity;
    }

    private static double GetPayloadCargoMass(ResourceDefinition rd, double amount, double supplyConsumed)
    {
        var resourceMass = IsHumanResource(rd)
            ? Math.Max(0, amount) * HumanLogisticsPayloadMass
            : Math.Max(0, amount);
        return resourceMass + Math.Max(0, supplyConsumed);
    }

    private static double EstimateCrewSupplyNeed(double crew, double travelDays, Company player)
    {
        if (crew <= 0 || travelDays <= 0)
            return 0;

        var economic = MonoBehaviourSingleton<GameManager>.Instance?.Economic;
        if (economic == null || player == null)
            return 0;

        var lifeSupportMultiplier = Math.Max(0.001, economic.GetLifeSupportMultiplayer(player));
        var supplyToLifeSupport = Math.Max(0.001, economic.SupplyToLifeSupportMultiplayer);
        var lifeSupportNeed = travelDays * crew / lifeSupportMultiplier;
        return Math.Max(0, lifeSupportNeed / supplyToLifeSupport);
    }

    private static double GetRouteSurfaceLiftOptionCapacity(LaunchSupportOption option, ObjectInfo providerOI, Company player)
    {
        if (option?.Type == null || providerOI == null || player == null)
            return 0;

        var singlePayload = GetVirtualLiftSinglePayloadCapacity(option, providerOI, player);
        if (singlePayload <= 0)
            return 0;

        if (option.ReservedLaunchVehicle != null)
            return singlePayload;

        var siteCount = GetVirtualLiftSiteCount(option);
        var payloadsPerDay = VirtualSurfaceLiftPayloadsPerDay();
        return siteCount > 0 && payloadsPerDay > 0 ? singlePayload * siteCount * payloadsPerDay : 0;
    }

    private static double GetVirtualLiftCapacityLeft(ObjectInfo providerOI, Company player, List<LaunchSupportOption> support)
    {
        if (support == null || support.Count == 0)
            return 0;

        var payloadsPerDay = VirtualSurfaceLiftPayloadsPerDay();
        if (payloadsPerDay <= 0)
            return 0;

        var total = support.Sum(option =>
        {
            var singlePayload = GetVirtualLiftSinglePayloadCapacity(option, providerOI, player);
            var siteCount = GetVirtualLiftSiteCount(option);
            return singlePayload > 0 && siteCount > 0 ? singlePayload * siteCount * payloadsPerDay : 0;
        });

        return Math.Max(0, total - GetVirtualLiftUsedToday(providerOI, player));
    }

    private static double GetVirtualLiftSinglePayloadCapacity(LaunchSupportOption option, ObjectInfo providerOI, Company player)
    {
        if (option?.Type == null || providerOI == null || player == null)
            return 0;

        var payload = option.Type.MaxPayloadOnThisObject(providerOI, player);
        if (payload <= 0)
            payload = option.Type.maxPayload;
        return Math.Max(0, payload);
    }

    private static double GetVirtualLiftFuelPerPayloadTon(LaunchSupportOption option, double singlePayload, ObjectInfo providerOI, Company player)
    {
        if (option?.Type == null || providerOI == null || player == null || singlePayload <= 0)
            return 0;

        var launchFuelCost = GetVirtualLiftFullPayloadLaunchFuelCost(option, providerOI, player);
        return Math.Max(0, launchFuelCost) / singlePayload;
    }

    private static double GetVirtualLiftFullPayloadLaunchFuelCost(LaunchSupportOption option, ObjectInfo providerOI, Company player)
    {
        if (option?.Type == null || providerOI == null || player == null)
            return 0;

        var launchFuelCost = (double)option.Type.costLaunch;
        if (launchFuelCost <= 0)
            return 0;

        launchFuelCost *= GetVirtualLiftLaunchEnvironmentMultiplier(providerOI);
        launchFuelCost *= GetVirtualLiftRelativeSurfaceGravity(providerOI, player);

        if (player.BonusController != null)
        {
            launchFuelCost *= player.BonusController.GetBonus(EBonus.LaunchCost);
            launchFuelCost *= player.BonusController.GetBonus(EBonus.LaunchCost, option.Type);
            var providerData = providerOI.GetObjectInfoData(player);
            if (providerData != null)
                launchFuelCost *= player.BonusController.GetBonusFromPlanet(EBonus.LaunchCost, providerData, option.Type);
        }

        if (launchFuelCost < 0.1)
            launchFuelCost = 0.1;

        return Math.Round(launchFuelCost * 10.0) / 10.0;
    }

    private static double GetVirtualLiftLaunchEnvironmentMultiplier(ObjectInfo providerOI)
    {
        if (providerOI == null || providerOI.objectTypes == global::Data.EObjectTypes.Orbit)
            return 1.0;

        var curve = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance
            ?.FlightConfig
            ?.HabitabilityParameterFlightCostMultiplierCurve;
        var multiplier = curve != null ? curve.GetMeanForObject(providerOI, null) : 1.0;
        return IsUsablePositiveNumber(multiplier) ? multiplier : 1.0;
    }

    private static double GetVirtualLiftRelativeSurfaceGravity(ObjectInfo providerOI, Company player)
    {
        var reference = player?.mainObjectInfo ?? MonoBehaviourSingleton<GameManager>.Instance?.Player?.mainObjectInfo;
        if (providerOI == null || reference == null)
            return 1.0;

        var massRatio = providerOI.Mass / reference.Mass;
        var radiusRatio = providerOI.Radius / reference.Radius;
        if (!IsUsablePositiveNumber(massRatio) || !IsUsablePositiveNumber(radiusRatio) || Math.Abs(radiusRatio) < 0.00001)
            return 1.0;

        var gravityRatio = massRatio / Math.Pow(radiusRatio, 2.0);
        return IsUsablePositiveNumber(gravityRatio) ? gravityRatio : 1.0;
    }

    private static bool IsUsablePositiveNumber(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static long GetVirtualLiftSiteCount(LaunchSupportOption option)
    {
        if (option?.Facility == null)
            return 1;
        if (!option.Facility.BuildingWorking)
            return 0;
        return 1;
    }

    private static double GetVirtualLiftFuelAvailable(ObjectInfo providerOI, ResourceDefinition fuelType, Company player)
    {
        if (providerOI == null || fuelType == null || player == null)
            return 0;

        var oid = providerOI.GetObjectInfoData(player);
        if (oid == null)
            return 0;

        var data = Data.LogisticsNetwork.Get(providerOI);
        var minKeep = data?.providers?
            .Where(p => p.isActive && p.ResourceDefinition == fuelType)
            .Sum(p => p.minimumKeep) ?? 0;
        return Math.Max(0, oid.CheckResources(fuelType) - minKeep - GetCommittedStock(providerOI, fuelType));
    }

    private static bool IsReservedLaunchVehicleReady(Data.GhostLaunchVehicleRecord record)
    {
        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        return Data.LogisticsNetwork.IsGhostLaunchVehicleReady(record, now);
    }

    private static void MarkReservedLaunchVehiclesUsed(ObjectInfo providerOI, Company player, GhostLaunchPlan plan)
    {
        if (providerOI == null || player == null || plan?.ReservedLaunchVehiclesUsed == null || plan.ReservedLaunchVehiclesUsed.Count == 0)
            return;

        MarkRouteReservedLaunchVehiclesUsed(providerOI, player, plan.ReservedLaunchVehiclesUsed);
    }

    private static void MarkRouteReservedLaunchVehiclesUsed(ObjectInfo providerOI, Company player,
        IEnumerable<Data.GhostLaunchVehicleRecord> records)
    {
        if (providerOI == null || player == null || records == null)
            return;

        var data = Data.LogisticsNetwork.Get(providerOI);
        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        foreach (var record in records.Distinct().ToList())
        {
            if (record == null)
                continue;
            if (record.status == Data.GhostLaunchVehicleStatus.Retired)
                continue;

            var lvType = ResolveLaunchVehicleType(record.launchVehicleTypeId);
            if (lvType == null || lvType.reusability <= 0f)
            {
                record.status = Data.GhostLaunchVehicleStatus.Retired;
                Log($"GHOST reserved-lv-consumed: body={providerOI.ObjectName} lv={record.typeName ?? record.launchVehicleTypeId}");
                continue;
            }

            var recoveryDays = GetReservedLaunchVehicleRecoveryDays(lvType, player);
            record.status = Data.GhostLaunchVehicleStatus.CoolingDown;
            record.availableDate = now.AddDays(recoveryDays);
            record.blockedReason = null;
            Log($"GHOST reserved-lv-recovering: body={providerOI.ObjectName} lv={lvType.Name} ready={record.availableDate:yyyy-MM-dd}");
        }

        if (data?.ghostLaunchVehicles != null)
            data.ghostLaunchVehicles.RemoveAll(lv => lv == null || lv.status == Data.GhostLaunchVehicleStatus.Retired);
    }

    private static double GetReservedLaunchVehicleRecoveryDays(LaunchVehicleType lvType, Company player)
    {
        if (lvType == null || player == null)
            return 0;

        var days = (1f - lvType.reusability) * lvType.TimeToBuildInDays;
        if (player.BonusController != null)
        {
            days *= player.BonusController.GetBonus(EBonus.BuildSpeed, lvType);
            days *= player.BonusController.GetBonus(EBonus.BuildSpeed);
        }
        return Math.Max(0, days);
    }

    private static bool ApplyVirtualLiftResourceChanges(ObjectInfo providerOI, ObjectInfo requester,
        ResourceDefinition rd, Company player, VirtualLiftPlan plan)
    {
        var sourceData = providerOI?.GetObjectInfoData(player);
        var targetData = requester?.GetObjectInfoData(player);
        if (sourceData == null || targetData == null || rd == null || plan == null || plan.PayloadAmount <= 0)
            return false;

        var removals = new Dictionary<ResourceDefinition, double> { [rd] = plan.PayloadAmount };
        if (plan.SupplyResource != null && plan.SupplyConsumed > 0)
        {
            removals.TryGetValue(plan.SupplyResource, out var existingSupply);
            removals[plan.SupplyResource] = existingSupply + plan.SupplyConsumed;
        }
        foreach (var kv in plan.FuelByResource)
        {
            if (kv.Key == null || kv.Value <= 0) continue;
            removals.TryGetValue(kv.Key, out var existing);
            removals[kv.Key] = existing + kv.Value;
        }

        foreach (var kv in removals)
        {
            if (sourceData.CheckResources(kv.Key) + 0.001 < kv.Value)
            {
                LogWarning($"VIRTUAL-LIFT abort: source={providerOI.ObjectName} target={requester.ObjectName} rd={rd.ID} missing={kv.Key.ID} need={kv.Value:0.#} have={sourceData.CheckResources(kv.Key):0.#}");
                return false;
            }
        }

        var removed = new List<KeyValuePair<ResourceDefinition, double>>();
        foreach (var kv in removals)
        {
            if (!sourceData.RemoveResource(kv.Key, kv.Value))
            {
                foreach (var rollback in removed)
                    sourceData.AddResources(rollback.Key, rollback.Value);
                LogWarning($"VIRTUAL-LIFT abort: remove failed source={providerOI.ObjectName} rd={kv.Key.ID} amount={kv.Value:0.#}");
                return false;
            }
            removed.Add(kv);
        }

        if (!targetData.AddResources(rd, plan.PayloadAmount))
        {
            foreach (var rollback in removed)
                sourceData.AddResources(rollback.Key, rollback.Value);
            LogWarning($"VIRTUAL-LIFT abort: add failed target={requester.ObjectName} rd={rd.ID} amount={plan.PayloadAmount:0.#}");
            return false;
        }

        return true;
    }

    private static bool CanApplyResourceRemovals(List<ResourceRemoval> removals, out string reason)
    {
        reason = null;
        var grouped = removals
            .Where(r => r?.Data != null && r.Resource != null && r.Amount > 0)
            .GroupBy(r => new { r.Data, r.Resource })
            .Select(g => new ResourceRemoval { Data = g.Key.Data, Resource = g.Key.Resource, Amount = g.Sum(x => x.Amount) })
            .ToList();

        foreach (var removal in grouped)
        {
            var have = removal.Data.CheckResources(removal.Resource);
            if (have + 0.001 < removal.Amount)
            {
                reason = $"Missing {removal.Resource.ID}: need {removal.Amount:0.#}, have {have:0.#}";
                return false;
            }
        }

        return true;
    }

    private static bool ApplyDirectResourceTransfer(ObjectInfo providerOI, ObjectInfo requester,
        ResourceDefinition rd, double amount, Company player, string label)
    {
        var sourceData = providerOI?.GetObjectInfoData(player);
        var targetData = requester?.GetObjectInfoData(player);
        if (sourceData == null || targetData == null || rd == null || amount <= 0)
            return false;

        if (sourceData.CheckResources(rd) + 0.001 < amount)
        {
            LogWarning($"{label} abort: source={providerOI.ObjectName} target={requester.ObjectName} rd={rd.ID} need={amount:0.#} have={sourceData.CheckResources(rd):0.#}");
            return false;
        }

        if (!sourceData.RemoveResource(rd, amount))
        {
            LogWarning($"{label} abort: remove failed source={providerOI.ObjectName} rd={rd.ID} amount={amount:0.#}");
            return false;
        }

        if (!targetData.AddResources(rd, amount))
        {
            sourceData.AddResources(rd, amount);
            LogWarning($"{label} abort: add failed target={requester.ObjectName} rd={rd.ID} amount={amount:0.#}");
            return false;
        }

        return true;
    }

    private static string FormatVirtualLiftFuel(VirtualLiftPlan plan)
    {
        if (plan?.FuelByResource == null || plan.FuelByResource.Count == 0)
            return "none";
        return string.Join(",", plan.FuelByResource
            .Where(kv => kv.Key != null && kv.Value > 0)
            .Select(kv => $"{kv.Key.ID}:{kv.Value:0.#}"));
    }

    private static string FormatVirtualLiftSupply(VirtualLiftPlan plan)
    {
        return plan != null && plan.SupplyConsumed > 0
            ? $" supplyUsed={plan.SupplyConsumed:0.###}"
            : "";
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

    private static bool IsOrbitOf(ObjectInfo orbit, ObjectInfo body)
    {
        if (orbit == null || body == null) return false;
        if (body.LowOrbitCustom != null && SameObjectInfo(body.LowOrbitCustom.GetObjectInfo(), orbit))
            return true;
        if (orbit.objectTypes == global::Data.EObjectTypes.Orbit && SameObjectInfo(orbit.parentObjectInfo, body))
            return true;
        return LooksLikeOrbitOfBody(orbit, body);
    }

    private static bool SameObjectInfo(ObjectInfo left, ObjectInfo right)
    {
        if (left == null || right == null)
            return left == right;
        return left == right || left.id == right.id;
    }

    private static bool LooksLikeOrbitOfBody(ObjectInfo orbit, ObjectInfo body)
    {
        if (orbit == null || body == null)
            return false;
        if (orbit.objectTypes != global::Data.EObjectTypes.Orbit
            && !ContainsOrbitMarker(orbit.ObjectName))
            return false;

        var orbitBodyName = CanonicalBodyName(orbit);
        var bodyName = CanonicalBodyName(body);
        return !string.IsNullOrWhiteSpace(orbitBodyName)
            && !string.IsNullOrWhiteSpace(bodyName)
            && string.Equals(orbitBodyName, bodyName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsOrbitMarker(string name)
    {
        return !string.IsNullOrWhiteSpace(name)
            && name.IndexOf("[ORBIT]", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string CanonicalBodyName(ObjectInfo oi)
    {
        if (oi == null)
            return "";
        if (oi.objectTypes == global::Data.EObjectTypes.Orbit && oi.parentObjectInfo != null)
            return CanonicalBodyName(oi.parentObjectInfo);

        var name = oi.ObjectName ?? "";
        var orbitIndex = name.IndexOf("[ORBIT]", StringComparison.OrdinalIgnoreCase);
        if (orbitIndex >= 0)
            name = (name.Substring(0, orbitIndex) + name.Substring(orbitIndex + "[ORBIT]".Length)).Trim();
        return name.Trim();
    }

    private static Data.LogisticsProvider FindAllowedProviderRule(Data.LogisticsObjectData providerData,
        Data.LogisticsRequest request, ObjectInfo providerOI, ObjectInfo requesterOI, ResourceDefinition rd, out string reason)
    {
        reason = null;
        if (providerData?.providers == null || rd == null)
            return null;

        foreach (var provider in providerData.providers)
        {
            if (provider == null || !provider.isActive || !ResourceMatches(provider, rd))
                continue;
            return provider;
        }

        return null;
    }

    private static bool IsRouteBlockedByCurrentRules(ObjectInfo providerOI, ObjectInfo requesterOI,
        ResourceDefinition rd, out string reason)
    {
        reason = null;
        return false;
    }

    private static bool ResourceMatches(Data.LogisticsRequest request, ResourceDefinition rd)
    {
        if (request == null || rd == null)
            return false;
        var id = request.ResourceDefinition?.ID ?? request.resourceDef.id;
        return string.Equals(id, rd.ID, StringComparison.Ordinal);
    }

    private static bool ResourceMatches(Data.LogisticsProvider provider, ResourceDefinition rd)
    {
        if (provider == null || rd == null)
            return false;
        var id = provider.ResourceDefinition?.ID ?? provider.resourceDef.id;
        return string.Equals(id, rd.ID, StringComparison.Ordinal);
    }

    private static string TryCreateDeliveries(Data.LogisticsRequest req, ObjectInfo requester,
        ResourceDefinition rd, double remaining, Company player, PlannerSnapshot snapshot = null)
    {
        LogVerbose($"DISPATCH begin: target={requester?.ObjectName} rd={rd.ID} remaining={remaining:0.#} systems=orbit-drop,virtual-lift,ghost-ledger");

        var orbitDropped = TryApplyVirtualOrbitDrop(req, requester, rd, remaining, player);
        if (orbitDropped > 0)
        {
            remaining = Math.Max(0, remaining - orbitDropped);
            req.status = Data.LogisticsRequestStatus.InProgress;
            if (remaining <= 0.001)
                return null;
        }

        var virtualLifted = TryApplyVirtualSurfaceLift(req, requester, rd, remaining, player, snapshot);
        if (virtualLifted > 0)
        {
            remaining = Math.Max(0, remaining - virtualLifted);
            req.status = Data.LogisticsRequestStatus.InProgress;
            if (remaining <= 0.001)
                return null;
        }

        var ghostReason = TryCreateGhostDelivery(req, requester, rd, remaining, player, snapshot);
        if (string.IsNullOrEmpty(ghostReason))
            return null;
        return ghostReason;
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

        AddBuiltFacilityLaunchSupport(result, objectData, providerOI, player);

        return result;
    }

    private static void AddBuiltFacilityLaunchSupport(List<LaunchSupportOption> result, ObjectInfoData objectData,
        ObjectInfo providerOI, Company player)
    {
        if (result == null || objectData?.ListFacility == null || providerOI == null || player == null)
            return;

        foreach (var facility in objectData.ListFacility)
        {
            if (!TryGetBuiltEnabledLaunchSupportCategory(facility, out var category))
                continue;
            if (result.Any(option => string.Equals(
                    NormalizeLaunchSupportCategory(option?.Category),
                    category,
                    StringComparison.Ordinal)))
                continue;

            var type = ResolveFacilityLaunchSupportType(facility, category);
            if (type == null)
            {
                LogWarning($"Built launch facility {FacilityLaunchSupportDisplayName(facility.facilityDescriptor)} at {providerOI.ObjectName} has no matching launch support type for {category}.");
                continue;
            }

            result.Add(new LaunchSupportOption
            {
                Vehicle = null,
                Type = type,
                Facility = facility,
                Category = category,
                IsFacilityBacked = true,
                Label = $"{type.Name ?? type.ID ?? "Launch Support"} via {FacilityLaunchSupportDisplayName(facility.facilityDescriptor)} [{category}]",
                TierAdjustment = GetLaunchSupportTierAdjustment(category)
            });
        }
    }

    private static bool IsBuiltEnabledLaunchSupportFacility(Facility facility, string expectedCategory)
    {
        if (!TryGetBuiltEnabledLaunchSupportCategory(facility, out var category))
            return false;
        var normalizedExpected = NormalizeLaunchSupportCategory(expectedCategory);
        return string.IsNullOrWhiteSpace(normalizedExpected)
            || string.Equals(category, normalizedExpected, StringComparison.Ordinal);
    }

    private static bool TryGetBuiltEnabledLaunchSupportCategory(Facility facility, out string category)
    {
        category = "";
        if (facility?.facilityDescriptor == null)
            return false;
        if (facility.BuildProgress < 1f || facility.Enabled <= 0 || facility.SinglePowerProductionMultiplier < 0.999)
            return false;

        category = GetLaunchSupportCategory(facility.facilityDescriptor);
        return IsFacilityLaunchSupportCategory(category)
            && IsLaunchSupportFacilityDescriptor(facility.facilityDescriptor, category);
    }

    private static string GetLaunchSupportCategory(FacilityBaseDescriptor descriptor)
    {
        if (descriptor == null)
            return "";

        var type = ResolveFakeLaunchSupportType(descriptor as GroundFacilityDescriptor);
        return NormalizeLaunchSupportCategory(ClassifyLaunchSupport(
            BuildLaunchSupportSearchText(descriptor.Name, descriptor.ID, descriptor.name),
            BuildLaunchSupportSearchText(type?.Name, type?.ID, type?.name)));
    }

    private static bool IsLaunchSupportFacilityDescriptor(FacilityBaseDescriptor descriptor, string category)
    {
        if (descriptor == null)
            return false;

        var normalized = NormalizeLaunchSupportCategory(category);
        if (string.Equals(normalized, "space-elevator", StringComparison.Ordinal)
            && descriptor is GroundFacilityDescriptor elevatorGround
            && elevatorGround.bonusData?.spaceElevatorPrefab3dView != null)
            return true;

        if (descriptor is GroundFacilityDescriptor ground)
        {
            if (ground.facilityType.HasFlag(FacilityBaseDescriptor.EFacilityType.LaunchFacility)
                || ground.bonusData?.bonus == EBonus.LaunchCostOptionInPlanMission)
                return true;

            var type = ResolveFakeLaunchSupportType(ground);
            if (type != null && LaunchSupportSearchTextMatchesCategory(
                    BuildLaunchSupportSearchText(type.Name, type.ID, type.name),
                    normalized))
                return true;
        }

        return LaunchSupportSearchTextMatchesCategory(
            BuildLaunchSupportSearchText(descriptor.Name, descriptor.ID, descriptor.name),
            normalized);
    }

    private static bool IsFacilityLaunchSupportCategory(string category)
    {
        switch (NormalizeLaunchSupportCategory(category))
        {
            case "magnetic-launch-rails":
            case "launch-pad":
            case "rotary-launcher":
            case "space-elevator":
            case "electromagnetic-catapult":
            case "stationary-mass-driver":
                return true;
            default:
                return false;
        }
    }

    private static LaunchVehicleType ResolveFakeLaunchSupportType(GroundFacilityDescriptor ground)
    {
        var allTypes = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllLaunchVehicleType;
        if (allTypes == null || string.IsNullOrWhiteSpace(ground?.bonusData?.fakeLVId))
            return null;

        return allTypes.GetByID(ground.bonusData.fakeLVId);
    }

    private static LaunchVehicleType ResolveFacilityLaunchSupportType(Facility facility, string category)
    {
        var allTypes = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllLaunchVehicleType;
        if (allTypes == null)
            return null;

        var fakeType = ResolveFakeLaunchSupportType(facility?.facilityDescriptor as GroundFacilityDescriptor);
        if (fakeType != null)
            return fakeType;

        return allTypes.ListNotEmpty?
            .Where(type => type != null)
            .Where(type => LaunchSupportSearchTextMatchesCategory(
                BuildLaunchSupportSearchText(type.Name, type.ID, type.name),
                category))
            .OrderByDescending(type => type.FakeForFacility)
            .ThenBy(type => type.Name ?? type.ID ?? "", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string BuildLaunchSupportLabel(LaunchVehicle lv, Facility facility, string category)
    {
        var lvName = lv?.launchVehicleType?.Name ?? "LV";
        if (facility != null)
        {
            var facilityName = FacilityLaunchSupportDisplayName(facility.facilityDescriptor);
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
            var facilityName = BuildLaunchSupportSearchText(
                facility.facilityDescriptor?.Name,
                facility.facilityDescriptor?.ID,
                facility.facilityDescriptor?.name);
            return ClassifyLaunchSupport(facilityName, lv?.launchVehicleType?.Name ?? "LV");
        }

        if (providerOI?.IsUseInSpaceElevator == true && providerOI.parentObjectInfo?.LowOrbitCustom != null)
            return "space-elevator";

        return "standard-launch";
    }

    private static string ClassifyLaunchSupport(string facilityName, string lvName)
    {
        var text = BuildLaunchSupportSearchText(facilityName, lvName);
        if (text.Contains("elevator"))
            return "space-elevator";
        if (text.Contains("rotary") || text.Contains("spin"))
            return "rotary-launcher";
        if (text.Contains("launch pad") || text.Contains("launchpad") || text.Contains(" pad"))
            return "launch-pad";
        if (text.Contains("electromagnetic") || text.Contains("catapult"))
            return "electromagnetic-catapult";
        if (text.Contains("mass driver"))
            return "stationary-mass-driver";
        if (text.Contains("magnetic launch rail") || text.Contains("magnetic rail") || text.Contains("launch rail")
            || text.Contains("magrail") || text.Contains(" rail"))
            return "magnetic-launch-rails";
        return "launch-pad";
    }

    private static string FacilityLaunchSupportDisplayName(FacilityBaseDescriptor facility)
    {
        if (facility == null)
            return "Facility";
        if (!string.IsNullOrWhiteSpace(facility.Name))
            return facility.Name;
        if (!string.IsNullOrWhiteSpace(facility.ID))
            return facility.ID;
        return facility.name ?? "Facility";
    }

    private static string BuildLaunchSupportSearchText(params string[] parts)
    {
        var text = string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (string.IsNullOrWhiteSpace(text))
            return "";

        text = text.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        while (text.Contains("  "))
            text = text.Replace("  ", " ");
        return text.Trim();
    }

    private static bool LaunchSupportSearchTextMatchesCategory(string text, string category)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(category))
            return false;

        switch (NormalizeLaunchSupportCategory(category))
        {
            case "magnetic-launch-rails":
                return text.Contains("magnetic launch rail")
                    || text.Contains("magnetic rail")
                    || text.Contains("launch rail")
                    || text.Contains("magrail")
                    || text.Contains(" rail");
            case "launch-pad":
                return text.Contains("launch pad")
                    || text.Contains("launchpad")
                    || text.Contains(" pad")
                    || text.Contains("launch facility")
                    || text.Contains("facility");
            case "rotary-launcher":
                return text.Contains("rotary") || text.Contains("spin");
            case "space-elevator":
                return text.Contains("elevator");
            case "electromagnetic-catapult":
                return text.Contains("electromagnetic") || text.Contains("catapult");
            case "stationary-mass-driver":
                return text.Contains("mass driver");
            default:
                return false;
        }
    }

    private static int GetLaunchSupportTierAdjustment(string category)
    {
        switch (category)
        {
            case "space-elevator":
                return -45;
            case "rotary-launcher":
                return -40;
            case "electromagnetic-catapult":
                return -39;
            case "magnetic-launch-rails":
                return -38;
            case "stationary-mass-driver":
                return -36;
            case "launch-pad":
                return -24;
            default:
                return 0;
        }
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

}
