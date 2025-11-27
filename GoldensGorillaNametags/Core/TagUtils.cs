using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace GoldensGorillaNametags.Core;

public class TagUtils : MonoBehaviour
{
    public static TagUtils Instance;

    private static readonly Dictionary<int, string> FpsClrs = new()
    {
            { 250, "#800080" }, { 200, "#1E90FF" }, { 150, "#006400" },
            { 100, "#00FF00" }, { 75, "#ADFF2F" }, { 55, "#FFFF00" },
            { 45, "#FFA500" }, { 30, "#FF0000" }, { 29, "#8B0000" },
    };

    private static readonly Dictionary<int, string> PingClrs = new()
    {
            { 25, "#800080" }, { 35, "#1E90FF" }, { 55, "#006400" },
            { 75, "#00FF00" }, { 90, "#ADFF2F" }, { 120, "#FFFF00" },
            { 150, "#FFA500" }, { 200, "#FF0000" }, { 250, "#8B0000" },
    };

    private static readonly Dictionary<string, string> PlatClrs = new()
    {
            { "SVR", "#ffff00" },
            { "PCVR", "#ff0000" },
            { "O", "#00ff00" },
    };

    private static readonly Dictionary<string, string> CosTags = new()
    {
            { "LBANI.", "[<color=#FCC200>AAC</color>]" }, { "LBADE.", "[<color=#FCC200>FP</color>]" },
            { "LBAGS.", "[<color=#FCC200>ILL</color>]" }, { "LBAAK.", "[<color=#FF0000>S</color>]" },
            { "LMAPY.", "[<color=#C80000>FS</color>]" }, { "LBAAD.", "[<color=#960000>A</color>]" },
            { "LMAGB.", "[<color=#ffffff>CG</color>]" }, { "LMAKH.", "[<color=#ffffff>ZC</color>]" },
            { "LMAJD.", "[<color=#ffffff>DK</color>]" }, { "LMAHF.", "[<color=#ffffff>CFP</color>]" },
            { "LMAAQ.", "[<color=#ffffff>ST</color>]" }, { "LMAAV.", "[<color=#ffffff>HTS</color>]" },
    };

    private Texture2D cTex, sTex, mTex, wCTex, wSTex, wMTex;

    private Dictionary<string, string> specialCache;

    private void Awake() => Instance = this;

    public void InitPlatIcons()
    {
        StartCoroutine(ImageCoroutine($"{Plugin.Giturl}computer.png",       tex => cTex  = tex));
        StartCoroutine(ImageCoroutine($"{Plugin.Giturl}steam.png",          tex => sTex  = tex));
        StartCoroutine(ImageCoroutine($"{Plugin.Giturl}meta.png",           tex => mTex  = tex));
        StartCoroutine(ImageCoroutine($"{Plugin.Giturl}Computer_White.png", tex => wCTex = tex));
        StartCoroutine(ImageCoroutine($"{Plugin.Giturl}Steam_White.png",    tex => wSTex = tex));
        StartCoroutine(ImageCoroutine($"{Plugin.Giturl}Meta_White.png",     tex => wMTex = tex));
    }

    public IEnumerator UpdPlatIconCoroutine(VRRig r, NametagData data)
    {
        while (cTex  == null || sTex == null || mTex == null || wCTex == null || wSTex == null ||
               wMTex == null)
            yield return null;

        yield return new WaitForSeconds(2f);

        while (r != null && data?.PlatIconRenderer != null)
        {
            if (Plugin.Instance.UsePlatIcons.Value && Plugin.Instance.CheckPlat.Value)
            {
                UpdPlatIconTex(r, data);
            }
            else
            {
                data.CurrentPlatTex          = null;
                data.PlatIconRenderer.sprite = null;
                data.PlatIconRenderer.gameObject.SetActive(false);
            }

            yield return new WaitForSeconds(10f);
        }
    }

    private void UpdPlatIconTex(VRRig r, NametagData data)
    {
        Texture2D newPlatTex = PlatTex(r);

        if (newPlatTex == data.CurrentPlatTex)
            return;

        data.CurrentPlatTex = newPlatTex;

        if (newPlatTex != null)
        {
            data.PlatIconRenderer.sprite = Sprite.Create(newPlatTex,
                    new Rect(0, 0, newPlatTex.width, newPlatTex.height),
                    Vector2.one * 0.5f);

            data.PlatIconRenderer.gameObject.SetActive(true);
        }
        else
        {
            data.PlatIconRenderer.sprite = null;
            data.PlatIconRenderer.gameObject.SetActive(false);
        }
    }

    public string FpsClr(int fps)
    {
        foreach (KeyValuePair<int, string> threshold in FpsClrs.OrderByDescending(kv => kv.Key))
            if (fps >= threshold.Key)
                return threshold.Value;

        return "#600000";
    }

    public string PingClr(int ping)
    {
        foreach (KeyValuePair<int, string> threshold in PingClrs.OrderByDescending(kv => kv.Key))
            if (ping >= threshold.Key)
                return threshold.Value;

        return "#AB0080";
    }

    public string SpecialTag(VRRig r)
    {
        if (!Plugin.Instance.CheckSpecial.Value || r?.OwningNetPlayer == null || specialCache == null)
            return string.Empty;

        return specialCache.TryGetValue(r.OwningNetPlayer.UserId, out string specialTag) ? specialTag : string.Empty;
    }

    public string PlatTag(VRRig r)
    {
        if (!Plugin.Instance.CheckPlat.Value) return string.Empty;

        string cosmetics   = r.concatStringOfCosmeticsAllowed ?? "";
        string platformKey = PlatKey(cosmetics, r);

        return PlatClrs.TryGetValue(platformKey, out string clr)
                       ? $"[<color={clr}>{platformKey}</color>]"
                       : "[Unknown]";
    }

    private string PlatKey(string cosmetics, VRRig r)
    {
        if (string.IsNullOrEmpty(cosmetics) || r?.OwningNetPlayer == null) return "Unknown";

        int propCount = r.OwningNetPlayer.GetPlayerRef().CustomProperties.Count;

        if (cosmetics.Contains("S. FIRST LOGIN")) return "SVR";
        if (cosmetics.Contains("FIRST LOGIN")  || propCount >= 2) return "PCVR";
        if (!cosmetics.Contains("FIRST LOGIN") || cosmetics.Contains("LMAKT.")) return "O";

        return "Unknown";
    }

    private Texture2D PlatTex(VRRig r)
    {
        if (r?.concatStringOfCosmeticsAllowed == null)
            return null;

        string cosmetics = r.concatStringOfCosmeticsAllowed;
        int    propCount = r.OwningNetPlayer.GetPlayerRef().CustomProperties.Count;

        if (Plugin.Instance.PlatIconClr.Value)
        {
            if (cosmetics.Contains("S. FIRST LOGIN"))
                return sTex;

            if (cosmetics.Contains("FIRST LOGIN") || propCount >= 2)
                return cTex;

            return mTex;
        }

        if (cosmetics.Contains("S. FIRST LOGIN"))
            return wSTex;

        if (cosmetics.Contains("FIRST LOGIN") || propCount >= 2)
            return wCTex;

        return wMTex;
    }

    public string CosmeticTag(VRRig r)
    {
        if (!Plugin.Instance.CheckCosmetics.Value) return string.Empty;

        StringBuilder sb        = new(32);
        string        cosmetics = r.concatStringOfCosmeticsAllowed ?? "";

        foreach (KeyValuePair<string, string> cosmetic in CosTags)
            if (cosmetics.Contains(cosmetic.Key))
                sb.Append(cosmetic.Value);

        return sb.ToString();
    }

    public void RefreshCache()
    {
        if (Plugin.Instance.CheckSpecial.Value)
            StartCoroutine(UpdCacheCoroutine());
    }

    private IEnumerator UpdCacheCoroutine()
    {
        yield return new WaitForEndOfFrame();

        if (Plugin.Instance.CheckSpecial.Value)
            specialCache = SpecialCache();
    }

    private Dictionary<string, string> SpecialCache()
    {
        Dictionary<string, string> cache = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            using WebClient client  = new();
            string          content = client.DownloadString($"{Plugin.Giturl}People.txt");
            KeyValShit(content, cache);
        }
        catch
        {
            // ignored
        }

        return cache;
    }
    

    private void KeyValShit(string content, Dictionary<string, string> dictionary)
    {
        string[] lines = content.Split(['\n', '\r',], StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            string[] parts = line.Split(['$',], 2);
            if (parts.Length == 2)
            {
                string key = parts[0].Trim();
                dictionary[key] = parts[1].Trim();
            }
        }
    }

    private IEnumerator ImageCoroutine(string url, Action<Texture2D> onComplete)
    {
        using UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
            yield break;

        Texture2D tex = DownloadHandlerTexture.GetContent(request);
        tex.filterMode = FilterMode.Point;
        onComplete(tex);
    }
}