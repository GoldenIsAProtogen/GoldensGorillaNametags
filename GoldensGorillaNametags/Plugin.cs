using System;
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
using UnityEngine.Serialization;
using UnityEngine.TextCore.LowLevel;

namespace GoldensGorillaNametags;

[BepInPlugin(Constants.Guid, Constants.Name, Constants.Version)]
public class Plugin : BaseUnityPlugin
{
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

    public static readonly string MainGitUrl = "https://raw.githubusercontent.com/GoldenIsAProtogen/GoldensGorillaNametags/main/";

    [FormerlySerializedAs("cineCam")] public Camera CineCam;
    [FormerlySerializedAs("font")]    public TMP_FontAsset Font;
    [FormerlySerializedAs("mainCam")] public Transform     MainCam;

    public string FormatPrefix = "";
    public string FormatSuffix = "";

    public readonly Regex ClrTagRegex = new(@"<color=[^>]+>|</color>", RegexOptions.Compiled);

    public readonly Dictionary<VRRig, int> playerPing = new();
    private         GameObject             componentHolder;
    private         bool                   tagsEnabled;

    private float lastCacheTime,
                  lastUpdateTime;
                          
    public ConfigEntry<string> IconLocation;

    public ConfigEntry<Color> OutlineColor;

    public ConfigEntry<bool> OutlineEnabled,
                             OutlineQuality,
                             CheckPlatform,
                             CheckSpecial,
                             CheckFps,
                             CheckPing,
                             CheckCosmetics,
                             Gfriends,
                             TextQuality,
                             UsePlatIcons;

    public ConfigEntry<float> TagSize, 
                              TagHeight, 
                              UpdateInt, 
                              OutlineThickness, 
                              IconSize;
    
    public ConfigEntry<TextCase>        TextCaseConfig;
    public ConfigEntry<TextFormatScope> TextFormatScopeConfig;
    public ConfigEntry<TextStyle>       TextStyleConfig;

    public static Plugin Instance { get; private set; }

    private void Start()
    {
        Instance = this;
        GorillaTagger.OnPlayerSpawned(OnInit);
        InitConfig();
        InitFont();
        InitCam();
        InitHarmony();
        Formatting();
        TagUtils.Instance.DownloadPlatformIcons();
        TagUtils.Instance.RefreshCache();
    }

    public void Update()
    {
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

        foreach (VRRig r in VRRigCache.ActiveRigs
                                         .Where(r => r != null && !r.isOfflineVRRig && r.mainSkin?.material != null)
                                         .Where(r => r.mainSkin.material.name.Contains("gorilla_body") &&
                                                     r.mainSkin.material.shader ==
                                                     Shader.Find("GorillaTag/UberShader")))
            r.mainSkin.material.color = r.playerColor;

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

    private void OnInit()
    {
        componentHolder = new GameObject("GoldensGorillaNametags Component Holder");
        componentHolder.AddComponent<TagUtils>();
        componentHolder.AddComponent<TagManager>();

        PlayerSerializePatch.OnPlayerSerialize += rig => { playerPing[rig] = GetTruePing(rig); };
    }

    private void InitConfig()
    {
        TagSize               = Config.Bind("Tags", "Size", 2.5f, "Nametag size");
        TagHeight             = Config.Bind("Tags", "Height", 0.85f, "Nametag height");
        UpdateInt             = Config.Bind("Tags", "Update Int", 0.01f, "Tag update interval");
        TextQuality           = Config.Bind("Tags", "Quality", false, "Nametag quality");
        TextStyleConfig       = Config.Bind("Tags", "Style", TextStyle.Normal, "Text style");
        TextCaseConfig        = Config.Bind("Tags", "Case", TextCase.Normal, "Text casing: Normal, Uppercase, Lowercase");
        TextFormatScopeConfig = Config.Bind("Tags", "Format Scope", TextFormatScope.NameOnly, "Choose whether formatting applies only to player names or to all text.");

        OutlineEnabled   = Config.Bind("Outlines", "Enabled",   true,        "Tag outlines");
        OutlineQuality   = Config.Bind("Outlines", "Quality",   false,       "Outline quality");
        OutlineColor     = Config.Bind("Outlines", "Color",     Color.black, "Outline color");
        OutlineThickness = Config.Bind("Outlines", "Thickness", 0.4f,        "Outline thickness");
        
        CheckSpecial   = Config.Bind("Checks", "Special",   true,  "Check special players");
        CheckFps       = Config.Bind("Checks", "FPS",       true,  "Check FPS");
        CheckPing      = Config.Bind("Checks", "Ping",      false, "Check Ping (Ping estimation, not 100% accurate)");
        CheckCosmetics = Config.Bind("Checks", "Cosmetics", true,  "Check cosmetics");
        CheckPlatform  = Config.Bind("Checks", "Platform",  true,  "Check platform");

        UsePlatIcons = Config.Bind("Platform", "UseIcons",     false,   "Show platform as icons instead of text"); // Currently Doesn't work, plz no use
        IconSize     = Config.Bind("Platform", "Icon Size",    0.010f, "Size of the platform icons");
        IconLocation = Config.Bind("Platform", "Icon Location", "left", "Platform icon position\nAcceptable Values: left, right");

        Gfriends = Config.Bind("Integrations", "GFriends", false, "Use GFriends");
    }

    private int GetTruePing(VRRig rig)
    {
        double ping     = Math.Abs((rig.velocityHistoryList[0].time - PhotonNetwork.Time) * 1000);
        int    safePing = (int)Math.Clamp(Math.Round(ping), 0, int.MaxValue);

        return safePing;
    }

    private void InitFont()
    {
        string fontDir = Path.Combine(Paths.BepInExRootPath, "Fonts");
        if (!Directory.Exists(fontDir))
            Directory.CreateDirectory(fontDir);

        string fontPath = Directory.EnumerateFiles(fontDir, "*.*")
                                   .FirstOrDefault(path => path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));

        try
        {
            if (fontPath != null)
            {
                Font unityFont = new(fontPath);
                Font = TextQuality.Value ? TMP_FontAsset.CreateFontAsset(unityFont, 120, 12, GlyphRenderMode.SDFAA, 4096, 4096) : TMP_FontAsset.CreateFontAsset(unityFont);

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
                       _                  => text,
               };

        if (FormatPrefix.Length == 0)
            return text;

        return FormatPrefix + text + FormatSuffix;
    }
}