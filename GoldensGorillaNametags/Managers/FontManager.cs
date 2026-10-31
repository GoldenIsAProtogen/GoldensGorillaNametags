using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace GoldensGorillaNametags.Managers;

internal static class FontManager
{
#region ===== - Fields & Init - =====

    private const string DefaultFontName = "DEFAULT";

    private static readonly Dictionary<string, TMP_FontAsset> LoadedFonts = new(StringComparer.OrdinalIgnoreCase);

    private static List<string> availableFonts;
    private static int          selectedIndex = -1;

    private static ConfigEntry<string> _fontName;

    private static string _fontsDirectory;

    internal static string FontsDirectory => _fontsDirectory ??= Path.Combine(Paths.ConfigPath, $"{Constants.Guid} Fonts");

    internal static void Init(ConfigEntry<string> fontName)
    {
        _fontName = fontName;

        EnsureDirectory();
        RefreshAvailableFonts();

        selectedIndex = availableFonts.FindIndex(f => string.Equals(f, _fontName.Value, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0)
            selectedIndex = 0;
    }

    private static bool IsReady => _fontName != null;

#endregion

#region ===== - Directory - =====

    internal static void EnsureDirectory()
    {
        try
        {
            if (!Directory.Exists(FontsDirectory))
                Directory.CreateDirectory(FontsDirectory);
        }
        catch
        {
            // ignored
        }
    }

    internal static void RefreshAvailableFonts()
    {
        availableFonts = new List<string> { DefaultFontName, };

        try
        {
            if (Directory.Exists(FontsDirectory))
                foreach (string path in Directory.EnumerateFiles(FontsDirectory))
                {
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext == ".ttf" || ext == ".otf")
                        availableFonts.Add(Path.GetFileName(path));
                }
        }
        catch
        {
            // ignored
        }

        if (selectedIndex < 0 || selectedIndex >= availableFonts.Count)
            selectedIndex = 0;
    }

    private static List<string> GetAvailableFontFiles()
    {
        if (availableFonts == null)
            RefreshAvailableFonts();

        return availableFonts;
    }

#endregion

#region ===== - Selection - =====

    internal static string GetSelectedFontName()
    {
        List<string> fonts = GetAvailableFontFiles();

        return fonts.Count == 0 ? DefaultFontName : fonts[selectedIndex];
    }

    internal static void NextFont()
    {
        List<string> fonts = GetAvailableFontFiles();

        if (fonts.Count == 0) return;

        selectedIndex = (selectedIndex + 1) % fonts.Count;
        if (IsReady) _fontName.Value = GetSelectedFontName();
    }

    internal static void PreviousFont()
    {
        List<string> fonts = GetAvailableFontFiles();

        if (fonts.Count == 0) return;

        selectedIndex = (selectedIndex - 1 + fonts.Count) % fonts.Count;
        if (IsReady) _fontName.Value = GetSelectedFontName();
    }

    internal static void SelectFontByName(string fontName)
    {
        if (string.IsNullOrEmpty(fontName))
            return;

        List<string> fonts = GetAvailableFontFiles();
        int          index = fonts.FindIndex(f => string.Equals(f, fontName, StringComparison.OrdinalIgnoreCase));

        selectedIndex = index >= 0 ? index : 0;
        if (IsReady) _fontName.Value = GetSelectedFontName();
    }

    internal static List<string> GetAvailableFonts() => availableFonts ?? new List<string> { DefaultFontName, };

#endregion

#region ===== - Loading - =====

    internal static TMP_FontAsset GetSelectedFont()
    {
        string name = GetSelectedFontName();

        return name == DefaultFontName ? FallbackFont() : LoadFont(name);
    }

    internal static TMP_FontAsset LoadFont(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || fileName == DefaultFontName)
            return FallbackFont();

        if (LoadedFonts.TryGetValue(fileName, out TMP_FontAsset cached) && cached != null)
            return cached;

        string fullPath = Path.Combine(FontsDirectory, fileName);

        if (!File.Exists(fullPath))
            return FallbackFont();

        try
        {
            Font          unityFont = new(fullPath);
            TMP_FontAsset asset     = TMP_FontAsset.CreateFontAsset(unityFont, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
            asset.material.shader = Shader.Find("TextMeshPro/Distance Field");

            LoadedFonts[fileName] = asset;

            return asset;
        }
        catch
        {
            return FallbackFont();
        }
    }

    private static TMP_FontAsset FallbackFont()
    {
        const string cacheKey = "__fallback";

        if (LoadedFonts.TryGetValue(cacheKey, out TMP_FontAsset cached) && cached != null)
            return cached;

        TMP_FontAsset asset = Resources.Load<TMP_FontAsset>("Fonts & Materials/Arial SDF");
        LoadedFonts[cacheKey] = asset;

        return asset;
    }

#endregion
}