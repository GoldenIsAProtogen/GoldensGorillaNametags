using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;

namespace GoldensGorillaNametags
{
    [BepInPlugin(Constants.Guid, Constants.Name, Constants.Version)]
    public class Main : BaseUnityPlugin
    {
        #region Start Stuff
        private TMP_FontAsset font;
        private Camera cineCam;
        private Transform mainCam;

        private ConfigEntry<float> tagSize;
        private ConfigEntry<float> tagHeight;
        private ConfigEntry<bool> outlineEnabled;
        private ConfigEntry<bool> outlineQuality;
        private ConfigEntry<Color> outlineColor;
        private ConfigEntry<float> outlineThick;
        private ConfigEntry<bool> chkMods;
        private ConfigEntry<bool> chkPlat;
        private ConfigEntry<bool> chkSpecial;
        private ConfigEntry<bool> chkFps;
        private ConfigEntry<bool> chkCos;

        private readonly Dictionary<VRRig, GameObject> tagMap = new Dictionary<VRRig, GameObject>();
        private readonly Dictionary<TextMeshPro, List<TextMeshPro>> outlineMap = new Dictionary<TextMeshPro, List<TextMeshPro>>();
        private Dictionary<string, string> specialCache;
        private Dictionary<string, string> modsCache;
        private float lastCache;
        private const float cacheInterval = 300f; // 5 min

        internal void Start()
        {
            tagSize = Config.Bind("Tags", "Size", 1f, "Nametag size");
            tagHeight = Config.Bind("Tags", "Height", 0.65f, "Nametag height");

            outlineEnabled = Config.Bind("Outlines", "Enabled", true, "Tag outlines");
            outlineQuality = Config.Bind("Outlines", "Quality", false, "Tag Quality (Can be laggy)");
            outlineColor = Config.Bind("Outlines", "Color", Color.black, "Outline color");
            outlineThick = Config.Bind("Outlines", "Thickness", 0.0015f, "Outline thickness");

            chkMods = Config.Bind("Checks", "Mods", true, "Check mods");
            chkPlat = Config.Bind("Checks", "Platform", true, "Check platform");
            chkSpecial = Config.Bind("Checks", "Special", true, "Check special players");
            chkFps = Config.Bind("Checks", "FPS", true, "Check FPS");
            chkCos = Config.Bind("Checks", "Cosmetics", true, "Check cosmetics");

            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Arial SDF");
            InitCam();
            RefreshCache();
        }
        #endregion

        #region Upd Things
        public void Update()
        {
            if (Time.time - lastCache >= cacheInterval)
            {
                RefreshCache();
                lastCache = Time.time;
            }

            foreach (var rig in GorillaParent.instance.vrrigs)
            {
                if (rig == null || rig.isOfflineVRRig || rig.mainSkin == null || rig.mainSkin.material == null) continue;
                if (rig.mainSkin.material.name.Contains("gorilla_body") && rig.mainSkin.material.shader == Shader.Find("GorillaTag/UberShader"))
                    rig.mainSkin.material.color = rig.playerColor;
            }

            var rigs = new HashSet<VRRig>(GorillaParent.instance.vrrigs);
            CleanupTags(rigs);
            SpawnTags(rigs);

            mainCam = Camera.main != null ? Camera.main.transform : null;

            foreach (var kv in tagMap)
            {
                var rig = kv.Key;
                var tag = kv.Value;
                if (rig == null || tag == null || rig.isOfflineVRRig || rig.OwningNetPlayer == null) continue;
                FaceCam(tag);
                UpdTag(rig, tag);
            }
        }

        private void FaceCam(GameObject tag)
        {
            if (tag == null) return;

            Transform cam = cineCam != null ? cineCam.transform : mainCam;
            if (cam == null) return;

            tag.transform.LookAt(cam.position);
            tag.transform.Rotate(0f, 180f, 0f);
        }
        #endregion

        #region Tag Things
        private void CleanupTags(HashSet<VRRig> rigs)
        {
            var removeList = new List<VRRig>();

            foreach (var kv in tagMap)
            {
                var rig = kv.Key;
                if (rig == null || !rigs.Contains(rig) || rig.isOfflineVRRig || rig.OwningNetPlayer == null)
                {
                    var mainTxt = kv.Value.GetComponent<TextMeshPro>();
                    if (mainTxt != null && outlineMap.ContainsKey(mainTxt))
                    {
                        foreach (var o in outlineMap[mainTxt]) Destroy(o.gameObject);
                        outlineMap.Remove(mainTxt);
                    }
                    Destroy(kv.Value);
                    removeList.Add(rig);
                }
            }

            foreach (var r in removeList) tagMap.Remove(r);
        }

        private void SpawnTags(HashSet<VRRig> rigs)
        {
            foreach (var r in rigs)
            {
                if (r == null || r.isOfflineVRRig || r.OwningNetPlayer == null) continue;
                if (!tagMap.ContainsKey(r)) tagMap[r] = CreateTag(r);
            }
        }

        private GameObject CreateTag(VRRig rig)
        {
            var go = new GameObject("nametag");
            go.transform.SetParent(rig.transform);
            go.transform.localScale = new Vector3(.8f, .8f, .8f);
            go.transform.localPosition = new Vector3(0f, tagHeight.Value, 0f);

            var t = go.AddComponent<TextMeshPro>();
            t.alignment = TextAlignmentOptions.Center;
            t.fontSize = tagSize.Value;
            t.font = font;
            t.textWrappingMode = TextWrappingModes.Normal;

            UpdTag(rig, go);
            return go;
        }

        private void UpdTag(VRRig rig, GameObject tag)
        {
            if (rig == null || tag == null) return;

            var t = tag.GetComponent<TextMeshPro>();
            if (t == null) return;

            if (t.fontSize != tagSize.Value) t.fontSize = tagSize.Value;
            tag.transform.localPosition = new Vector3(0f, tagHeight.Value, 0f);

            Color c = rig.mainSkin.material.name.Contains("It") ? new Color(1f, 0f, 0f) :
                      rig.mainSkin.material.name.Contains("fected") ? new Color(1f, .5f, 0f) :
                      rig.playerColor;

            t.color = c;
            var sb = new StringBuilder();

            if (chkSpecial.Value)
            {
                string sp = GetSpecial(rig);
                if (!string.IsNullOrEmpty(sp)) sb.AppendLine(sp);
            }

            if (chkFps.Value)
            {
                int fps = (int)Traverse.Create(rig).Field("fps").GetValue();
                sb.Append(string.Format("<color={0}>{1}</color>\n", FPSColor(fps), fps));
            }

            if (chkPlat.Value)
            {
                string plat = Platform(rig);
                string cos = chkCos.Value ? Cosmetics(rig) : "";
                sb.Append(string.Format("<color=white>{0}{1}</color>\n", plat, cos));
            }
            else if (chkCos.Value)
            {
                sb.Append(string.Format("<color=white>{0}</color>\n", Cosmetics(rig)));
            }

            sb.AppendLine(rig.OwningNetPlayer.NickName);

            if (chkMods.Value)
            {
                string m = ModCheck(rig);
                if (!string.IsNullOrEmpty(m)) sb.Append(string.Format("<color=white><size=70%>{0}</size></color>", m));
            }

            t.text = sb.ToString();
            Outlines(t);
        }

        private void Outlines(TextMeshPro main)
        {
            if (outlineMap.ContainsKey(main))
            {
                foreach (var o in outlineMap[main]) if (o != null) Destroy(o.gameObject);
                outlineMap.Remove(main);
            }

            if (!outlineEnabled.Value) return;

            var clones = new List<TextMeshPro>();
            float thick = outlineThick.Value;
            Vector3[] offsets;
            if (outlineQuality.Value == true)
            {
                offsets = new Vector3[] { new Vector3(0f, thick, 0f), new Vector3(0f, -thick, 0f), new Vector3(thick, 0f, 0f), new Vector3(-thick, 0f, 0f), new Vector3(thick, thick, 0f), new Vector3(-thick, thick, 0f), new Vector3(thick, -thick, 0f), new Vector3(-thick, -thick, 0f) };
            }
            else
            {
                offsets = new Vector3[] { new Vector3(0f, thick, 0f), new Vector3(0f, -thick, 0f), new Vector3(thick, 0f, 0f), new Vector3(-thick, 0f, 0f) };
            }

            foreach (var off in offsets)
            {
                var txt = Instantiate(main, main.transform.parent);
                txt.text = StripColors(main.text);
                txt.transform.localPosition = main.transform.localPosition + off;
                txt.transform.localRotation = main.transform.localRotation;
                txt.transform.localScale = main.transform.localScale;

                txt.color = outlineColor.Value;
                txt.fontMaterial = main.fontMaterial;
                txt.fontSize = main.fontSize;
                txt.sortingLayerID = main.sortingLayerID;
                txt.sortingOrder = main.sortingOrder - 1;

                var cr = txt.GetComponent<CanvasRenderer>();
                if (cr != null) cr.cull = false;
                clones.Add(txt);
            }
            outlineMap[main] = clones;
        }

        private string StripColors(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"<color=[^>]+>|</color>", "");
        #endregion

        #region Utils
        private string FPSColor(int fps)
        {
            if (fps >= 250) return "#800080";
            if (fps >= 200) return "#1E90FF";
            if (fps >= 150) return "#006400";
            if (fps >= 100) return "#00FF00";
            if (fps >= 75) return "#ADFF2F";
            if (fps >= 55) return "#FFFF00";
            if (fps >= 45) return "#FFA500";
            if (fps >= 30) return "#FF0000";
            if (fps <= 29) return "#600000";
            return "#FFFFFF";
        }

        private void InitCam()
        {
            try { cineCam = FindFirstObjectByType<CinemachineBrain>()?.GetComponent<Camera>(); }
            catch { cineCam = null; }
        }

        private void RefreshCache()
        {
            if (chkSpecial.Value || chkMods.Value) StartCoroutine(CacheRoutine());
        }

        private IEnumerator CacheRoutine()
        {
            yield return new WaitForEndOfFrame();
            if (chkSpecial.Value) specialCache = PullSpecials();
            if (chkMods.Value) modsCache = PullMods();
        }

        private string GetSpecial(VRRig rig)
        {
            if (!chkSpecial.Value || rig == null || rig.OwningNetPlayer == null || specialCache == null) return "";
            string r;
            if (specialCache.TryGetValue(rig.OwningNetPlayer.UserId, out r)) return r;
            return "";
        }

        private Dictionary<string, string> PullSpecials()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string url = "https://raw.githubusercontent.com/GoldenIsAProtogen/GoldensGorillaNametags/refs/heads/main/People.txt";
                using (WebClient w = new WebClient())
                {
                    string content = w.DownloadString(url);
                    string[] lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        string[] parts = line.Split(new[] { '=' }, 2);
                        if (parts.Length == 2) d[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }
            catch { }
            return d;
        }

        private string Platform(VRRig r)
        {
            if (!chkPlat.Value) return "";
            string cos = r.concatStringOfCosmeticsAllowed;
            if (cos.Contains("S. FIRST LOGIN")) return "[<color=#ffff00>SVR</color>]";
            if (cos.Contains("FIRST LOGIN") || r.OwningNetPlayer.GetPlayerRef().CustomProperties.Count >= 2) return "[<color=#ff0000>PCVR</color>]";
            if (!cos.Contains("FIRST LOGIN") || cos.Contains("LMAKT.")) return "[<color=#00ff00>O</color>]";
            return "[Unknown]";
        }

        private string Cosmetics(VRRig r)
        {
            if (!chkCos.Value) return "";
            var sb = new StringBuilder();
            string c = r.concatStringOfCosmeticsAllowed;
            var dict = new Dictionary<string, string>
            {
                { "LBANI.", "[<color=#FCC200>AAC</color>]" },
                { "LBADE.", "[<color=#FCC200>FP</color>]" },
                { "LBAGS.", "[<color=#FCC200>ILL</color>]" },
                { "LBAAK.", "[<color=#FF0000>S</color>]" },
                { "LMAPY.", "[<color=#C80000>FS</color>]" },
                { "LBAAD.", "[<color=#960000>A</color>]" },
                { "LMAGB.", "[<color=#ffffff>CG</color>]" },
                { "LMAKH.", "[<color=#ffffff>ZC</color>]" },
                { "LMAJD.", "[<color=#ffffff>DK</color>]" },
                { "LMAHF.", "[<color=#ffffff>CFP</color>]" },
                { "LMAAQ.", "[<color=#ffffff>ST</color>]" },
                { "LMAAV.", "[<color=#ffffff>HTS</color>]" }
            };
            foreach (var kv in dict) if (c.Contains(kv.Key)) sb.Append(kv.Value);
            return sb.ToString();
        }

        private string ModCheck(VRRig r)
        {
            if (!chkMods.Value || modsCache == null) return "";
            var sb = new StringBuilder();
            var props = r.Creator.GetPlayerRef().CustomProperties;

            foreach (DictionaryEntry e in props)
            {
                string key = e.Key.ToString().ToLower();
                string tag;
                if (modsCache.TryGetValue(key, out tag))
                {
                    object val = e.Value;
                    if (key == "cheese is gouda")
                    {
                        string v = (val != null) ? val.ToString().ToLower() : "";
                        if (v.Contains("whoisthatmonke")) tag = "[<color=#808080>WITM!</color>]";
                        else if (v.Contains("whoischeating")) tag = "[<color=#00A0FF>WIC</color>]";
                        else tag = "[WI]";
                    }
                    else if (key == "")
                    {
                        string v = (val != null) ? val.ToString().ToLower() : "";
                        if (v.Contains("zern")) tag = "[<color=#00A0FF>ZERN</color>]";
                        else if (v.Contains("wyndigo")) tag = "[<color=#FF0000>WYNDIGO</color>]";
                        else tag = "";
                    }
                    else if (tag.Contains("{0}")) tag = string.Format(tag, val);
                    sb.Append(tag);
                }
            }

            if (chkCos.Value)
            {
                foreach (var i in r.cosmeticSet.items)
                {
                    if (!i.isNullItem && !r.concatStringOfCosmeticsAllowed.Contains(i.itemName))
                    {
                        sb.Append("[<color=#008000>COSMETX</color>] ");
                        break;
                    }
                }
            }
            return sb.ToString();
        }

        private Dictionary<string, string> PullMods()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string url = "https://raw.githubusercontent.com/GoldenIsAProtogen/GoldensGorillaNametags/refs/heads/main/Mods.txt";
                using (WebClient w = new WebClient())
                {
                    string content = w.DownloadString(url);
                    string[] lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        string[] parts = line.Split(new[] { '=' }, 2);
                        if (parts.Length == 2) d[parts[0].Trim().ToLower()] = parts[1].Trim();
                    }
                }
            }
            catch { }
            return d;
        }
        #endregion
    }
}
