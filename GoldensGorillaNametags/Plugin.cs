using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using GoldensGorillaNametags.Core;
using GoldensGorillaNametags.Managers;
using GoldensGorillaNametags.Patches;
using HarmonyLib;
using Photon.Pun;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace GoldensGorillaNametags;

[BepInPlugin(Constants.Guid, Constants.Name, Constants.Version)]
public class Plugin : BaseUnityPlugin
{
#region ===== - Fields & Init - =====

    public enum TextCase
    {
        Normal,
        Uppercase,
        Lowercase,
        Titlecase,
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

    private bool _modListsChecked, _modsChecked, _cheatsChecked, _abbreviated;

    private const float CacheInt = 150f;

    public static readonly string MainGitUrl = "https://raw.githubusercontent.com/GoldenIsAProtogen/GoldensGorillaNametags/main/", ModsGitUrl = "https://raw.githubusercontent.com/GoldenIsAProtogen/GoldensGorillaNametagsMC/main/Mods.txt";

    public Camera    CineCam;
    public Transform MainCam;

    public TMP_FontAsset Font => FontManager.GetNametagFont();

    public string FormatPrefix = "",
                  FormatSuffix = "";

    public readonly Regex ColorTagRegex = new("<color=[^>]+>|</color>", RegexOptions.Compiled);

    public readonly Dictionary<VRRig, int> playerPing = new();

    private GameObject componentHolder;
    private GUIManager guiManager;

    private float lastCacheTime, lastUpdateTime;

    private readonly HashSet<VRRig> _currentRigsBuffer = new();

    private static Shader _uberShaderCache;
    private static Shader UberShader => _uberShaderCache ??= Shader.Find("GorillaTag/UberShader");

    private bool tagsEnabled;

    public ConfigEntry<Key>    GuiButton;
    public ConfigEntry<string> IconLocation, FontName, GuiFontName;

    public ConfigEntry<Color> OutlineColor, GuiAccentColor;

    public ConfigEntry<bool> OutlineEnabled, CheckModLists, CheckMods, CheckCheats, AbbreviateMods, EstimateFPS, EstimatePing, CheckPlatform, CheckSpecial, CheckFps, CheckPing, CheckCosmetics, GorillaFriends, UsePlatIcons;

    public ConfigEntry<float> TagSize, TagHeight, UpdateInt, OutlineThickness, OutlineSoftness, IconSize, GuiCornerRadius, GuiOpacity;

    public ConfigEntry<TextCase>        TextCaseConfig;
    public ConfigEntry<TextFormatScope> TextFormatScopeConfig;
    public ConfigEntry<TextStyle>       TextStyleConfig;

    internal static Plugin Instance { get; set; }

    public Texture2D SteamTex   { get; set; }
    public Texture2D PcvrTex    { get; set; }
    public Texture2D QuestpcTex { get; set; }
    public Texture2D QuestTex   { get; set; }
    public Texture2D MaybeTex   { get; set; }

#endregion

#region ===== - Startup & Config - =====

    private void Start()
    {
        Instance   = this;
        guiManager = new GUIManager(this);
        GorillaTagger.OnPlayerSpawned(OnInit);
        CosmeticsV2Spawner_Dirty.OnPostInstantiateAllPrefabs += OnCosmeticsLoaded;
        InitConfig();
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
        TagSize               = Config.Bind("Tags", "Size",         2f,                     "Nametag size");
        TagHeight             = Config.Bind("Tags", "Height",       0.7f,                    "Nametag height");
        UpdateInt             = Config.Bind("Tags", "Update Int",   0.01f,                    "Tag update interval");
        TextStyleConfig       = Config.Bind("Tags", "Style",        TextStyle.Normal,         "Text style");
        TextCaseConfig        = Config.Bind("Tags", "Case",         TextCase.Normal,          "Text casing: Normal, Uppercase, Lowercase, Titlecase");
        TextFormatScopeConfig = Config.Bind("Tags", "Format Scope", TextFormatScope.NameOnly, "Whether formatting only changes Name or All text");

        FontName = Config.Bind("Tags", "Font Name", "DEFAULT",
                "The currently selected nametag font. \"DEFAULT\" uses the built-in Arial font");

        GuiFontName = Config.Bind("GUI", "Font Name", "DEFAULT",
                "The currently selected GUI font. \"DEFAULT\" uses Unity's built-in font");

        FontManager.Init(FontName, GuiFontName);

        OutlineEnabled   = Config.Bind("Outlines", "Enabled",   true, "Tag outlines");
        OutlineColor     = Config.Bind("Outlines", "Color",      Color.black, "Outline color");
        OutlineThickness = Config.Bind("Outlines", "Thickness", 0.4f, "Outline thickness");
        OutlineSoftness  = Config.Bind("Outlines", "Softness",  0.2f, "Outline softness (drop shadow)");

        CheckModLists  = Config.Bind("Checks", "Mod Lists", true, "Check mod lists");
        CheckSpecial   = Config.Bind("Checks", "Special",   true, "Check special players");
        CheckFps       = Config.Bind("Checks", "FPS",       true, "Check FPS");
        CheckPing      = Config.Bind("Checks", "Ping",      true, "Check Ping");
        CheckCosmetics = Config.Bind("Checks", "Cosmetics", true, "Check cosmetics");
        CheckPlatform  = Config.Bind("Checks", "Platform",  true, "Check platform");

        UsePlatIcons = Config.Bind("Platform", "UseIcons",      true,   "Show platform as icons instead of text");
        IconSize     = Config.Bind("Platform", "Icon Size",     0.07f,  "Size of the platform icons");
        IconLocation = Config.Bind("Platform", "Icon Location", "left", "Platform icon position\nAcceptable Values: left, right");

        CheckMods      = Config.Bind("Mods", "Mods",        true, "Show Mods");
        CheckCheats    = Config.Bind("Mods", "Cheats",      true, "Show Cheats");
        AbbreviateMods = Config.Bind("Mods", "Abbreviated", true, "Abbreviated");

        GorillaFriends = Config.Bind("Integrations", "GorillaFriends", false, "GorillaFriends name coloring (Requires GorillaFriends)");

        EstimateFPS  = Config.Bind("Miscellaneous", "Estimate FPS",  false, "Whether or not you estimate FPS");
        EstimatePing = Config.Bind("Miscellaneous", "Estimate Ping", false, "Whether or not you estimate ping");

        GuiButton       = Config.Bind("GUI", "Toggle Key",    Key.F4,                         "Key to toggle GUI");
        GuiCornerRadius = Config.Bind("GUI", "Corner Radius", 0f,                             "How rounded the GUI is");
        GuiOpacity      = Config.Bind("GUI", "Opacity",       1f,                             "GUI opacity");
        GuiAccentColor  = Config.Bind("GUI", "Accent Color",  new Color(0.93f, 0.91f, 0.67f), "Accent color");
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

#region ===== - Runtime - =====

    public void Update()
    {
        guiManager?.Update();

        if (_modListsChecked != CheckModLists.Value || _modsChecked   != CheckMods.Value   ||
            _cheatsChecked   != CheckCheats.Value   || _abbreviated != AbbreviateMods.Value)
        {
            if (TagUtils.Instance != null)
                TagUtils.Instance.RefreshCache();

            _modListsChecked = CheckModLists.Value;
            _modsChecked     = CheckMods.Value;
            _cheatsChecked   = CheckCheats.Value;
            _abbreviated     = AbbreviateMods.Value;
        }

        if (!tagsEnabled)
            return;

        if (Time.time - lastCacheTime >= CacheInt)
        {
            TagUtils.Instance.RefreshCache();
            lastCacheTime = Time.time;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            MainCam = mainCamera.transform;

        float currentTime = Time.time;

        if (!(currentTime - lastUpdateTime >= UpdateInt.Value))
            return;

        IReadOnlyList<VRRig> activeRigs = VRRigCache.m_activeRigs;

        if (activeRigs != null)
            foreach (VRRig rig in activeRigs)
            {
                if (rig == null || rig.isOfflineVRRig)
                    continue;

                Material mat = rig.mainSkin != null ? rig.mainSkin.material : null;

                if (mat == null)
                    continue;

                if (mat.shader == UberShader && mat.name.Contains("gorilla_body"))
                    mat.color = rig.playerColor;
            }

        _currentRigsBuffer.Clear();
        if (activeRigs != null)
            foreach (VRRig rig in activeRigs)
                _currentRigsBuffer.Add(rig);

        TagManager.Instance.CleanupTags(_currentRigsBuffer);
        TagManager.Instance.CreateTagmap(_currentRigsBuffer);
        TagManager.Instance.UpdateTags();
        lastUpdateTime = currentTime;
    }

    private void OnGUI() => guiManager?.OnGUI();

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
                       TextCase.Uppercase => text.ToUpperInvariant(),
                       TextCase.Lowercase => text.ToLowerInvariant(),
                       TextCase.Titlecase => ToTitleCase(text),
                       var _              => text,
               };

        if (FormatPrefix.Length == 0)
            return text;

        return FormatPrefix + text + FormatSuffix;
    }

    // Because it doesn't have this, or I'm stupid and can't find it
    private static string ToTitleCase(string text) => string.IsNullOrWhiteSpace(text) ? text : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant());

    private int GetTruePing(VRRig rig)
    {
        double ping = Math.Abs((rig.velocityHistoryList[0].time - PhotonNetwork.Time) * 1000);

        return (int)Math.Clamp(Math.Round(ping), 0, int.MaxValue);
    }

#endregion

#region ===== - Icons - =====

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
                ("steamicon.png", $"{MainGitUrl}Icons/steamicon.png"), ("pcvricon.png", $"{MainGitUrl}Icons/pcvricon.png"), ("questpcicon.png", $"{MainGitUrl}Icons/questpcicon.png"), ("questicon.png", $"{MainGitUrl}Icons/questicon.png"), ("maybeicon.png", $"{MainGitUrl}Icons/maybeicon.png"),
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
        MaybeTex   = LoadIconFromFile(Path.Combine(iconsDir, "maybeicon.png"));
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

            Texture2D tex = new(2, 2, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, };

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
}