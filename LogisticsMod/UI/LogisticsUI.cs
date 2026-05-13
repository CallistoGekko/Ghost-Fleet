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
    private static readonly Color RowBgColor = new Color(0.12f, 0.12f, 0.14f, 0.96f);
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
            if (_font == null) return;

            _objectInfoWindow = GetComponent<ObjectInfoWindow>();
            if (_objectInfoWindow == null) return;

            var oics = _objectInfoWindow.GetComponent<ObjectInfoCollapseSections>();
            if (oics == null || oics.uiLists == null || oics.uiLists.Count == 0) return;

            var sectionParent = oics.uiLists[0].transform;
            _parentRt = sectionParent.parent as RectTransform;
            float sectionWidth = (sectionParent as RectTransform).sizeDelta.x;
            if (sectionWidth <= 0) sectionWidth = _parentRt.rect.width;

            var styleButton = oics.expandButtons != null && oics.expandButtons.Count > 5 ? oics.expandButtons[5] : null;
            var styleIcon = oics.buttonsIcons != null && oics.buttonsIcons.Count > 5 ? oics.buttonsIcons[5] : null;
            CaptureRuntimeStyle(oics, styleButton);

            _getSection = new LogisticsSection(_parentRt, FormatSectionTitle("GET", "Request Resources"), _font, sectionWidth,
                styleButton, styleIcon, oics.spriteExpand, oics.spriteCollapse,
                _runtimeStyle.HeaderHeight, _runtimeStyle.HeaderFontSize, _runtimeStyle.HeaderBackgroundColor,
                _runtimeStyle.HeaderTextColor, _runtimeStyle.HeaderDividerColor, new Color(0f, 0f, 0f, 0f),
                _runtimeStyle.HasHeaderButtonColors ? _runtimeStyle.HeaderButtonColors : null);
            _sections.Add(_getSection);

            _sendSection = new LogisticsSection(_parentRt, FormatSectionTitle("SEND", "Provide Resources"), _font, sectionWidth,
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
        catch { }
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

        TryCaptureHeaderTypography(oics, 5, "PLANNED");
        TryCaptureLaunchListRowStyle(_objectInfoWindow?.launchVehicleList);
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

        if (data != null && data.requests.Count > 0)
        {
            for (int i = 0; i < data.requests.Count; i++)
            {
                var req = data.requests[i];
                var idx = i;
                var rd = req.ResourceDefinition;
                var displayName = ResourceLabel(rd, req.resourceDef?.id);
                var statusStr = StatusToString(req.status);
                var noteStr = !string.IsNullOrEmpty(req.statusNote) ? $" ({req.statusNote})" : "";

                var row = MakeHLRow(_getSection.ContentArea, 24f, 8);
                MakeTMP(row.transform, $"{displayName}: {req.requestedAmount:0.#}  [{statusStr}]{noteStr}", 13, StatusColor(req.status));
                var capturedOi = _currentObjectInfo;
                MakeXButton(row.transform, () =>
                {
                    Data.LogisticsNetwork.RemoveRequest(capturedOi, idx);
                    BuildGetSection();
                    RebuildSectionLayout(_getSection);
                });
            }
        }
        else
        {
            _getSection.AddTextRow("No resource requests configured.", _font, 13f, new Color(0.5f, 0.5f, 0.5f, 1f));
        }

        AddBigButton(_getSection.ContentArea, "+ Add Request", _runtimeStyle.ConfirmButtonColor, () =>
        {
            ShowResourcePicker(_getSection, true);
        });
    }

    private void BuildSendSection()
    {
        _sendSection.ClearContent();
        var data = Data.LogisticsNetwork.Get(_currentObjectInfo);

        if (data != null && data.providers.Count > 0)
        {
            for (int i = 0; i < data.providers.Count; i++)
            {
                var prov = data.providers[i];
                var idx = i;
                var rd = prov.ResourceDefinition;
                var displayName = ResourceLabel(rd, prov.resourceDef?.id);

                var row = MakeHLRow(_sendSection.ContentArea, 24f, 8);
                MakeTMP(row.transform, $"{displayName}: min keep {prov.minimumKeep:0.#}", 13, new Color(0.7f, 0.7f, 0.7f, 1f));
                var capturedOi = _currentObjectInfo;
                MakeXButton(row.transform, () =>
                {
                    Data.LogisticsNetwork.RemoveProvider(capturedOi, idx);
                    BuildSendSection();
                    RebuildSectionLayout(_sendSection);
                });
            }
        }
        else
        {
            _sendSection.AddTextRow("No resource exports configured.", _font, 13f, new Color(0.5f, 0.5f, 0.5f, 1f));
        }

        AddBigButton(_sendSection.ContentArea, "+ Add Provider", _runtimeStyle.ActionButtonColor, () =>
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

        if (!isSpacecraft)
        {
            BuildLVSectionOnly(section);
            return;
        }

        var data = Data.LogisticsNetwork.Get(_currentObjectInfo);
        var quotas = data?.spacecraftQuota ?? new List<Data.ShipQuotaEntry>();

        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        LogisticsObserver.GetActiveCycleCounts(player, out var scActive, out var lvActive);
        var active = isSpacecraft ? scActive : lvActive;

        if (quotas.Count > 0)
        {
            foreach (var q in quotas)
            {
                var quotaTypeName = q.typeName;
                var quotaCount = q.count;
                active.TryGetValue(quotaTypeName, out var activeCount);
                var free = quotaCount - activeCount;

                var row = MakeHLRow(section.ContentArea, 28f, 4);
                row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;

                var countColor = free > 0
                    ? new Color(0.5f, 0.9f, 0.5f, 1f)
                    : new Color(0.9f, 0.55f, 0.1f, 1f);
                var countLabel = MakeTMP(row.transform, $"{free}/{quotaCount}", 14, countColor);
                countLabel.alignment = TextAlignmentOptions.Center;
                countLabel.rectTransform.sizeDelta = new Vector2(72, 0);

                MakeTMP(row.transform, ShipDisplayName(quotaTypeName, true), _runtimeStyle.RowFontSize, _runtimeStyle.RowTextColor);

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

        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var typeCounts = Data.LogisticsNetwork.GetShipTypeCountsOnObject(_currentObjectInfo, false, player);

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

            var currentQuota = Data.LogisticsNetwork.GetQuota(_currentObjectInfo, lvTypeName, false);
            var isEnabled = currentQuota > 0;

            var row = MakeHLRow(section.ContentArea, 26f, 4);
            row.GetComponent<Image>().color = isEnabled ? _runtimeStyle.ToggleOnColor : _runtimeStyle.ToggleOffColor;

            var activeColor = isEnabled ? new Color(0.54f, 0.9f, 0.62f, 1f) : new Color(0.66f, 0.66f, 0.7f, 1f);
            MakeTMP(row.transform, $"{ShipDisplayName(lvTypeName, false)}  x{count}", 13, activeColor);

            var statusText = isEnabled ? "ON" : "OFF";
            var statusColor = isEnabled ? new Color(0.58f, 0.9f, 0.58f, 1f) : new Color(0.48f, 0.48f, 0.52f, 1f);
            MakeTMP(row.transform, statusText, 11, statusColor);

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
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var typeCounts = Data.LogisticsNetwork.GetShipTypeCountsOnObject(_currentObjectInfo, isSpacecraft, player);
        LogisticsObserver.GetActiveCycleCounts(player, out var scActive, out var lvActive);
        var active = isSpacecraft ? scActive : lvActive;

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
            active.TryGetValue(shipTypeName, out var activeCount);
            var freeQuota = (currentQuota > 0) ? $"{currentQuota - activeCount}/{currentQuota}" : "0";
            var displayQuota = currentQuota > 0 ? $"quota: {freeQuota}" : "no quota";

            var row = MakeHLRow(section.ContentArea, 26f, 4);
            row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;

            MakeTMP(row.transform, $"{ShipDisplayName(shipTypeName, isSpacecraft)}  {totalCount} available ({displayQuota})", _runtimeStyle.RowFontSize, _runtimeStyle.RowTextColor);

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
        _inputConfirmed = false;
        double currentAmount = 0;
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

        var titleLabel = MakeTMP(section.ContentArea, $"{(isGet ? "Request" : "Provide")}: {ResourceLabel(rd)}", 14, new Color(0.9f, 0.9f, 0.5f, 1f));
        titleLabel.rectTransform.sizeDelta = new Vector2(0, 22);

        var amountRow = MakeHLRow(section.ContentArea, 34f, 0);
        var amountDisplay = MakeTMP(amountRow.transform, "0", 22, Color.white);
        amountDisplay.alignment = TextAlignmentOptions.Center;

        void UpdateAmountDisplay()
        {
            if (currentAmount >= 1_000_000)
                amountDisplay.text = (currentAmount / 1_000_000).ToString("0.##") + "M";
            else if (currentAmount >= 1_000)
                amountDisplay.text = (currentAmount / 1_000).ToString("0.##") + "K";
            else
                amountDisplay.text = currentAmount.ToString("0");
        }

        var plusRow = MakeHLRow(section.ContentArea, 28f, 4);
        AddSmallButton(plusRow.transform, "+10", _runtimeStyle.SmallButtonPositiveColor, () => { currentAmount += 10; UpdateAmountDisplay(); });
        AddSmallButton(plusRow.transform, "+100", _runtimeStyle.SmallButtonPositiveColor, () => { currentAmount += 100; UpdateAmountDisplay(); });
        AddSmallButton(plusRow.transform, "+1K", _runtimeStyle.SmallButtonPositiveColor, () => { currentAmount += 1000; UpdateAmountDisplay(); });
        AddSmallButton(plusRow.transform, "+10K", _runtimeStyle.SmallButtonPositiveColor, () => { currentAmount += 10000; UpdateAmountDisplay(); });
        AddSmallButton(plusRow.transform, "+100K", _runtimeStyle.SmallButtonPositiveColor, () => { currentAmount += 100000; UpdateAmountDisplay(); });
        AddSmallButton(plusRow.transform, "+1M", _runtimeStyle.SmallButtonPositiveColor, () => { currentAmount += 1000000; UpdateAmountDisplay(); });

        var minusRow = MakeHLRow(section.ContentArea, 28f, 4);
        AddSmallButton(minusRow.transform, "\u221210", _runtimeStyle.SmallButtonColor, () => { currentAmount = System.Math.Max(0, currentAmount - 10); UpdateAmountDisplay(); });
        AddSmallButton(minusRow.transform, "\u2212100", _runtimeStyle.SmallButtonColor, () => { currentAmount = System.Math.Max(0, currentAmount - 100); UpdateAmountDisplay(); });
        AddSmallButton(minusRow.transform, "\u22121K", _runtimeStyle.SmallButtonColor, () => { currentAmount = System.Math.Max(0, currentAmount - 1000); UpdateAmountDisplay(); });
        AddSmallButton(minusRow.transform, "\u221210K", _runtimeStyle.SmallButtonColor, () => { currentAmount = System.Math.Max(0, currentAmount - 10000); UpdateAmountDisplay(); });
        AddSmallButton(minusRow.transform, "\u2212100K", _runtimeStyle.SmallButtonColor, () => { currentAmount = System.Math.Max(0, currentAmount - 100000); UpdateAmountDisplay(); });
        AddSmallButton(minusRow.transform, "\u22121M", _runtimeStyle.SmallButtonColor, () => { currentAmount = System.Math.Max(0, currentAmount - 1000000); UpdateAmountDisplay(); });

        void DoConfirm()
        {
            if (_inputConfirmed) return;
            _inputConfirmed = true;
            if (currentAmount > 0)
            {
                if (isGet)
                    Data.LogisticsNetwork.AddRequest(capturedOi, rd, currentAmount);
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
        var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(Image), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().preferredHeight = height;
        row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.spacing = spacing;
        hlg.padding = new RectOffset(8, 8, 2, 2);
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
        Data.LogisticsRequestStatus.Pending => "pending",
        Data.LogisticsRequestStatus.InProgress => "in transit",
        Data.LogisticsRequestStatus.Satisfied => "satisfied",
        Data.LogisticsRequestStatus.Failed => "failed",
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

}