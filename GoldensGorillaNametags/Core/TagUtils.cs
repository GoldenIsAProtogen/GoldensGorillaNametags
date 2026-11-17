using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ExitGames.Client.Photon;
using Photon.Pun;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace GoldensGorillaNametags.Core;

public class TagUtils : MonoBehaviour
{
#region ===== - Fields & Init - =====

    public static TagUtils Instance;

    private static readonly (int Min, string Color)[] FpsColors =
    {
            (250, "#800080"), (200, "#1e90ff"), (150, "#006400"),
            (100, "#00ff00"), (75, "#adff2f"), (55, "#ffff00"),
            (45, "#ffa500"), (30, "#ff0000"), (29, "#8b0000"),
    };

    private static readonly (int Min, string Color)[] PingColors =
    {
            (250, "#8b0000"), (200, "#ff0000"), (150, "#ffa500"),
            (120, "#ffff00"), (90, "#adff2f"), (75, "#00ff00"),
            (55, "#006400"), (35, "#1e90ff"), (25, "#800080"),
    };

    private static readonly Dictionary<string, string> PlatColors = new()
    {
            { "STEAM", "#ffff00" }, { "QUESTPC", "#ffaa00" },
            { "PC", "#ffcc00" }, { "PCVR", "#ff0000" },
            { "QUEST", "#00ff00" }, { "UNKNOWN", "#808080" },
    };

    private static readonly Dictionary<string, string> SpecialCosmetics = new()
    {
            { "LBANI.", "[<color=#fcc200>AAC</color>]" }, { "LBADE.", "[<color=#fcc200>FP</color>]" }, //Another Axiom Creator Badge, Finger Painter Badge
            { "LBAGS.", "[<color=#fcc200>ILL</color>]" }, { "LBAAK.", "[<color=#ff0000>S</color>]" },  //Illustrator Badge, Stick
            { "LMAPY.", "[<color=#c80000>FS</color>]" }, { "LBAAD.", "[<color=#960000>A</color>]" },   //Fire Stick, Admin Badge
    };

    private static readonly DateTime                     AddedSteamPaymentDate  = new(2023, 02, 06);
    private static readonly Dictionary<string, DateTime> PlayerCreationDateDict = new();

    private readonly        Dictionary<NametagData, bool> _platformFallbackState = new();
    
    private static readonly Regex ControlCharRegex    = new(@"[\x00-\x1F\x7F]", RegexOptions.Compiled);
    private static readonly Regex EscapeSequenceRegex = new(@"\\[nrtvbfa\\]+", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex     = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex ModVersionRegex     = new(@"v?\s*([0-9]+(\.[0-9]+){1,3})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private Dictionary<string, string> specialCache, modsCache;

    private void Awake() => Instance = this;

#endregion

#region ===== - Icons - =====

    public IEnumerator UpdatePlatformIconCoroutine(VRRig rig, NametagData data)
    {
        while (Plugin.Instance.SteamTex   == null || Plugin.Instance.PcvrTex  == null ||
               Plugin.Instance.QuestpcTex == null || Plugin.Instance.QuestTex == null)
            yield return null;

        yield return new WaitForSeconds(2f);

        while (rig != null && data?.PlatformIconRenderer != null)
        {
            if (Plugin.Instance.UsePlatIcons.Value && Plugin.Instance.CheckPlatform.Value)
            {
                UpdatePlatformIconTex(rig, data);
            }
            else
            {
                data.CurrentPlatformTex          = null;
                data.PlatformIconRenderer.sprite = null;
                data.PlatformIconRenderer.gameObject.SetActive(false);

                UpdateMaybeIcon(data, false);
            }

            yield return new WaitForSeconds(10f);
        }
    }

    private void UpdatePlatformIconTex(VRRig rig, NametagData data)
    {
        Texture2D newTex = PlatformTex(rig, out bool isFallback);

        if (newTex == data.CurrentPlatformTex && _platformFallbackState.TryGetValue(data, out bool prev) && prev == isFallback)
            return;

        data.CurrentPlatformTex      = newTex;
        _platformFallbackState[data] = isFallback;

        data.PlatformIconRenderer.sprite = newTex != null ? MakeSprite(newTex) : null;
        data.PlatformIconRenderer.gameObject.SetActive(newTex != null);

        UpdateMaybeIcon(data, newTex != null && isFallback);
    }

    private static Sprite MakeSprite(Texture2D tex) => Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);

    private void UpdateMaybeIcon(NametagData data, bool show)
    {
        if (data?.PlatformIconRenderer == null)
            return;

        Transform parent = data.PlatformIconRenderer.transform;
        Transform maybe  = parent.Find("MaybeIcon");

        if (!show)
        {
            if (maybe != null)
                maybe.gameObject.SetActive(false);

            return;
        }

        Texture2D tex = Plugin.Instance.MaybeTex;

        if (tex == null)
            return;

        if (maybe == null)
        {
            GameObject go = new("MaybeIcon");
            go.transform.SetParent(parent, false);
            go.AddComponent<SpriteRenderer>().sortingOrder = data.PlatformIconRenderer.sortingOrder + 1;
            maybe = go.transform;
        }

        maybe.localPosition = new Vector3(2.5f, 0f, 0f);

        SpriteRenderer renderer = maybe.GetComponent<SpriteRenderer>();

        if (renderer.sprite == null || renderer.sprite.texture != tex)
            renderer.sprite = MakeSprite(tex);

        maybe.gameObject.SetActive(true);
    }

    private Texture2D PlatformTex(VRRig rig, out bool isFallback)
    {
        isFallback = false;

        if (!rig.initializedCosmetics)
            return null;

        Texture2D tex = TexForKey(GetPlatformProperty(rig));

        if (tex != null)
            return tex;

        isFallback = true;

        return TexForKey(PlatformKeyFallback(rig)) ?? Plugin.Instance.MaybeTex;
    }

    private static Texture2D TexForKey(string key) => key?.TrimEnd('?') switch
                                                      {
                                                              "STEAM"   => Plugin.Instance.SteamTex,
                                                              "PC"      => Plugin.Instance.PcvrTex,
                                                              "PCVR"    => Plugin.Instance.PcvrTex,
                                                              "QUESTPC" => Plugin.Instance.QuestpcTex,
                                                              "QUEST"   => Plugin.Instance.QuestTex,
                                                              var _     => null,
                                                      };

#endregion

#region ===== - Tag Content - =====

    private static string PickColor((int Min, string Color)[] table, int value, string fallback)
    {
        foreach ((int min, string color) in table)
            if (value >= min)
                return color;

        return fallback;
    }

    public string FpsColor(int fps) => PickColor(FpsColors, fps, "#600000");

    public string PingColor(int ping) => PickColor(PingColors, ping, "#ab0080");

    public string SpecialPlayerTag(VRRig rig)
    {
        if (!Plugin.Instance.CheckSpecial.Value || rig?.Creator == null || specialCache == null)
            return string.Empty;

        return specialCache.TryGetValue(rig.Creator.UserId, out string specialTag) ? specialTag : string.Empty;
    }

    public string PlatformTag(VRRig rig)
    {
        if (!Plugin.Instance.CheckPlatform.Value) return string.Empty;

        string platformKey = PlatformKey(rig, out bool _);

        return PlatColors.TryGetValue(platformKey, out string clr) ? $"[<color={clr}>{platformKey}</color>]" : $"[{platformKey}]";
    }

    // ReSharper disable Unity.PerformanceAnalysis
    private string PlatformKey(VRRig rig, out bool isFallback)
    {
        isFallback = false;

        if (!rig.initializedCosmetics)
            return "LOADING...";

        string platform = GetPlatformProperty(rig);

        if (!string.IsNullOrEmpty(platform) && platform != "UNKNOWN")
            return platform;

        isFallback = true;

        return PlatformKeyFallback(rig);
    }

    private string PlatformKeyFallback(VRRig rig)
    {
        string cosmetics = rig._playerOwnedCosmetics.Concat() ?? "";

        if (cosmetics.Contains("S. FIRST LOGIN")) return "STEAM?";
        if (cosmetics.Contains("FIRST LOGIN") || cosmetics.Contains("game-purchase-bundle")) return "QUESTPC?";
        if (rig.Creator.GetPlayerRef().CustomProperties.Count > 1 || rig.currentRankedSubTierPC > 0) return "PCVR?";
        if (rig.currentRankedSubTierQuest > 0) return "QUEST?";

        DateTime? playerCreationDate = GetPlayerCreationDate(rig.Creator.UserId);

        return playerCreationDate > AddedSteamPaymentDate ? "QUEST?" : "UNKNOWN";
    }

    private string GetPlatformProperty(VRRig rig)
    {
        Hashtable props = rig.Creator.GetPlayerRef().CustomProperties;

        object exact = props["platform"];

        if (exact != null)
            return exact.ToString().ToUpperInvariant();

        foreach (DictionaryEntry entry in props)
            if (entry.Key is string k && string.Equals(k, "platform", StringComparison.OrdinalIgnoreCase))
                return entry.Value?.ToString()?.ToUpperInvariant();

        return null;
    }

    public string SpecialCosmeticsTag(VRRig rig)
    {
        if (!Plugin.Instance.CheckCosmetics.Value) return string.Empty;

        string cosmetics = rig._playerOwnedCosmetics.Concat() ?? "";

        return string.Concat(SpecialCosmetics.Where(c => cosmetics.Contains(c.Key)).Select(c => c.Value));
    }

    public string PlayerModsTag(VRRig rig)
    {
        if (!Plugin.Instance.CheckModLists.Value || modsCache == null)
            return string.Empty;

        StringBuilder sb = new(128);

        try
        {
            foreach (DictionaryEntry entry in rig.Creator.GetPlayerRef().CustomProperties)
                try
                {
                    string rawKey = entry.Key?.ToString() ?? "";

                    int wyndigoIdx = rawKey.IndexOf("wyndigo", StringComparison.OrdinalIgnoreCase);
                    if (wyndigoIdx >= 0)
                    {
                        sb.Append($"[<color=#ff0000>WYNDIGO</color> {rawKey.Substring(wyndigoIdx + 7).Trim().ToLower()}] ");

                        continue;
                    }

                    string key = NormalizeString(rawKey);

                    if (!modsCache.TryGetValue(key, out string tag))
                        continue;

                    string formatted = FormatModValue(key, tag, entry.Value);

                    if (formatted != null)
                        sb.Append(formatted + " ");
                }
                catch
                {
                    // ignored
                }
        }
        catch
        {
            return string.Empty;
        }

        try
        {
            if (rig.initializedCosmetics && Plugin.Instance.CheckCheats.Value && HasCosmetx(rig))
                sb.Append("[<color=#ff0000>COSMETX</color>]");
        }
        catch
        {
            // ignored
        }
        return sb.ToString().Trim();
    }

    private string FormatModValue(string key, string tag, object value)
    {
        if (value == null) return null;

        string version = value switch
                         {
                                 string s when s.StartsWith("System.", StringComparison.OrdinalIgnoreCase) => null,
                                 string s                                                                  => ModVersionRegex.Match(s) is { Success: true, } m ? m.Groups[1].Value : null,
                                 Hashtable ht                                                              => FindDictValue(ht, "version"),
                                 IDictionary d                                                             => FindDictValue(d,  "version"),
                                 var _                                                                     => null,
                         };

        if (!string.IsNullOrEmpty(version))
            return bool.TryParse(version, out bool _) ? null : string.Format(tag, version);

        string raw = value switch
                     {
                             Hashtable ht  => FindDictValue(ht, "other", "extra") ?? FindDictValue(ht, "version"),
                             IDictionary d => FindDictValue(d, "other", "extra"),
                             var _         => value.ToString(),
                     };

        if (string.IsNullOrEmpty(raw))
            return null;

        string trimmed = raw.Trim();

        if (bool.TryParse(trimmed, out bool _) || string.Equals(NormalizeString(trimmed), key, StringComparison.OrdinalIgnoreCase))
            return null;

        return Regex.Replace(tag, @"\s*\{0\}\s*", " ").Trim();
    }

    private string FindDictValue(IEnumerable dict, params string[] keys)
    {
        if (dict == null) return null;

        try
        {
            foreach (DictionaryEntry e in dict)
                if (e.Key is string k && keys.Contains(k, StringComparer.OrdinalIgnoreCase))
                    return e.Value?.ToString();
        }
        catch (InvalidCastException)
        {
            try
            {
                foreach (object item in dict)
                {
                    Type   type = item?.GetType();
                    string key  = type?.GetProperty("Key")?.GetValue(item)?.ToString();

                    if (key != null && keys.Contains(key, StringComparer.OrdinalIgnoreCase))
                        return type.GetProperty("Value")?.GetValue(item)?.ToString();
                }
            }
            catch
            {
                // ignored
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private bool HasCosmetx(VRRig rig)
    {
        string cosmetics = rig._playerOwnedCosmetics.Concat();

        return rig.cosmeticSet.items.Any(item => !item.isNullItem && cosmetics?.Contains(item.itemName) != true);
    }

#endregion

#region ===== - Cache - =====

    public void RefreshCache()
    {
        if (Plugin.Instance.CheckSpecial.Value || Plugin.Instance.CheckModLists.Value)
            StartCoroutine(UpdateCacheCoroutine());
    }

    private IEnumerator UpdateCacheCoroutine()
    {
        yield return new WaitForEndOfFrame();

        if (Plugin.Instance.CheckSpecial.Value)
            specialCache = LoadCache($"{Plugin.MainGitUrl}People.txt");

        if (Plugin.Instance.CheckModLists.Value)
        {
            modsCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (Plugin.Instance.CheckMods.Value)
                ParseKeyValues(Download($"{Plugin.ModsGitUrl}{(Plugin.Instance.AbbreviateMods.Value ? "Mods.txt" : "Mods_unabbreviated.txt")}"), modsCache, Constants.LegalMod);

            if (Plugin.Instance.CheckCheats.Value)
                ParseKeyValues(Download($"{Plugin.ModsGitUrl}Cheats.txt"), modsCache, Constants.IllegalMod);
        }
    }

    private static string Download(string url)
    {
        try
        {
            using WebClient client = new();

            return client.DownloadString(url);
        }
        catch
        {
            return null;
        }
    }

    private Dictionary<string, string> LoadCache(string url)
    {
        Dictionary<string, string> cache = new(StringComparer.OrdinalIgnoreCase);
        ParseKeyValues(Download(url), cache);

        return cache;
    }

    private void ParseKeyValues(string content, Dictionary<string, string> dictionary, string color = null)
    {
        if (content == null)
            return;

        foreach (string line in content.Split(['\n', '\r',], StringSplitOptions.RemoveEmptyEntries))
        {
            int sep = line.IndexOf('|');

            if (sep < 0)
                continue;

            string value = line.Substring(sep + 1).Trim();

            dictionary[NormalizeString(line.Substring(0, sep))] = color != null ? FormatColoredTag(value, color) : value;
        }
    }

    private string FormatColoredTag(string value, string color)
    {
        const string versionSuffix = "v{0}";

        if (value.EndsWith(versionSuffix, StringComparison.OrdinalIgnoreCase))
            return $"[<color={color}>{value.Substring(0, value.Length - versionSuffix.Length).TrimEnd()}</color> {versionSuffix}]";

        return value.Contains("{0}") ? $"[{value}]" : $"[<color={color}>{value}</color>]";
    }

    private static string NormalizeString(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        input = EscapeSequenceRegex.Replace(ControlCharRegex.Replace(input, ""), "").Replace("\\", "");

        return WhitespaceRegex.Replace(input, " ").Trim().ToLowerInvariant();
    }

    private DateTime? GetPlayerCreationDate(string playFabId)
    {
        if (PlayerCreationDateDict.TryGetValue(playFabId, out DateTime cachedDate))
            return cachedDate;

        PlayFabClientAPI.GetAccountInfo(new GetAccountInfoRequest { PlayFabId = playFabId, },
                result =>
                {
                    if (result.AccountInfo != null)
                        PlayerCreationDateDict[playFabId] = result.AccountInfo.Created;
                },
                _ => { });

        return null;
    }

#endregion
}