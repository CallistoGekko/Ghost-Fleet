using System;
using System.Collections.Generic;
using System.Linq;
using CustomUpdate;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.UI.Windows.Elements.PlanMissionElements;
using LogisticsMod.Logic;
using Manager;
using ScriptableObjectScripts;
using UnityEngine;

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

    public static List<ShipQuotaEntry> GetQuotas(ObjectInfo oi, bool isSpacecraft)
    {
        var data = GetOrCreate(oi);
        return isSpacecraft ? data.spacecraftQuota : data.launchVehicleQuota;
    }

    public static int GetQuota(ObjectInfo oi, string typeName, bool isSpacecraft)
    {
        var data = Get(oi);
        if (data == null) return 0;
        var quotas = isSpacecraft ? data.spacecraftQuota : data.launchVehicleQuota;
        var entry = quotas.Find(q => q.typeName == typeName);
        return entry?.count ?? 0;
    }

    public static ShipQuotaEntry GetQuotaEntry(ObjectInfo oi, string typeName, bool isSpacecraft)
    {
        var data = Get(oi);
        if (data == null) return null;
        var quotas = isSpacecraft ? data.spacecraftQuota : data.launchVehicleQuota;
        return quotas.Find(q => q.typeName == typeName);
    }

    public static void SetQuota(ObjectInfo oi, string typeName, int count, bool isSpacecraft)
    {
        var quotas = GetQuotas(oi, isSpacecraft);
        var entry = quotas.Find(q => q.typeName == typeName);
        if (entry != null)
            entry.count = count;
        else if (count > 0)
            quotas.Add(new ShipQuotaEntry { typeName = typeName, count = count });
    }

    public static void SetQuotaTransferPreference(ObjectInfo oi, string typeName, bool isSpacecraft, bool useFastestTransfer)
    {
        var quotas = GetQuotas(oi, isSpacecraft);
        var entry = quotas.Find(q => q.typeName == typeName);
        if (entry == null)
        {
            entry = new ShipQuotaEntry { typeName = typeName, count = 1 };
            quotas.Add(entry);
        }
        entry.useFastestTransfer = useFastestTransfer;
    }

    public static void RemoveQuota(ObjectInfo oi, string typeName, bool isSpacecraft)
    {
        var data = Get(oi);
        if (data == null) return;
        var quotas = isSpacecraft ? data.spacecraftQuota : data.launchVehicleQuota;
        quotas.RemoveAll(q => q.typeName == typeName);
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

    public static Dictionary<string, int> GetShipTypeCountsOnObject(ObjectInfo oi, bool isSpacecraft)
    {
        var result = new Dictionary<string, int>();
        if (oi == null) return result;
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (player == null) return result;

        if (isSpacecraft)
        {
            var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
            foreach (var sc in UnityEngine.Object.FindObjectsOfType<Spacecraft>())
            {
                if (sc == null || sc.spacecraftType == null) continue;
                if (sc.GetCompany() != player) continue;
                if (sc.CurrentlyOnThisObject != oi) continue;
                if (!IsSpacecraftReadyForLogistics(sc, player, cm)) continue;
                var tn = TypeKey(sc.spacecraftType.ID, sc.spacecraftType.NameRocketType ?? "SC");
                if (!result.ContainsKey(tn)) result[tn] = 0;
                result[tn]++;
            }
        }
        else
        {
            var rows = oi.GetListLaunchVehicle(player);
            if (rows == null) return result;

            foreach (var row in rows)
            {
                var lv = row?.launchVehicle;
                if (lv == null || lv.launchVehicleType == null) continue;
                if (lv.GetCompany() != player) continue;
                if (lv.objectInfo != oi) continue;
                if (!lv.IsReadyToLaunchReusable()) continue;
                var tn = TypeKey(lv.launchVehicleType.ID, lv.launchVehicleType.Name ?? "LV");
                if (!result.ContainsKey(tn)) result[tn] = 0;
                result[tn]++;
            }
        }
        return result;
    }

    public static int GetReadySpacecraftCountForQuota(ObjectInfo oi, ShipQuotaEntry quota)
    {
        if (oi == null || quota == null) return 0;
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (player == null) return 0;
        var cm = MonoBehaviourSingleton<CycleMissionManager>.Instance;
        var count = 0;

        foreach (var sc in UnityEngine.Object.FindObjectsOfType<Spacecraft>())
        {
            if (sc == null || sc.spacecraftType == null) continue;
            if (sc.GetCompany() != player) continue;
            if (sc.CurrentlyOnThisObject != oi) continue;
            if (!IsSpacecraftReadyForLogistics(sc, player, cm)) continue;
            if (!QuotaMatches(quota, sc.spacecraftType.ID, sc.spacecraftType.NameRocketType ?? "SC")) continue;
            count++;
        }

        return count;
    }

    private static bool IsSpacecraftReadyForLogistics(Spacecraft sc, Company player, CycleMissionManager cm)
    {
        if (sc == null || sc.spacecraftType == null || player == null) return false;
        if (sc.GetCompany() != player) return false;
        if (sc.CurrentPhase != Spacecraft.EPhase.None) return false;

        var directCycle = cm?.GetCycleMission(sc);
        if (directCycle != null && !directCycle.CheckComplete()) return false;

        var controllerCycle = sc.CraftCyclicalMissionController?.CycleMissionsData;
        if (controllerCycle != null && !controllerCycle.CheckComplete()) return false;

        if (cm == null) return true;
        foreach (var cmd in cm.GetAllCycleMission(player))
        {
            if (cmd == null || cmd.CheckComplete() || cmd.ListSC == null)
                continue;

            foreach (var sci in cmd.ListSC)
            {
                if (sci is not Spacecraft other)
                    continue;
                if (ReferenceEquals(sc, other))
                    return false;
                if (sc.ID >= 0 && other.ID >= 0 && sc.ID == other.ID)
                    return false;
            }
        }

        return true;
    }

    public static bool ObjectRequiresLVForLaunch(ObjectInfo oi)
    {
        return oi?.NeedVehicleToLaunch() ?? false;
    }

    public static string TypeKey(string id, string fallbackName)
    {
        return !string.IsNullOrEmpty(id) ? id : fallbackName;
    }

    public static bool QuotaMatches(ShipQuotaEntry quota, string id, string fallbackName)
    {
        if (quota == null) return false;
        var key = TypeKey(id, fallbackName);
        return SameQuotaKey(quota.typeName, key) || SameQuotaKey(quota.typeName, fallbackName);
    }

    public static int ActiveCountFor(Dictionary<string, int> active, string id, string fallbackName)
    {
        var result = 0;
        if (active == null) return 0;
        active.TryGetValue(TypeKey(id, fallbackName), out result);
        if (!string.IsNullOrEmpty(fallbackName) && active.TryGetValue(fallbackName, out var legacy))
            result += legacy;
        return result;
    }

    private static bool SameQuotaKey(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
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
