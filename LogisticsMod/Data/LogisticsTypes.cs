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

    [NonSerialized]
    public ResourceDefinition ResourceDefinition;

    [NonSerialized]
    public string statusNote;
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
public class GhostCraftRecord
{
    public int ledgerId;
    public int originalShipId;
    public string originalName;
    public string shipName;
    public string shipTypeId;
    public int homeObjectId;
    public int currentObjectId;
    public double tankFuel;
    public double tankFuelCapacity;
    public GhostCraftStatus status = GhostCraftStatus.IdleAtHome;
    public string currentFlightId;
    public int routeFromObjectId = -1;
    public int routeToObjectId = -1;
    public DateTime departureDate;
    public DateTime arrivalDate;
    public string cargoResourceId;
    public double cargoAmount;
    public int assignedRouteId = -1;
    public string blockedReason;
}

public enum GhostCraftStatus
{
    IdleAtHome,
    PlanningOutbound,
    Outbound,
    AtDestination,
    PlanningReturn,
    ReturningHome,
    Blocked,
    Retired
}

public enum LogisticsFlightPlanMode
{
    Fast = 1,
    Optimal = 2
}

[Serializable]
public class GhostLaunchVehicleRecord
{
    public int ledgerId;
    public int originalLaunchVehicleId;
    public string typeName;
    public string launchVehicleTypeId;
    public int homeObjectId;
    public int currentObjectId;
    public DateTime availableDate;
    public GhostLaunchVehicleStatus status = GhostLaunchVehicleStatus.Ready;
    public int assignedRouteId = -1;
    public string blockedReason;
}

public enum GhostLaunchVehicleStatus
{
    Ready,
    CoolingDown,
    Retired
}

[Serializable]
public class GhostFlightCargoRecord
{
    public string resourceId;
    public double cargoAmount;
    public double supplyConsumed;
}

[Serializable]
public class GhostFlightModuleRecord
{
    public string moduleId;
    public string displayName;
    public double mass;
    public bool crew;
    public int crewValue;
}

[Serializable]
public class GhostFlightRecord
{
    public string flightId;
    public int routeId = -1;
    public List<int> craftLedgerIds = new List<int>();
    public int homeObjectId;
    public int fromObjectId;
    public int toObjectId;
    public List<GhostFlightCargoRecord> cargoManifest = new List<GhostFlightCargoRecord>();
    public List<GhostFlightModuleRecord> moduleManifest = new List<GhostFlightModuleRecord>();
    public List<GhostFlightCargoRecord> launchFuelManifest = new List<GhostFlightCargoRecord>();
    public List<string> launchSupportLabels = new List<string>();
    public double outboundFuel;
    public double returnFuel;
    public double launchFuel;
    public double reservedReturnFuel;
    public string fuelResourceId;
    public bool destinationRefuel;
    public double launchPayloadMass;
    public double outboundTravelDays;
    public double returnTravelDays;
    public double outboundDeltaV;
    public double returnDeltaV;
    public double outboundAvailableDeltaV;
    public double returnAvailableDeltaV;
    public string outboundRouteKind;
    public string returnRouteKind;
    public LogisticsFlightPlanMode outboundFlightPlanMode = LogisticsFlightPlanMode.Optimal;
    public LogisticsFlightPlanMode returnFlightPlanMode = LogisticsFlightPlanMode.Optimal;
    public int dispatchCraftCount;
    public double dryMassPerCraft;
    public double cargoPayloadMass;
    public double outboundMassToFuel;
    public double returnMassToFuel;
    public double exhaustVelocity;
    public double fuelPowVariable;
    public double tankCapacity;
    public double tankFuelBeforeLaunch;
    public double originFuelTopUp;
    public double tankFuelAtDeparture;
    public double tankFuelAfterOutbound;
    public string tankFuelDeliveryResourceId;
    public double tankFuelDelivered;
    public double cargoHoldFuelDelivered;
    public double tankFuelReservedForOutbound;
    public double tankFuelReservedForReturn;
    public double tankFuelAtArrivalAfterUnload;
    public DateTime departureDate;
    public DateTime arrivalDate;
    public GhostFlightStatus status = GhostFlightStatus.Outbound;
    public bool isReturnFlight;
    public string blockedReason;
}

[Serializable]
public class LogisticsRouteResourceRule
{
    public ResourceDefinitionIDSave resourceDef;
    public double sourceKeep;
    public double destinationTarget;
    public bool isActive = true;
    public int priority;
    public string statusNote;

    [NonSerialized]
    public ResourceDefinition ResourceDefinition;
}

[Serializable]
public class LogisticsRouteSpacecraftFlightPlan
{
    public string shipTypeId;
    public LogisticsFlightPlanMode flightPlanMode = LogisticsFlightPlanMode.Optimal;
}

[Serializable]
public class LogisticsRouteRecord
{
    public int routeId;
    public int sourceObjectId;
    public int destinationObjectId;
    public bool isActive = true;
    public bool uiCollapsed;
    public string statusNote;
    public List<LogisticsRouteResourceRule> resources = new List<LogisticsRouteResourceRule>();
    public List<GhostFlightModuleRecord> pendingModules = new List<GhostFlightModuleRecord>();
    public List<string> disabledFacilityLaunchCategories = new List<string>();
    public List<LogisticsRouteSpacecraftFlightPlan> spacecraftFlightPlans = new List<LogisticsRouteSpacecraftFlightPlan>();
}

public enum GhostFlightStatus
{
    Planned,
    Outbound,
    Arrived,
    Returning,
    Complete,
    Blocked,
    Cancelled
}

[Serializable]
public class LogisticsObjectData
{
    public string objectInfoSaveId;
    public List<LogisticsRequest> requests = new List<LogisticsRequest>();
    public List<LogisticsProvider> providers = new List<LogisticsProvider>();
    public List<GhostCraftRecord> ghostCraft = new List<GhostCraftRecord>();
    public List<GhostLaunchVehicleRecord> ghostLaunchVehicles = new List<GhostLaunchVehicleRecord>();
    public List<GhostFlightRecord> ghostFlights = new List<GhostFlightRecord>();
    public List<LogisticsRouteRecord> routes = new List<LogisticsRouteRecord>();

    [NonSerialized]
    public object ObjectInfo;
}
