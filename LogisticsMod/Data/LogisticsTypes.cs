using System;
using System.Collections.Generic;
using Game.Info;
using ScriptableObjectScripts;
using UnityEngine;

namespace LogisticsMod.Data;

[Serializable]
public class LogisticsRequest
{
    public ResourceDefinitionIDSave resourceDef;
    public double requestedAmount;
    public double minimumAmount;
    public bool useMinimumAmount;
    public LogisticsRequestStatus status;
    public RelayStage relayStage;
    public int relaySourceObjectId = -1;
    public int relayOrbitObjectId = -1;
    public int relayFinalTargetObjectId = -1;

    [NonSerialized]
    public ResourceDefinition ResourceDefinition;

    [NonSerialized]
    public string statusNote;
}

public enum RelayStage
{
    None,
    WaitingForSourceOrbitStock,
    WaitingForFinalLeg
}

public enum LogisticsRequestStatus
{
    Pending,
    InProgress,
    Satisfied,
    Failed
}

[Serializable]
public class LogisticsProvider
{
    public ResourceDefinitionIDSave resourceDef;
    public double minimumKeep;
    public bool isActive;

    [NonSerialized]
    public ResourceDefinition ResourceDefinition;
}

[Serializable]
public class ShipQuotaEntry
{
    public string typeName;
    public int count;
    public bool useFastestTransfer;
}

[Serializable]
public class LogisticsObjectData
{
    public string objectInfoSaveId;
    public List<LogisticsRequest> requests = new List<LogisticsRequest>();
    public List<LogisticsProvider> providers = new List<LogisticsProvider>();
    public List<ShipQuotaEntry> spacecraftQuota = new List<ShipQuotaEntry>();
    public List<ShipQuotaEntry> launchVehicleQuota = new List<ShipQuotaEntry>();

    [NonSerialized]
    public object ObjectInfo;
}
