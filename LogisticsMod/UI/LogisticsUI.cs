using System.Collections.Generic;
using System.Linq;
using CustomUpdate;
using Extensions;
using Game;
using Game.Info;
using Game.ObjectInfoDataScripts;
using Game.UI.Windows.Elements.ObjectInfoElements;
using Game.UI.Windows.Windows;
using Language;
using LogisticsMod.Logic;
using Manager;
using ScriptableObjectScripts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogisticsMod.UI;

public class LogisticsUI : MonoBehaviour
{
    private const int SectionIndexSpacecraft = 3;
    private const int SectionIndexLaunchVehicle = 4;
    private const int SectionIndexPlannedMission = 5;

    private static readonly Color RowBgColor = new Color(0.12f, 0.12f, 0.14f, 0.96f);
    private static readonly Color RowBgMutedColor = new Color(0.1f, 0.1f, 0.12f, 0.92f);
    private static readonly Color AccentButtonColor = new Color(0.24f, 0.29f, 0.36f, 0.98f);
    private static readonly Color ConfirmButtonColor = new Color(0.23f, 0.33f, 0.25f, 0.98f);
    private static readonly Color BackButtonColor = new Color(0.24f, 0.22f, 0.24f, 0.98f);
    private static readonly Color RemoveButtonColor = new Color(0.27f, 0.2f, 0.2f, 0.98f);
    private static readonly Color CountButtonColor = new Color(0.25f, 0.28f, 0.33f, 0.98f);
    private static readonly Color CountButtonPositiveColor = new Color(0.24f, 0.31f, 0.27f, 0.98f);
    private static readonly Color ToggleOnRowColor = new Color(0.12f, 0.27f, 0.16f, 0.96f);
    private static readonly Color ToggleOffRowColor = new Color(0.16f, 0.16f, 0.2f, 0.96f);
    private static readonly Color SubtleTextColor = new Color(0.8f, 0.8f, 0.82f, 1f);

    private List<LogisticsSection> _sections = new List<LogisticsSection>();
    private ObjectInfoData _currentData;
    private ObjectInfo _currentObjectInfo;
    private ObjectInfoWindow _objectInfoWindow;
    private RectTransform _parentRt;
    private bool _built;
    private TMP_FontAsset _font;
    private RuntimeUiStyle _runtimeStyle = new RuntimeUiStyle();

    private LogisticsSection _getSection;
    private LogisticsSection _sendSection;
    private LogisticsSection _scSection;
    private LogisticsSection _lvSection;

    private sealed class RuntimeUiStyle
    {
        public TMP_FontAsset Font;
        public float RowFontSize = 13f;
        public float HeaderFontSize = 15f;
        public float HeaderHeight = 50f;
        public float RowHeight = 28f;
        public Color HeaderTextColor = new Color(0.604f, 0.604f, 0.604f, 1f);
        public Color HeaderDividerColor = new Color(0.425f, 0.425f, 0.425f, 1f);
        public Color HeaderBackgroundColor = new Color(0f, 0f, 0f, 0f);
        public Color RowBackgroundColor = RowBgColor;
        public Color RowTextColor = SubtleTextColor;
        public Color ActionButtonColor = LogisticsUI.AccentButtonColor;
        public Color ConfirmButtonColor = LogisticsUI.ConfirmButtonColor;
        public Color BackButtonColor = LogisticsUI.BackButtonColor;
        public Color RemoveButtonColor = LogisticsUI.RemoveButtonColor;
        public Color SmallButtonColor = LogisticsUI.CountButtonColor;
        public Color SmallButtonPositiveColor = LogisticsUI.CountButtonPositiveColor;
        public Color ToggleOnColor = LogisticsUI.ToggleOnRowColor;
        public Color ToggleOffColor = LogisticsUI.ToggleOffRowColor;
        public ColorBlock HeaderButtonColors;
        public bool HasHeaderButtonColors;
    }

    private void Start()
    {
        try
        {
            _font = FindFont();
            if (_font == null) { LogisticsObserver.LogError("No TMP font found!"); return; }

            _objectInfoWindow = GetComponent<ObjectInfoWindow>();
            if (_objectInfoWindow == null) { LogisticsObserver.LogError("No ObjectInfoWindow"); return; }

            var oics = _objectInfoWindow.GetComponent<ObjectInfoCollapseSections>();
            if (oics == null || oics.uiLists == null || oics.uiLists.Count == 0)
            { LogisticsObserver.LogError("No ObjectInfoCollapseSections"); return; }

            var sectionParent = oics.uiLists[0].transform;
            _parentRt = sectionParent.parent as RectTransform;
            float sectionWidth = (sectionParent as RectTransform).sizeDelta.x;
            if (sectionWidth <= 0) sectionWidth = _parentRt.rect.width;

            var styleButton = oics.expandButtons != null && oics.expandButtons.Count > SectionIndexPlannedMission ? oics.expandButtons[SectionIndexPlannedMission] : null;
            var styleIcon = oics.buttonsIcons != null && oics.buttonsIcons.Count > SectionIndexPlannedMission ? oics.buttonsIcons[SectionIndexPlannedMission] : null;
            CaptureRuntimeStyle(oics, styleButton);

            _getSection = new LogisticsSection(_parentRt, FormatSectionTitle("GET", "Import Resources"), _font, sectionWidth,
                styleButton, styleIcon, oics.spriteExpand, oics.spriteCollapse,
                _runtimeStyle.HeaderHeight, _runtimeStyle.HeaderFontSize, _runtimeStyle.HeaderBackgroundColor,
                _runtimeStyle.HeaderTextColor, _runtimeStyle.HeaderDividerColor, new Color(0f, 0f, 0f, 0f),
                _runtimeStyle.HasHeaderButtonColors ? _runtimeStyle.HeaderButtonColors : null);
            _sections.Add(_getSection);

            _sendSection = new LogisticsSection(_parentRt, FormatSectionTitle("SEND", "Export Resources"), _font, sectionWidth,
                styleButton, styleIcon, oics.spriteExpand, oics.spriteCollapse,
                _runtimeStyle.HeaderHeight, _runtimeStyle.HeaderFontSize, _runtimeStyle.HeaderBackgroundColor,
                _runtimeStyle.HeaderTextColor, _runtimeStyle.HeaderDividerColor, new Color(0f, 0f, 0f, 0f),
                _runtimeStyle.HasHeaderButtonColors ? _runtimeStyle.HeaderButtonColors : null);
            _sections.Add(_sendSection);

            _scSection = new LogisticsSection(_parentRt, FormatSectionTitle("SPACECRAFT", "Logistics Vessels"), _font, sectionWidth,
                styleButton, styleIcon, oics.spriteExpand, oics.spriteCollapse,
                _runtimeStyle.HeaderHeight, _runtimeStyle.HeaderFontSize, _runtimeStyle.HeaderBackgroundColor,
                _runtimeStyle.HeaderTextColor, _runtimeStyle.HeaderDividerColor, new Color(0f, 0f, 0f, 0f),
                _runtimeStyle.HasHeaderButtonColors ? _runtimeStyle.HeaderButtonColors : null);
            _sections.Add(_scSection);

            _lvSection = new LogisticsSection(_parentRt, FormatSectionTitle("LAUNCH VEHICLE", "Surface Shuttles"), _font, sectionWidth,
                styleButton, styleIcon, oics.spriteExpand, oics.spriteCollapse,
                _runtimeStyle.HeaderHeight, _runtimeStyle.HeaderFontSize, _runtimeStyle.HeaderBackgroundColor,
                _runtimeStyle.HeaderTextColor, _runtimeStyle.HeaderDividerColor, new Color(0f, 0f, 0f, 0f),
                _runtimeStyle.HasHeaderButtonColors ? _runtimeStyle.HeaderButtonColors : null);
            _sections.Add(_lvSection);

            _built = true;
            TrySyncFromWindow(force: true);
            RefreshAllSections();
        }
        catch (System.Exception ex) { LogisticsObserver.LogError("Start Exception: " + ex); }
    }

    private void OnEnable()
    {
        TrySyncFromWindow(force: true);
    }

    private void LateUpdate()
    {
        if (!_built || !isActiveAndEnabled) return;
        TrySyncFromWindow(force: false);
    }

    private TMP_FontAsset FindFont()
    {
        foreach (var tmp in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            if (tmp.font != null && tmp.isActiveAndEnabled)
                return tmp.font;
        return null;
    }

    private void CaptureRuntimeStyle(ObjectInfoCollapseSections oics, Button headerButton)
    {
        _runtimeStyle.Font = _font;
        if (headerButton != null)
        {
            _runtimeStyle.HeaderButtonColors = headerButton.colors;
            _runtimeStyle.HasHeaderButtonColors = true;
        }

        TryCaptureHeaderTypography(oics, SectionIndexPlannedMission, "PLANNED");
        TryCaptureLaunchListRowStyle(_objectInfoWindow?.launchVehicleList);
        LogCapturedSectionStyle(oics, SectionIndexSpacecraft, "SPACECRAFT", _objectInfoWindow?.rocketList);
        LogCapturedSectionStyle(oics, SectionIndexLaunchVehicle, "LAUNCH VEHICLES", _objectInfoWindow?.launchVehicleList);
        LogCapturedSectionStyle(oics, SectionIndexPlannedMission, "PLANNED MISSIONS", _objectInfoWindow?.missionsList);
    }

    private void TryCaptureHeaderTypography(ObjectInfoCollapseSections oics, int sectionIndex, string headerHint)
    {
        var button = oics?.expandButtons != null && sectionIndex >= 0 && sectionIndex < oics.expandButtons.Count
            ? oics.expandButtons[sectionIndex]
            : null;
        if (button == null) return;

        foreach (var tmp in button.transform.parent.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (tmp == null) continue;
            if (tmp.text == null || tmp.text.IndexOf(headerHint, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            _runtimeStyle.Font ??= tmp.font;
            _runtimeStyle.HeaderFontSize = tmp.fontSize;
            _runtimeStyle.HeaderTextColor = tmp.color;
            var rt = button.transform.parent as RectTransform;
            if (rt != null && rt.rect.height >= 20f)
                _runtimeStyle.HeaderHeight = rt.rect.height;
            break;
        }
    }

    private void TryCaptureLaunchListRowStyle(MonoBehaviour donorList)
    {
        if (donorList == null) return;

        foreach (var btn in donorList.GetComponentsInChildren<Button>(true))
        {
            if (btn == null || btn.gameObject == donorList.gameObject) continue;
            var rt = btn.transform as RectTransform;
            if (rt == null || rt.rect.height < 40f) continue;

            var bg = btn.GetComponent<Image>();
            if (bg != null)
                _runtimeStyle.RowBackgroundColor = bg.color;

            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null)
            {
                _runtimeStyle.Font ??= tmp.font;
                _runtimeStyle.RowFontSize = tmp.fontSize;
                _runtimeStyle.RowTextColor = tmp.color;
            }
            _runtimeStyle.RowHeight = rt.rect.height;

            break;
        }
    }

    private void LogCapturedSectionStyle(ObjectInfoCollapseSections oics, int sectionIndex, string sectionName, MonoBehaviour donorList)
    {
        try
        {
            var button = oics?.expandButtons != null && sectionIndex >= 0 && sectionIndex < oics.expandButtons.Count
                ? oics.expandButtons[sectionIndex]
                : null;
            var headerTmp = button?.transform.parent.GetComponentsInChildren<TextMeshProUGUI>(true)
                .FirstOrDefault(tmp => tmp != null && tmp.text != null && tmp.text.IndexOf(sectionName.Split(' ')[0], System.StringComparison.OrdinalIgnoreCase) >= 0);

            var headerRect = button?.transform as RectTransform;
            var headerImage = button?.GetComponent<Image>();

            Button rowButton = null;
            TextMeshProUGUI rowTmp = null;
            Image rowImage = null;
            RectTransform rowRect = null;

            if (donorList != null)
            {
                foreach (var btn in donorList.GetComponentsInChildren<Button>(true))
                {
                    if (btn == null || btn.gameObject == donorList.gameObject) continue;
                    var rt = btn.transform as RectTransform;
                    if (rt == null || rt.rect.height < 30f) continue;
                    rowButton = btn;
                    rowRect = rt;
                    rowImage = btn.GetComponent<Image>();
                    rowTmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
                    break;
                }
            }

        }
        catch (System.Exception ex)
        {
            LogisticsObserver.LogWarning($"UISTYLE capture failed for {sectionName}: {ex.Message}");
        }
    }

    private string FormatSectionTitle(string primary, string secondary)
    {
        var subtitleColor = Color.Lerp(_runtimeStyle.HeaderTextColor, new Color(0.45f, 0.45f, 0.48f, _runtimeStyle.HeaderTextColor.a), 0.35f);
        var subtitleHex = ColorUtility.ToHtmlStringRGBA(subtitleColor);
        return $"{primary} <size=82%><color=#{subtitleHex}>— {secondary}</color></size>";
    }

    public void RefreshData(ObjectInfoData oid)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (oid != null && player != null && oid.company != player)
        {
            _currentData = null;
            _currentObjectInfo = null;
            if (_built)
                ClearForNonPlayerCompany();
            return;
        }

        var newOi = oid?.ObjectInfo;
        var newName = newOi?.ObjectName ?? "NULL";
        var newId = newOi?.id ?? -1;
        var prevName = _currentObjectInfo?.ObjectName ?? "null";
        var prevId = _currentObjectInfo?.id ?? -1;
        LogisticsObserver.LogVerbose($"RefreshData: \"{newName}\" (id={newId}), _built={_built}, prev=\"{prevName}\" (id={prevId})");

        if (newOi != null && _currentObjectInfo != null && newId == prevId && newName != prevName)
            LogisticsObserver.LogWarning($"DIAG RefreshData: SAME id ({newId}) but DIFFERENT name! prev=\"{prevName}\" new=\"{newName}\"");

        if (newOi != null)
        {
            var dictData = Data.LogisticsNetwork.Get(newOi);
            if (dictData != null)
            {
                var storedOiName = (dictData.ObjectInfo as ObjectInfo)?.ObjectName ?? "NULL";
                if (storedOiName != newName)
                    LogisticsObserver.LogWarning($"DIAG RefreshData: dict entry id={newId} has storedOI=\"{storedOiName}\" but incoming OI name=\"{newName}\" — MISMATCH!");
                LogisticsObserver.LogVerbose($"DIAG RefreshData: dict data for id={newId}: {dictData.requests.Count}req {dictData.providers.Count}prov");
            }
            else
            {
                LogisticsObserver.LogVerbose($"DIAG RefreshData: NO dict entry for id={newId} name=\"{newName}\"");
            }
        }

        _currentData = oid;
        _currentObjectInfo = newOi;
        if (!_built) return;
        RefreshAllSections();
    }

    private void TrySyncFromWindow(bool force)
    {
        if (_objectInfoWindow == null)
            _objectInfoWindow = GetComponent<ObjectInfoWindow>();
        if (_objectInfoWindow == null) return;

        var liveData = _objectInfoWindow.ObjectInfoDataCurrent;
        var liveOi = liveData?.ObjectInfo;
        var liveId = liveOi?.id ?? -1;
        var currentId = _currentObjectInfo?.id ?? -1;
        var liveCompany = liveData?.company;
        var currentCompany = _currentData?.company;

        if (!force && liveId == currentId && liveCompany == currentCompany)
            return;

        LogisticsObserver.LogVerbose($"UI sync-from-window: force={force} live=\"{liveOi?.ObjectName ?? "NULL"}\"(id={liveId}) cached=\"{_currentObjectInfo?.ObjectName ?? "NULL"}\"(id={currentId})");
        RefreshData(liveData);
    }

    private void ClearForNonPlayerCompany()
    {
        foreach (var section in _sections)
            section.ClearContent();

        _getSection?.AddTextRow("Logistics are only available for the player company.", _font, 13f, new Color(0.55f, 0.55f, 0.6f, 1f));
        LayoutRebuilder.ForceRebuildLayoutImmediate(_parentRt);
    }

    private void RefreshAllSections()
    {
        if (_currentObjectInfo == null) return;
        BuildGetSection();
        BuildSendSection();
        BuildSCSection();
        BuildLVSection();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_parentRt);
    }

    private void RebuildSectionLayout(LogisticsSection section)
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(section.ContentArea);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_parentRt);
    }

    private void BuildGetSection()
    {
        _getSection.ClearContent();
        var data = Data.LogisticsNetwork.Get(_currentObjectInfo);
        var requestCount = data?.requests.Count ?? 0;
        LogisticsObserver.LogVerbose($"BuildGet for {_currentObjectInfo?.ObjectName}: {requestCount} requests");

        if (requestCount > 0)
        {
            for (int i = 0; i < data.requests.Count; i++)
            {
                var req = data.requests[i];
                var idx = i;
                var rd = req.ResourceDefinition;
                var displayName = ResourceLabel(rd, req.resourceDef?.id);
                var statusStr = StatusToString(req.status);
                var noteStr = !string.IsNullOrEmpty(req.statusNote) ? $" ({req.statusNote})" : "";
                var transitStr = BuildTransitInfoSuffix(req, rd);

                var row = MakeHLRow(_getSection.ContentArea, 24f, 8);
                var amountText = req.useMinimumAmount
                    ? $"target {req.requestedAmount:0.#}, min {System.Math.Min(req.minimumAmount, req.requestedAmount):0.#}"
                    : $"{req.requestedAmount:0.#}";
                var labelTmp = MakeTMP(row.transform, $"{displayName}: {amountText}  [{statusStr}]{noteStr}{transitStr}", 13, StatusColor(req.status));
                labelTmp.enableWordWrapping = true;
                labelTmp.overflowMode = TextOverflowModes.Overflow;
                labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
                var labelLe = labelTmp.gameObject.AddComponent<LayoutElement>();
                labelLe.flexibleWidth = 1f;
                labelLe.preferredWidth = 0f;
                MakeXButton(row.transform, () =>
                {
                    var capturedOi = _currentObjectInfo;
                    LogisticsObserver.Log($"X clicked on GET req idx={idx} capturedOi=\"{capturedOi?.ObjectName}\"(id={capturedOi?.id})");
                    Data.LogisticsNetwork.RemoveRequest(capturedOi, idx);
                    BuildGetSection();
                    RebuildSectionLayout(_getSection);
                });
            }
        }
        else
        {
            _getSection.AddTextRow("No import rules configured.", _font, 13f, new Color(0.5f, 0.5f, 0.5f, 1f));
        }

        AddBigButton(_getSection.ContentArea, "+ Add Import Rule", _runtimeStyle.ConfirmButtonColor, () =>
        {
            ShowResourcePicker(_getSection, true);
        });
    }

    private string BuildTransitInfoSuffix(Data.LogisticsRequest req, ResourceDefinition rd)
    {
        if (req == null || rd == null || req.status != Data.LogisticsRequestStatus.InProgress)
            return "";

        var vehicle = FindInboundLogisticsVehicle(rd);
        if (vehicle == null)
            return "";

        var vehicleName = VehicleDisplayName(vehicle);
        var mission = vehicle?.GetMissionInfo();
        if (mission == null)
            mission = FindInboundMissionInfo(rd, vehicle);
        if (mission == null)
            return string.IsNullOrEmpty(vehicleName) ? "" : Logic.LogisticsStrings.TransitOnVehicleOnly(vehicleName);

        var arrivalText = mission.DateArrive.ToString("yyyy MMM d", LEManager.GetCultureInfoForDateTrajectory());
        if (string.IsNullOrEmpty(vehicleName))
            return Logic.LogisticsStrings.TransitArrivesOnly(arrivalText);
        return Logic.LogisticsStrings.TransitOnVehicleArrives(vehicleName, arrivalText);
    }

    private Spacecraft FindInboundLogisticsVehicle(ResourceDefinition rd)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var ships = MonoBehaviourSingleton<ShipManager>.Instance?.ListAllSpaceShip
            ?? UnityEngine.Object.FindObjectsOfType<Spacecraft>().ToList();
        if (player == null || _currentObjectInfo == null || rd == null)
            return null;

        foreach (var ship in ships)
        {
            if (ship == null || ship.GetCompany() != player)
                continue;

            var cycle = ship.CycleMissionsData;
            if (cycle == null || cycle.CheckComplete())
                continue;
            if (cycle.B != _currentObjectInfo)
                continue;
            if (cycle.customNameFromPlanMission == null
                || !cycle.customNameFromPlanMission.StartsWith("[LOGI]", System.StringComparison.Ordinal))
                continue;
            if (cycle.cargoAllStart?.Tab == null || !cycle.cargoAllStart.Tab.Any(tabRd => tabRd == rd))
                continue;
            return ship;
        }

        return null;
    }

    private MissionInfo FindInboundMissionInfo(ResourceDefinition rd, Spacecraft preferredVehicle = null)
    {
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var missionManager = MonoBehaviourSingleton<MissionInfoManager>.Instance;
        if (player == null || missionManager?.ListMissionInfo == null || _currentObjectInfo == null || rd == null)
            return null;

        return missionManager.ListMissionInfo
            .Where(mi => mi != null
                && !mi.complete
                && !mi.cancel
                && mi.company == player
                && mi.target == _currentObjectInfo
                && (preferredVehicle == null || Equals(mi.spacecraftInfo2, preferredVehicle))
                && MissionCarriesResource(mi, rd))
            .OrderBy(mi => mi.DateArrive)
            .FirstOrDefault();
    }

    private static bool MissionCarriesResource(MissionInfo mission, ResourceDefinition rd)
    {
        if (mission?.cargoAll == null || rd == null)
            return false;
        return CargoListCarriesResource(mission.cargoAll.listCargo, rd)
            || CargoListCarriesResource(mission.cargoAll.listCargoToOrbit, rd);
    }

    private static bool CargoListCarriesResource(IEnumerable<Cargo> cargoList, ResourceDefinition rd)
    {
        if (cargoList == null || rd == null)
            return false;
        return cargoList.Any(c => c != null
            && c.resourceTypeType == EResourceTypeType.resorces
            && c.resourceType == rd
            && c.cargoMass > 0);
    }

    private static string VehicleDisplayName(Spacecraft spacecraft)
    {
        if (spacecraft == null)
            return null;
        return spacecraft.GetSpacecraftName();
    }

    private static string MissionVehicleName(MissionInfo mission)
    {
        if (mission?.spacecraftInfo2 is Spacecraft spacecraft)
            return spacecraft.GetSpacecraftName();
        if (mission?.spacecraftInfo2?.GetTypeSpaceCraft() != null)
            return mission.spacecraftInfo2.GetTypeSpaceCraft().NameRocketType;
        return null;
    }

    private void BuildSendSection()
    {
        _sendSection.ClearContent();
        var data = Data.LogisticsNetwork.Get(_currentObjectInfo);
        var providerCount = data?.providers.Count ?? 0;

        if (providerCount > 0)
        {
            for (int i = 0; i < data.providers.Count; i++)
            {
                var prov = data.providers[i];
                var idx = i;
                var rd = prov.ResourceDefinition;
                var displayName = ResourceLabel(rd, prov.resourceDef?.id);

                var row = MakeHLRow(_sendSection.ContentArea, 24f, 8);
                MakeTMP(row.transform, $"{displayName}: keep {prov.minimumKeep:0.#} in reserve", 13, new Color(0.7f, 0.7f, 0.7f, 1f));
                MakeXButton(row.transform, () =>
                {
                    var capturedOi = _currentObjectInfo;
                    LogisticsObserver.Log($"X clicked on SEND prov idx={idx} capturedOi=\"{capturedOi?.ObjectName}\"(id={capturedOi?.id})");
                    Data.LogisticsNetwork.RemoveProvider(capturedOi, idx);
                    BuildSendSection();
                    RebuildSectionLayout(_sendSection);
                });
            }
        }
        else
        {
            _sendSection.AddTextRow("No export rules configured.", _font, 13f, new Color(0.5f, 0.5f, 0.5f, 1f));
        }

        AddBigButton(_sendSection.ContentArea, "+ Add Export Rule", _runtimeStyle.ActionButtonColor, () =>
        {
            ShowResourcePicker(_sendSection, false);
        });
    }

    private void BuildSCSection()
    {
        BuildShipSection(_scSection, true);
    }

    private void BuildLVSection()
    {
        BuildShipSection(_lvSection, false);
    }

    private void BuildShipSection(LogisticsSection section, bool isSpacecraft)
    {
        section.ClearContent();
        if (_currentObjectInfo == null) return;

        var typeName = isSpacecraft ? "spacecraft" : "launch vehicles";

        // LV section - no quotas, just show available types
        if (!isSpacecraft)
        {
            BuildLVSectionOnly(section);
            return;
        }

        var data = Data.LogisticsNetwork.Get(_currentObjectInfo);
        var quotas = data?.spacecraftQuota ?? new List<Data.ShipQuotaEntry>();

        if (quotas.Count > 0)
        {
            foreach (var q in quotas)
            {
                var quotaTypeName = q.typeName;
                var displayName = ShipDisplayName(quotaTypeName, true);
                var quotaCount = q.count;
                var readyHere = Data.LogisticsNetwork.GetReadySpacecraftCountForQuota(_currentObjectInfo, q);

                var row = MakeHLRow(section.ContentArea, 28f, 4);
                row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;

                var countColor = readyHere > 0
                    ? new Color(0.5f, 0.9f, 0.5f, 1f)
                    : new Color(0.9f, 0.55f, 0.1f, 1f);
                var countLabel = MakeTMP(row.transform, $"{readyHere}/{quotaCount}", 14, countColor);
                countLabel.alignment = TextAlignmentOptions.Center;
                countLabel.rectTransform.sizeDelta = new Vector2(72, 0);

                MakeTMP(row.transform, displayName, _runtimeStyle.RowFontSize, _runtimeStyle.RowTextColor);

                var transferLabel = q.useFastestTransfer ? "[x] Fast" : "[ ] Fast";
                var transferColor = q.useFastestTransfer
                    ? new Color(0.35f, 0.58f, 0.82f, 1f)
                    : new Color(0.33f, 0.43f, 0.34f, 1f);
                AddSmallButton(row.transform, transferLabel, transferColor, () =>
                {
                    var capturedOi = _currentObjectInfo;
                    Data.LogisticsNetwork.SetQuotaTransferPreference(capturedOi, quotaTypeName, true, !q.useFastestTransfer);
                    BuildShipSection(section, isSpacecraft);
                    RebuildSectionLayout(section);
                });

                AddSmallButton(row.transform, "-", _runtimeStyle.SmallButtonColor, () =>
                {
                    var capturedOi = _currentObjectInfo;
                    var newVal = quotaCount - 1;
                    if (newVal <= 0)
                        Data.LogisticsNetwork.RemoveQuota(capturedOi, quotaTypeName, isSpacecraft);
                    else
                        Data.LogisticsNetwork.SetQuota(capturedOi, quotaTypeName, newVal, isSpacecraft);
                    BuildShipSection(section, isSpacecraft);
                    RebuildSectionLayout(section);
                });

                AddSmallButton(row.transform, "+", _runtimeStyle.SmallButtonPositiveColor, () =>
                {
                    var capturedOi = _currentObjectInfo;
                    Data.LogisticsNetwork.SetQuota(capturedOi, quotaTypeName, quotaCount + 1, isSpacecraft);
                    BuildShipSection(section, isSpacecraft);
                    RebuildSectionLayout(section);
                });
            }
        }
        else
        {
            section.AddTextRow($"No logistics {typeName} quotas set.", _font, 13f, new Color(0.5f, 0.5f, 0.5f, 1f));
        }

        AddBigButton(section.ContentArea, $"+ Add {typeName} quota", _runtimeStyle.ActionButtonColor, () =>
        {
            ShowShipPicker(section, true);
        });
    }

    private void BuildLVSectionOnly(LogisticsSection section)
    {
        section.ClearContent();
        if (_currentObjectInfo == null) return;

        var typeCounts = Data.LogisticsNetwork.GetShipTypeCountsOnObject(_currentObjectInfo, false);

        if (typeCounts.Count == 0)
        {
            section.AddTextRow("No launch vehicles on this object.", _font, 13f, new Color(0.5f, 0.5f, 0.5f, 1f));
            RebuildSectionLayout(section);
            return;
        }

        section.AddTextRow("Click to toggle:", _font, 12f, new Color(0.5f, 0.5f, 0.58f, 1f));

        foreach (var kv in typeCounts)
        {
            var lvTypeName = kv.Key;
            var count = kv.Value;

            // Check if this LV type is "enabled" (has quota > 0)
            var currentQuota = Data.LogisticsNetwork.GetQuota(_currentObjectInfo, lvTypeName, false);
            var isEnabled = currentQuota > 0;

            var row = MakeHLRow(section.ContentArea, 26f, 4);
            row.GetComponent<Image>().color = isEnabled ? _runtimeStyle.ToggleOnColor : _runtimeStyle.ToggleOffColor;

            var activeColor = isEnabled ? new Color(0.54f, 0.9f, 0.62f, 1f) : new Color(0.66f, 0.66f, 0.7f, 1f);
            MakeTMP(row.transform, $"{ShipDisplayName(lvTypeName, false)}  x{count}", 13, activeColor);

            var statusText = isEnabled ? "ON" : "OFF";
            var statusColor = isEnabled ? new Color(0.58f, 0.9f, 0.58f, 1f) : new Color(0.48f, 0.48f, 0.52f, 1f);
            MakeTMP(row.transform, statusText, 11, statusColor);

            // Click to toggle
            var btn = row.AddComponent<Button>();
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            var capturedOi = _currentObjectInfo;
            var capturedType = lvTypeName;
            btn.onClick.AddListener(() =>
            {
                if (currentQuota > 0)
                    Data.LogisticsNetwork.RemoveQuota(capturedOi, capturedType, false);
                else
                    Data.LogisticsNetwork.SetQuota(capturedOi, capturedType, 1, false);
                BuildShipSection(section, false);
                RebuildSectionLayout(section);
            });
        }

        RebuildSectionLayout(section);
    }

    private void ShowShipPicker(LogisticsSection section, bool isSpacecraft)
    {
        section.ClearContent();

        AddBigButton(section.ContentArea, "\u2190 Back", _runtimeStyle.BackButtonColor, () =>
        {
            if (isSpacecraft) BuildSCSection(); else BuildLVSection();
            RebuildSectionLayout(section);
        });

        if (_currentObjectInfo == null)
        {
            section.AddTextRow("No object selected.", _font);
            RebuildSectionLayout(section);
            return;
        }

        var typeName = isSpacecraft ? "spacecraft" : "launch vehicles";
        var typeCounts = Data.LogisticsNetwork.GetShipTypeCountsOnObject(_currentObjectInfo, isSpacecraft);

        if (typeCounts.Count == 0)
        {
            section.AddTextRow($"No {typeName} found on this object.", _font);
            RebuildSectionLayout(section);
            return;
        }

        foreach (var kv in typeCounts)
        {
            var shipTypeName = kv.Key;
            var totalCount = kv.Value;
            var currentQuota = Data.LogisticsNetwork.GetQuota(_currentObjectInfo, shipTypeName, isSpacecraft);
            var quotaEntry = Data.LogisticsNetwork.GetQuotaEntry(_currentObjectInfo, shipTypeName, isSpacecraft);
            var displayQuota = currentQuota > 0 ? $"quota: {totalCount}/{currentQuota}" : "no quota";
            var transferText = !isSpacecraft || quotaEntry == null
                ? ""
                : $" (route: {(quotaEntry.useFastestTransfer ? "Fastest" : "Optimal")})";

            var row = MakeHLRow(section.ContentArea, 26f, 4);
            row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;

            MakeTMP(row.transform, $"{ShipDisplayName(shipTypeName, isSpacecraft)}  {totalCount} available ({displayQuota}){transferText}", _runtimeStyle.RowFontSize, _runtimeStyle.RowTextColor);

            if (isSpacecraft && currentQuota > 0)
            {
                var useFastest = quotaEntry?.useFastestTransfer ?? false;
                AddSmallButton(row.transform, useFastest ? "[x] Fast" : "[ ] Fast",
                    useFastest ? new Color(0.35f, 0.58f, 0.82f, 1f) : new Color(0.33f, 0.43f, 0.34f, 1f), () =>
                    {
                        var capturedOi = _currentObjectInfo;
                        Data.LogisticsNetwork.SetQuotaTransferPreference(capturedOi, shipTypeName, true, !useFastest);
                        ShowShipPicker(section, isSpacecraft);
                        RebuildSectionLayout(section);
                    });
            }

            AddSmallButton(row.transform, "+", _runtimeStyle.SmallButtonPositiveColor, () =>
            {
                var capturedOi = _currentObjectInfo;
                Data.LogisticsNetwork.SetQuota(capturedOi, shipTypeName, currentQuota + 1, isSpacecraft);
                if (isSpacecraft) BuildSCSection(); else BuildLVSection();
                RebuildSectionLayout(section);
            });
        }

        RebuildSectionLayout(section);
    }

    private void ShowResourcePicker(LogisticsSection section, bool isGet)
    {
        section.ClearContent();

        AddBigButton(section.ContentArea, "\u2190 Back", _runtimeStyle.BackButtonColor, () =>
        {
            if (isGet) BuildGetSection(); else BuildSendSection();
            RebuildSectionLayout(section);
        });

        var am = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance;
        if (am == null || am.AllResourceDefinitions == null)
        {
            section.AddTextRow("Resource list not available.", _font);
            RebuildSectionLayout(section);
            return;
        }

        var gm = MonoBehaviourSingleton<GameManager>.Instance;
        var player = gm?.Player;
        HashSet<ResourceDefinition> available;
        if (player != null && _currentObjectInfo != null)
        {
            if (isGet)
                available = Data.LogisticsNetwork.GetNetworkResourcesSet(player);
            else
                available = Data.LogisticsNetwork.GetAvailableResourcesOnObject(_currentObjectInfo, player);
        }
        else
        {
            available = new HashSet<ResourceDefinition>();
        }

        foreach (var rd in am.AllResourceDefinitions.ListNotEmpty)
        {
            var rdCaptured = rd;
            var sectionRef = section;
            var isGetCaptured = isGet;
            var isAvailable = available.Contains(rd);

            var row = MakeHLRow(section.ContentArea, 24f, 0);
            row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
            var color = isAvailable ? new Color(0.8f, 0.8f, 0.8f, 1f) : new Color(0.35f, 0.35f, 0.35f, 1f);
            var label = isAvailable ? ResourceLabel(rd) : $"{ResourceLabel(rd)} (not available)";
            MakeTMP(row.transform, label, 13, color);

            var btn = row.AddComponent<Button>();
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            btn.onClick.AddListener(() =>
            {
                ShowAmountInput(sectionRef, rdCaptured, isGetCaptured, isAvailable);
            });
        }

        RebuildSectionLayout(section);
    }

    private bool _inputConfirmed;

    private void ShowAmountInput(LogisticsSection section, ResourceDefinition rd, bool isGet, bool isAvailable = true)
    {
        var capturedOi = _currentObjectInfo;
        LogisticsObserver.Log($"ShowAmountInput: rd={rd.ID} isGet={isGet} capturedOi=\"{capturedOi?.ObjectName}\"(id={capturedOi?.id})");
        _inputConfirmed = false;
        double currentAmount = 0;
        double targetAmount = 0;
        double minimumAmount = 0;
        bool useMinimum = false;
        bool editingMinimum = false;
        section.ClearContent();

        AddBigButton(section.ContentArea, "\u2190 Back to resources", _runtimeStyle.BackButtonColor, () =>
        {
            ShowResourcePicker(section, isGet);
        });

        if (!isAvailable)
        {
            var warnTmp = MakeTMP(section.ContentArea, "WARNING: Resource not currently available", 12, new Color(0.9f, 0.6f, 0.1f, 1f));
            warnTmp.rectTransform.sizeDelta = new Vector2(0, 20);
        }

        var titleLabel = MakeTMP(section.ContentArea, $"{(isGet ? "Import target" : "Export reserve")}: {ResourceLabel(rd)}", 14, new Color(0.9f, 0.9f, 0.5f, 1f));
        titleLabel.rectTransform.sizeDelta = new Vector2(0, 22);

        var amountRow = MakeHLRow(section.ContentArea, 34f, 0);
        var amountDisplay = MakeTMP(amountRow.transform, "0", 22, Color.white);
        amountDisplay.alignment = TextAlignmentOptions.Center;
        TextMeshProUGUI targetSummary = null;
        TextMeshProUGUI minimumSummary = null;

        void UpdateAmountDisplay()
        {
            if (isGet)
            {
                if (!useMinimum)
                    editingMinimum = false;
                currentAmount = editingMinimum ? minimumAmount : targetAmount;
            }
            if (currentAmount >= 1_000_000)
                amountDisplay.text = (currentAmount / 1_000_000).ToString("0.##") + "M";
            else if (currentAmount >= 1_000)
                amountDisplay.text = (currentAmount / 1_000).ToString("0.##") + "K";
            else
                amountDisplay.text = currentAmount.ToString("0");

            if (isGet)
            {
                amountDisplay.text = (editingMinimum ? "Minimum: " : "Target: ") + amountDisplay.text;
                if (minimumAmount > targetAmount)
                    minimumAmount = targetAmount;
                if (targetSummary != null)
                    targetSummary.text = $"Target: {targetAmount:0.#}";
                if (minimumSummary != null)
                    minimumSummary.text = useMinimum ? $"Minimum: {minimumAmount:0.#}" : "Minimum: off";
            }
            else
            {
                amountDisplay.text = "Keep: " + amountDisplay.text;
            }
        }

        if (isGet)
        {
            var editRow = MakeHLRow(section.ContentArea, 28f, 6);
            AddBigButtonInline(editRow.transform, "Edit Target", _runtimeStyle.ActionButtonColor, () =>
            {
                editingMinimum = false;
                UpdateAmountDisplay();
            });
            AddBigButtonInline(editRow.transform, "Edit Minimum", _runtimeStyle.ActionButtonColor, () =>
            {
                editingMinimum = useMinimum;
                UpdateAmountDisplay();
            });

            var minimumToggleRow = MakeHLRow(section.ContentArea, 28f, 6);
            TextMeshProUGUI minimumToggleLabel = null;
            void RefreshMinimumToggle()
            {
                if (minimumToggleLabel != null)
                    minimumToggleLabel.text = useMinimum ? "[X] Minimum threshold" : "[ ] Minimum threshold";
            }
            var minimumToggleGo = new GameObject("MinimumToggle", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            minimumToggleGo.transform.SetParent(minimumToggleRow.transform, false);
            var minimumToggleLayout = minimumToggleGo.GetComponent<LayoutElement>();
            minimumToggleLayout.preferredHeight = 28f;
            minimumToggleLayout.minWidth = 160f;
            minimumToggleLayout.flexibleWidth = 1f;
            minimumToggleGo.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
            var minimumToggleButton = minimumToggleGo.GetComponent<Button>();
            minimumToggleButton.navigation = new Navigation { mode = Navigation.Mode.None };
            minimumToggleLabel = MakeTMP(minimumToggleGo.transform, "", 13, new Color(0.8f, 0.8f, 0.82f, 1f));
            minimumToggleLabel.alignment = TextAlignmentOptions.Center;
            minimumToggleButton.onClick.AddListener(() =>
            {
                useMinimum = !useMinimum;
                if (!useMinimum)
                    editingMinimum = false;
                else if (minimumAmount <= 0 && targetAmount > 0)
                    minimumAmount = targetAmount;
                RefreshMinimumToggle();
                UpdateAmountDisplay();
            });
            RefreshMinimumToggle();

            targetSummary = MakeTMP(section.ContentArea, "Target: 0", 12, new Color(0.7f, 0.7f, 0.75f, 1f));
            targetSummary.rectTransform.sizeDelta = new Vector2(0, 18);
            minimumSummary = MakeTMP(section.ContentArea, "Minimum: 0", 12, new Color(0.7f, 0.7f, 0.75f, 1f));
            minimumSummary.rectTransform.sizeDelta = new Vector2(0, 18);
        }

        void AddAmount(double delta)
        {
            if (isGet)
            {
                if (editingMinimum)
                    minimumAmount = useMinimum ? System.Math.Max(0, System.Math.Min(targetAmount, minimumAmount + delta)) : 0;
                else
                {
                    targetAmount = System.Math.Max(0, targetAmount + delta);
                    if (minimumAmount > targetAmount)
                        minimumAmount = targetAmount;
                }
            }
            else
            {
                currentAmount = System.Math.Max(0, currentAmount + delta);
            }
            UpdateAmountDisplay();
        }

        var plusRow = MakeHLRow(section.ContentArea, 28f, 4);
        AddSmallButton(plusRow.transform, "+10", _runtimeStyle.SmallButtonPositiveColor, () => AddAmount(10));
        AddSmallButton(plusRow.transform, "+100", _runtimeStyle.SmallButtonPositiveColor, () => AddAmount(100));
        AddSmallButton(plusRow.transform, "+1K", _runtimeStyle.SmallButtonPositiveColor, () => AddAmount(1000));
        AddSmallButton(plusRow.transform, "+10K", _runtimeStyle.SmallButtonPositiveColor, () => AddAmount(10000));
        AddSmallButton(plusRow.transform, "+100K", _runtimeStyle.SmallButtonPositiveColor, () => AddAmount(100000));
        AddSmallButton(plusRow.transform, "+1M", _runtimeStyle.SmallButtonPositiveColor, () => AddAmount(1000000));

        var minusRow = MakeHLRow(section.ContentArea, 28f, 4);
        AddSmallButton(minusRow.transform, "\u221210", _runtimeStyle.SmallButtonColor, () => AddAmount(-10));
        AddSmallButton(minusRow.transform, "\u2212100", _runtimeStyle.SmallButtonColor, () => AddAmount(-100));
        AddSmallButton(minusRow.transform, "\u22121K", _runtimeStyle.SmallButtonColor, () => AddAmount(-1000));
        AddSmallButton(minusRow.transform, "\u221210K", _runtimeStyle.SmallButtonColor, () => AddAmount(-10000));
        AddSmallButton(minusRow.transform, "\u2212100K", _runtimeStyle.SmallButtonColor, () => AddAmount(-100000));
        AddSmallButton(minusRow.transform, "\u22121M", _runtimeStyle.SmallButtonColor, () => AddAmount(-1000000));

        void DoConfirm()
        {
            if (_inputConfirmed) return;
            _inputConfirmed = true;
            var finalAmount = isGet ? targetAmount : currentAmount;
            if (isGet ? finalAmount > 0 : finalAmount >= 0)
            {
                if (isGet)
                    Data.LogisticsNetwork.AddRequest(capturedOi, rd, targetAmount, minimumAmount, useMinimum);
                else
                    Data.LogisticsNetwork.AddProvider(capturedOi, rd, currentAmount);
            }
            if (isGet) BuildGetSection(); else BuildSendSection();
            RebuildSectionLayout(section);
        }

        var confirmRow = new GameObject("ConfirmRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        confirmRow.transform.SetParent(section.ContentArea, false);
        confirmRow.GetComponent<LayoutElement>().preferredHeight = 32f;
        var crHLG = confirmRow.GetComponent<HorizontalLayoutGroup>();
        crHLG.spacing = 8;

        AddBigButtonInline(confirmRow.transform, "Confirm", _runtimeStyle.ConfirmButtonColor, () => DoConfirm());
        AddBigButtonInline(confirmRow.transform, "Cancel", _runtimeStyle.BackButtonColor, () =>
        {
            _inputConfirmed = true;
            ShowResourcePicker(section, isGet);
        });

        RebuildSectionLayout(section);
    }

    private GameObject MakeHLRow(Transform parent, float height, float spacing)
    {
        var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(Image), typeof(LayoutElement), typeof(ContentSizeFitter));
        row.transform.SetParent(parent, false);
        var le = row.GetComponent<LayoutElement>();
        le.minHeight = height;
        row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.spacing = spacing;
        hlg.padding = new RectOffset(8, 8, 4, 4);
        var fitter = row.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        return row;
    }

    private void MakeXButton(Transform parent, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject("XBtn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGo.transform.SetParent(parent, false);
        btnGo.GetComponent<LayoutElement>().preferredWidth = 24f;
        btnGo.GetComponent<Image>().color = _runtimeStyle.RemoveButtonColor;
        var btn = btnGo.GetComponent<Button>();
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var tmp = MakeTMP(btnGo.transform, "X", 12, new Color(0.92f, 0.88f, 0.88f, 1f));
        tmp.alignment = TextAlignmentOptions.Center;
        btn.onClick.AddListener(onClick);
    }

    private void AddBigButton(Transform parent, string text, Color color, UnityEngine.Events.UnityAction onClick)
    {
        AddBigButtonInline(parent, text, color, onClick);
    }

    private void AddBigButtonInline(Transform parent, string text, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGo.transform.SetParent(parent, false);
        var layout = btnGo.GetComponent<LayoutElement>();
        layout.preferredHeight = 28f;
        layout.minWidth = 120f;
        layout.flexibleWidth = 1f;
        btnGo.GetComponent<Image>().color = color;
        var btn = btnGo.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };

        var labelTmp = MakeTMP(btnGo.transform, text, 14, new Color(0.86f, 0.86f, 0.88f, 1f));
        labelTmp.alignment = TextAlignmentOptions.Center;
        labelTmp.rectTransform.offsetMin = new Vector2(8, 2);
        labelTmp.rectTransform.offsetMax = new Vector2(-8, -2);

        btn.onClick.AddListener(onClick);
    }

    private void AddSmallButton(Transform parent, string text, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGo.transform.SetParent(parent, false);
        btnGo.GetComponent<LayoutElement>().preferredWidth = 46f;
        btnGo.GetComponent<LayoutElement>().preferredHeight = 24f;
        btnGo.GetComponent<Image>().color = color;
        var btn = btnGo.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };

        var labelTmp = MakeTMP(btnGo.transform, text, 12, new Color(0.86f, 0.86f, 0.88f, 1f));
        labelTmp.alignment = TextAlignmentOptions.Center;
        labelTmp.rectTransform.offsetMin = new Vector2(4, 1);
        labelTmp.rectTransform.offsetMax = new Vector2(-4, -1);

        btn.onClick.AddListener(onClick);
    }

    private TextMeshProUGUI MakeTMP(Transform parent, string text, float fontSize, Color color)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(4, 2); rt.offsetMax = new Vector2(-4, -2);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.font = _font; tmp.fontSize = fontSize; tmp.color = color;
        tmp.richText = true;
        return tmp;
    }

    private static string ResourceLabel(ResourceDefinition rd, string fallbackId = null)
    {
        if (rd == null) return fallbackId ?? "?";
        var name = LEManager.Get(rd.ID, rd.ID);
        return $"{rd.IconString} {name}";
    }

    private static string ShipDisplayName(string typeKey, bool isSpacecraft)
    {
        if (string.IsNullOrEmpty(typeKey)) return "?";

        if (isSpacecraft)
        {
            foreach (var sc in Object.FindObjectsOfType<Spacecraft>())
            {
                var type = sc?.spacecraftType;
                if (type == null) continue;
                if (sc.GetCompany() != MonoBehaviourSingleton<GameManager>.Instance?.Player) continue;
                if (Data.LogisticsNetwork.TypeKey(type.ID, type.NameRocketType ?? "SC") == typeKey || type.NameRocketType == typeKey)
                    return ShipIcon(type.SpriteId) + " " + type.NameRocketType;
            }
        }
        else
        {
            foreach (var lv in Object.FindObjectsOfType<LaunchVehicle>())
            {
                var type = lv?.launchVehicleType;
                if (type == null) continue;
                if (lv.GetCompany() != MonoBehaviourSingleton<GameManager>.Instance?.Player) continue;
                if (Data.LogisticsNetwork.TypeKey(type.ID, type.Name ?? "LV") == typeKey || type.Name == typeKey)
                    return ShipIcon(type.SpriteId) + " " + type.Name;
            }
        }

        return typeKey;
    }

    private static string ShipIcon(string spriteId)
    {
        if (string.IsNullOrEmpty(spriteId)) return "";
        var objManager = MonoBehaviourSingleton<ObjectInfoManager>.Instance;
        return objManager != null ? objManager.spriteTextStart5.MyFormat(spriteId, "") : "";
    }

    public void RebuildLayout()
    {
        if (_built && isActiveAndEnabled)
            LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
    }

    private void OnDestroy()
    {
        foreach (var sec in _sections)
            if (sec?.Root != null) Destroy(sec.Root);
        _sections.Clear();
    }

    private static string StatusToString(Data.LogisticsRequestStatus s) => s switch
    {
        Data.LogisticsRequestStatus.Pending => Logic.LogisticsStrings.StatusPending(),
        Data.LogisticsRequestStatus.InProgress => Logic.LogisticsStrings.StatusInTransit(),
        Data.LogisticsRequestStatus.Satisfied => Logic.LogisticsStrings.StatusSatisfied(),
        Data.LogisticsRequestStatus.Failed => Logic.LogisticsStrings.StatusFailed(),
        _ => "?"
    };

    private static Color StatusColor(Data.LogisticsRequestStatus s) => s switch
    {
        Data.LogisticsRequestStatus.Pending => new Color(0.7f, 0.7f, 0.3f, 1f),
        Data.LogisticsRequestStatus.InProgress => new Color(0.3f, 0.5f, 0.9f, 1f),
        Data.LogisticsRequestStatus.Satisfied => new Color(0.3f, 0.8f, 0.3f, 1f),
        Data.LogisticsRequestStatus.Failed => new Color(0.9f, 0.3f, 0.3f, 1f),
        _ => new Color(0.5f, 0.5f, 0.5f, 1f)
    };
}
