using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using GoldensGorillaNametags.Core;
using GoldensGorillaNametags.Patches;
using HarmonyLib;
using Photon.Pun;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.TextCore.LowLevel;

namespace GoldensGorillaNametags;

[BepInPlugin(Constants.Guid, Constants.Name, Constants.Version)]
public class Plugin : BaseUnityPlugin
{
#region ==-== Fields & Init ==-==

    public enum TextCase
    {
        Normal,
        Uppercase,
        Lowercase,
    }

    public enum TextFormatScope
    {
        NameOnly,
        AllText,
    }

    [Flags]
    public enum TextStyle
    {
        Normal    = 0,
        Bold      = 1 << 0,
        Italic    = 1 << 1,
        Underline = 1 << 2,
    }

    private const float CacheInt = 150f;

    public static readonly  string GitUrl     = "https://raw.githubusercontent.com/GoldenIsAProtogen/GoldensGorillaNametags/main/";
    private static readonly Color  _paleGoldenRod = new(0.93f, 0.91f, 0.67f), _paleGreen = new(0.6f, 0.98f, 0.6f), _darkGray = new(0.25f, 0.25f, 0.25f), _darkerGray = new(0.2f, 0.2f, 0.2f), _darkestGray = new(0.15f, 0.15f, 0.15f), _white = Color.white, _black = Color.black;

    public Camera        CineCam;
    public TMP_FontAsset Font;
    public Transform     MainCam;

    public string FormatPrefix = "";
    public string FormatSuffix = "";

    public readonly Regex ColorTagRegex = new("<color=[^>]+>|</color>", RegexOptions.Compiled);

    private readonly string[] pages =
    [
            "Tags", "Outlines", "Checks", "Platform", "Integrations", "GUI",
    ];

    public readonly Dictionary<VRRig, int> playerPing = new();

    private int        currentPage, activeSliderId = -1;
    private GameObject componentHolder;

    private float lastCacheTime, lastUpdateTime;

    private Vector2 pageScrollPos = Vector2.zero;

    private GUIStyle scrollbarStyle, scrollbarThumbStyle, sliderStyle, sliderThumbStyle, windowStyle, boxStyle, buttonStyle, toggleStyle, subtoggleStyle, labelStyle, centeredLabelStyle;

    private bool tagsEnabled, sliderBeingDragged, stylesInitialized, waitingForKey, guiOpen;

    public ConfigEntry<Key>    GuiButton;
    public ConfigEntry<string> IconLocation;

    public ConfigEntry<Color> OutlineColor;

    public ConfigEntry<bool> OutlineEnabled, OutlineQuality, CheckPlatform, CheckSpecial, CheckFps, CheckPing, CheckCosmetics, GorillaFriends, TextQuality, UsePlatIcons;

    public ConfigEntry<float> TagSize, TagHeight, UpdateInt, OutlineThickness, IconSize;

    public ConfigEntry<TextCase>        TextCaseConfig;
    public ConfigEntry<TextFormatScope> TextFormatScopeConfig;
    public ConfigEntry<TextStyle>       TextStyleConfig;

    private Rect windowRect = new(200f, 120f, 560f, 520f);

    internal static Plugin Instance { get; set; }

    public Texture2D SteamTex   { get; set; }
    public Texture2D PcvrTex    { get; set; }
    public Texture2D QuestpcTex { get; set; }
    public Texture2D QuestTex   { get; set; }

#endregion

#region ==-== Startup & Config ==-==

    private void Start()
    {
        Instance = this;
        GorillaTagger.OnPlayerSpawned(OnInit);
        CosmeticsV2Spawner_Dirty.OnPostInstantiateAllPrefabs += OnCosmeticsLoaded;
        InitConfig();
        InitFont();
        InitCam();
        InitHarmony();
        Formatting();
    }

    private void OnInit()
    {
        componentHolder = new GameObject("GoldensGorillaNametags Component Holder");
        componentHolder.AddComponent<TagUtils>();
        componentHolder.AddComponent<TagManager>();

        PlayerSerializePatch.OnPlayerSerialize += rig => { playerPing[rig] = GetTruePing(rig); };
    }

    private void InitConfig()
    {
        TagSize               = Config.Bind("Tags", "Size",         2.5f,                     "Nametag size");
        TagHeight             = Config.Bind("Tags", "Height",       0.85f,                    "Nametag height");
        UpdateInt             = Config.Bind("Tags", "Update Int",   0.01f,                    "Tag update interval");
        TextQuality           = Config.Bind("Tags", "Quality",      false,                    "Nametag quality");
        TextStyleConfig       = Config.Bind("Tags", "Style",        TextStyle.Normal,         "Text style");
        TextCaseConfig        = Config.Bind("Tags", "Case",         TextCase.Normal,          "Text casing: Normal, Uppercase, Lowercase");
        TextFormatScopeConfig = Config.Bind("Tags", "Format Scope", TextFormatScope.NameOnly, "Choose whether formatting applies only to player names or to all text.");

        OutlineEnabled   = Config.Bind("Outlines", "Enabled",   true,   "Tag outlines");
        OutlineQuality   = Config.Bind("Outlines", "Quality",   false,  "Outline quality");
        OutlineColor     = Config.Bind("Outlines", "Color",     _black, "Outline color");
        OutlineThickness = Config.Bind("Outlines", "Thickness", 0.4f,   "Outline thickness");
        
        CheckSpecial   = Config.Bind("Checks", "Special",   true,  "Check special players");
        CheckFps       = Config.Bind("Checks", "FPS",       true,  "Check FPS");
        CheckPing      = Config.Bind("Checks", "Ping",      false, "Check Ping (Ping estimation, not 100% accurate)");
        CheckCosmetics = Config.Bind("Checks", "Cosmetics", true,  "Check cosmetics");
        CheckPlatform  = Config.Bind("Checks", "Platform",  true,  "Check platform");

        UsePlatIcons = Config.Bind("Platform", "UseIcons",      false,  "Show platform as icons instead of text");
        IconSize     = Config.Bind("Platform", "Icon Size",     0.07f,  "Size of the platform icons");
        IconLocation = Config.Bind("Platform", "Icon Location", "left", "Platform icon position\nAcceptable Values: left, right");

        GorillaFriends = Config.Bind("Integrations", "GorillaFriends", false, "Use GorillaFriends");

        GuiButton = Config.Bind("GUI", "Toggle Key", Key.F4, "Key to toggle the GUI");
    }

    private void InitFont()
    {
        string fontDir = Path.Combine(Paths.BepInExRootPath, "Fonts");
        if (!Directory.Exists(fontDir))
            Directory.CreateDirectory(fontDir);

        string fontPath
                = Directory.EnumerateFiles(fontDir, "*.*").FirstOrDefault(path => path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));

        try
        {
            if (fontPath != null)
            {
                Font unityFont = new(fontPath);
                Font
                        = TextQuality.Value
                                  ? TMP_FontAsset.CreateFontAsset(unityFont, 120, 12, GlyphRenderMode.SDFAA, 4096, 4096)
                                  : TMP_FontAsset.CreateFontAsset(unityFont);

                Font.material.shader = Shader.Find("TextMeshPro/Distance Field");
            }
            else
            {
                Font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Arial SDF");
            }
        }
        catch
        {
            Font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Arial SDF");
        }
    }

    private void InitCam()
    {
        try
        {
            CineCam = FindFirstObjectByType<CinemachineBrain>()?.GetComponent<Camera>();
        }
        catch
        {
            CineCam = null;
        }
    }

    private void InitHarmony()
    {
        Harmony harmony = new(Constants.Guid);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

#endregion

#region ==-== Runtime ==-==

    public void Update()
    {
        if (Keyboard.current != null && Keyboard.current[GuiButton.Value].wasPressedThisFrame)
        {
            guiOpen          = !guiOpen;
            Cursor.visible   = guiOpen;
            Cursor.lockState = guiOpen ? CursorLockMode.None : CursorLockMode.Locked;
        }

        if (!tagsEnabled)
            return;

        if (Time.time - lastCacheTime >= CacheInt)
        {
            TagUtils.Instance.RefreshCache();
            lastCacheTime = Time.time;
        }

        if (MainCam == null || Camera.main != null)
            MainCam = Camera.main?.transform;

        float currentTime = Time.time;

        if (!(currentTime - lastUpdateTime >= UpdateInt.Value))
            return;

        foreach (VRRig rig in VRRigCache.ActiveRigs
                                        .Where(vrrig => vrrig != null && !vrrig.isOfflineVRRig && vrrig.mainSkin?.material != null)
                                        .Where(vrriggy => vrriggy.mainSkin.material.name
                                                                 .Contains("gorilla_body") && vrriggy.mainSkin.material.shader == Shader.Find("GorillaTag/UberShader"))) rig.mainSkin.material.color = rig.playerColor;

        HashSet<VRRig> currentRigs = new(VRRigCache.ActiveRigs ?? new List<VRRig>());
        TagManager.Instance.CleanupTags(currentRigs);
        TagManager.Instance.CreateTagmap(currentRigs);
        TagManager.Instance.UpdateTags();
        lastUpdateTime = currentTime;
    }

    private void OnEnable()
    {
        tagsEnabled = true;

        if (TagManager.Instance != null)
            TagManager.Instance.ClearTags();
    }

    private void OnDisable()
    {
        tagsEnabled = false;

        if (TagManager.Instance != null)
            TagManager.Instance.ClearTags();
    }

    public void Formatting()
    {
        FormatPrefix = "";
        FormatSuffix = "";

        TextStyle style = TextStyleConfig.Value;

        if (style.HasFlag(TextStyle.Bold))
        {
            FormatPrefix += "<b>";
            FormatSuffix =  "</b>" + FormatSuffix;
        }

        if (style.HasFlag(TextStyle.Italic))
        {
            FormatPrefix += "<i>";
            FormatSuffix =  "</i>" + FormatSuffix;
        }

        if (style.HasFlag(TextStyle.Underline))
        {
            FormatPrefix += "<u>";
            FormatSuffix =  "</u>" + FormatSuffix;
        }
    }

    public string TextFormat(string text)
    {
        TextCase c = TextCaseConfig.Value;

        text = c switch
               {
                       TextCase.Uppercase => text.ToUpperInvariant(), TextCase.Lowercase => text.ToLowerInvariant(), var _ => text,
               };

        if (FormatPrefix.Length == 0)
            return text;

        return FormatPrefix + text + FormatSuffix;
    }

    private int GetTruePing(VRRig rig)
    {
        double ping = Math.Abs((rig.velocityHistoryList[0].time - PhotonNetwork.Time) * 1000);

        return (int)Math.Clamp(Math.Round(ping), 0, int.MaxValue);
    }

#endregion

#region ==-== Icons ==-==

    private void OnCosmeticsLoaded() => StartCoroutine(DownloadAndCacheIcons());

    private IEnumerator DownloadAndCacheIcons()
    {
        string dllPath  = Assembly.GetExecutingAssembly().Location;
        string dllDir   = Path.GetDirectoryName(dllPath);
        string iconsDir = Path.Combine(dllDir, "Icons");

        if (!Directory.Exists(iconsDir))
            Directory.CreateDirectory(iconsDir);

        (string, string)[] icons = new[]
        {
                ("steamicon.png", $"{GitUrl}GoldensGorillaNametags/Icons/steamicon.png"), ("pcvricon.png", $"{GitUrl}GoldensGorillaNametags/Icons/pcvricon.png"), ("questpcicon.png", $"{GitUrl}GoldensGorillaNametags/Icons/questpcicon.png"), ("questicon.png", $"{GitUrl}GoldensGorillaNametags/Icons/questicon.png"),
        };

        foreach ((string fileName, string url) in icons)
        {
            string filePath = Path.Combine(iconsDir, fileName);

            if (!File.Exists(filePath))
            {
                using UnityWebRequest request = UnityWebRequest.Get(url);
                request.timeout = 30;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                    try
                    {
                        File.WriteAllBytes(filePath, request.downloadHandler.data);
                    }
                    catch
                    {
                        // ignored
                    }
            }

            yield return new WaitForSeconds(0.1f);
        }

        LoadIconsFromFile(iconsDir);

        if (TagUtils.Instance != null)
            TagUtils.Instance.RefreshCache();
    }

    private void LoadIconsFromFile(string iconsDir)
    {
        SteamTex   = LoadIconFromFile(Path.Combine(iconsDir, "steamicon.png"));
        PcvrTex    = LoadIconFromFile(Path.Combine(iconsDir, "pcvricon.png"));
        QuestpcTex = LoadIconFromFile(Path.Combine(iconsDir, "questpcicon.png"));
        QuestTex   = LoadIconFromFile(Path.Combine(iconsDir, "questicon.png"));
    }

    private Texture2D LoadIconFromFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            byte[] fileData = File.ReadAllBytes(path);

            if (fileData.Length == 0)
                return null;

            Texture2D tex = new(2, 2, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            if (tex.LoadImage(fileData))
                return tex;

            Destroy(tex);

            return null;
        }
        catch
        {
            return null;
        }
    }

#endregion

#region ==-== GUI ==-==

    private void OnGUI()
    {
        InitStyles();

        if (!guiOpen) return;

        GUI.backgroundColor = _white;
        windowRect          = GUI.Window(42069420, windowRect, DrawWindow, GUIContent.none, windowStyle);

        if (waitingForKey) CaptureKey();
    }

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

    private void DrawWindow(int id)
    {
        GUILayout.BeginVertical();
        GUILayout.Label($"<b>{Constants.Name} Configuration | v{Constants.Version}</b>",
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
        GUILayout.Label(waitingForKey ? "Press any key to rebind..." : $"Press <b>\"{GuiButton.Value}\"</b> to open/close",
                new GUIStyle(centeredLabelStyle)
                {
                        fontSize = 11, richText = true,
                });

        GUILayout.EndVertical();
        GUI.DragWindow();
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

    private void DrawTagsPage()
    {
        TextQuality.Value = StyledToggle(TextQuality.Value, "High Quality Text", GUILayout.ExpandWidth(true));

        GUILayout.Space(8);
        GUILayout.Label($"Tag Size: {TagSize.Value:F2}", centeredLabelStyle);
        TagSize.Value = StyledSlider(TagSize.Value, 0.2f, 3f, 10);

        GUILayout.Label($"Tag Height: {TagHeight.Value:F2}", centeredLabelStyle);
        TagHeight.Value = StyledSlider(TagHeight.Value, 0.2f, 2f, 11);

        GUILayout.Label($"Update Interval: {UpdateInt.Value:F3}", centeredLabelStyle);
        UpdateInt.Value = StyledSlider(UpdateInt.Value, 0.005f, 0.2f, 12);

        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        GUILayout.Label("Text Case", centeredLabelStyle);
        TextCaseConfig.Value = (TextCase)StyledSelectionGridVerticalFullWidth((int)TextCaseConfig.Value, Enum.GetNames(typeof(TextCase)));

        GUILayout.EndVertical();
        GUILayout.Space(10);
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        GUILayout.Label("Text Style", centeredLabelStyle);
        TextStyle style = TextStyleConfig.Value;

        bool bold    = (style & TextStyle.Bold) != 0;
        bool newBold = StyledToggle(bold, "Bold", GUILayout.ExpandWidth(true));
        if (newBold != bold)
            style = newBold ? style | TextStyle.Bold : style & ~TextStyle.Bold;

        bool italic    = (style & TextStyle.Italic) != 0;
        bool newItalic = StyledToggle(italic, "Italic", GUILayout.ExpandWidth(true));
        if (newItalic != italic)
            style = newItalic ? style | TextStyle.Italic : style & ~TextStyle.Italic;

        bool underline    = (style & TextStyle.Underline) != 0;
        bool newUnderline = StyledToggle(underline, "Underline", GUILayout.ExpandWidth(true));
        if (newUnderline != underline)
            style = newUnderline ? style | TextStyle.Underline : style & ~TextStyle.Underline;

        TextStyleConfig.Value = style;

        GUILayout.EndVertical();
        GUILayout.Space(10);
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        GUILayout.Label("Format Scope", centeredLabelStyle);
        TextFormatScopeConfig.Value = (TextFormatScope)StyledSelectionGridVerticalFullWidth((int)TextFormatScopeConfig.Value, Enum.GetNames(typeof(TextFormatScope)));

        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        Formatting();
    }

    private void DrawOutlinesPage()
    {
        OutlineEnabled.Value = StyledToggle(OutlineEnabled.Value, "Enable Outlines",       GUILayout.ExpandWidth(true));
        OutlineQuality.Value = StyledToggle(OutlineQuality.Value, "High Quality Outlines", GUILayout.ExpandWidth(true));

        GUILayout.Space(8);
        GUILayout.Label($"Thickness: {OutlineThickness.Value:F2}", centeredLabelStyle);
        OutlineThickness.Value = StyledSlider(OutlineThickness.Value, 0f, 1f, 20);

        GUILayout.Space(10);
        GUILayout.Label("Outline Color", centeredLabelStyle);
        OutlineColor.Value = DrawColorSlider(OutlineColor.Value);
    }

    private void DrawChecksPage()
    {
        CheckSpecial.Value   = StyledToggle(CheckSpecial.Value,   "Special Players", GUILayout.ExpandWidth(true));
        CheckFps.Value       = StyledToggle(CheckFps.Value,       "FPS",             GUILayout.ExpandWidth(true));
        CheckPing.Value      = StyledToggle(CheckPing.Value,      "Ping",            GUILayout.ExpandWidth(true));
        CheckCosmetics.Value = StyledToggle(CheckCosmetics.Value, "Cosmetics",       GUILayout.ExpandWidth(true));
        CheckPlatform.Value  = StyledToggle(CheckPlatform.Value,  "Platform",        GUILayout.ExpandWidth(true));
    }

    private void DrawPlatformPage()
    {
        UsePlatIcons.Value = StyledToggle(UsePlatIcons.Value, "Use Platform Icons", GUILayout.ExpandWidth(true));

        GUILayout.Space(8);
        GUILayout.Label("Icon Location", centeredLabelStyle);

        int iconLeftRight = IconLocation.Value.ToLower() switch
                            {
                                    "left" => 0, var _ => 1,
                            };

        if (StyledButton("Left",  iconLeftRight == 0, GUILayout.ExpandWidth(true))) iconLeftRight = 0;
        if (StyledButton("Right", iconLeftRight == 1, GUILayout.ExpandWidth(true))) iconLeftRight = 1;

        IconLocation.Value = iconLeftRight == 0 ? "left" : "right";

        GUILayout.Space(8);
        GUILayout.Label($"Icon Size: {IconSize.Value:F2}", centeredLabelStyle);
        IconSize.Value = StyledSlider(IconSize.Value, 0, 1, 30);
    }

    private void DrawIntegrationsPage()
    {
        GorillaFriends.Value = StyledToggle(GorillaFriends.Value, "Enable GorillaFriends", GUILayout.ExpandWidth(true));
    }

    private void DrawGuiPage()
    {
        GUILayout.Label($"GUI Toggle Key: <b>{GuiButton.Value.ToString()}</b>", centeredLabelStyle);
        if (StyledButton(waitingForKey ? "Press any key..." : "Rebind", false, GUILayout.ExpandWidth(true)))
            waitingForKey = true;
    }

    private void CaptureKey()
    {
        Event e = Event.current;

        if (e is not { isKey: true, } || e.type != EventType.KeyDown) return;

        GuiButton.Value = (Key)e.keyCode;
        waitingForKey   = false;
        e.Use();
    }

#endregion
}