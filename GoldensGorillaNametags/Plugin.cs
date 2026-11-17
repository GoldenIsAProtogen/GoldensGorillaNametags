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

    private const float CacheInt = 150f;

    public static readonly string MainGitUrl = "https://raw.githubusercontent.com/GoldenIsAProtogen/GoldensGorillaNametags/main/",
                                  ModsGitUrl = "https://raw.githubusercontent.com/GoldenIsAProtogen/GoldensGorillaNametagsMC/main/Mods.txt";

    public Camera    CineCam;
    public Transform MainCam;

    public TMP_FontAsset Font => FontManager.GetNametagFont();

    public string FormatPrefix = "",
                  FormatSuffix = "";

    public readonly Regex ColorTagRegex = new("<color=[^>]+>|</color>", RegexOptions.Compiled);

    public readonly Dictionary<VRRig, int> playerPing = new();

    private GUIManager guiManager;

    private float lastCacheTime, lastUpdateTime;

    private bool tagsEnabled;

    private (bool, bool, bool, bool) _lastChecks;

    private readonly HashSet<VRRig> _currentRigsBuffer = new();

    private static Shader _uberShaderCache;
    private static Shader UberShader => _uberShaderCache ??= Shader.Find("GorillaTag/UberShader");

    public ConfigEntry<Key>    GuiButton;
    public ConfigEntry<string> IconLocation, FontName, GuiFontName;

    public ConfigEntry<Color> OutlineColor, GuiAccentColor;

    public ConfigEntry<bool> OutlineEnabled, CheckModLists, CheckMods, CheckCheats, AbbreviateMods, EstimateFPS, EstimatePing,
                             CheckPlatform, CheckSpecial, CheckFps, CheckPing, CheckCosmetics, GorillaFriends, UsePlatIcons;

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
        CineCam = FindFirstObjectByType<CinemachineBrain>()?.GetComponent<Camera>();
        new Harmony(Constants.Guid).PatchAll(Assembly.GetExecutingAssembly());
        Formatting();
    }

    private void OnInit()
    {
        GameObject holder = new("GoldensGorillaNametags Component Holder");
        holder.AddComponent<TagUtils>();
        holder.AddComponent<TagManager>();

        PlayerSerializePatch.OnPlayerSerialize += rig => playerPing[rig] = GetTruePing(rig);
    }

    private void InitConfig()
    {
        TagSize               = Config.Bind("Tags", "Size",         2f,                       "Nametag size");
        TagHeight             = Config.Bind("Tags", "Height",       0.7f,                     "Nametag height");
        UpdateInt             = Config.Bind("Tags", "Update Int",   0.01f,                    "Tag update interval");
        TextStyleConfig       = Config.Bind("Tags", "Style",        TextStyle.Normal,         "Text style");
        TextCaseConfig        = Config.Bind("Tags", "Case",         TextCase.Normal,          "Text casing: Normal, Uppercase, Lowercase, Titlecase");
        TextFormatScopeConfig = Config.Bind("Tags", "Format Scope", TextFormatScope.NameOnly, "Whether formatting only changes Name or All text");

        FontName = Config.Bind("Tags", "Font Name", "DEFAULT",
                "The currently selected nametag font. \"DEFAULT\" uses the built-in Arial font");

        GuiFontName = Config.Bind("GUI", "Font Name", "DEFAULT",
                "The currently selected GUI font. \"DEFAULT\" uses Unity's built-in font");

        FontManager.Init(FontName, GuiFontName);

        OutlineEnabled   = Config.Bind("Outlines", "Enabled",   true,        "Tag outlines");
        OutlineColor     = Config.Bind("Outlines", "Color",     Color.black, "Outline color");
        OutlineThickness = Config.Bind("Outlines", "Thickness", 0.4f,        "Outline thickness");
        OutlineSoftness  = Config.Bind("Outlines", "Softness",  0.2f,        "Outline softness (drop shadow)");

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

#endregion

#region ===== - Runtime - =====

    public void Update()
    {
        guiManager?.Update();

        (bool, bool, bool, bool) checks = (CheckModLists.Value, CheckMods.Value, CheckCheats.Value, AbbreviateMods.Value);

        if (checks != _lastChecks)
        {
            _lastChecks = checks;
            RefreshCache();
        }

        if (!tagsEnabled)
            return;

        float currentTime = Time.time;

        if (currentTime - lastCacheTime >= CacheInt)
        {
            RefreshCache();
            lastCacheTime = currentTime;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
            MainCam = mainCamera.transform;

        if (currentTime - lastUpdateTime < UpdateInt.Value)
            return;

        _currentRigsBuffer.Clear();

        if (VRRigCache.m_activeRigs is { } activeRigs)
            foreach (VRRig rig in activeRigs)
            {
                _currentRigsBuffer.Add(rig);

                if (rig == null || rig.isOfflineVRRig || rig.mainSkin == null)
                    continue;

                Material mat = rig.mainSkin.material;

                if (mat != null && mat.shader == UberShader && mat.name.Contains("gorilla_body"))
                    mat.color = rig.playerColor;
            }

        TagManager.Instance.CleanupTags(_currentRigsBuffer);
        TagManager.Instance.CreateTagmap(_currentRigsBuffer);
        TagManager.Instance.UpdateTags();
        lastUpdateTime = currentTime;
    }

    private void OnGUI() => guiManager?.OnGUI();

    private void OnEnable()  => SetTagsEnabled(true);
    private void OnDisable() => SetTagsEnabled(false);

    private void SetTagsEnabled(bool value)
    {
        tagsEnabled = value;

        if (TagManager.Instance != null)
            TagManager.Instance.ClearTags();
    }

    private static void RefreshCache()
    {
        if (TagUtils.Instance != null)
            TagUtils.Instance.RefreshCache();
    }

    public void Formatting()
    {
        TextStyle style = TextStyleConfig.Value;

        bool bold      = style.HasFlag(TextStyle.Bold);
        bool italic    = style.HasFlag(TextStyle.Italic);
        bool underline = style.HasFlag(TextStyle.Underline);

        FormatPrefix = (bold ? "<b>" : "") + (italic ? "<i>" : "") + (underline ? "<u>" : "");
        FormatSuffix = (underline ? "</u>" : "") + (italic ? "</i>" : "") + (bold ? "</b>" : "");
    }

    public string TextFormat(string text)
    {
        text = TextCaseConfig.Value switch
               {
                       TextCase.Uppercase => text.ToUpperInvariant(),
                       TextCase.Lowercase => text.ToLowerInvariant(),
                       TextCase.Titlecase => ToTitleCase(text),
                       _                  => text,
               };

        return FormatPrefix + text + FormatSuffix;
    }

    private static string ToTitleCase(string text) => string.IsNullOrWhiteSpace(text) ? text : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant());

    private static int GetTruePing(VRRig rig) => (int)Math.Min(Math.Round(Math.Abs((rig.velocityHistoryList[0].time - PhotonNetwork.Time) * 1000)), int.MaxValue);

#endregion

#region ===== - Icons - =====

    private void OnCosmeticsLoaded() => StartCoroutine(DownloadAndCacheIcons());

    private IEnumerator DownloadAndCacheIcons()
    {
        string iconsDir = Path.Combine(Path.GetDirectoryName(Info.Location), "Icons");
        Directory.CreateDirectory(iconsDir);

        foreach (string fileName in new[] { "steamicon.png", "pcvricon.png", "questpcicon.png", "questicon.png", "maybeicon.png", })
        {
            string filePath = Path.Combine(iconsDir, fileName);

            if (!File.Exists(filePath))
            {
                using UnityWebRequest request = UnityWebRequest.Get($"{MainGitUrl}Icons/{fileName}");
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

        SteamTex   = LoadIconFromFile(Path.Combine(iconsDir, "steamicon.png"));
        PcvrTex    = LoadIconFromFile(Path.Combine(iconsDir, "pcvricon.png"));
        QuestpcTex = LoadIconFromFile(Path.Combine(iconsDir, "questpcicon.png"));
        QuestTex   = LoadIconFromFile(Path.Combine(iconsDir, "questicon.png"));
        MaybeTex   = LoadIconFromFile(Path.Combine(iconsDir, "maybeicon.png"));

        RefreshCache();
    }

    private static Texture2D LoadIconFromFile(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);

            Texture2D tex = new(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, };

            if (tex.LoadImage(data))
                return tex;

            Destroy(tex);
        }
        catch
        {
            // ignored
        }

        return null;
    }

#endregion
}