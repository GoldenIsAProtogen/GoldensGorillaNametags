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

    private const string DefaultFontName  = "DEFAULT",
                         SharedFolderName = "Goldens Mods Fonts";

    private static readonly Dictionary<string, TMP_FontAsset> LoadedTmpFonts   = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Font>          LoadedUnityFonts = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConfigEntry<string>[] _fontEntries   = new ConfigEntry<string>[2];
    private static readonly int[]                 _selectedIndex = [-1, -1,];

    private static List<string> availableFonts;
    private static string       _fontsDirectory;

    internal static string FontsDirectory => _fontsDirectory ??= Path.Combine(Paths.ConfigPath, SharedFolderName);

    internal enum FontTarget
    {
        Nametag,
        Gui,
    }

    internal static void Init(ConfigEntry<string> nametagFont, ConfigEntry<string> guiFont)
    {
        _fontEntries[(int)FontTarget.Nametag] = nametagFont;
        _fontEntries[(int)FontTarget.Gui]     = guiFont;

        EnsureDirectory();
        RefreshAvailableFonts();
    }

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

    private static bool IsFontFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();

        return ext == ".ttf" || ext == ".otf";
    }

    internal static void RefreshAvailableFonts()
    {
        string[] wanted = new string[_selectedIndex.Length];

        for (int i = 0; i < wanted.Length; i++)
            wanted[i] = _fontEntries[i]?.Value ?? DefaultFontName;

        availableFonts = [DefaultFontName,];

        try
        {
            if (Directory.Exists(FontsDirectory))
                foreach (string path in Directory.EnumerateFiles(FontsDirectory))
                    if (IsFontFile(path))
                        availableFonts.Add(Path.GetFileName(path));
        }
        catch
        {
            // ignored
        }

        for (int i = 0; i < wanted.Length; i++)
        {
            string name  = wanted[i];
            int    index = availableFonts.FindIndex(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));

            _selectedIndex[i] = index < 0 ? 0 : index;

            if (index < 0)
                SaveSelection((FontTarget)i);
        }
    }

    private static List<string> GetAvailableFonts()
    {
        if (availableFonts == null)
            RefreshAvailableFonts();

        return availableFonts;
    }

#endregion

#region ===== - Selection - =====

    internal static string GetSelectedFontName(FontTarget target)
    {
        List<string> fonts = GetAvailableFonts();
        int          index = _selectedIndex[(int)target];

        return index >= 0 && index < fonts.Count ? fonts[index] : DefaultFontName;
    }

    internal static string GetSelectedFontDisplayName(FontTarget target)
    {
        string name = GetSelectedFontName(target);

        return name == DefaultFontName ? name : Path.GetFileNameWithoutExtension(name);
    }

    internal static void NextFont(FontTarget target) => StepFont(target, 1);

    internal static void PreviousFont(FontTarget target) => StepFont(target, -1);

    internal static void MatchFont(FontTarget target, string fontName)
    {
        List<string> fonts = GetAvailableFonts();
        int          index = fonts.FindIndex(f => string.Equals(f, fontName, StringComparison.OrdinalIgnoreCase));

        _selectedIndex[(int)target] = index >= 0 ? index : 0;
        SaveSelection(target);
    }

    private static void StepFont(FontTarget target, int direction)
    {
        int count = GetAvailableFonts().Count;

        _selectedIndex[(int)target] = ((_selectedIndex[(int)target] + direction) % count + count) % count;
        SaveSelection(target);
    }

    private static void SaveSelection(FontTarget target)
    {
        ConfigEntry<string> entry = _fontEntries[(int)target];

        if (entry != null)
            entry.Value = GetSelectedFontName(target);
    }

#endregion

#region ===== - Loading - =====

    internal static TMP_FontAsset GetNametagFont()
    {
        string name = GetSelectedFontName(FontTarget.Nametag);

        return name == DefaultFontName ? FallbackFont() : LoadTmpFont(name);
    }

    internal static Font GetGuiFont(string name) => string.IsNullOrEmpty(name) || name == DefaultFontName ? null : LoadUnityFont(name);

    private static Font LoadUnityFont(string fileName)
    {
        if (LoadedUnityFonts.TryGetValue(fileName, out Font cached) && cached != null)
            return cached;

        string fullPath = Path.Combine(FontsDirectory, fileName);

        if (!File.Exists(fullPath))
            return null;

        try
        {
            Font font = new(fullPath);
            LoadedUnityFonts[fileName] = font;

            return font;
        }
        catch
        {
            return null;
        }
    }

    private static TMP_FontAsset LoadTmpFont(string fileName)
    {
        if (LoadedTmpFonts.TryGetValue(fileName, out TMP_FontAsset cached) && cached != null)
            return cached;

        Font unityFont = LoadUnityFont(fileName);

        if (unityFont == null)
            return FallbackFont();

        try
        {
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(unityFont, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
            asset.material.shader = Shader.Find("TextMeshPro/Distance Field");

            LoadedTmpFonts[fileName] = asset;

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

        if (LoadedTmpFonts.TryGetValue(cacheKey, out TMP_FontAsset cached) && cached != null)
            return cached;

        TMP_FontAsset asset = Resources.Load<TMP_FontAsset>("Fonts & Materials/Arial SDF");
        LoadedTmpFonts[cacheKey] = asset;

        return asset;
    }

#endregion
}