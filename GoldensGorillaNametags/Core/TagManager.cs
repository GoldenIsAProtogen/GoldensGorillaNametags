using System.Collections.Generic;
using System.Linq;
using System.Text;
using GoldensGorillaNametags.Utils;
using TMPro;
using UnityEngine;
using GFriends = GorillaFriends.Main;

namespace GoldensGorillaNametags.Core;

public class TagManager : MonoBehaviour
{
    private const           float                    TagUpdateTime = 0.3f;
    public static           TagManager               Instance;
    private static readonly Vector3                  BaseScale  = Vector3.one * 0.8f;
    private static readonly Vector3                  ImgBasePos = new(0f, 0.85f, 0f);
    private readonly        Dictionary<VRRig, float> lastTagUpd = new();

    private readonly Dictionary<VRRig, NametagData> tagMap = new();

    private void Awake() => Instance = this;

    public void CleanupTags(HashSet<VRRig> validRigs)
    {
        List<VRRig> rigsToRemove = tagMap.Where(kv =>
                                                        kv.Key == null              ||
                                                        !validRigs.Contains(kv.Key) ||
                                                        kv.Key.isOfflineVRRig       ||
                                                        kv.Key.OwningNetPlayer == null).Select(kv => kv.Key)
                                         .ToList();

        foreach (VRRig r in rigsToRemove)
        {
            if (tagMap.TryGetValue(r, out NametagData data))
            {
                if (data.ImgUpdCoroutine != null)
                    StopCoroutine(data.ImgUpdCoroutine);

                CleanupOutline(data);
                if (data.Container != null)
                    Destroy(data.Container);
            }

            tagMap.Remove(r);
            lastTagUpd.Remove(r);
        }
    }

    public void CreateTags(HashSet<VRRig> validRigs)
    {
        foreach (VRRig r in validRigs.Where(r => r != null && !r.isOfflineVRRig && r.OwningNetPlayer != null)
                                     .Where(r => !tagMap.ContainsKey(r)))
            tagMap[r] = CreateTags(r);
    }

    private NametagData CreateTags(VRRig r)
    {
        NametagData data = new();

        data.Container = new GameObject("NametagContainer");
        data.Container.transform.SetParent(r.transform, false);
        data.Container.transform.localScale    = BaseScale;
        data.Container.transform.localPosition = new Vector3(0f, Plugin.Instance.TagHeight.Value, 0f);

        GameObject mainTxtGo = new("NametagMain");
        mainTxtGo.transform.SetParent(data.Container.transform, false);
        mainTxtGo.transform.localPosition = Vector3.zero;
        mainTxtGo.transform.localScale    = new Vector3(0.8f, 0.8f, 0.8f);

        data.MainTxt = mainTxtGo.AddComponent<TextMeshPro>();
        TagTxt(data.MainTxt);

        data.PlatIconObj = new GameObject("PlatformIcon");
        data.PlatIconObj.transform.SetParent(data.Container.transform, false);
        data.PlatIconObj.transform.localPosition = IconPos(Plugin.Instance.IconLocation.Value);
        data.PlatIconObj.transform.localScale = new Vector3(Plugin.Instance.IconSize.Value,
                Plugin.Instance.IconSize.Value, Plugin.Instance.IconSize.Value);

        data.PlatIconRenderer              = data.PlatIconObj.AddComponent<SpriteRenderer>();
        data.PlatIconRenderer.sortingOrder = 10;
        data.PlatIconRenderer.gameObject.SetActive(false);

        data.ImgUpdCoroutine = StartCoroutine(TagUtils.Instance.UpdPlatIconCoroutine(r, data));

        return data;
    }

    private Vector3 IconPos(string location)
    {
        return location.ToLower() switch
               {
                       "left" => new Vector3(Plugin.Instance.TagSize.Value * -0.5f,
                               Plugin.Instance.TagHeight.Value - .85f, 0f),
                       "right" => new Vector3(Plugin.Instance.TagSize.Value * 0.5f,
                               Plugin.Instance.TagHeight.Value - .85f, 0f),
                       "top"    => new Vector3(0f, Plugin.Instance.TagHeight.Value * 0.85f, 0f),
                       "bottom" => new Vector3(0f, Plugin.Instance.TagHeight.Value - 1f,    0f),
                       var _    => ImgBasePos,
               };
    }

    private void TagTxt(TextMeshPro txt)
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
            VRRig       r    = kv.Key;
            NametagData data = kv.Value;

            if (r == null || data?.Container == null || r.isOfflineVRRig || r.OwningNetPlayer == null)
                continue;

            Cam(data.Container.transform);

            if (!lastTagUpd.ContainsKey(r) || currentTime - lastTagUpd[r] >= TagUpdateTime)
            {
                UpdTagContent(r, data);
                lastTagUpd[r] = currentTime;
            }

            UpdPlatIcon(data);
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

    private void UpdPlatIcon(NametagData data)
    {
        if (data.PlatIconRenderer == null)
            return;

        bool shouldBeVisible = Plugin.Instance.UsePlatIcons.Value && data.CurrentPlatTex != null;
        data.PlatIconRenderer.gameObject.SetActive(shouldBeVisible);
    }

    private void UpdTagContent(VRRig r, NametagData data)
    {
        data.Container.transform.localPosition = new Vector3(0f, Plugin.Instance.TagHeight.Value, 0f);

        if (!Mathf.Approximately(data.MainTxt.fontSize, Plugin.Instance.TagSize.Value))
            data.MainTxt.fontSize = Plugin.Instance.TagSize.Value;

        string txt = CreateTagTxt(r);

        if (data.LastTxt == txt)
            return;

        data.MainTxt.text = txt;
        data.LastTxt      = txt;
        UpdTxtClr(r, data.MainTxt);
        UpdOutline(data);
    }

    private string CreateTagTxt(VRRig rig)
    {
        StringBuilder stringBuilder = new(128);

        if (Plugin.Instance.CheckSpecial.Value)
        {
            string specialTag = TagUtils.Instance.SpecialTag(rig);
            if (!string.IsNullOrEmpty(specialTag))
                stringBuilder.AppendLine(specialTag);
        }

        if (Plugin.Instance.CheckFps.Value || Plugin.Instance.CheckPing.Value)
        {
            string line = "";

            if (Plugin.Instance.CheckFps.Value)
            {
                int fps  = rig.fps;
                line += $"<color={TagUtils.Instance.FpsClr(fps)}>{fps}</color>";
            }

            if (Plugin.Instance.CheckPing.Value)
            {
                int    ping      = rig.ping();

                string pingText  = ping == int.MaxValue ? "N/A" : ping.ToString();
                string pingColor = ping == int.MaxValue ? "#AB0080" : TagUtils.Instance.PingClr(ping);

                if (Plugin.Instance.CheckFps.Value)
                    line += " <color=white>|</color> ";

                line += $"<color={pingColor}>{pingText}</color>";
            }

            stringBuilder.Append(line + "\n");
        }

        string platformTag = Plugin.Instance.CheckPlat.Value && !Plugin.Instance.UsePlatIcons.Value
                                     ? TagUtils.Instance.PlatTag(rig)
                                     : "";

        string cosmeticsTag = Plugin.Instance.CheckCosmetics.Value ? TagUtils.Instance.CosmeticTag(rig) : "";

        if (Plugin.Instance.CheckPlat.Value && !Plugin.Instance.UsePlatIcons.Value &&
            Plugin.Instance.CheckCosmetics.Value)
        {
            if (!string.IsNullOrEmpty(platformTag) || !string.IsNullOrEmpty(cosmeticsTag))
                stringBuilder.Append($"<color=white>{platformTag}{cosmeticsTag}</color>\n");
        }
        else if (Plugin.Instance.CheckCosmetics.Value && !string.IsNullOrEmpty(cosmeticsTag))
        {
            stringBuilder.Append($"<color=white>{cosmeticsTag}</color>\n");
        }
        else if (Plugin.Instance.CheckPlat.Value && !Plugin.Instance.UsePlatIcons.Value &&
                 !string.IsNullOrEmpty(platformTag))
        {
            stringBuilder.Append($"<color=white>{platformTag}</color>\n");
        }

        string plrName = rig.OwningNetPlayer.NickName;
        stringBuilder.AppendLine(plrName.Length > 12 ? plrName.Substring(0, 12) + "..." : plrName);

        return stringBuilder.ToString();
    }

    private void UpdTxtClr(VRRig r, TextMeshPro txt)
    {
        Color clr = PlrClr(r);
        txt.color = clr;
    }

    private Color PlrClr(VRRig r)
    {
        if (Plugin.Instance.Gf.Value && r.OwningNetPlayer != null)
        {
            if (GFriendUtils.Verified(r.OwningNetPlayer))
                return GFriends.m_clrVerified;

            if (GFriendUtils.Friend(r.OwningNetPlayer))
                return GFriends.m_clrFriend;

            if (GFriendUtils.RecentlyPlayedWith(r.OwningNetPlayer))
                return GFriends.m_clrPlayedRecently;
        }

        if (r.mainSkin.material.name.Contains("It"))
            return new Color(1f, 0f, 0f);

        return r.mainSkin.material.name.Contains("fected") ? new Color(1f, 0.5f, 0f) : r.playerColor;
    }

    private void UpdOutline(NametagData data)
    {
        CleanupOutline(data);

        if (!Plugin.Instance.OutlineEnabled.Value || data.MainTxt == null)
            return;

        CreateOutlineClones(data);
    }

    private void CleanupOutline(NametagData data)
    {
        if (data.OutlineClones == null)
            return;

        foreach (TextMeshPro outline in data.OutlineClones.Where(outline => outline            != null &&
                                                                            outline.gameObject != null))
            Destroy(outline.gameObject);

        data.OutlineClones.Clear();
    }

    private void CreateOutlineClones(NametagData data)
    {
        float thickness = Plugin.Instance.OutlineThick.Value;
        Vector3[] offsets = Plugin.Instance.OutlineQual.Value
                                    ? CreateHighQualOutline(thickness)
                                    : CreateOutline(thickness);

        string plainTxt = StripClrTags(data.MainTxt.text);

        foreach (Vector3 offset in offsets)
        {
            TextMeshPro outline = CreateOutlineClone(data.MainTxt, offset, plainTxt);
            data.OutlineClones.Add(outline);
        }
    }

    private Vector3[] CreateOutline(float thickness) => new[]
    {
            new Vector3(0f,        thickness, 0f), new Vector3(0f,         -thickness, 0f),
            new Vector3(thickness, 0f,        0f), new Vector3(-thickness, 0f,         0f),
    };

    private Vector3[] CreateHighQualOutline(float thickness) => new[]
    {
            new Vector3(0f,        thickness,  0f), new Vector3(0f,         -thickness, 0f),
            new Vector3(thickness, 0f,         0f), new Vector3(-thickness, 0f,         0f),
            new Vector3(thickness, thickness,  0f), new Vector3(-thickness, thickness,  0f),
            new Vector3(thickness, -thickness, 0f), new Vector3(-thickness, -thickness, 0f),
    };

    private TextMeshPro CreateOutlineClone(TextMeshPro original, Vector3 offset, string txt)
    {
        TextMeshPro clone = Instantiate(original, original.transform.parent);
        clone.text                    = txt;
        clone.transform.localPosition = original.transform.localPosition + offset;
        clone.transform.localRotation = original.transform.localRotation;
        clone.transform.localScale    = original.transform.localScale;
        clone.color                   = Plugin.Instance.OutlineClr.Value;
        clone.sortingOrder            = original.sortingOrder - 1;

        CanvasRenderer canvasRenderer = clone.GetComponent<CanvasRenderer>();
        if (canvasRenderer != null)
            canvasRenderer.cull = false;

        return clone;
    }

    private string StripClrTags(string txt) => Plugin.Instance.ClrTagRegex.Replace(txt, "");
}