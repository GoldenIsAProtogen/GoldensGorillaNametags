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
    private static readonly Vector3    BaseScale         = Vector3.one * 0.8f;
    private static readonly Regex      NameTagStripRegex = new(@"<.*?>", RegexOptions.Compiled);

    private readonly Dictionary<VRRig, float>       lastTagUpdate = new();
    private readonly Dictionary<VRRig, NametagData> tagMap        = new();
    private readonly List<VRRig>                    removeBuffer  = new();

    private void Awake() => Instance = this;

    private static bool IsInvalid(VRRig rig) => rig == null || rig.isOfflineVRRig || rig.Creator == null;

#endregion

#region ===== - Tag Lifecycle - =====

    public void CleanupTags(HashSet<VRRig> validRigs)
    {
        removeBuffer.Clear();

        foreach (VRRig rig in tagMap.Keys)
            if (IsInvalid(rig) || !validRigs.Contains(rig))
                removeBuffer.Add(rig);

        foreach (VRRig rig in removeBuffer)
        {
            if (tagMap.Remove(rig, out NametagData data))
                DestroyTag(data);

            lastTagUpdate.Remove(rig);
        }
    }

    public void CreateTagmap(HashSet<VRRig> validRigs)
    {
        foreach (VRRig rig in validRigs)
            if (!IsInvalid(rig) && !tagMap.ContainsKey(rig))
                tagMap[rig] = CreateTags(rig);
    }

    private NametagData CreateTags(VRRig rig)
    {
        Plugin p = Plugin.Instance;

        GameObject container = new("NametagContainer");
        container.transform.SetParent(rig.transform, false);
        container.transform.localScale    = BaseScale;
        container.transform.localPosition = new Vector3(0f, p.TagHeight.Value, 0f);

        GameObject mainGo = new("NametagMain");
        mainGo.transform.SetParent(container.transform, false);
        mainGo.transform.localScale = BaseScale;

        TextMeshPro text = mainGo.AddComponent<TextMeshPro>();
        text.alignment        = TextAlignmentOptions.Center;
        text.fontSize         = p.TagSize.Value;
        text.font             = p.Font;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.richText         = true;

        GameObject iconGo = new("PlatformIcon");
        iconGo.transform.SetParent(mainGo.transform, false);

        SpriteRenderer iconRenderer = iconGo.AddComponent<SpriteRenderer>();
        iconRenderer.sortingOrder = 10;
        iconGo.SetActive(false);

        NametagData data = new()
        {
                Container            = container,
                MainText             = text,
                PlatformIconObj      = iconGo,
                PlatformIconRenderer = iconRenderer,
        };

        data.ImageUpdateCoroutine = StartCoroutine(TagUtils.Instance.UpdatePlatformIconCoroutine(rig, data));

        return data;
    }

    public void ClearTags()
    {
        foreach (NametagData data in tagMap.Values)
            DestroyTag(data);

        tagMap.Clear();
        lastTagUpdate.Clear();
    }

    private void DestroyTag(NametagData data)
    {
        if (data == null) return;

        if (data.ImageUpdateCoroutine != null)
            StopCoroutine(data.ImageUpdateCoroutine);

        if (data.Container != null)
            Destroy(data.Container);
    }

#endregion

#region ===== - Runtime - ====

    public void UpdateTags()
    {
        float     time = Time.time;
        Transform cam  = Plugin.Instance.CineCam != null ? Plugin.Instance.CineCam.transform : Plugin.Instance.MainCam;

        foreach (KeyValuePair<VRRig, NametagData> kv in tagMap)
        {
            VRRig       rig  = kv.Key;
            NametagData data = kv.Value;

            if (IsInvalid(rig) || data?.Container == null)
                continue;

            if (cam != null)
            {
                Transform tag = data.Container.transform;
                tag.rotation = Quaternion.LookRotation(tag.position - cam.position);
            }

            if (!lastTagUpdate.TryGetValue(rig, out float last) || time - last >= TagUpdateTime)
            {
                UpdateTagContent(rig, data);
                lastTagUpdate[rig] = time;
            }

            UpdatePlatformIcon(data);
        }
    }

    private void UpdateTagContent(VRRig rig, NametagData data)
    {
        Plugin p = Plugin.Instance;

        data.Container.transform.localPosition = new Vector3(0f, p.TagHeight.Value, 0f);
        data.MainText.fontSize                 = p.TagSize.Value;

        string text = CreateTagText(rig);

        if (data.LastText != text)
        {
            data.MainText.text  = text;
            data.LastText       = text;
            data.MainText.color = PlayerColor(rig);
        }

        UpdateOutline(data.MainText);
    }

#endregion

#region ===== - Icons - =====

    private void UpdatePlatformIcon(NametagData data)
    {
        if (data.PlatformIconRenderer == null) return;

        Plugin p       = Plugin.Instance;
        bool   visible = p.UsePlatIcons.Value && data.CurrentPlatformTex != null;

        data.PlatformIconRenderer.gameObject.SetActive(visible);

        if (!visible) return;

        float tagSize  = p.TagSize.Value;
        float iconSize = p.IconSize.Value * tagSize;

        float y = 0f;
        if (p.CheckSpecial.Value)                  y -= 0.017f;
        if (p.CheckFps.Value || p.CheckPing.Value) y -= 0.01f;
        if (p.CheckCosmetics.Value)                y -= 0.01f;
        y *= tagSize * 1.2f;

        float x = 0.72f * tagSize + iconSize * 0.55f;
        if (p.IconLocation.Value.ToLower() == "left")
            x = -x;

        Transform icon = data.PlatformIconRenderer.transform;
        icon.localPosition = new Vector3(x, y, -0.01f);
        icon.localScale    = Vector3.one * iconSize;
    }

#endregion

#region ===== - Tag Content - =====

    private string CreateTagText(VRRig rig)
    {
        Plugin   p     = Plugin.Instance;
        TagUtils utils = TagUtils.Instance;

        StringBuilder sb = new(128);

        if (p.CheckSpecial.Value)
        {
            string special = utils.SpecialPlayerTag(rig);
            if (!string.IsNullOrEmpty(special))
                sb.AppendLine($"<size=70%>{special}</size>");
        }

        if (p.CheckFps.Value || p.CheckPing.Value)
        {
            if (p.CheckFps.Value)
            {
                int fps = p.EstimateFPS.Value ? rig.fps : rig.rigContainer.m_playerStats.FPS;
                sb.Append($"<color={utils.FpsColor(fps)}>{fps}</color>");
            }

            if (p.CheckPing.Value)
            {
                int ping = p.EstimatePing.Value ? rig.ping() : rig.rigContainer.m_playerStats.Ping;

                if (p.CheckFps.Value)
                    sb.Append(" <color=white>|</color> ");

                sb.Append($"<color={utils.PingColor(ping)}>{ping}</color>");
            }

            sb.Append('\n');
        }

        string extras = (p.CheckPlatform.Value && !p.UsePlatIcons.Value ? utils.PlatformTag(rig) : "")
                      + (p.CheckCosmetics.Value ? utils.SpecialCosmeticsTag(rig) : "");

        if (!string.IsNullOrEmpty(extras))
            sb.Append($"<color=white>{extras}</color>\n");

        string name = NameTagStripRegex.Replace(rig.Creator.NickName ?? string.Empty, string.Empty);

        if (name.Length > 12)
            name = name.Substring(0, 12) + "...";

        if (p.TextFormatScopeConfig.Value == Plugin.TextFormatScope.NameOnly)
            name = p.TextFormat(name);

        sb.AppendLine(name);

        if (p.CheckMods.Value)
        {
            string mods = utils.PlayerModsTag(rig);
            if (!string.IsNullOrEmpty(mods))
                sb.Append($"<color=white><size=70%>{mods}</size></color>");
        }

        string result = sb.ToString();
        return p.TextFormatScopeConfig.Value == Plugin.TextFormatScope.AllText ? p.TextFormat(result) : result;
    }

    private Color PlayerColor(VRRig rig)
    {
        if (Plugin.Instance.GorillaFriends.Value)
        {
            if (GFriendUtils.Verified(rig.Creator))
                return GFriends.m_clrVerified;

            if (GFriendUtils.Friend(rig.Creator))
                return GFriends.m_clrFriend;

            if (GFriendUtils.RecentlyPlayedWith(rig.Creator))
                return GFriends.m_clrPlayedRecently;
        }

        string mat = rig.mainSkin.material.name;

        //Paintbrawl Eliminated
        if (mat.Contains("paintsplatterneutral"))
            return Color.white;

        //Paintbrawl Unattackable (after balloon is popped)
        if (mat.Contains("neutralstunned"))
            return new Color(.478f, .247f, 0f);

        //Rock Monke
        if (mat.Contains("It"))
            return new Color(.459f, .027f, 0f);

        //Lava Monke
        return mat.Contains("fected") ? new Color(1f, 0.5f, 0.102f) : rig.playerColor;
    }

    private void UpdateOutline(TextMeshPro text)
    {
        Plugin   p   = Plugin.Instance;
        Material mat = text.fontMaterial;

        if (p.OutlineEnabled.Value)
        {
            float thickness = p.OutlineThickness.Value;

            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, thickness);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, p.OutlineColor.Value);
            mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, p.OutlineSoftness.Value);
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, Mathf.Clamp(thickness * 0.5f, 0f, 0.2f));
            mat.EnableKeyword("OUTLINE_ON");
        }
        else
        {
            mat.DisableKeyword("OUTLINE_ON");
        }

        text.UpdateMeshPadding();
        text.SetAllDirty();
    }

#endregion
}