using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace GoldensGorillaNametags.Managers;

internal static class FontManager
{
#region ===== - Fields & Init - =====

    private const string DefaultFontName  = "DEFAULT",
                         SharedFolderName = "Goldens Mods Fonts";

    private static readonly Dictionary<string, TMP_FontAsset> TmpFonts   = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Font>          UnityFonts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConfigEntry<string>[]             Entries    = new ConfigEntry<string>[2];

    private static List<string> availableFonts;
    private static string       fontsDirectory;

    internal static string FontsDirectory => fontsDirectory ??= Path.Combine(Paths.ConfigPath, SharedFolderName);

    internal enum FontTarget
    {
        Nametag,
        Gui,
    }

    internal static void Init(ConfigEntry<string> nametagFont, ConfigEntry<string> guiFont)
    {
        Entries[(int)FontTarget.Nametag] = nametagFont;
        Entries[(int)FontTarget.Gui]     = guiFont;

        EnsureDirectory();
        RefreshAvailableFonts();
    }

#endregion

#region ===== - Directory - =====

    private static List<string> Fonts
    {
        get
        {
            if (availableFonts == null)
                RefreshAvailableFonts();

            return availableFonts;
        }
    }

    internal static void EnsureDirectory()
    {
        try
        {
            Directory.CreateDirectory(FontsDirectory);
        }
        catch
        {
            // ignored
        }
    }

    internal static void RefreshAvailableFonts()
    {
        availableFonts = [DefaultFontName,];

        try
        {
            availableFonts.AddRange(Directory.EnumerateFiles(FontsDirectory)
                                             .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".ttf" or ".otf")
                                             .Select(path => Path.GetFileName(path)));
        }
        catch
        {
            // ignored
        }

        foreach (FontTarget target in Enum.GetValues(typeof(FontTarget)))
            Set(target, GetSelectedFontName(target));
    }

#endregion

#region ===== - Selection - =====

    private static string Resolve(string name) =>
            Fonts.Find(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase)) ?? DefaultFontName;

    private static void Set(FontTarget target, string name)
    {
        ConfigEntry<string> entry = Entries[(int)target];

        if (entry != null)
            entry.Value = name;
    }

    internal static string GetSelectedFontName(FontTarget target) => Resolve(Entries[(int)target]?.Value);

    internal static string GetSelectedFontDisplayName(FontTarget target)
    {
        string name = GetSelectedFontName(target);

        return name == DefaultFontName ? name : Path.GetFileNameWithoutExtension(name);
    }

    internal static void NextFont(FontTarget target) => StepFont(target, 1);

    internal static void PreviousFont(FontTarget target) => StepFont(target, -1);

    internal static void MatchFont(FontTarget target, string fontName) => Set(target, Resolve(fontName));

    private static void StepFont(FontTarget target, int direction)
    {
        List<string> fonts = Fonts;
        int          index = fonts.IndexOf(GetSelectedFontName(target));

        Set(target, fonts[(index + direction + fonts.Count) % fonts.Count]);
    }

#endregion

#region ===== - Loading - =====

    internal static TMP_FontAsset GetNametagFont()
    {
        string name = GetSelectedFontName(FontTarget.Nametag);

        return name == DefaultFontName ? FallbackFont() : LoadTmpFont(name);
    }

    internal static Font GetGuiFont(string name) =>
            string.IsNullOrEmpty(name) || name == DefaultFontName ? null : LoadUnityFont(name);

    private static Font LoadUnityFont(string fileName)
    {
        if (UnityFonts.TryGetValue(fileName, out Font cached) && cached)
            return cached;

        string fullPath = Path.Combine(FontsDirectory, fileName);

        if (!File.Exists(fullPath))
            return null;

        try
        {
            return UnityFonts[fileName] = new Font(fullPath);
        }
        catch
        {
            return null;
        }
    }

    private static TMP_FontAsset LoadTmpFont(string fileName)
    {
        if (TmpFonts.TryGetValue(fileName, out TMP_FontAsset cached) && cached)
            return cached;

        Font unityFont = LoadUnityFont(fileName);

        if (!unityFont)
            return FallbackFont();

        try
        {
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(unityFont, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
            asset.material.shader = Shader.Find("TextMeshPro/Distance Field");

            return TmpFonts[fileName] = asset;
        }
        catch
        {
            return FallbackFont();
        }
    }

    private static TMP_FontAsset FallbackFont() => Resources.Load<TMP_FontAsset>("Fonts & Materials/Arial SDF");

#endregion
}