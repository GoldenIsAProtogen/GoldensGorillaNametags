using System;
using System.Collections.Generic;
using GoldensGorillaNametags.Core;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Object = UnityEngine.Object;

namespace GoldensGorillaNametags.Managers;

public class GUIManager
{
#region ===== - Fields & Init - =====

    private const int ButtonTextPadding = 8;

    private const float WindowWidth        = 760f,
                        WindowHeight       = 520f,
                        SidebarWidth       = 210f,
                        PanelMargin        = 10f,
                        PanelGap           = 10f,
                        SidebarPadding     = 16f,
                        NavButtonHeight    = 38f,
                        NavButtonSpacing   = 6f,
                        NavAnimSpeed       = 6f,
                        ContentPadding     = 24f,
                        DragStripHeight    = 50f,
                        GroupGap           = 14f,
                        FpsRefreshTime     = 0.5f,
                        SwitchAnimSpeed    = 5f,
                        ButtonWidth        = 92f,
                        ButtonHeight       = 26f,
                        ButtonGap          = 4f,
                        StepperButtonWidth = 28f,
                        ToggleWidth        = StepperButtonWidth * 2f + ButtonGap,
                        TitlePadding       = 10f;

    private static readonly Color _buttonIdle    = new(0.15f, 0.15f, 0.17f),
                                  _buttonHover   = new(0.21f, 0.21f, 0.24f),
                                  _navSelected   = new(0.22f, 0.22f, 0.26f),
                                  _textPrimary   = new(0.96f, 0.96f, 0.98f),
                                  _textSecondary = new(0.74f, 0.74f, 0.80f),
                                  _textDark      = new(0.08f, 0.08f, 0.10f);

    private static readonly string[] _pages = ["Tags", "Outlines", "Checks", "Platform", "Integrations", "GUI",];

    private static readonly string[] _textCaseNames   = Enum.GetNames(typeof(Plugin.TextCase)),
                                     _formatScopes    = ["Name Only", "All Text",],
                                     _iconSides       = ["Left", "Right",],
                                     _nametagFontActs = ["Refresh", "Match GUI", "Apply Font",],
                                     _guiFontActs     = ["Refresh", "Match Tags", "Apply Font",],
                                     _resetAction     = ["Reset",];

    private readonly string[] _keyAction = new string[1];

    private readonly Plugin _plugin;

    private readonly Dictionary<string, float> switchAnim = new();
    private readonly float[]                   navAnim    = new float[_pages.Length];

    private readonly Dictionary<(int radius, Color color), Texture2D> textureCache  = new();
    private readonly Dictionary<float, GUILayoutOption[]>             layoutOptions = new();

    private Rect    windowRect;
    private Vector2 pageScrollPos;

    private int currentPage;

    private bool guiOpen,
                 waitingForKey,
                 accentValid;

    private int   fpsFrames,
                  builtRadiusKey;
    private float fpsTimer,
                  builtOpacity = -1f,
                  titleTextWidth;

    private string builtGuiFont,
                   appliedGuiFont,
                   appliedNametagFont,
                   versionText,
                   fpsText = string.Empty;

    private Color accent,
                  accentIdle,
                  onAccent,
                  accentSource;

    private GUIStyle windowStyle,
                     panelStyle,
                     pillStyle,
                     switchStyle,
                     switchKnobStyle,
                     trackStyle,
                     sliderKnobStyle,
                     navButtonStyle,
                     tabStyle,
                     tabSelectedStyle,
                     headerStyle,
                     versionStyle,
                     fpsStyle,
                     rowLabelStyle,
                     rowValueStyle;

    private int WindowRadius => Mathf.RoundToInt(_plugin.GuiCornerRadius.Value * 4f);
    private int PanelRadius  => Mathf.RoundToInt(_plugin.GuiCornerRadius.Value * 2.5f);
    private int ButtonRadius => Mathf.RoundToInt(_plugin.GuiCornerRadius.Value);

    public GUIManager(Plugin plugin)
    {
        _plugin    = plugin;
        windowRect = new Rect(60f, 60f, WindowWidth, WindowHeight);

        navAnim[currentPage] = 1f;
    }

#endregion

#region ===== - Runtime - =====

    public void Update()
    {
        if (guiOpen)
            UpdateFps();

        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
            return;

        if (waitingForKey)
        {
            foreach (KeyControl key in keyboard.allKeys)
            {
                if (!key.wasPressedThisFrame)
                    continue;

                if (key.keyCode != Key.Escape)
                    _plugin.GuiButton.Value = key.keyCode;

                waitingForKey = false;

                return;
            }

            return;
        }

        if (!keyboard[_plugin.GuiButton.Value].wasPressedThisFrame)
            return;

        guiOpen          = !guiOpen;
        Cursor.visible   = guiOpen;
        Cursor.lockState = guiOpen ? CursorLockMode.None : CursorLockMode.Locked;

        fpsFrames = 0;
        fpsTimer  = 0f;
    }

    public void OnGUI()
    {
        if (!guiOpen)
            return;

        EnsureStyles();
        RefreshAccent();

        GUI.backgroundColor = Color.white;
        windowRect          = GUI.Window(42069420, windowRect, DrawWindow, string.Empty, windowStyle);
    }

    private void UpdateFps()
    {
        fpsFrames++;
        fpsTimer += Time.unscaledDeltaTime;

        if (fpsTimer < FpsRefreshTime)
            return;

        fpsText   = $"{Mathf.RoundToInt(fpsFrames / fpsTimer)} FPS\n{PhotonNetwork.GetPing()} PING";
        fpsFrames = 0;
        fpsTimer  = 0f;
    }

    private void RefreshAccent()
    {
        Color src = _plugin.GuiAccentColor.Value;

        if (accentValid && src == accentSource)
            return;

        accentValid  = true;
        accentSource = src;
        accent       = new Color(src.r, src.g, src.b);

        float luma  = accent.r * 0.299f + accent.g * 0.587f + accent.b * 0.114f;
        Color muted = Color.Lerp(accent, new Color(luma, luma, luma), 0.65f);

        accentIdle = Color.Lerp(_buttonIdle, muted, 0.35f);
        onAccent   = luma > 0.55f ? _textDark : _textPrimary;

        SetTextColors(tabSelectedStyle, onAccent);
    }

#endregion

#region ===== - Style & Texture Setup - =====

    private void EnsureStyles()
    {
        int    radiusKey = WindowRadius * 10000 + PanelRadius * 100 + ButtonRadius;
        float  opacity   = Mathf.Clamp01(_plugin.GuiOpacity.Value);
        string guiFont   = appliedGuiFont ??= FontManager.GetSelectedFontName(FontManager.FontTarget.Gui);
        appliedNametagFont ??= FontManager.GetSelectedFontName(FontManager.FontTarget.Nametag);

        if (windowStyle != null && radiusKey == builtRadiusKey && Mathf.Approximately(opacity, builtOpacity) && guiFont == builtGuiFont)
            return;

        BuildStyles(opacity);

        builtRadiusKey = radiusKey;
        builtOpacity   = opacity;
        builtGuiFont   = guiFont;
        accentValid    = false;
    }

    private void ApplyGuiFont() => appliedGuiFont = FontManager.GetSelectedFontName(FontManager.FontTarget.Gui);

    private void ApplyNametagFont()
    {
        appliedNametagFont = FontManager.GetSelectedFontName(FontManager.FontTarget.Nametag);
        TagManager.Instance?.ClearTags();
    }

    private void BuildStyles(float opacity)
    {
        foreach (Texture2D texture in textureCache.Values)
            Object.Destroy(texture);

        textureCache.Clear();

        Font guiFont = FontManager.GetGuiFont(appliedGuiFont);

        windowStyle = RoundedStyle(GUI.skin.window, WindowRadius, new Color(0.075f, 0.075f, 0.09f,  opacity));
        panelStyle  = RoundedStyle(null,            PanelRadius,  new Color(0.115f, 0.115f, 0.135f, opacity));

        pillStyle       = RoundedStyle(null, ButtonRadius,                                  Color.white);
        switchStyle     = RoundedStyle(null, Mathf.Min(ButtonRadius, 13),                   Color.white);
        switchKnobStyle = RoundedStyle(null, Mathf.Max(0, Mathf.Min(10, ButtonRadius - 3)), Color.white);
        trackStyle      = RoundedStyle(null, Mathf.Min(ButtonRadius, 5),                    Color.white);
        sliderKnobStyle = RoundedStyle(null, Mathf.Min(ButtonRadius, 8),                    Color.white);

        navButtonStyle           = RoundedStyle(GUI.skin.button, ButtonRadius, Color.white);
        navButtonStyle.font      = guiFont;
        navButtonStyle.alignment = TextAnchor.MiddleLeft;
        navButtonStyle.fontStyle = FontStyle.Bold;
        navButtonStyle.fontSize  = 13;
        navButtonStyle.padding   = new RectOffset(16, 8, 0, 0);
        SetTextColors(navButtonStyle, _textPrimary);

        tabStyle           = RoundedStyle(GUI.skin.button, ButtonRadius, Color.white);
        tabStyle.font      = guiFont;
        tabStyle.alignment = TextAnchor.MiddleCenter;
        tabStyle.fontStyle = FontStyle.Bold;
        tabStyle.fontSize  = 11;
        tabStyle.clipping  = TextClipping.Clip;
        tabStyle.padding   = new RectOffset(ButtonTextPadding, ButtonTextPadding, 0, 0);
        SetTextColors(tabStyle, _textPrimary);

        tabSelectedStyle = new GUIStyle(tabStyle);

        headerStyle           = new GUIStyle(GUI.skin.label);
        headerStyle.font      = guiFont;
        headerStyle.alignment = TextAnchor.MiddleCenter;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.padding   = new RectOffset();
        headerStyle.wordWrap  = false;
        headerStyle.clipping  = TextClipping.Overflow;
        SetTextColors(headerStyle, _textPrimary);

        FitTitleFont(Constants.Name, SidebarWidth - 24f - TitlePadding * 2f);

        versionStyle           = new GUIStyle(GUI.skin.label);
        versionStyle.font      = guiFont;
        versionStyle.alignment = TextAnchor.MiddleLeft;
        versionStyle.fontSize  = 11;
        versionStyle.padding   = new RectOffset();
        SetTextColors(versionStyle, _textSecondary);

        fpsStyle           = new GUIStyle(versionStyle);
        fpsStyle.alignment = TextAnchor.MiddleRight;

        rowLabelStyle           = new GUIStyle(GUI.skin.label);
        rowLabelStyle.font      = guiFont;
        rowLabelStyle.alignment = TextAnchor.MiddleLeft;
        rowLabelStyle.fontStyle = FontStyle.Bold;
        rowLabelStyle.fontSize  = 12;
        rowLabelStyle.padding   = new RectOffset();
        rowLabelStyle.clipping  = TextClipping.Clip;
        SetTextColors(rowLabelStyle, _textPrimary);

        rowValueStyle           = new GUIStyle(rowLabelStyle);
        rowValueStyle.alignment = TextAnchor.MiddleRight;
        rowValueStyle.fontStyle = FontStyle.Normal;
        SetTextColors(rowValueStyle, _textSecondary);
    }

    // Shrinks the header font (13 -> 8) until the title fits. Leaves headerStyle.fontSize at the chosen size.
    private void FitTitleFont(string text, float maxTextWidth)
    {
        GUIContent content = new(text);

        for (int size = 13; size >= 8; size--)
        {
            headerStyle.fontSize = size;
            titleTextWidth       = headerStyle.CalcSize(content).x;

            if (titleTextWidth <= maxTextWidth)
                break;
        }
    }

    private static GUIStyleState[] States(GUIStyle style) =>
            [style.normal, style.hover, style.active, style.focused, style.onNormal, style.onHover, style.onActive, style.onFocused,];

    private static void SetTextColors(GUIStyle style, Color color)
    {
        foreach (GUIStyleState state in States(style))
            state.textColor = color;
    }

    private GUIStyle RoundedStyle(GUIStyle basis, int radius, Color color)
    {
        if (!textureCache.TryGetValue((radius, color), out Texture2D texture))
            textureCache[(radius, color)] = texture = CreateRoundedTexture(radius * 2 + 4, radius, color);

        GUIStyle style = basis == null ? new GUIStyle() : new GUIStyle(basis);

        style.border  = new RectOffset(radius, radius, radius, radius);
        style.padding = new RectOffset();

        foreach (GUIStyleState state in States(style))
            state.background = texture;

        return style;
    }

    private static Texture2D CreateRoundedTexture(int size, int radius, Color color)
    {
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
        };

        Color32[] pixels = new Color32[size * size];
        Color32   baseC  = color;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            pixels[y * size + x] = new Color32(baseC.r, baseC.g, baseC.b, (byte)(baseC.a * CornerAlpha(x, y, size, radius) + 0.5f));

        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        return texture;
    }

    private static float CornerAlpha(int x, int y, int size, int radius)
    {
        int cx = Mathf.Clamp(x, radius, size - radius - 1);
        int cy = Mathf.Clamp(y, radius, size - radius - 1);

        // Only pixels inside a corner square are anti-aliased
        if (x == cx || y == cy)
            return 1f;

        float dx = x - cx;
        float dy = y - cy;

        return Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
    }

#endregion

#region ===== - Window Drawing - =====

    private void DrawWindow(int windowId)
    {
        Rect sidebarRect = new(PanelMargin, PanelMargin, SidebarWidth, windowRect.height - PanelMargin * 2f);

        Rect contentRect = new(
                sidebarRect.xMax + PanelGap,
                PanelMargin,
                windowRect.width  - sidebarRect.xMax - PanelGap - PanelMargin,
                windowRect.height - PanelMargin * 2f);

        GUI.Box(sidebarRect, GUIContent.none, panelStyle);
        GUI.Box(contentRect, GUIContent.none, panelStyle);

        DrawSidebar(sidebarRect);
        DrawContent(contentRect);

        GUI.DragWindow(new Rect(0f, 0f, windowRect.width, DragStripHeight));
    }

    private void DrawSidebar(Rect area)
    {
        Rect headerRect = new(area.x + 12f, area.y + SidebarPadding, area.width - 24f, 30f);

        DrawTitleBackground(headerRect);
        GUI.Label(headerRect, Constants.Name, headerStyle);

        float navStartY = headerRect.yMax + 24f;

        for (int i = 0; i < _pages.Length; i++)
        {
            Rect buttonRect = new(
                    area.x + SidebarPadding,
                    navStartY + i * (NavButtonHeight + NavButtonSpacing),
                    area.width - SidebarPadding * 2f,
                    NavButtonHeight);

            if (!DrawNavButton(buttonRect, i, _pages[i], i == currentPage))
                continue;

            currentPage   = i;
            pageScrollPos = Vector2.zero;
        }

        if (Event.current.type != EventType.Repaint)
            return;

        Rect footerRect  = new(area.x + 12f, area.yMax - 44f, area.width - 24f, 32f);
        Rect versionRect = new(footerRect.x,                       footerRect.y, footerRect.width * 0.6f, footerRect.height);
        Rect fpsRect     = new(footerRect.xMax - footerRect.width * 0.4f, footerRect.y, footerRect.width * 0.4f, footerRect.height);

        versionText ??= $"{Constants.Version}{Constants.BuildType}{Constants.BuildNumber}\n{Application.version}";

        GUI.Label(versionRect, versionText, versionStyle);
        GUI.Label(fpsRect,     fpsText,     fpsStyle);
    }

    private void DrawTitleBackground(Rect textRect)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        float bgWidth = Mathf.Min(titleTextWidth + TitlePadding * 2f, textRect.width);

        Rect bgRect = new(
                textRect.x + (textRect.width - bgWidth) * 0.5f,
                textRect.y - 6f,
                bgWidth,
                textRect.height + 10f);

        GUI.backgroundColor = _buttonIdle;
        GUI.Box(bgRect, GUIContent.none, pillStyle);
        GUI.backgroundColor = Color.white;
    }

    private bool DrawNavButton(Rect rect, int index, string label, bool selected)
    {
        bool  repaint  = Event.current.type == EventType.Repaint;
        float progress = navAnim[index];

        if (repaint)
        {
            progress       = Mathf.Lerp(progress, selected ? 1f : 0f, 1f - Mathf.Exp(-NavAnimSpeed * Time.unscaledDeltaTime));
            navAnim[index] = progress;
        }

        navButtonStyle.normal.textColor = Color.Lerp(_textSecondary, _textPrimary, progress);

        Color idle = rect.Contains(Event.current.mousePosition) ? _buttonHover : _buttonIdle;

        GUI.backgroundColor = Color.Lerp(idle, _navSelected, progress);
        bool clicked = GUI.Button(rect, label, navButtonStyle);
        GUI.backgroundColor = Color.white;

        if (!repaint)
            return clicked;

        float barHeight = Mathf.Lerp(rect.height * 0.55f, rect.height - 4f, progress);
        Rect  bar       = new(rect.x, rect.y + (rect.height - barHeight) * 0.5f, 3f, barHeight);

        GUI.color = Color.Lerp(accentIdle, accent, progress);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = Color.white;

        return clicked;
    }

    private void DrawContent(Rect area)
    {
        GUILayout.BeginArea(new Rect(
                area.x + ContentPadding,
                area.y + ContentPadding,
                area.width  - ContentPadding * 2f,
                area.height - ContentPadding * 2f));

        pageScrollPos = GUILayout.BeginScrollView(pageScrollPos, false, false, GUIStyle.none, GUIStyle.none);

        switch (currentPage)
        {
            case 0: DrawTagsPage(); break;
            case 1: DrawOutlinesPage(); break;
            case 2: DrawChecksPage(); break;
            case 3: DrawPlatformPage(); break;
            case 4: DrawIntegrationsPage(); break;
            case 5: DrawGuiPage(); break;
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

#endregion

#region ===== - Controls - =====

    private Rect LayoutRect(float height)
    {
        if (!layoutOptions.TryGetValue(height, out GUILayoutOption[] options))
            layoutOptions[height] = options = [GUILayout.ExpandWidth(true), GUILayout.Height(height),];

        return GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, options);
    }

    private static void Gap() => GUILayout.Space(GroupGap);

    private bool DrawButton(Rect rect, string text, bool selected)
    {
        GUI.backgroundColor = selected ? accent : rect.Contains(Event.current.mousePosition) ? _buttonHover : _buttonIdle;
        bool clicked = GUI.Button(rect, text, selected ? tabSelectedStyle : tabStyle);
        GUI.backgroundColor = Color.white;

        return clicked;
    }

    private bool ToggleRow(string label, bool value, bool indented = false)
    {
        Rect rect = LayoutRect(36f);

        if (!switchAnim.TryGetValue(label, out float progress))
            progress = value ? 1f : 0f;

        if (Event.current.type == EventType.Repaint)
        {
            progress          = Mathf.MoveTowards(progress, value ? 1f : 0f, Time.unscaledDeltaTime * SwitchAnimSpeed);
            switchAnim[label] = progress;

            float eased  = progress * progress * (3f - 2f * progress);
            float indent = indented ? 18f : 0f;

            GUI.Label(new Rect(rect.x + 4f + indent, rect.y, rect.width - ToggleWidth - 20f - indent, rect.height), label, rowLabelStyle);

            Rect track = new(
                    Mathf.Round(rect.xMax     - 4f - ToggleWidth),
                    Mathf.Round(rect.center.y - ButtonHeight * 0.5f),
                    ToggleWidth,
                    ButtonHeight);

            GUI.backgroundColor = Color.Lerp(_buttonIdle, accent, eased);
            GUI.Box(track, GUIContent.none, switchStyle);

            const float knobSize    = 16f;
            float       knobPadding = Mathf.Floor((track.height - knobSize) * 0.5f);
            float       knobX       = Mathf.Lerp(track.x + knobPadding, track.xMax - knobPadding - knobSize, eased);

            GUI.backgroundColor = Color.Lerp(_textSecondary, onAccent, eased);
            GUI.Box(new Rect(knobX, track.y + knobPadding, knobSize, knobSize), GUIContent.none, switchKnobStyle);
            GUI.backgroundColor = Color.white;
        }

        return GUI.Button(rect, GUIContent.none, GUIStyle.none) ? !value : value;
    }

    private float SliderRow(string label, float value, float min, float max, string format = "F2")
    {
        Rect rect = LayoutRect(48f);

        Rect track = new(rect.x  + 10f, rect.y + 30f, rect.width - 20f, 10f);
        Rect hit   = new(track.x - 8f,  track.y - 8f, track.width + 16f, track.height + 16f);

        int   id = GUIUtility.GetControlID(FocusType.Passive);
        Event e  = Event.current;

        float ValueAtMouse() => Mathf.Lerp(min, max, Mathf.InverseLerp(track.x, track.xMax, e.mousePosition.x));

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown when e.button == 0 && hit.Contains(e.mousePosition):
                GUIUtility.hotControl = id;
                value                 = ValueAtMouse();
                e.Use();

                break;

            case EventType.MouseDrag when GUIUtility.hotControl == id:
                value = ValueAtMouse();
                e.Use();

                break;

            case EventType.MouseUp when GUIUtility.hotControl == id:
                GUIUtility.hotControl = 0;
                e.Use();

                break;
        }

        if (e.type != EventType.Repaint)
            return value;

        GUI.Label(new Rect(rect.x    + 4f,                     rect.y + 4f, rect.width * 0.6f, 22f), label,                  rowLabelStyle);
        GUI.Label(new Rect(rect.xMax - 4f - rect.width * 0.4f, rect.y + 4f, rect.width * 0.4f, 22f), value.ToString(format), rowValueStyle);

        GUI.backgroundColor = _buttonIdle;
        GUI.Box(track, GUIContent.none, trackStyle);

        float fillWidth = track.width * Mathf.InverseLerp(min, max, value);

        if (fillWidth >= track.height)
        {
            GUI.backgroundColor = accent;
            GUI.Box(new Rect(track.x, track.y, fillWidth, track.height), GUIContent.none, trackStyle);
        }

        GUI.backgroundColor = _textPrimary;
        GUI.Box(new Rect(track.x + fillWidth - 8f, track.center.y - 8f, 16f, 16f), GUIContent.none, sliderKnobStyle);
        GUI.backgroundColor = Color.white;

        return value;
    }

    private int OptionRow(string label, string[] options, int selected)
    {
        Rect rect = LayoutRect(42f);

        GUI.Label(new Rect(rect.x + 4f, rect.y, rect.width * 0.3f, rect.height), label, rowLabelStyle);

        float startX = rect.xMax - 4f - (options.Length * ButtonWidth + (options.Length - 1) * ButtonGap);
        int   result = selected;

        for (int i = 0; i < options.Length; i++)
        {
            Rect buttonRect = new(startX + i * (ButtonWidth + ButtonGap), rect.center.y - ButtonHeight * 0.5f, ButtonWidth, ButtonHeight);

            if (DrawButton(buttonRect, options[i], i == selected))
                result = i;
        }

        return result;
    }

    // Returns -1 for "<", 1 for ">", 0 for no click
    private int StepperRow(string label, string value)
    {
        Rect rect = LayoutRect(42f);
        float y   = rect.center.y - ButtonHeight * 0.5f;

        GUI.Label(new Rect(rect.x + 4f, rect.y, 110f, rect.height), label, rowLabelStyle);

        Rect next  = new(rect.xMax - 4f - StepperButtonWidth,         y, StepperButtonWidth, ButtonHeight);
        Rect prior = new(next.x - ButtonGap - StepperButtonWidth,     y, StepperButtonWidth, ButtonHeight);
        Rect name  = new(rect.x + 118f, rect.y, prior.x - 8f - (rect.x + 118f), rect.height);

        GUI.Label(name, value, rowValueStyle);

        if (DrawButton(prior, "<", false)) return -1;
        if (DrawButton(next,  ">", false)) return 1;

        return 0;
    }

    // Returns the index of the clicked button, or -1
    private int ActionRow(string label, string[] texts)
    {
        Rect rect = LayoutRect(42f);

        if (label != null)
            GUI.Label(new Rect(rect.x + 4f, rect.y, rect.width * 0.4f, rect.height), label, rowLabelStyle);

        float x      = rect.xMax - 4f;
        int   result = -1;

        for (int i = texts.Length - 1; i >= 0; i--)
        {
            x -= ButtonWidth;

            if (DrawButton(new Rect(x, rect.center.y - ButtonHeight * 0.5f, ButtonWidth, ButtonHeight), texts[i], false))
                result = i;

            x -= ButtonGap;
        }

        return result;
    }

    private Color ColorRow(string label, Color c, bool withAlpha)
    {
        Rect rect = LayoutRect(40f);

        if (Event.current.type == EventType.Repaint)
        {
            GUI.Label(new Rect(rect.x + 4f, rect.y, rect.width * 0.5f, rect.height), label, rowLabelStyle);

            GUI.backgroundColor = new Color(c.r, c.g, c.b);
            GUI.Box(new Rect(rect.xMax - 48f, rect.center.y - 9f, 44f, 18f), GUIContent.none, pillStyle);
            GUI.backgroundColor = Color.white;
        }

        float r = SliderRow("Red",   c.r, 0f, 1f);
        float g = SliderRow("Green", c.g, 0f, 1f);
        float b = SliderRow("Blue",  c.b, 0f, 1f);
        float a = withAlpha ? SliderRow("Opacity", c.a, 0f, 1f) : c.a;

        return new Color(r, g, b, a);
    }

    // Font stepper + Refresh/Match/Apply row. Returns true when the font should be applied.
    private bool FontRows(string label, FontManager.FontTarget target, string[] actions, string matchFont)
    {
        int step = StepperRow(label, FontManager.GetSelectedFontDisplayName(target));

        if (step < 0) FontManager.PreviousFont(target);
        else if (step > 0) FontManager.NextFont(target);

        switch (ActionRow(null, actions))
        {
            case 0:
                FontManager.RefreshAvailableFonts();

                return false;

            case 1:
                FontManager.MatchFont(target, matchFont);

                return true;

            case 2: return true;
            default: return false;
        }
    }

    private Plugin.TextStyle StyleToggle(string label, Plugin.TextStyle style, Plugin.TextStyle flag) =>
            ToggleRow(label, (style & flag) != 0) ? style | flag : style & ~flag;

#endregion

#region ===== - Pages - =====

    private void DrawTagsPage()
    {
        _plugin.TagSize.Value   = SliderRow("Tag Size",        _plugin.TagSize.Value,   1f,     3f);
        _plugin.TagHeight.Value = SliderRow("Tag Height",      _plugin.TagHeight.Value, 0f,     3f);
        _plugin.UpdateInt.Value = SliderRow("Update Interval", _plugin.UpdateInt.Value, 0.005f, 0.2f, "F3");

        Gap();

        if (FontRows("Nametag Font", FontManager.FontTarget.Nametag, _nametagFontActs, appliedGuiFont))
            ApplyNametagFont();

        Gap();

        Plugin.TextCase        oldCase  = _plugin.TextCaseConfig.Value;
        Plugin.TextFormatScope oldScope = _plugin.TextFormatScopeConfig.Value;
        Plugin.TextStyle       oldStyle = _plugin.TextStyleConfig.Value;

        Plugin.TextCase        newCase  = (Plugin.TextCase)OptionRow("Text Case",         _textCaseNames, (int)oldCase);
        Plugin.TextFormatScope newScope = (Plugin.TextFormatScope)OptionRow("Format Scope", _formatScopes, (int)oldScope);

        Gap();

        Plugin.TextStyle newStyle = oldStyle;
        newStyle = StyleToggle("Bold",      newStyle, Plugin.TextStyle.Bold);
        newStyle = StyleToggle("Italic",    newStyle, Plugin.TextStyle.Italic);
        newStyle = StyleToggle("Underline", newStyle, Plugin.TextStyle.Underline);

        if (newCase == oldCase && newScope == oldScope && newStyle == oldStyle)
            return;

        _plugin.TextCaseConfig.Value        = newCase;
        _plugin.TextFormatScopeConfig.Value = newScope;
        _plugin.TextStyleConfig.Value       = newStyle;
        _plugin.Formatting();
    }

    private void DrawOutlinesPage()
    {
        _plugin.OutlineEnabled.Value = ToggleRow("Enable Outlines", _plugin.OutlineEnabled.Value);

        Gap();

        _plugin.OutlineThickness.Value = SliderRow("Thickness",              _plugin.OutlineThickness.Value, 0.05f, 0.8f);
        _plugin.OutlineSoftness.Value  = SliderRow("Softness (drop shadow)", _plugin.OutlineSoftness.Value,  0.05f, 0.8f);

        Gap();

        _plugin.OutlineColor.Value = ColorRow("Outline Color", _plugin.OutlineColor.Value, true);
    }

    private void DrawChecksPage()
    {
        _plugin.CheckModLists.Value = ToggleRow("Mods", _plugin.CheckModLists.Value);

        if (_plugin.CheckModLists.Value)
        {
            _plugin.CheckMods.Value      = ToggleRow("Show Mods",       _plugin.CheckMods.Value,      true);
            _plugin.CheckCheats.Value    = ToggleRow("Show Cheats",     _plugin.CheckCheats.Value,    true);
            _plugin.AbbreviateMods.Value = ToggleRow("Abbreviate Mods", _plugin.AbbreviateMods.Value, true);
        }

        _plugin.CheckSpecial.Value = ToggleRow("Special Players", _plugin.CheckSpecial.Value);

        _plugin.CheckFps.Value = ToggleRow("FPS", _plugin.CheckFps.Value);

        if (_plugin.CheckFps.Value)
            _plugin.EstimateFPS.Value = ToggleRow("Estimate FPS", _plugin.EstimateFPS.Value, true);

        _plugin.CheckPing.Value = ToggleRow("Ping", _plugin.CheckPing.Value);

        if (_plugin.CheckPing.Value)
            _plugin.EstimatePing.Value = ToggleRow("Estimate Ping", _plugin.EstimatePing.Value, true);

        _plugin.CheckCosmetics.Value = ToggleRow("Cosmetics", _plugin.CheckCosmetics.Value);
        _plugin.CheckPlatform.Value  = ToggleRow("Platform",  _plugin.CheckPlatform.Value);
    }

    private void DrawPlatformPage()
    {
        _plugin.UsePlatIcons.Value = ToggleRow("Use Platform Icons", _plugin.UsePlatIcons.Value);

        bool left = string.Equals(_plugin.IconLocation.Value, "left", StringComparison.OrdinalIgnoreCase);

        _plugin.IconLocation.Value = OptionRow("Icon Location", _iconSides, left ? 0 : 1) == 0 ? "left" : "right";
        _plugin.IconSize.Value     = SliderRow("Icon Size", _plugin.IconSize.Value, 0f, 1f);
    }

    private void DrawIntegrationsPage() =>
            _plugin.GorillaFriends.Value = ToggleRow("Enable GorillaFriends", _plugin.GorillaFriends.Value);

    private void DrawGuiPage()
    {
        _keyAction[0] = waitingForKey ? "Press key" : _plugin.GuiButton.Value.ToString();

        if (ActionRow("Toggle Key", _keyAction) == 0)
            waitingForKey = true;

        Gap();

        if (FontRows("GUI Font", FontManager.FontTarget.Gui, _guiFontActs, appliedNametagFont))
            ApplyGuiFont();

        Gap();

        _plugin.GuiCornerRadius.Value = SliderRow("Corner Radius", _plugin.GuiCornerRadius.Value, 0f, 8f, "F1");
        _plugin.GuiOpacity.Value      = SliderRow("Opacity",       _plugin.GuiOpacity.Value,      0f, 1f);

        Gap();

        _plugin.GuiAccentColor.Value = ColorRow("Accent Color", _plugin.GuiAccentColor.Value, false);

        Gap();

        if (ActionRow("Appearance", _resetAction) != 0)
            return;

        _plugin.GuiCornerRadius.Value = (float)_plugin.GuiCornerRadius.DefaultValue;
        _plugin.GuiOpacity.Value      = (float)_plugin.GuiOpacity.DefaultValue;
        _plugin.GuiAccentColor.Value  = (Color)_plugin.GuiAccentColor.DefaultValue;
    }

#endregion
}