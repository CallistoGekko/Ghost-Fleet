using System;
using System.Collections.Generic;
using System.Linq;
using CustomUpdate;
using Data.ScriptableObject;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using LogisticsMod.Logic;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;
using LaunchVehicleType = global::Data.ScriptableObject.LaunchVehicleType;

namespace LogisticsMod.Data;

public static class LogisticsNetwork
{
    private static Dictionary<int, LogisticsObjectData> _dataByObject
        = new Dictionary<int, LogisticsObjectData>();

    public static LogisticsObjectData GetOrCreate(ObjectInfo oi)
    {
        if (oi == null) return null;
        if (!_dataByObject.TryGetValue(oi.id, out var data))
        {
            data = new LogisticsObjectData { ObjectInfo = oi, objectInfoSaveId = oi.id.ToString() };
            _dataByObject[oi.id] = data;
            LogisticsObserver.Log($"NETWORK add object: id={oi.id} name=\"{oi.ObjectName}\"");
        }
        else if (data.ObjectInfo == null)
        {
            data.ObjectInfo = oi;
        }
        return data;
    }

    public static LogisticsObjectData Get(ObjectInfo oi)
    {
        if (oi == null) return null;
        _dataByObject.TryGetValue(oi.id, out var data);
        return data;
    }

    public static LogisticsRequest AddRequest(ObjectInfo oi, ResourceDefinition rd, double amount)
    {
        return AddRequest(oi, rd, amount, amount, false);
    }

    public static LogisticsRequest AddRequest(ObjectInfo oi, ResourceDefinition rd, double targetAmount,
        double minimumAmount, bool useMinimumAmount)
    {
        var data = GetOrCreate(oi);
        minimumAmount = System.Math.Max(0, System.Math.Min(minimumAmount, targetAmount));
        var req = new LogisticsRequest
        {
            resourceDef = rd,
            ResourceDefinition = rd,
            requestedAmount = targetAmount,
            minimumAmount = minimumAmount,
            useMinimumAmount = useMinimumAmount,
            status = LogisticsRequestStatus.Pending
        };
        data.requests.Add(req);
        LogisticsObserver.Log($"Added request: {rd.ID} target={targetAmount} minimum={(useMinimumAmount ? minimumAmount : targetAmount)} on {oi.ObjectName}");
        return req;
    }

    public static LogisticsProvider AddProvider(ObjectInfo oi, ResourceDefinition rd, double minimumKeep)
    {
        var data = GetOrCreate(oi);
        var prov = new LogisticsProvider
        {
            resourceDef = rd,
            ResourceDefinition = rd,
            minimumKeep = minimumKeep,
            isActive = true
        };
        data.providers.Add(prov);
        LogisticsObserver.Log($"Added provider: {rd.ID} min={minimumKeep} on {oi.ObjectName}");
        return prov;
    }

    public static void RemoveRequest(ObjectInfo oi, int index)
    {
        var data = Get(oi);
        if (data != null && index >= 0 && index < data.requests.Count)
            data.requests.RemoveAt(index);
    }

    public static void RemoveProvider(ObjectInfo oi, int index)
    {
        var data = Get(oi);
        if (data != null && index >= 0 && index < data.providers.Count)
            data.providers.RemoveAt(index);
    }

    public static int NextRouteId()
    {
        var max = 0;
        foreach (var data in _dataByObject.Values)
        {
            if (data?.routes == null) continue;
            foreach (var route in data.routes)
                if (route != null && route.routeId > max)
                    max = route.routeId;
        }
        return max + 1;
    }

    public static LogisticsRouteRecord AddRoute(ObjectInfo source, ObjectInfo destination)
    {
        if (source == null || destination == null || source == destination)
            return null;

        var data = GetOrCreate(source);
        data.routes ??= new List<LogisticsRouteRecord>();
        var existing = data.routes.FirstOrDefault(route => route != null
            && route.sourceObjectId == source.id
            && route.destinationObjectId == destination.id);
        if (existing != null)
            return existing;

        GetOrCreate(destination);
        var record = new LogisticsRouteRecord
        {
            routeId = NextRouteId(),
            sourceObjectId = source.id,
            destinationObjectId = destination.id,
            isActive = true
        };
        data.routes.Add(record);
        LogisticsObserver.Log($"ROUTE add: id={record.routeId} {source.ObjectName}->{destination.ObjectName}");
        return record;
    }

    public static void RemoveRoute(ObjectInfo source, int routeId)
    {
        var data = Get(source);
        if (data?.routes == null)
            return;

        var route = data.routes.FirstOrDefault(r => r != null && r.routeId == routeId);
        if (route == null)
            return;

        foreach (var craft in GetAllGhostCraft().Where(c => c != null && c.assignedRouteId == routeId).ToList())
        {
            if (!ReleaseGhostCraft(source, craft.ledgerId, out var reason))
            {
                craft.assignedRouteId = -1;
                LogisticsObserver.LogWarning($"ROUTE remove orphaned craft pending release: route={routeId} ship={craft.shipName ?? craft.shipTypeId} reason={reason}");
            }
        }
        foreach (var lv in GetAllGhostLaunchVehicles().Where(lv => lv != null && lv.assignedRouteId == routeId).ToList())
        {
            if (!ReleaseGhostLaunchVehicle(source, lv.ledgerId, out var reason))
            {
                lv.assignedRouteId = -1;
                LogisticsObserver.LogWarning($"ROUTE remove orphaned lv pending release: route={routeId} lv={lv.typeName ?? lv.launchVehicleTypeId} reason={reason}");
            }
        }

        data.routes.Remove(route);
        LogisticsObserver.Log($"ROUTE remove: id={routeId} source={source.ObjectName}");
    }

    public static void ReleaseOrphanedRouteAssets()
    {
        var validRouteIds = new HashSet<int>(GetAllRoutes().Select(route => route.routeId));
        var releasedCraft = 0;
        var releasedLaunchVehicles = 0;
        var pending = 0;

        foreach (var owner in GetAllObjects().ToList())
        {
            var data = Get(owner);
            if (owner == null || data == null)
                continue;

            foreach (var craft in (data.ghostCraft ?? new List<GhostCraftRecord>()).ToList())
            {
                if (craft == null || craft.status == GhostCraftStatus.Retired)
                    continue;
                if (craft.assignedRouteId > 0 && validRouteIds.Contains(craft.assignedRouteId))
                    continue;

                craft.assignedRouteId = -1;
                if (craft.status == GhostCraftStatus.IdleAtHome || craft.status == GhostCraftStatus.Blocked)
                {
                    if (ReleaseGhostCraft(owner, craft.ledgerId, out var reason))
                        releasedCraft++;
                    else
                    {
                        pending++;
                        LogisticsObserver.LogWarning($"GHOST orphan-craft release failed: owner={owner.ObjectName} ship={craft.shipName ?? craft.shipTypeId} reason={reason}");
                    }
                }
                else
                {
                    pending++;
                }
            }

            RefreshReservedLaunchVehicles(data);
            foreach (var lv in (data.ghostLaunchVehicles ?? new List<GhostLaunchVehicleRecord>()).ToList())
            {
                if (lv == null || lv.status == GhostLaunchVehicleStatus.Retired)
                    continue;
                if (lv.assignedRouteId > 0 && validRouteIds.Contains(lv.assignedRouteId))
                    continue;

                lv.assignedRouteId = -1;
                if (ReleaseGhostLaunchVehicle(owner, lv.ledgerId, out var reason))
                    releasedLaunchVehicles++;
                else
                {
                    pending++;
                    LogisticsObserver.LogWarning($"GHOST orphan-lv release failed: owner={owner.ObjectName} lv={lv.typeName ?? lv.launchVehicleTypeId} reason={reason}");
                }
            }
        }

        if (releasedCraft > 0 || releasedLaunchVehicles > 0 || pending > 0)
            LogisticsObserver.Log($"GHOST orphan cleanup: releasedCraft={releasedCraft} releasedLV={releasedLaunchVehicles} pending={pending}");
    }

    public static IEnumerable<LogisticsRouteRecord> GetAllRoutes()
    {
        return _dataByObject.Values
            .Where(data => data?.routes != null)
            .SelectMany(data => data.routes)
            .Where(route => route != null);
    }

    public static LogisticsRouteRecord FindRoute(int routeId)
    {
        if (routeId <= 0)
            return null;
        return GetAllRoutes().FirstOrDefault(route => route.routeId == routeId);
    }

    public static LogisticsFlightPlanMode GetRouteSpacecraftFlightPlanMode(LogisticsRouteRecord route, string shipTypeId)
    {
        if (route == null || string.IsNullOrWhiteSpace(shipTypeId))
            return LogisticsFlightPlanMode.Optimal;

        var record = route.spacecraftFlightPlans?.FirstOrDefault(plan =>
            plan != null && string.Equals(plan.shipTypeId, shipTypeId, StringComparison.Ordinal));
        return LogisticsFlightCalculator.NormalizeFlightPlanMode(
            record?.flightPlanMode ?? LogisticsFlightPlanMode.Optimal);
    }

    public static void SetRouteSpacecraftFlightPlanMode(LogisticsRouteRecord route, string shipTypeId, LogisticsFlightPlanMode mode)
    {
        if (route == null || string.IsNullOrWhiteSpace(shipTypeId))
            return;

        var normalized = LogisticsFlightCalculator.NormalizeFlightPlanMode(mode);
        route.spacecraftFlightPlans ??= new List<LogisticsRouteSpacecraftFlightPlan>();
        route.spacecraftFlightPlans.RemoveAll(plan =>
            plan == null || string.IsNullOrWhiteSpace(plan.shipTypeId)
            || string.Equals(plan.shipTypeId, shipTypeId, StringComparison.Ordinal));

        if (normalized != LogisticsFlightPlanMode.Optimal)
        {
            route.spacecraftFlightPlans.Add(new LogisticsRouteSpacecraftFlightPlan
            {
                shipTypeId = shipTypeId,
                flightPlanMode = normalized
            });
        }
    }

    public static LogisticsRouteResourceRule AddRouteResource(LogisticsRouteRecord route,
        ResourceDefinition rd, double sourceKeep, double destinationTarget)
    {
        if (route == null || rd == null)
            return null;

        route.resources ??= new List<LogisticsRouteResourceRule>();
        var existing = route.resources.FirstOrDefault(rule => rule != null
            && string.Equals(rule.ResourceDefinition?.ID ?? rule.resourceDef?.id, rd.ID, StringComparison.Ordinal));
        if (existing == null)
        {
            existing = new LogisticsRouteResourceRule
            {
                resourceDef = rd,
                ResourceDefinition = rd,
                isActive = true
            };
            route.resources.Add(existing);
        }

        existing.sourceKeep = Math.Max(0, sourceKeep);
        existing.destinationTarget = Math.Max(0, destinationTarget);
        existing.statusNote = null;
        return existing;
    }

    public static void RemoveRouteResource(LogisticsRouteRecord route, int index)
    {
        if (route?.resources == null || index < 0 || index >= route.resources.Count)
            return;
        route.resources.RemoveAt(index);
    }

    public static bool TryAddPendingRouteModule(LogisticsRouteRecord route, SpaceModule module, Company player,
        out string reason)
    {
        return TryAddPendingRouteModule(route, module, player, 1, out _, out reason);
    }

    public static bool TryAddPendingRouteModule(LogisticsRouteRecord route, SpaceModule module, Company player,
        int requestedCount, out int queuedCount, out string reason)
    {
        reason = null;
        queuedCount = 0;
        if (route == null || module == null || player == null)
        {
            reason = "Route module drop is unavailable";
            return false;
        }

        var source = MonoBehaviourSingleton<ObjectInfoManager>.Instance?.GetByID(route.sourceObjectId);
        if (source == null)
        {
            reason = "Route source is unavailable";
            return false;
        }

        if (module.facilityDescriptor is not SpaceModuleDescriptor descriptor || !descriptor.CanBeLoadAsCargo)
        {
            reason = "Only cargo-capable modules can be dropped onto a route";
            return false;
        }

        var available = source.GetAvailableModulesForCargo(player, null);
        var availableCount = Math.Max(0, (int)module.CountSelectFromDropDown());
        if (available == null || !available.Contains(module) || availableCount <= 0)
        {
            reason = "Module is not available at the route source";
            return false;
        }

        queuedCount = Math.Min(Math.Max(1, requestedCount), availableCount);
        module.Scrap(queuedCount, addResourceOnScrap: false);
        route.pendingModules ??= new List<GhostFlightModuleRecord>();
        for (var i = 0; i < queuedCount; i++)
        {
            route.pendingModules.Add(new GhostFlightModuleRecord
            {
                moduleId = descriptor.ID,
                displayName = string.IsNullOrWhiteSpace(descriptor.Name) ? descriptor.ID : descriptor.Name,
                mass = Math.Max(0.0, descriptor.GetMass(player)),
                crew = false,
                crewValue = 0
            });
        }
        route.statusNote = null;
        LogisticsObserver.Log($"ROUTE-MODULE queue: route={route.routeId} source={source.ObjectName} module={descriptor.ID} count={queuedCount}");
        return true;
    }

    public static bool TryAddPendingRouteModule(LogisticsRouteRecord route, string moduleId, Company player,
        int requestedCount, out int queuedCount, out string reason)
    {
        reason = null;
        queuedCount = 0;
        if (route == null || string.IsNullOrWhiteSpace(moduleId) || player == null)
        {
            reason = "Route module edit is unavailable";
            return false;
        }

        var source = MonoBehaviourSingleton<ObjectInfoManager>.Instance?.GetByID(route.sourceObjectId);
        if (source == null)
        {
            reason = "Route source is unavailable";
            return false;
        }

        var descriptor = ResolveSpaceModuleDescriptor(moduleId);
        if (descriptor == null || !descriptor.CanBeLoadAsCargo)
        {
            reason = "Only cargo-capable modules can be added to a route";
            return false;
        }

        var module = source.GetAvailableModulesForCargo(player, null)
            ?.Where(candidate => candidate?.facilityDescriptor is SpaceModuleDescriptor candidateDescriptor
                && string.Equals(candidateDescriptor.ID, descriptor.ID, StringComparison.Ordinal)
                && candidate.CountSelectFromDropDown() > 0)
            .OrderByDescending(candidate => candidate.CountSelectFromDropDown())
            .FirstOrDefault();
        if (module == null)
        {
            reason = "No more matching modules are available at the route source";
            return false;
        }

        return TryAddPendingRouteModule(route, module, player, requestedCount, out queuedCount, out reason);
    }

    public static bool TryReturnPendingRouteModule(LogisticsRouteRecord route, int index, Company player,
        out string reason)
    {
        reason = null;
        if (route?.pendingModules == null || index < 0 || index >= route.pendingModules.Count || player == null)
        {
            reason = "Pending module is unavailable";
            return false;
        }

        var module = route.pendingModules[index];
        var source = MonoBehaviourSingleton<ObjectInfoManager>.Instance?.GetByID(route.sourceObjectId);
        var sourceData = source?.GetObjectInfoData(player);
        var descriptor = ResolveSpaceModuleDescriptor(module?.moduleId);
        if (source == null || sourceData == null || descriptor == null)
        {
            reason = "Could not return module to route source";
            return false;
        }

        sourceData.AddSpaceModuleWithValidityCheck(descriptor);
        route.pendingModules.RemoveAt(index);
        route.statusNote = null;
        LogisticsObserver.Log($"ROUTE-MODULE return: route={route.routeId} source={source.ObjectName} module={descriptor.ID}");
        return true;
    }

    public static SpaceModuleDescriptor ResolveSpaceModuleDescriptor(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
            return null;
        return SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllFacility
            ?.GetByID(moduleId) as SpaceModuleDescriptor;
    }

    public static bool AssignGhostCraftToRoute(int routeId, GhostCraftRecord craft, out string reason)
    {
        reason = null;
        var route = FindRoute(routeId);
        if (route == null || craft == null)
        {
            reason = "Route or spacecraft is unavailable";
            return false;
        }

        if (craft.status != GhostCraftStatus.IdleAtHome || craft.currentObjectId != route.sourceObjectId)
        {
            reason = "Spacecraft must be idle at the route source";
            return false;
        }

        craft.assignedRouteId = routeId;
        return true;
    }

    public static bool UnassignGhostCraftFromRoute(int routeId, GhostCraftRecord craft, out string reason)
    {
        reason = null;
        if (craft == null || craft.assignedRouteId != routeId)
        {
            reason = "Spacecraft is not assigned to this route";
            return false;
        }

        if (craft.status != GhostCraftStatus.IdleAtHome)
        {
            reason = "Spacecraft must be idle before leaving a route";
            return false;
        }

        craft.assignedRouteId = -1;
        return true;
    }

    public static bool AssignGhostLaunchVehicleToRoute(int routeId, GhostLaunchVehicleRecord launchVehicle, out string reason)
    {
        reason = null;
        var route = FindRoute(routeId);
        if (route == null || launchVehicle == null)
        {
            reason = "Route or launch vehicle is unavailable";
            return false;
        }

        if (launchVehicle.status == GhostLaunchVehicleStatus.Retired || launchVehicle.currentObjectId != route.sourceObjectId)
        {
            reason = "Launch vehicle must be available at the route source";
            return false;
        }

        launchVehicle.assignedRouteId = routeId;
        return true;
    }

    public static bool UnassignGhostLaunchVehicleFromRoute(int routeId, GhostLaunchVehicleRecord launchVehicle, out string reason)
    {
        reason = null;
        if (launchVehicle == null || launchVehicle.assignedRouteId != routeId)
        {
            reason = "Launch vehicle is not assigned to this route";
            return false;
        }

        if (launchVehicle.status == GhostLaunchVehicleStatus.Retired)
        {
            reason = "Launch vehicle is no longer available";
            return false;
        }

        launchVehicle.assignedRouteId = -1;
        return true;
    }

    public static int NextGhostCraftId()
    {
        var max = 0;
        foreach (var data in _dataByObject.Values)
        {
            if (data?.ghostCraft == null) continue;
            foreach (var craft in data.ghostCraft)
                if (craft != null && craft.ledgerId > max)
                    max = craft.ledgerId;
        }
        return max + 1;
    }

    public static GhostCraftRecord FindGhostCraft(int ledgerId)
    {
        foreach (var data in _dataByObject.Values)
        {
            var found = data?.ghostCraft?.FirstOrDefault(c => c != null && c.ledgerId == ledgerId);
            if (found != null)
                return found;
        }
        return null;
    }

    public static IEnumerable<GhostCraftRecord> GetAllGhostCraft()
    {
        return _dataByObject.Values
            .Where(data => data?.ghostCraft != null)
            .SelectMany(data => data.ghostCraft)
            .Where(craft => craft != null);
    }

    public static IEnumerable<GhostFlightRecord> GetAllGhostFlights()
    {
        return _dataByObject.Values
            .Where(data => data?.ghostFlights != null)
            .SelectMany(data => data.ghostFlights)
            .Where(flight => flight != null);
    }

    public static int NextGhostLaunchVehicleId()
    {
        var max = 0;
        foreach (var data in _dataByObject.Values)
        {
            if (data?.ghostLaunchVehicles == null) continue;
            foreach (var lv in data.ghostLaunchVehicles)
                if (lv != null && lv.ledgerId > max)
                    max = lv.ledgerId;
        }
        return max + 1;
    }

    public static IEnumerable<GhostLaunchVehicleRecord> GetAllGhostLaunchVehicles()
    {
        return _dataByObject.Values
            .Where(data => data?.ghostLaunchVehicles != null)
            .SelectMany(data => data.ghostLaunchVehicles)
            .Where(lv => lv != null);
    }

    public static void RefreshReservedLaunchVehicles(LogisticsObjectData data)
    {
        if (data?.ghostLaunchVehicles == null)
            return;

        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        foreach (var lv in data.ghostLaunchVehicles)
        {
            if (lv == null || lv.status != GhostLaunchVehicleStatus.CoolingDown)
                continue;
            if (lv.availableDate <= now)
            {
                lv.status = GhostLaunchVehicleStatus.Ready;
                lv.blockedReason = null;
            }
        }
        data.ghostLaunchVehicles.RemoveAll(lv => lv == null || lv.status == GhostLaunchVehicleStatus.Retired);
    }

    public static bool IsGhostLaunchVehicleReady(GhostLaunchVehicleRecord record, DateTime now)
    {
        if (record == null || record.status == GhostLaunchVehicleStatus.Retired)
            return false;
        if (record.status == GhostLaunchVehicleStatus.CoolingDown && record.availableDate > now)
            return false;
        return true;
    }

    public static bool ReserveLaunchVehicle(ObjectInfo home, LaunchVehicle lv, out string reason, int assignedRouteId = -1)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (!IsLaunchVehicleReservableAt(home, lv, player, out reason))
            return false;

        var data = GetOrCreate(home);
        var record = new GhostLaunchVehicleRecord
        {
            ledgerId = NextGhostLaunchVehicleId(),
            originalLaunchVehicleId = lv.ID,
            typeName = lv.launchVehicleType.Name,
            launchVehicleTypeId = lv.launchVehicleType.ID,
            homeObjectId = home.id,
            currentObjectId = home.id,
            availableDate = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now,
            status = GhostLaunchVehicleStatus.Ready,
            assignedRouteId = assignedRouteId
        };
        data.ghostLaunchVehicles.Add(record);

        home.RemoveRocket(lv);
        UnityEngine.Object.Destroy(lv.gameObject);
        LogisticsObserver.Log($"GHOST reserve-lv: body={home.ObjectName} lv={record.typeName} type={record.launchVehicleTypeId} id={record.ledgerId} original={record.originalLaunchVehicleId}");
        return true;
    }

    public static bool IsLaunchVehicleReservableAt(ObjectInfo home, LaunchVehicle lv, Company player, out string reason)
    {
        reason = null;
        if (home == null || lv == null || player == null)
        {
            reason = "No launch vehicle selected";
            return false;
        }

        if (lv.company != player)
        {
            reason = "Only player launch vehicles can be reserved";
            return false;
        }

        if (lv.launchVehicleType == null)
        {
            reason = "Launch vehicle has no type";
            return false;
        }

        if (lv.objectInfo != home)
        {
            reason = "Launch vehicle is not at this logistics body";
            return false;
        }

        if (lv.launchVehicleType.FakeForFacility || home.GetObjectInfoData(player)?.GetFakeLVFromFacilityReverse(lv) != null)
        {
            reason = "Facility launch capacity is shared automatically";
            return false;
        }

        if (lv.spacecraft != null)
        {
            reason = "Launch vehicle already has a spacecraft assigned";
            return false;
        }

        if (!home.ListLaunchVehicle.Contains(lv))
        {
            reason = "Launch vehicle is not resting in this body's ready list";
            return false;
        }

        if (!lv.IsReadyToLaunchReusable())
        {
            reason = "Launch vehicle is not ready";
            return false;
        }

        return true;
    }

    public static bool ReleaseGhostLaunchVehicle(ObjectInfo owner, int ledgerId, out string reason)
    {
        reason = null;
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (owner == null || player == null)
        {
            reason = "No logistics body selected";
            return false;
        }

        var ownerData = Get(owner);
        RefreshReservedLaunchVehicles(ownerData);
        var record = ownerData?.ghostLaunchVehicles?.FirstOrDefault(lv => lv != null && lv.ledgerId == ledgerId);
        if (record == null)
        {
            reason = "Reserved launch vehicle not found";
            return false;
        }

        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.Now;
        if (!IsGhostLaunchVehicleReady(record, now))
        {
            reason = "Launch vehicle must be ready before release";
            return false;
        }

        var location = MonoBehaviourSingleton<ObjectInfoManager>.Instance?.GetByID(record.currentObjectId);
        if (location == null)
        {
            reason = "Current ledger location no longer exists";
            return false;
        }

        var lvType = ResolveLaunchVehicleType(record.launchVehicleTypeId);
        if (lvType == null)
        {
            reason = "Launch vehicle type no longer exists";
            return false;
        }

        var created = location.AddRocket(lvType, location, player);
        if (created == null)
        {
            reason = "Could not recreate launch vehicle";
            return false;
        }

        ownerData.ghostLaunchVehicles.Remove(record);
        LogisticsObserver.Log($"GHOST release-lv: body={owner.ObjectName} lv={created.launchVehicleType?.Name ?? record.typeName} at={location.ObjectName}");
        return true;
    }

    private static LaunchVehicleType ResolveLaunchVehicleType(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId))
            return null;
        return SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllLaunchVehicleType?.GetByID(typeId);
    }

    public static bool AdoptSpacecraft(ObjectInfo home, Spacecraft sc, out string reason)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (!IsSpacecraftAdoptableAt(home, sc, player, out reason))
            return false;

        var data = GetOrCreate(home);
        var fuelType = sc.spacecraftType.GetFuelType();
        var tankFuel = fuelType == null ? 0 : Math.Max(0, sc.CargoAll?.cargoFuel?.cargoMass ?? 0);
        var record = new GhostCraftRecord
        {
            ledgerId = NextGhostCraftId(),
            originalShipId = sc.ID,
            originalName = sc.GetSpacecraftName(),
            shipName = sc.GetSpacecraftName(),
            shipTypeId = sc.spacecraftType.ID,
            homeObjectId = home.id,
            currentObjectId = home.id,
            tankFuel = tankFuel,
            tankFuelCapacity = sc.spacecraftType.GetFuelCapacity(player),
            status = GhostCraftStatus.IdleAtHome
        };
        data.ghostCraft.Add(record);

        home.GetObjectInfoData(player)?.RemoveSpacecraft(sc);
        MonoBehaviourSingleton<ShipManager>.Instance?.RemoveSpacecraft(sc);
        LogisticsObserver.Log($"GHOST adopt: body={home.ObjectName} ship={record.shipName} type={record.shipTypeId} id={record.ledgerId} original={record.originalShipId} tank={record.tankFuel:0.#}/{record.tankFuelCapacity:0.#}");
        return true;
    }

    public static bool IsSpacecraftAdoptableAt(ObjectInfo home, Spacecraft sc, Company player, out string reason)
    {
        reason = null;
        if (home == null || sc == null || player == null)
        {
            reason = "No spacecraft selected";
            return false;
        }

        if (sc.GetCompany() != player)
        {
            reason = "Only player spacecraft can be adopted";
            return false;
        }

        if (sc.spacecraftType == null)
        {
            reason = "Spacecraft has no type";
            return false;
        }

        if (sc.CurrentlyOnThisObject != home)
        {
            reason = "Spacecraft is not at this logistics body";
            return false;
        }

        if (!IsSpacecraftRestingIdleAt(home, sc, player, out reason))
            return false;

        return true;
    }

    private static bool IsSpacecraftRestingIdleAt(ObjectInfo home, Spacecraft sc, Company player, out string reason)
    {
        reason = null;
        var objectData = home?.GetObjectInfoData(player);
        if (objectData?.ListSpaceCrafts == null || !objectData.ListSpaceCrafts.Contains(sc))
        {
            reason = "Spacecraft is not resting in this body's idle ship list";
            return false;
        }

        if (sc.spacecraftType.LowOrbitContainer || sc.spacecraftType.MagneticCatapult || sc.scFromFacility)
        {
            reason = "Spacecraft is not a normal idle vessel";
            return false;
        }

        if (sc.CurrentPhase != Spacecraft.EPhase.None)
        {
            reason = "Spacecraft is not idle";
            return false;
        }

        return true;
    }

    public static bool ReleaseGhostCraft(ObjectInfo owner, int ledgerId, out string reason)
    {
        reason = null;
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (owner == null || player == null)
        {
            reason = "No logistics body selected";
            return false;
        }

        var ownerData = Get(owner);
        var craft = ownerData?.ghostCraft?.FirstOrDefault(c => c != null && c.ledgerId == ledgerId);
        if (craft == null)
        {
            reason = "Ghost craft not found";
            return false;
        }

        if (craft.status != GhostCraftStatus.IdleAtHome && craft.status != GhostCraftStatus.Blocked)
        {
            reason = "Craft must be idle before release";
            return false;
        }

        var location = MonoBehaviourSingleton<ObjectInfoManager>.Instance?.GetByID(craft.currentObjectId);
        if (location == null)
        {
            reason = "Current ledger location no longer exists";
            return false;
        }

        var scType = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllSpacecraftType?.GetByID(craft.shipTypeId);
        if (scType == null)
        {
            reason = "Spacecraft type no longer exists";
            return false;
        }

        var created = MonoBehaviourSingleton<ShipManager>.Instance?.ConstructShipOnPlanet(location, scType, null, player) as Spacecraft;
        if (created == null)
        {
            reason = "Could not recreate spacecraft";
            return false;
        }

        created.spacecraftName = string.IsNullOrWhiteSpace(craft.shipName) ? craft.originalName : craft.shipName;
        var cargo = CargoAll.CreateCargoEmpty();
        if (cargo?.cargoFuel != null && scType.GetFuelType() != null)
        {
            cargo.cargoFuel.resourceTypeType = EResourceTypeType.resorces;
            cargo.cargoFuel.resourceType = scType.GetFuelType();
            cargo.cargoFuel.cargoMass = Math.Min(Math.Max(0, craft.tankFuel), scType.GetFuelCapacity(player));
            cargo.cargoFuel.cargoMassPotencjal = cargo.cargoFuel.cargoMass;
        }
        created.SetTabCargo(cargo);

        ownerData.ghostCraft.Remove(craft);
        LogisticsObserver.Log($"GHOST release: body={owner.ObjectName} ship={created.GetSpacecraftName()} type={craft.shipTypeId} at={location.ObjectName} tank={craft.tankFuel:0.#}");
        return true;
    }

    public static void ClearAll()
    {
        var count = _dataByObject.Count;
        _dataByObject.Clear();
        LogisticsObserver.Log($"DIAG ClearAll: cleared {count} entries");
    }

    public static void RemoveObject(ObjectInfo oi)
    {
        if (oi != null)
        {
            LogisticsObserver.Log($"DIAG RemoveObject: id={oi.id} name=\"{oi.ObjectName}\"");
            _dataByObject.Remove(oi.id);
        }
    }

    public static List<ObjectInfo> GetAllObjects()
    {
        var objManager = MonoBehaviourSingleton<ObjectInfoManager>.Instance;
        var result = new List<ObjectInfo>();
        foreach (var kv in _dataByObject)
        {
            var oi = kv.Value.ObjectInfo as ObjectInfo;
            if (oi == null && objManager != null)
            {
                oi = objManager.GetByID(kv.Key);
                if (oi != null)
                    kv.Value.ObjectInfo = oi;
                else
                    LogisticsObserver.LogWarning($"DIAG GetAllObjects: id={kv.Key} could NOT resolve via objManager");
            }
            if (oi != null)
                result.Add(oi);
        }
        return result;
    }

    public static HashSet<ResourceDefinition> GetAvailableResourcesOnObject(ObjectInfo oi, Company player)
    {
        var result = new HashSet<ResourceDefinition>();
        if (oi == null || player == null) return result;

        var oid = oi.GetObjectInfoData(player);
        if (oid == null) return result;

        var am = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance;
        if (am?.AllResourceDefinitions == null) return result;

        foreach (var rd in am.AllResourceDefinitions.ListNotEmpty)
        {
            if (oid.CheckResources(rd) > 0)
                result.Add(rd);
        }
        return result;
    }

    public static HashSet<ResourceDefinition> GetNetworkResourcesSet(Company player)
    {
        return GetNetworkResourcesSet(player, GetAllObjects());
    }

    public static HashSet<ResourceDefinition> GetNetworkResourcesSet(Company player, IEnumerable<ObjectInfo> objects)
    {
        var result = new HashSet<ResourceDefinition>();
        if (player == null) return result;

        foreach (var oi in objects ?? Enumerable.Empty<ObjectInfo>())
        {
            var data = Get(oi);
            if (data == null) continue;

            var oid = oi.GetObjectInfoData(player);
            if (oid == null) continue;

            foreach (var prov in data.providers)
            {
                if (!prov.isActive) continue;
                var rd = prov.ResourceDefinition;
                if (rd == null) continue;

                if (oid.CheckResources(rd) > prov.minimumKeep)
                    result.Add(rd);
            }
        }
        return result;
    }
}
