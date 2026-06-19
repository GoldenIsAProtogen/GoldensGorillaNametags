using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace GoldensGorillaNametags.Core;

public class TagUtils : MonoBehaviour
{
#region ==-== Fields & Init ==-==

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
            { "STEAMVR", "#ffff00" }, { "QUESTPC", "#ffaa00" }, { "PCVR", "#ff0000" },
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

    private Dictionary<string, string> specialCache;

    private void Awake() => Instance = this;

#endregion

#region ==-== Icons ==-==

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
            }

            yield return new WaitForSeconds(10f);
        }
    }

    private void UpdatePlatformIconTex(VRRig rig, NametagData data)
    {
        Texture2D newPlatTex = PlatformTex(rig);

        if (newPlatTex == data.CurrentPlatformTex)
            return;

        data.CurrentPlatformTex = newPlatTex;

        if (newPlatTex != null)
        {
            data.PlatformIconRenderer.sprite = Sprite.Create(newPlatTex, new Rect(0, 0, newPlatTex.width, newPlatTex.height), Vector2.one * 0.5f);
            data.PlatformIconRenderer.gameObject.SetActive(true);
        }
        else
        {
            data.PlatformIconRenderer.sprite = null;
            data.PlatformIconRenderer.gameObject.SetActive(false);
        }
    }

    private Texture2D PlatformTex(VRRig rig)
    {
        string cosmetics = rig._playerOwnedCosmetics.Concat();
        int    propCount = rig.Creator.GetPlayerRef().CustomProperties.Count;

        if (rig.initializedCosmetics)
        {
            if (cosmetics.Contains("S. FIRST LOGIN")) return Plugin.Instance.SteamTex;
            if (cosmetics.Contains("FIRST LOGIN") || cosmetics.Contains("game-purchase-bundle"))
                return Plugin.Instance.QuestpcTex;

            if (propCount > 1 || rig.currentRankedSubTierPC > 0) return Plugin.Instance.PcvrTex;
            if (rig.currentRankedSubTierQuest > 0) return Plugin.Instance.QuestTex;

            DateTime? playerCreationDate = GetPlayerCreationDate(rig.Creator.UserId);

            if (playerCreationDate.HasValue && playerCreationDate.Value > AddedSteamPaymentDate)
                return Plugin.Instance.QuestTex;
        }

        return null;
    }

#endregion

#region ==-== Tag Content ==-==

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

        string cosmetics   = rig._playerOwnedCosmetics.Concat() ?? "";
        string platformKey = PlatformKey(cosmetics, rig);

        return PlatColors.TryGetValue(platformKey, out string clr) ? $"[<color={clr}>{platformKey}</color>]" : $"[{platformKey}]";
    }

    // ReSharper disable Unity.PerformanceAnalysis
    private string PlatformKey(string cosmetics, VRRig rig)
    {
        int propCount = rig.Creator.GetPlayerRef().CustomProperties.Count;

        if (rig.initializedCosmetics)
        {
            if (cosmetics.Contains("S. FIRST LOGIN")) return "STEAMVR";
            if (cosmetics.Contains("FIRST LOGIN") || cosmetics.Contains("game-purchase-bundle")) return "QUESTPC";
            if (propCount > 1                     || rig.currentRankedSubTierPC > 0) return "PCVR";
            if (rig.currentRankedSubTierQuest > 0) return "QUEST";

            DateTime? playerCreationDate = GetPlayerCreationDate(rig.Creator.UserId);

            if (playerCreationDate.HasValue && playerCreationDate.Value > AddedSteamPaymentDate)
                return "QUEST";

            return "UNKNOWN";
        }

        return "LOADING...";
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

#endregion

#region ==-== Cache ==-==

    public void RefreshCache()
    {
        if (Plugin.Instance.CheckSpecial.Value)
            StartCoroutine(UpdateCacheCoroutine());
    }

    private IEnumerator UpdateCacheCoroutine()
    {
        yield return new WaitForEndOfFrame();

        if (Plugin.Instance.CheckSpecial.Value)
            specialCache = SpecialPlayerCache();
    }

    private Dictionary<string, string> SpecialPlayerCache()
    {
        Dictionary<string, string> cache = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            using WebClient client  = new();
            string          content = client.DownloadString($"{Plugin.GitUrl}People.txt");
            ParseKeyValues(content, cache);
        }
        catch
        {
            // ignored
        }

        return cache;
    }
    

    private void ParseKeyValues(string content, Dictionary<string, string> dictionary)
    {
        string[] lines = content.Split(['\n', '\r',], StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            string[] parts = line.Split(['|',], 2, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                string key = NormalizeString(parts[0]);
                dictionary[key] = parts[1].Trim();
            }
        }
    }

    private static string NormalizeString(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        input = Regex.Replace(input, @"[\x00-\x1F\x7F]", "");
        input = Regex.Replace(input, @"\\[nrtvbfa\\]+",  "");
        input = input.Replace("\\", "");
        input = Regex.Replace(input, @"\s+", " ");

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