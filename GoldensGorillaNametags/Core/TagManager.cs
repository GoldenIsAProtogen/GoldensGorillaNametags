using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using GoldensGorillaNametags.Utils;
using TMPro;
using UnityEngine;
using GFriends = GorillaFriends.Main;

namespace GoldensGorillaNametags.Core;

public class TagManager : MonoBehaviour
{
#region ===== - Fields & Init - =====

    private const           float      TagUpdateTime = 0.3f;
    public static           TagManager Instance;
    private static readonly Vector3    BaseScale = Vector3.one * 0.8f;

    private readonly Dictionary<VRRig, float>       lastTagUpdate = new();
    private readonly Dictionary<VRRig, NametagData> tagMap        = new();

    private readonly List<VRRig> _rigsToRemoveBuffer = new();

    private static readonly Regex NameTagStripRegex = new(@"<.*?>", RegexOptions.Compiled);

    private void Awake() => Instance = this;

#endregion

#region ===== - Tag Lifecycle - =====

    public void CleanupTags(HashSet<VRRig> validRigs)
    {
        _rigsToRemoveBuffer.Clear();

        foreach (KeyValuePair<VRRig, NametagData> kv in tagMap)
        {
            VRRig rig = kv.Key;

            if (rig == null || !validRigs.Contains(rig) || rig.isOfflineVRRig || rig.Creator == null)
                _rigsToRemoveBuffer.Add(rig);
        }

        foreach (VRRig rig in _rigsToRemoveBuffer)
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
            lastTagUpdate.Remove(rig);
        }
    }

    public void CreateTagmap(HashSet<VRRig> validRigs)
    {
        foreach (VRRig rig in validRigs)
        {
            if (rig == null || rig.isOfflineVRRig || rig.Creator == null)
                continue;

            if (tagMap.ContainsKey(rig))
                continue;

            tagMap[rig] = CreateTags(rig);
        }
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
        data.PlatformIconObj.transform.SetParent(mainTxtGo.transform, false);
        data.PlatformIconObj.transform.localPosition = Vector3.zero;
        data.PlatformIconObj.transform.localScale    = Vector3.one * (Plugin.Instance.IconSize.Value * Plugin.Instance.TagSize.Value);

        data.PlatformIconRenderer              = data.PlatformIconObj.AddComponent<SpriteRenderer>();
        data.PlatformIconRenderer.sortingOrder = 10;
        data.PlatformIconRenderer.gameObject.SetActive(false);

        data.ImageUpdateCoroutine = StartCoroutine(TagUtils.Instance.UpdatePlatformIconCoroutine(rig, data));

        return data;
    }

    private void TagTextSpecifications(TextMeshPro text)
    {
        text.alignment        = TextAlignmentOptions.Center;
        text.fontSize         = Plugin.Instance.TagSize.Value;
        text.font             = Plugin.Instance.Font;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.richText         = true;

        if (text.fontSharedMaterial != null)
        {
            text.fontSharedMaterial.DisableKeyword("OUTLINE_ON");
            text.fontMaterial = text.fontSharedMaterial;
        }
    }

    public void ClearTags()
    {
        foreach (KeyValuePair<VRRig, NametagData> kevVal in tagMap)
        {
            VRRig       rig  = kevVal.Key;
            NametagData data = kevVal.Value;

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
        lastTagUpdate.Clear();
    }

#endregion

#region ===== - Runtime - ====

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

            if (!lastTagUpdate.TryGetValue(rig, out float lastUpdate) || currentTime - lastUpdate >= TagUpdateTime)
            {
                UpdateTagContent(rig, data);
                lastTagUpdate[rig] = currentTime;
            }

            UpdatePlatformIcon(data);
        }
    }

    private void Cam(Transform tagTransform)
    {
        if (tagTransform == null) return;

        Transform cameraTransform = Plugin.Instance.CineCam != null ? Plugin.Instance.CineCam.transform : Plugin.Instance.MainCam;

        if (cameraTransform == null) return;

        tagTransform.LookAt(cameraTransform.position);
        tagTransform.Rotate(0f, 180f, 0f);

        int childCount = tagTransform.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform child = tagTransform.GetChild(i);
            if (child.name != "PlatformIcon")
                child.localRotation = Quaternion.identity;
        }
    }

    private void UpdateTagContent(VRRig rig, NametagData data)
    {
        data.Container.transform.localPosition = new Vector3(0f, Plugin.Instance.TagHeight.Value, 0f);

        if (!Mathf.Approximately(data.MainText.fontSize, Plugin.Instance.TagSize.Value))
            data.MainText.fontSize = Plugin.Instance.TagSize.Value;

        string text = CreateTagText(rig);

        bool textChanged = data.LastText != text;

        if (textChanged)
        {
            data.MainText.text = text;
            data.LastText      = text;
            UpdateTextColor(rig, data.MainText);
        }

        UpdateOutlines(data);

        if (textChanged && Plugin.Instance.UsePlatIcons.Value)
            UpdateIconPosition(data);
    }

#endregion

#region ===== - Icons - =====

    private void UpdatePlatformIcon(NametagData data)
    {
        if (data.PlatformIconRenderer == null)
            return;

        bool shouldBeVisible = Plugin.Instance.UsePlatIcons.Value && data.CurrentPlatformTex != null;

        if (shouldBeVisible)
        {
            UpdateIconPosition(data);
            data.PlatformIconRenderer.gameObject.SetActive(true);
        }
        else
        {
            data.PlatformIconRenderer.gameObject.SetActive(false);
        }
    }

    private void UpdateIconPosition(NametagData data)
    {
        if (data.PlatformIconObj == null) return;

        float yOffset    = 0f;
        float lineHeight = Plugin.Instance.TagSize.Value * 1.2f;

        if (Plugin.Instance.CheckSpecial.Value)
            yOffset -= lineHeight * 0.017f;

        if (Plugin.Instance.CheckFps.Value || Plugin.Instance.CheckPing.Value)
            yOffset -= lineHeight * 0.01f;

        if (Plugin.Instance.CheckPlatform.Value && !Plugin.Instance.UsePlatIcons.Value ||
            Plugin.Instance.CheckCosmetics.Value)
            yOffset -= lineHeight * 0.01f;

        string location = Plugin.Instance.IconLocation.Value.ToLower();
        float  iconSize = Plugin.Instance.IconSize.Value * Plugin.Instance.TagSize.Value;
        float  xOffset  = 0f;
        float  padding  = iconSize * 0.05f;

        float nameWidth = 12f * Plugin.Instance.TagSize.Value * 0.12f;

        if (location == "left")
            xOffset = -(nameWidth * 0.5f) - iconSize * 0.5f - padding;
        else // right
            xOffset = nameWidth * 0.5f + iconSize * 0.5f + padding;

        data.PlatformIconObj.transform.localPosition = new Vector3(xOffset, yOffset, -0.01f);

        data.PlatformIconObj.transform.localScale = Vector3.one * iconSize;
    }

#endregion

#region ===== - Tag Content - =====

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
            if (Plugin.Instance.CheckFps.Value)
            {
                int fps = Plugin.Instance.EstimateFPS.Value ? rig.fps : rig.rigContainer.m_playerStats.FPS;
                stringBuilder.Append("<color=").Append(TagUtils.Instance.FpsColor(fps)).Append('>').Append(fps).Append("</color>");
            }

            if (Plugin.Instance.CheckPing.Value)
            {
                int ping = Plugin.Instance.EstimatePing.Value ? rig.ping() : rig.rigContainer.m_playerStats.Ping;

                if (Plugin.Instance.CheckFps.Value)
                    stringBuilder.Append(" <color=white>|</color> ");

                stringBuilder.Append("<color=").Append(TagUtils.Instance.PingColor(ping)).Append('>').Append(ping).Append("</color>");
            }

            stringBuilder.Append('\n');
        }

        string platformTag  = Plugin.Instance.CheckPlatform.Value && !Plugin.Instance.UsePlatIcons.Value ? TagUtils.Instance.PlatformTag(rig) : string.Empty;
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

        string SanitizePlayerName(string unsanitizedPlayerName)
            => string.IsNullOrEmpty(unsanitizedPlayerName) ? "" : NameTagStripRegex.Replace(unsanitizedPlayerName, string.Empty);

        string playerName = rig.Creator.NickName;
        playerName = SanitizePlayerName(playerName);

        string displayName = playerName.Length > 12 ? playerName.Substring(0, 12) + "..." : playerName;

        if (Plugin.Instance.TextFormatScopeConfig.Value == Plugin.TextFormatScope.NameOnly)
            displayName = Plugin.Instance.TextFormat(displayName);

        stringBuilder.AppendLine(displayName);

        if (!Plugin.Instance.CheckMods.Value)
            return stringBuilder.ToString();

        string modTag = TagUtils.Instance.PlayerModsTag(rig);
        if (!string.IsNullOrEmpty(modTag))
            stringBuilder.Append($"<color=white><size=70%>{modTag}</size></color>");

        return FinalizeFormat(stringBuilder.ToString());
    }

    private string FinalizeFormat(string text)
        => Plugin.Instance.TextFormatScopeConfig.Value == Plugin.TextFormatScope.AllText ? Plugin.Instance.TextFormat(text) : text;

    private void UpdateTextColor(VRRig rig, TextMeshPro text)
    {
        Color color = PlayerColor(rig);
        text.color = color;
    }

    private Color PlayerColor(VRRig rig)
    {
        if (Plugin.Instance.GorillaFriends.Value && rig.Creator != null)
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
        if (data?.MainText == null) return;

        CleanupOutlines(data);

        if (!Plugin.Instance.OutlineEnabled.Value) return;

        ApplyOutlines(data.MainText, Plugin.Instance.OutlineThickness.Value, Plugin.Instance.OutlineColor.Value);
    }

    private void CleanupOutlines(NametagData data)
    {
        if (data?.MainText == null) return;

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
                /* ignored */
            }
        }
    }

    private void ApplyOutlines(TextMeshPro txt, float thickness, Color color)
    {
        if (txt == null) return;

        Material baseMat = txt.fontSharedMaterial;

        if (baseMat == null) return;

        Material instanceMat = txt.fontMaterial;

        if (instanceMat == null || instanceMat == baseMat)
        {
            instanceMat = new Material(baseMat)
            {
                    name      = baseMat.name + " (Outlined)",
                    hideFlags = HideFlags.HideAndDontSave,
            };

            txt.fontMaterial = instanceMat;
        }

        instanceMat.SetFloat(ShaderUtilities.ID_OutlineWidth, thickness);
        instanceMat.SetColor(ShaderUtilities.ID_OutlineColor, color);
        instanceMat.SetFloat(ShaderUtilities.ID_OutlineSoftness, Plugin.Instance.OutlineSoftness.Value);

        float dilate = Mathf.Clamp(thickness * 0.5f, 0f, 0.2f);
        instanceMat.SetFloat(ShaderUtilities.ID_FaceDilate, dilate);
        instanceMat.EnableKeyword("OUTLINE_ON");

        txt.UpdateMeshPadding();
        txt.SetAllDirty();
    }

#endregion
}