using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Info;
using LogisticsMod.Logic;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;
using Newtonsoft.Json;

namespace LogisticsMod.Data;

public static class LogisticsPersistence
{
    private static string GetPath(string saveName)
    {
        var dir = Path.Combine(Application.dataPath, "..", "BepInEx", "saves", saveName);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "LogisticsData.json");
    }

    [Serializable]
    private class SaveData
    {
        public List<SavedObject> objects = new List<SavedObject>();
    }

    [Serializable]
    private class SavedObject
    {
        public int objectId;
        public List<SavedRequest> requests = new List<SavedRequest>();
        public List<SavedProvider> providers = new List<SavedProvider>();
        public List<GhostCraftRecord> ghostCraft = new List<GhostCraftRecord>();
        public List<GhostLaunchVehicleRecord> ghostLaunchVehicles = new List<GhostLaunchVehicleRecord>();
        public List<GhostFlightRecord> ghostFlights = new List<GhostFlightRecord>();
        public List<SavedRoute> routes = new List<SavedRoute>();
    }

    [Serializable]
    private class SavedRequest
    {
        public string resourceId;
        public double amount;
        public double minimumAmount;
        public bool useMinimumAmount;
        public int status;
    }

    [Serializable]
    private class SavedProvider
    {
        public string resourceId;
        public double minKeep;
        public bool active;
    }

    [Serializable]
    private class SavedRoute
    {
        public int routeId;
        public int sourceObjectId;
        public int destinationObjectId;
        public bool active;
        public bool collapsed;
        public List<SavedRouteResource> resources = new List<SavedRouteResource>();
        public List<string> disabledFacilityLaunchCategories = new List<string>();
        public List<SavedRouteSpacecraftFlightPlan> spacecraftFlightPlans = new List<SavedRouteSpacecraftFlightPlan>();
    }

    [Serializable]
    private class SavedRouteSpacecraftFlightPlan
    {
        public string shipTypeId;
        public int flightPlanMode;
    }

    [Serializable]
    private class SavedRouteResource
    {
        public string resourceId;
        public double sourceKeep;
        public double destinationTarget;
        public bool active;
        public int priority;
    }

    public static void Save(string saveName)
    {
        try
        {
            var objManager = MonoBehaviourSingleton<ObjectInfoManager>.Instance;
            var allObjectInfos = objManager?.allObjectInfos;
            if (allObjectInfos != null)
            {
                var deadKeys = LogisticsNetwork.GetAllObjects()
                    .Where(oi => oi == null || !allObjectInfos.Contains(oi))
                    .ToList();
                foreach (var deadOi in deadKeys)
                {
                    LogisticsObserver.Log($"Save: removing stale data for object id={deadOi?.id ?? -1}");
                    LogisticsNetwork.RemoveObject(deadOi);
                }
            }

            var data = new SaveData();

            foreach (var oi in LogisticsNetwork.GetAllObjects())
            {
                var ld = LogisticsNetwork.Get(oi);
                if (ld == null) continue;

                var so = new SavedObject { objectId = oi.id };

                foreach (var r in ld.requests)
                {
                    so.requests.Add(new SavedRequest
                    {
                        resourceId = r.ResourceDefinition?.ID ?? r.resourceDef.id,
                        amount = r.requestedAmount,
                        minimumAmount = r.minimumAmount,
                        useMinimumAmount = r.useMinimumAmount,
                        status = (int)r.status
                    });
                }

                foreach (var p in ld.providers)
                {
                    so.providers.Add(new SavedProvider
                    {
                        resourceId = p.ResourceDefinition?.ID ?? p.resourceDef.id,
                        minKeep = p.minimumKeep,
                        active = p.isActive
                    });
                }

                foreach (var craft in ld.ghostCraft)
                    so.ghostCraft.Add(craft);

                foreach (var lv in ld.ghostLaunchVehicles)
                    so.ghostLaunchVehicles.Add(lv);

                foreach (var flight in ld.ghostFlights)
                    so.ghostFlights.Add(flight);

                foreach (var route in ld.routes ?? new List<LogisticsRouteRecord>())
                {
                    if (route == null)
                        continue;

                    var savedRoute = new SavedRoute
                    {
                        routeId = route.routeId,
                        sourceObjectId = route.sourceObjectId,
                        destinationObjectId = route.destinationObjectId,
                        active = route.isActive,
                        collapsed = route.uiCollapsed,
                        disabledFacilityLaunchCategories = (route.disabledFacilityLaunchCategories ?? new List<string>())
                            .Where(category => !string.IsNullOrWhiteSpace(category))
                            .Select(category => category.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()
                    };
                    foreach (var plan in route.spacecraftFlightPlans ?? new List<LogisticsRouteSpacecraftFlightPlan>())
                    {
                        if (plan == null || string.IsNullOrWhiteSpace(plan.shipTypeId))
                            continue;
                        var mode = LogisticsFlightCalculator.NormalizeFlightPlanMode(plan.flightPlanMode);
                        savedRoute.spacecraftFlightPlans.Add(new SavedRouteSpacecraftFlightPlan
                        {
                            shipTypeId = plan.shipTypeId,
                            flightPlanMode = (int)mode
                        });
                    }
                    foreach (var rule in route.resources ?? new List<LogisticsRouteResourceRule>())
                    {
                        if (rule == null)
                            continue;
                        savedRoute.resources.Add(new SavedRouteResource
                        {
                            resourceId = rule.ResourceDefinition?.ID ?? rule.resourceDef?.id,
                            sourceKeep = rule.sourceKeep,
                            destinationTarget = rule.destinationTarget,
                            active = rule.isActive,
                            priority = rule.priority
                        });
                    }
                    so.routes.Add(savedRoute);
                }

                data.objects.Add(so);
            }

            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(GetPath(saveName), json);
            LogisticsObserver.Log($"Saved to {saveName}");
        }
        catch (Exception ex)
        {
            LogisticsObserver.LogError($"Save error: {ex}");
        }
    }

    public static void Load(string saveName)
    {
        try
        {
            LogisticsNetwork.ClearAll();

            var path = GetPath(saveName);
            if (!File.Exists(path))
            {
                LogisticsObserver.Log($"No save for {saveName}");
                return;
            }

            var json = File.ReadAllText(path);
            var data = JsonConvert.DeserializeObject<SaveData>(json);
            if (data == null || data.objects == null) return;

            var allResources = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllResourceDefinitions;
            var objManager = MonoBehaviourSingleton<ObjectInfoManager>.Instance;

            foreach (var so in data.objects)
            {
                var oi = objManager?.GetByID(so.objectId);
                if (oi == null)
                {
                    LogisticsObserver.LogWarning($"Load: object id={so.objectId} not found, skipping");
                    continue;
                }

                var ld = LogisticsNetwork.GetOrCreate(oi);

                foreach (var sr in so.requests)
                {
                    var rd = allResources?.GetByID(sr.resourceId);
                    ld.requests.Add(new LogisticsRequest
                    {
                        resourceDef = (ResourceDefinitionIDSave)rd,
                        ResourceDefinition = rd,
                        requestedAmount = sr.amount,
                        minimumAmount = sr.minimumAmount,
                        useMinimumAmount = sr.useMinimumAmount,
                        status = (LogisticsRequestStatus)sr.status
                    });
                }

                foreach (var sp in so.providers)
                {
                    var rd = allResources?.GetByID(sp.resourceId);
                    ld.providers.Add(new LogisticsProvider
                    {
                        resourceDef = (ResourceDefinitionIDSave)rd,
                        ResourceDefinition = rd,
                        minimumKeep = sp.minKeep,
                        isActive = sp.active
                    });
                }

                if (so.ghostCraft != null)
                    ld.ghostCraft.AddRange(so.ghostCraft);

                if (so.ghostLaunchVehicles != null)
                    ld.ghostLaunchVehicles.AddRange(so.ghostLaunchVehicles);

                if (so.ghostFlights != null)
                    ld.ghostFlights.AddRange(so.ghostFlights);

                if (so.routes != null)
                {
                    foreach (var sr in so.routes)
                    {
                        if (sr == null)
                            continue;
                        var route = new LogisticsRouteRecord
                        {
                            routeId = sr.routeId,
                            sourceObjectId = sr.sourceObjectId > 0 ? sr.sourceObjectId : so.objectId,
                            destinationObjectId = sr.destinationObjectId,
                            isActive = sr.active,
                            uiCollapsed = sr.collapsed,
                            disabledFacilityLaunchCategories = (sr.disabledFacilityLaunchCategories ?? new List<string>())
                                .Where(category => !string.IsNullOrWhiteSpace(category))
                                .Select(category => category.Trim())
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList(),
                            spacecraftFlightPlans = (sr.spacecraftFlightPlans ?? new List<SavedRouteSpacecraftFlightPlan>())
                                .Where(plan => plan != null && !string.IsNullOrWhiteSpace(plan.shipTypeId))
                                .Select(plan => new LogisticsRouteSpacecraftFlightPlan
                                {
                                    shipTypeId = plan.shipTypeId,
                                    flightPlanMode = LogisticsFlightCalculator.NormalizeFlightPlanMode(
                                        (LogisticsFlightPlanMode)plan.flightPlanMode)
                                })
                                .ToList(),
                            resources = new List<LogisticsRouteResourceRule>()
                        };
                        foreach (var savedRule in sr.resources ?? new List<SavedRouteResource>())
                        {
                            var rd = allResources?.GetByID(savedRule.resourceId);
                            route.resources.Add(new LogisticsRouteResourceRule
                            {
                                resourceDef = (ResourceDefinitionIDSave)rd,
                                ResourceDefinition = rd,
                                sourceKeep = savedRule.sourceKeep,
                                destinationTarget = savedRule.destinationTarget,
                                isActive = savedRule.active,
                                priority = savedRule.priority
                            });
                        }
                        ld.routes.Add(route);
                    }
                }
            }

            LogisticsObserver.Log($"Loaded from {saveName}");
        }
        catch (Exception ex)
        {
            LogisticsObserver.LogError($"Load error: {ex}");
        }
    }

}
