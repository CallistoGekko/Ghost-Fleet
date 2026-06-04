using System;
using System.Collections;
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
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LaunchVehicleType = global::Data.ScriptableObject.LaunchVehicleType;
using SpacecraftType = global::Data.ScriptableObject.SpacecraftType;

namespace LogisticsMod.UI;

public class LogisticsUI : MonoBehaviour
{
    private const int SectionIndexSpacecraft = 3;
    private const int SectionIndexLaunchVehicle = 4;
    private const float RouteResourceTargetStatusGap = 14f;
    private const float RouteHeaderNameMaxWidth = 190f;
    private const float RouteStickyHeaderNameMaxWidth = 124f;
    private const float RouteHeaderArrowWidth = 28f;
    private const float RouteOverviewDetailPanelIndent = 24f;
    private const float RouteOverviewDetailContentInset = 32f;
    private const float RouteOverviewDetailRightInset = 8f;
    private const float RouteOverviewDetailBottomInset = 4f;
    private const float RouteOverviewGroupGap = 12f;
    private const float RouteResourceRowHeight = 34f;
    private const float GhostFlightRowHeight = 46f;
    private const float RouteAssetNumberColumnWidth = 48f;
    private const float RouteAssetIconColumnWidth = 46f;
    private const float RouteAssetNameColumnWidth = 160f;
    private const float FlightPlanModeControlsWidth = 150f;
    private const float FlightPlanModeFastButtonWidth = 58f;
    private const float FlightPlanModeOptimalButtonWidth = 76f;
    private const float FlightPlanModeButtonGap = 4f;
    private const float FlightPlanModeButtonGroupWidth =
        FlightPlanModeFastButtonWidth + FlightPlanModeButtonGap + FlightPlanModeOptimalButtonWidth;
    private const float FlightPlanModeColumnGap = 10f;
    private const float RouteFacilityLaunchOptionWidth = 176f;
    private const float RouteFacilityLaunchOptionHeight = 54f;
    private const float RouteFacilityLaunchIconColumnWidth = 42f;
    private const int RouteFacilityLaunchSectionHorizontalPadding = 0;
    private const int RouteFacilityLaunchSectionVerticalPadding = 8;
    private const int RouteFacilityLaunchTileHorizontalPadding = 8;
    private const int RouteFacilityLaunchTileVerticalPadding = 6;
    private const float RouteFacilityLaunchTileGap = 6f;
    private const int RouteFacilityLaunchTilesPerRow = 3;
    private const float RouteEditorLiveRefreshIntervalSeconds = 0.5f;
    private const int VirtualListBufferRows = 5;
    private const int BodySwitcherCrowdedChildThreshold = 6;
    private const string RouteLaunchCategoryMagneticRails = "magnetic-launch-rails";
    private const string RouteLaunchCategoryLaunchPad = "launch-pad";
    private const string RouteLaunchCategoryRotary = "rotary-launcher";
    private const string RouteLaunchCategorySpaceElevator = "space-elevator";
    private const string RouteLaunchCategoryElectromagneticCatapult = "electromagnetic-catapult";
    private const string RouteLaunchCategoryMassDriver = "stationary-mass-driver";

    private static readonly string[] RouteFacilityLaunchCategories =
    {
        RouteLaunchCategoryMagneticRails,
        RouteLaunchCategoryLaunchPad,
        RouteLaunchCategoryRotary,
        RouteLaunchCategorySpaceElevator,
        RouteLaunchCategoryElectromagneticCatapult,
        RouteLaunchCategoryMassDriver
    };

    private static readonly Color VoidColor = UiColor(0x07, 0x08, 0x0A);
    private static readonly Color PanelColor = UiColor(0x0D, 0x0F, 0x13);
    private static readonly Color CardColor = UiColor(0x13, 0x16, 0x1C);
    private static readonly Color BorderColor = UiColor(0x1E, 0x22, 0x29);
    private static readonly Color HoverColor = UiColor(0x2A, 0x2F, 0x38);
    private static readonly Color PrimaryTextColor = UiColor(0xF0, 0xEA, 0xE0);
    private static readonly Color SecondaryTextColor = UiColor(0xC8, 0xC4, 0xBC);
    private static readonly Color TertiaryTextColor = UiColor(0x8C, 0x8F, 0x95);
    private static readonly Color DisabledTextColor = UiColor(0x4A, 0x50, 0x58);
    private static readonly Color EngineAccentColor = UiColor(0xF8, 0x62, 0x2A);
    private static readonly Color EnginePressedColor = UiColor(0xC4, 0x4F, 0x1E);
    private static readonly Color EngineTintColor = UiColor(0x3A, 0x1A, 0x0C);
    private static readonly Color WarningColor = UiColor(0xD4, 0x92, 0x2A);
    private static readonly Color CriticalColor = UiColor(0xB8, 0x40, 0x40);
    private static readonly Color NominalColor = UiColor(0x3E, 0x9E, 0x80);

    private static readonly Color PanelOuterColor = WithAlpha(PanelColor, 0.98f);
    private static readonly Color PanelInnerColor = PanelOuterColor;
    private static readonly Color HeaderBarColor = WithAlpha(CardColor, 0.98f);
    private static readonly Color RouteSubheaderBarColor = UiColor(0x10, 0x12, 0x17, 0.98f);
    private static readonly Color RowBgColor = WithAlpha(CardColor, 0.96f);
    private static readonly Color RowBgMutedColor = WithAlpha(PanelColor, 0.94f);
    private static readonly Color AccentLineColor = WithAlpha(BorderColor, 0.82f);
    private static readonly Color AccentTextColor = EngineAccentColor;
    private static readonly Color SubtleTextColor = TertiaryTextColor;
    private static readonly Color AccentButtonColor = WithAlpha(BorderColor, 0.98f);
    private static readonly Color ConfirmButtonColor = WithAlpha(NominalColor, 0.5f);
    private static readonly Color BackButtonColor = WithAlpha(CardColor, 0.98f);
    private static readonly Color RemoveButtonColor = WithAlpha(CriticalColor, 0.72f);
    private static readonly Color CountButtonColor = WithAlpha(HoverColor, 0.98f);
    private static readonly Color CountButtonPositiveColor = WithAlpha(NominalColor, 0.82f);
    private static readonly Color ToggleOnRowColor = WithAlpha(NominalColor, 0.88f);
    private static readonly Color ToggleOffRowColor = WithAlpha(HoverColor, 0.98f);
    private static readonly Color ButtonFillColor = WithAlpha(VoidColor, 0.12f);
    private static readonly Color ButtonSelectedFillColor = WithAlpha(NominalColor, 0.22f);
    private static readonly Color ButtonHoverFillColor = WithAlpha(HoverColor, 0.55f);
    private static readonly Color ButtonPressedFillColor = WithAlpha(BorderColor, 0.82f);
    private static readonly Color CloseButtonFillColor = UiColor(0x34, 0x08, 0x08, 0.96f);
    private static readonly Color CloseButtonHoverFillColor = WithAlpha(CriticalColor, 0.58f);

    private static Color UiColor(int r, int g, int b, float a = 1f)
    {
        return new Color(r / 255f, g / 255f, b / 255f, a);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        return new Color(color.r, color.g, color.b, alpha);
    }

    private List<LogisticsSection> _sections = new List<LogisticsSection>();
    private ObjectInfoData _currentData;
    private ObjectInfo _currentObjectInfo;
    private ObjectInfoData _selectedData;
    private ObjectInfo _selectedObjectInfo;
    private ObjectInfoData _popupPinnedData;
    private ObjectInfo _popupPinnedObjectInfo;
    private ObjectInfoWindow _objectInfoWindow;
    private RectTransform _parentRt;
    private bool _built;
    private bool _popupBuilt;
    private TMP_FontAsset _font;
    private RuntimeUiStyle _runtimeStyle = new RuntimeUiStyle();
    private Button _styleButton;
    private Image _styleIcon;
    private Sprite _styleExpand;
    private Sprite _styleCollapse;
    private float _sectionWidth = 560f;
    private GameObject _launcherButtonGo;
    private GameObject _popupRoot;
    private RectTransform _popupPanelRt;
    private RectTransform _popupContentRt;
    private ScrollRect _popupScroll;
    private Image _popupTitleIcon;
    private TextMeshProUGUI _popupTitle;
    private RectTransform _popupBodySwitchRow;
    private RectTransform _popupRouteSubheader;
    private int _activeRouteEditorRouteId = -1;
    private bool _routeEditorMainPageVisible;
    private DateTime _lastRouteEditorRefreshTime = DateTime.MinValue;
    private float _nextRouteEditorLiveRefreshAt;
    private bool _popupRegisteredOpen;

    private LogisticsSection _getSection;
    private LogisticsSection _sendSection;
    private LogisticsSection _routesSection;
    private static int _openPopupCount;
    private static readonly List<RectTransform> _openPopupPanels = new List<RectTransform>();
    private static readonly List<LogisticsUI> _openPopupInstances = new List<LogisticsUI>();

    public static bool AnyPopupOpen => _openPopupCount > 0;

    public static bool ConsumeEscapeIfPopupOpen()
    {
        if (!AnyPopupOpen || !Input.GetKeyDown(KeyCode.Escape))
            return false;

        for (var i = _openPopupInstances.Count - 1; i >= 0; i--)
        {
            var ui = _openPopupInstances[i];
            if (ui == null || ui._popupRoot == null || !ui._popupRoot.activeSelf)
            {
                _openPopupInstances.RemoveAt(i);
                continue;
            }

            MonoBehaviourSingleton<InputManager>.Instance?.BlockInputForMoment();
            ui.ClosePopup();
            return true;
        }

        return false;
    }

    public static bool PointerIsOverPopupPanel()
    {
        var mousePosition = Input.mousePosition;
        foreach (var panel in _openPopupPanels.ToList())
        {
            if (panel == null)
            {
                _openPopupPanels.Remove(panel);
                continue;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(panel, mousePosition, GetPanelEventCamera(panel)))
                return true;
        }
        return false;
    }

    private static Camera GetPanelEventCamera(RectTransform panel)
    {
        var canvas = panel != null ? panel.GetComponentInParent<Canvas>() : null;
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;
        return canvas.worldCamera;
    }

    private static bool SameObjectInfo(ObjectInfo left, ObjectInfo right)
    {
        if (left == null || right == null)
            return left == right;
        return left.id == right.id;
    }

    private sealed class PopupClickBlocker : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        private static void Block(PointerEventData eventData)
        {
            MonoBehaviourSingleton<InputManager>.Instance?.BlockInputForMoment();
            eventData.Use();
        }

        public void OnPointerDown(PointerEventData eventData) { Block(eventData); }
        public void OnPointerUp(PointerEventData eventData) { Block(eventData); }
        public void OnPointerClick(PointerEventData eventData) { Block(eventData); }
        public void OnBeginDrag(PointerEventData eventData) { Block(eventData); }
        public void OnDrag(PointerEventData eventData) { Block(eventData); }
        public void OnEndDrag(PointerEventData eventData) { Block(eventData); }
        public void OnScroll(PointerEventData eventData) { Block(eventData); }
    }

    private sealed class SmoothPopupScroll : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float WheelPixelsPerTick = 118f;
        private const float SmoothTime = 0.075f;
        private const float MiddleScrollDeadZone = 12f;
        private const float MiddleScrollPixelsPerSecondPerPixel = 6.5f;
        private const float MiddleScrollMaxPixelsPerSecond = 1800f;
        private ScrollRect _scroll;
        private RectTransform _indicatorRt;
        private float _targetY = 1f;
        private float _velocityY;
        private bool _dragging;
        private bool _middleScrolling;
        private bool _hasSmoothTarget;
        private Vector2 _middleAnchorScreen;

        public void Attach(ScrollRect scroll, TMP_FontAsset font)
        {
            _scroll = scroll;
            if (_scroll == null)
                return;

            _targetY = _scroll.verticalNormalizedPosition;
            _scroll.scrollSensitivity = 0f;
            _scroll.inertia = true;
            _scroll.decelerationRate = 0.12f;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            BuildMiddleScrollIndicator(font);
        }

        private void OnEnable()
        {
            if (_scroll != null)
                _targetY = _scroll.verticalNormalizedPosition;
            _velocityY = 0f;
            _dragging = false;
            StopMiddleScroll();
        }

        private void OnDisable()
        {
            StopMiddleScroll();
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (_scroll == null || !_scroll.vertical || _scroll.content == null || _scroll.viewport == null)
                return;

            var hiddenHeight = _scroll.content.rect.height - _scroll.viewport.rect.height;
            if (hiddenHeight <= 0f)
                return;

            if (Mathf.Abs(_scroll.verticalNormalizedPosition - _targetY) > 0.15f)
            {
                _targetY = _scroll.verticalNormalizedPosition;
                _velocityY = 0f;
            }

            _targetY = Mathf.Clamp01(_targetY + eventData.scrollDelta.y * (WheelPixelsPerTick / hiddenHeight));
            _hasSmoothTarget = true;
            eventData.Use();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragging = true;
            _hasSmoothTarget = false;
            SyncToCurrent();
        }

        public void OnDrag(PointerEventData eventData)
        {
            SyncToCurrent();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragging = false;
            _hasSmoothTarget = false;
            SyncToCurrent();
        }

        private void Update()
        {
            if (_scroll == null || _scroll.content == null || _scroll.viewport == null)
                return;

            if (_middleScrolling || Input.GetMouseButtonDown(2))
                HandleMiddleClickScroll();

            if (_middleScrolling)
            {
                MonoBehaviourSingleton<InputManager>.Instance?.BlockInputForMoment();
                _scroll.velocity = Vector2.zero;
                return;
            }

            if (_dragging || _scroll.velocity.sqrMagnitude > 0.01f)
            {
                SyncToCurrent();
                return;
            }

            if (!_hasSmoothTarget)
                return;

            var hiddenHeight = _scroll.content.rect.height - _scroll.viewport.rect.height;
            if (hiddenHeight <= 0f)
            {
                _targetY = 1f;
                _velocityY = 0f;
                _hasSmoothTarget = false;
                return;
            }

            var current = _scroll.verticalNormalizedPosition;
            if (Mathf.Abs(current - _targetY) < 0.0005f)
            {
                _scroll.verticalNormalizedPosition = _targetY;
                _velocityY = 0f;
                _hasSmoothTarget = false;
                return;
            }

            _scroll.verticalNormalizedPosition = Mathf.SmoothDamp(current, _targetY, ref _velocityY,
                SmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        }

        private void HandleMiddleClickScroll()
        {
            var hiddenHeight = _scroll.content.rect.height - _scroll.viewport.rect.height;
            if (hiddenHeight <= 0f)
            {
                StopMiddleScroll();
                return;
            }

            if (!_middleScrolling && Input.GetMouseButtonDown(2) && PointerIsOverViewport())
            {
                _middleScrolling = true;
                _middleAnchorScreen = Input.mousePosition;
                _targetY = _scroll.verticalNormalizedPosition;
                _velocityY = 0f;
                PositionIndicator();
                SetIndicatorVisible(true);
                MonoBehaviourSingleton<InputManager>.Instance?.BlockInputForMoment();
            }

            if (!_middleScrolling)
                return;

            if (!Input.GetMouseButton(2) || Input.GetKeyDown(KeyCode.Escape) || !PointerIsOverViewport())
            {
                StopMiddleScroll();
                SyncToCurrent();
                return;
            }

            var offset = ((Vector2)Input.mousePosition).y - _middleAnchorScreen.y;
            var magnitude = Mathf.Abs(offset) - MiddleScrollDeadZone;
            if (magnitude <= 0f)
                return;

            var pixelsPerSecond = Mathf.Min(MiddleScrollMaxPixelsPerSecond, magnitude * MiddleScrollPixelsPerSecondPerPixel);
            var direction = Mathf.Sign(offset);
            _targetY = Mathf.Clamp01(_targetY + direction * pixelsPerSecond * Time.unscaledDeltaTime / hiddenHeight);
            _hasSmoothTarget = false;
            _scroll.verticalNormalizedPosition = Mathf.SmoothDamp(_scroll.verticalNormalizedPosition, _targetY,
                ref _velocityY, SmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        }

        private bool PointerIsOverViewport()
        {
            if (_scroll == null || _scroll.viewport == null)
                return false;

            return RectTransformUtility.RectangleContainsScreenPoint(_scroll.viewport, Input.mousePosition,
                GetPanelEventCamera(_scroll.viewport));
        }

        private void StopMiddleScroll()
        {
            _middleScrolling = false;
            SetIndicatorVisible(false);
        }

        private void SetIndicatorVisible(bool visible)
        {
            if (_indicatorRt != null)
                _indicatorRt.gameObject.SetActive(visible);
        }

        private void PositionIndicator()
        {
            if (_indicatorRt == null || _scroll == null || _scroll.viewport == null)
                return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_scroll.viewport, _middleAnchorScreen,
                    GetPanelEventCamera(_scroll.viewport), out var localPoint))
                _indicatorRt.anchoredPosition = localPoint;
        }

        private void BuildMiddleScrollIndicator(TMP_FontAsset font)
        {
            if (_scroll?.viewport == null)
                return;

            var indicator = new GameObject("MiddleScrollIndicator", typeof(RectTransform), typeof(Image));
            indicator.transform.SetParent(_scroll.viewport, false);
            _indicatorRt = indicator.GetComponent<RectTransform>();
            _indicatorRt.anchorMin = new Vector2(0.5f, 0.5f);
            _indicatorRt.anchorMax = new Vector2(0.5f, 0.5f);
            _indicatorRt.pivot = new Vector2(0.5f, 0.5f);
            _indicatorRt.sizeDelta = new Vector2(26f, 26f);
            var image = indicator.GetComponent<Image>();
            image.color = WithAlpha(HoverColor, 0.88f);
            image.raycastTarget = false;

            var glyph = new GameObject("Glyph", typeof(RectTransform), typeof(TextMeshProUGUI));
            glyph.transform.SetParent(indicator.transform, false);
            var glyphRt = glyph.GetComponent<RectTransform>();
            glyphRt.anchorMin = Vector2.zero;
            glyphRt.anchorMax = Vector2.one;
            glyphRt.offsetMin = Vector2.zero;
            glyphRt.offsetMax = Vector2.zero;
            var tmp = glyph.GetComponent<TextMeshProUGUI>();
            tmp.text = "\u2195";
            tmp.font = font;
            tmp.fontSize = 15f;
            tmp.color = PrimaryTextColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            indicator.SetActive(false);
        }

        private void SyncToCurrent()
        {
            if (_scroll == null)
                return;
            _targetY = _scroll.verticalNormalizedPosition;
            _velocityY = 0f;
            _hasSmoothTarget = false;
        }
    }

    private sealed class VirtualFixedList : MonoBehaviour
    {
        private readonly Dictionary<int, GameObject> _activeRows = new Dictionary<int, GameObject>();
        private readonly Stack<GameObject> _pooledRows = new Stack<GameObject>();
        private readonly List<int> _scratchIndexes = new List<int>();
        private ScrollRect _scroll;
        private RectTransform _viewport;
        private RectTransform _listRt;
        private LayoutElement _layout;
        private Func<Transform, float, GameObject> _createRow;
        private Action<GameObject, int> _bindRow;
        private int _count;
        private int _bufferRows;
        private float _rowHeight;
        private bool _dirty = true;
        private float _lastScroll = -1f;
        private float _lastViewportHeight = -1f;
        private float _lastListTop = float.MinValue;

        public void Configure(ScrollRect scroll, int count, float rowHeight,
            Func<Transform, float, GameObject> createRow, Action<GameObject, int> bindRow, int bufferRows)
        {
            if (_scroll != null)
                _scroll.onValueChanged.RemoveListener(OnScrollValueChanged);

            _scroll = scroll;
            _viewport = scroll?.viewport;
            _listRt = transform as RectTransform;
            _layout = GetComponent<LayoutElement>();
            _count = Mathf.Max(0, count);
            _rowHeight = Mathf.Max(1f, rowHeight);
            _createRow = createRow;
            _bindRow = bindRow;
            _bufferRows = Mathf.Max(1, bufferRows);

            if (_layout != null)
            {
                _layout.minHeight = _count * _rowHeight;
                _layout.preferredHeight = _layout.minHeight;
                _layout.flexibleHeight = 0f;
                _layout.flexibleWidth = 1f;
            }

            if (_scroll != null)
                _scroll.onValueChanged.AddListener(OnScrollValueChanged);

            _dirty = true;
            RefreshVisibleRows(true);
        }

        private void OnDestroy()
        {
            if (_scroll != null)
                _scroll.onValueChanged.RemoveListener(OnScrollValueChanged);
        }

        private void OnEnable()
        {
            _dirty = true;
        }

        private void LateUpdate()
        {
            if (_scroll == null || _viewport == null || _listRt == null)
                return;

            var viewportHeight = _viewport.rect.height;
            var scrollValue = _scroll.verticalNormalizedPosition;
            var listTop = CurrentListTop();
            if (!_dirty
                && Mathf.Abs(scrollValue - _lastScroll) < 0.0001f
                && Mathf.Abs(viewportHeight - _lastViewportHeight) < 0.5f
                && Mathf.Abs(listTop - _lastListTop) < 0.5f)
                return;

            RefreshVisibleRows(false);
        }

        private void OnScrollValueChanged(Vector2 _)
        {
            _dirty = true;
        }

        private void RefreshVisibleRows(bool force)
        {
            _dirty = false;
            _lastScroll = _scroll != null ? _scroll.verticalNormalizedPosition : -1f;
            _lastViewportHeight = _viewport != null ? _viewport.rect.height : -1f;
            _lastListTop = CurrentListTop();

            if (_count <= 0 || _createRow == null || _bindRow == null || _listRt == null || _viewport == null)
            {
                ReleaseAllRows();
                return;
            }

            var totalHeight = _count * _rowHeight;
            if (!TryGetVisibleRange(totalHeight, out var first, out var last))
            {
                ReleaseAllRows();
                return;
            }

            _scratchIndexes.Clear();
            foreach (var index in _activeRows.Keys)
                if (force || index < first || index > last)
                    _scratchIndexes.Add(index);
            foreach (var index in _scratchIndexes)
                ReleaseRow(index);

            for (var index = first; index <= last; index++)
            {
                if (_activeRows.ContainsKey(index))
                    continue;

                var row = AcquireRow(index);
                _activeRows[index] = row;
                _bindRow(row, index);
            }

            var sibling = 0;
            for (var index = first; index <= last; index++)
            {
                if (_activeRows.TryGetValue(index, out var row))
                    row.transform.SetSiblingIndex(sibling++);
            }
        }

        private bool TryGetVisibleRange(float totalHeight, out int first, out int last)
        {
            first = 0;
            last = -1;

            var listCorners = new Vector3[4];
            var viewportCorners = new Vector3[4];
            _listRt.GetWorldCorners(listCorners);
            _viewport.GetWorldCorners(viewportCorners);

            var scale = Mathf.Max(0.001f, Mathf.Abs(_listRt.lossyScale.y));
            var listTop = listCorners[1].y;
            var viewportBottom = viewportCorners[0].y;
            var viewportTop = viewportCorners[1].y;

            var visibleStart = Mathf.Max(0f, (listTop - viewportTop) / scale);
            var visibleEnd = Mathf.Min(totalHeight, (listTop - viewportBottom) / scale);
            if (visibleEnd <= 0f || visibleStart >= totalHeight)
                return false;

            first = Mathf.Clamp(Mathf.FloorToInt(visibleStart / _rowHeight) - _bufferRows, 0, _count - 1);
            last = Mathf.Clamp(Mathf.CeilToInt(visibleEnd / _rowHeight) + _bufferRows, 0, _count - 1);
            return last >= first;
        }

        private float CurrentListTop()
        {
            if (_listRt == null)
                return float.MinValue;

            var corners = new Vector3[4];
            _listRt.GetWorldCorners(corners);
            return corners[1].y;
        }

        private GameObject AcquireRow(int index)
        {
            var row = _pooledRows.Count > 0
                ? _pooledRows.Pop()
                : _createRow(transform, _rowHeight);
            row.name = $"VirtualRow_{index}";
            row.SetActive(true);
            PrepareRowForReuse(row);
            var rt = row.transform as RectTransform;
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(0f, rt.offsetMin.y);
                rt.offsetMax = new Vector2(0f, rt.offsetMax.y);
                rt.sizeDelta = new Vector2(0f, _rowHeight);
                rt.anchoredPosition = new Vector2(0f, -index * _rowHeight);
            }
            return row;
        }

        private void PrepareRowForReuse(GameObject row)
        {
            if (row == null)
                return;

            foreach (var button in row.GetComponents<Button>())
                DestroyImmediate(button);

            var image = row.GetComponent<Image>();
            if (image != null)
                image.raycastTarget = false;

            var children = new List<GameObject>();
            foreach (Transform child in row.transform)
                if (child != null)
                    children.Add(child.gameObject);
            foreach (var child in children)
                DestroyImmediate(child);
        }

        private void ReleaseRow(int index)
        {
            if (!_activeRows.TryGetValue(index, out var row))
                return;

            _activeRows.Remove(index);
            row.SetActive(false);
            _pooledRows.Push(row);
        }

        private void ReleaseAllRows()
        {
            _scratchIndexes.Clear();
            foreach (var index in _activeRows.Keys)
                _scratchIndexes.Add(index);
            foreach (var index in _scratchIndexes)
                ReleaseRow(index);
        }
    }

    private enum ButtonTone
    {
        Launcher,
        Neutral,
        Back,
        Add,
        Action,
        Confirm,
        Positive,
        Warning,
        Destructive
    }

    private sealed class RouteResourceTableItem
    {
        public Data.LogisticsRouteResourceRule Rule;
        public int Index;
        public ResourceDefinition Resource;
    }

    private sealed class RouteFacilityLaunchOptionUi
    {
        public string Category;
        public string Icon;
        public Sprite IconSprite;
        public int Count;
        public bool IsAvailable;
        public readonly List<string> Details = new List<string>();
    }

    private struct ButtonVisualStyle
    {
        public Color NormalFill;
        public Color HoverFill;
        public Color PressedFill;
        public Color SelectedFill;
        public Color NormalBorder;
        public Color HoverBorder;
        public Color PressedBorder;
        public Color SelectedBorder;
        public Color NormalText;
        public Color HoverText;
        public Color PressedText;
        public Color SelectedText;
        public bool HasBorder;
    }

    private sealed class ThemedButtonVisual : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        private ButtonVisualStyle _style;
        private bool _configured;
        private bool _hovered;
        private bool _pressed;
        private bool _selected;

        public void Configure(ButtonVisualStyle style, bool selected = false)
        {
            _style = style;
            _selected = selected;
            _configured = true;
            Apply();
        }

        public void SetSelected(bool selected)
        {
            _selected = selected;
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            Apply();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            _pressed = false;
            Apply();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = true;
            Apply();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
            Apply();
        }

        public void OnSelect(BaseEventData eventData)
        {
            _hovered = true;
            Apply();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            _hovered = false;
            _pressed = false;
            Apply();
        }

        private void OnDisable()
        {
            _hovered = false;
            _pressed = false;
            Apply();
        }

        private void Apply()
        {
            if (!_configured)
                return;

            var fill = _selected ? _style.SelectedFill : _style.NormalFill;
            var border = _selected ? _style.SelectedBorder : _style.NormalBorder;
            var text = _selected ? _style.SelectedText : _style.NormalText;

            if (_hovered && !_selected)
            {
                fill = _style.HoverFill;
                border = _style.HoverBorder;
                text = _style.HoverText;
            }

            if (_pressed)
            {
                fill = _style.PressedFill;
                border = _style.PressedBorder;
                text = _style.PressedText;
            }

            var image = GetComponent<Image>();
            if (image != null)
                image.color = fill;

            SetButtonBorderColor(transform, _style.HasBorder ? border : new Color(0f, 0f, 0f, 0f));
            foreach (var label in GetComponentsInChildren<TextMeshProUGUI>(true))
                if (label != null)
                    label.color = text;
        }
    }

    private sealed class SmallHoverTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float DelaySeconds = 0.14f;
        private const float TooltipHeight = 22f;
        private const float MaxTooltipWidth = 220f;
        private const float ScreenPadding = 4f;
        private const float GapBelowButton = 6f;

        private string _text;
        private TMP_FontAsset _font;
        private RectTransform _overlayRoot;
        private GameObject _tooltip;
        private bool _hovered;
        private float _hoverStartedAt;

        public void Configure(string text, TMP_FontAsset font, RectTransform overlayRoot)
        {
            _text = string.IsNullOrWhiteSpace(text) ? "?" : text.Trim();
            _font = font;
            _overlayRoot = overlayRoot;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            _hoverStartedAt = Time.unscaledTime;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            Hide();
        }

        private void OnDisable()
        {
            _hovered = false;
            Hide();
        }

        private void OnDestroy()
        {
            Hide();
        }

        private void Update()
        {
            if (!_hovered)
                return;

            if (_tooltip == null && Time.unscaledTime - _hoverStartedAt >= DelaySeconds)
                Show();

            if (_tooltip != null)
                PositionTooltip();
        }

        private void Show()
        {
            var root = TooltipRoot();
            if (root == null || _font == null || string.IsNullOrWhiteSpace(_text))
                return;

            _tooltip = new GameObject("ObjectSwitchTooltip", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            _tooltip.transform.SetParent(root, false);
            _tooltip.transform.SetAsLastSibling();

            var tooltipRt = _tooltip.GetComponent<RectTransform>();
            tooltipRt.anchorMin = new Vector2(0.5f, 0.5f);
            tooltipRt.anchorMax = new Vector2(0.5f, 0.5f);
            tooltipRt.pivot = new Vector2(0.5f, 1f);
            tooltipRt.anchoredPosition = Vector2.zero;

            var image = _tooltip.GetComponent<Image>();
            image.color = WithAlpha(VoidColor, 0.98f);
            image.raycastTarget = false;

            var group = _tooltip.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            AddBorderSegment(_tooltip.transform, "BorderTop", WithAlpha(BorderColor, 0.95f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1f));
            AddBorderSegment(_tooltip.transform, "BorderBottom", WithAlpha(BorderColor, 0.95f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 1f));
            AddBorderSegment(_tooltip.transform, "BorderLeft", WithAlpha(BorderColor, 0.95f),
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(1f, 0f));
            AddBorderSegment(_tooltip.transform, "BorderRight", WithAlpha(BorderColor, 0.95f),
                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(1f, 0f));

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(_tooltip.transform, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(7f, 2f);
            labelRt.offsetMax = new Vector2(-7f, -2f);

            var label = labelGo.GetComponent<TextMeshProUGUI>();
            label.text = _text;
            label.font = _font;
            label.fontSize = 11f;
            label.color = PrimaryTextColor;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;

            var preferred = label.GetPreferredValues(_text, MaxTooltipWidth, TooltipHeight);
            tooltipRt.sizeDelta = new Vector2(Mathf.Clamp(preferred.x + 16f, 38f, MaxTooltipWidth), TooltipHeight);
            PositionTooltip();
        }

        private void Hide()
        {
            if (_tooltip == null)
                return;

            Destroy(_tooltip);
            _tooltip = null;
        }

        private RectTransform TooltipRoot()
        {
            if (_overlayRoot != null)
                return _overlayRoot;

            var canvas = GetComponentInParent<Canvas>();
            return canvas != null ? canvas.GetComponent<RectTransform>() : null;
        }

        private void PositionTooltip()
        {
            var root = TooltipRoot();
            var tooltipRt = _tooltip != null ? _tooltip.GetComponent<RectTransform>() : null;
            var sourceRt = transform as RectTransform;
            if (root == null || tooltipRt == null || sourceRt == null)
                return;

            var camera = GetPanelEventCamera(root);
            var corners = new Vector3[4];
            sourceRt.GetWorldCorners(corners);
            var bottomCenter = (corners[0] + corners[3]) * 0.5f;
            var screenPoint = RectTransformUtility.WorldToScreenPoint(camera, bottomCenter);
            screenPoint.y -= GapBelowButton;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPoint, camera, out var localPoint))
                return;

            var rootRect = root.rect;
            var tooltipSize = tooltipRt.sizeDelta;
            var minX = rootRect.xMin + tooltipSize.x * 0.5f + ScreenPadding;
            var maxX = rootRect.xMax - tooltipSize.x * 0.5f - ScreenPadding;
            var minY = rootRect.yMin + tooltipSize.y + ScreenPadding;
            var maxY = rootRect.yMax - ScreenPadding;

            localPoint.x = minX <= maxX ? Mathf.Clamp(localPoint.x, minX, maxX) : rootRect.center.x;
            localPoint.y = minY <= maxY ? Mathf.Clamp(localPoint.y, minY, maxY) : rootRect.center.y;

            tooltipRt.anchoredPosition = localPoint;
            _tooltip.transform.SetAsLastSibling();
        }
    }

    private sealed class RuntimeUiStyle
    {
        public TMP_FontAsset Font;
        public float RowFontSize = 13f;
        public float HeaderFontSize = 15f;
        public float HeaderHeight = 50f;
        public float RowHeight = 28f;
        public Color HeaderTextColor = AccentTextColor;
        public Color HeaderDividerColor = AccentLineColor;
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
            var sectionRt = sectionParent as RectTransform;
            var vanillaSectionParent = sectionParent.parent as RectTransform;
            if (sectionRt != null && sectionRt.sizeDelta.x > 0)
                _sectionWidth = Mathf.Clamp(sectionRt.sizeDelta.x, 420f, 620f);
            else if (vanillaSectionParent != null && vanillaSectionParent.rect.width > 0)
                _sectionWidth = Mathf.Clamp(vanillaSectionParent.rect.width, 420f, 620f);

            _styleButton = oics.expandButtons != null && oics.expandButtons.Count > SectionIndexLaunchVehicle ? oics.expandButtons[SectionIndexLaunchVehicle] : null;
            _styleIcon = oics.buttonsIcons != null && oics.buttonsIcons.Count > SectionIndexLaunchVehicle ? oics.buttonsIcons[SectionIndexLaunchVehicle] : null;
            _styleExpand = oics.spriteExpand;
            _styleCollapse = oics.spriteCollapse;
            CaptureRuntimeStyle(oics, _styleButton);
            ApplyLogisticsTheme();
            CreateLauncherButton();

            _built = true;
            TrySyncFromWindow(force: true);
            UpdateLauncherVisibility();
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
        ConsumeEscapeIfPopupOpen();
        TrySyncFromWindow(force: false);
        RefreshOpenRouteEditorOnTimeAdvance();
        UpdateLauncherVisibility();
    }

    private TMP_FontAsset FindFont()
    {
        foreach (var tmp in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            if (tmp.font != null && tmp.isActiveAndEnabled)
                return tmp.font;
        return null;
    }

    private void CreateLauncherButton()
    {
        if (_launcherButtonGo != null)
            return;

        var parent = transform as RectTransform;
        if (parent == null)
            return;

        _launcherButtonGo = new GameObject("LogisticsPopupLauncher", typeof(RectTransform), typeof(Image), typeof(Button));
        _launcherButtonGo.transform.SetParent(parent, false);
        _launcherButtonGo.transform.SetAsLastSibling();

        var rt = _launcherButtonGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(116f, 30f);
        rt.anchoredPosition = new Vector2(-14f, -42f);

        _launcherButtonGo.GetComponent<Image>().color = WithAlpha(CardColor, 0.94f);
        var button = _launcherButtonGo.GetComponent<Button>();
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(OpenPopup);

        var label = MakeTMP(_launcherButtonGo.transform, "Logistics", 13f, SecondaryTextColor);
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        label.rectTransform.offsetMin = new Vector2(8f, 2f);
        label.rectTransform.offsetMax = new Vector2(-8f, -2f);
        AddButtonBorder(_launcherButtonGo, WithAlpha(TertiaryTextColor, 0.78f));
        ApplyButtonVisual(button, ButtonTone.Launcher);
    }

    private void UpdateLauncherVisibility()
    {
        if (_launcherButtonGo == null)
            return;

        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var visible = _selectedData != null && _selectedObjectInfo != null && player != null && _selectedData.company == player;
        _launcherButtonGo.SetActive(visible);
    }

    private void OpenPopup()
    {
        TrySyncFromWindow(force: true);
        if (_selectedObjectInfo == null || _selectedData == null)
            return;

        BuildPopupShell();
        if (_popupRoot == null)
            return;

        _currentData = _selectedData;
        _currentObjectInfo = _selectedObjectInfo;
        _popupRoot.SetActive(true);
        _popupRoot.transform.SetAsLastSibling();
        _popupPinnedData = _currentData;
        _popupPinnedObjectInfo = _currentObjectInfo;
        RegisterPopupOpen(true);
        EnsurePopupSections();
        RefreshAllSections();
    }

    private void ClosePopup(bool syncFromWindow = true)
    {
        if (_popupRoot != null)
            _popupRoot.SetActive(false);
        ClearActiveRouteEditor();
        HidePopupRouteSubheader();
        RegisterPopupOpen(false);
        _popupPinnedData = null;
        _popupPinnedObjectInfo = null;
        if (syncFromWindow)
            TrySyncFromWindow(force: true);
    }

    private void RegisterPopupOpen(bool open)
    {
        if (open == _popupRegisteredOpen)
            return;

        _popupRegisteredOpen = open;
        if (open)
        {
            _openPopupCount++;
            if (_popupPanelRt != null && !_openPopupPanels.Contains(_popupPanelRt))
                _openPopupPanels.Add(_popupPanelRt);
            if (!_openPopupInstances.Contains(this))
                _openPopupInstances.Add(this);
        }
        else
        {
            _openPopupCount = Mathf.Max(0, _openPopupCount - 1);
            if (_popupPanelRt != null)
                _openPopupPanels.Remove(_popupPanelRt);
            _openPopupInstances.Remove(this);
        }
    }

    private void BuildPopupShell()
    {
        if (_popupRoot != null)
            return;

        var canvas = GetComponentInParent<Canvas>();
        var parent = canvas != null ? canvas.transform : transform;

        _popupRoot = new GameObject("LogisticsPopup", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        _popupRoot.transform.SetParent(parent, false);
        _popupRoot.SetActive(false);
        var overlayRt = _popupRoot.GetComponent<RectTransform>();
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.pivot = new Vector2(0.5f, 0.5f);
        overlayRt.offsetMin = Vector2.zero;
        overlayRt.offsetMax = Vector2.zero;
        var overlayImage = _popupRoot.GetComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.01f);
        overlayImage.raycastTarget = false;
        var canvasGroup = _popupRoot.GetComponent<CanvasGroup>();
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(PopupClickBlocker));
        panelGo.transform.SetParent(_popupRoot.transform, false);
        _popupPanelRt = panelGo.GetComponent<RectTransform>();
        _popupPanelRt.anchorMin = new Vector2(0.5f, 0.5f);
        _popupPanelRt.anchorMax = new Vector2(0.5f, 0.5f);
        _popupPanelRt.pivot = new Vector2(0.5f, 0.5f);
        _popupPanelRt.sizeDelta = new Vector2(Mathf.Max(620f, _sectionWidth + 54f), 700f);
        _popupPanelRt.anchoredPosition = Vector2.zero;

        var panelImage = panelGo.GetComponent<Image>();
        panelImage.color = PanelOuterColor;
        panelImage.raycastTarget = true;
        var panelLayout = panelGo.GetComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(0, 0, 0, 14);
        panelLayout.spacing = 0f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        var header = new GameObject("Header", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        header.transform.SetParent(panelGo.transform, false);
        header.GetComponent<Image>().color = HeaderBarColor;
        var headerElement = header.GetComponent<LayoutElement>();
        headerElement.minHeight = 34f;
        headerElement.preferredHeight = 34f;
        headerElement.flexibleHeight = 0f;
        var headerLayout = header.GetComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(10, 6, 4, 4);
        headerLayout.spacing = 6f;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        headerLayout.childAlignment = TextAnchor.MiddleCenter;

        _popupTitleIcon = AddObjectIcon(header.transform, null, 22f);
        _popupTitle = MakeTMP(header.transform, "Logistics", 14f, PrimaryTextColor);
        _popupTitle.fontStyle = FontStyles.Bold;
        _popupTitle.alignment = TextAlignmentOptions.MidlineLeft;
        var titleElement = _popupTitle.gameObject.AddComponent<LayoutElement>();
        titleElement.minHeight = 24f;
        titleElement.preferredHeight = 24f;
        titleElement.flexibleHeight = 0f;
        titleElement.flexibleWidth = 1f;

        var switchRow = new GameObject("BodySwitchRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        switchRow.transform.SetParent(header.transform, false);
        _popupBodySwitchRow = switchRow.GetComponent<RectTransform>();
        var switchLayout = switchRow.GetComponent<HorizontalLayoutGroup>();
        switchLayout.padding = new RectOffset(0, 2, 0, 0);
        switchLayout.spacing = 3f;
        switchLayout.childControlWidth = true;
        switchLayout.childControlHeight = true;
        switchLayout.childForceExpandWidth = false;
        switchLayout.childForceExpandHeight = false;
        switchLayout.childAlignment = TextAnchor.MiddleRight;
        var switchElement = switchRow.GetComponent<LayoutElement>();
        switchElement.minHeight = 24f;
        switchElement.preferredHeight = 24f;
        switchElement.minWidth = 0f;
        switchElement.preferredWidth = 0f;
        switchElement.flexibleWidth = 0f;
        switchElement.flexibleHeight = 0f;

        AddSmallButton(header.transform, "X", _runtimeStyle.RemoveButtonColor, () => ClosePopup(), 28f);

        var routeSubheader = new GameObject("RouteSubheader", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        routeSubheader.transform.SetParent(panelGo.transform, false);
        routeSubheader.SetActive(false);
        _popupRouteSubheader = routeSubheader.GetComponent<RectTransform>();
        var routeSubheaderImage = routeSubheader.GetComponent<Image>();
        routeSubheaderImage.color = RouteSubheaderBarColor;
        routeSubheaderImage.raycastTarget = true;
        var routeSubheaderElement = routeSubheader.GetComponent<LayoutElement>();
        routeSubheaderElement.minHeight = 30f;
        routeSubheaderElement.preferredHeight = 30f;
        routeSubheaderElement.flexibleHeight = 0f;
        var routeSubheaderLayout = routeSubheader.GetComponent<HorizontalLayoutGroup>();
        routeSubheaderLayout.padding = new RectOffset(10, 6, 3, 3);
        routeSubheaderLayout.spacing = 8f;
        routeSubheaderLayout.childControlWidth = true;
        routeSubheaderLayout.childControlHeight = true;
        routeSubheaderLayout.childForceExpandWidth = false;
        routeSubheaderLayout.childForceExpandHeight = false;
        routeSubheaderLayout.childAlignment = TextAnchor.MiddleCenter;

        var scrollGo = new GameObject("BodyScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
        scrollGo.transform.SetParent(panelGo.transform, false);
        scrollGo.GetComponent<Image>().color = PanelInnerColor;
        var scrollLayout = scrollGo.GetComponent<LayoutElement>();
        scrollLayout.flexibleHeight = 1f;
        scrollLayout.minHeight = 420f;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        var viewportRt = viewportGo.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = new Vector2(-10f, 0f);
        var viewportImage = viewportGo.GetComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        viewportImage.raycastTarget = false;
        viewportGo.GetComponent<Mask>().showMaskGraphic = false;

        var scrollbar = BuildPopupScrollbar(scrollGo.transform);

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewportGo.transform, false);
        _popupContentRt = contentGo.GetComponent<RectTransform>();
        _popupContentRt.anchorMin = new Vector2(0f, 1f);
        _popupContentRt.anchorMax = new Vector2(1f, 1f);
        _popupContentRt.pivot = new Vector2(0.5f, 1f);
        _popupContentRt.offsetMin = Vector2.zero;
        _popupContentRt.offsetMax = Vector2.zero;

        var contentLayout = contentGo.GetComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(14, 20, 8, 0);
        contentLayout.spacing = 8f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        var fitter = contentGo.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = viewportRt;
        scroll.content = _popupContentRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        scroll.verticalScrollbarSpacing = -2f;
        _popupScroll = scroll;
        scrollGo.AddComponent<SmoothPopupScroll>().Attach(scroll, _font);
    }

    private Scrollbar BuildPopupScrollbar(Transform parent)
    {
        var scrollbarGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        scrollbarGo.transform.SetParent(parent, false);
        var rt = scrollbarGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-3f, 0f);
        rt.sizeDelta = new Vector2(6f, -6f);

        var trackImage = scrollbarGo.GetComponent<Image>();
        trackImage.color = WithAlpha(BorderColor, 0.3f);
        trackImage.raycastTarget = true;

        var slidingAreaGo = new GameObject("SlidingArea", typeof(RectTransform));
        slidingAreaGo.transform.SetParent(scrollbarGo.transform, false);
        var slidingRt = slidingAreaGo.GetComponent<RectTransform>();
        slidingRt.anchorMin = Vector2.zero;
        slidingRt.anchorMax = Vector2.one;
        slidingRt.offsetMin = new Vector2(0f, 2f);
        slidingRt.offsetMax = new Vector2(0f, -2f);

        var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleGo.transform.SetParent(slidingAreaGo.transform, false);
        var handleRt = handleGo.GetComponent<RectTransform>();
        handleRt.anchorMin = Vector2.zero;
        handleRt.anchorMax = Vector2.one;
        handleRt.offsetMin = Vector2.zero;
        handleRt.offsetMax = Vector2.zero;

        var handleImage = handleGo.GetComponent<Image>();
        handleImage.color = WithAlpha(TertiaryTextColor, 0.52f);
        handleImage.raycastTarget = true;

        var scrollbar = scrollbarGo.GetComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.targetGraphic = handleImage;
        scrollbar.handleRect = handleRt;
        scrollbar.numberOfSteps = 0;
        scrollbar.colors = new ColorBlock
        {
            normalColor = WithAlpha(TertiaryTextColor, 0.52f),
            highlightedColor = WithAlpha(SecondaryTextColor, 0.76f),
            pressedColor = WithAlpha(PrimaryTextColor, 0.92f),
            selectedColor = WithAlpha(SecondaryTextColor, 0.76f),
            disabledColor = WithAlpha(DisabledTextColor, 0.24f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };
        return scrollbar;
    }

    private void EnsurePopupSections()
    {
        if (_popupBuilt || _popupContentRt == null)
            return;

        _parentRt = _popupContentRt;
        _routesSection = CreatePopupSection("ROUTES", "Shipping Lanes");
        _routesSection.HideHeader();
        _getSection = _routesSection;
        _sendSection = _routesSection;
        _popupBuilt = true;
    }

    private LogisticsSection CreatePopupSection(string primary, string secondary)
    {
        var section = new LogisticsSection(_popupContentRt, FormatThemeSectionTitle(primary, secondary), _font, _sectionWidth,
            _styleButton, _styleIcon, _styleExpand, _styleCollapse,
            _runtimeStyle.HeaderHeight, _runtimeStyle.HeaderFontSize, _runtimeStyle.HeaderBackgroundColor,
            _runtimeStyle.HeaderTextColor, _runtimeStyle.HeaderDividerColor, new Color(0f, 0f, 0f, 0f),
            _runtimeStyle.HasHeaderButtonColors ? _runtimeStyle.HeaderButtonColors : null);
        section.SetExpanded(true);
        _sections.Add(section);
        return section;
    }

    private LogisticsSection CreateInlineSection(Transform parent, string primary, string secondary, bool expanded = true)
    {
        var section = new LogisticsSection(parent, FormatThemeSectionTitle(primary, secondary), _font, _sectionWidth,
            _styleButton, _styleIcon, _styleExpand, _styleCollapse,
            _runtimeStyle.HeaderHeight, _runtimeStyle.HeaderFontSize, _runtimeStyle.HeaderBackgroundColor,
            _runtimeStyle.HeaderTextColor, _runtimeStyle.HeaderDividerColor, new Color(0f, 0f, 0f, 0f),
            _runtimeStyle.HasHeaderButtonColors ? _runtimeStyle.HeaderButtonColors : null);
        section.SetExpanded(expanded);
        section.ClearContent();
        return section;
    }

    private void CaptureRuntimeStyle(ObjectInfoCollapseSections oics, Button headerButton)
    {
        _runtimeStyle.Font = _font;
        if (headerButton != null)
        {
            _runtimeStyle.HeaderButtonColors = headerButton.colors;
            _runtimeStyle.HasHeaderButtonColors = true;
        }

        TryCaptureHeaderTypography(oics, SectionIndexSpacecraft, "SPACECRAFT");
        TryCaptureLaunchListRowStyle(_objectInfoWindow?.launchVehicleList);
        LogCapturedSectionStyle(oics, SectionIndexSpacecraft, "SPACECRAFT", _objectInfoWindow?.rocketList);
        LogCapturedSectionStyle(oics, SectionIndexLaunchVehicle, "LAUNCH VEHICLES", _objectInfoWindow?.launchVehicleList);
    }

    private void ApplyLogisticsTheme()
    {
        _runtimeStyle.HeaderTextColor = AccentTextColor;
        _runtimeStyle.HeaderDividerColor = AccentLineColor;
        _runtimeStyle.HeaderBackgroundColor = WithAlpha(VoidColor, 0.34f);
        _runtimeStyle.RowBackgroundColor = RowBgColor;
        _runtimeStyle.RowTextColor = PrimaryTextColor;
        _runtimeStyle.ActionButtonColor = AccentButtonColor;
        _runtimeStyle.ConfirmButtonColor = ConfirmButtonColor;
        _runtimeStyle.BackButtonColor = BackButtonColor;
        _runtimeStyle.RemoveButtonColor = RemoveButtonColor;
        _runtimeStyle.SmallButtonColor = CountButtonColor;
        _runtimeStyle.SmallButtonPositiveColor = CountButtonPositiveColor;
        _runtimeStyle.ToggleOnColor = ToggleOnRowColor;
        _runtimeStyle.ToggleOffColor = ToggleOffRowColor;
        _runtimeStyle.HeaderButtonColors = MakeThemeColorBlock(AccentTextColor, PrimaryTextColor, EnginePressedColor);
        _runtimeStyle.HasHeaderButtonColors = true;
    }

    private static ColorBlock MakeThemeColorBlock(Color normal, Color highlighted, Color pressed)
    {
        return new ColorBlock
        {
            normalColor = normal,
            highlightedColor = highlighted,
            pressedColor = pressed,
            selectedColor = highlighted,
            disabledColor = WithAlpha(DisabledTextColor, 0.65f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };
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
            if (LogisticsObserver.VerboseLoggingEnabled)
                LogisticsObserver.LogWarning($"UISTYLE capture failed for {sectionName}: {ex.Message}");
        }
    }

    private string FormatThemeSectionTitle(string primary, string secondary)
    {
        var primaryHex = ColorUtility.ToHtmlStringRGBA(_runtimeStyle.HeaderTextColor);
        var subtitleColor = Color.Lerp(_runtimeStyle.HeaderTextColor, SubtleTextColor, 0.55f);
        var subtitleHex = ColorUtility.ToHtmlStringRGBA(subtitleColor);
        return $"<color=#{primaryHex}>{primary}</color> <size=78%><color=#{subtitleHex}>- {secondary}</color></size>";
    }

    private string FormatSectionTitle(string primary, string secondary)
    {
        var subtitleColor = Color.Lerp(_runtimeStyle.HeaderTextColor, WithAlpha(TertiaryTextColor, _runtimeStyle.HeaderTextColor.a), 0.35f);
        var subtitleHex = ColorUtility.ToHtmlStringRGBA(subtitleColor);
        return $"{primary} <size=82%><color=#{subtitleHex}>— {secondary}</color></size>";
    }

    public void RefreshData(ObjectInfoData oid)
    {
        var newOi = oid?.ObjectInfo;
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var playerOwned = oid != null && newOi != null && player != null && oid.company == player;
        _selectedData = playerOwned ? oid : null;
        _selectedObjectInfo = playerOwned ? newOi : null;
        UpdateLauncherVisibility();

        if (_popupRoot != null && _popupRoot.activeSelf && _popupPinnedObjectInfo != null && !SameObjectInfo(newOi, _popupPinnedObjectInfo))
        {
            LogisticsObserver.LogVerbose($"UI popup pinned: keeping \"{_popupPinnedObjectInfo.ObjectName}\" focused while selected panel is \"{newOi?.ObjectName ?? "NULL"}\"");
            return;
        }

        if (!playerOwned)
        {
            if (_popupRoot == null || !_popupRoot.activeSelf)
            {
                _currentData = null;
                _currentObjectInfo = null;
            }
            return;
        }

        var newName = newOi?.ObjectName ?? "NULL";
        var newId = newOi?.id ?? -1;
        var prevName = _currentObjectInfo?.ObjectName ?? "null";
        var prevId = _currentObjectInfo?.id ?? -1;
        LogisticsObserver.LogVerbose($"RefreshData: \"{newName}\" (id={newId}), _built={_built}, prev=\"{prevName}\" (id={prevId})");

        if (LogisticsObserver.VerboseLoggingEnabled && newOi != null && _currentObjectInfo != null && newId == prevId && newName != prevName)
            LogisticsObserver.LogWarning($"DIAG RefreshData: SAME id ({newId}) but DIFFERENT name! prev=\"{prevName}\" new=\"{newName}\"");

        if (newOi != null)
        {
            var dictData = Data.LogisticsNetwork.Get(newOi);
            if (dictData != null)
            {
                if (LogisticsObserver.VerboseLoggingEnabled)
                {
                var storedOiName = (dictData.ObjectInfo as ObjectInfo)?.ObjectName ?? "NULL";
                if (storedOiName != newName)
                    LogisticsObserver.LogWarning($"DIAG RefreshData: dict entry id={newId} has storedOI=\"{storedOiName}\" but incoming OI name=\"{newName}\" — MISMATCH!");
                LogisticsObserver.LogVerbose($"DIAG RefreshData: dict data for id={newId}: {dictData.requests.Count}req {dictData.providers.Count}prov");
                }
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
        var currentId = _selectedObjectInfo?.id ?? -1;
        var liveCompany = liveData?.company;
        var currentCompany = _selectedData?.company;

        if (!force && liveId == currentId && liveCompany == currentCompany)
            return;

        LogisticsObserver.LogVerbose($"UI sync-from-window: force={force} live=\"{liveOi?.ObjectName ?? "NULL"}\"(id={liveId}) selected=\"{_selectedObjectInfo?.ObjectName ?? "NULL"}\"(id={currentId})");
        RefreshData(liveData);
    }

    private void ClearForNonPlayerCompany()
    {
        if (!_popupBuilt)
            return;

        foreach (var section in _sections)
            section.ClearContent();

        (_routesSection ?? _getSection)?.AddTextRow("Logistics are only available for the player company.", _font, 13f, DisabledTextColor);
        if (_parentRt != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_parentRt);
    }

    private void RefreshAllSections()
    {
        if (!_popupBuilt || _popupRoot == null || !_popupRoot.activeSelf || _currentObjectInfo == null) return;
        SetObjectIcon(_popupTitleIcon, _currentObjectInfo);
        if (_popupTitle != null)
            _popupTitle.text = $"{CompactObjectName(_currentObjectInfo)} Logistics";
        RebuildBodySwitchButtons();
        if (TryGetActiveRouteEditor(out var activeRoute))
            ShowRouteEditor(_routesSection, activeRoute);
        else
            BuildRoutesSection();
        if (_parentRt != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_parentRt);
        if (_popupPanelRt != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_popupPanelRt);
    }

    private bool TryGetActiveRouteEditor(out Data.LogisticsRouteRecord route)
    {
        route = null;
        if (_activeRouteEditorRouteId <= 0 || _currentObjectInfo == null)
            return false;

        var data = Data.LogisticsNetwork.Get(_currentObjectInfo);
        route = data?.routes?.FirstOrDefault(candidate => candidate != null
            && candidate.routeId == _activeRouteEditorRouteId);
        if (route != null)
            return true;

        ClearActiveRouteEditor();
        return false;
    }

    private void ClearActiveRouteEditor()
    {
        _activeRouteEditorRouteId = -1;
        SetRouteEditorMainPageVisible(false);
    }

    private void SetRouteEditorMainPageVisible(bool visible)
    {
        _routeEditorMainPageVisible = visible;
        if (visible)
        {
            var currentTime = CurrentGameTimeOrMinValue();
            if (currentTime != DateTime.MinValue)
                _lastRouteEditorRefreshTime = currentTime;
        }
        else
        {
            _lastRouteEditorRefreshTime = DateTime.MinValue;
            _nextRouteEditorLiveRefreshAt = 0f;
        }
    }

    private void RefreshOpenRouteEditorOnTimeAdvance()
    {
        if (!_popupBuilt
            || _popupRoot == null
            || !_popupRoot.activeSelf
            || !_routeEditorMainPageVisible
            || _routesSection == null
            || _activeRouteEditorRouteId <= 0)
            return;

        var currentTime = CurrentGameTimeOrMinValue();
        if (currentTime == DateTime.MinValue)
            return;

        if (_lastRouteEditorRefreshTime == DateTime.MinValue)
        {
            _lastRouteEditorRefreshTime = currentTime;
            return;
        }

        if (currentTime == _lastRouteEditorRefreshTime || Time.unscaledTime < _nextRouteEditorLiveRefreshAt)
            return;

        if (!TryGetActiveRouteEditor(out var route))
            return;

        var scrollPosition = _popupScroll != null ? _popupScroll.verticalNormalizedPosition : 1f;
        _lastRouteEditorRefreshTime = currentTime;
        _nextRouteEditorLiveRefreshAt = Time.unscaledTime + RouteEditorLiveRefreshIntervalSeconds;
        ShowRouteEditor(_routesSection, route);
        RestorePopupScrollPosition(scrollPosition);
    }

    private static DateTime CurrentGameTimeOrMinValue()
    {
        return MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? DateTime.MinValue;
    }

    private void RestorePopupScrollPosition(float scrollPosition)
    {
        if (_popupScroll == null)
            return;

        Canvas.ForceUpdateCanvases();
        _popupScroll.verticalNormalizedPosition = Mathf.Clamp01(scrollPosition);
        _popupScroll.velocity = Vector2.zero;
    }

    private void ShowPopupRouteSubheader(LogisticsSection section, Data.LogisticsRouteRecord route)
    {
        if (_popupRouteSubheader == null || route == null)
            return;

        ClearChildren(_popupRouteSubheader);
        _popupRouteSubheader.gameObject.SetActive(true);

        AddRouteStatusDot(_popupRouteSubheader, route, 14f);
        AddRouteHeaderRow(_popupRouteSubheader, route, true);

        AddSmallButton(_popupRouteSubheader, "\u2190 Back", _runtimeStyle.BackButtonColor, () =>
        {
            ClearActiveRouteEditor();
            HidePopupRouteSubheader();
            BuildRoutesSection();
            RebuildSectionLayout(section);
        }, 70f);

        AddSmallButton(_popupRouteSubheader, route.isActive ? "Pause Route" : "Resume Route",
            route.isActive ? _runtimeStyle.ToggleOffColor : _runtimeStyle.RemoveButtonColor, () =>
        {
            SetRouteActive(route, !route.isActive);
            ShowRouteEditor(section, route);
        }, 104f);
    }

    private void HidePopupRouteSubheader()
    {
        if (_popupRouteSubheader == null)
            return;

        ClearChildren(_popupRouteSubheader);
        _popupRouteSubheader.gameObject.SetActive(false);
    }

    private void RebuildBodySwitchButtons()
    {
        if (_popupBodySwitchRow == null)
            return;

        ClearChildren(_popupBodySwitchRow);
        var targets = BuildBodySwitchTargets(_currentObjectInfo);
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var visibleTargets = targets
            .Where(target => CanOpenLogisticsFor(target, player))
            .ToList();

        foreach (var target in visibleTargets)
        {
            var captured = target;
            AddObjectSwitchButton(_popupBodySwitchRow, captured, () => SwapPopupToObject(captured));
        }

        var width = visibleTargets.Count <= 0 ? 0f : visibleTargets.Count * 24f + Math.Max(0, visibleTargets.Count - 1) * 3f + 2f;
        var layout = _popupBodySwitchRow.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.minWidth = width;
            layout.preferredWidth = width;
        }
        _popupBodySwitchRow.gameObject.SetActive(visibleTargets.Count > 0);
    }

    private void SwapPopupToObject(ObjectInfo target)
    {
        if (target == null || SameObjectInfo(target, _currentObjectInfo))
            return;

        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var targetData = target.GetObjectInfoData(player);
        if (targetData == null || targetData.company != player)
            return;

        BuildPopupShell();
        if (_popupRoot == null)
            return;

        _currentData = targetData;
        _currentObjectInfo = target;
        ClearActiveRouteEditor();
        _popupPinnedData = targetData;
        _popupPinnedObjectInfo = target;
        _popupRoot.SetActive(true);
        _popupRoot.transform.SetAsLastSibling();
        RegisterPopupOpen(true);
        EnsurePopupSections();
        if (_popupScroll != null)
            _popupScroll.verticalNormalizedPosition = 1f;
        RefreshAllSections();
    }

    private static bool CanOpenLogisticsFor(ObjectInfo target, Company player)
    {
        if (target == null || player == null)
            return false;
        var data = target.GetObjectInfoData(player);
        return data != null && data.company == player;
    }

    private List<ObjectInfo> BuildBodySwitchTargets(ObjectInfo current)
    {
        var result = new List<ObjectInfo>();
        var seen = new HashSet<int>();
        var objects = MonoBehaviourSingleton<ObjectInfoManager>.Instance?.allObjectInfos?
            .Where(oi => oi != null)
            .ToList() ?? new List<ObjectInfo>();
        var objectOrder = BuildObjectOrderMap(objects);

        void AddTarget(ObjectInfo target)
        {
            if (target == null
                || target.objectTypes == global::Data.EObjectTypes.SolarSystem
                || IsExcludedBodySwitchTarget(target)
                || SameObjectInfo(target, current)
                || !seen.Add(target.id))
                return;
            result.Add(target);
        }

        void AddBodyAndOrbit(ObjectInfo body)
        {
            if (body == null
                || body.objectTypes == global::Data.EObjectTypes.SolarSystem
                || IsExcludedBodySwitchTarget(body))
                return;
            AddTarget(body);
            AddTarget(body.LowOrbitCustom?.GetObjectInfo());
        }

        var body = SwitchBodyForObject(current);
        if (IsSolarSwitchContext(current, body))
        {
            foreach (var target in objects
                         .Where(candidate => IsSolarPrimaryClusterObject(candidate, body))
                         .OrderBy(SwitchSortDistance)
                         .ThenBy(candidate => ObjectOrder(candidate, objectOrder)))
                AddTarget(target);
            return result;
        }

        AddBodyAndOrbit(body?.parentObjectInfo);
        AddBodyAndOrbit(body);

        var children = objects
            .Where(candidate => IsDirectChildBody(candidate, body) && !IsExcludedBodySwitchTarget(candidate))
            .OrderBy(SwitchSortDistance)
            .ThenBy(candidate => ObjectOrder(candidate, objectOrder))
            .ToList();
        var suppressChildOrbits = children.Count >= BodySwitcherCrowdedChildThreshold;
        foreach (var child in children)
        {
            if (suppressChildOrbits)
                AddTarget(child);
            else
                AddBodyAndOrbit(child);
        }

        return result;
    }

    private static Dictionary<int, int> BuildObjectOrderMap(List<ObjectInfo> objects)
    {
        var result = new Dictionary<int, int>();
        if (objects == null)
            return result;
        for (var i = 0; i < objects.Count; i++)
        {
            var oi = objects[i];
            if (oi != null && !result.ContainsKey(oi.id))
                result[oi.id] = i;
        }
        return result;
    }

    private static int ObjectOrder(ObjectInfo oi, Dictionary<int, int> order)
    {
        if (oi != null && order != null && order.TryGetValue(oi.id, out var value))
            return value;
        return int.MaxValue;
    }

    private static bool IsSolarSwitchContext(ObjectInfo current, ObjectInfo body)
    {
        return IsSolarRootObject(current) || IsSolarRootObject(body);
    }

    private static bool IsSolarRootObject(ObjectInfo oi)
    {
        if (oi == null)
            return false;
        if (oi.objectTypes == global::Data.EObjectTypes.SolarSystem
            || oi.objectTypes == global::Data.EObjectTypes.SolarOrbit)
            return true;
        return oi.parentObjectInfo == null && LooksLikeSolarName(oi);
    }

    private static bool LooksLikeSolarName(ObjectInfo oi)
    {
        var name = oi?.ObjectName ?? "";
        return name.IndexOf("sun", System.StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("solar", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsSolarPrimaryClusterObject(ObjectInfo candidate, ObjectInfo solarRoot)
    {
        if (candidate == null
            || candidate.objectTypes == global::Data.EObjectTypes.SolarSystem
            || IsExcludedBodySwitchTarget(candidate))
            return false;
        if (candidate.objectTypes == global::Data.EObjectTypes.Orbit)
            return false;
        if (candidate.objectTypes == global::Data.EObjectTypes.SolarOrbit)
            return true;
        if (IsSolarRootObject(candidate))
            return false;
        return IsSolarRootObject(candidate.parentObjectInfo)
            || (solarRoot != null && SameObjectInfo(candidate.parentObjectInfo, solarRoot));
    }

    private static double SwitchSortDistance(ObjectInfo oi)
    {
        if (oi == null)
            return double.MaxValue;
        if (oi.objectTypes == global::Data.EObjectTypes.SolarOrbit)
            return 0;
        if (oi.DistanceToCentralObjectAu > 0.000001)
            return oi.DistanceToCentralObjectAu;
        if (oi.DistanceToSunInAU > 0.000001)
            return oi.DistanceToSunInAU;
        return double.MaxValue;
    }

    private static ObjectInfo SwitchBodyForObject(ObjectInfo oi)
    {
        if (oi == null)
            return null;
        return (oi.objectTypes == global::Data.EObjectTypes.Orbit
                || oi.objectTypes == global::Data.EObjectTypes.SolarOrbit)
               && oi.parentObjectInfo != null
            ? oi.parentObjectInfo
            : oi;
    }

    private static bool IsExcludedBodySwitchTarget(ObjectInfo oi)
    {
        if (oi == null || oi.IsInGameDestroy || IsAsteroidOrComet(oi))
            return true;

        return oi.objectTypes == global::Data.EObjectTypes.Orbit
            && IsAsteroidOrComet(oi.parentObjectInfo);
    }

    private static bool IsAsteroidOrComet(ObjectInfo oi)
    {
        return oi != null
            && (oi.objectTypes == global::Data.EObjectTypes.Asteroid
                || oi.objectTypes == global::Data.EObjectTypes.Comet);
    }

    private static bool IsDirectChildBody(ObjectInfo candidate, ObjectInfo body)
    {
        return candidate != null
            && body != null
            && candidate.objectTypes != global::Data.EObjectTypes.Orbit
            && candidate.objectTypes != global::Data.EObjectTypes.SolarSystem
            && SameObjectInfo(candidate.parentObjectInfo, body);
    }

    private void RebuildSectionLayout(LogisticsSection section)
    {
        if (section == null)
            return;

        LayoutRebuilder.ForceRebuildLayoutImmediate(section.ContentArea);
        if (_parentRt != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_parentRt);
        if (_popupPanelRt != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_popupPanelRt);
    }

    private void BuildRoutesSection()
    {
        SetRouteEditorMainPageVisible(false);
        HidePopupRouteSubheader();
        _routesSection.ClearContent();
        if (_currentObjectInfo == null) return;

        var data = Data.LogisticsNetwork.GetOrCreate(_currentObjectInfo);
        var routes = data?.routes ?? new List<Data.LogisticsRouteRecord>();

        AddBigButton(_routesSection.ContentArea, "+ Add Route", _runtimeStyle.ActionButtonColor, () =>
        {
            ShowRouteDestinationPicker(_routesSection);
        });

        if (routes.Count == 0)
        {
            _routesSection.AddTextRow("No routes configured.", _font, 13f, DisabledTextColor);
            return;
        }

        var sortedRoutes = routes
            .Where(route => route != null)
            .OrderBy(route => ObjectName(route.destinationObjectId), System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var routeIndex = 0; routeIndex < sortedRoutes.Count; routeIndex++)
        {
            var route = sortedRoutes[routeIndex];
            var routeRef = route;
            var destinationOi = ResolveObjectInfo(route.destinationObjectId);
            var destination = CompactObjectName(destinationOi, ObjectName(route.destinationObjectId));
            var routeGroup = MakeRouteOverviewGroup(_routesSection.ContentArea);
            var routeParent = routeGroup.transform;

            var row = MakeHLRow(routeParent, 30f, 6);
            row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
            AddRouteStatusDot(row.transform, route);
            AddObjectIcon(row.transform, destinationOi, 22f);
            var routeState = route.isActive ? "" : "  paused";
            var label = MakeTMP(row.transform, $"{destination}{routeState}", _runtimeStyle.RowFontSize, EngineAccentColor);
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Overflow;
            var le = label.gameObject.AddComponent<LayoutElement>();
            if (route.uiCollapsed)
            {
                var measured = label.GetPreferredValues(label.text, RouteHeaderNameMaxWidth, 22f).x;
                le.minWidth = Mathf.Clamp(measured + 4f, 36f, RouteHeaderNameMaxWidth);
                le.preferredWidth = le.minWidth;
                le.flexibleWidth = 0f;
                AddCollapsedRouteResourceIconStrip(row.transform, route);
                AddFlexibleSpacer(row.transform);
            }
            else
            {
                le.flexibleWidth = 1f;
                le.preferredWidth = 0f;
            }

            AddSmallButton(row.transform, "Open", _runtimeStyle.ActionButtonColor, () =>
            {
                ShowRouteEditor(_routesSection, routeRef);
            }, 70f);
            AddSmallButton(row.transform, route.isActive ? "Pause" : "Run", route.isActive ? _runtimeStyle.ToggleOffColor : _runtimeStyle.ToggleOnColor, () =>
            {
                SetRouteActive(routeRef, !routeRef.isActive);
                BuildRoutesSection();
                RebuildSectionLayout(_routesSection);
            }, 58f);
            AddSmallButton(row.transform, "X", _runtimeStyle.RemoveButtonColor, () =>
            {
                Data.LogisticsNetwork.RemoveRoute(_currentObjectInfo, routeRef.routeId);
                BuildRoutesSection();
                RebuildSectionLayout(_routesSection);
            }, 38f);

            MakeRouteOverviewHeaderToggle(row, routeRef);

            if (!route.uiCollapsed)
            {
                var assigned = Data.LogisticsNetwork.GetAllGhostCraft()
                    .Where(c => c != null && c.assignedRouteId == route.routeId)
                    .ToList();
                var detailParent = MakeRouteOverviewDetailGroup(routeParent).transform;
                AddRouteHealthRow(detailParent, route);
                AddRouteResourceIconRow(detailParent, route);
                AddRouteShipSummaryRows(detailParent, route, assigned);
                AddRouteLaunchSummaryRows(detailParent, route);
            }

            if (routeIndex < sortedRoutes.Count - 1)
                AddVerticalSpacer(_routesSection.ContentArea, RouteOverviewGroupGap);
        }
    }

    private void MakeRouteOverviewHeaderToggle(GameObject row, Data.LogisticsRouteRecord route)
    {
        if (row == null || route == null)
            return;

        var button = MakeRowButton(row);
        button.onClick.AddListener(() =>
        {
            route.uiCollapsed = !route.uiCollapsed;
            BuildRoutesSection();
            RebuildSectionLayout(_routesSection);
        });
    }

    private GameObject MakeRouteOverviewGroup(Transform parent)
    {
        var group = new GameObject("RouteGroup", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(Image),
            typeof(LayoutElement), typeof(ContentSizeFitter));
        group.transform.SetParent(parent, false);
        var image = group.GetComponent<Image>();
        image.color = WithAlpha(VoidColor, 0.96f);
        image.raycastTarget = false;
        var layout = group.GetComponent<LayoutElement>();
        layout.minHeight = 1f;
        layout.flexibleWidth = 1f;

        var vlg = group.GetComponent<VerticalLayoutGroup>();
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.spacing = 2f;
        vlg.padding = new RectOffset(0, 0, 0, 2);

        var fitter = group.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        return group;
    }

    private GameObject MakeRouteOverviewDetailGroup(Transform parent)
    {
        var shell = new GameObject("RouteDetailsShell", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement),
            typeof(ContentSizeFitter));
        shell.transform.SetParent(parent, false);

        var shellLayout = shell.GetComponent<LayoutElement>();
        shellLayout.minHeight = 1f;
        shellLayout.flexibleWidth = 1f;

        var shellHlg = shell.GetComponent<HorizontalLayoutGroup>();
        shellHlg.childForceExpandWidth = false;
        shellHlg.childForceExpandHeight = false;
        shellHlg.childControlWidth = true;
        shellHlg.childControlHeight = true;
        shellHlg.childAlignment = TextAnchor.UpperLeft;
        shellHlg.spacing = 0f;
        shellHlg.padding = new RectOffset(0, 0, 0, 0);

        var shellFitter = shell.GetComponent<ContentSizeFitter>();
        shellFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        shellFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        AddSpacer(shell.transform, RouteOverviewDetailPanelIndent);

        var group = new GameObject("RouteDetails", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(Image), typeof(LayoutElement),
            typeof(ContentSizeFitter));
        group.transform.SetParent(shell.transform, false);
        var image = group.GetComponent<Image>();
        image.color = WithAlpha(VoidColor, 0.98f);
        image.raycastTarget = false;

        var layout = group.GetComponent<LayoutElement>();
        layout.minHeight = 1f;
        layout.flexibleWidth = 1f;

        var vlg = group.GetComponent<VerticalLayoutGroup>();
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.spacing = 2f;
        vlg.padding = new RectOffset(0, Mathf.RoundToInt(RouteOverviewDetailRightInset), 0,
            Mathf.RoundToInt(RouteOverviewDetailBottomInset));

        var fitter = group.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        return group;
    }

    private static bool IsRouteOverviewDetailParent(Transform parent)
    {
        return parent != null && parent.gameObject != null && parent.gameObject.name == "RouteDetails";
    }

    private static void ApplyRouteOverviewDetailRowPadding(GameObject row, Transform parent)
    {
        if (!IsRouteOverviewDetailParent(parent) || row == null)
            return;

        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        if (hlg == null)
            return;

        hlg.padding = new RectOffset(Mathf.RoundToInt(RouteOverviewDetailContentInset),
            hlg.padding.right, hlg.padding.top, hlg.padding.bottom);
    }

    private void AddRouteStatusDot(Transform parent, Data.LogisticsRouteRecord route, float fontSize = 13f)
    {
        var dot = MakeTMP(parent, "\u25CF", fontSize, RouteOverviewStatusColor(route));
        dot.alignment = TextAlignmentOptions.Midline;
        dot.enableWordWrapping = false;
        dot.raycastTarget = false;
        var layout = dot.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = 14f;
        layout.preferredWidth = 14f;
        layout.minHeight = 22f;
        layout.preferredHeight = 22f;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;
    }

    private void AddRouteResourceIconRow(LogisticsSection section, Data.LogisticsRouteRecord route)
    {
        AddRouteResourceIconRow(section?.ContentArea, section, route);
    }

    private void AddRouteResourceIconRow(Transform parent, Data.LogisticsRouteRecord route)
    {
        AddRouteResourceIconRow(parent, _routesSection, route);
    }

    private void AddRouteResourceIconRow(Transform parent, LogisticsSection editorSection, Data.LogisticsRouteRecord route)
    {
        if (parent == null)
            return;

        var icons = (route?.resources ?? new List<Data.LogisticsRouteResourceRule>())
            .Select((rule, index) => new { Rule = rule, Index = index })
            .Where(item => item.Rule != null)
            .Select(item =>
            {
                var rd = item.Rule.ResourceDefinition ?? ResolveResource(item.Rule.resourceDef?.id);
                item.Rule.ResourceDefinition = rd;
                return new
                {
                    item.Index,
                    Resource = rd,
                    Icon = rd?.IconString,
                    SortName = ResourceSortName(rd, item.Rule.resourceDef?.id),
                    Color = item.Rule.isActive ? _runtimeStyle.RowTextColor : SubtleTextColor
                };
            })
            .Where(item => item.Resource != null && !string.IsNullOrWhiteSpace(item.Icon))
            .OrderBy(item => item.SortName, System.StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (icons.Count == 0)
            return;

        var row = MakeHLRow(parent, 28f, 6);
        row.GetComponent<Image>().color = RowBgMutedColor;
        ApplyRouteOverviewDetailRowPadding(row, parent);
        foreach (var icon in icons)
        {
            var capturedResource = icon.Resource;
            var capturedIndex = icon.Index;
            AddRouteResourceIconButton(row.transform, icon.Icon, icon.Color, () =>
            {
                ShowRouteResourceInput(editorSection, route, capturedResource, capturedIndex);
            });
        }
    }

    private void AddCollapsedRouteResourceIconStrip(Transform parent, Data.LogisticsRouteRecord route)
    {
        if (parent == null || route == null)
            return;

        var icons = (route.resources ?? new List<Data.LogisticsRouteResourceRule>())
            .Where(rule => rule != null)
            .Select(rule =>
            {
                var rd = rule.ResourceDefinition ?? ResolveResource(rule.resourceDef?.id);
                rule.ResourceDefinition = rd;
                return new
                {
                    Icon = rd?.IconString,
                    SortName = ResourceSortName(rd, rule.resourceDef?.id),
                    Color = rule.isActive ? _runtimeStyle.RowTextColor : DisabledTextColor
                };
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Icon))
            .OrderBy(item => item.SortName, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        const int maxIcons = 8;
        foreach (var icon in icons.Take(maxIcons))
            AddCollapsedRouteResourceIcon(parent, icon.Icon, icon.Color);

        if (icons.Count > maxIcons)
            AddCollapsedRouteSummaryText(parent, $"+{icons.Count - maxIcons}");
    }

    private TextMeshProUGUI AddCollapsedRouteResourceIcon(Transform parent, string icon, Color color)
    {
        var label = MakeTMP(parent, icon, 13f, color);
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        var layout = label.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = 18f;
        layout.preferredWidth = 18f;
        layout.minHeight = 18f;
        layout.preferredHeight = 18f;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;
        return label;
    }

    private TextMeshProUGUI AddCollapsedRouteSummaryText(Transform parent, string text)
    {
        var label = MakeTMP(parent, text, 10.5f, DisabledTextColor);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        var layout = label.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = 22f;
        layout.preferredWidth = 22f;
        layout.minHeight = 18f;
        layout.preferredHeight = 18f;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;
        return label;
    }

    private void AddRouteResourceIconButton(Transform parent, string icon, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject("RouteResourceIconBtn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGo.transform.SetParent(parent, false);
        var layout = btnGo.GetComponent<LayoutElement>();
        layout.minWidth = 30f;
        layout.preferredWidth = 30f;
        layout.minHeight = 24f;
        layout.preferredHeight = 24f;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;
        btnGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
        var btn = btnGo.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        btn.onClick.AddListener(onClick);

        var label = MakeTMP(btnGo.transform, icon, 18f, color);
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.rectTransform.offsetMin = new Vector2(1f, 0f);
        label.rectTransform.offsetMax = new Vector2(-1f, 0f);
    }

    private void AddRouteShipSummaryRows(LogisticsSection section, Data.LogisticsRouteRecord route, List<Data.GhostCraftRecord> assigned)
    {
        AddRouteShipSummaryRows(section?.ContentArea, route, assigned);
    }

    private void AddRouteShipSummaryRows(Transform parent, Data.LogisticsRouteRecord route, List<Data.GhostCraftRecord> assigned)
    {
        if (parent == null || route == null)
            return;
        assigned ??= Data.LogisticsNetwork.GetAllGhostCraft()
            .Where(c => c != null && c.assignedRouteId == route.routeId)
            .ToList();
        if (assigned.Count == 0)
            return;

        AddRouteAssetTableHeader(parent);
        var sourceId = route.sourceObjectId;
        foreach (var group in assigned.GroupBy(c => c.shipTypeId).OrderBy(g => GhostShipName(g.Key)))
        {
            var list = group.ToList();
            var ready = list.Count(c => c.status == Data.GhostCraftStatus.IdleAtHome && c.currentObjectId == sourceId);
            AddRouteAssetTableRow(parent, GhostShipIcon(group.Key), GhostShipName(group.Key), list.Count, ready, SubtleTextColor);
        }
    }

    private void AddRouteLaunchSummaryRows(LogisticsSection section, Data.LogisticsRouteRecord route)
    {
        AddRouteLaunchSummaryRows(section?.ContentArea, route);
    }

    private void AddRouteLaunchSummaryRows(Transform parent, Data.LogisticsRouteRecord route)
    {
        if (parent == null || route == null)
            return;

        var launchVehicles = Data.LogisticsNetwork.GetAllGhostLaunchVehicles()
            .Where(lv => lv != null && lv.assignedRouteId == route.routeId)
            .ToList();
        if (launchVehicles.Count == 0)
            return;

        AddRouteAssetTableHeader(parent);
        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? System.DateTime.Now;
        foreach (var group in launchVehicles.GroupBy(lv => lv.launchVehicleTypeId).OrderBy(g => LaunchVehicleName(g.Key)))
        {
            var list = group.ToList();
            var ready = list.Count(lv => Data.LogisticsNetwork.IsGhostLaunchVehicleReady(lv, now));
            AddRouteAssetTableRow(parent, LaunchVehicleIcon(group.Key), LaunchVehicleName(group.Key), list.Count, ready, SubtleTextColor);
        }
    }

    private GameObject AddRouteAssetTableRow(Transform parent, string icon, string assetName, int total, int ready,
        Color textColor, Color? rowColor = null, float height = 24f,
        Action<Transform> addInlineControls = null)
    {
        var row = MakeHLRow(parent, height, 4);
        row.GetComponent<Image>().color = rowColor ?? RowBgMutedColor;
        ApplyRouteOverviewDetailRowPadding(row, parent);
        AddTableIconCell(row.transform, icon);
        AddTableCell(row.transform, assetName, RouteAssetNameColumnWidth, 12.5f, textColor, TextAlignmentOptions.MidlineLeft);
        AddTableCell(row.transform, ready.ToString(), RouteAssetNumberColumnWidth, 12.5f, textColor, TextAlignmentOptions.MidlineRight);
        AddTableCell(row.transform, total.ToString(), RouteAssetNumberColumnWidth, 12.5f, textColor, TextAlignmentOptions.MidlineRight);
        if (addInlineControls != null)
        {
            AddSpacer(row.transform, FlightPlanModeColumnGap);
            AddTableCell(row.transform, "", 0f, 12.5f, textColor, TextAlignmentOptions.MidlineLeft, true);
            var controlsRow = MakeHLContainer(row.transform, 24f, FlightPlanModeButtonGap);
            var controlsHlg = controlsRow.GetComponent<HorizontalLayoutGroup>();
            if (controlsHlg != null)
                controlsHlg.childAlignment = TextAnchor.MiddleRight;
            var controlsLayout = controlsRow.GetComponent<LayoutElement>();
            if (controlsLayout != null)
            {
                controlsLayout.minWidth = FlightPlanModeControlsWidth;
                controlsLayout.preferredWidth = FlightPlanModeControlsWidth;
                controlsLayout.flexibleWidth = 0f;
            }
            addInlineControls(controlsRow.transform);
        }
        return row;
    }

    private GameObject AddRouteEmptyMessageRow(Transform parent, string message)
    {
        var row = MakeHLRow(parent, 28f, 0);
        row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
        var layout = row.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
            layout.padding = IsRouteOverviewDetailParent(parent)
                ? new RectOffset(Mathf.RoundToInt(RouteOverviewDetailContentInset), layout.padding.right,
                    layout.padding.top, layout.padding.bottom)
                : new RectOffset(8, 8, layout.padding.top, layout.padding.bottom);
        AddTableCell(row.transform, message, 0f, 13f, PrimaryTextColor, TextAlignmentOptions.MidlineLeft, true);
        return row;
    }

    private void AddRouteAssetTableHeader(Transform parent, bool includeFlightPlan = false)
    {
        var row = MakeHLRow(parent, 20f, 4);
        row.GetComponent<Image>().color = WithAlpha(BorderColor, 0.36f);
        ApplyRouteOverviewDetailRowPadding(row, parent);
        AddTableCell(row.transform, "", RouteAssetIconColumnWidth, 10.5f, TertiaryTextColor, TextAlignmentOptions.MidlineLeft);
        AddTableCell(row.transform, "Asset", RouteAssetNameColumnWidth, 10.5f, TertiaryTextColor, TextAlignmentOptions.MidlineLeft);
        AddTableCell(row.transform, "Ready", RouteAssetNumberColumnWidth, 10.5f, TertiaryTextColor, TextAlignmentOptions.MidlineRight);
        AddTableCell(row.transform, "Qty", RouteAssetNumberColumnWidth, 10.5f, TertiaryTextColor, TextAlignmentOptions.MidlineRight);
        if (includeFlightPlan)
        {
            AddSpacer(row.transform, FlightPlanModeColumnGap);
            AddTableCell(row.transform, "", 0f, 10.5f, TertiaryTextColor, TextAlignmentOptions.MidlineLeft, true);
            var planHeaderRow = MakeHLContainer(row.transform, 20f, FlightPlanModeButtonGap);
            var planHeaderHlg = planHeaderRow.GetComponent<HorizontalLayoutGroup>();
            if (planHeaderHlg != null)
                planHeaderHlg.childAlignment = TextAnchor.MiddleRight;
            var planHeaderLayout = planHeaderRow.GetComponent<LayoutElement>();
            if (planHeaderLayout != null)
            {
                planHeaderLayout.minWidth = FlightPlanModeControlsWidth;
                planHeaderLayout.preferredWidth = FlightPlanModeControlsWidth;
                planHeaderLayout.flexibleWidth = 0f;
            }
            AddTableCell(planHeaderRow.transform, "Plan", FlightPlanModeButtonGroupWidth, 10.5f, TertiaryTextColor,
                TextAlignmentOptions.MidlineLeft);
        }
    }

    private void ShowRouteDestinationPicker(LogisticsSection section)
    {
        ClearActiveRouteEditor();
        section.ClearContent();
        AddBigButton(section.ContentArea, "\u2190 Back", _runtimeStyle.BackButtonColor, () =>
        {
            ClearActiveRouteEditor();
            BuildRoutesSection();
            RebuildSectionLayout(section);
        });

        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var objects = MonoBehaviourSingleton<ObjectInfoManager>.Instance?.allObjectInfos
            ?.Where(oi => IsRouteDestinationCandidate(oi, _currentObjectInfo, player))
            .GroupBy(oi => oi.id)
            .Select(group => group.OrderBy(RouteDestinationCandidateRank).First())
            .OrderBy(oi => oi.ObjectName, System.StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<ObjectInfo>();

        var resultsGo = new GameObject("RouteDestinationResults", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        var resultsRt = resultsGo.GetComponent<RectTransform>();
        resultsRt.SetParent(section.ContentArea, false);
        var resultsLayout = resultsGo.GetComponent<VerticalLayoutGroup>();
        resultsLayout.childControlHeight = true;
        resultsLayout.childControlWidth = true;
        resultsLayout.childForceExpandHeight = false;
        resultsLayout.childForceExpandWidth = true;
        resultsLayout.spacing = 3f;
        resultsGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TMP_InputField searchField = null;
        void BuildDestinationRows()
        {
            var es = EventSystem.current;
            var children = new List<GameObject>();
            foreach (Transform child in resultsRt)
                children.Add(child.gameObject);
            foreach (var child in children)
            {
                if (es != null && es.currentSelectedGameObject == child)
                    es.SetSelectedGameObject(null);
                DestroyImmediate(child);
            }

            var search = searchField?.text ?? "";
            var matches = objects
                .Where(oi => RouteDestinationMatches(oi, search))
                .ToList();
            if (matches.Count == 0)
            {
                var emptyRow = MakeHLRow(resultsRt, 24f, 0);
                emptyRow.GetComponent<Image>().color = RowBgMutedColor;
                var emptyLabel = MakeTMP(emptyRow.transform, "No matching destinations.", 13f, SubtleTextColor);
                emptyLabel.enableWordWrapping = true;
                emptyLabel.overflowMode = TextOverflowModes.Overflow;
                var emptyLe = emptyLabel.gameObject.AddComponent<LayoutElement>();
                emptyLe.flexibleWidth = 1f;
                emptyLe.preferredWidth = 0f;
                RebuildSectionLayout(section);
                return;
            }

            foreach (var destination in matches)
                AddRouteDestinationRow(resultsRt, destination, section);

            RebuildSectionLayout(section);
        }

        searchField = AddSearchField(section.ContentArea, "Search destinations...", _ => BuildDestinationRows());
        searchField.transform.SetSiblingIndex(1);
        resultsRt.SetSiblingIndex(2);
        BuildDestinationRows();
    }

    private static bool RouteDestinationMatches(ObjectInfo destination, string search)
    {
        if (destination == null)
            return false;
        if (string.IsNullOrWhiteSpace(search))
            return true;

        return SearchContains(destination.ObjectName, search)
            || SearchContains(destination.parentObjectInfo?.ObjectName, search)
            || SearchContains(destination.objectTypes.ToString(), search);
    }

    private static bool SearchContains(string value, string search)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !string.IsNullOrWhiteSpace(search)
            && value.IndexOf(search.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsRouteDestinationCandidate(ObjectInfo destination, ObjectInfo current, Company player)
    {
        if (destination == null
            || player == null
            || SameObjectInfo(destination, current)
            || destination.IsInGameDestroy
            || destination.IsHideToDiscover
            || !destination.IsDiscoveredForPlayerCache
            || destination.IsUseInSpaceElevator
            || destination.AsteroidFromPush
            || destination.clickObjectInfo != null
            || !IsRouteDestinationObjectType(destination))
            return false;

        var objectManager = MonoBehaviourSingleton<ObjectInfoManager>.Instance;
        if (SameObjectInfo(destination, objectManager?.mainObjectInfoSun))
            return false;

        if (destination.objectTypes == global::Data.EObjectTypes.Orbit && destination.parentObjectInfo != null)
        {
            var parent = destination.parentObjectInfo;
            if (parent.objectTypes == global::Data.EObjectTypes.Orbit
                || IsAsteroidOrComet(parent)
                || parent.AsteroidFromPush
                || parent.IsUseInSpaceElevator)
                return false;
        }

        return CanOpenLogisticsFor(destination, player);
    }

    private static bool IsRouteDestinationObjectType(ObjectInfo destination)
    {
        if (destination == null)
            return false;

        switch (destination.objectTypes)
        {
            case global::Data.EObjectTypes.Planet:
            case global::Data.EObjectTypes.Moons:
            case global::Data.EObjectTypes.DwarfPlanet:
            case global::Data.EObjectTypes.Protoplanet:
            case global::Data.EObjectTypes.Asteroid:
            case global::Data.EObjectTypes.Comet:
            case global::Data.EObjectTypes.Orbit:
                return true;
            default:
                return false;
        }
    }

    private static int RouteDestinationCandidateRank(ObjectInfo destination)
    {
        if (destination == null)
            return int.MaxValue;
        if (destination.InterstellarConstructionFacility)
            return 0;
        if (destination.objectTypes == global::Data.EObjectTypes.Orbit)
            return 20;
        return 10;
    }

    private TMP_InputField AddSearchField(Transform parent, string placeholderText, System.Action<string> onChanged)
    {
        var fieldGo = new GameObject("SearchField", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
        fieldGo.transform.SetParent(parent, false);
        var layout = fieldGo.GetComponent<LayoutElement>();
        layout.minHeight = 30f;
        layout.preferredHeight = 30f;
        layout.flexibleWidth = 1f;

        var image = fieldGo.GetComponent<Image>();
        image.color = WithAlpha(CardColor, 0.98f);

        var textAreaGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        textAreaGo.transform.SetParent(fieldGo.transform, false);
        var textAreaRt = textAreaGo.GetComponent<RectTransform>();
        textAreaRt.anchorMin = Vector2.zero;
        textAreaRt.anchorMax = Vector2.one;
        textAreaRt.offsetMin = new Vector2(10f, 2f);
        textAreaRt.offsetMax = new Vector2(-10f, -2f);

        var placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
        placeholderGo.transform.SetParent(textAreaGo.transform, false);
        var placeholderRt = placeholderGo.GetComponent<RectTransform>();
        placeholderRt.anchorMin = Vector2.zero;
        placeholderRt.anchorMax = Vector2.one;
        placeholderRt.offsetMin = Vector2.zero;
        placeholderRt.offsetMax = Vector2.zero;
        var placeholder = placeholderGo.GetComponent<TextMeshProUGUI>();
        placeholder.text = placeholderText;
        placeholder.font = _font;
        placeholder.fontSize = 13f;
        placeholder.color = DisabledTextColor;
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        placeholder.enableWordWrapping = false;
        placeholder.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(textAreaGo.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;
        var text = textGo.GetComponent<TextMeshProUGUI>();
        text.text = "";
        text.font = _font;
        text.fontSize = 13f;
        text.color = _runtimeStyle.RowTextColor;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.raycastTarget = false;

        var input = fieldGo.GetComponent<TMP_InputField>();
        input.targetGraphic = image;
        input.textViewport = textAreaRt;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterLimit = 64;
        input.caretColor = Color.white;
        input.selectionColor = WithAlpha(EngineAccentColor, 0.42f);
        input.transition = Selectable.Transition.None;
        input.navigation = new Navigation { mode = Navigation.Mode.None };
        input.onSelect.AddListener(_ => MonoBehaviourSingleton<InputManager>.Instance?.BlockInputForMoment());
        input.onValueChanged.AddListener(value => onChanged?.Invoke(value ?? ""));
        return input;
    }

    private void AddRouteDestinationRow(Transform parent, ObjectInfo destination, LogisticsSection section)
    {
        if (destination == null)
            return;

        var captured = destination;
        var row = MakeHLRow(parent, 24f, 0);
        row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
        AddObjectIcon(row.transform, captured, 20f);
        var label = MakeTMP(row.transform, CompactObjectName(captured), 13f, _runtimeStyle.RowTextColor);
        var labelLayout = label.gameObject.AddComponent<LayoutElement>();
        labelLayout.flexibleWidth = 1f;
        labelLayout.preferredWidth = 0f;
        var btn = MakeRowButton(row);
        btn.onClick.AddListener(() =>
        {
            var route = Data.LogisticsNetwork.AddRoute(_currentObjectInfo, captured);
            ShowRouteEditor(section, route);
        });
    }

    private void AddRouteHeaderRow(Transform parent, Data.LogisticsRouteRecord route, bool flexible = false)
    {
        var row = MakeHLContainer(parent, 26f, 4f);
        if (flexible)
        {
            var rowLayout = row.GetComponent<LayoutElement>();
            rowLayout.flexibleWidth = 1f;
            rowLayout.preferredWidth = 0f;
        }
        var source = ResolveObjectInfo(route?.sourceObjectId ?? -1) ?? _currentObjectInfo;
        var destination = ResolveObjectInfo(route?.destinationObjectId ?? -1);
        var nameMaxWidth = flexible ? RouteStickyHeaderNameMaxWidth : RouteHeaderNameMaxWidth;

        AddObjectIcon(row.transform, source, 22f);
        AddHeaderLabel(row.transform, CompactObjectName(source, ObjectName(route?.sourceObjectId ?? -1)), EngineAccentColor,
            0f, nameMaxWidth);
        AddHeaderLabel(row.transform, "\u2192", TertiaryTextColor, RouteHeaderArrowWidth);
        AddObjectIcon(row.transform, destination, 22f);
        AddHeaderLabel(row.transform, CompactObjectName(destination, ObjectName(route?.destinationObjectId ?? -1)), EngineAccentColor,
            0f, nameMaxWidth);
    }

    private TextMeshProUGUI AddHeaderLabel(Transform parent, string text, Color color, float width = 0f,
        float maxWidth = RouteHeaderNameMaxWidth)
    {
        var label = MakeTMP(parent, text ?? "?", 14f, color);
        label.alignment = width > 0f ? TextAlignmentOptions.Midline : TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        var layout = label.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = 22f;
        layout.preferredHeight = 22f;
        layout.flexibleHeight = 0f;
        var measured = label.GetPreferredValues(text ?? "?", maxWidth, 22f).x;
        var preferredWidth = width > 0f
            ? width
            : Mathf.Clamp(measured + 4f, 26f, maxWidth);
        layout.minWidth = preferredWidth;
        layout.preferredWidth = preferredWidth;
        layout.flexibleWidth = 0f;
        return label;
    }

    private Image AddObjectIcon(Transform parent, ObjectInfo oi, float size)
    {
        var iconGo = new GameObject("ObjectIcon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        iconGo.transform.SetParent(parent, false);
        var layout = iconGo.GetComponent<LayoutElement>();
        layout.minWidth = size;
        layout.preferredWidth = size;
        layout.minHeight = size;
        layout.preferredHeight = size;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;

        var image = iconGo.GetComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;

        var fallback = MakeTMP(iconGo.transform, "", size * 0.72f, PrimaryTextColor);
        fallback.gameObject.name = "ObjectIconFallback";
        fallback.alignment = TextAlignmentOptions.Center;
        fallback.raycastTarget = false;
        SetObjectIcon(image, oi);
        return image;
    }

    private static void SetObjectIcon(Image image, ObjectInfo oi)
    {
        if (image == null)
            return;

        var sprite = ObjectHeaderSprite(oi);
        image.sprite = sprite;
        image.enabled = sprite != null;
        image.color = Color.white;

        var fallback = image.GetComponentInChildren<TextMeshProUGUI>(true);
        if (fallback != null)
        {
            fallback.enabled = sprite == null;
            fallback.text = sprite == null ? EndpointMarker(oi, oi?.ObjectName) : "";
        }
    }

    private static Sprite ObjectHeaderSprite(ObjectInfo oi)
    {
        return oi?.ImagePlanetUI ?? oi?.ParentObjectInfo?.ImagePlanetUI;
    }

    private void ShowRouteEditor(LogisticsSection section, Data.LogisticsRouteRecord route)
    {
        section.ClearContent();
        if (route == null)
        {
            ClearActiveRouteEditor();
            HidePopupRouteSubheader();
            BuildRoutesSection();
            RebuildSectionLayout(section);
            return;
        }

        _activeRouteEditorRouteId = route.routeId;
        SetRouteEditorMainPageVisible(true);
        ShowPopupRouteSubheader(section, route);

        AddBigButton(section.ContentArea, "+ Add Route Resource", _runtimeStyle.ConfirmButtonColor, () =>
        {
            ShowRouteResourcePicker(section, route);
        });

        var rules = route.resources ?? new List<Data.LogisticsRouteResourceRule>();
        if (rules.Count == 0)
        {
            AddRouteEmptyMessageRow(section.ContentArea, "No resources on this route.");
        }
        else
        {
            AddRouteResourceTableHeader(section);
            var resourceRows = rules
                .Select((rule, index) => new RouteResourceTableItem { Rule = rule, Index = index })
                .Where(item => item.Rule != null)
                .Select(item =>
                {
                    var rd = item.Rule.ResourceDefinition ?? ResolveResource(item.Rule.resourceDef?.id);
                    item.Rule.ResourceDefinition = rd;
                    item.Resource = rd;
                    return item;
                })
                .OrderBy(item => ResourceSortName(item.Resource, item.Rule.resourceDef?.id), System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            AddVirtualRouteResourceTableRows(section, route, resourceRows);
        }

        AddBigButton(section.ContentArea, "+ Assign Spacecraft", _runtimeStyle.ActionButtonColor, () =>
        {
            ShowRouteShipPicker(section, route);
        });

        BuildRouteCraftRows(section, route);

        AddBigButton(section.ContentArea, "+ Assign Launch Vehicle", _runtimeStyle.ActionButtonColor, () =>
        {
            ShowRouteLaunchVehiclePicker(section, route);
        });

        BuildRouteLaunchVehicleRows(section, route);
        AddRouteFacilityLaunchOptionRows(section, route);
        AddRouteGhostFlightsSection(section, route);
        RebuildSectionLayout(section);
    }

    private void AddRouteResourceTableHeader(LogisticsSection section)
    {
        var row = MakeHLRow(section.ContentArea, 22f, 4);
        row.GetComponent<Image>().color = WithAlpha(BorderColor, 0.58f);
        AddTableCell(row.transform, "Resource", 138f, 11f, TertiaryTextColor);
        AddTableCell(row.transform, "Prio", 58f, 11f, TertiaryTextColor);
        AddTableCell(row.transform, "Keep", 78f, 11f, TertiaryTextColor, TextAlignmentOptions.MidlineRight);
        AddTableCell(row.transform, "Target", 98f, 11f, TertiaryTextColor, TextAlignmentOptions.MidlineRight);
        AddSpacer(row.transform, RouteResourceTargetStatusGap);
        AddTableCell(row.transform, "Status", 0f, 11f, TertiaryTextColor, TextAlignmentOptions.MidlineLeft, true, false);
        AddTableCell(row.transform, "", 24f, 11f, TertiaryTextColor);
    }

    private void AddVirtualRouteResourceTableRows(LogisticsSection section, Data.LogisticsRouteRecord route,
        List<RouteResourceTableItem> rows)
    {
        if (section == null || rows == null || rows.Count == 0)
            return;

        AddVirtualFixedList(section.ContentArea, "RouteResourceVirtualList", rows.Count, RouteResourceRowHeight,
            (parent, height) => MakeVirtualHLRow(parent, height, 4f),
            (row, index) =>
            {
                var item = rows[index];
                PopulateRouteResourceTableRow(row, section, route, item.Rule, item.Index, item.Resource);
            });
    }

    private void AddRouteResourceTableRow(LogisticsSection section, Data.LogisticsRouteRecord route,
        Data.LogisticsRouteResourceRule rule, int idx, ResourceDefinition rd)
    {
        var row = MakeHLRow(section.ContentArea, RouteResourceRowHeight, 4);
        PopulateRouteResourceTableRow(row, section, route, rule, idx, rd);
    }

    private void PopulateRouteResourceTableRow(GameObject row, LogisticsSection section, Data.LogisticsRouteRecord route,
        Data.LogisticsRouteResourceRule rule, int idx, ResourceDefinition rd)
    {
        if (row == null || rule == null)
            return;

        row.GetComponent<Image>().color = rule.isActive ? _runtimeStyle.RowBackgroundColor : RowBgMutedColor;

        var textColor = rule.isActive ? _runtimeStyle.RowTextColor : DisabledTextColor;
        AddTableCell(row.transform, ResourceLabel(rd, rule.resourceDef?.id), 138f, 12.2f, textColor);
        AddTableCell(row.transform, rule.isActive ? RoutePriorityLabel(rule.priority) : "Paused", 58f, 12f,
            rule.isActive ? SecondaryTextColor : DisabledTextColor);
        AddTableCell(row.transform, FormatNiceAmount(rule.sourceKeep), 78f, 12.2f, textColor, TextAlignmentOptions.MidlineRight);
        AddTableCell(row.transform, FormatNiceAmount(rule.destinationTarget), 98f, 12.2f, textColor, TextAlignmentOptions.MidlineRight);
        AddSpacer(row.transform, RouteResourceTargetStatusGap);

        var status = RouteResourceStatusText(rule, route);
        var statusLabel = AddTableCell(row.transform, status, 0f, 12f, RouteResourceStatusColor(status, rule.isActive),
            TextAlignmentOptions.MidlineLeft, true, false);
        statusLabel.overflowMode = TextOverflowModes.Ellipsis;

        if (rd != null)
        {
            var editButton = MakeRowButton(row);
            editButton.onClick.AddListener(() => ShowRouteResourceInput(section, route, rd, idx));
        }
        MakeXButton(row.transform, () =>
        {
            Data.LogisticsNetwork.RemoveRouteResource(route, idx);
            ShowRouteEditor(section, route);
        });
    }

    private static string CleanRouteStatus(string status, Data.LogisticsRouteRecord route = null)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "";
        var cleaned = status.Trim();
        if (cleaned.StartsWith("No idle logistics vessel", System.StringComparison.OrdinalIgnoreCase)
            || cleaned.StartsWith("No idle logistics vessels", System.StringComparison.OrdinalIgnoreCase))
            return "No idle vessels";
        if (cleaned.StartsWith("Waiting for source surplus", System.StringComparison.OrdinalIgnoreCase))
            return "Waiting for surplus";
        if (cleaned.StartsWith("Waiting for full load", System.StringComparison.OrdinalIgnoreCase))
            return "Waiting for full load";
        if (cleaned.StartsWith("Waiting for convoy capacity", System.StringComparison.OrdinalIgnoreCase))
            return "Waiting for capacity";
        if (cleaned.StartsWith("No route convoy dispatched", System.StringComparison.OrdinalIgnoreCase))
            return "No convoy dispatched";
        if (cleaned.StartsWith("Could not commit route convoy", System.StringComparison.OrdinalIgnoreCase))
            return "Convoy blocked";
        if (cleaned.StartsWith("No reserved launch vehicle", System.StringComparison.OrdinalIgnoreCase))
            return "No launch capacity";
        if (route == null)
            return cleaned;

        var source = ObjectName(route.sourceObjectId);
        var destination = ObjectName(route.destinationObjectId);
        var compact = CompactRouteLane(route);
        return ReplaceRawRouteText(cleaned, source, destination, compact);
    }

    private void AddRouteHealthRow(LogisticsSection section, Data.LogisticsRouteRecord route)
    {
        AddRouteHealthRow(section?.ContentArea, route);
    }

    private void AddRouteHealthRow(Transform parent, Data.LogisticsRouteRecord route)
    {
        if (parent == null || route == null)
            return;

        var row = MakeHLRow(parent, 24f, 6);
        row.GetComponent<Image>().color = RowBgMutedColor;
        ApplyRouteOverviewDetailRowPadding(row, parent);
        var label = MakeTMP(row.transform, BuildRouteHealthSummary(route), 12f, SubtleTextColor);
        label.enableWordWrapping = true;
        label.overflowMode = TextOverflowModes.Overflow;
        var layout = label.gameObject.AddComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        layout.preferredWidth = 0f;
    }

    private string BuildRouteHealthSummary(Data.LogisticsRouteRecord route)
    {
        if (route == null)
            return "Route unavailable";
        if (!route.isActive)
            return "Paused";
        if (!string.IsNullOrWhiteSpace(route.statusNote))
            return CleanRouteStatus(route.statusNote, route);

        var rules = route.resources?.Where(rule => rule != null).ToList() ?? new List<Data.LogisticsRouteResourceRule>();
        if (rules.Count == 0)
            return "No resources configured";

        var activeRules = rules.Where(rule => rule.isActive).ToList();
        if (activeRules.Count == 0)
            return "No active resources";

        var noteRule = activeRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.statusNote))
            .OrderBy(rule => IsStockedRouteStatus(rule.statusNote) ? 1 : 0)
            .ThenByDescending(rule => NormalizeRouteResourcePriority(rule.priority))
            .ThenBy(rule => ResourceSortName(rule.ResourceDefinition ?? ResolveResource(rule.resourceDef?.id), rule.resourceDef?.id), System.StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        var primary = noteRule != null
            ? $"{ResourceLabel(noteRule.ResourceDefinition ?? ResolveResource(noteRule.resourceDef?.id), noteRule.resourceDef?.id)}: {CleanRouteStatus(noteRule.statusNote, route)}"
            : "Ready";
        return primary;
    }

    private static bool IsStockedRouteStatus(string status)
    {
        return !string.IsNullOrWhiteSpace(status)
            && status.StartsWith("Target stocked", System.StringComparison.Ordinal);
    }

    private static string BuildRouteResourceFlagText(Data.LogisticsRouteResourceRule rule)
    {
        if (rule == null)
            return "";

        var flags = new List<string> { RoutePriorityLabel(rule.priority) };
        if (!rule.isActive)
            flags.Add("paused");
        return $"  [{string.Join(", ", flags)}]";
    }

    private static string RouteResourceStatusText(Data.LogisticsRouteResourceRule rule, Data.LogisticsRouteRecord route)
    {
        if (rule == null)
            return "";
        if (!rule.isActive)
            return "Paused";

        var status = CleanRouteStatus(rule.statusNote, route);
        return string.IsNullOrWhiteSpace(status) ? "Ready" : status;
    }

    private static Color RouteResourceStatusColor(string status, bool active)
    {
        if (!active)
            return DisabledTextColor;
        if (string.IsNullOrWhiteSpace(status))
            return SecondaryTextColor;

        var lower = status.ToLowerInvariant();
        if (lower.Contains("ready")
            || lower.Contains("target stocked")
            || lower.StartsWith("dispatched", System.StringComparison.Ordinal)
            || lower.StartsWith("lifted", System.StringComparison.Ordinal)
            || lower.StartsWith("dropped", System.StringComparison.Ordinal))
            return NominalColor;

        if (lower.Contains("unavailable")
            || lower.Contains("blocked")
            || lower.Contains("failed")
            || lower.Contains("could not"))
            return CriticalColor;

        if (lower.Contains("waiting")
            || lower.Contains("no idle")
            || lower.Contains("capacity")
            || lower.Contains("surplus"))
            return WarningColor;

        return SecondaryTextColor;
    }

    private static Color RouteOverviewStatusColor(Data.LogisticsRouteRecord route)
    {
        if (route == null || !route.isActive)
            return DisabledTextColor;

        var rules = route.resources?
            .Where(rule => rule != null && rule.isActive)
            .ToList() ?? new List<Data.LogisticsRouteResourceRule>();
        if (rules.Count == 0)
            return DisabledTextColor;

        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (player == null)
            return CriticalColor;

        var source = ResolveObjectInfo(route.sourceObjectId);
        var destination = ResolveObjectInfo(route.destinationObjectId);
        var sourceData = source?.GetObjectInfoData(player);
        var destinationData = destination?.GetObjectInfoData(player);
        if (sourceData == null || destinationData == null)
            return CriticalColor;

        var hasUnmetDemand = false;
        var hasInTransit = false;
        var hasShippableSupply = false;
        foreach (var rule in rules)
        {
            var rd = rule.ResourceDefinition ?? ResolveResource(rule.resourceDef?.id);
            rule.ResourceDefinition = rd;
            if (rd == null)
                continue;

            var destinationStock = destinationData.CheckResources(rd);
            var inFlight = RouteInFlightDeliveryAmount(route, rd);
            if (inFlight > 0.001)
                hasInTransit = true;

            var remaining = System.Math.Max(0, rule.destinationTarget - destinationStock - inFlight);
            if (remaining <= 0.001)
                continue;

            hasUnmetDemand = true;
            var sourceAvailable = System.Math.Max(0, sourceData.CheckResources(rd) - System.Math.Max(0, rule.sourceKeep));
            if (sourceAvailable > 0.001 && !IsUnavailableRouteStatus(rule.statusNote))
                hasShippableSupply = true;
        }

        if (!hasUnmetDemand || hasInTransit)
            return NominalColor;

        var readyCraft = CountReadyRouteCraft(route);
        if (readyCraft <= 0)
            return WarningColor;

        return hasShippableSupply ? WarningColor : CriticalColor;
    }

    private static int CountReadyRouteCraft(Data.LogisticsRouteRecord route)
    {
        if (route == null)
            return 0;

        return Data.LogisticsNetwork.GetAllGhostCraft()
            .Count(c => c != null
                && c.assignedRouteId == route.routeId
                && c.status == Data.GhostCraftStatus.IdleAtHome
                && c.currentObjectId == route.sourceObjectId);
    }

    private static bool IsUnavailableRouteStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        var lower = status.ToLowerInvariant();
        return lower.Contains("resource unavailable")
            || lower.Contains("stockpile unavailable")
            || lower.Contains("route body unavailable");
    }

    private static double RouteInFlightDeliveryAmount(Data.LogisticsRouteRecord route, ResourceDefinition rd)
    {
        if (route == null || rd == null)
            return 0;

        return Data.LogisticsNetwork.GetAllGhostFlights()
            .Where(f => f != null
                && !f.isReturnFlight
                && f.routeId == route.routeId
                && (f.status == Data.GhostFlightStatus.Outbound || f.status == Data.GhostFlightStatus.Planned))
            .Sum(f => GhostFlightCargoAmount(f, rd));
    }

    private static string RoutePriorityLabel(int priority)
    {
        switch (NormalizeRouteResourcePriority(priority))
        {
            case -1: return "Low";
            case 1: return "High";
            case 2: return "Critical";
            default: return "Normal";
        }
    }

    private static int NormalizeRouteResourcePriority(int priority)
    {
        return System.Math.Max(-1, System.Math.Min(2, priority));
    }

    private static void SetRouteActive(Data.LogisticsRouteRecord route, bool active)
    {
        if (route == null)
            return;

        route.isActive = active;
        ClearRoutePlanningStatus(route);
    }

    private static void ClearRoutePlanningStatus(Data.LogisticsRouteRecord route)
    {
        if (route == null)
            return;

        route.statusNote = null;
        foreach (var rule in route.resources ?? new List<Data.LogisticsRouteResourceRule>())
        {
            if (rule != null)
                rule.statusNote = null;
        }
    }

    private void AddRouteShipGroupFlightPlanControls(Transform parent, Data.LogisticsRouteRecord route, string typeId,
        LogisticsSection section)
    {
        if (parent == null || route == null || string.IsNullOrWhiteSpace(typeId))
            return;

        var type = RouteSpacecraftType(typeId);
        var fastDisabled = type?.SolarSC == true;
        var selectedMode = GetRouteShipTypeFlightPlanMode(route, typeId, fastDisabled);
        AddFlightPlanModeButtons(parent, selectedMode, fastDisabled, mode =>
        {
            SetRouteShipTypeFlightPlanMode(route, typeId, fastDisabled, mode);
            ClearRoutePlanningStatus(route);
            LogisticsObserver.NormalizeGhostConvoys();
            ShowRouteEditor(section, route);
        });
    }

    private void AddRouteShipTypeFlightPlanRow(LogisticsSection section, Data.LogisticsRouteRecord route, string typeId,
        int desiredCount)
    {
        if (section == null || route == null || string.IsNullOrWhiteSpace(typeId))
            return;

        var type = RouteSpacecraftType(typeId);
        var fastDisabled = type?.SolarSC == true;
        var row = MakeHLRow(section.ContentArea, 30f, 6);
        row.GetComponent<Image>().color = RowBgMutedColor;
        AddTableCell(row.transform, "Default plan", 0f, 12.5f, _runtimeStyle.RowTextColor,
            TextAlignmentOptions.MidlineLeft, true);
        AddFlightPlanModeButtons(row.transform, GetRouteShipTypeFlightPlanMode(route, typeId, fastDisabled),
            fastDisabled, mode =>
            {
                SetRouteShipTypeFlightPlanMode(route, typeId, fastDisabled, mode);
                ClearRoutePlanningStatus(route);
                LogisticsObserver.NormalizeGhostConvoys();
                ShowRouteShipCountEditor(section, route, typeId, desiredCount);
            });
    }

    private static Data.LogisticsFlightPlanMode GetRouteShipTypeFlightPlanMode(
        Data.LogisticsRouteRecord route,
        string typeId,
        bool fastDisabled)
    {
        var mode = Data.LogisticsNetwork.GetRouteSpacecraftFlightPlanMode(route, typeId);
        if (fastDisabled && mode == Data.LogisticsFlightPlanMode.Fast)
        {
            Data.LogisticsNetwork.SetRouteSpacecraftFlightPlanMode(route, typeId,
                Data.LogisticsFlightPlanMode.Optimal);
            return Data.LogisticsFlightPlanMode.Optimal;
        }

        return mode;
    }

    private static void SetRouteShipTypeFlightPlanMode(
        Data.LogisticsRouteRecord route,
        string typeId,
        bool fastDisabled,
        Data.LogisticsFlightPlanMode mode)
    {
        var normalized = fastDisabled && mode == Data.LogisticsFlightPlanMode.Fast
            ? Data.LogisticsFlightPlanMode.Optimal
            : NormalizeUiFlightPlanMode(mode);
        Data.LogisticsNetwork.SetRouteSpacecraftFlightPlanMode(route, typeId, normalized);
    }

    private void AddFlightPlanModeButtons(Transform parent, Data.LogisticsFlightPlanMode? selectedMode,
        bool fastDisabled, Action<Data.LogisticsFlightPlanMode> onSelected)
    {
        selectedMode = selectedMode.HasValue ? NormalizeUiFlightPlanMode(selectedMode.Value) : (Data.LogisticsFlightPlanMode?)null;
        foreach (var mode in new[]
                 {
                     Data.LogisticsFlightPlanMode.Fast,
                     Data.LogisticsFlightPlanMode.Optimal
                 })
        {
            var capturedMode = mode;
            var button = AddSmallButton(parent, FlightPlanModeLabel(mode), _runtimeStyle.ToggleOffColor, () =>
            {
                if (fastDisabled && capturedMode == Data.LogisticsFlightPlanMode.Fast)
                    return;
                onSelected?.Invoke(capturedMode);
            }, FlightPlanModeButtonWidth(mode));
            SetOutlinedButtonState(button, selectedMode.HasValue && selectedMode.Value == mode);
            if (fastDisabled && mode == Data.LogisticsFlightPlanMode.Fast)
                SetFlightPlanButtonDisabled(button);
        }
    }

    private static void SetFlightPlanButtonDisabled(Button button)
    {
        if (button == null)
            return;

        button.interactable = false;
        var style = ButtonStyle(ButtonTone.Neutral);
        style.NormalFill = WithAlpha(VoidColor, 0.08f);
        style.HoverFill = style.NormalFill;
        style.PressedFill = style.NormalFill;
        style.SelectedFill = style.NormalFill;
        style.NormalBorder = WithAlpha(DisabledTextColor, 0.5f);
        style.HoverBorder = style.NormalBorder;
        style.PressedBorder = style.NormalBorder;
        style.SelectedBorder = style.NormalBorder;
        style.NormalText = DisabledTextColor;
        style.HoverText = DisabledTextColor;
        style.PressedText = DisabledTextColor;
        style.SelectedText = DisabledTextColor;
        ApplyButtonVisual(button, style);
    }

    private static float FlightPlanModeButtonWidth(Data.LogisticsFlightPlanMode mode)
    {
        return mode == Data.LogisticsFlightPlanMode.Optimal
            ? FlightPlanModeOptimalButtonWidth
            : FlightPlanModeFastButtonWidth;
    }

    private static string FlightPlanModeLabel(Data.LogisticsFlightPlanMode mode)
    {
        switch (NormalizeUiFlightPlanMode(mode))
        {
            case Data.LogisticsFlightPlanMode.Fast:
                return "Fast";
            case Data.LogisticsFlightPlanMode.Optimal:
                return "Optimal";
            default:
                return "Optimal";
        }
    }

    private static Data.LogisticsFlightPlanMode NormalizeUiFlightPlanMode(Data.LogisticsFlightPlanMode mode)
    {
        return LogisticsFlightCalculator.NormalizeFlightPlanMode(mode);
    }

    private static SpacecraftType RouteSpacecraftType(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId))
            return null;
        return SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllSpacecraftType?.GetByID(typeId);
    }

    private void BuildRouteCraftRows(LogisticsSection section, Data.LogisticsRouteRecord route)
    {
        var sourceId = route?.sourceObjectId ?? -1;
        var craft = Data.LogisticsNetwork.GetAllGhostCraft()
            .Where(c => c != null && c.assignedRouteId == route.routeId)
            .ToList();
        if (craft.Count == 0)
        {
            AddRouteEmptyMessageRow(section.ContentArea, "None assigned");
            return;
        }

        AddRouteAssetTableHeader(section.ContentArea, true);
        foreach (var group in craft.GroupBy(c => c.shipTypeId).OrderBy(g => GhostShipName(g.Key)))
        {
            var typeId = group.Key;
            var list = group.ToList();
            var ready = list.Count(c => c.status == Data.GhostCraftStatus.IdleAtHome && c.currentObjectId == sourceId);
            var row = AddRouteAssetTableRow(section.ContentArea, GhostShipIcon(typeId), GhostShipName(typeId),
                list.Count, ready, _runtimeStyle.RowTextColor, _runtimeStyle.RowBackgroundColor, 28f,
                controlsParent => AddRouteShipGroupFlightPlanControls(controlsParent, route, typeId, section));
            row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
            var rowButton = MakeRowButton(row);
            rowButton.onClick.AddListener(() => ShowRouteShipCountEditor(section, route, typeId));
        }
    }

    private void ShowRouteShipCountEditor(LogisticsSection section, Data.LogisticsRouteRecord route, string typeId, int desiredCount = -1)
    {
        SetRouteEditorMainPageVisible(false);
        section.ClearContent();
        AddBigButton(section.ContentArea, "\u2190 Back", _runtimeStyle.BackButtonColor, () =>
        {
            ShowRouteEditor(section, route);
        });

        if (route == null || string.IsNullOrWhiteSpace(typeId))
        {
            ClearActiveRouteEditor();
            BuildRoutesSection();
            RebuildSectionLayout(section);
            return;
        }

        var source = MonoBehaviourSingleton<ObjectInfoManager>.Instance?.GetByID(route.sourceObjectId) ?? _currentObjectInfo;
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (source == null || player == null)
        {
            section.AddTextRow("Route source is unavailable.", _font, 13f, DisabledTextColor);
            RebuildSectionLayout(section);
            return;
        }

        var assigned = Data.LogisticsNetwork.GetAllGhostCraft()
            .Where(c => c != null
                && c.assignedRouteId == route.routeId
                && string.Equals(c.shipTypeId, typeId, System.StringComparison.Ordinal))
            .ToList();
        var releasable = assigned.Where(IsGhostCraftReleasable).ToList();
        var locked = Mathf.Max(0, assigned.Count - releasable.Count);
        var adoptable = GetAdoptableRouteSpacecraft(source, player, typeId);
        var max = assigned.Count + adoptable.Count;
        var desired = desiredCount < 0
            ? assigned.Count
            : Mathf.Clamp(desiredCount, locked, max);

        var title = MakeTMP(section.ContentArea, GhostShipTypeName(typeId), 14f, EngineAccentColor);
        title.rectTransform.sizeDelta = new Vector2(0, 22);

        section.AddTextRow($"Assigned {assigned.Count}  available {adoptable.Count}", _font, 12.5f, SubtleTextColor);
        if (locked > 0)
            section.AddTextRow($"{locked} craft busy; minimum count is {locked}.", _font, 12.5f, WarningColor);

        var row = MakeHLRow(section.ContentArea, 28f, 6);
        row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
        var label = MakeTMP(row.transform, "Route count", _runtimeStyle.RowFontSize, _runtimeStyle.RowTextColor);
        var le = label.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.preferredWidth = 0f;

        AddSmallButton(row.transform, "-", _runtimeStyle.SmallButtonColor, () =>
        {
            ShowRouteShipCountEditor(section, route, typeId, Mathf.Max(locked, desired - GetSpacecraftStackClickStep()));
        }, 28f);
        AddFixedLabel(row.transform, $"{desired}/{max}", 62f);
        AddSmallButton(row.transform, "+", _runtimeStyle.SmallButtonColor, () =>
        {
            ShowRouteShipCountEditor(section, route, typeId, Mathf.Min(max, desired + GetSpacecraftStackClickStep()));
        }, 28f);
        AddSmallButton(row.transform, "Min", _runtimeStyle.SmallButtonColor, () =>
        {
            ShowRouteShipCountEditor(section, route, typeId, locked);
        }, 42f);
        AddSmallButton(row.transform, "Max", _runtimeStyle.SmallButtonColor, () =>
        {
            ShowRouteShipCountEditor(section, route, typeId, max);
        }, 42f);

        AddRouteShipTypeFlightPlanRow(section, route, typeId, desired);

        var confirmRow = MakeHLRow(section.ContentArea, 30f, 8);
        confirmRow.GetComponent<Image>().color = RowBgMutedColor;
        AddBigButtonInline(confirmRow.transform, "Apply", _runtimeStyle.ConfirmButtonColor, () =>
        {
            ApplyRouteShipCount(route, typeId, desired);
            ShowRouteEditor(section, route);
        });
        AddBigButtonInline(confirmRow.transform, "Cancel", _runtimeStyle.BackButtonColor, () => ShowRouteEditor(section, route));

        RebuildSectionLayout(section);
    }

    private void ApplyRouteShipCount(Data.LogisticsRouteRecord route, string typeId, int desiredCount)
    {
        if (route == null || string.IsNullOrWhiteSpace(typeId))
            return;

        var source = MonoBehaviourSingleton<ObjectInfoManager>.Instance?.GetByID(route.sourceObjectId) ?? _currentObjectInfo;
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (source == null || player == null)
            return;

        var assigned = Data.LogisticsNetwork.GetAllGhostCraft()
            .Where(c => c != null
                && c.assignedRouteId == route.routeId
                && string.Equals(c.shipTypeId, typeId, System.StringComparison.Ordinal))
            .ToList();
        var releasable = assigned.Where(IsGhostCraftReleasable)
            .OrderBy(c => c.status == Data.GhostCraftStatus.Blocked ? 1 : 0)
            .ThenByDescending(c => c.ledgerId)
            .ToList();
        var adoptable = GetAdoptableRouteSpacecraft(source, player, typeId);
        var locked = Mathf.Max(0, assigned.Count - releasable.Count);
        var desired = Mathf.Clamp(desiredCount, locked, assigned.Count + adoptable.Count);

        if (desired < assigned.Count)
        {
            var releaseCount = assigned.Count - desired;
            foreach (var record in releasable.Take(releaseCount).ToList())
            {
                if (!Data.LogisticsNetwork.ReleaseGhostCraft(source, record.ledgerId, out var reason))
                    LogisticsObserver.LogWarning($"ROUTE craft release failed: ship={record.shipName ?? record.shipTypeId} reason={reason}");
            }
            return;
        }

        var addCount = desired - assigned.Count;
        if (addCount <= 0)
            return;

        foreach (var sc in adoptable.Take(addCount).ToList())
        {
            if (Data.LogisticsNetwork.AdoptSpacecraft(source, sc, out var reason))
            {
                var adopted = Data.LogisticsNetwork.GetAllGhostCraft()
                    .Where(c => c != null && c.originalShipId == sc.ID && c.currentObjectId == source.id)
                    .OrderByDescending(c => c.ledgerId)
                    .FirstOrDefault();
                if (adopted != null && !Data.LogisticsNetwork.AssignGhostCraftToRoute(route.routeId, adopted, out var assignReason))
                    LogisticsObserver.LogWarning($"ROUTE craft assign failed: ship={adopted.shipName ?? adopted.shipTypeId} reason={assignReason}");
            }
            else
            {
                LogisticsObserver.LogWarning($"ROUTE adopt failed: {reason}");
            }
        }
    }

    private static List<Spacecraft> GetAdoptableRouteSpacecraft(ObjectInfo source, Company player, string typeId)
    {
        if (source == null || player == null || string.IsNullOrWhiteSpace(typeId))
            return new List<Spacecraft>();

        return UnityEngine.Object.FindObjectsOfType<Spacecraft>()
            .Where(sc => sc != null
                && string.Equals(sc.spacecraftType?.ID, typeId, System.StringComparison.Ordinal)
                && Data.LogisticsNetwork.IsSpacecraftAdoptableAt(source, sc, player, out _))
            .OrderBy(sc => sc.GetSpacecraftName())
            .ToList();
    }

    private void BuildRouteLaunchVehicleRows(LogisticsSection section, Data.LogisticsRouteRecord route)
    {
        var sourceId = route?.sourceObjectId ?? -1;
        var launchVehicles = Data.LogisticsNetwork.GetAllGhostLaunchVehicles()
            .Where(lv => lv != null && lv.assignedRouteId == route.routeId)
            .ToList();
        if (launchVehicles.Count == 0)
        {
            AddRouteEmptyMessageRow(section.ContentArea, "None assigned");
            return;
        }

        AddRouteAssetTableHeader(section.ContentArea);
        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? System.DateTime.Now;
        foreach (var group in launchVehicles.GroupBy(lv => lv.launchVehicleTypeId).OrderBy(g => LaunchVehicleName(g.Key)))
        {
            var typeId = group.Key;
            var list = group.ToList();
            var ready = list.Count(lv => lv.currentObjectId == sourceId && Data.LogisticsNetwork.IsGhostLaunchVehicleReady(lv, now));
            var row = AddRouteAssetTableRow(section.ContentArea, LaunchVehicleIcon(typeId), LaunchVehicleName(typeId), list.Count, ready, _runtimeStyle.RowTextColor,
                _runtimeStyle.RowBackgroundColor, 28f);
            row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
            var rowButton = MakeRowButton(row);
            rowButton.onClick.AddListener(() => ShowRouteLaunchVehicleCountEditor(section, route, typeId));
        }
    }

    private void AddRouteFacilityLaunchOptionRows(LogisticsSection section, Data.LogisticsRouteRecord route)
    {
        if (section == null || route == null)
            return;

        var source = ResolveObjectInfo(route.sourceObjectId) ?? _currentObjectInfo;
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (source == null || player == null || IsOrbitObject(source))
            return;

        var availableOptions = GetAvailableRouteFacilityLaunchOptions(source, player);

        NormalizeRouteFacilityLaunchSettings(route);
        var optionsByCategory = availableOptions.ToDictionary(option => option.Category, StringComparer.Ordinal);

        var visibleOptionCount = RouteFacilityLaunchCategories.Length;
        var tileRowCount = Mathf.Max(1, Mathf.CeilToInt(visibleOptionCount / (float)RouteFacilityLaunchTilesPerRow));
        var shell = MakeRouteFacilityLaunchSectionShell(section.ContentArea, tileRowCount);

        GameObject tileRow = null;
        var tilesInRow = 0;

        foreach (var category in RouteFacilityLaunchCategories)
        {
            if (!optionsByCategory.TryGetValue(category, out var option))
                option = CreateUnavailableRouteFacilityLaunchOption(category);

            if (tileRow == null || tilesInRow >= RouteFacilityLaunchTilesPerRow)
            {
                tileRow = MakeRouteFacilityLaunchTileRow(shell.transform);
                tilesInRow = 0;
            }

            var capturedCategory = category;
            var enabled = IsRouteFacilityLaunchCategoryEnabled(route, capturedCategory);
            AddRouteFacilityLaunchTile(tileRow.transform, option, enabled, option.IsAvailable, () =>
            {
                SetRouteFacilityLaunchCategoryEnabled(route, capturedCategory,
                    !IsRouteFacilityLaunchCategoryEnabled(route, capturedCategory));
                ShowRouteEditor(section, route);
            });
            tilesInRow++;
        }
    }

    private static RouteFacilityLaunchOptionUi CreateUnavailableRouteFacilityLaunchOption(string category)
    {
        var normalized = NormalizeRouteFacilityLaunchCategory(category);
        var option = new RouteFacilityLaunchOptionUi
        {
            Category = normalized,
            IconSprite = RouteFacilityLaunchRepresentativeSprite(normalized),
            Icon = RouteFacilityLaunchFallbackIcon(normalized),
            Count = 0,
            IsAvailable = false
        };
        option.Details.Add("Not available at this route source");
        return option;
    }

    private static bool IsOrbitObject(ObjectInfo oi)
    {
        return oi != null
            && (oi.objectTypes == global::Data.EObjectTypes.Orbit
                || (oi.ObjectName?.IndexOf("[ORBIT]", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
    }

    private GameObject MakeRouteFacilityLaunchSectionShell(Transform parent, int tileRowCount)
    {
        var rowCount = Mathf.Max(1, tileRowCount);
        var preferredHeight = (RouteFacilityLaunchSectionVerticalPadding * 2f)
            + (rowCount * RouteFacilityLaunchOptionHeight)
            + (Mathf.Max(0, rowCount - 1) * RouteFacilityLaunchTileGap);
        var shell = new GameObject("RouteFacilityLaunchSection", typeof(RectTransform),
            typeof(VerticalLayoutGroup), typeof(LayoutElement));
        shell.transform.SetParent(parent, false);

        var layout = shell.GetComponent<VerticalLayoutGroup>();
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.spacing = RouteFacilityLaunchTileGap;
        layout.padding = new RectOffset(RouteFacilityLaunchSectionHorizontalPadding, RouteFacilityLaunchSectionHorizontalPadding,
            RouteFacilityLaunchSectionVerticalPadding, RouteFacilityLaunchSectionVerticalPadding);

        var layoutElement = shell.GetComponent<LayoutElement>();
        layoutElement.minHeight = preferredHeight;
        layoutElement.preferredHeight = preferredHeight;
        layoutElement.flexibleWidth = 1f;
        layoutElement.flexibleHeight = 0f;

        return shell;
    }

    private GameObject MakeRouteFacilityLaunchTileRow(Transform parent)
    {
        var row = new GameObject("RouteFacilityLaunchTiles", typeof(RectTransform), typeof(HorizontalLayoutGroup),
            typeof(LayoutElement));
        row.transform.SetParent(parent, false);

        var layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = RouteFacilityLaunchTileGap;
        layout.padding = new RectOffset(0, 0, 0, 0);

        var layoutElement = row.GetComponent<LayoutElement>();
        layoutElement.minHeight = RouteFacilityLaunchOptionHeight;
        layoutElement.preferredHeight = RouteFacilityLaunchOptionHeight;
        layoutElement.flexibleWidth = 1f;
        layoutElement.flexibleHeight = 0f;
        return row;
    }

    private Button AddRouteFacilityLaunchTile(Transform parent, RouteFacilityLaunchOptionUi option, bool enabled,
        bool available, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject("RouteFacilityLaunchOption", typeof(RectTransform), typeof(Image), typeof(RectMask2D),
            typeof(HorizontalLayoutGroup), typeof(Button), typeof(LayoutElement));
        btnGo.transform.SetParent(parent, false);

        var layout = btnGo.GetComponent<LayoutElement>();
        layout.minWidth = RouteFacilityLaunchOptionWidth;
        layout.preferredWidth = RouteFacilityLaunchOptionWidth;
        layout.minHeight = RouteFacilityLaunchOptionHeight;
        layout.preferredHeight = RouteFacilityLaunchOptionHeight;
        layout.flexibleWidth = 1f;
        layout.flexibleHeight = 0f;

        var buttonLayout = btnGo.GetComponent<HorizontalLayoutGroup>();
        buttonLayout.childForceExpandWidth = false;
        buttonLayout.childForceExpandHeight = false;
        buttonLayout.childControlWidth = true;
        buttonLayout.childControlHeight = true;
        buttonLayout.childAlignment = TextAnchor.MiddleLeft;
        buttonLayout.spacing = RouteFacilityLaunchTileGap;
        buttonLayout.padding = new RectOffset(RouteFacilityLaunchTileHorizontalPadding,
            RouteFacilityLaunchTileHorizontalPadding, RouteFacilityLaunchTileVerticalPadding,
            RouteFacilityLaunchTileVerticalPadding);

        var image = btnGo.GetComponent<Image>();
        image.color = RouteFacilityLaunchTileFill(enabled, available);
        image.raycastTarget = true;

        var button = btnGo.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic = image;
        button.interactable = true;
        if (onClick != null)
            button.onClick.AddListener(onClick);

        AddButtonBorder(btnGo, RouteFacilityLaunchTileBorder(enabled, available));
        ApplyButtonVisual(button, RouteFacilityLaunchTileStyle(enabled, available));

        var iconColor = RouteFacilityLaunchIconColor(enabled, available);
        if (option?.IconSprite != null)
        {
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconGo.transform.SetParent(btnGo.transform, false);
            var iconImage = iconGo.GetComponent<Image>();
            iconImage.sprite = option.IconSprite;
            iconImage.color = iconColor;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            ApplyRouteFacilityLaunchIconLayout(iconGo);
        }
        else
        {
            var iconText = string.IsNullOrWhiteSpace(option?.Icon)
                ? RouteFacilityLaunchFallbackIcon(option?.Category)
                : option.Icon;
            iconText = TintRouteFacilityLaunchIcon(iconText, iconColor);
            var icon = MakeTMP(btnGo.transform, iconText, RouteFacilityLaunchIconFontSize(iconText), iconColor);
            icon.alignment = TextAlignmentOptions.Center;
            icon.enableWordWrapping = false;
            icon.overflowMode = TextOverflowModes.Overflow;
            icon.fontStyle = FontStyles.Bold;
            icon.margin = Vector4.zero;
            icon.rectTransform.offsetMin = Vector2.zero;
            icon.rectTransform.offsetMax = Vector2.zero;
            ApplyRouteFacilityLaunchIconLayout(icon.gameObject);
        }

        var textStack = new GameObject("Text", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        textStack.transform.SetParent(btnGo.transform, false);
        var textLayout = textStack.GetComponent<VerticalLayoutGroup>();
        textLayout.childForceExpandWidth = true;
        textLayout.childForceExpandHeight = false;
        textLayout.childControlWidth = true;
        textLayout.childControlHeight = true;
        textLayout.childAlignment = TextAnchor.MiddleLeft;
        textLayout.spacing = 0f;
        textLayout.padding = new RectOffset(0, 0, 0, 0);
        var textLayoutElement = textStack.GetComponent<LayoutElement>();
        textLayoutElement.minWidth = 0f;
        textLayoutElement.preferredWidth = 0f;
        textLayoutElement.flexibleWidth = 1f;
        textLayoutElement.minHeight = RouteFacilityLaunchOptionHeight - (RouteFacilityLaunchTileVerticalPadding * 2f);
        textLayoutElement.preferredHeight = RouteFacilityLaunchOptionHeight - (RouteFacilityLaunchTileVerticalPadding * 2f);

        var name = MakeTMP(textStack.transform, RouteFacilityLaunchButtonLabel(option?.Category), 11.2f, iconColor);
        name.alignment = TextAlignmentOptions.MidlineLeft;
        name.enableWordWrapping = false;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.fontStyle = FontStyles.Bold;
        name.rectTransform.offsetMin = Vector2.zero;
        name.rectTransform.offsetMax = Vector2.zero;
        var nameLayout = name.gameObject.AddComponent<LayoutElement>();
        nameLayout.minHeight = 18f;
        nameLayout.preferredHeight = 18f;
        nameLayout.flexibleWidth = 1f;

        var state = MakeTMP(textStack.transform, RouteFacilityLaunchStateLabel(option, enabled, available), 9.6f, iconColor);
        state.alignment = TextAlignmentOptions.MidlineLeft;
        state.enableWordWrapping = false;
        state.overflowMode = TextOverflowModes.Ellipsis;
        state.rectTransform.offsetMin = Vector2.zero;
        state.rectTransform.offsetMax = Vector2.zero;
        var stateLayout = state.gameObject.AddComponent<LayoutElement>();
        stateLayout.minHeight = 14f;
        stateLayout.preferredHeight = 14f;
        stateLayout.flexibleWidth = 1f;

        var tooltip = btnGo.AddComponent<SmallHoverTooltip>();
        tooltip.Configure(RouteFacilityLaunchTooltip(option, enabled, available), _font,
            _popupRoot != null ? _popupRoot.GetComponent<RectTransform>() : null);
        return button;
    }

    private static void ApplyRouteFacilityLaunchIconLayout(GameObject iconGo)
    {
        if (iconGo == null)
            return;

        var iconLayout = iconGo.GetComponent<LayoutElement>() ?? iconGo.AddComponent<LayoutElement>();
        iconLayout.minWidth = RouteFacilityLaunchIconColumnWidth;
        iconLayout.preferredWidth = RouteFacilityLaunchIconColumnWidth;
        iconLayout.minHeight = RouteFacilityLaunchOptionHeight - (RouteFacilityLaunchTileVerticalPadding * 2f);
        iconLayout.preferredHeight = RouteFacilityLaunchOptionHeight - (RouteFacilityLaunchTileVerticalPadding * 2f);
        iconLayout.flexibleWidth = 0f;
        iconLayout.flexibleHeight = 0f;
    }

    private static ButtonVisualStyle RouteFacilityLaunchTileStyle(bool enabled, bool available)
    {
        if (!available)
        {
            var fill = RouteFacilityLaunchTileFill(enabled, false);
            var border = RouteFacilityLaunchTileBorder(enabled, false);
            var text = RouteFacilityLaunchIconColor(enabled, false);
            var accent = enabled ? TertiaryTextColor : WarningColor;
            return new ButtonVisualStyle
            {
                HasBorder = true,
                NormalFill = fill,
                HoverFill = enabled ? WithAlpha(HoverColor, 0.18f) : WithAlpha(WarningColor, 0.105f),
                PressedFill = enabled ? WithAlpha(HoverColor, 0.28f) : WithAlpha(WarningColor, 0.17f),
                SelectedFill = fill,
                NormalBorder = border,
                HoverBorder = WithAlpha(accent, enabled ? 0.44f : 0.52f),
                PressedBorder = PrimaryTextColor,
                SelectedBorder = border,
                NormalText = text,
                HoverText = enabled ? SecondaryTextColor : PrimaryTextColor,
                PressedText = PrimaryTextColor,
                SelectedText = text
            };
        }

        if (!enabled)
        {
            var fill = RouteFacilityLaunchTileFill(false, available);
            var border = RouteFacilityLaunchTileBorder(false, available);
            var text = RouteFacilityLaunchIconColor(false, available);
            return new ButtonVisualStyle
            {
                HasBorder = true,
                NormalFill = fill,
                HoverFill = WithAlpha(CriticalColor, 0.15f),
                PressedFill = WithAlpha(CriticalColor, 0.24f),
                SelectedFill = fill,
                NormalBorder = border,
                HoverBorder = WithAlpha(CriticalColor, 0.62f),
                PressedBorder = PrimaryTextColor,
                SelectedBorder = border,
                NormalText = text,
                HoverText = PrimaryTextColor,
                PressedText = PrimaryTextColor,
                SelectedText = text
            };
        }

        return new ButtonVisualStyle
        {
            HasBorder = true,
            NormalFill = WithAlpha(NominalColor, 0.1f),
            HoverFill = WithAlpha(NominalColor, 0.18f),
            PressedFill = WithAlpha(NominalColor, 0.26f),
            SelectedFill = WithAlpha(NominalColor, 0.1f),
            NormalBorder = WithAlpha(NominalColor, 0.56f),
            HoverBorder = WithAlpha(NominalColor, 0.76f),
            PressedBorder = PrimaryTextColor,
            SelectedBorder = WithAlpha(NominalColor, 0.56f),
            NormalText = RouteFacilityLaunchIconColor(true, true),
            HoverText = PrimaryTextColor,
            PressedText = PrimaryTextColor,
            SelectedText = RouteFacilityLaunchIconColor(true, true)
        };
    }

    private static Color RouteFacilityLaunchTileFill(bool enabled, bool available)
    {
        if (!available)
            return enabled ? WithAlpha(HoverColor, 0.1f) : WithAlpha(WarningColor, 0.055f);
        if (!enabled)
            return WithAlpha(CriticalColor, 0.075f);
        return WithAlpha(NominalColor, 0.1f);
    }

    private static Color RouteFacilityLaunchTileBorder(bool enabled, bool available)
    {
        if (!available)
            return enabled ? WithAlpha(TertiaryTextColor, 0.24f) : WithAlpha(WarningColor, 0.34f);
        if (!enabled)
            return WithAlpha(CriticalColor, 0.42f);
        return WithAlpha(NominalColor, 0.56f);
    }

    private static Color RouteFacilityLaunchIconColor(bool enabled, bool available)
    {
        if (!available)
            return enabled
                ? WithAlpha(TertiaryTextColor, 0.74f)
                : WithAlpha(Color.Lerp(TertiaryTextColor, WarningColor, 0.56f), 0.84f);
        if (!enabled)
            return WithAlpha(Color.Lerp(TertiaryTextColor, CriticalColor, 0.58f), 0.84f);
        return Color.Lerp(SecondaryTextColor, NominalColor, 0.34f);
    }

    private static string TintRouteFacilityLaunchIcon(string iconText, Color color)
    {
        if (string.IsNullOrWhiteSpace(iconText))
            return iconText;

        var hex = ColorUtility.ToHtmlStringRGBA(color);
        var colorIndex = iconText.IndexOf("color=", StringComparison.OrdinalIgnoreCase);
        if (colorIndex >= 0)
        {
            var valueStart = colorIndex + "color=".Length;
            var valueEnd = iconText.IndexOfAny(new[] { ' ', '>' }, valueStart);
            if (valueEnd < 0)
                valueEnd = iconText.Length;
            return iconText.Substring(0, valueStart) + $"#{hex}" + iconText.Substring(valueEnd);
        }

        if (iconText.IndexOf("<sprite", StringComparison.OrdinalIgnoreCase) >= 0)
            return $"<color=#{hex}>{iconText}</color>";

        return iconText;
    }

    private static string RouteFacilityLaunchStateLabel(RouteFacilityLaunchOptionUi option, bool enabled, bool available)
    {
        if (!available)
            return enabled ? "Missing" : "Missing Disabled";

        if (!enabled)
            return "Disabled";

        return option != null && option.Count > 1 ? $"Enabled x{option.Count}" : "Enabled";
    }

    private static string RouteFacilityLaunchTooltip(RouteFacilityLaunchOptionUi option, bool enabled, bool available)
    {
        var label = RouteFacilityLaunchButtonLabel(option?.Category);
        if (option == null)
            return label;

        var detail = option.Details.FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
        var state = RouteFacilityLaunchStateLabel(option, enabled, available);
        var prefix = $"{label} - {state}";
        return string.IsNullOrWhiteSpace(detail) ? prefix : $"{prefix}: {detail}";
    }

    private static string RouteFacilityLaunchFallbackIcon(string category)
    {
        var representativeIcon = RouteFacilityLaunchRepresentativeIcon(category);
        if (!string.IsNullOrWhiteSpace(representativeIcon))
            return representativeIcon;

        switch (NormalizeRouteFacilityLaunchCategory(category))
        {
            case RouteLaunchCategoryMagneticRails:
            case RouteLaunchCategoryLaunchPad:
            case RouteLaunchCategoryRotary:
            case RouteLaunchCategorySpaceElevator:
            case RouteLaunchCategoryElectromagneticCatapult:
            case RouteLaunchCategoryMassDriver:
                return RouteFacilityLaunchFallbackInitials(category);
            default:
                return RouteFacilityLaunchFallbackInitials(RouteLaunchCategoryLaunchPad);
        }
    }

    private static string RouteFacilityLaunchFallbackInitials(string category)
    {
        switch (NormalizeRouteFacilityLaunchCategory(category))
        {
            case RouteLaunchCategoryMagneticRails:
                return "MR";
            case RouteLaunchCategoryLaunchPad:
                return "LP";
            case RouteLaunchCategoryRotary:
                return "RL";
            case RouteLaunchCategorySpaceElevator:
                return "SE";
            case RouteLaunchCategoryElectromagneticCatapult:
                return "EC";
            case RouteLaunchCategoryMassDriver:
                return "MD";
            default:
                return "LP";
        }
    }

    private static Sprite RouteFacilityLaunchRepresentativeSprite(string category)
    {
        var normalized = NormalizeRouteFacilityLaunchCategory(category);
        if (string.Equals(normalized, RouteLaunchCategorySpaceElevator, StringComparison.Ordinal))
            return null;

        return RouteFacilityLaunchRepresentativeFacilitySprite(normalized);
    }

    private static Sprite RouteFacilityLaunchRepresentativeFacilitySprite(string category)
    {
        foreach (var candidate in EnumerateRouteFacilityLaunchDescriptors()
                     .Select(facility => new
                     {
                         Facility = facility,
                         Rank = RouteFacilityLaunchFacilityRepresentativeRank(facility, category)
                     })
                     .Where(candidate => candidate.Rank < 100)
                     .OrderBy(candidate => candidate.Rank)
                     .ThenBy(candidate => FacilityLaunchDisplayName(candidate.Facility), StringComparer.OrdinalIgnoreCase))
        {
            var sprite = RouteFacilityLaunchDescriptorSprite(candidate.Facility);
            if (sprite != null)
                return sprite;
        }

        return null;
    }

    private static string RouteFacilityLaunchRepresentativeIcon(string category)
    {
        var normalized = NormalizeRouteFacilityLaunchCategory(category);
        var facilityIcon = RouteFacilityLaunchRepresentativeFacilityIcon(normalized);
        if (!string.IsNullOrWhiteSpace(facilityIcon))
            return facilityIcon;

        var allTypes = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllLaunchVehicleType;
        if (allTypes == null)
            return "";

        foreach (var type in EnumerateLaunchVehicleTypes(allTypes)
                     .Where(type => IsRepresentativeRouteFacilityLaunchType(type, normalized))
                     .OrderBy(type => RouteFacilityLaunchRepresentativeRank(type, normalized))
                     .ThenBy(type => type.Name ?? "", StringComparer.OrdinalIgnoreCase))
        {
            var icon = ShipIcon(type.SpriteId);
            if (!string.IsNullOrWhiteSpace(icon))
                return icon;
        }

        if (string.Equals(normalized, RouteLaunchCategoryElectromagneticCatapult, StringComparison.Ordinal))
            return "";

        return "";
    }

    private static string RouteFacilityLaunchRepresentativeFacilityIcon(string category)
    {
        foreach (var candidate in EnumerateRouteFacilityLaunchDescriptors()
                     .Select(facility => new
                     {
                         Facility = facility,
                         Rank = RouteFacilityLaunchFacilityRepresentativeRank(facility, category)
                     })
                     .Where(candidate => candidate.Rank < 100)
                     .OrderBy(candidate => candidate.Rank)
                     .ThenBy(candidate => FacilityLaunchDisplayName(candidate.Facility), StringComparer.OrdinalIgnoreCase))
        {
            var icon = RouteFacilityLaunchIconForFacility(candidate.Facility,
                RouteFacilityLaunchFakeType(candidate.Facility), category);
            if (!string.IsNullOrWhiteSpace(icon))
                return icon;
        }

        return "";
    }

    private static int RouteFacilityLaunchFacilityRepresentativeRank(global::Data.ScriptableObject.GroundFacilityDescriptor facility,
        string category)
    {
        if (facility == null)
            return 100;

        var type = RouteFacilityLaunchFakeType(facility);
        var facilityText = RouteFacilityLaunchDescriptorSearchText(facility);
        var typeText = RouteFacilityLaunchSearchText(type?.Name, type?.ID);
        var classified = ClassifyRouteFacilityLaunchCategory(facilityText, typeText);
        if (!string.Equals(classified, category, StringComparison.Ordinal))
            return 100;

        switch (category)
        {
            case RouteLaunchCategoryMagneticRails:
                if (facilityText.Contains("magnetic launch rail"))
                    return 0;
                if (facilityText.Contains("launch rail") || facilityText.Contains(" rail"))
                    return 5;
                break;
            case RouteLaunchCategoryLaunchPad:
                if (facilityText.Contains("launch pad"))
                    return 0;
                if (facilityText.Contains(" pad"))
                    return 5;
                if (typeText.Contains("launch pad") || typeText.Contains(" pad"))
                    return 10;
                if (facilityText.Contains("launch facility") || facilityText.Contains("facility"))
                    return 80;
                break;
            case RouteLaunchCategoryRotary:
                if (facilityText.Contains("rotary launcher") || facilityText.Contains("rotary"))
                    return 0;
                if (facilityText.Contains("spin"))
                    return 5;
                break;
            case RouteLaunchCategorySpaceElevator:
                if (facilityText.Contains("space elevator") || facilityText.Contains("elevator"))
                    return 0;
                break;
            case RouteLaunchCategoryElectromagneticCatapult:
                if (facilityText.Contains("electromagnetic catapult"))
                    return 0;
                if (facilityText.Contains("electromagnetic") || facilityText.Contains("catapult"))
                    return 5;
                break;
            case RouteLaunchCategoryMassDriver:
                if (facilityText.Contains("stationary mass driver"))
                    return 0;
                if (facilityText.Contains("mass driver"))
                    return 5;
                break;
        }

        return 25;
    }

    private static IEnumerable<global::Data.ScriptableObject.GroundFacilityDescriptor> EnumerateRouteFacilityLaunchDescriptors()
    {
        var allFacilities = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllFacility;
        if (allFacilities?.ListNotEmpty == null)
            yield break;

        foreach (var facility in allFacilities.ListNotEmpty.OfType<global::Data.ScriptableObject.GroundFacilityDescriptor>())
        {
            if (facility == null)
                continue;

            if (facility.facilityType.HasFlag(global::Data.ScriptableObject.FacilityBaseDescriptor.EFacilityType.LaunchFacility)
                || facility.bonusData?.bonus == EBonus.LaunchCostOptionInPlanMission)
                yield return facility;
        }
    }

    private static LaunchVehicleType RouteFacilityLaunchFakeType(global::Data.ScriptableObject.GroundFacilityDescriptor facility)
    {
        if (facility == null || string.IsNullOrWhiteSpace(facility.bonusData?.fakeLVId))
            return null;

        return SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllLaunchVehicleType
            ?.GetByID(facility.bonusData.fakeLVId);
    }

    private static string FacilityLaunchDisplayName(global::Data.ScriptableObject.FacilityBaseDescriptor facility)
    {
        if (facility == null)
            return "";

        if (!string.IsNullOrWhiteSpace(facility.Name))
            return facility.Name;
        if (!string.IsNullOrWhiteSpace(facility.ID))
            return facility.ID;
        return facility.name ?? "";
    }

    private static string RouteFacilityLaunchDescriptorSearchText(global::Data.ScriptableObject.FacilityBaseDescriptor facility)
    {
        if (facility == null)
            return "";

        return RouteFacilityLaunchSearchText(facility.Name, facility.ID, facility.name);
    }

    private static string RouteFacilityLaunchSearchText(params string[] parts)
    {
        var text = string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (string.IsNullOrWhiteSpace(text))
            return "";

        text = text.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        while (text.Contains("  "))
            text = text.Replace("  ", " ");
        return text.Trim();
    }

    private static string RouteFacilityLaunchIconForFacility(global::Data.ScriptableObject.FacilityBaseDescriptor facility,
        LaunchVehicleType type, string category)
    {
        if (string.Equals(NormalizeRouteFacilityLaunchCategory(category), RouteLaunchCategorySpaceElevator, StringComparison.Ordinal))
            return RouteFacilityLaunchTypeIcon(type);

        var facilityIcon = RouteFacilityLaunchDescriptorIcon(facility);
        var typeIcon = RouteFacilityLaunchTypeIcon(type);
        return !string.IsNullOrWhiteSpace(facilityIcon) ? facilityIcon : typeIcon;
    }

    private static Sprite RouteFacilityLaunchDescriptorSprite(global::Data.ScriptableObject.FacilityBaseDescriptor facility)
    {
        var sprite = facility?.Sprite;
        if (sprite == null || !IsUsableRouteFacilitySpriteId(sprite.name))
            return null;

        return sprite;
    }

    private static string RouteFacilityLaunchDescriptorIcon(global::Data.ScriptableObject.FacilityBaseDescriptor facility)
    {
        var sprite = RouteFacilityLaunchDescriptorSprite(facility);
        if (sprite == null)
            return "";

        var icon = ShipIcon(sprite.name);
        return IsPlaceholderRouteFacilityIcon(icon) ? "" : icon;
    }

    private static string RouteFacilityLaunchTypeIcon(LaunchVehicleType type)
    {
        if (type == null || !IsUsableRouteFacilitySpriteId(type.SpriteId))
            return "";

        var icon = ShipIcon(type.SpriteId);
        return IsPlaceholderRouteFacilityIcon(icon) ? "" : icon;
    }

    private static bool IsUsableRouteFacilitySpriteId(string spriteId)
    {
        if (string.IsNullOrWhiteSpace(spriteId))
            return false;

        var lower = spriteId.Trim().ToLowerInvariant();
        return !lower.Contains("empty string")
            && !lower.Contains("no sprite")
            && !lower.Contains("missing sprite");
    }

    private static bool IsPlaceholderRouteFacilityIcon(string icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
            return true;

        var lower = icon.ToLowerInvariant();
        return lower.Contains("empty string")
            || lower.Contains("no sprite")
            || lower.Contains("missing sprite");
    }

    private static IEnumerable<LaunchVehicleType> EnumerateLaunchVehicleTypes(object allTypes)
    {
        if (allTypes == null)
            yield break;

        foreach (var memberName in new[] { "ListNotEmpty", "List", "list", "All", "all" })
        {
            var prop = allTypes.GetType().GetProperty(memberName);
            if (prop != null && prop.GetValue(allTypes, null) is IEnumerable propItems)
            {
                foreach (var item in propItems)
                    if (item is LaunchVehicleType type)
                        yield return type;
                yield break;
            }

            var field = allTypes.GetType().GetField(memberName);
            if (field != null && field.GetValue(allTypes) is IEnumerable fieldItems)
            {
                foreach (var item in fieldItems)
                    if (item is LaunchVehicleType type)
                        yield return type;
                yield break;
            }
        }
    }

    private static bool IsRepresentativeRouteFacilityLaunchType(LaunchVehicleType type, string category)
    {
        if (type == null || string.IsNullOrWhiteSpace(type.SpriteId))
            return false;

        var name = type.Name ?? "";
        var classified = ClassifyRouteFacilityLaunchCategory("", name);
        if (string.Equals(classified, category, StringComparison.Ordinal))
            return true;

        if (string.Equals(category, RouteLaunchCategoryLaunchPad, StringComparison.Ordinal))
            return type.FakeForFacility
                || name.IndexOf("pad", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("facility", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("launcher", StringComparison.OrdinalIgnoreCase) >= 0;

        return false;
    }

    private static int RouteFacilityLaunchRepresentativeRank(LaunchVehicleType type, string category)
    {
        if (type == null)
            return 100;

        var name = type.Name ?? "";
        if (string.Equals(category, RouteLaunchCategoryLaunchPad, StringComparison.Ordinal))
        {
            if (type.FakeForFacility)
                return 0;
            if (name.IndexOf("pad", StringComparison.OrdinalIgnoreCase) >= 0)
                return 1;
            if (name.IndexOf("facility", StringComparison.OrdinalIgnoreCase) >= 0)
                return 2;
            if (name.IndexOf("launcher", StringComparison.OrdinalIgnoreCase) >= 0)
                return 3;
        }

        if (ClassifyRouteFacilityLaunchCategory("", name) == category)
            return 0;
        return 50;
    }

    private static float RouteFacilityLaunchIconFontSize(string iconText)
    {
        return !string.IsNullOrWhiteSpace(iconText) && iconText.IndexOf("<sprite", StringComparison.OrdinalIgnoreCase) >= 0
            ? 18f
            : 12f;
    }

    private static List<RouteFacilityLaunchOptionUi> GetAvailableRouteFacilityLaunchOptions(ObjectInfo source, Company player)
    {
        var options = new Dictionary<string, RouteFacilityLaunchOptionUi>(StringComparer.Ordinal);
        if (source == null || player == null)
            return new List<RouteFacilityLaunchOptionUi>();

        var objectData = source.GetObjectInfoData(player);
        var seen = new HashSet<int>();
        var rows = source.GetListLaunchVehicle(player);
        if (rows != null)
        {
            foreach (var row in rows)
                AddRouteFacilityLaunchOptionForVehicle(options, seen, source, player, objectData, row?.launchVehicle);
        }

        if (source.ListLaunchVehicle != null)
        {
            foreach (var lv in source.ListLaunchVehicle)
                AddRouteFacilityLaunchOptionForVehicle(options, seen, source, player, objectData, lv);
        }

        AddBuiltRouteFacilityLaunchOptions(options, objectData);

        return RouteFacilityLaunchCategories
            .Select(category => options.TryGetValue(category, out var option) ? option : null)
            .Where(option => option != null)
            .ToList();
    }

    private static void AddBuiltRouteFacilityLaunchOptions(Dictionary<string, RouteFacilityLaunchOptionUi> options, ObjectInfoData objectData)
    {
        if (options == null || objectData?.ListFacility == null)
            return;

        foreach (var facility in objectData.ListFacility)
        {
            if (!TryGetBuiltEnabledRouteFacilityLaunchCategory(facility, out var category))
                continue;
            if (options.ContainsKey(category))
                continue;

            var iconSprite = string.Equals(category, RouteLaunchCategorySpaceElevator, StringComparison.Ordinal)
                ? null
                : RouteFacilityLaunchRepresentativeSprite(category);
            var icon = RouteFacilityLaunchRepresentativeIcon(category);
            if (string.IsNullOrWhiteSpace(icon))
            {
                var type = RouteFacilityLaunchFakeType(facility.facilityDescriptor as global::Data.ScriptableObject.GroundFacilityDescriptor);
                icon = RouteFacilityLaunchIconForFacility(facility.facilityDescriptor, type, category);
            }
            if (string.IsNullOrWhiteSpace(icon))
                icon = RouteFacilityLaunchFallbackIcon(category);

            var option = new RouteFacilityLaunchOptionUi
            {
                Category = category,
                IconSprite = iconSprite,
                Icon = icon,
                Count = (int)Math.Max(1, Math.Min(int.MaxValue, facility.Enabled)),
                IsAvailable = true
            };

            var detail = RouteFacilityLaunchDetail(facility, null);
            if (!string.IsNullOrWhiteSpace(detail))
                option.Details.Add(detail);
            options[category] = option;
        }
    }

    private static bool TryGetBuiltEnabledRouteFacilityLaunchCategory(Facility facility, out string category)
    {
        category = "";
        if (facility?.facilityDescriptor == null)
            return false;
        if (facility.BuildProgress < 1f || facility.Enabled <= 0 || facility.SinglePowerProductionMultiplier < 0.999)
            return false;

        category = GetRouteFacilityLaunchCategory(facility.facilityDescriptor);
        return IsRouteFacilityLaunchCategory(category)
            && IsRouteFacilityLaunchDescriptor(facility.facilityDescriptor, category);
    }

    private static string GetRouteFacilityLaunchCategory(global::Data.ScriptableObject.FacilityBaseDescriptor descriptor)
    {
        if (descriptor == null)
            return "";

        var type = RouteFacilityLaunchFakeType(descriptor as global::Data.ScriptableObject.GroundFacilityDescriptor);
        return ClassifyRouteFacilityLaunchCategory(
            RouteFacilityLaunchDescriptorSearchText(descriptor),
            RouteFacilityLaunchSearchText(type?.Name, type?.ID, type?.name));
    }

    private static bool IsRouteFacilityLaunchDescriptor(global::Data.ScriptableObject.FacilityBaseDescriptor descriptor,
        string category)
    {
        if (descriptor == null)
            return false;

        var normalized = NormalizeRouteFacilityLaunchCategory(category);
        if (string.Equals(normalized, RouteLaunchCategorySpaceElevator, StringComparison.Ordinal)
            && descriptor is global::Data.ScriptableObject.GroundFacilityDescriptor elevatorGround
            && elevatorGround.bonusData?.spaceElevatorPrefab3dView != null)
            return true;

        if (descriptor is global::Data.ScriptableObject.GroundFacilityDescriptor ground)
        {
            if (ground.facilityType.HasFlag(global::Data.ScriptableObject.FacilityBaseDescriptor.EFacilityType.LaunchFacility)
                || ground.bonusData?.bonus == EBonus.LaunchCostOptionInPlanMission)
                return true;

            var type = RouteFacilityLaunchFakeType(ground);
            if (type != null && RouteFacilityLaunchSearchTextMatchesCategory(
                    RouteFacilityLaunchSearchText(type.Name, type.ID, type.name),
                    normalized))
                return true;
        }

        return RouteFacilityLaunchSearchTextMatchesCategory(RouteFacilityLaunchDescriptorSearchText(descriptor), normalized);
    }

    private static bool RouteFacilityLaunchSearchTextMatchesCategory(string text, string category)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(category))
            return false;

        switch (NormalizeRouteFacilityLaunchCategory(category))
        {
            case RouteLaunchCategoryMagneticRails:
                return text.Contains("magnetic launch rail")
                    || text.Contains("magnetic rail")
                    || text.Contains("launch rail")
                    || text.Contains("magrail")
                    || text.Contains(" rail");
            case RouteLaunchCategoryLaunchPad:
                return text.Contains("launch pad")
                    || text.Contains("launchpad")
                    || text.Contains(" pad")
                    || text.Contains("launch facility")
                    || text.Contains("facility");
            case RouteLaunchCategoryRotary:
                return text.Contains("rotary") || text.Contains("spin");
            case RouteLaunchCategorySpaceElevator:
                return text.Contains("elevator");
            case RouteLaunchCategoryElectromagneticCatapult:
                return text.Contains("electromagnetic") || text.Contains("catapult");
            case RouteLaunchCategoryMassDriver:
                return text.Contains("mass driver");
            default:
                return false;
        }
    }

    private static void AddRouteFacilityLaunchOptionForVehicle(Dictionary<string, RouteFacilityLaunchOptionUi> options, HashSet<int> seen,
        ObjectInfo source, Company player, ObjectInfoData objectData, LaunchVehicle lv)
    {
        if (options == null || seen == null || source == null || player == null || lv?.launchVehicleType == null)
            return;
        if (lv.ID > 0 && !seen.Add(lv.ID))
            return;
        if (lv.GetCompany() != player || lv.objectInfo != source)
            return;

        var facility = objectData?.GetFakeLVFromFacilityReverse(lv);
        var category = GetRouteFacilityLaunchCategory(source, lv, facility);
        if (facility == null && !string.Equals(category, RouteLaunchCategorySpaceElevator, StringComparison.Ordinal))
            return;
        if (!IsRouteFacilityLaunchCategory(category))
            return;

        if (!options.TryGetValue(category, out var option))
        {
            var iconSprite = !string.Equals(category, RouteLaunchCategorySpaceElevator, StringComparison.Ordinal) && facility != null
                ? RouteFacilityLaunchDescriptorSprite(facility.facilityDescriptor)
                : null;
            var icon = RouteFacilityLaunchIconForFacility(facility?.facilityDescriptor, lv.launchVehicleType, category);
            option = new RouteFacilityLaunchOptionUi
            {
                Category = category,
                IconSprite = iconSprite,
                Icon = icon,
                IsAvailable = true
            };
            options[category] = option;
        }

        if (option.IconSprite == null
            && !string.Equals(category, RouteLaunchCategorySpaceElevator, StringComparison.Ordinal)
            && facility != null)
            option.IconSprite = RouteFacilityLaunchDescriptorSprite(facility.facilityDescriptor);

        if (string.IsNullOrWhiteSpace(option.Icon))
            option.Icon = RouteFacilityLaunchIconForFacility(facility?.facilityDescriptor, lv.launchVehicleType, category);

        option.Count++;
        var detail = RouteFacilityLaunchDetail(facility, lv);
        if (!string.IsNullOrWhiteSpace(detail)
            && !option.Details.Any(existing => string.Equals(existing, detail, StringComparison.OrdinalIgnoreCase)))
            option.Details.Add(detail);
    }

    private static string RouteFacilityLaunchDetail(Facility facility, LaunchVehicle lv)
    {
        var facilityName = FacilityLaunchDisplayName(facility?.facilityDescriptor);
        if (!string.IsNullOrWhiteSpace(facilityName))
            return facilityName;

        return lv?.launchVehicleType?.Name;
    }

    private static string GetRouteFacilityLaunchCategory(ObjectInfo source, LaunchVehicle lv, Facility facility)
    {
        if (facility != null)
        {
            var facilityName = FacilityLaunchDisplayName(facility.facilityDescriptor);
            if (string.IsNullOrWhiteSpace(facilityName))
                facilityName = facility.GetType().Name;
            return ClassifyRouteFacilityLaunchCategory(facilityName, lv?.launchVehicleType?.Name ?? "LV");
        }

        if (source?.IsUseInSpaceElevator == true && source.parentObjectInfo?.LowOrbitCustom != null)
            return RouteLaunchCategorySpaceElevator;

        return "standard-launch";
    }

    private static string ClassifyRouteFacilityLaunchCategory(string facilityName, string lvName)
    {
        var text = RouteFacilityLaunchSearchText(facilityName, lvName);
        if (text.Contains("elevator"))
            return RouteLaunchCategorySpaceElevator;
        if (text.Contains("rotary") || text.Contains("spin"))
            return RouteLaunchCategoryRotary;
        if (text.Contains("launch pad") || text.Contains("launchpad") || text.Contains(" pad"))
            return RouteLaunchCategoryLaunchPad;
        if (text.Contains("electromagnetic") || text.Contains("catapult"))
            return RouteLaunchCategoryElectromagneticCatapult;
        if (text.Contains("mass driver"))
            return RouteLaunchCategoryMassDriver;
        if (text.Contains("magnetic launch rail") || text.Contains("magnetic rail") || text.Contains("launch rail")
            || text.Contains("magrail") || text.Contains(" rail"))
            return RouteLaunchCategoryMagneticRails;
        return RouteLaunchCategoryLaunchPad;
    }

    private static bool IsRouteFacilityLaunchCategoryEnabled(Data.LogisticsRouteRecord route, string category)
    {
        NormalizeRouteFacilityLaunchSettings(route);
        var normalized = NormalizeRouteFacilityLaunchCategory(category);
        return route?.disabledFacilityLaunchCategories == null
            || !route.disabledFacilityLaunchCategories.Any(disabled =>
                string.Equals(NormalizeRouteFacilityLaunchCategory(disabled), normalized, StringComparison.Ordinal));
    }

    private static void SetRouteFacilityLaunchCategoryEnabled(Data.LogisticsRouteRecord route, string category, bool enabled)
    {
        if (route == null)
            return;

        NormalizeRouteFacilityLaunchSettings(route);
        var normalized = NormalizeRouteFacilityLaunchCategory(category);
        if (!IsRouteFacilityLaunchCategory(normalized))
            return;

        route.disabledFacilityLaunchCategories ??= new List<string>();
        route.disabledFacilityLaunchCategories.RemoveAll(disabled =>
            string.Equals(NormalizeRouteFacilityLaunchCategory(disabled), normalized, StringComparison.Ordinal));
        if (!enabled)
            route.disabledFacilityLaunchCategories.Add(normalized);
    }

    private static void NormalizeRouteFacilityLaunchSettings(Data.LogisticsRouteRecord route)
    {
        if (route == null)
            return;

        route.disabledFacilityLaunchCategories = (route.disabledFacilityLaunchCategories ?? new List<string>())
            .Select(category => category?.Trim().ToLowerInvariant())
            .Where(IsRouteFacilityLaunchCategory)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsRouteFacilityLaunchCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return false;

        var normalized = category.Trim().ToLowerInvariant();
        return RouteFacilityLaunchCategories.Any(candidate =>
            string.Equals(candidate, normalized, StringComparison.Ordinal));
    }

    private static string NormalizeRouteFacilityLaunchCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return RouteLaunchCategoryLaunchPad;

        var normalized = category.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case RouteLaunchCategoryMagneticRails:
            case RouteLaunchCategoryLaunchPad:
            case RouteLaunchCategoryRotary:
            case RouteLaunchCategorySpaceElevator:
            case RouteLaunchCategoryElectromagneticCatapult:
            case RouteLaunchCategoryMassDriver:
                return normalized;
            default:
                return RouteLaunchCategoryLaunchPad;
        }
    }

    private static string RouteFacilityLaunchButtonLabel(string category)
    {
        switch (NormalizeRouteFacilityLaunchCategory(category))
        {
            case RouteLaunchCategoryMagneticRails:
                return "Magnetic Rails";
            case RouteLaunchCategoryLaunchPad:
                return "Launch Pad";
            case RouteLaunchCategoryRotary:
                return "Rotary Launcher";
            case RouteLaunchCategorySpaceElevator:
                return "Space Elevator";
            case RouteLaunchCategoryElectromagneticCatapult:
                return "EM Catapult";
            case RouteLaunchCategoryMassDriver:
                return "Mass Driver";
            default:
                return "Launch Pad";
        }
    }

    private void ShowRouteLaunchVehicleCountEditor(LogisticsSection section, Data.LogisticsRouteRecord route,
        string typeId, int desiredCount = -1)
    {
        SetRouteEditorMainPageVisible(false);
        section.ClearContent();
        AddBigButton(section.ContentArea, "\u2190 Back", _runtimeStyle.BackButtonColor, () =>
        {
            ShowRouteEditor(section, route);
        });

        if (route == null || string.IsNullOrWhiteSpace(typeId))
        {
            ClearActiveRouteEditor();
            BuildRoutesSection();
            RebuildSectionLayout(section);
            return;
        }

        var source = MonoBehaviourSingleton<ObjectInfoManager>.Instance?.GetByID(route.sourceObjectId) ?? _currentObjectInfo;
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (source == null || player == null)
        {
            section.AddTextRow("Route source is unavailable.", _font, 13f, DisabledTextColor);
            RebuildSectionLayout(section);
            return;
        }

        var data = Data.LogisticsNetwork.GetOrCreate(source);
        Data.LogisticsNetwork.RefreshReservedLaunchVehicles(data);
        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? System.DateTime.Now;
        var assigned = Data.LogisticsNetwork.GetAllGhostLaunchVehicles()
            .Where(lv => lv != null
                && lv.assignedRouteId == route.routeId
                && string.Equals(lv.launchVehicleTypeId, typeId, System.StringComparison.Ordinal))
            .ToList();
        var releasable = assigned
            .Where(lv => lv.currentObjectId == source.id && Data.LogisticsNetwork.IsGhostLaunchVehicleReady(lv, now))
            .ToList();
        var locked = Mathf.Max(0, assigned.Count - releasable.Count);
        var reservable = GetReservableRouteLaunchVehicles(source, player, typeId);
        var max = assigned.Count + reservable.Count;
        var desired = desiredCount < 0
            ? assigned.Count
            : Mathf.Clamp(desiredCount, locked, max);

        var title = MakeTMP(section.ContentArea, LaunchVehicleTypeName(typeId), 14f, EngineAccentColor);
        title.rectTransform.sizeDelta = new Vector2(0, 22);

        section.AddTextRow($"Assigned {assigned.Count}  available {reservable.Count}", _font, 12.5f, SubtleTextColor);
        if (locked > 0)
            section.AddTextRow($"{locked} launch vehicles busy; minimum count is {locked}.", _font, 12.5f, WarningColor);

        var row = MakeHLRow(section.ContentArea, 28f, 6);
        row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
        var label = MakeTMP(row.transform, "Route count", _runtimeStyle.RowFontSize, _runtimeStyle.RowTextColor);
        var le = label.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;
        le.preferredWidth = 0f;

        AddSmallButton(row.transform, "-", _runtimeStyle.SmallButtonColor, () =>
        {
            ShowRouteLaunchVehicleCountEditor(section, route, typeId, Mathf.Max(locked, desired - GetSpacecraftStackClickStep()));
        }, 28f);
        AddFixedLabel(row.transform, $"{desired}/{max}", 62f);
        AddSmallButton(row.transform, "+", _runtimeStyle.SmallButtonColor, () =>
        {
            ShowRouteLaunchVehicleCountEditor(section, route, typeId, Mathf.Min(max, desired + GetSpacecraftStackClickStep()));
        }, 28f);
        AddSmallButton(row.transform, "Min", _runtimeStyle.SmallButtonColor, () =>
        {
            ShowRouteLaunchVehicleCountEditor(section, route, typeId, locked);
        }, 42f);
        AddSmallButton(row.transform, "Max", _runtimeStyle.SmallButtonColor, () =>
        {
            ShowRouteLaunchVehicleCountEditor(section, route, typeId, max);
        }, 42f);

        var confirmRow = MakeHLRow(section.ContentArea, 30f, 8);
        confirmRow.GetComponent<Image>().color = RowBgMutedColor;
        AddBigButtonInline(confirmRow.transform, "Apply", _runtimeStyle.ConfirmButtonColor, () =>
        {
            ApplyRouteLaunchVehicleCount(route, typeId, desired);
            ShowRouteEditor(section, route);
        });
        AddBigButtonInline(confirmRow.transform, "Cancel", _runtimeStyle.BackButtonColor, () => ShowRouteEditor(section, route));

        RebuildSectionLayout(section);
    }

    private void ApplyRouteLaunchVehicleCount(Data.LogisticsRouteRecord route, string typeId, int desiredCount)
    {
        if (route == null || string.IsNullOrWhiteSpace(typeId))
            return;

        var source = MonoBehaviourSingleton<ObjectInfoManager>.Instance?.GetByID(route.sourceObjectId) ?? _currentObjectInfo;
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        if (source == null || player == null)
            return;

        var data = Data.LogisticsNetwork.GetOrCreate(source);
        Data.LogisticsNetwork.RefreshReservedLaunchVehicles(data);
        var now = MonoBehaviourSingleton<TimeController>.Instance?.CurrentTime ?? System.DateTime.Now;
        var assigned = Data.LogisticsNetwork.GetAllGhostLaunchVehicles()
            .Where(lv => lv != null
                && lv.assignedRouteId == route.routeId
                && string.Equals(lv.launchVehicleTypeId, typeId, System.StringComparison.Ordinal))
            .ToList();
        var releasable = assigned
            .Where(lv => lv.currentObjectId == source.id && Data.LogisticsNetwork.IsGhostLaunchVehicleReady(lv, now))
            .OrderByDescending(lv => lv.ledgerId)
            .ToList();
        var reservable = GetReservableRouteLaunchVehicles(source, player, typeId);
        var locked = Mathf.Max(0, assigned.Count - releasable.Count);
        var desired = Mathf.Clamp(desiredCount, locked, assigned.Count + reservable.Count);

        if (desired < assigned.Count)
        {
            var releaseCount = assigned.Count - desired;
            foreach (var record in releasable.Take(releaseCount).ToList())
            {
                if (!Data.LogisticsNetwork.ReleaseGhostLaunchVehicle(source, record.ledgerId, out var reason))
                    LogisticsObserver.LogWarning($"ROUTE launch vehicle release failed: lv={record.typeName ?? record.launchVehicleTypeId} reason={reason}");
            }
            return;
        }

        var addCount = desired - assigned.Count;
        if (addCount <= 0)
            return;

        foreach (var lv in reservable.Take(addCount).ToList())
        {
            if (!Data.LogisticsNetwork.ReserveLaunchVehicle(source, lv, out var reason, route.routeId))
                LogisticsObserver.LogWarning($"ROUTE reserve-lv failed: {reason}");
        }
    }

    private static List<LaunchVehicle> GetReservableRouteLaunchVehicles(ObjectInfo source, Company player, string typeId)
    {
        if (source == null || player == null || string.IsNullOrWhiteSpace(typeId))
            return new List<LaunchVehicle>();

        return (source.ListLaunchVehicle ?? new List<LaunchVehicle>())
            .Where(lv => lv != null
                && string.Equals(lv.launchVehicleType?.ID, typeId, System.StringComparison.Ordinal)
                && Data.LogisticsNetwork.IsLaunchVehicleReservableAt(source, lv, player, out _))
            .OrderBy(lv => lv.ID)
            .ToList();
    }

    private void ShowRouteResourcePicker(LogisticsSection section, Data.LogisticsRouteRecord route)
    {
        SetRouteEditorMainPageVisible(false);
        section.ClearContent();
        AddBigButton(section.ContentArea, "\u2190 Back", _runtimeStyle.BackButtonColor, () =>
        {
            ShowRouteEditor(section, route);
        });

        var am = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance;
        if (am?.AllResourceDefinitions == null)
        {
            section.AddTextRow("Resource list not available.", _font);
            RebuildSectionLayout(section);
            return;
        }

        var existing = new HashSet<string>((route.resources ?? new List<Data.LogisticsRouteResourceRule>())
            .Where(rule => rule != null)
            .Select(rule => rule.ResourceDefinition?.ID ?? rule.resourceDef?.id)
            .Where(id => !string.IsNullOrWhiteSpace(id)));

        foreach (var rd in am.AllResourceDefinitions.ListNotEmpty
                     .OrderBy(rd => ResourceSortName(rd), System.StringComparer.OrdinalIgnoreCase))
        {
            var captured = rd;
            var already = existing.Contains(rd.ID);
            var row = MakeHLRow(section.ContentArea, 24f, 0);
            row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
            MakeTMP(row.transform, already ? $"{ResourceLabel(rd)} (already on route)" : ResourceLabel(rd),
                13f, already ? DisabledTextColor : _runtimeStyle.RowTextColor);
            if (already)
                continue;

            var btn = MakeRowButton(row);
            btn.onClick.AddListener(() =>
            {
                ShowRouteResourceInput(section, route, captured, -1);
            });
        }

        RebuildSectionLayout(section);
    }

    private void ShowRouteResourceInput(LogisticsSection section, Data.LogisticsRouteRecord route,
        ResourceDefinition rd, int editIndex)
    {
        SetRouteEditorMainPageVisible(false);
        section.ClearContent();
        var sourceKeep = 0d;
        var destinationTarget = 0d;
        var editingKeep = true;
        var isActive = true;
        var priority = 0;
        var isEditing = route?.resources != null && editIndex >= 0 && editIndex < route.resources.Count;
        if (isEditing)
        {
            var existing = route.resources[editIndex];
            sourceKeep = System.Math.Max(0, existing.sourceKeep);
            destinationTarget = System.Math.Max(0, existing.destinationTarget);
            isActive = existing.isActive;
            priority = NormalizeRouteResourcePriority(existing.priority);
        }

        AddBigButton(section.ContentArea, "\u2190 Back", _runtimeStyle.BackButtonColor, () =>
        {
            if (isEditing)
                ShowRouteEditor(section, route);
            else
                ShowRouteResourcePicker(section, route);
        });

        var title = MakeTMP(section.ContentArea, $"Route resource: {ResourceLabel(rd)}", 14f, EngineAccentColor);
        title.rectTransform.sizeDelta = new Vector2(0, 22);

        var optionRow = MakeHLRow(section.ContentArea, 28f, 6);
        Button activeButton = null;
        TextMeshProUGUI activeButtonLabel = null;
        var priorityButtons = new List<(int Priority, Button Button)>();

        void RefreshResourceOptions()
        {
            if (activeButton != null)
                SetOutlinedButtonState(activeButton, isActive);
            if (activeButtonLabel != null)
                activeButtonLabel.text = isActive ? "Active" : "Paused";
            foreach (var pair in priorityButtons)
            {
                if (pair.Button != null)
                    SetOutlinedButtonState(pair.Button, pair.Priority == priority);
            }
        }

        activeButton = AddBigButtonInline(optionRow.transform, "", isActive ? _runtimeStyle.ToggleOnColor : _runtimeStyle.ToggleOffColor, () =>
        {
            isActive = !isActive;
            RefreshResourceOptions();
        });
        activeButtonLabel = activeButton.GetComponentInChildren<TextMeshProUGUI>();

        var priorityRow = MakeHLRow(section.ContentArea, 28f, 4);
        foreach (var option in new[] { -1, 0, 1, 2 })
        {
            var capturedPriority = option;
            var button = AddSmallButton(priorityRow.transform, RoutePriorityLabel(option),
                _runtimeStyle.ToggleOffColor, () =>
                {
                    priority = capturedPriority;
                    RefreshResourceOptions();
                }, option == 2 ? 74f : 66f);
            priorityButtons.Add((option, button));
        }
        RefreshResourceOptions();

        var editRow = MakeHLRow(section.ContentArea, 28f, 6);
        TextMeshProUGUI keepButtonLabel = null;
        TextMeshProUGUI targetButtonLabel = null;
        Button keepButton = null;
        Button targetButton = null;
        TextMeshProUGUI amountDisplay = null;

        void RefreshModeButtons()
        {
            if (keepButton != null)
                SetOutlinedButtonState(keepButton, editingKeep);
            if (targetButton != null)
                SetOutlinedButtonState(targetButton, !editingKeep);
            if (keepButtonLabel != null)
                keepButtonLabel.text = $"Keep: {FormatNiceAmount(sourceKeep)}";
            if (targetButtonLabel != null)
                targetButtonLabel.text = $"Target: {FormatNiceAmount(destinationTarget)}";
        }

        keepButton = AddBigButtonInline(editRow.transform, "", _runtimeStyle.ToggleOffColor, () =>
        {
            editingKeep = true;
            RefreshAmount();
        });
        keepButtonLabel = keepButton.GetComponentInChildren<TextMeshProUGUI>();
        targetButton = AddBigButtonInline(editRow.transform, "", _runtimeStyle.ToggleOffColor, () =>
        {
            editingKeep = false;
            RefreshAmount();
        });
        targetButtonLabel = targetButton.GetComponentInChildren<TextMeshProUGUI>();

        var amountRow = MakeHLRow(section.ContentArea, 34f, 0);
        amountDisplay = MakeTMP(amountRow.transform, "", 22f, Color.white);
        amountDisplay.alignment = TextAlignmentOptions.Center;

        void RefreshAmount()
        {
            amountDisplay.text = editingKeep
                ? $"Keep: {FormatNiceAmount(sourceKeep)}"
                : $"Target: {FormatNiceAmount(destinationTarget)}";
            RefreshModeButtons();
        }

        void AddAmount(double delta)
        {
            if (editingKeep)
                sourceKeep = System.Math.Max(0, sourceKeep + delta);
            else
                destinationTarget = System.Math.Max(0, destinationTarget + delta);
            RefreshAmount();
        }

        RefreshAmount();
        AddAmountStepRows(section.ContentArea, AddAmount);

        var confirmRow = new GameObject("ConfirmRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        confirmRow.transform.SetParent(section.ContentArea, false);
        confirmRow.GetComponent<LayoutElement>().preferredHeight = 32f;
        confirmRow.GetComponent<HorizontalLayoutGroup>().spacing = 8f;
        AddBigButtonInline(confirmRow.transform, "Confirm", _runtimeStyle.ConfirmButtonColor, () =>
        {
            if (isEditing)
            {
                var existing = route.resources[editIndex];
                existing.resourceDef = (ResourceDefinitionIDSave)rd;
                existing.ResourceDefinition = rd;
                existing.sourceKeep = sourceKeep;
                existing.destinationTarget = destinationTarget;
                existing.isActive = isActive;
                existing.priority = priority;
                existing.statusNote = null;
            }
            else
            {
                var created = Data.LogisticsNetwork.AddRouteResource(route, rd, sourceKeep, destinationTarget);
                if (created != null)
                {
                    created.isActive = isActive;
                    created.priority = priority;
                }
            }
            ShowRouteEditor(section, route);
        });
        AddBigButtonInline(confirmRow.transform, "Cancel", _runtimeStyle.BackButtonColor, () => ShowRouteEditor(section, route));

        RebuildSectionLayout(section);
    }

    private void ShowRouteShipPicker(LogisticsSection section, Data.LogisticsRouteRecord route, Dictionary<string, int> selectedCounts = null)
    {
        SetRouteEditorMainPageVisible(false);
        selectedCounts ??= new Dictionary<string, int>();
        section.ClearContent();
        AddBigButton(section.ContentArea, "\u2190 Back", _runtimeStyle.BackButtonColor, () =>
        {
            ShowRouteEditor(section, route);
        });

        var source = _currentObjectInfo;
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var adoptable = UnityEngine.Object.FindObjectsOfType<Spacecraft>()
            .Where(sc => Data.LogisticsNetwork.IsSpacecraftAdoptableAt(source, sc, player, out _))
            .ToList();

        var typeIds = adoptable.Select(sc => sc.spacecraftType?.ID)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .OrderBy(GhostShipTypeName)
            .ToList();

        if (typeIds.Count == 0)
        {
            section.AddTextRow("No idle spacecraft available at this route source.", _font, 13f, DisabledTextColor);
            RebuildSectionLayout(section);
            return;
        }

        foreach (var typeId in typeIds)
        {
            var shipList = adoptable.Where(sc => sc.spacecraftType?.ID == typeId).OrderBy(sc => sc.GetSpacecraftName()).ToList();
            var total = shipList.Count;
            var selected = Mathf.Clamp(selectedCounts.TryGetValue(typeId, out var count) ? count : 1, 1, total);
            selectedCounts[typeId] = selected;

            var row = MakeHLRow(section.ContentArea, 28f, 6);
            row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
            var label = MakeTMP(row.transform, $"{total} x {GhostShipTypeName(typeId)}", _runtimeStyle.RowFontSize, _runtimeStyle.RowTextColor);
            var le = label.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredWidth = 0f;
            AddSmallButton(row.transform, "-", _runtimeStyle.SmallButtonColor, () =>
            {
                selectedCounts[typeId] = Mathf.Max(1, selected - GetSpacecraftStackClickStep());
                ShowRouteShipPicker(section, route, selectedCounts);
            }, 28f);
            AddFixedLabel(row.transform, $"{selected}/{total}", 52f);
            AddSmallButton(row.transform, "+", _runtimeStyle.SmallButtonColor, () =>
            {
                selectedCounts[typeId] = Mathf.Min(total, selected + GetSpacecraftStackClickStep());
                ShowRouteShipPicker(section, route, selectedCounts);
            }, 28f);
            AddSmallButton(row.transform, "All", _runtimeStyle.SmallButtonColor, () =>
            {
                selectedCounts[typeId] = total;
                ShowRouteShipPicker(section, route, selectedCounts);
            }, 42f);
            AddSmallButton(row.transform, "Assign", _runtimeStyle.SmallButtonPositiveColor, () =>
            {
                var remaining = selected;
                foreach (var sc in shipList.Take(remaining).ToList())
                {
                    if (Data.LogisticsNetwork.AdoptSpacecraft(source, sc, out var reason))
                    {
                        var adopted = Data.LogisticsNetwork.GetAllGhostCraft()
                            .Where(c => c != null && c.originalShipId == sc.ID && c.currentObjectId == source.id)
                            .OrderByDescending(c => c.ledgerId)
                            .FirstOrDefault();
                        if (adopted != null)
                            Data.LogisticsNetwork.AssignGhostCraftToRoute(route.routeId, adopted, out _);
                    }
                    else
                    {
                        LogisticsObserver.LogWarning($"ROUTE adopt failed: {reason}");
                    }
                }
                ShowRouteEditor(section, route);
            }, 76f);
        }

        RebuildSectionLayout(section);
    }

    private void ShowRouteLaunchVehiclePicker(LogisticsSection section, Data.LogisticsRouteRecord route, Dictionary<string, int> selectedCounts = null)
    {
        SetRouteEditorMainPageVisible(false);
        selectedCounts ??= new Dictionary<string, int>();
        section.ClearContent();
        AddBigButton(section.ContentArea, "\u2190 Back", _runtimeStyle.BackButtonColor, () =>
        {
            ShowRouteEditor(section, route);
        });

        var source = _currentObjectInfo;
        var player = MonoBehaviourSingleton<GameManager>.Instance?.Player;
        var data = Data.LogisticsNetwork.GetOrCreate(source);
        Data.LogisticsNetwork.RefreshReservedLaunchVehicles(data);
        var reservable = (source?.ListLaunchVehicle ?? new List<LaunchVehicle>())
            .Where(lv => Data.LogisticsNetwork.IsLaunchVehicleReservableAt(source, lv, player, out _))
            .ToList();

        var typeIds = reservable.Select(lv => lv.launchVehicleType?.ID)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .OrderBy(LaunchVehicleTypeName)
            .ToList();

        if (typeIds.Count == 0)
        {
            section.AddTextRow("No idle launch vehicles available at this route source.", _font, 13f, DisabledTextColor);
            RebuildSectionLayout(section);
            return;
        }

        foreach (var typeId in typeIds)
        {
            var vehicleList = reservable.Where(lv => lv.launchVehicleType?.ID == typeId).OrderBy(lv => lv.ID).ToList();
            var total = vehicleList.Count;
            var selected = Mathf.Clamp(selectedCounts.TryGetValue(typeId, out var count) ? count : 1, 1, total);
            selectedCounts[typeId] = selected;

            var row = MakeHLRow(section.ContentArea, 28f, 6);
            row.GetComponent<Image>().color = _runtimeStyle.RowBackgroundColor;
            var label = MakeTMP(row.transform, $"{total} x {LaunchVehicleTypeName(typeId)}", _runtimeStyle.RowFontSize, _runtimeStyle.RowTextColor);
            var le = label.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredWidth = 0f;
            AddSmallButton(row.transform, "-", _runtimeStyle.SmallButtonColor, () =>
            {
                selectedCounts[typeId] = Mathf.Max(1, selected - GetSpacecraftStackClickStep());
                ShowRouteLaunchVehiclePicker(section, route, selectedCounts);
            }, 28f);
            AddFixedLabel(row.transform, $"{selected}/{total}", 52f);
            AddSmallButton(row.transform, "+", _runtimeStyle.SmallButtonColor, () =>
            {
                selectedCounts[typeId] = Mathf.Min(total, selected + GetSpacecraftStackClickStep());
                ShowRouteLaunchVehiclePicker(section, route, selectedCounts);
            }, 28f);
            AddSmallButton(row.transform, "All", _runtimeStyle.SmallButtonColor, () =>
            {
                selectedCounts[typeId] = total;
                ShowRouteLaunchVehiclePicker(section, route, selectedCounts);
            }, 42f);
            AddSmallButton(row.transform, "Assign", _runtimeStyle.SmallButtonPositiveColor, () =>
            {
                var remaining = selected;
                foreach (var lv in vehicleList.Take(remaining).ToList())
                {
                    if (!Data.LogisticsNetwork.ReserveLaunchVehicle(source, lv, out var reason, route.routeId))
                        LogisticsObserver.LogWarning($"ROUTE reserve-lv failed: {reason}");
                }
                ShowRouteEditor(section, route);
            }, 76f);
        }

        RebuildSectionLayout(section);
    }

    private void BuildGetSection()
    {
        _getSection.ClearContent();
        var data = Data.LogisticsNetwork.Get(_currentObjectInfo);
        var requestCount = data?.requests.Count ?? 0;
        LogisticsObserver.LogVerbose($"BuildGet for {_currentObjectInfo?.ObjectName}: {requestCount} requests");

        AddBigButton(_getSection.ContentArea, "+ Add Inbound Rule", _runtimeStyle.ConfirmButtonColor, () =>
        {
            ShowResourcePicker(_getSection, true);
        });

        if (requestCount > 0)
        {
            foreach (var item in data.requests
                         .Select((request, index) => new { Request = request, Index = index })
                         .OrderBy(x => ResourceSortName(x.Request?.ResourceDefinition, x.Request?.resourceDef?.id), System.StringComparer.OrdinalIgnoreCase)
                         .ThenBy(x => x.Index))
            {
                var req = item.Request;
                var idx = item.Index;
                var rd = req.ResourceDefinition;
                var displayName = ResourceLabel(rd, req.resourceDef?.id);
                var statusStr = StatusToString(req.status);
                var transitStr = BuildTransitInfoSuffix(req, rd);

                var row = MakeHLRow(_getSection.ContentArea, 24f, 8);
                var amountText = req.useMinimumAmount
                    ? $"target {FormatNiceAmount(req.requestedAmount)}, min {FormatNiceAmount(System.Math.Min(req.minimumAmount, req.requestedAmount))}"
                    : FormatNiceAmount(req.requestedAmount);
                var labelTmp = MakeTMP(row.transform, $"{displayName}: {amountText}  [{statusStr}]{transitStr}", 13, StatusColor(req.status));
                labelTmp.enableWordWrapping = true;
                labelTmp.overflowMode = TextOverflowModes.Overflow;
                labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
                var labelLe = labelTmp.gameObject.AddComponent<LayoutElement>();
                labelLe.flexibleWidth = 1f;
                labelLe.preferredWidth = 0f;
                if (rd != null)
                {
                    var editButton = MakeRowButton(row);
                    editButton.onClick.AddListener(() => ShowAmountInput(_getSection, rd, true, true, idx));
                }
                MakeXButton(row.transform, () =>
                {
                    var capturedOi = _currentObjectInfo;
                    if (LogisticsObserver.VerboseLoggingEnabled)
                        LogisticsObserver.Log($"X clicked on INBOUND req idx={idx} capturedOi=\"{capturedOi?.ObjectName}\"(id={capturedOi?.id})");
                    Data.LogisticsNetwork.RemoveRequest(capturedOi, idx);
                    BuildGetSection();
                    RebuildSectionLayout(_getSection);
                });
            }
        }
        else
        {
            _getSection.AddTextRow("No inbound rules configured.", _font, 13f, DisabledTextColor);
        }
    }

    private string BuildTransitInfoSuffix(Data.LogisticsRequest req, ResourceDefinition rd)
    {
        if (req == null || rd == null || req.status != Data.LogisticsRequestStatus.InProgress)
            return "";

        var ghostFlight = FindInboundGhostFlight(rd);
        if (ghostFlight != null)
            return Logic.LogisticsStrings.TransitArrivesOnly(ghostFlight.arrivalDate.ToString("yyyy MMM d", LEManager.GetCultureInfoForDateTrajectory()));

        return "";
    }

    private Data.GhostFlightRecord FindInboundGhostFlight(ResourceDefinition rd)
    {
        if (_currentObjectInfo == null || rd == null)
            return null;
        return Data.LogisticsNetwork.GetAllGhostFlights()
            .Where(f => f != null
                && !f.isReturnFlight
                && f.toObjectId == _currentObjectInfo.id
                && GhostFlightCargoAmount(f, rd) > 0
                && (f.status == Data.GhostFlightStatus.Outbound || f.status == Data.GhostFlightStatus.Planned))
            .OrderBy(f => f.arrivalDate)
            .FirstOrDefault();
    }

    private void BuildSendSection()
    {
        _sendSection.ClearContent();
        var data = Data.LogisticsNetwork.Get(_currentObjectInfo);
        var providerCount = data?.providers.Count ?? 0;

        AddBigButton(_sendSection.ContentArea, "+ Add Outbound Rule", _runtimeStyle.ActionButtonColor, () =>
        {
            ShowResourcePicker(_sendSection, false);
        });

        if (providerCount > 0)
        {
            foreach (var item in data.providers
                         .Select((provider, index) => new { Provider = provider, Index = index })
                         .OrderBy(x => ResourceSortName(x.Provider?.ResourceDefinition, x.Provider?.resourceDef?.id), System.StringComparer.OrdinalIgnoreCase)
                         .ThenBy(x => x.Index))
            {
                var prov = item.Provider;
                var idx = item.Index;
                var rd = prov.ResourceDefinition;
                var displayName = ResourceLabel(rd, prov.resourceDef?.id);

                var row = MakeHLRow(_sendSection.ContentArea, 24f, 8);
                var labelTmp = MakeTMP(row.transform, $"{displayName}: ship surplus above {FormatNiceAmount(prov.minimumKeep)}", 13, SecondaryTextColor);
                labelTmp.enableWordWrapping = true;
                labelTmp.overflowMode = TextOverflowModes.Overflow;
                labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
                var labelLe = labelTmp.gameObject.AddComponent<LayoutElement>();
                labelLe.flexibleWidth = 1f;
                labelLe.preferredWidth = 0f;
                if (rd != null)
                {
                    var editButton = MakeRowButton(row);
                    editButton.onClick.AddListener(() => ShowAmountInput(_sendSection, rd, false, true, idx));
                }
                MakeXButton(row.transform, () =>
                {
                    var capturedOi = _currentObjectInfo;
                    if (LogisticsObserver.VerboseLoggingEnabled)
                        LogisticsObserver.Log($"X clicked on OUTBOUND prov idx={idx} capturedOi=\"{capturedOi?.ObjectName}\"(id={capturedOi?.id})");
                    Data.LogisticsNetwork.RemoveProvider(capturedOi, idx);
                    BuildSendSection();
                    RebuildSectionLayout(_sendSection);
                });
            }
        }
        else
        {
            _sendSection.AddTextRow("No outbound rules configured.", _font, 13f, DisabledTextColor);
        }
    }

    private void AddRouteGhostFlightsSection(LogisticsSection section, Data.LogisticsRouteRecord route)
    {
        if (section == null || route == null || _currentObjectInfo == null) return;

        LogisticsObserver.NormalizeGhostConvoys();
        var data = Data.LogisticsNetwork.GetOrCreate(_currentObjectInfo);
        var ghostFlights = (data?.ghostFlights ?? new List<Data.GhostFlightRecord>())
            .Where(f => f != null
                && f.routeId == route.routeId
                && f.status != Data.GhostFlightStatus.Complete
                && f.status != Data.GhostFlightStatus.Cancelled)
            .OrderBy(f => f.arrivalDate)
            .ThenBy(f => f.departureDate)
            .ToList();
        if (ghostFlights.Count == 0)
            return;

        var flightsSection = CreateInlineSection(section.ContentArea, "FLIGHTS", "Route Traffic");
        AddGhostFlightTableHeader(flightsSection);
        AddVirtualGhostFlightTableRows(flightsSection, ghostFlights);
    }

    private void AddGhostFlightTableHeader(LogisticsSection section)
    {
        var row = MakeHLRow(section.ContentArea, 22f, 4);
        row.GetComponent<Image>().color = WithAlpha(BorderColor, 0.58f);
        AddTableCell(row.transform, "Ships", 68f, 11f, TertiaryTextColor);
        AddTableCell(row.transform, "Lane", 0f, 11f, TertiaryTextColor, TextAlignmentOptions.MidlineLeft, true, false);
        AddTableCell(row.transform, "Fuel", 86f, 11f, TertiaryTextColor, TextAlignmentOptions.MidlineRight);
        AddTableCell(row.transform, "Arrives", 82f, 11f, TertiaryTextColor, TextAlignmentOptions.MidlineRight);
    }

    private void AddGhostFlightTableRow(LogisticsSection section, Data.GhostFlightRecord flight)
    {
        var row = MakeVLRow(section.ContentArea, GhostFlightRowHeight, 0f);
        PopulateGhostFlightTableRow(row, flight);
    }

    private void AddVirtualGhostFlightTableRows(LogisticsSection section, List<Data.GhostFlightRecord> flights)
    {
        if (section == null || flights == null || flights.Count == 0)
            return;

        AddVirtualFixedList(section.ContentArea, "GhostFlightVirtualList", flights.Count, GhostFlightRowHeight,
            (parent, height) => MakeVirtualVLRow(parent, height, 0f),
            (row, index) => PopulateGhostFlightTableRow(row, flights[index]));
    }

    private void PopulateGhostFlightTableRow(GameObject row, Data.GhostFlightRecord flight)
    {
        if (row == null || flight == null)
            return;

        row.GetComponent<Image>().color = RowBgMutedColor;

        var topLine = MakeHLContainer(row.transform, 22f, 4f);
        AddTableCell(topLine.transform, GhostFlightCraftName(flight), 68f, 12f, SecondaryTextColor);
        AddTableCell(topLine.transform, CompactRouteLane(flight.fromObjectId, flight.toObjectId), 0f, 12f,
            PrimaryTextColor, TextAlignmentOptions.MidlineLeft, true, false);
        AddTableCell(topLine.transform, BuildGhostFlightFuelLabel(flight), 86f, 11.5f, SecondaryTextColor,
            TextAlignmentOptions.MidlineRight);
        AddTableCell(topLine.transform, flight.arrivalDate.ToString("yyyy.MM.dd"), 82f, 12f, TertiaryTextColor,
            TextAlignmentOptions.MidlineRight);

        var manifestLine = MakeHLContainer(row.transform, 18f, 4f);
        AddSpacer(manifestLine.transform, 72f);
        var cargo = BuildGhostFlightCargoLabel(flight);
        var manifestText = string.IsNullOrWhiteSpace(cargo) ? "<alpha=#00>.</alpha>" : cargo;
        var manifestLabel = AddTableCell(manifestLine.transform, manifestText, 0f, 11.5f, SecondaryTextColor,
            TextAlignmentOptions.MidlineLeft, true, false);
        manifestLabel.overflowMode = TextOverflowModes.Overflow;
    }

    private string BuildGhostFlightLabel(Data.GhostFlightRecord flight)
    {
        var craft = GhostFlightCraftName(flight);
        var cargo = BuildGhostFlightCargoLabel(flight);
        var fuel = BuildGhostFlightFuelLabel(flight);
        var fuelPart = string.IsNullOrWhiteSpace(fuel) ? "" : $"  {fuel}";
        var routeLine = $"{craft}: {CompactRouteLane(flight.fromObjectId, flight.toObjectId)}{fuelPart}  {flight.arrivalDate:yyyy.MM.dd}";
        return string.IsNullOrWhiteSpace(cargo) ? $"{routeLine}\n<alpha=#00>.</alpha>" : $"{routeLine}\n{cargo}";
    }

    private static string BuildGhostFlightFuelLabel(Data.GhostFlightRecord flight)
    {
        if (flight == null)
            return "";

        var fuel = Math.Max(0, flight.outboundFuel);
        if (fuel <= 0.001)
            return "";

        var rd = ResolveResource(flight.fuelResourceId);
        return $"{CompactResourceLabel(rd, flight.fuelResourceId)}x{FormatNiceAmount(fuel)}";
    }

    private static string BuildGhostFlightCargoLabel(Data.GhostFlightRecord flight)
    {
        var manifest = flight?.cargoManifest?
            .Where(item => item != null
                && !string.IsNullOrWhiteSpace(item.resourceId)
                && item.cargoAmount > 0)
            .ToList() ?? new List<Data.GhostFlightCargoRecord>();
        if (manifest.Count == 0)
            return "";

        var parts = manifest
            .OrderBy(item => ResourceSortName(ResolveResource(item.resourceId), item.resourceId), System.StringComparer.OrdinalIgnoreCase)
            .Select(item => $"{CompactResourceLabel(ResolveResource(item.resourceId), item.resourceId)}x{FormatNiceAmount(item.cargoAmount)}")
            .ToList();
        var cargo = string.Join(", ", parts);
        var supplyConsumed = manifest.Sum(item => System.Math.Max(0, item.supplyConsumed));
        if (supplyConsumed > 0)
            cargo += $", {CompactResourceLabel(ResolveSupplyResource())}x{FormatNiceAmount(supplyConsumed)}";
        return cargo;
    }

    private static double GhostFlightCargoAmount(Data.GhostFlightRecord flight, ResourceDefinition rd)
    {
        if (flight == null || rd == null)
            return 0;

        return flight.cargoManifest?
            .Where(item => item != null
                && string.Equals(item.resourceId, rd.ID, System.StringComparison.Ordinal))
            .Sum(item => System.Math.Max(0, item.cargoAmount)) ?? 0;
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
            var color = isAvailable ? PrimaryTextColor : DisabledTextColor;
            var label = isAvailable ? ResourceLabel(rd) : $"{ResourceLabel(rd)} (not available)";
            MakeTMP(row.transform, label, 13, color);

            var btn = MakeRowButton(row);
            btn.onClick.AddListener(() =>
            {
                ShowAmountInput(sectionRef, rdCaptured, isGetCaptured, isAvailable);
            });
        }

        RebuildSectionLayout(section);
    }

    private bool _inputConfirmed;

    private void ShowAmountInput(LogisticsSection section, ResourceDefinition rd, bool isGet, bool isAvailable = true, int editIndex = -1)
    {
        var capturedOi = _currentObjectInfo;
        if (LogisticsObserver.VerboseLoggingEnabled)
            LogisticsObserver.Log($"ShowAmountInput: rd={rd.ID} isGet={isGet} editIndex={editIndex} capturedOi=\"{capturedOi?.ObjectName}\"(id={capturedOi?.id})");
        _inputConfirmed = false;
        double currentAmount = 0;
        double targetAmount = 0;
        double minimumAmount = 0;
        bool useMinimum = false;
        bool editingMinimum = false;
        var isEditing = editIndex >= 0;
        var existingData = Data.LogisticsNetwork.Get(capturedOi);
        if (isEditing && isGet && existingData != null && editIndex < existingData.requests.Count)
        {
            var existing = existingData.requests[editIndex];
            targetAmount = System.Math.Max(0, existing.requestedAmount);
            minimumAmount = System.Math.Max(0, System.Math.Min(existing.minimumAmount, targetAmount));
            useMinimum = existing.useMinimumAmount;
            currentAmount = targetAmount;
        }
        else if (isEditing && !isGet && existingData != null && editIndex < existingData.providers.Count)
        {
            var existing = existingData.providers[editIndex];
            currentAmount = System.Math.Max(0, existing.minimumKeep);
        }

        section.ClearContent();

        void ReturnToList()
        {
            if (isGet) BuildGetSection(); else BuildSendSection();
            RebuildSectionLayout(section);
        }

        AddBigButton(section.ContentArea, isEditing ? "\u2190 Back" : "\u2190 Back to resources", _runtimeStyle.BackButtonColor, () =>
        {
            if (isEditing)
                ReturnToList();
            else
                ShowResourcePicker(section, isGet);
        });

        if (!isAvailable)
        {
            var warnTmp = MakeTMP(section.ContentArea, "WARNING: Resource not currently available", 12, WarningColor);
            warnTmp.rectTransform.sizeDelta = new Vector2(0, 20);
        }

        var titlePrefix = isEditing
            ? (isGet ? "Edit inbound rule" : "Edit outbound rule")
            : (isGet ? "Inbound target" : "Outbound surplus");
        var titleLabel = MakeTMP(section.ContentArea, $"{titlePrefix}: {ResourceLabel(rd)}", 14, EngineAccentColor);
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
            amountDisplay.text = FormatNiceAmount(currentAmount);

            if (isGet)
            {
                amountDisplay.text = (editingMinimum ? "Minimum: " : "Target: ") + amountDisplay.text;
                if (minimumAmount > targetAmount)
                    minimumAmount = targetAmount;
                if (targetSummary != null)
                    targetSummary.text = $"Target: {FormatNiceAmount(targetAmount)}";
                if (minimumSummary != null)
                    minimumSummary.text = useMinimum ? $"Minimum: {FormatNiceAmount(minimumAmount)}" : "Minimum: off";
            }
            else
            {
                amountDisplay.text = "Ship above: " + amountDisplay.text;
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
            Button minimumToggleButton = null;
            void RefreshMinimumToggle()
            {
                if (minimumToggleLabel != null)
                    minimumToggleLabel.text = useMinimum ? "[X] Minimum threshold" : "[ ] Minimum threshold";
                if (minimumToggleButton != null)
                    SetOutlinedButtonState(minimumToggleButton, useMinimum);
            }
            var minimumToggleGo = new GameObject("MinimumToggle", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            minimumToggleGo.transform.SetParent(minimumToggleRow.transform, false);
            var minimumToggleLayout = minimumToggleGo.GetComponent<LayoutElement>();
            minimumToggleLayout.preferredHeight = 28f;
            minimumToggleLayout.minWidth = 160f;
            minimumToggleLayout.flexibleWidth = 1f;
            minimumToggleGo.GetComponent<Image>().color = ButtonFillColor;
            minimumToggleButton = minimumToggleGo.GetComponent<Button>();
            minimumToggleButton.navigation = new Navigation { mode = Navigation.Mode.None };
            minimumToggleButton.transition = Selectable.Transition.None;
            var minimumToggleStyle = ButtonStyle(ButtonTone.Neutral);
            minimumToggleLabel = MakeTMP(minimumToggleGo.transform, "", 13, minimumToggleStyle.NormalText);
            minimumToggleLabel.alignment = TextAlignmentOptions.Center;
            AddButtonBorder(minimumToggleGo, minimumToggleStyle.NormalBorder);
            ApplyButtonVisual(minimumToggleButton, minimumToggleStyle);
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

            targetSummary = MakeTMP(section.ContentArea, "Target: 0", 12, TertiaryTextColor);
            targetSummary.rectTransform.sizeDelta = new Vector2(0, 18);
            minimumSummary = MakeTMP(section.ContentArea, "Minimum: 0", 12, TertiaryTextColor);
            minimumSummary.rectTransform.sizeDelta = new Vector2(0, 18);
        }
        UpdateAmountDisplay();

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

        AddAmountStepRows(section.ContentArea, AddAmount);

        void DoConfirm()
        {
            if (_inputConfirmed) return;
            _inputConfirmed = true;
            var finalAmount = isGet ? targetAmount : currentAmount;
            if (isGet ? finalAmount > 0 : finalAmount >= 0)
            {
                if (isGet)
                {
                    var dataNow = Data.LogisticsNetwork.Get(capturedOi);
                    if (isEditing && dataNow != null && editIndex >= 0 && editIndex < dataNow.requests.Count)
                    {
                        var existing = dataNow.requests[editIndex];
                        existing.resourceDef = (ResourceDefinitionIDSave)rd;
                        existing.ResourceDefinition = rd;
                        existing.requestedAmount = targetAmount;
                        existing.minimumAmount = System.Math.Max(0, System.Math.Min(minimumAmount, targetAmount));
                        existing.useMinimumAmount = useMinimum;
                        existing.status = Data.LogisticsRequestStatus.Pending;
                        existing.statusNote = null;
                    }
                    else
                    {
                        Data.LogisticsNetwork.AddRequest(capturedOi, rd, targetAmount, minimumAmount, useMinimum);
                    }
                }
                else
                {
                    var dataNow = Data.LogisticsNetwork.Get(capturedOi);
                    if (isEditing && dataNow != null && editIndex >= 0 && editIndex < dataNow.providers.Count)
                    {
                        var existing = dataNow.providers[editIndex];
                        existing.resourceDef = (ResourceDefinitionIDSave)rd;
                        existing.ResourceDefinition = rd;
                        existing.minimumKeep = currentAmount;
                        existing.isActive = true;
                    }
                    else
                    {
                        Data.LogisticsNetwork.AddProvider(capturedOi, rd, currentAmount);
                    }
                }
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
            if (isEditing)
                ReturnToList();
            else
                ShowResourcePicker(section, isGet);
        });

        RebuildSectionLayout(section);
    }

    private GameObject MakeVLRow(Transform parent, float minHeight, float spacing)
    {
        var row = new GameObject("Row", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(Image), typeof(LayoutElement), typeof(ContentSizeFitter));
        row.transform.SetParent(parent, false);
        var le = row.GetComponent<LayoutElement>();
        le.minHeight = minHeight;
        le.flexibleWidth = 1f;
        var image = row.GetComponent<Image>();
        image.color = _runtimeStyle.RowBackgroundColor;
        image.raycastTarget = false;
        var vlg = row.GetComponent<VerticalLayoutGroup>();
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.spacing = spacing;
        vlg.padding = new RectOffset(8, 8, 4, 4);
        var fitter = row.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        return row;
    }

    private void AddVirtualFixedList(Transform parent, string name, int count, float rowHeight,
        Func<Transform, float, GameObject> createRow, Action<GameObject, int> bindRow)
    {
        if (parent == null || count <= 0 || createRow == null || bindRow == null)
            return;

        if (_popupScroll == null || _popupScroll.viewport == null)
        {
            for (var index = 0; index < count; index++)
            {
                var row = createRow(parent, rowHeight);
                bindRow(row, index);
            }
            return;
        }

        var listGo = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(VirtualFixedList));
        listGo.transform.SetParent(parent, false);
        var rt = listGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, count * rowHeight);

        var layout = listGo.GetComponent<LayoutElement>();
        layout.minHeight = count * rowHeight;
        layout.preferredHeight = layout.minHeight;
        layout.flexibleWidth = 1f;
        layout.flexibleHeight = 0f;

        listGo.GetComponent<VirtualFixedList>().Configure(_popupScroll, count, rowHeight, createRow, bindRow,
            VirtualListBufferRows);
    }

    private GameObject MakeVirtualHLRow(Transform parent, float height, float spacing)
    {
        var row = new GameObject("VirtualHLRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(Image));
        row.transform.SetParent(parent, false);
        var image = row.GetComponent<Image>();
        image.color = _runtimeStyle.RowBackgroundColor;
        image.raycastTarget = false;
        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.spacing = spacing;
        hlg.padding = new RectOffset(8, 8, 4, 4);
        return row;
    }

    private GameObject MakeVirtualVLRow(Transform parent, float height, float spacing)
    {
        var row = new GameObject("VirtualVLRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(Image));
        row.transform.SetParent(parent, false);
        var image = row.GetComponent<Image>();
        image.color = _runtimeStyle.RowBackgroundColor;
        image.raycastTarget = false;
        var vlg = row.GetComponent<VerticalLayoutGroup>();
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.spacing = spacing;
        vlg.padding = new RectOffset(8, 8, 4, 4);
        return row;
    }

    private GameObject MakeHLContainer(Transform parent, float height, float spacing)
    {
        var row = new GameObject("Line", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        var le = row.GetComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        le.flexibleWidth = 1f;
        le.flexibleHeight = 0f;
        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.spacing = spacing;
        hlg.padding = new RectOffset(0, 0, 0, 0);
        return row;
    }

    private GameObject MakeHLRow(Transform parent, float height, float spacing)
    {
        var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(Image), typeof(LayoutElement), typeof(ContentSizeFitter));
        row.transform.SetParent(parent, false);
        var le = row.GetComponent<LayoutElement>();
        le.minHeight = height;
        var image = row.GetComponent<Image>();
        image.color = _runtimeStyle.RowBackgroundColor;
        image.raycastTarget = false;
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

    private Button MakeRowButton(GameObject row)
    {
        if (row == null)
            return null;

        var image = row.GetComponent<Image>();
        if (image != null)
            image.raycastTarget = true;

        var button = row.GetComponent<Button>() ?? row.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        if (image != null)
            button.targetGraphic = image;
        return button;
    }

    private TextMeshProUGUI AddTableCell(Transform parent, string text, float width, float fontSize, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, bool flexible = false, bool wrap = false)
    {
        var label = MakeTMP(parent, text ?? "", fontSize, color);
        label.alignment = alignment;
        label.enableWordWrapping = wrap;
        label.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        var layout = label.gameObject.AddComponent<LayoutElement>();
        if (width > 0f)
        {
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.flexibleWidth = 0f;
        }
        else
        {
            layout.minWidth = 0f;
            layout.preferredWidth = 0f;
            layout.flexibleWidth = flexible ? 1f : 0f;
        }
        layout.minHeight = 18f;
        layout.preferredHeight = wrap ? 34f : 20f;
        layout.flexibleHeight = wrap ? 1f : 0f;
        return label;
    }

    private TextMeshProUGUI AddTableIconCell(Transform parent, string icon)
    {
        var label = AddTableCell(parent, icon, RouteAssetIconColumnWidth, 18f, PrimaryTextColor, TextAlignmentOptions.Midline);
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.rectTransform.offsetMin = new Vector2(0, 0);
        label.rectTransform.offsetMax = new Vector2(0, 0);

        var layout = label.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.minHeight = 24f;
            layout.preferredHeight = 24f;
        }

        return label;
    }

    private Button AddInlineTextButton(Transform parent, string text, Color normalColor, UnityEngine.Events.UnityAction onClick,
        TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var btnGo = new GameObject("InlineTextBtn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGo.transform.SetParent(parent, false);

        var labelText = text ?? "";
        var label = MakeTMP(btnGo.transform, labelText, 13f, normalColor);
        label.alignment = alignment;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.rectTransform.offsetMin = new Vector2(4f, 1f);
        label.rectTransform.offsetMax = new Vector2(-4f, -1f);

        var measured = label.GetPreferredValues(labelText, 180f, 22f).x;
        var layout = btnGo.GetComponent<LayoutElement>();
        layout.minWidth = Mathf.Clamp(measured + 10f, 46f, 180f);
        layout.preferredWidth = layout.minWidth;
        layout.minHeight = 22f;
        layout.preferredHeight = 22f;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;

        var image = btnGo.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);

        var btn = btnGo.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        btn.onClick.AddListener(onClick);

        ApplyButtonVisual(btn, new ButtonVisualStyle
        {
            NormalFill = new Color(0f, 0f, 0f, 0f),
            HoverFill = WithAlpha(HoverColor, 0.18f),
            PressedFill = WithAlpha(BorderColor, 0.32f),
            SelectedFill = WithAlpha(HoverColor, 0.18f),
            NormalBorder = new Color(0f, 0f, 0f, 0f),
            HoverBorder = new Color(0f, 0f, 0f, 0f),
            PressedBorder = new Color(0f, 0f, 0f, 0f),
            SelectedBorder = new Color(0f, 0f, 0f, 0f),
            NormalText = normalColor,
            HoverText = PrimaryTextColor,
            PressedText = PrimaryTextColor,
            SelectedText = PrimaryTextColor,
            HasBorder = false
        });

        return btn;
    }

    private void AddSpacer(Transform parent, float width)
    {
        var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(parent, false);
        var layout = spacer.GetComponent<LayoutElement>();
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.flexibleWidth = 0f;
        layout.minHeight = 1f;
        layout.preferredHeight = 1f;
        layout.flexibleHeight = 0f;
    }

    private void AddFlexibleSpacer(Transform parent)
    {
        var spacer = new GameObject("FlexibleSpacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(parent, false);
        var layout = spacer.GetComponent<LayoutElement>();
        layout.minWidth = 0f;
        layout.preferredWidth = 0f;
        layout.flexibleWidth = 1f;
        layout.minHeight = 1f;
        layout.preferredHeight = 1f;
        layout.flexibleHeight = 0f;
    }

    private void AddVerticalSpacer(Transform parent, float height)
    {
        var spacer = new GameObject("VerticalSpacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(parent, false);
        var layout = spacer.GetComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
        layout.flexibleHeight = 0f;
        layout.minWidth = 1f;
        layout.preferredWidth = 1f;
        layout.flexibleWidth = 0f;
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null)
            return;

        var children = new List<GameObject>();
        foreach (Transform child in parent)
            children.Add(child.gameObject);
        foreach (var child in children)
            DestroyImmediate(child);
    }

    private Button AddObjectSwitchButton(Transform parent, ObjectInfo target, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject("ObjectSwitchBtn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGo.transform.SetParent(parent, false);

        var layout = btnGo.GetComponent<LayoutElement>();
        layout.minWidth = 24f;
        layout.preferredWidth = 24f;
        layout.minHeight = 24f;
        layout.preferredHeight = 24f;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;

        var image = btnGo.GetComponent<Image>();
        image.color = WithAlpha(VoidColor, 0.96f);
        image.raycastTarget = true;

        var button = btnGo.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.targetGraphic = image;

        var icon = AddObjectIcon(btnGo.transform, target, 19f);
        if (icon != null)
        {
            var iconRt = icon.rectTransform;
            iconRt.anchorMin = new Vector2(0.5f, 0.5f);
            iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.anchoredPosition = Vector2.zero;
            iconRt.sizeDelta = new Vector2(19f, 19f);
        }

        ApplyButtonVisual(button, ObjectSwitchButtonStyle());
        var tooltip = btnGo.AddComponent<SmallHoverTooltip>();
        tooltip.Configure(SwitchTooltipName(target), _font, _popupRoot != null ? _popupRoot.GetComponent<RectTransform>() : null);
        button.onClick.AddListener(onClick);
        return button;
    }

    private void MakeXButton(Transform parent, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject("XBtn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGo.transform.SetParent(parent, false);
        var layout = btnGo.GetComponent<LayoutElement>();
        layout.minWidth = 24f;
        layout.preferredWidth = 24f;
        layout.minHeight = 24f;
        layout.preferredHeight = 24f;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;
        btnGo.GetComponent<Image>().color = ButtonFillColor;
        var btn = btnGo.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };
        var tmp = MakeTMP(btnGo.transform, "X", 12, PrimaryTextColor);
        tmp.alignment = TextAlignmentOptions.Center;
        AddButtonBorder(btnGo, WithAlpha(CriticalColor, 0.72f));
        ApplyButtonVisual(btn, ButtonTone.Destructive);
        btn.onClick.AddListener(onClick);
    }

    private void AddBigButton(Transform parent, string text, Color color, UnityEngine.Events.UnityAction onClick)
    {
        AddBigButtonInline(parent, text, color, onClick);
    }

    private Button AddBigButtonInline(Transform parent, string text, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var btnGo = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGo.transform.SetParent(parent, false);
        var layout = btnGo.GetComponent<LayoutElement>();
        layout.preferredHeight = 28f;
        layout.minWidth = 120f;
        layout.flexibleWidth = 1f;
        layout.flexibleHeight = 0f;
        btnGo.GetComponent<Image>().color = ButtonFillColor;
        var btn = btnGo.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };

        var style = ResolveButtonStyle(color, text);
        var labelTmp = MakeTMP(btnGo.transform, text, 14, style.NormalText);
        labelTmp.alignment = TextAlignmentOptions.Center;
        labelTmp.rectTransform.offsetMin = new Vector2(8, 2);
        labelTmp.rectTransform.offsetMax = new Vector2(-8, -2);
        AddButtonBorder(btnGo, style.NormalBorder);
        ApplyButtonVisual(btn, style);

        btn.onClick.AddListener(onClick);
        return btn;
    }

    private void AddAmountStepRows(Transform parent, System.Action<double> addAmount)
    {
        if (parent == null || addAmount == null)
            return;

        AddAmountStepRow(parent, _runtimeStyle.SmallButtonPositiveColor, addAmount, new (string Label, double Delta, float Width)[]
        {
            ("+10", 10, 46f),
            ("+100", 100, 46f),
            ("+1K", 1000, 46f),
            ("+10K", 10000, 46f),
            ("+100K", 100000, 58f),
            ("+1M", 1000000, 46f),
            ("+10M", 10000000, 54f),
            ("+100M", 100000000, 62f),
            ("+1B", 1000000000, 46f)
        });
        AddAmountStepRow(parent, _runtimeStyle.SmallButtonColor, addAmount, new (string Label, double Delta, float Width)[]
        {
            ("\u221210", -10, 46f),
            ("\u2212100", -100, 46f),
            ("\u22121K", -1000, 46f),
            ("\u221210K", -10000, 46f),
            ("\u2212100K", -100000, 58f),
            ("\u22121M", -1000000, 46f),
            ("\u221210M", -10000000, 54f),
            ("\u2212100M", -100000000, 62f),
            ("\u22121B", -1000000000, 46f)
        });
    }

    private void AddAmountStepRow(Transform parent, Color color, System.Action<double> addAmount,
        IEnumerable<(string Label, double Delta, float Width)> steps)
    {
        var row = MakeHLRow(parent, 28f, 4);
        foreach (var step in steps)
        {
            var delta = step.Delta;
            AddSmallButton(row.transform, step.Label, color, () => addAmount(delta), step.Width);
        }
    }

    private Button AddSmallButton(Transform parent, string text, Color color, UnityEngine.Events.UnityAction onClick, float width = 46f)
    {
        var btnGo = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        btnGo.transform.SetParent(parent, false);
        var layout = btnGo.GetComponent<LayoutElement>();
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.minHeight = 24f;
        layout.preferredHeight = 24f;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;
        btnGo.GetComponent<Image>().color = ButtonFillColor;
        var btn = btnGo.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };

        var style = ResolveButtonStyle(color, text);
        var labelTmp = MakeTMP(btnGo.transform, text, 12, style.NormalText);
        labelTmp.alignment = TextAlignmentOptions.Center;
        labelTmp.enableWordWrapping = false;
        labelTmp.overflowMode = TextOverflowModes.Overflow;
        labelTmp.rectTransform.offsetMin = new Vector2(4, 1);
        labelTmp.rectTransform.offsetMax = new Vector2(-4, -1);
        AddButtonBorder(btnGo, style.NormalBorder);
        ApplyButtonVisual(btn, style);

        btn.onClick.AddListener(onClick);
        return btn;
    }

    private static ThemedButtonVisual ApplyButtonVisual(Button button, ButtonTone tone, bool selected = false)
    {
        return ApplyButtonVisual(button, ButtonStyle(tone), selected);
    }

    private static ThemedButtonVisual ApplyButtonVisual(Button button, ButtonVisualStyle style, bool selected = false)
    {
        if (button == null)
            return null;

        var visual = button.GetComponent<ThemedButtonVisual>();
        if (visual == null)
            visual = button.gameObject.AddComponent<ThemedButtonVisual>();
        visual.Configure(style, selected);
        return visual;
    }

    private static ButtonVisualStyle ResolveButtonStyle(Color semanticColor, string text = null)
    {
        return ButtonStyle(ResolveButtonTone(semanticColor, text));
    }

    private static ButtonVisualStyle ObjectSwitchButtonStyle()
    {
        return new ButtonVisualStyle
        {
            HasBorder = false,
            NormalFill = WithAlpha(VoidColor, 0.96f),
            HoverFill = WithAlpha(HoverColor, 0.62f),
            PressedFill = WithAlpha(BorderColor, 0.94f),
            SelectedFill = WithAlpha(CardColor, 0.96f),
            NormalBorder = new Color(0f, 0f, 0f, 0f),
            HoverBorder = new Color(0f, 0f, 0f, 0f),
            PressedBorder = new Color(0f, 0f, 0f, 0f),
            SelectedBorder = new Color(0f, 0f, 0f, 0f),
            NormalText = PrimaryTextColor,
            HoverText = PrimaryTextColor,
            PressedText = PrimaryTextColor,
            SelectedText = PrimaryTextColor
        };
    }

    private static ButtonTone ResolveButtonTone(Color semanticColor, string text = null)
    {
        if (SameColor(semanticColor, RemoveButtonColor))
            return ButtonTone.Destructive;
        if (IsAddLikeButton(text))
            return ButtonTone.Add;
        if (SameColor(semanticColor, ConfirmButtonColor))
            return ButtonTone.Confirm;
        if (SameColor(semanticColor, ToggleOnRowColor) || SameColor(semanticColor, CountButtonPositiveColor))
            return ButtonTone.Positive;
        if (SameColor(semanticColor, ToggleOffRowColor))
            return ButtonTone.Neutral;
        if (SameColor(semanticColor, WarningColor))
            return ButtonTone.Warning;
        if (SameColor(semanticColor, AccentButtonColor))
            return ButtonTone.Neutral;
        if (SameColor(semanticColor, BackButtonColor))
            return ButtonTone.Back;
        return ButtonTone.Neutral;
    }

    private static bool IsAddLikeButton(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        return trimmed.StartsWith("+", System.StringComparison.Ordinal)
            || trimmed.IndexOf("Add ", System.StringComparison.OrdinalIgnoreCase) >= 0
            || trimmed.IndexOf("Assign", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool SameColor(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.01f
            && Mathf.Abs(a.g - b.g) < 0.01f
            && Mathf.Abs(a.b - b.b) < 0.01f
            && Mathf.Abs(a.a - b.a) < 0.02f;
    }

    private static ButtonVisualStyle ButtonStyle(ButtonTone tone)
    {
        var style = new ButtonVisualStyle
        {
            NormalFill = ButtonFillColor,
            HoverFill = ButtonHoverFillColor,
            PressedFill = ButtonPressedFillColor,
            SelectedFill = ButtonSelectedFillColor,
            NormalBorder = WithAlpha(TertiaryTextColor, 0.72f),
            HoverBorder = PrimaryTextColor,
            PressedBorder = PrimaryTextColor,
            SelectedBorder = NominalColor,
            NormalText = SecondaryTextColor,
            HoverText = PrimaryTextColor,
            PressedText = PrimaryTextColor,
            SelectedText = NominalColor,
            HasBorder = true
        };

        switch (tone)
        {
            case ButtonTone.Launcher:
                style.NormalFill = WithAlpha(CardColor, 0.94f);
                style.HoverFill = WithAlpha(HoverColor, 0.96f);
                style.PressedFill = WithAlpha(BorderColor, 0.98f);
                style.SelectedFill = WithAlpha(HoverColor, 0.96f);
                style.NormalBorder = WithAlpha(TertiaryTextColor, 0.78f);
                style.NormalText = PrimaryTextColor;
                style.HoverText = PrimaryTextColor;
                break;
            case ButtonTone.Back:
                style.NormalFill = WithAlpha(VoidColor, 0.08f);
                style.NormalBorder = WithAlpha(TertiaryTextColor, 0.72f);
                style.NormalText = SecondaryTextColor;
                break;
            case ButtonTone.Add:
                style.HasBorder = false;
                style.NormalFill = WithAlpha(NominalColor, 0.22f);
                style.HoverFill = WithAlpha(NominalColor, 0.34f);
                style.PressedFill = WithAlpha(NominalColor, 0.46f);
                style.SelectedFill = WithAlpha(NominalColor, 0.38f);
                style.NormalBorder = new Color(0f, 0f, 0f, 0f);
                style.HoverBorder = new Color(0f, 0f, 0f, 0f);
                style.PressedBorder = new Color(0f, 0f, 0f, 0f);
                style.SelectedBorder = new Color(0f, 0f, 0f, 0f);
                style.NormalText = SecondaryTextColor;
                style.HoverText = PrimaryTextColor;
                style.PressedText = PrimaryTextColor;
                style.SelectedText = PrimaryTextColor;
                break;
            case ButtonTone.Action:
                style.NormalFill = WithAlpha(HoverColor, 0.18f);
                style.NormalBorder = WithAlpha(TertiaryTextColor, 0.78f);
                style.NormalText = SecondaryTextColor;
                style.PressedFill = WithAlpha(HoverColor, 0.7f);
                break;
            case ButtonTone.Confirm:
                style.HasBorder = false;
                style.NormalFill = WithAlpha(NominalColor, 0.5f);
                style.HoverFill = NominalColor;
                style.PressedFill = WithAlpha(NominalColor, 0.76f);
                style.SelectedFill = NominalColor;
                style.NormalBorder = new Color(0f, 0f, 0f, 0f);
                style.HoverBorder = new Color(0f, 0f, 0f, 0f);
                style.PressedBorder = new Color(0f, 0f, 0f, 0f);
                style.SelectedBorder = new Color(0f, 0f, 0f, 0f);
                style.NormalText = PrimaryTextColor;
                style.HoverText = PrimaryTextColor;
                style.PressedText = PrimaryTextColor;
                style.SelectedText = PrimaryTextColor;
                break;
            case ButtonTone.Positive:
                style.HasBorder = false;
                style.NormalFill = WithAlpha(NominalColor, 0.3f);
                style.HoverFill = WithAlpha(NominalColor, 0.48f);
                style.PressedFill = WithAlpha(NominalColor, 0.62f);
                style.SelectedFill = WithAlpha(NominalColor, 0.52f);
                style.NormalBorder = new Color(0f, 0f, 0f, 0f);
                style.HoverBorder = new Color(0f, 0f, 0f, 0f);
                style.PressedBorder = new Color(0f, 0f, 0f, 0f);
                style.SelectedBorder = new Color(0f, 0f, 0f, 0f);
                style.NormalText = PrimaryTextColor;
                style.HoverText = PrimaryTextColor;
                style.PressedText = PrimaryTextColor;
                style.SelectedText = PrimaryTextColor;
                break;
            case ButtonTone.Warning:
                style.NormalFill = WithAlpha(WarningColor, 0.08f);
                style.HoverFill = WithAlpha(WarningColor, 0.2f);
                style.PressedFill = WithAlpha(WarningColor, 0.34f);
                style.SelectedFill = WithAlpha(WarningColor, 0.24f);
                style.NormalBorder = WithAlpha(WarningColor, 0.76f);
                style.HoverBorder = WarningColor;
                style.PressedBorder = WarningColor;
                style.SelectedBorder = WarningColor;
                style.NormalText = WarningColor;
                style.HoverText = PrimaryTextColor;
                style.PressedText = PrimaryTextColor;
                style.SelectedText = PrimaryTextColor;
                break;
            case ButtonTone.Destructive:
                style.HasBorder = false;
                style.NormalFill = CloseButtonFillColor;
                style.HoverFill = CloseButtonHoverFillColor;
                style.PressedFill = WithAlpha(CriticalColor, 0.82f);
                style.SelectedFill = CloseButtonHoverFillColor;
                style.NormalBorder = new Color(0f, 0f, 0f, 0f);
                style.HoverBorder = new Color(0f, 0f, 0f, 0f);
                style.PressedBorder = new Color(0f, 0f, 0f, 0f);
                style.SelectedBorder = new Color(0f, 0f, 0f, 0f);
                style.NormalText = PrimaryTextColor;
                style.HoverText = PrimaryTextColor;
                style.PressedText = PrimaryTextColor;
                style.SelectedText = PrimaryTextColor;
                break;
        }

        return style;
    }

    private static void SetOutlinedButtonState(Button button, bool selected)
    {
        if (button == null)
            return;

        var fill = selected ? ButtonSelectedFillColor : ButtonFillColor;
        var border = selected ? NominalColor : SecondaryTextColor;
        var textColor = selected ? NominalColor : SecondaryTextColor;

        var image = button.GetComponent<Image>();
        if (image != null)
            image.color = fill;

        var visual = button.GetComponent<ThemedButtonVisual>();
        if (visual != null)
        {
            visual.Configure(ButtonStyle(ButtonTone.Neutral), selected);
            return;
        }

        SetButtonBorderColor(button.transform, border);

        foreach (var tmp in button.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (tmp != null)
                tmp.color = textColor;
    }

    private static void AddButtonBorder(GameObject buttonGo, Color color)
    {
        if (buttonGo == null)
            return;
        AddBorderSegment(buttonGo.transform, "BorderTop", color, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1f));
        AddBorderSegment(buttonGo.transform, "BorderBottom", color, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 1f));
        AddBorderSegment(buttonGo.transform, "BorderLeft", color, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(1f, 0f));
        AddBorderSegment(buttonGo.transform, "BorderRight", color, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(1f, 0f));
    }

    private static void AddBorderSegment(Transform parent, string name, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        var border = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        border.transform.SetParent(parent, false);
        var rt = border.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = sizeDelta;
        border.GetComponent<LayoutElement>().ignoreLayout = true;
        var image = border.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private static void SetButtonBorderColor(Transform buttonTransform, Color color)
    {
        if (buttonTransform == null)
            return;
        foreach (var image in buttonTransform.GetComponentsInChildren<Image>(true))
        {
            if (image != null && image.gameObject.name.StartsWith("Border", System.StringComparison.Ordinal))
                image.color = color;
        }
    }

    private void AddFixedLabel(Transform parent, string text, float width)
    {
        var label = MakeTMP(parent, text, 12, _runtimeStyle.RowTextColor);
        label.alignment = TextAlignmentOptions.Center;
        var layout = label.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.minHeight = 24f;
        layout.preferredHeight = 24f;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;
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
        tmp.raycastTarget = false;
        return tmp;
    }

    private static string ResourceLabel(ResourceDefinition rd, string fallbackId = null)
    {
        var id = rd?.ID ?? fallbackId;
        if (string.IsNullOrWhiteSpace(id)) return "?";
        var name = ResourceSortName(rd, fallbackId);
        var icon = rd?.IconString ?? "";
        return $"{icon} {name}".Trim();
    }

    private static string CompactResourceLabel(ResourceDefinition rd, string fallbackId = null)
    {
        var icon = rd?.IconString;
        if (!string.IsNullOrWhiteSpace(icon))
            return icon;
        return ResourceSortName(rd, fallbackId);
    }

    private static ResourceDefinition ResolveSupplyResource()
    {
        var allResources = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllResourceDefinitions;
        return allResources?.Supply ?? allResources?.GetByID("id_resource_supply");
    }

    private static string ResourceSortName(ResourceDefinition rd, string fallbackId = null)
    {
        var id = rd?.ID ?? fallbackId;
        if (string.IsNullOrWhiteSpace(id)) return "?";
        var name = rd == null ? id : LEManager.Get(rd.ID, rd.ID);
        if (string.IsNullOrWhiteSpace(name)
            || string.Equals(name, id, System.StringComparison.OrdinalIgnoreCase)
            || LooksLikeResourceId(name))
        {
            name = FriendlyResourceName(id);
        }
        return name;
    }

    private static string FormatNiceAmount(double amount)
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount))
            return amount.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return amount.ToPostfixString("{0}{1}T", gray: false, intFormat: false, minPostFixIndex: 0, colorFmt: "{0}");
    }

    private static int GetSpacecraftStackClickStep()
    {
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            return 100;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            return 10;
        return 1;
    }

    private static ResourceDefinition ResolveResource(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId)) return null;
        return SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllResourceDefinitions?.GetByID(resourceId);
    }

    private static bool LooksLikeResourceId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.StartsWith("ID_resource_", System.StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("id_resource_", System.StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("resource_", System.StringComparison.OrdinalIgnoreCase);
    }

    private static string FriendlyResourceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "?";
        var name = value.Trim();
        foreach (var prefix in new[] { "ID_resource_", "id_resource_", "resource_" })
        {
            if (name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(prefix.Length);
                break;
            }
        }
        return name.Replace('_', ' ').ToLowerInvariant();
    }

    private static bool IsGhostCraftReleasable(Data.GhostCraftRecord craft)
    {
        return craft != null
            && (craft.status == Data.GhostCraftStatus.IdleAtHome
                || craft.status == Data.GhostCraftStatus.Blocked);
    }

    private static string GhostShipTypeName(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId)) return "?";
        var type = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllSpacecraftType?.GetByID(typeId);
        if (type == null) return typeId;
        return $"{GhostShipIcon(typeId)} {GhostShipName(typeId)}";
    }

    private static string GhostShipIcon(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId)) return "";
        var type = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllSpacecraftType?.GetByID(typeId);
        return type == null ? "" : ShipIcon(type.SpriteId);
    }

    private static string GhostShipName(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId)) return "?";
        var type = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllSpacecraftType?.GetByID(typeId);
        return NormalizeAssetDisplayName(type?.NameRocketType ?? typeId);
    }

    private static string LaunchVehicleTypeName(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId)) return "?";
        var type = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllLaunchVehicleType?.GetByID(typeId);
        if (type == null) return typeId;
        return $"{LaunchVehicleIcon(typeId)} {LaunchVehicleName(typeId)}";
    }

    private static string LaunchVehicleIcon(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId)) return "";
        var type = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllLaunchVehicleType?.GetByID(typeId);
        return type == null ? "" : ShipIcon(type.SpriteId);
    }

    private static string LaunchVehicleName(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId)) return "?";
        var type = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllLaunchVehicleType?.GetByID(typeId);
        return NormalizeAssetDisplayName(type?.Name ?? typeId);
    }

    private static string GhostFlightCraftName(Data.GhostFlightRecord flight)
    {
        if (flight == null)
            return "Unknown craft";

        var craftIds = GhostFlightCraftIds(flight);
        var craftRecords = craftIds
            .Select(Data.LogisticsNetwork.FindGhostCraft)
            .Where(craft => craft != null)
            .ToList();
        var craft = craftRecords.FirstOrDefault();
        if (craft == null)
            return craftIds.Count > 0 ? $"craftx{craftIds.Count}" : "craftx?";

        var type = SerializedMonoBehaviourSingleton<AllScriptableObjectManager>.Instance?.AllSpacecraftType?.GetByID(craft.shipTypeId);
        var icon = type != null ? ShipIcon(type.SpriteId) : "";
        var count = System.Math.Max(1, craftIds.Count);
        return string.IsNullOrWhiteSpace(icon) ? $"craftx{count}" : $"{icon}x{count}";
    }

    private static List<int> GhostFlightCraftIds(Data.GhostFlightRecord flight)
    {
        if (flight == null)
            return new List<int>();
        var ids = flight.craftLedgerIds?
            .Where(id => id > 0)
            .Distinct()
            .ToList() ?? new List<int>();
        return ids;
    }

    private static string ObjectName(int objectId)
    {
        if (objectId <= 0) return "?";
        var oi = MonoBehaviourSingleton<ObjectInfoManager>.Instance?.GetByID(objectId);
        return oi?.ObjectName ?? objectId.ToString();
    }

    private static ObjectInfo ResolveObjectInfo(int objectId)
    {
        if (objectId <= 0) return null;
        return MonoBehaviourSingleton<ObjectInfoManager>.Instance?.GetByID(objectId);
    }

    private static string CompactRouteLane(Data.LogisticsRouteRecord route)
    {
        return route == null ? "? ●─● ?" : CompactRouteLane(route.sourceObjectId, route.destinationObjectId);
    }

    private static string CompactRouteLane(int fromObjectId, int toObjectId)
    {
        return CompactRouteLane(ResolveObjectInfo(fromObjectId), ResolveObjectInfo(toObjectId),
            ObjectName(fromObjectId), ObjectName(toObjectId));
    }

    private static string CompactRouteLane(ObjectInfo from, ObjectInfo to, string fromFallback = null, string toFallback = null)
    {
        var fromName = CompactObjectName(from, fromFallback);
        var toName = CompactObjectName(to, toFallback);
        return $"{fromName} {EndpointMarker(from, fromFallback)}─{EndpointMarker(to, toFallback)} {toName}";
    }

    private static string CompactObjectLabel(int objectId)
    {
        return CompactObjectLabel(ResolveObjectInfo(objectId), ObjectName(objectId));
    }

    private static string CompactObjectLabel(ObjectInfo oi, string fallback = null)
    {
        return $"{EndpointMarker(oi, fallback)} {CompactObjectName(oi, fallback)}";
    }

    private static string CompactObjectName(ObjectInfo oi, string fallback = null)
    {
        var name = oi?.ObjectName ?? fallback;
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        var orbitIndex = name.IndexOf("[ORBIT]", System.StringComparison.OrdinalIgnoreCase);
        if (orbitIndex >= 0)
            name = (name.Substring(0, orbitIndex) + name.Substring(orbitIndex + "[ORBIT]".Length)).Trim();

        while (name.Contains("  "))
            name = name.Replace("  ", " ");
        return NormalizeObjectDisplayName(name);
    }

    private static string SwitchTooltipName(ObjectInfo oi)
    {
        if (oi == null)
            return "?";

        var name = oi.ObjectName ?? "";
        int orbitIndex;
        while ((orbitIndex = name.IndexOf("[ORBIT]", System.StringComparison.OrdinalIgnoreCase)) >= 0)
            name = (name.Substring(0, orbitIndex) + " Orbit " + name.Substring(orbitIndex + "[ORBIT]".Length)).Trim();

        while (name.Contains("  "))
            name = name.Replace("  ", " ");

        name = NormalizeObjectDisplayName(name);
        var isOrbit = oi.objectTypes == global::Data.EObjectTypes.Orbit
            || oi.objectTypes == global::Data.EObjectTypes.SolarOrbit;

        if (isOrbit && name.IndexOf("orbit", System.StringComparison.OrdinalIgnoreCase) < 0)
        {
            var bodyName = oi.parentObjectInfo != null ? CompactObjectName(oi.parentObjectInfo) : CompactObjectName(oi);
            name = string.IsNullOrWhiteSpace(bodyName) || bodyName == "?" ? "Orbit" : $"{bodyName} Orbit";
        }

        return string.IsNullOrWhiteSpace(name) ? "?" : name;
    }

    private static string NormalizeObjectDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        var hasLetter = name.Any(char.IsLetter);
        var hasLower = name.Any(char.IsLower);
        if (!hasLetter || hasLower)
            return name;

        var lower = name.ToLowerInvariant();
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);
    }

    private static string NormalizeAssetDisplayName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        name = name.Trim();
        var hasLetter = name.Any(char.IsLetter);
        var hasLower = name.Any(char.IsLower);
        if (!hasLetter || hasLower)
            return name;

        var letterCount = name.Count(char.IsLetter);
        if (letterCount <= 3)
            return name;

        var lower = name.ToLowerInvariant();
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);
    }

    private static string EndpointMarker(ObjectInfo oi, string fallback = null)
    {
        var name = oi?.ObjectName ?? fallback ?? "";
        return name.IndexOf("[ORBIT]", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "◉" : "●";
    }

    private static string ReplaceRawRouteText(string text, string source, string destination, string compact)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
            return text;

        foreach (var raw in new[]
        {
            $"{source}->{destination}",
            $"{source} -> {destination}",
            $"{source}-->{destination}",
            $"{source} --> {destination}"
        })
        {
            text = text.Replace(raw, compact);
        }
        return text;
    }

    private static string ShipIcon(string spriteId)
    {
        if (string.IsNullOrEmpty(spriteId)) return "";
        var objManager = MonoBehaviourSingleton<ObjectInfoManager>.Instance;
        return objManager != null ? objManager.spriteTextStart5.MyFormat(spriteId, "") : "";
    }

    public void RebuildLayout()
    {
        if (!_built || !isActiveAndEnabled)
            return;

        if (_parentRt != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_parentRt);
        if (_popupPanelRt != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(_popupPanelRt);
    }

    private void OnDestroy()
    {
        foreach (var sec in _sections)
            if (sec?.Root != null) Destroy(sec.Root);
        _sections.Clear();
        if (_launcherButtonGo != null)
            Destroy(_launcherButtonGo);
        RegisterPopupOpen(false);
        if (_popupRoot != null)
            Destroy(_popupRoot);
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
        Data.LogisticsRequestStatus.Pending => WarningColor,
        Data.LogisticsRequestStatus.InProgress => EngineAccentColor,
        Data.LogisticsRequestStatus.Satisfied => NominalColor,
        Data.LogisticsRequestStatus.Failed => CriticalColor,
        _ => DisabledTextColor
    };
}
