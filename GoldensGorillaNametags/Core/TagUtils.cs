using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

    private static readonly Dictionary<int, string> FpsColors = new()
    {
            { 250, "#800080" }, { 200, "#1e90ff" }, { 150, "#006400" },
            { 100, "#00ff00" }, { 75, "#adff2f" }, { 55, "#ffff00" },
            { 45, "#ffa500" }, { 30, "#ff0000" }, { 29, "#8b0000" },
    };

    private static readonly Dictionary<int, string> PingColors = new()
    {
            { 25, "#800080" }, { 35, "#1e90ff" }, { 55, "#006400" },
            { 75, "#00ff00" }, { 90, "#adff2f" }, { 120, "#ffff00" },
            { 150, "#ffa500" }, { 200, "#ff0000" }, { 250, "#8b0000" },
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

    private Dictionary<string, string> specialCache, modsCache, eventColorsCache;

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
        Texture2D newPlatTex = PlatformTex(rig, out bool isFallback);

        bool fallbackChanged = !_platformFallbackState.TryGetValue(data, out bool prevFallback) || prevFallback != isFallback;

        if (newPlatTex == data.CurrentPlatformTex && !fallbackChanged)
            return;

        data.CurrentPlatformTex      = newPlatTex;
        _platformFallbackState[data] = isFallback;

        if (newPlatTex != null)
        {
            data.PlatformIconRenderer.sprite = Sprite.Create(newPlatTex, new Rect(0, 0, newPlatTex.width, newPlatTex.height), Vector2.one * 0.5f);
            data.PlatformIconRenderer.gameObject.SetActive(true);

            UpdateMaybeIcon(data, isFallback);
        }
        else
        {
            data.PlatformIconRenderer.sprite = null;
            data.PlatformIconRenderer.gameObject.SetActive(false);

            UpdateMaybeIcon(data, false);
        }
    }

    private void UpdateMaybeIcon(NametagData data, bool show)
    {
        if (data?.PlatformIconRenderer == null)
            return;

        Transform existingT = data.PlatformIconRenderer.transform.Find("MaybeIcon");

        if (!show)
        {
            if (existingT != null)
                existingT.gameObject.SetActive(false);

            return;
        }

        if (Plugin.Instance.MaybeTex == null)
            return;

        SpriteRenderer maybeRenderer = MaybeIconRenderer(data.PlatformIconRenderer);

        if (maybeRenderer.sprite == null || maybeRenderer.sprite.texture != Plugin.Instance.MaybeTex)
            maybeRenderer.sprite = Sprite.Create(Plugin.Instance.MaybeTex, new Rect(0, 0, Plugin.Instance.MaybeTex.width, Plugin.Instance.MaybeTex.height), Vector2.one * 0.5f);

        maybeRenderer.gameObject.SetActive(true);
    }

    private SpriteRenderer MaybeIconRenderer(SpriteRenderer parentRenderer)
    {
        Transform  existing = parentRenderer.transform.Find("MaybeIcon");
        GameObject go;

        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject("MaybeIcon");
            go.transform.SetParent(parentRenderer.transform, false);
            go.AddComponent<SpriteRenderer>().sortingOrder = parentRenderer.sortingOrder + 1;
        }

        go.transform.localPosition = new Vector3(2.5f, 0f, 0f);

        return go.GetComponent<SpriteRenderer>();
    }

    private Texture2D PlatformTex(VRRig rig, out bool isFallback)
    {
        isFallback = false;

        if (!rig.initializedCosmetics)
            return null;

        string platform = GetPlatformProperty(rig);

        Texture2D tex = platform switch
                        {
                                "STEAM"   => Plugin.Instance.SteamTex,
                                "PC"      => Plugin.Instance.PcvrTex,
                                "QUESTPC" => Plugin.Instance.QuestpcTex,
                                "PCVR"    => Plugin.Instance.PcvrTex,
                                "QUEST"   => Plugin.Instance.QuestTex,
                                var _     => null,
                        };

        if (tex != null)
            return tex;

        isFallback = true;

        return PlatformTexFallback(rig);
    }

    private Texture2D PlatformTexFallback(VRRig rig)
    {
        string cosmetics = rig._playerOwnedCosmetics.Concat();
        int    propCount = rig.Creator.GetPlayerRef().CustomProperties.Count;

        if (cosmetics.Contains("S. FIRST LOGIN")) return Plugin.Instance.SteamTex;
        if (cosmetics.Contains("FIRST LOGIN") || cosmetics.Contains("game-purchase-bundle"))
            return Plugin.Instance.QuestpcTex;

        if (propCount > 1 || rig.currentRankedSubTierPC > 0) return Plugin.Instance.PcvrTex;
        if (rig.currentRankedSubTierQuest > 0) return Plugin.Instance.QuestTex;

        DateTime? playerCreationDate = GetPlayerCreationDate(rig.Creator.UserId);

        if (playerCreationDate.HasValue && playerCreationDate.Value > AddedSteamPaymentDate)
            return Plugin.Instance.QuestTex;

        return Plugin.Instance.MaybeTex; //was null before
    }

#endregion

#region ===== - Tag Content - =====

    public string FpsColor(int fps)
    {
        foreach (KeyValuePair<int, string> threshold in FpsColors.OrderByDescending(kv => kv.Key))
            if (fps >= threshold.Key)
                return threshold.Value;

        return "#600000";
    }

    public string PingColor(int ping)
    {
        foreach (KeyValuePair<int, string> threshold in PingColors.OrderByDescending(kv => kv.Key))
            if (ping >= threshold.Key)
                return threshold.Value;

        return "#ab0080";
    }

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
        int    propCount = rig.Creator.GetPlayerRef().CustomProperties.Count;

        if (cosmetics.Contains("S. FIRST LOGIN")) return "STEAM?";
        if (cosmetics.Contains("FIRST LOGIN") || cosmetics.Contains("game-purchase-bundle")) return "QUESTPC?";
        if (propCount > 1                     || rig.currentRankedSubTierPC > 0) return "PCVR?";
        if (rig.currentRankedSubTierQuest > 0) return "QUEST?";

        DateTime? playerCreationDate = GetPlayerCreationDate(rig.Creator.UserId);

        if (playerCreationDate.HasValue && playerCreationDate.Value > AddedSteamPaymentDate)
            return "QUEST?";

        return "UNKNOWN";
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

        StringBuilder sb        = new(32);
        string        cosmetics = rig._playerOwnedCosmetics.Concat() ?? "";

        foreach (KeyValuePair<string, string> cosmetic in SpecialCosmetics)
            if (cosmetics.Contains(cosmetic.Key))
                sb.Append(cosmetic.Value);

        return sb.ToString();
    }

    public string PlayerModsTag(VRRig rig)
    {
        if (!Plugin.Instance.CheckModLists.Value || modsCache == null)
            return string.Empty;

        StringBuilder sb    = new(128);
        Hashtable     props = null;

        try
        {
            props = rig.Creator.GetPlayerRef().CustomProperties;
        }
        catch
        {
            return string.Empty;
        }

        if (props == null) return string.Empty;

        foreach (DictionaryEntry entry in props)
            try
            {
                string _firstkey = entry.Key?.ToString() ?? "";

                int wyndigoIdx = _firstkey.IndexOf("wyndigo", StringComparison.OrdinalIgnoreCase);
                if (wyndigoIdx >= 0)
                {
                    string afterWyndigo = _firstkey.Substring(wyndigoIdx + "wyndigo".Length).Trim();

                    sb.Append($"[<color=#ff0000>WYNDIGO</color> {afterWyndigo.ToLower()}] ");

                    continue;
                }

                string _secondkey = NormalizeString(entry.Key.ToString());

                if (!modsCache.TryGetValue(_secondkey, out string tag))
                    continue;

                string formatted = FormatModValue(_secondkey, tag, entry.Value);

                if (formatted == null)
                    continue;

                sb.Append(formatted + " ");
            }
            catch { }

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

    private string BuildEventTag(string str1, string str2)
    {
        string color        = GetEventColor(str2);
        string coloredLabel = $"<color={color}>{str2}</color>";

        return str1.Length == 16
                       ? $"[{coloredLabel}]"
                       : $"[{coloredLabel} v{str1}]";
    }

    private string GetEventColor(string str2)
    {
        if (eventColorsCache != null && eventColorsCache.TryGetValue(NormalizeString(str2), out string color))
            return $"#{color}";

        return "#ffffff";
    }

    private string FormatModValue(string key, string tag, object value)
    {
        if (value == null) return null;

        string version = null;

        try
        {
            version = value switch
                      {
                              string s when s.StartsWith("System.", StringComparison.OrdinalIgnoreCase) => null,
                              string s                                                                  => ModVersionRegex.Match(s) is { Success: true, } m ? m.Groups[1].Value : null,
                              Hashtable ht                                                              => FindDictValue(ht, "version"),
                              IDictionary d                                                             => FindDictValue(d,  "version"),
                              bool                                                                      => null,
                              var _                                                                     => null,
                      };
        }
        catch
        {
            version = null;
        }

        if (!string.IsNullOrEmpty(version))
        {
            if (string.Equals(version.Trim(), "true",  StringComparison.OrdinalIgnoreCase) ||
                string.Equals(version.Trim(), "false", StringComparison.OrdinalIgnoreCase))
                return null;

            return string.Format(tag, version);
        }

        string raw = null;

        try
        {
            raw = value switch
                  {
                          Hashtable ht  => FindDictValue(ht, "other", "extra") ?? FindDictValue(ht, "version"),
                          IDictionary d => FindDictValue(d, "other", "extra"),
                          string s      => s,
                          bool b        => b.ToString(),
                          var _         => value.ToString(),
                  };
        }
        catch
        {
            raw = value.ToString();
        }

        if (string.IsNullOrEmpty(raw))
            return null;

        string trimmed = raw.Trim();

        if (bool.TryParse(trimmed, out bool _)                                  ||
            string.Equals(trimmed, "true",  StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.Equals(NormalizeString(trimmed), key, StringComparison.OrdinalIgnoreCase))
            return null;

        if (tag.Contains("{0}"))
            return Regex.Replace(tag, @"\s*\{0\}\s*", " ").Trim();

        return tag.Trim();
    }

    private string FindDictValue(IEnumerable dict, params string[] keys)
    {
        if (dict == null) return null;

        try
        {
            foreach (DictionaryEntry e in dict)
                if (e.Key is string k && keys.Any(target => string.Equals(k, target, StringComparison.OrdinalIgnoreCase)))
                    return e.Value?.ToString();
        }
        catch (InvalidCastException)
        {
            try
            {
                foreach (object item in dict)
                {
                    if (item == null) continue;

                    Type         type      = item.GetType();
                    PropertyInfo keyProp   = type.GetProperty("Key");
                    PropertyInfo valueProp = type.GetProperty("Value");

                    if (keyProp != null && valueProp != null)
                    {
                        string key = keyProp.GetValue(item)?.ToString();

                        if (key != null && keys.Any(target => string.Equals(key, target, StringComparison.OrdinalIgnoreCase)))
                            return valueProp.GetValue(item)?.ToString();
                    }
                }
            }
            catch
            {
                // ignored
            }
        }
        catch (Exception)
        {
            // ignored
        }

        return null;
    }

    private bool HasCosmetx(VRRig rig)
    {
        return rig.cosmeticSet.items.Any(item => !item.isNullItem && rig._playerOwnedCosmetics.Concat()?.Contains(item.itemName) != true);
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
            specialCache = SpecialPlayerCache();

        if (Plugin.Instance.CheckModLists.Value)
            modsCache = ModsCache();
    }

    private Dictionary<string, string> SpecialPlayerCache()
    {
        Dictionary<string, string> cache = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            using WebClient client  = new();
            string          content = client.DownloadString($"{Plugin.MainGitUrl}People.txt");
            ParseKeyValues(content, cache);
        }
        catch
        {
            // ignored
        }

        return cache;
    }

    private Dictionary<string, string> ModsCache()
    {
        Dictionary<string, string> cache = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            using WebClient client = new();

            if (Plugin.Instance.CheckMods.Value)
            {
                string modFile = Plugin.Instance.AbbreviateMods.Value ? "FEMods.txt" : "FEMods_unabbreviated.txt";
                try
                {
                    string content = client.DownloadString($"{Plugin.ModsGitUrl}{modFile}");
                    ParseKeyValues(content, cache, Constants.LegalMod);
                }
                catch
                {
                    // ignored
                }
            }

            if (Plugin.Instance.CheckCheats.Value)
            {
                string cheatFile = "FECheats.txt";
                try
                {
                    string content = client.DownloadString($"{Plugin.ModsGitUrl}{cheatFile}");
                    ParseKeyValues(content, cache, Constants.IllegalMod);
                }
                catch
                {
                    // ignored
                }
            }
        }
        catch
        {
            // ignored
        }

        return cache;
    }

    private Dictionary<string, string> EventColorsCache()
    {
        Dictionary<string, string> cache = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            using WebClient client  = new();
            string          content = client.DownloadString($"{Plugin.ModsGitUrl}FEEvents.txt");

            foreach (string line in content.Split(['\n', '\r',], StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split(['|',], 2, StringSplitOptions.None);
                if (parts.Length == 2)
                    cache[NormalizeString(parts[0])] = parts[1].Trim();
            }
        }
        catch
        {
            // ignored
        }

        return cache;
    }

    private void ParseKeyValues(string content, Dictionary<string, string> dictionary, string color = null)
    {
        string[] lines = content.Split(['\n', '\r',], StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            string[] parts = line.Split(['|',], 2, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                string key     = NormalizeString(parts[0]);
                string _string = parts[1].Trim();

                dictionary[key] = color != null ? FormatColoredTag(_string, color) : _string;
            }
        }
    }

    private string FormatColoredTag(string _string, string color)
    {
        const string versionSuffix = "v{0}";

        if (_string.EndsWith(versionSuffix, StringComparison.OrdinalIgnoreCase))
        {
            string name = _string.Substring(0, _string.Length - versionSuffix.Length).TrimEnd();

            return $"[<color={color}>{name}</color> {versionSuffix}]";
        }

        if (_string.Contains("{0}"))
            return $"[{_string}]";

        return $"[<color={color}>{_string}</color>]";
    }

    private static string NormalizeString(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        input = ControlCharRegex.Replace(input, "");
        input = EscapeSequenceRegex.Replace(input, "");
        input = input.Replace("\\", "");
        input = WhitespaceRegex.Replace(input, " ");

        return input.Trim().ToLowerInvariant();
    }

    private DateTime? GetPlayerCreationDate(string playFabId)
    {
        if (PlayerCreationDateDict.TryGetValue(playFabId, out DateTime cachedDate))
            return cachedDate;

        _ = FetchCreationDateAsync(playFabId);

        return null;
    }

    private async Task FetchCreationDateAsync(string playFabId)
    {
        if (PlayerCreationDateDict.ContainsKey(playFabId))
            return;

        TaskCompletionSource<GetAccountInfoResult> tcs = new();

        PlayFabClientAPI.GetAccountInfo(new GetAccountInfoRequest { PlayFabId = playFabId, },
                result => tcs.SetResult(result),
                error => tcs.SetException(new Exception(error.ErrorMessage)));

        try
        {
            GetAccountInfoResult result = await tcs.Task;
            PlayerCreationDateDict[playFabId] = result.AccountInfo.Created;
        }
        catch
        {
            // ignored
        }
    }

#endregion
}