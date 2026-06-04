using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Info;
using Game.UI.Windows.Elements.PlanMissionElements;
using Game.UI.Windows.Elements.PlanMissionElements.PMScheduleElements;
using HarmonyLib;
using LogisticsMod.Logic;

namespace LogisticsMod.Patches;

internal static class PlanMissionDiagnosticsPatches
{
    private const string LogPrefix = "VANILLA-MISSION";
    private const int MaxLoggedGridCells = 400;
    private const int MaxScannedGridCells = 400;
    private static readonly HashSet<string> LoggedMissionSnapshots = new HashSet<string>();
    private static readonly HashSet<string> LoggedMissionCompares = new HashSet<string>();

    [HarmonyPatch]
    private static class PMMissionParameterSetterPatch
    {
        private static bool Prepare()
        {
            return VanillaDiagnosticsEnabled();
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var names = new HashSet<string>(StringComparer.Ordinal)
            {
                "SetCompany",
                "SetCostType",
                "SetTabDestination",
                "SetTabSC",
                "SetTabLV",
                "SetTabCargo",
                "SetDeltaV",
                "SetTabDateFromPorkchope",
                "SetFuelNeed"
            };

            return AccessTools.GetDeclaredMethods(typeof(PMMissionParameter))
                .Where(method => names.Contains(method.Name));
        }

        private static void Prefix(PMMissionParameter __instance, MethodBase __originalMethod, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=mission-param-enter method={__originalMethod.Name} args={FormatArgs(__args)} before={DescribeMission(__instance)}");
        }

        private static void Postfix(PMMissionParameter __instance, MethodBase __originalMethod, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=mission-param-exit method={__originalMethod.Name} args={FormatArgs(__args)} after={DescribeMission(__instance)}");
        }
    }

    [HarmonyPatch(typeof(PMMissionParameter), nameof(PMMissionParameter.CheckScheduleFly))]
    private static class CheckScheduleFlyPatch
    {
        private static bool Prepare()
        {
            return VanillaDiagnosticsEnabled();
        }

        private static void Prefix(PMMissionParameter __instance, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=schedule-check-enter args={FormatArgs(__args)} {DescribeMission(__instance)}");
        }

        private static void Postfix(PMMissionParameter __instance, bool __result, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=schedule-check-exit result={__result} args={FormatArgs(__args)} checks={DescribeScheduleChecks(__instance)} {DescribeMission(__instance)}");
        }
    }

    [HarmonyPatch]
    private static class PMTabSchedulePatch
    {
        private static bool Prepare()
        {
            return VanillaDiagnosticsEnabled();
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var names = new HashSet<string>(StringComparer.Ordinal)
            {
                "ActiveTab",
                "ActiveTabNew",
                "BeforeGravityEngineCalculate",
                "GravityEngineCalculate",
                "ComputePorkchopCustom",
                "ComputePorkchopCustomCorutineFunctionpart1",
                "SetDataAfterLambert",
                "CalculateAlternativeD",
                "FunctionCalculateFuel",
                "CalculateCostInFuel",
                "PublicGridClicked2",
                "PublicGridClicked2Short",
                "ButtonFastestClickButton",
                "TransferTypeMoonCaseChange"
            };

            return AllInstanceMethods(typeof(PMTabSchedule))
                .Where(method => names.Contains(method.Name));
        }

        private static void Prefix(PMTabSchedule __instance, MethodBase __originalMethod, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=schedule-enter method={__originalMethod.Name} args={FormatArgs(__args)} {DescribeSchedule(__instance)}");
        }

        private static void Postfix(PMTabSchedule __instance, MethodBase __originalMethod, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=schedule-exit method={__originalMethod.Name} args={FormatArgs(__args)} {DescribeSchedule(__instance)}");

            if (__originalMethod.Name == "SetDataAfterLambert"
                || __originalMethod.Name == "ComputePorkchopCustomCorutineFunctionpart1"
                || __originalMethod.Name == "PublicGridClicked2"
                || __originalMethod.Name == "PublicGridClicked2Short"
                || __originalMethod.Name == "ButtonFastestClickButton")
            {
                LogLambertGrid("schedule-grid-" + __originalMethod.Name, SafeLambert(__instance));
            }
        }
    }

    [HarmonyPatch]
    private static class PMTabScheduleCreateFlyPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PMTabSchedule), "CreateFly");
        }

        private static void Prefix(PMTabSchedule __instance)
        {
            LogSafe(() =>
                $"{LogPrefix} step=createfly-enter {DescribeSchedule(__instance)}");
        }

        private static void Postfix(PMTabSchedule __instance)
        {
            LogSafe(() =>
                $"{LogPrefix} step=createfly-exit {DescribeSchedule(__instance)}");
        }
    }

    [HarmonyPatch]
    private static class PMTabScheduleDirectCommitPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var names = new HashSet<string>(StringComparer.Ordinal)
            {
                "CreateFly",
                "AddToSave"
            };

            return AllInstanceMethods(typeof(PMTabSchedule))
                .Where(method => names.Contains(method.Name));
        }

        private static void Prefix(PMTabSchedule __instance, MethodBase __originalMethod, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=schedule-commit-enter method={__originalMethod.Name} args={FormatArgs(__args)} mission={DescribeMissionInfoObject(FirstMissionInfo(__args))} {DescribeSchedule(__instance)}");
        }

        private static void Postfix(PMTabSchedule __instance, MethodBase __originalMethod, object[] __args)
        {
            var missionInfo = FirstMissionInfo(__args);
            LogSafe(() =>
                $"{LogPrefix} step=schedule-commit-exit method={__originalMethod.Name} args={FormatArgs(__args)} mission={DescribeMissionInfoObject(missionInfo)} {DescribeSchedule(__instance)}");
            LogVanillaMissionCompare(missionInfo, "schedule-" + __originalMethod.Name);
        }
    }

    [HarmonyPatch]
    private static class PMTabScheduleCodeCommitPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PMTabSchedule), "OnClickScheduleButtonForCode");
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static void Prefix(PMTabSchedule __instance, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=schedule-code-commit-enter args={FormatArgs(__args)} {DescribeSchedule(__instance)}");
        }

        private static void Postfix(PMTabSchedule __instance, object[] __args, object __result)
        {
            LogSafe(() =>
                $"{LogPrefix} step=schedule-code-commit-exit args={FormatArgs(__args)} result={DescribeMissionInfoObject(__result)} {DescribeSchedule(__instance)}");
            LogVanillaMissionCompare(__result, "schedule-code");
        }
    }

    [HarmonyPatch]
    private static class MissionInfoManagerCreateMissionInfoPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type = MissionInfoManagerType();
            if (type == null)
                return Enumerable.Empty<MethodBase>();

            return AccessTools.GetDeclaredMethods(type)
                .Where(method => method.Name == "CreateMissionInfo");
        }

        private static void Prefix(MethodBase __originalMethod, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=create-missioninfo-enter method={__originalMethod.Name} {FormatCreateMissionInfoArgs(__args)}");
        }

        private static void Postfix(MethodBase __originalMethod, object[] __args, object __result)
        {
            LogSafe(() =>
                $"{LogPrefix} step=create-missioninfo-exit method={__originalMethod.Name} {FormatCreateMissionInfoArgs(__args)} result={DescribeMissionInfoObject(__result)}");
            LogVanillaMissionCompare(__result, "create-missioninfo");
        }
    }

    [HarmonyPatch]
    private static class MissionInfoManagerAddMissionInfoPatch
    {
        private static bool Prepare()
        {
            return VanillaDiagnosticsEnabled();
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type = MissionInfoManagerType();
            if (type == null)
                return Enumerable.Empty<MethodBase>();

            return AccessTools.GetDeclaredMethods(type)
                .Where(method => method.Name == "AddMissionInfo");
        }

        private static void Prefix(object __instance, MethodBase __originalMethod, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=manager-add-enter method={__originalMethod.Name} mission={DescribeMissionInfoObject(FirstMissionInfo(__args))} manager={DescribeMissionManager(__instance)}");
        }

        private static void Postfix(object __instance, MethodBase __originalMethod, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=manager-add-exit method={__originalMethod.Name} mission={DescribeMissionInfoObject(FirstMissionInfo(__args))} manager={DescribeMissionManager(__instance)}");
            LogVanillaMissionCompare(FirstMissionInfo(__args), "manager-add");
        }
    }

    [HarmonyPatch]
    private static class MissionInfoManagerSaveLinkPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type = MissionInfoManagerType();
            if (type == null)
                return Enumerable.Empty<MethodBase>();

            return AccessTools.GetDeclaredMethods(type)
                .Where(method => method.Name == "AddPMMissionParameterDataSave"
                    || method.Name == "GetMissionInfoSaveFromMissionInfo");
        }

        private static void Prefix(object __instance, MethodBase __originalMethod, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=manager-save-link-enter method={__originalMethod.Name} args={FormatArgs(__args)} mission={DescribeMissionInfoObject(FirstMissionInfo(__args))} manager={DescribeMissionManager(__instance)}");
        }

        private static void Postfix(object __instance, MethodBase __originalMethod, object[] __args)
        {
            var missionInfo = FirstMissionInfo(__args);
            LogSafe(() =>
                $"{LogPrefix} step=manager-save-link-exit method={__originalMethod.Name} args={FormatArgs(__args)} mission={DescribeMissionInfoObject(missionInfo)} manager={DescribeMissionManager(__instance)}");
            LogVanillaMissionCompare(missionInfo, "manager-save-link");
        }
    }

    [HarmonyPatch]
    private static class MissionInfoManagerLoadStatePatch
    {
        private static bool Prepare()
        {
            return VanillaDiagnosticsEnabled();
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type = MissionInfoManagerType();
            if (type == null)
                return Enumerable.Empty<MethodBase>();

            return AccessTools.GetDeclaredMethods(type)
                .Where(method => method.Name.EndsWith("BeforeLoadState", StringComparison.Ordinal)
                    || method.Name.EndsWith("ExtractFromSaveGameData", StringComparison.Ordinal)
                    || method.Name.EndsWith("AfterLoadState", StringComparison.Ordinal));
        }

        private static void Prefix(object __instance, MethodBase __originalMethod, object[] __args)
        {
            LogMissionSnapshot("manager-load-enter-" + CleanMethodName(__originalMethod), __instance);
        }

        private static void Postfix(object __instance, MethodBase __originalMethod, object[] __args)
        {
            LogMissionSnapshot("manager-load-exit-" + CleanMethodName(__originalMethod), __instance);
        }
    }

    [HarmonyPatch]
    private static class MissionInfoManagerListAccessPatch
    {
        private static bool Prepare()
        {
            return VanillaDiagnosticsEnabled();
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type = MissionInfoManagerType();
            if (type == null)
                return Enumerable.Empty<MethodBase>();

            return AccessTools.GetDeclaredMethods(type)
                .Where(method => method.Name == "get_ListMissionInfo");
        }

        private static void Postfix(object __instance)
        {
            LogMissionSnapshot("manager-list-access", __instance);
        }
    }

    [HarmonyPatch]
    private static class MissionInfoLifecyclePatch
    {
        private static bool Prepare()
        {
            return VanillaDiagnosticsEnabled();
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var names = new HashSet<string>(StringComparer.Ordinal)
            {
                "Start",
                "UpdateDate",
                "Complete",
                "Cancel"
            };

            return AccessTools.GetDeclaredMethods(typeof(MissionInfo))
                .Where(method => names.Contains(method.Name));
        }

        private static void Prefix(MissionInfo __instance, MethodBase __originalMethod)
        {
            LogSafe(() =>
                $"{LogPrefix} step=mission-lifecycle-enter method={__originalMethod.Name} mission={DescribeMissionInfoObject(__instance)}");
        }

        private static void Postfix(MissionInfo __instance, MethodBase __originalMethod)
        {
            LogSafe(() =>
                $"{LogPrefix} step=mission-lifecycle-exit method={__originalMethod.Name} mission={DescribeMissionInfoObject(__instance)}");
            LogVanillaMissionCompare(__instance, "mission-lifecycle-" + __originalMethod.Name);
        }
    }

    [HarmonyPatch]
    private static class MissionInfoRowDisplayPatch
    {
        private static bool Prepare()
        {
            return VanillaDiagnosticsEnabled();
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var types = new[]
            {
                AccessTools.TypeByName("Game.UI.Windows.Elements.ObjectInfoElements.UIMissionsList"),
                AccessTools.TypeByName("Game.UI.Windows.Elements.ObjectInfoElements.UIRowMission"),
                AccessTools.TypeByName("MissionRowNew"),
                AccessTools.TypeByName("Game.UI.Windows.Elements.MissionsElements.MissionRow")
            };
            var names = new HashSet<string>(StringComparer.Ordinal)
            {
                "SetObjectInfoData",
                "SetData",
                "SetDataRowMissionData",
                "SetDataRowMissionDataMissionInfo",
                "SetMissionInfo",
                "UpdateText"
            };

            return types
                .Where(type => type != null)
                .SelectMany(AllInstanceMethods)
                .Where(method => names.Contains(method.Name));
        }

        private static void Postfix(object __instance, MethodBase __originalMethod, object[] __args)
        {
            var missionInfo = ExtractMissionInfoObject(__instance) ?? FirstMissionInfo(__args);
            LogVanillaMissionCompare(missionInfo, "mission-row-display");
            LogSafe(() =>
            {
                return $"{LogPrefix} step=mission-row-display method={__instance?.GetType().FullName}.{__originalMethod.Name} args={FormatArgs(__args)} mission={DescribeMissionInfoObject(missionInfo)} row={DescribeMissionRow(__instance)}";
            });
        }
    }

    [HarmonyPatch]
    private static class DeltaVPickerPatch
    {
        private static bool Prepare()
        {
            return VanillaDiagnosticsEnabled();
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var names = new HashSet<string>(StringComparer.Ordinal)
            {
                "PreInit",
                "MyInit",
                "SetPosition"
            };

            return AllInstanceMethods(typeof(DeltaVPicker))
                .Where(method => names.Contains(method.Name));
        }

        private static void Prefix(DeltaVPicker __instance, MethodBase __originalMethod, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=picker-enter method={__originalMethod.Name} args={FormatArgs(__args)} {DescribePicker(__instance)}");
        }

        private static void Postfix(DeltaVPicker __instance, MethodBase __originalMethod, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=picker-exit method={__originalMethod.Name} args={FormatArgs(__args)} {DescribePicker(__instance)}");

            if (__originalMethod.Name == "PreInit" || __originalMethod.Name == "MyInit")
                LogDataGrid("picker-grid-" + __originalMethod.Name, GetMember(__instance, "dataGrid"), null);
        }
    }

    [HarmonyPatch]
    private static class LambertPorkchopPatch
    {
        private static bool Prepare()
        {
            return VanillaDiagnosticsEnabled();
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var names = new HashSet<string>(StringComparer.Ordinal)
            {
                "ConvertReltoAbsolute",
                "ComputePorkchop",
                "ForceResult",
                "GridClickedCustom",
                "GetTimeGridClickedCustom"
            };

            return AllInstanceMethods(typeof(LambertPorkchop))
                .Where(method => names.Contains(method.Name));
        }

        private static void Prefix(LambertPorkchop __instance, MethodBase __originalMethod, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=lambert-enter method={__originalMethod.Name} args={FormatArgs(__args)} {DescribeLambert(__instance)}");
        }

        private static void Postfix(LambertPorkchop __instance, MethodBase __originalMethod, object[] __args)
        {
            LogSafe(() =>
                $"{LogPrefix} step=lambert-exit method={__originalMethod.Name} args={FormatArgs(__args)} {DescribeLambert(__instance)}");

            if (__originalMethod.Name == "ForceResult" || __originalMethod.Name == "GridClickedCustom")
                LogLambertGrid("lambert-grid-" + __originalMethod.Name, __instance);
        }
    }

    private static IEnumerable<MethodInfo> AllInstanceMethods(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        while (type != null)
        {
            foreach (var method in type.GetMethods(flags))
                yield return method;
            type = type.BaseType;
        }
    }

    internal static void LogPatchStatus()
    {
        var managerType = MissionInfoManagerType();
        var managerMethods = managerType == null
            ? Enumerable.Empty<MethodInfo>()
            : AccessTools.GetDeclaredMethods(managerType);
        var missionRowMethods = MissionRowTargetMethodsForStatus().ToList();
        var lines = new[]
        {
            PatchStatusFor("PMTabSchedule.CreateFly", AccessTools.Method(typeof(PMTabSchedule), "CreateFly")),
            PatchStatusFor("PMTabSchedule.OnClickScheduleButtonForCode",
                AccessTools.Method(typeof(PMTabSchedule), "OnClickScheduleButtonForCode")),
            PatchStatusFor("PMTabSchedule.AddToSave",
                AllInstanceMethods(typeof(PMTabSchedule)).FirstOrDefault(method => method.Name == "AddToSave")),
            PatchStatusFor("MissionInfoManager.CreateMissionInfo",
                managerMethods.FirstOrDefault(method => method.Name == "CreateMissionInfo")),
            PatchStatusFor("MissionInfoManager.AddMissionInfo",
                managerMethods.FirstOrDefault(method => method.Name == "AddMissionInfo")),
            PatchStatusFor("MissionInfoManager.get_ListMissionInfo",
                managerMethods.FirstOrDefault(method => method.Name == "get_ListMissionInfo")),
            PatchStatusFor("MissionInfo.Start", AccessTools.Method(typeof(MissionInfo), "Start")),
            PatchStatusFor("MissionInfo.UpdateDate", AccessTools.Method(typeof(MissionInfo), "UpdateDate")),
            $"MissionRowTargets=count:{missionRowMethods.Count}/patched:{missionRowMethods.Count(IsPatchedByThisMod)}"
        };

        LogisticsObserver.LogAlways($"{LogPrefix} step=patch-status {string.Join(" | ", lines)}");
    }

    private static IEnumerable<MethodBase> MissionRowTargetMethodsForStatus()
    {
        var types = new[]
        {
            AccessTools.TypeByName("Game.UI.Windows.Elements.ObjectInfoElements.UIMissionsList"),
            AccessTools.TypeByName("Game.UI.Windows.Elements.ObjectInfoElements.UIRowMission"),
            AccessTools.TypeByName("MissionRowNew"),
            AccessTools.TypeByName("Game.UI.Windows.Elements.MissionsElements.MissionRow")
        };
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            "SetObjectInfoData",
            "SetData",
            "SetDataRowMissionData",
            "SetDataRowMissionDataMissionInfo",
            "SetMissionInfo",
            "UpdateText"
        };

        return types
            .Where(type => type != null)
            .SelectMany(AllInstanceMethods)
            .Where(method => names.Contains(method.Name));
    }

    private static string PatchStatusFor(string label, MethodBase method)
    {
        if (method == null)
            return $"{label}=target-null";

        var patchInfo = Harmony.GetPatchInfo(method);
        if (patchInfo == null)
            return $"{label}=unpatched";

        return $"{label}=owners:{string.Join(",", patchInfo.Owners)}";
    }

    private static bool IsPatchedByThisMod(MethodBase method)
    {
        var patchInfo = method == null ? null : Harmony.GetPatchInfo(method);
        return patchInfo?.Owners?.Contains("com.logisticsmod") == true;
    }

    private static Type MissionInfoManagerType()
    {
        return AccessTools.TypeByName("Manager.MissionInfoManager")
            ?? AccessTools.TypeByName("MissionInfoManager");
    }

    private static object FirstMissionInfo(object[] args)
    {
        if (args == null)
            return null;

        foreach (var arg in args)
        {
            var missionInfo = ExtractMissionInfoObject(arg);
            if (missionInfo != null)
                return missionInfo;
        }

        return null;
    }

    private static object ExtractMissionInfoObject(object value)
    {
        if (value == null)
            return null;
        if (value is MissionInfo)
            return value;

        return GetMember(value, "missionInfo")
            ?? GetMember(value, "MissionInfo")
            ?? GetMember(value, "mi")
            ?? GetMember(value, "Mi")
            ?? GetMember(value, "miLast")
            ?? GetMember(value, "MiLast");
    }

    private static string DescribeMissionManager(object manager)
    {
        if (manager == null)
            return "null";

        var list = GetMember(manager, "listMissionInfo") ?? GetMember(manager, "ListMissionInfo");
        return string.Join("/", new[]
        {
            $"type={manager.GetType().FullName}",
            $"lastId={DescribeFirstMember(manager, "lastMissionID", "LastMissionID")}",
            $"list={DescribeCollection(list)}",
            $"dictionary={DescribeCollection(GetMember(manager, "dictionaryMission"))}",
            $"saveLinks={DescribeCollection(GetMember(manager, "dictionaryMI2Save"))}"
        });
    }

    private static void LogMissionSnapshot(string step, object manager)
    {
        try
        {
            var list = GetMember(manager, "listMissionInfo") ?? GetMember(manager, "ListMissionInfo");
            var snapshot = DescribeMissionListDetailed(list);
            var key = $"{step}|{snapshot}";
            if (!LoggedMissionSnapshots.Add(key))
                return;

            LogisticsObserver.LogAlways(
                $"{LogPrefix} step={step} manager={DescribeMissionManager(manager)} missions={snapshot}");
        }
        catch (Exception exception)
        {
            LogisticsObserver.LogAlways($"{LogPrefix} step=diagnostic-error reason={exception.GetType().Name}:{exception.Message}");
        }
    }

    private static string CleanMethodName(MethodBase method)
    {
        return method?.Name?.Replace('.', '-') ?? "unknown";
    }

    private static string DescribeMissionListDetailed(object value)
    {
        if (value == null)
            return "null";

        if (!(value is IEnumerable enumerable) || value is string)
            return DescribeObject(value);

        var count = 0;
        var missions = new List<string>();
        foreach (var item in enumerable)
        {
            if (count < 8)
                missions.Add(DescribeMissionInfoObject(item));
            count++;
        }

        return $"{value.GetType().Name}[count={count},items={string.Join(" || ", missions)}]";
    }

    private static string DescribeMissionRow(object row)
    {
        if (row == null)
            return "null";

        return string.Join("/", new[]
        {
            $"type={row.GetType().FullName}",
            $"title={DescribeText(GetMember(row, "titleTextMeshPro") ?? GetMember(row, "nameTextTextMeshPro"))}",
            $"description={DescribeText(GetMember(row, "descriptionTextMeshPro"))}",
            $"date={DescribeText(GetMember(row, "dateTextMeshPro") ?? GetMember(row, "arrivalTextTextMeshPro"))}",
            $"source={DescribeText(GetMember(row, "sourceText") ?? GetMember(row, "originTextTextMeshPro"))}",
            $"destination={DescribeText(GetMember(row, "destinationText") ?? GetMember(row, "destinationTextTextMeshPro"))}",
            $"action={DescribeText(GetMember(row, "actionTextTextMeshPro"))}"
        });
    }

    private static bool VanillaDiagnosticsEnabled()
    {
        return LogisticsMod.Plugin.VanillaMissionDiagnosticsEnabled?.Value == true;
    }

    private static void LogSafe(Func<string> buildMessage)
    {
        try
        {
            LogisticsObserver.LogAlways(buildMessage());
        }
        catch (Exception exception)
        {
            LogisticsObserver.LogAlways($"{LogPrefix} step=diagnostic-error reason={exception.GetType().Name}:{exception.Message}");
        }
    }

    private static string DescribeSchedule(PMTabSchedule schedule)
    {
        if (schedule == null)
            return "schedule=null";

        var pm = GetMission(schedule);
        return $"schedule={DescribeMission(pm)} lambert={DescribeLambert(SafeLambert(schedule))}";
    }

    private static PMMissionParameter GetMission(PMTabSchedule schedule)
    {
        var window = GetMember(schedule, "PlanMissionWindow") ?? GetMember(schedule, "planMissionWindow");
        return GetMember(window, "PMMissionParameter") as PMMissionParameter;
    }

    private static LambertPorkchop SafeLambert(PMTabSchedule schedule)
    {
        try
        {
            return schedule?.LambertPorkchop;
        }
        catch
        {
            return GetMember(schedule, "lambertPorkchop") as LambertPorkchop;
        }
    }

    private static string DescribeMission(PMMissionParameter pm)
    {
        if (pm == null)
            return "pm=null";

        return string.Join(" ", new[]
        {
            $"start={DescribeObjectInfo(Safe(() => pm.Start))}",
            $"target={DescribeObjectInfo(Safe(() => pm.Target))}",
            $"calcStart={DescribeObjectInfo(Safe(() => pm.ObjectInfoStartCalculation))}",
            $"calcTarget={DescribeObjectInfo(Safe(() => pm.ObjectInfoTargetCalculation))}",
            $"center={DescribeObject(GetMember(Safe(() => pm.CenterBodyLambertPorkchop), "name"))}",
            $"orbit={Safe(() => pm.OrbitCase)}",
            $"moon={Safe(() => pm.MoonCase)}",
            $"transfer={Safe(() => pm.TransferTypeMoonCase)}",
            $"gravityAssist={Safe(() => pm.GravityAssist)}",
            $"cost={Safe(() => pm.CostType)}",
            $"tryFast={Safe(() => pm.TryFastAsPossible)}",
            $"tryFixThrust={Safe(() => pm.TryFixWrongThrust)}",
            $"scCount={Safe(() => pm.SCCount)}",
            $"sc={DescribeSpacecraft(pm)}",
            $"cargo={DescribeCargo(pm)}",
            $"dV1={FormatNumber(Safe(() => pm.DV11))}",
            $"dV2={FormatNumber(Safe(() => pm.DV22))}",
            $"depart={FormatDate(Safe(() => pm.DepartureTimeDate))}",
            $"arrive={FormatDate(Safe(() => pm.Arrival))}",
            $"days={FormatNumber(Safe(() => pm.TimeSpanMissionLenght.TotalDays))}",
            $"fuelNeed={FormatNumber(Safe(() => pm.AllFuelNeed))}",
            $"minFuel={FormatNumber(Safe(() => pm.MINFuelCost))}",
            $"fuelStart={DescribeResource(Safe(() => pm.FuelNeedToStart))}"
        });
    }

    private static string DescribeScheduleChecks(PMMissionParameter pm)
    {
        if (pm == null)
            return "pm=null";

        return string.Join(",", new[]
        {
            $"removeFuel={Safe(() => pm.RemoveFuelOk)}",
            $"maxCapacity={Safe(() => pm.MAXCapacityFuelOk)}",
            $"scNoLvFuel={Safe(() => pm.ScNoLVFuelOk)}",
            $"checkLv={Safe(() => pm.CheckLvOk)}",
            $"thrust={Safe(() => pm.ThrustOk)}",
            $"lifeSupport={Safe(() => pm.LifeSupportOk)}",
            $"removeCargo={Safe(() => pm.RemoveResourceCargoOk)}",
            $"transferLambert={Safe(() => pm.TransferLambertOK)}"
        });
    }

    private static string DescribeSpacecraft(PMMissionParameter pm)
    {
        var sc = GetMember(pm, "SC");
        var type = SafeCall(sc, "GetTypeSpaceCraft");
        if (type == null)
            return "null";

        return string.Join("/", new[]
        {
            DescribeObject(GetMember(type, "Name") ?? GetMember(type, "ID")),
            $"id={DescribeObject(GetMember(type, "ID"))}",
            $"solar={DescribeObject(GetMember(type, "SolarSC"))}",
            $"notPork={DescribeObject(GetMember(type, "NotUsePorkchope"))}",
            $"constAccel={DescribeObject(GetMember(type, "ConstanceAcceleration"))}",
            $"availableDV={FormatNumber(GetMemberDouble(type, "AvailableDeltaV"))}",
            $"minMaxRel={FormatNumber(GetMemberDouble(type, "MinFlightTimeHohRel"))}/{FormatNumber(GetMemberDouble(type, "MaxFlightTimeHohRel"))}",
            $"massField={FormatNumber(GetMemberDouble(type, "Mass"))}",
            $"cargoCapField={FormatNumber(GetMemberDouble(type, "CargoCapacity"))}",
            $"fuelCapField={FormatNumber(GetMemberDouble(type, "FuelCapacity"))}"
        });
    }

    private static string DescribeCargo(PMMissionParameter pm)
    {
        var cargoAll = GetMember(pm, "CargoAll");
        return DescribeCargoAll(cargoAll);
    }

    private static string DescribeLambert(LambertPorkchop porkchop)
    {
        if (porkchop == null)
            return "null";

        return string.Join(" ", new[]
        {
            $"from={DescribeNBody(GetMember(porkchop, "fromNbody"))}",
            $"to={DescribeNBody(GetMember(porkchop, "toNBody"))}",
            $"inputMode={DescribeObject(GetMember(porkchop, "inputMode"))}",
            $"depart={FormatPhysDate(GetMemberDouble(porkchop, "departureStart"))}..{FormatPhysDate(GetMemberDouble(porkchop, "departureEnd"))}",
            $"arrival={FormatPhysDate(GetMemberDouble(porkchop, "arrivalStart"))}..{FormatPhysDate(GetMemberDouble(porkchop, "arrivalEnd"))}",
            $"steps={FormatNumber(GetMemberDouble(porkchop, "DepartureStep"))}/{FormatNumber(GetMemberDouble(porkchop, "ArrivalStep"))}",
            $"intervals={DescribeObject(GetMember(porkchop, "departureIntervals"))}x{DescribeObject(GetMember(porkchop, "arrivalIntervals"))}",
            $"minFlight={FormatNumber(GetMemberDouble(porkchop, "minFlightTime"))}",
            $"minMaxRel={FormatNumber(GetMemberDouble(porkchop, "minFlightTimeHohRel"))}/{FormatNumber(GetMemberDouble(porkchop, "maxFlightTimeHohRel"))}",
            $"mods=start:{DescribeObject(GetMember(porkchop, "modyficatTimeStart"))},period:{DescribeObject(GetMember(porkchop, "modyficatTime2"))},orbit:{DescribeObject(GetMember(porkchop, "modyficatTime3"))},zoomD0:{DescribeObject(GetMember(porkchop, "modyficatTime4"))},zoomD1:{DescribeObject(GetMember(porkchop, "modyficatTime5"))},zoomA:{DescribeObject(GetMember(porkchop, "modyficatTime6"))}",
            $"gridOk={Safe(() => porkchop.GridClickedOk)}",
            $"noTransfer={Safe(() => porkchop.NoTransfer)}",
            $"grid={DescribeDataGrid(GetMember(porkchop, "vinfTotalDataGrid"), porkchop, includeCells: false)}"
        });
    }

    private static string DescribePicker(DeltaVPicker picker)
    {
        if (picker == null)
            return "picker=null";

        return string.Join(" ", new[]
        {
            $"min={FormatNumber(GetMemberDouble(picker, "min"))}@{FormatNumber(GetMemberDouble(picker, "minI"))},{FormatNumber(GetMemberDouble(picker, "minJ"))}",
            $"fastScore={FormatNumber(GetMemberDouble(picker, "min2"))}@{FormatNumber(GetMemberDouble(picker, "minI2"))},{FormatNumber(GetMemberDouble(picker, "minJ2"))}",
            $"thrustScore={FormatNumber(GetMemberDouble(picker, "min3"))}@{FormatNumber(GetMemberDouble(picker, "minI3"))},{FormatNumber(GetMemberDouble(picker, "minJ3"))}",
            $"max={FormatNumber(GetMemberDouble(picker, "max"))}",
            $"grid={DescribeDataGrid(GetMember(picker, "dataGrid"), null, includeCells: false)}"
        });
    }

    private static void LogLambertGrid(string step, LambertPorkchop porkchop)
    {
        if (porkchop == null)
            return;
        LogDataGrid(step, GetMember(porkchop, "vinfTotalDataGrid"), porkchop);
    }

    private static void LogDataGrid(string step, object grid, LambertPorkchop porkchop)
    {
        LogSafe(() => $"{LogPrefix} step={step} {DescribeDataGrid(grid, porkchop, includeCells: true)}");
    }

    private static string DescribeDataGrid(object grid, LambertPorkchop porkchop, bool includeCells)
    {
        if (grid == null)
            return "grid=null";

        var xIntervals = (int)Math.Max(0, GetMemberDouble(grid, "xIntervals"));
        var yIntervals = (int)Math.Max(0, GetMemberDouble(grid, "yIntervals"));
        var xStart = GetMemberDouble(grid, "xStart");
        var xEnd = GetMemberDouble(grid, "xEnd");
        var yStart = GetMemberDouble(grid, "yStart");
        var yEnd = GetMemberDouble(grid, "yEnd");
        var cellCount = (xIntervals + 1) * (yIntervals + 1);
        var canScanCells = cellCount <= MaxScannedGridCells;
        var canLogCells = includeCells && cellCount <= MaxLoggedGridCells;
        var cells = new List<string>();
        var finite = 0;
        var invalid = 0;
        var min = double.MaxValue;
        var max = double.MinValue;
        var minCell = "-";
        var maxCell = "-";

        if (canScanCells)
        {
            for (var x = 0; x <= xIntervals; x++)
            {
                for (var y = 0; y <= yIntervals; y++)
                {
                    var raw = SafeCallDouble(grid, "GetData", x, y);
                    var converted = porkchop != null ? SafeCallDouble(porkchop, "ConvertVelocity", raw) : double.NaN;
                    var depart = xIntervals <= 0 ? xStart : xStart + (xEnd - xStart) * x / xIntervals;
                    var arrive = yIntervals <= 0 ? yStart : yStart + (yEnd - yStart) * y / yIntervals;
                    if (double.IsNaN(raw) || double.IsInfinity(raw) || raw == double.MaxValue)
                    {
                        invalid++;
                    }
                    else
                    {
                        finite++;
                        if (raw < min)
                        {
                            min = raw;
                            minCell = $"{x},{y}";
                        }
                        if (raw > max)
                        {
                            max = raw;
                            maxCell = $"{x},{y}";
                        }
                    }

                    if (canLogCells)
                    {
                        cells.Add(
                            $"{x},{y}:raw={FormatNumber(raw)},dv={FormatNumber(converted)},depart={FormatPhysDate(depart)},arrive={FormatPhysDate(arrive)}");
                    }
                }
            }
        }

        var scanSummary = canScanCells
            ? $"finite={finite} invalid={invalid} minRaw={FormatNumber(min)}@{minCell} maxRaw={FormatNumber(max)}@{maxCell}"
            : $"finite=not-scanned invalid=not-scanned minRaw=not-scanned maxRaw=not-scanned scanSkippedCells={cellCount}";
        var summary =
            $"grid={xIntervals + 1}x{yIntervals + 1} boundsD={FormatPhysDate(xStart)}..{FormatPhysDate(xEnd)} boundsA={FormatPhysDate(yStart)}..{FormatPhysDate(yEnd)} {scanSummary}";
        if (includeCells && !canLogCells)
            return $"{summary} cells=skipped cellCount={cellCount} maxLogged={MaxLoggedGridCells}";
        return canLogCells ? $"{summary} cells=[{string.Join("; ", cells)}]" : summary;
    }

    private static string FormatCreateMissionInfoArgs(object[] args)
    {
        if (args == null)
            return "args=null";

        if (args.Length < 16)
            return $"args={FormatArgs(args)}";

        return string.Join(" ", new[]
        {
            $"missionId={DescribeObject(args[0])}",
            $"trajectory={DescribeObject(args[1])}",
            $"lv={DescribeObject(args[2])}",
            $"sc={DescribeSpacecraftInfoObject(args[3])}",
            $"name={DescribeObject(args[4])}",
            $"cargo={DescribeCargoAll(args[5])}",
            $"allFuelNeed={FormatNumber(args[6])}",
            $"optimalFuelNeed={FormatNumber(args[7])}",
            $"creator={DescribeObject(args[8])}",
            $"costType={DescribeObject(args[9])}",
            $"dV={FormatNumber(args[10])}",
            $"transactions={DescribeCollection(args[11])}",
            $"company={DescribeObject(args[12])}",
            $"scList={DescribeCollection(args[13])}",
            $"lvList={DescribeCollection(args[14])}",
            $"loadingFromSaveAndLaunch={DescribeObject(args[15])}"
        });
    }

    private static string DescribeMissionInfoObject(object missionInfo)
    {
        if (missionInfo == null)
            return "null";

        return string.Join("/", new[]
        {
            $"type={missionInfo.GetType().Name}",
            $"id={DescribeFirstMember(missionInfo, "MissionID", "missionID", "ID", "id")}",
            $"name={DescribeFirstMember(missionInfo, "MissionName", "missionName", "Name")}",
            $"creator={DescribeFirstMember(missionInfo, "MissionCreator", "missionCreator")}",
            $"start={DescribeFirstMember(missionInfo, "start", "Start")}",
            $"target={DescribeFirstMember(missionInfo, "target", "Target")}",
            $"launch={DescribeFirstMember(missionInfo, "DateLaunch", "dateLaunch", "LaunchDate")}",
            $"arrival={DescribeFirstMember(missionInfo, "DateArrive", "dateArrive", "DateArrival", "dateArrival", "ArrivalDate")}",
            $"fuel={DescribeFirstMember(missionInfo, "costFuel", "CostFuel", "AllFuelNeed", "allFuelNeed", "FuelNeed", "fuelNeed")}",
            $"optimalFuel={DescribeFirstMember(missionInfo, "optimalCostFuel", "OptimalCostFuel", "OptimalFuelNeed", "optimalFuelNeed")}",
            $"cost={DescribeFirstMember(missionInfo, "CostType", "costType")}",
            $"dV={DescribeFirstMember(missionInfo, "DeltaV", "deltaV", "dV")}",
            $"sc={DescribeSpacecraftInfoObject(GetMember(missionInfo, "spacecraftInfo2") ?? GetMember(missionInfo, "spacecraftInfo") ?? GetMember(missionInfo, "SC") ?? GetMember(missionInfo, "sc"))}",
            $"scList={DescribeCollection(GetMember(missionInfo, "ListSpacecraftInfo2") ?? GetMember(missionInfo, "listSpacecraftInfo2"))}",
            $"lv={DescribeObject(GetMember(missionInfo, "launchVehicleInfo2"))}",
            $"lvList={DescribeCollection(GetMember(missionInfo, "ListLaunchVehicleInfo2") ?? GetMember(missionInfo, "listLaunchVehicleInfo2"))}",
            $"cargo={DescribeCargoAll(GetMember(missionInfo, "cargoAll") ?? GetMember(missionInfo, "CargoAll"))}"
        });
    }

    private static void LogVanillaMissionCompare(object missionInfo, string source)
    {
        if (missionInfo == null)
            return;

        var key = FormatVanillaMissionCompare(missionInfo, source: null);
        if (!LoggedMissionCompares.Add(key))
            return;

        LogisticsObserver.LogAlways(FormatVanillaMissionCompare(missionInfo, source));
    }

    private static string FormatVanillaMissionCompare(object missionInfo, string source)
    {
        var sc = GetMember(missionInfo, "spacecraftInfo2")
            ?? GetMember(missionInfo, "spacecraftInfo")
            ?? GetMember(missionInfo, "SC")
            ?? GetMember(missionInfo, "sc");
        var scType = SafeCall(sc, "GetTypeSpaceCraft")
            ?? GetMember(sc, "spacecraftType")
            ?? GetMember(sc, "typeSpaceCraft");
        var scList = GetMember(missionInfo, "ListSpacecraftInfo2") ?? GetMember(missionInfo, "listSpacecraftInfo2");
        var lvList = GetMember(missionInfo, "ListLaunchVehicleInfo2") ?? GetMember(missionInfo, "listLaunchVehicleInfo2");
        var cargoAll = GetMember(missionInfo, "cargoAll") ?? GetMember(missionInfo, "CargoAll");
        var shipCount = CountCollection(scList);
        if (shipCount <= 0 && sc != null)
            shipCount = 1;

        return string.Join(" ", new[]
        {
            "COMPARE-MISSION",
            "side=vanilla",
            source == null ? null : $"source={source}",
            $"id={DescribeFirstMember(missionInfo, "MissionID", "missionID", "ID", "id")}",
            $"name={DescribeFirstMember(missionInfo, "MissionName", "missionName", "Name")}",
            $"creator={DescribeFirstMember(missionInfo, "MissionCreator", "missionCreator")}",
            $"from={DescribeFirstMember(missionInfo, "start", "Start")}",
            $"to={DescribeFirstMember(missionInfo, "target", "Target")}",
            $"ship={DescribeObject(GetMember(scType, "ID") ?? GetMember(scType, "Name") ?? sc)}",
            $"ships={shipCount}",
            $"scList={DescribeCollection(scList)}",
            $"lvList={DescribeCollection(lvList)}",
            $"cargoMass={FormatNumber(GetMemberDouble(cargoAll, "CargoCurrent"))}",
            $"cargo={DescribeCargoAll(cargoAll)}",
            $"fuel={DescribeFirstMember(missionInfo, "costFuel", "CostFuel", "AllFuelNeed", "allFuelNeed", "FuelNeed", "fuelNeed")}",
            $"optimalFuel={DescribeFirstMember(missionInfo, "optimalCostFuel", "OptimalCostFuel", "OptimalFuelNeed", "optimalFuelNeed")}",
            $"dV={DescribeFirstMember(missionInfo, "DeltaV", "deltaV", "dV")}",
            $"costType={DescribeFirstMember(missionInfo, "CostType", "costType")}",
            $"launch={DescribeFirstMember(missionInfo, "DateLaunch", "dateLaunch", "LaunchDate")}",
            $"arrive={DescribeFirstMember(missionInfo, "DateArrive", "dateArrive", "DateArrival", "dateArrival", "ArrivalDate")}"
        }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static int CountCollection(object value)
    {
        if (value == null || value is string)
            return 0;

        if (value is ICollection collection)
            return collection.Count;

        if (!(value is IEnumerable enumerable))
            return 0;

        var count = 0;
        foreach (var _ in enumerable)
            count++;
        return count;
    }

    private static string DescribeFirstMember(object instance, params string[] names)
    {
        if (instance == null || names == null)
            return "null";

        foreach (var name in names)
        {
            var value = GetMember(instance, name);
            if (value != null)
                return DescribeObject(value);
        }

        return "null";
    }

    private static string DescribeCargoAll(object cargoAll)
    {
        if (cargoAll == null)
            return "null";

        var cargoFuel = GetMember(cargoAll, "cargoFuel");
        return string.Join("/", new[]
        {
            $"current={FormatNumber(GetMemberDouble(cargoAll, "CargoCurrent"))}",
            $"fuelMass={FormatNumber(GetMemberDouble(cargoFuel, "cargoMass"))}",
            $"fuelPotential={FormatNumber(GetMemberDouble(cargoFuel, "cargoMassPotencjal"))}",
            $"fuelLifeSupport={FormatNumber(GetMemberDouble(cargoFuel, "lifeSupportValue"))}",
            $"items={DescribeCollection(GetMember(cargoAll, "listCargo"))}"
        });
    }

    private static string DescribeSpacecraftInfoObject(object sc)
    {
        if (sc == null)
            return "null";

        var type = SafeCall(sc, "GetTypeSpaceCraft") ?? GetMember(sc, "spacecraftType") ?? GetMember(sc, "typeSpaceCraft");
        if (type == null)
            return DescribeObject(sc);

        return string.Join("/", new[]
        {
            DescribeObject(GetMember(type, "Name") ?? GetMember(type, "ID") ?? type),
            $"id={DescribeObject(GetMember(type, "ID"))}",
            $"solar={DescribeObject(GetMember(type, "SolarSC"))}",
            $"notPork={DescribeObject(GetMember(type, "NotUsePorkchope"))}",
            $"constAccel={DescribeObject(GetMember(type, "ConstanceAcceleration"))}",
            $"availableDV={FormatNumber(GetMemberDouble(type, "AvailableDeltaV"))}",
            $"massField={FormatNumber(GetMemberDouble(type, "Mass"))}",
            $"cargoCapField={FormatNumber(GetMemberDouble(type, "CargoCapacity"))}",
            $"fuelCapField={FormatNumber(GetMemberDouble(type, "FuelCapacity"))}"
        });
    }

    private static string DescribeCollection(object value)
    {
        if (value == null)
            return "null";

        if (value is string)
            return DescribeObject(value);

        if (value is IEnumerable enumerable)
        {
            var count = 0;
            var firstItems = new List<string>();
            foreach (var item in enumerable)
            {
                if (count < 4)
                    firstItems.Add(DescribeObject(item));
                count++;
            }

            return $"{value.GetType().Name}[count={count},first={string.Join("|", firstItems)}]";
        }

        return DescribeObject(value);
    }

    private static object GetMember(object instance, string name)
    {
        if (instance == null || string.IsNullOrWhiteSpace(name))
            return null;

        var type = instance.GetType();
        while (type != null)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(instance, null);

            var field = type.GetField(name, flags);
            if (field != null)
                return field.GetValue(instance);

            type = type.BaseType;
        }

        return null;
    }

    private static double GetMemberDouble(object instance, string name)
    {
        return ToDouble(GetMember(instance, name));
    }

    private static object SafeCall(object instance, string methodName, params object[] args)
    {
        try
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
                return null;

            var type = instance.GetType();
            while (type != null)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var method = type.GetMethods(flags)
                    .FirstOrDefault(candidate => candidate.Name == methodName
                        && candidate.GetParameters().Length == (args?.Length ?? 0));
                if (method != null)
                    return method.Invoke(instance, args);

                type = type.BaseType;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static double SafeCallDouble(object instance, string methodName, params object[] args)
    {
        return ToDouble(SafeCall(instance, methodName, args));
    }

    private static T Safe<T>(Func<T> valueFactory)
    {
        try
        {
            return valueFactory();
        }
        catch
        {
            return default;
        }
    }

    private static double ToDouble(object value)
    {
        try
        {
            if (value == null)
                return double.NaN;
            return Convert.ToDouble(value);
        }
        catch
        {
            return double.NaN;
        }
    }

    private static string FormatArgs(object[] args)
    {
        if (args == null || args.Length == 0)
            return "[]";

        return "[" + string.Join(", ", args.Select(DescribeObject)) + "]";
    }

    private static string DescribeObject(object value)
    {
        if (value == null)
            return "null";

        if (value is ObjectInfo oi)
            return DescribeObjectInfo(oi);
        if (value is DateTime dateTime)
            return FormatDate(dateTime);
        if (value is TimeSpan timeSpan)
            return FormatNumber(timeSpan.TotalDays) + "d";
        if (value is float || value is double || value is decimal)
            return FormatNumber(ToDouble(value));
        if (value is int || value is long || value is bool || value is Enum)
            return value.ToString();

        var name = GetMember(value, "Name") ?? GetMember(value, "ID") ?? GetMember(value, "ObjectName");
        if (name != null)
            return $"{value.GetType().Name}:{name}";

        return value.ToString();
    }

    private static string DescribeText(object textMesh)
    {
        if (textMesh == null)
            return "null";

        return DescribeObject(GetMember(textMesh, "text") ?? GetMember(textMesh, "Text") ?? textMesh);
    }

    private static string DescribeObjectInfo(ObjectInfo oi)
    {
        if (oi == null)
            return "null";

        return $"{Safe(() => oi.ObjectName)}#{Safe(() => oi.id)}({Safe(() => oi.objectTypes)})";
    }

    private static string DescribeNBody(object nbody)
    {
        var oi = SafeCall(nbody, "GetObjectInfo") as ObjectInfo;
        if (oi != null)
            return DescribeObjectInfo(oi);
        return DescribeObject(GetMember(nbody, "name"));
    }

    private static string DescribeResource(object resource)
    {
        if (resource == null)
            return "null";

        return DescribeObject(GetMember(resource, "ID") ?? GetMember(resource, "Name") ?? resource);
    }

    private static string FormatNumber(object value)
    {
        return FormatNumber(ToDouble(value));
    }

    private static string FormatNumber(double value)
    {
        if (double.IsNaN(value))
            return "NaN";
        if (double.IsPositiveInfinity(value))
            return "+Inf";
        if (double.IsNegativeInfinity(value))
            return "-Inf";
        if (value == double.MaxValue)
            return "Max";
        if (value == double.MinValue)
            return "Min";
        return value.ToString("0.###");
    }

    private static string FormatDate(DateTime dateTime)
    {
        if (dateTime == default)
            return "default";

        return dateTime.ToString("yyyy-MM-dd");
    }

    private static string FormatPhysDate(double physicalTime)
    {
        if (double.IsNaN(physicalTime) || double.IsInfinity(physicalTime) || physicalTime == 0)
            return FormatNumber(physicalTime);

        try
        {
            return GravityScaler.GetWorldTimeDateTime(physicalTime, GravityScaler.Units.SOLAR).ToString("yyyy-MM-dd");
        }
        catch
        {
            return FormatNumber(physicalTime);
        }
    }
}
