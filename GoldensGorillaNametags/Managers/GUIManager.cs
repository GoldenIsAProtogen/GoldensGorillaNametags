using System;
using GoldensGorillaNametags.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoldensGorillaNametags.Managers;

public class GUIManager
{
#region ===== - Fields & Init - =====

    private readonly Plugin _plugin;

    public GUIManager(Plugin plugin) => _plugin = plugin;

    private static readonly Color _paleGoldenRod = new(0.93f, 0.91f, 0.67f), _paleGreen = new(0.6f, 0.98f, 0.6f), _darkGray = new(0.25f, 0.25f, 0.25f), _darkerGray = new(0.2f, 0.2f, 0.2f), _darkestGray = new(0.15f, 0.15f, 0.15f), _white = Color.white, _black = Color.black;

    private readonly string[] pages =
    [
            "Tags", "Outlines", "Checks", "Platform", "Integrations", "GUI",
    ];

    private int     currentPage, activeSliderId = -1;
    private Vector2 pageScrollPos               = Vector2.zero;
    private Rect    windowRect                  = new(200f, 120f, 560f, 520f);

    private GUIStyle scrollbarStyle, scrollbarThumbStyle, sliderStyle, sliderThumbStyle, windowStyle, boxStyle, buttonStyle, toggleStyle, subtoggleStyle, labelStyle, centeredLabelStyle;

    private bool sliderBeingDragged, stylesInitialized, waitingForKey, guiOpen;

#endregion

#region ===== - Runtime - =====

    public void Update()
    {
        if (Keyboard.current != null && Keyboard.current[_plugin.GuiButton.Value].wasPressedThisFrame)
        {
            guiOpen          = !guiOpen;
            Cursor.visible   = guiOpen;
            Cursor.lockState = guiOpen ? CursorLockMode.None : CursorLockMode.Locked;
        }
    }

    public void OnGUI()
    {
        InitStyles();

        if (!guiOpen) return;

        GUI.backgroundColor = _white;
        windowRect          = GUI.Window(42069420, windowRect, DrawWindow, GUIContent.none, windowStyle);

        if (waitingForKey) CaptureKey();
    }

#endregion

#region ===== - Styles & Helpers - =====

    private void InitStyles()
    {
        if (stylesInitialized)
            return;

        windowStyle                      = new GUIStyle(GUI.skin.window);
        windowStyle.normal.background    = MakeTex(1, 1, _darkestGray);
        windowStyle.focused.background   = windowStyle.normal.background;
        windowStyle.active.background    = windowStyle.normal.background;
        windowStyle.onNormal.background  = windowStyle.normal.background;
        windowStyle.onFocused.background = windowStyle.normal.background;
        windowStyle.onActive.background  = windowStyle.normal.background;
        windowStyle.padding              = new RectOffset(10, 10, 10, 10);

        boxStyle                   = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = MakeTex(1, 1, _darkerGray);
        boxStyle.padding           = new RectOffset(8, 8, 8, 8);

        buttonStyle                   = new GUIStyle(GUI.skin.button);
        buttonStyle.normal.background = MakeTex(1, 1, _darkGray);
        buttonStyle.hover.background  = MakeTex(1, 1, _paleGoldenRod);
        buttonStyle.active.background = MakeTex(1, 1, _darkerGray);
        buttonStyle.normal.textColor  = _white;
        buttonStyle.hover.textColor   = _black;
        buttonStyle.active.textColor  = _white;
        buttonStyle.alignment         = TextAnchor.MiddleCenter;
        buttonStyle.padding           = new RectOffset(8, 8, 6, 6);
        buttonStyle.margin            = new RectOffset(4, 4, 4, 4);

        sliderStyle             = new GUIStyle(GUI.skin.horizontalSlider);
        sliderStyle.margin      = new RectOffset(4, 4, 8, 8);
        sliderStyle.fixedHeight = 12;

        sliderThumbStyle                   = new GUIStyle(GUI.skin.horizontalSliderThumb);
        sliderThumbStyle.normal.background = MakeTex(1, 1, _paleGoldenRod);
        sliderThumbStyle.hover.background  = MakeTex(1, 1, _paleGreen);
        sliderThumbStyle.active.background = MakeTex(1, 1, _paleGoldenRod);
        sliderThumbStyle.fixedWidth        = 12;
        sliderThumbStyle.fixedHeight       = 12;

        toggleStyle                     = new GUIStyle(buttonStyle);
        toggleStyle.onNormal.background = MakeTex(1, 1, _paleGreen);
        toggleStyle.onHover.background  = MakeTex(1, 1, _paleGoldenRod);
        toggleStyle.onActive.background = MakeTex(1, 1, _darkerGray);
        toggleStyle.onNormal.textColor  = _black;
        toggleStyle.onHover.textColor   = _black;
        toggleStyle.onActive.textColor  = _white;

        subtoggleStyle         = new GUIStyle(toggleStyle);
        subtoggleStyle.padding = new RectOffset(4, 4, 2, 2);

        labelStyle                  = new GUIStyle(GUI.skin.label);
        labelStyle.normal.textColor = _white;
        labelStyle.margin           = new RectOffset(4, 4, 4, 4);

        centeredLabelStyle           = new GUIStyle(labelStyle);
        centeredLabelStyle.alignment = TextAnchor.MiddleCenter;

        scrollbarStyle                   = new GUIStyle(GUI.skin.verticalScrollbar);
        scrollbarStyle.normal.background = MakeTex(1, 1, _darkestGray);
        scrollbarStyle.hover.background  = MakeTex(1, 1, _darkestGray);
        scrollbarStyle.active.background = MakeTex(1, 1, _darkestGray);

        scrollbarThumbStyle                   = new GUIStyle(GUI.skin.verticalScrollbarThumb);
        scrollbarThumbStyle.normal.background = MakeTex(1, 1, _darkGray);
        scrollbarThumbStyle.hover.background  = MakeTex(1, 1, _paleGoldenRod);
        scrollbarThumbStyle.active.background = MakeTex(1, 1, _paleGreen);

        stylesInitialized = true;
    }

    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; i++)
            pix[i] = col;

        Texture2D result = new(width, height);
        result.SetPixels(pix);
        result.Apply();

        return result;
    }

    private bool StyledButton(string label, bool selected = false, params GUILayoutOption[] options)
    {
        bool clicked = GUILayout.Toggle(selected, label, toggleStyle, options);

        return clicked && !selected;
    }

    private bool StyledToggle(bool value, string label, params GUILayoutOption[] options) => GUILayout.Toggle(value, label, toggleStyle, options);

    private bool SubStyledToggle(bool value, string label, params GUILayoutOption[] options) => GUILayout.Toggle(value, label, subtoggleStyle, options);

    private float StyledSlider(float value, float leftValue, float rightValue, int sliderId = 0)
    {
        GUIStyle themedSliderStyle = new(sliderStyle);
        themedSliderStyle.normal.background = MakeTex(1, 1, _darkestGray);

        Rect sliderRect = GUILayoutUtility.GetRect(GUIContent.none, themedSliderStyle, GUILayout.ExpandWidth(true));

        Vector2 mousePos        = Event.current.mousePosition;
        float   normalizedValue = (value - leftValue) / (rightValue - leftValue);
        float   thumbWidth      = 12f;
        float   thumbX          = sliderRect.x + normalizedValue * (sliderRect.width - thumbWidth);
        Rect    thumbRect       = new(thumbX, sliderRect.y, thumbWidth, sliderRect.height);

        bool isHover    = thumbRect.Contains(mousePos);
        bool isDown     = Event.current.type == EventType.MouseDown && thumbRect.Contains(mousePos);
        bool isDragging = sliderBeingDragged                        && activeSliderId == sliderId;

        if (isDown)
        {
            sliderBeingDragged = true;
            activeSliderId     = sliderId;
        }

        if (Event.current.type == EventType.MouseUp)
        {
            sliderBeingDragged = false;
            activeSliderId     = -1;
        }

        Color thumbColor;
        if (isDragging)
            thumbColor = _paleGreen;
        else if (isHover)
            thumbColor = _paleGoldenRod;
        else
            thumbColor = _darkGray;

        GUIStyle customThumbStyle = new(sliderThumbStyle);
        customThumbStyle.normal.background = MakeTex(1, 1, thumbColor);
        customThumbStyle.hover.background  = MakeTex(1, 1, thumbColor);
        customThumbStyle.active.background = MakeTex(1, 1, thumbColor);

        return GUI.HorizontalSlider(sliderRect, value, leftValue, rightValue, themedSliderStyle, customThumbStyle);
    }

    private Color DrawColorSlider(Color c)
    {
        GUILayout.Label("R", centeredLabelStyle);
        float r = StyledSlider(c.r, 0f, 1f, 1);
        GUILayout.Label("G", centeredLabelStyle);
        float g = StyledSlider(c.g, 0f, 1f, 2);
        GUILayout.Label("B", centeredLabelStyle);
        float b = StyledSlider(c.b, 0f, 1f, 3);
        GUILayout.Label("A", centeredLabelStyle);
        float a = StyledSlider(c.a, 0f, 1f, 4);

        return new Color(r, g, b, a);
    }

    private int StyledSelectionGridVerticalFullWidth(int selected, string[] labels)
    {
        int result = selected;

        for (int i = 0; i < labels.Length; i++)
            if (StyledButton(labels[i], i == selected, GUILayout.ExpandWidth(true)))
                result = i;

        return result;
    }

#endregion

#region ===== - Window & Pages - =====

    private void DrawWindow(int id)
    {
        GUILayout.BeginVertical();
        GUILayout.Label($"<b>{Constants.Name} Configuration | v{Constants.Version}{Constants.BuildType}</b>",
                new GUIStyle(centeredLabelStyle)
                {
                        fontSize = 18, richText = true,
                });

        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        int tabWidth = (int)(windowRect.width - 40) / pages.Length;
        for (int i = 0; i < pages.Length; i++)
            if (StyledButton(pages[i], i == currentPage, GUILayout.Width(tabWidth)))
            {
                currentPage   = i;
                pageScrollPos = Vector2.zero;
            }

        GUILayout.EndHorizontal();
        GUILayout.Space(12);
        GUILayout.BeginVertical(boxStyle);

        float scrollbarWidth   = 20f;
        float scrollbarPadding = 12f;
        float contentWidth     = windowRect.width - 40 - scrollbarWidth - scrollbarPadding;

        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(contentWidth));

        pageScrollPos
                = GUILayout.BeginScrollView(pageScrollPos, false, true, GUIStyle.none, scrollbarThumbStyle, GUILayout.Height(windowRect.height - 145f), GUILayout.Width(contentWidth + scrollbarWidth + scrollbarPadding));

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
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.Label(waitingForKey ? "Press any key to rebind..." : $"Press <b>\"{_plugin.GuiButton.Value}\"</b> to open/close",
                new GUIStyle(centeredLabelStyle)
                {
                        fontSize = 11, richText = true,
                });

        GUILayout.EndVertical();
        GUI.DragWindow();
    }

    private void DrawTagsPage()
    {
        GUILayout.Space(8);
        GUILayout.Label($"Tag Size: {_plugin.TagSize.Value:F2}", centeredLabelStyle);
        _plugin.TagSize.Value = StyledSlider(_plugin.TagSize.Value, 0.2f, 3f, 10);

        GUILayout.Label($"Tag Height: {_plugin.TagHeight.Value:F2}", centeredLabelStyle);
        _plugin.TagHeight.Value = StyledSlider(_plugin.TagHeight.Value, 0.2f, 2f, 11);

        GUILayout.Label($"Update Interval: {_plugin.UpdateInt.Value:F3}", centeredLabelStyle);
        _plugin.UpdateInt.Value = StyledSlider(_plugin.UpdateInt.Value, 0.005f, 0.2f, 12);

        GUILayout.Space(10);
        GUILayout.Label($"Nametag Font: {FontManager.GetSelectedFontName()}", centeredLabelStyle);
        GUILayout.BeginHorizontal();

        if (StyledButton("<", false, GUILayout.Width(40)))
            FontManager.PreviousFont();

        if (StyledButton("Refresh List", false, GUILayout.ExpandWidth(true)))
            FontManager.RefreshAvailableFonts();

        if (StyledButton(">", false, GUILayout.Width(40)))
            FontManager.NextFont();

        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        if (StyledButton("Apply Font", false, GUILayout.ExpandWidth(true)) && TagManager.Instance != null)
            TagManager.Instance.ClearTags();

        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        GUILayout.Label("Text Case", centeredLabelStyle);
        _plugin.TextCaseConfig.Value = (Plugin.TextCase)StyledSelectionGridVerticalFullWidth((int)_plugin.TextCaseConfig.Value, Enum.GetNames(typeof(Plugin.TextCase)));

        GUILayout.EndVertical();
        GUILayout.Space(10);
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        GUILayout.Label("Text Style", centeredLabelStyle);
        Plugin.TextStyle style = _plugin.TextStyleConfig.Value;

        bool bold    = (style & Plugin.TextStyle.Bold) != 0;
        bool newBold = StyledToggle(bold, "Bold", GUILayout.ExpandWidth(true));
        if (newBold != bold)
            style = newBold ? style | Plugin.TextStyle.Bold : style & ~Plugin.TextStyle.Bold;

        bool italic    = (style & Plugin.TextStyle.Italic) != 0;
        bool newItalic = StyledToggle(italic, "Italic", GUILayout.ExpandWidth(true));
        if (newItalic != italic)
            style = newItalic ? style | Plugin.TextStyle.Italic : style & ~Plugin.TextStyle.Italic;

        bool underline    = (style & Plugin.TextStyle.Underline) != 0;
        bool newUnderline = StyledToggle(underline, "Underline", GUILayout.ExpandWidth(true));
        if (newUnderline != underline)
            style = newUnderline ? style | Plugin.TextStyle.Underline : style & ~Plugin.TextStyle.Underline;

        _plugin.TextStyleConfig.Value = style;

        GUILayout.EndVertical();
        GUILayout.Space(10);
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        GUILayout.Label("Format Scope", centeredLabelStyle);
        _plugin.TextFormatScopeConfig.Value = (Plugin.TextFormatScope)StyledSelectionGridVerticalFullWidth((int)_plugin.TextFormatScopeConfig.Value, Enum.GetNames(typeof(Plugin.TextFormatScope)));

        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        _plugin.Formatting();
    }

    private void DrawOutlinesPage()
    {
        _plugin.OutlineEnabled.Value = StyledToggle(_plugin.OutlineEnabled.Value, "Enable Outlines", GUILayout.ExpandWidth(true));

        GUILayout.Space(8);
        GUILayout.Label($"Thickness: {_plugin.OutlineThickness.Value:F2}", centeredLabelStyle);
        _plugin.OutlineThickness.Value = StyledSlider(_plugin.OutlineThickness.Value, 0.05f, 1f, 20);

        GUILayout.Label($"Softness (drop shadow): {_plugin.OutlineSoftness.Value:F2}", centeredLabelStyle);
        _plugin.OutlineSoftness.Value = StyledSlider(_plugin.OutlineSoftness.Value, 0.05f, 1f, 20);

        GUILayout.Space(10);
        GUILayout.Label("Outline Color", centeredLabelStyle);
        _plugin.OutlineColor.Value = DrawColorSlider(_plugin.OutlineColor.Value);
    }

    private void DrawChecksPage()
    {
        _plugin.CheckModLists.Value = StyledToggle(_plugin.CheckModLists.Value, "Mods", GUILayout.ExpandWidth(true));

        if (_plugin.CheckModLists.Value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(8);

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            _plugin.CheckMods.Value      = SubStyledToggle(_plugin.CheckMods.Value,      "Show Mods",       GUILayout.ExpandWidth(true));
            _plugin.CheckCheats.Value    = SubStyledToggle(_plugin.CheckCheats.Value,    "Show Cheats",     GUILayout.ExpandWidth(true));
            _plugin.AbbreviateMods.Value = SubStyledToggle(_plugin.AbbreviateMods.Value, "Abbreviate Mods", GUILayout.ExpandWidth(true));

            GUILayout.EndVertical();
            GUILayout.Space(6);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
        }

        _plugin.CheckSpecial.Value = StyledToggle(_plugin.CheckSpecial.Value, "Special Players", GUILayout.ExpandWidth(true));

        _plugin.CheckFps.Value = StyledToggle(_plugin.CheckFps.Value, "FPS", GUILayout.ExpandWidth(true));

        if (_plugin.CheckFps.Value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(8);

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            _plugin.EstimateFPS.Value = SubStyledToggle(_plugin.EstimateFPS.Value, "Estimate FPS", GUILayout.ExpandWidth(true));

            GUILayout.EndVertical();
            GUILayout.Space(6);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
        }

        _plugin.CheckPing.Value = StyledToggle(_plugin.CheckPing.Value, "Ping", GUILayout.ExpandWidth(true));

        if (_plugin.CheckPing.Value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(8);

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            _plugin.EstimatePing.Value = SubStyledToggle(_plugin.EstimatePing.Value, "Estimate Ping", GUILayout.ExpandWidth(true));

            GUILayout.EndVertical();
            GUILayout.Space(6);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
        }

        _plugin.CheckCosmetics.Value = StyledToggle(_plugin.CheckCosmetics.Value, "Cosmetics", GUILayout.ExpandWidth(true));
        _plugin.CheckPlatform.Value  = StyledToggle(_plugin.CheckPlatform.Value,  "Platform",  GUILayout.ExpandWidth(true));
    }

    private void DrawPlatformPage()
    {
        _plugin.UsePlatIcons.Value = StyledToggle(_plugin.UsePlatIcons.Value, "Use Platform Icons", GUILayout.ExpandWidth(true));

        GUILayout.Space(8);
        GUILayout.Label("Icon Location", centeredLabelStyle);

        int iconLeftRight = _plugin.IconLocation.Value.ToLower() switch
                            {
                                    "left" => 0, var _ => 1,
                            };

        if (StyledButton("Left",  iconLeftRight == 0, GUILayout.ExpandWidth(true))) iconLeftRight = 0;
        if (StyledButton("Right", iconLeftRight == 1, GUILayout.ExpandWidth(true))) iconLeftRight = 1;

        _plugin.IconLocation.Value = iconLeftRight == 0 ? "left" : "right";

        GUILayout.Space(8);
        GUILayout.Label($"Icon Size: {_plugin.IconSize.Value:F2}", centeredLabelStyle);
        _plugin.IconSize.Value = StyledSlider(_plugin.IconSize.Value, 0, 1, 30);
    }

    private void DrawIntegrationsPage()
    {
        _plugin.GorillaFriends.Value = StyledToggle(_plugin.GorillaFriends.Value, "Enable GorillaFriends", GUILayout.ExpandWidth(true));
    }

    private void DrawGuiPage()
    {
        GUILayout.Label($"GUI Toggle Key: <b>{_plugin.GuiButton.Value.ToString()}</b>", centeredLabelStyle);
        if (StyledButton(waitingForKey ? "Press any key..." : "Rebind", false, GUILayout.ExpandWidth(true)))
            waitingForKey = true;
    }

    private void CaptureKey()
    {
        Event e = Event.current;

        if (e is not { isKey: true, } || e.type != EventType.KeyDown) return;

        _plugin.GuiButton.Value = (Key)e.keyCode;
        waitingForKey           = false;
        e.Use();
    }

#endregion
}