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
    private const float CacheInt = 150f;

    public static readonly string Giturl =
            "https://raw.githubusercontent.com/GoldenIsAProtogen/GoldensGorillaNametags/main/";

    [FormerlySerializedAs("cineCam")] public Camera CineCam;

    [FormerlySerializedAs("font")]    public TMP_FontAsset Font;
    [FormerlySerializedAs("mainCam")] public Transform     MainCam;

    public readonly Regex ClrTagRegex = new(@"<color=[^>]+>|</color>", RegexOptions.Compiled);

    public readonly Dictionary<VRRig, int> playerPing = new();
    private         GameObject             componentHolder;
    public          ConfigEntry<string>    IconLocation;

    private float lastCacheT, lastUpdT;

    public ConfigEntry<Color> OutlineClr;

    public ConfigEntry<bool> OutlineEnabled,
                             OutlineQual,
                             CheckPlat,
                             CheckSpecial,
                             CheckFps,
                             CheckPing,
                             CheckCosmetics,
                             Gf,
                             TxtQual,
                             UsePlatIcons,
                             PlatIconClr;

    public ConfigEntry<float> TagSize, TagHeight, UpdInt, OutlineThick, IconSize;

    public static Plugin Instance { get; private set; }

    private void Start()
    {
        Instance = this;
        GorillaTagger.OnPlayerSpawned(OnPlayerSpawned);
        InitCfg();
        InitFont();
        InitCam();
        InitHarmony();
        TagUtils.Instance.InitPlatIcons();
        TagUtils.Instance.RefreshCache();
    }

    public void Update()
    {
        if (Time.time - lastCacheT >= CacheInt)
        {
            TagUtils.Instance.RefreshCache();
            lastCacheT = Time.time;
        }

        if (MainCam == null || Camera.main != null)
            MainCam = Camera.main?.transform;

        float currentTime = Time.time;

        if (!(currentTime - lastUpdT >= UpdInt.Value))
            return;

        foreach (VRRig r in GorillaParent.instance.vrrigs
                                         .Where(r => r != null && !r.isOfflineVRRig && r.mainSkin?.material != null)
                                         .Where(r => r.mainSkin.material.name.Contains("gorilla_body") &&
                                                     r.mainSkin.material.shader ==
                                                     Shader.Find("GorillaTag/UberShader")))
            r.mainSkin.material.color = r.playerColor;

        HashSet<VRRig> currentRigs = new(GorillaParent.instance.vrrigs ?? new List<VRRig>());
        TagManager.Instance.CleanupTags(currentRigs);
        TagManager.Instance.CreateTags(currentRigs);
        TagManager.Instance.UpdateTags();
        lastUpdT = currentTime;
    }

    private void OnPlayerSpawned()
    {
        componentHolder = new GameObject("GoldensGorillaNametags Component Holder");
        componentHolder.AddComponent<TagUtils>();
        componentHolder.AddComponent<TagManager>();

        PlayerSerializePatch.OnPlayerSerialize += rig => { playerPing[rig] = GetTruePing(rig); };
    }

    private void InitCfg()
    {
        TagSize   = Config.Bind("Tags", "Size",       1f,    "Nametag size");
        TagHeight = Config.Bind("Tags", "Height",     0.65f, "Nametag height");
        UpdInt    = Config.Bind("Tags", "Update Int", 0.01f, "Tag update interval");
        TxtQual   = Config.Bind("Tags", "Quality",    false, "Nametag quality");

        OutlineEnabled = Config.Bind("Outlines", "Enabled",   true,        "Tag outlines");
        OutlineQual    = Config.Bind("Outlines", "Quality",   false,       "Outline quality");
        OutlineClr     = Config.Bind("Outlines", "Color",     Color.black, "Outline color");
        OutlineThick   = Config.Bind("Outlines", "Thickness", 0.0025f,     "Outline thickness");
        
        CheckSpecial   = Config.Bind("Checks", "Special",   true,  "Check special players");
        CheckFps       = Config.Bind("Checks", "FPS",       true,  "Check FPS");
        CheckPing      = Config.Bind("Checks", "Ping",      false, "Check Ping (Ping estimation, not 100% accurate)");
        CheckCosmetics = Config.Bind("Checks", "Cosmetics", true,  "Check cosmetics");
        CheckPlat      = Config.Bind("Checks", "Platform",  true,  "Check platform");

        UsePlatIcons = Config.Bind("Platform", "UseIcons",  true,   "Show platform as icons instead of text");
        IconSize     = Config.Bind("Platform", "Icon Size", 0.015f, "Size of the platform icons");
        PlatIconClr  = Config.Bind("Platform", "Icon Colored", true,
                "If the icons platform icons are colored or not");

        IconLocation = Config.Bind("Platform", "Icon Location", "left",
                "Platform icon position\nAcceptable Values: top, bottom, left, right");

        Gf = Config.Bind("Miscellaneous", "GFriends", false, "Use GFriends");
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
                                   .FirstOrDefault(path =>
                                                           path.EndsWith(".ttf",
                                                                   StringComparison.OrdinalIgnoreCase) ||
                                                           path.EndsWith(".otf",
                                                                   StringComparison.OrdinalIgnoreCase));

        try
        {
            if (fontPath != null)
            {
                Font unityFont = new(fontPath);
                Font = TxtQual.Value
                               ? TMP_FontAsset.CreateFontAsset(unityFont, 90, 9, GlyphRenderMode.SDFAA, 4096, 4096)
                               : TMP_FontAsset.CreateFontAsset(unityFont);

                Font.material.shader = Shader.Find("TextMeshPro/Mobile/Distance Field");
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
}