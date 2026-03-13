using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GoldensGorillaNametags.Utils;
using TMPro;
using UnityEngine;
using GFriends = GorillaFriends.Main;

namespace GoldensGorillaNametags.Core;

public class TagManager : MonoBehaviour
{
    private const           float      TagUpdateTime = 0.3f;
    public static           TagManager Instance;
    private static readonly Vector3    BaseScale  = Vector3.one * 0.8f;
    private static readonly Vector3    ImageBasePos = new(0f, 0.85f, 0f);

    private readonly Dictionary<VRRig, float> lastTagUpd = new();

    private readonly Dictionary<VRRig, NametagData> tagMap = new();

    private void Awake() => Instance = this;

    public void CleanupTags(HashSet<VRRig> validRigs)
    {
        List<VRRig> rigsToRemove
                = tagMap.Where(kv => kv.Key == null        || !validRigs.Contains(kv.Key) || kv.Key.isOfflineVRRig || kv.Key.Creator == null).Select(kv => kv.Key).ToList();

        foreach (VRRig rig in rigsToRemove)
        {
            if (tagMap.TryGetValue(rig, out NametagData data))
            {
                if (data.ImageUpdateCoroutine != null)
                    StopCoroutine(data.ImageUpdateCoroutine);

                CleanupOutlines(data);
                if (data.Container != null)
                    Destroy(data.Container);
            }

            tagMap.Remove(rig);
            lastTagUpd.Remove(rig);
        }
    }

    public void CreateTagmap(HashSet<VRRig> validRigs)
    {
        foreach (VRRig rig in validRigs.Where(r => r != null && !r.isOfflineVRRig && r.Creator != null).Where(r => !tagMap.ContainsKey(r)))
            tagMap[rig] = CreateTags(rig);
    }

    private NametagData CreateTags(VRRig rig)
    {
        NametagData data = new();

        data.Container = new GameObject("NametagContainer");
        data.Container.transform.SetParent(rig.transform, false);
        data.Container.transform.localScale    = BaseScale;
        data.Container.transform.localPosition = new Vector3(0f, Plugin.Instance.TagHeight.Value, 0f);

        GameObject mainTxtGo = new("NametagMain");
        mainTxtGo.transform.SetParent(data.Container.transform, false);
        mainTxtGo.transform.localPosition = Vector3.zero;
        mainTxtGo.transform.localScale    = new Vector3(0.8f, 0.8f, 0.8f);

        data.MainText = mainTxtGo.AddComponent<TextMeshPro>();
        TagTextSpecifications(data.MainText);

        data.PlatformIconObj = new GameObject("PlatformIcon");
        data.PlatformIconObj.transform.SetParent(data.Container.transform, false);
        data.PlatformIconObj.transform.localPosition = IconPos(Plugin.Instance.IconLocation.Value);
        data.PlatformIconObj.transform.localScale = new Vector3(Plugin.Instance.IconSize.Value, Plugin.Instance.IconSize.Value, Plugin.Instance.IconSize.Value);

        data.PlatformIconRenderer              = data.PlatformIconObj.AddComponent<SpriteRenderer>();
        data.PlatformIconRenderer.sortingOrder = 10;
        data.PlatformIconRenderer.gameObject.SetActive(false);

        data.ImageUpdateCoroutine = StartCoroutine(TagUtils.Instance.UpdatePlatformIconCoroutine(rig, data));

        return data;
    }

    private Vector3 IconPos(string location)
    {
        return location.ToLower() switch
               {
                       "left" => new Vector3(Plugin.Instance.TagSize.Value * -0.5f, Plugin.Instance.TagHeight.Value - .85f, 0f),
                       "right" => new Vector3(Plugin.Instance.TagSize.Value * 0.5f, Plugin.Instance.TagHeight.Value - .85f, 0f),
                       var _    => ImageBasePos,
               };
    }

    private void TagTextSpecifications(TextMeshPro txt)
    {
        txt.alignment        = TextAlignmentOptions.Center;
        txt.fontSize         = Plugin.Instance.TagSize.Value;
        txt.font             = Plugin.Instance.Font;
        txt.textWrappingMode = TextWrappingModes.Normal;
        txt.richText         = true;
    }

    public void UpdateTags()
    {
        float currentTime = Time.time;

        foreach (KeyValuePair<VRRig, NametagData> kv in tagMap)
        {
            VRRig       rig  = kv.Key;
            NametagData data = kv.Value;

            if (rig == null || data?.Container == null || rig.isOfflineVRRig || rig.Creator == null)
                continue;

            Cam(data.Container.transform);

            if (!lastTagUpd.ContainsKey(rig) || currentTime - lastTagUpd[rig] >= TagUpdateTime)
            {
                UpdateTagContent(rig, data);
                lastTagUpd[rig] = currentTime;
            }

            UpdatePlatformIcon(data);
        }
    }

    private void Cam(Transform tagTransform)
    {
        if (tagTransform == null) return;

        Transform cameraTransform = Plugin.Instance.CineCam != null
                                            ? Plugin.Instance.CineCam.transform
                                            : Plugin.Instance.MainCam;

        if (cameraTransform == null) return;

        tagTransform.LookAt(cameraTransform.position);
        tagTransform.Rotate(0f, 180f, 0f);

        foreach (Transform child in tagTransform)
            child.localRotation = Quaternion.identity;
    }

    private void UpdatePlatformIcon(NametagData data)
    {
        if (data.PlatformIconRenderer == null)
            return;

        bool shouldBeVisible = Plugin.Instance.UsePlatIcons.Value && data.CurrentPlatformTex != null;
        data.PlatformIconRenderer.gameObject.SetActive(shouldBeVisible);
    }

    private void UpdateTagContent(VRRig r, NametagData data)
    {
        data.Container.transform.localPosition = new Vector3(0f, Plugin.Instance.TagHeight.Value, 0f);

        if (!Mathf.Approximately(data.MainText.fontSize, Plugin.Instance.TagSize.Value))
            data.MainText.fontSize = Plugin.Instance.TagSize.Value;

        string txt = CreateTagText(r);

        if (data.LastText == txt)
            return;

        data.MainText.text = txt;
        data.LastText      = txt;
        UpdateTextColor(r, data.MainText);
        UpdateOutlines(data);
    }

    public void ClearTags()
    {
        foreach (KeyValuePair<VRRig, NametagData> kv in tagMap)
        {
            VRRig       rig  = kv.Key;
            NametagData data = kv.Value;

            if (data != null)
            {
                if (data.ImageUpdateCoroutine != null)
                    StopCoroutine(data.ImageUpdateCoroutine);

                CleanupOutlines(data);

                if (data.Container != null)
                    Destroy(data.Container);
            }
        }

        tagMap.Clear();
        lastTagUpd.Clear();
    }

    private string CreateTagText(VRRig rig)
    {
        StringBuilder stringBuilder = new(128);

        if (Plugin.Instance.CheckSpecial.Value)
        {
            string specialTag = TagUtils.Instance.SpecialPlayerTag(rig);
            if (!string.IsNullOrEmpty(specialTag))
                stringBuilder.AppendLine($"<size=70%>{specialTag}</size>");
        }

        if (Plugin.Instance.CheckFps.Value || Plugin.Instance.CheckPing.Value)
        {
            string line = "";

            if (Plugin.Instance.CheckFps.Value)
            {
                int fps = rig.fps;
                line += $"<color={TagUtils.Instance.FpsColor(fps)}>{fps}</color>";
            }

            if (Plugin.Instance.CheckPing.Value)
            {
                int ping = rig.ping();

                if (Plugin.Instance.CheckFps.Value)
                    line += " <color=white>|</color> ";

                line += $"<color={TagUtils.Instance.PingColor(ping)}>{ping}</color>";
            }

            stringBuilder.Append(line + "\n");
        }

        string platformTag = Plugin.Instance.CheckPlatform.Value && !Plugin.Instance.UsePlatIcons.Value ? TagUtils.Instance.PlatformTag(rig) : string.Empty;

        string cosmeticsTag = Plugin.Instance.CheckCosmetics.Value ? TagUtils.Instance.SpecialCosmeticsTag(rig) : string.Empty;

        if (Plugin.Instance.CheckPlatform.Value && !Plugin.Instance.UsePlatIcons.Value && Plugin.Instance.CheckCosmetics.Value)
        {
            if (!string.IsNullOrEmpty(platformTag) || !string.IsNullOrEmpty(cosmeticsTag))
                stringBuilder.Append($"<color=white>{platformTag}{cosmeticsTag}</color>\n");
        }
        else if (Plugin.Instance.CheckCosmetics.Value && !string.IsNullOrEmpty(cosmeticsTag))
        {
            stringBuilder.Append($"<color=white>{cosmeticsTag}</color>\n");
        }
        else if (Plugin.Instance.CheckPlatform.Value && !Plugin.Instance.UsePlatIcons.Value && !string.IsNullOrEmpty(platformTag))
        {
            stringBuilder.Append($"<color=white>{platformTag}</color>\n");
        }

        string SanitizePlayerName(string unsanitizedPlrName)
            => string.IsNullOrEmpty(unsanitizedPlrName) ? "" : Regex.Replace(unsanitizedPlrName, "<.*?>", string.Empty);

        string plrName = rig.Creator.NickName;
        plrName = SanitizePlayerName(plrName);

        string displayName = plrName.Length > 12 ? plrName.Substring(0, 12) + "..." : plrName;

        if (Plugin.Instance.TextFormatScopeConfig.Value == Plugin.TextFormatScope.NameOnly)
            displayName = Plugin.Instance.TextFormat(displayName);

        stringBuilder.AppendLine(displayName);

        return FinalizeFormat(stringBuilder.ToString());
    }

    private string FinalizeFormat(string text) => Plugin.Instance.TextFormatScopeConfig.Value == Plugin.TextFormatScope.AllText ? Plugin.Instance.TextFormat(text) : text;

    private void UpdateTextColor(VRRig rig, TextMeshPro txt)
    {
        Color clr = PlayerColor(rig);
        txt.color = clr;
    }

    private Color PlayerColor(VRRig rig)
    {
        if (Plugin.Instance.Gfriends.Value && rig.Creator != null)
        {
            if (GFriendUtils.Verified(rig.Creator))
                return GFriends.m_clrVerified;

            if (GFriendUtils.Friend(rig.Creator))
                return GFriends.m_clrFriend;

            if (GFriendUtils.RecentlyPlayedWith(rig.Creator))
                return GFriends.m_clrPlayedRecently;
        }

        //Paintbrawl Eliminated
        if (rig.mainSkin.material.name.Contains("paintsplatterneutral"))
            return new Color(1f, 1f, 1f);

        //Paintbrawl Unattackable (after balloon is popped)
        if (rig.mainSkin.material.name.Contains("neutralstunned"))
            return new Color(.478f, .247f, 0f);

        //Rock Monke
        if (rig.mainSkin.material.name.Contains("It"))
            return new Color(.459f, .027f, 0f);

        //Lava Monke
        return rig.mainSkin.material.name.Contains("fected") ? new Color(1f, 0.5f, 0.102f) : rig.playerColor;
    }

    private void UpdateOutlines(NametagData data)
    {
        CleanupOutlines(data);

        if (!Plugin.Instance.OutlineEnabled.Value || data.MainText == null)
            return;

        ApplyOutlines(data.MainText, Plugin.Instance.OutlineThickness.Value, Plugin.Instance.OutlineColor.Value,
                Plugin.Instance.OutlineQuality.Value);
    }

    private void CleanupOutlines(NametagData data)
    {
        if (data == null || data.MainText == null)
            return;

        Material currentMat = data.MainText.fontMaterial;
        Material sharedMat  = data.MainText.fontSharedMaterial;

        if (currentMat != null && sharedMat != null && currentMat != sharedMat)
        {
            data.MainText.fontMaterial = sharedMat;

            try
            {
                Destroy(currentMat);
            }
            catch
            {
                // ignored
            }
        }
    }

    private void ApplyOutlines(TextMeshPro txt, float thickness, Color color, bool highQual)
    {
        if (txt == null) return;

        Material __base = txt.fontSharedMaterial;

        if (__base == null) return;

        Material __instance = txt.fontMaterial;
        if (__instance == null || __instance == __base)
        {
            __instance = new Material(__base)
            {
                    name = __base.name + " (Instance)",
            };

            txt.fontMaterial = __instance;
        }

        __instance.SetFloat(ShaderUtilities.ID_OutlineWidth, thickness);
        __instance.SetColor(ShaderUtilities.ID_OutlineColor, color);
    
        float softness = highQual ? 0.35f : 0f;
        try
        {
            __instance.SetFloat(ShaderUtilities.ID_OutlineSoftness, softness);
        }
        catch
        {
            // ignored
        }

        if (!(softness > 0f)) return;

        try
        {
            float dilate = Mathf.Clamp(thickness * 0.5f, 0f, 0.2f);
            __instance.SetFloat(ShaderUtilities.ID_FaceDilate, dilate);
        }
        catch
        {
            // ignored
        }
    }
}