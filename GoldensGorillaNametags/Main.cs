using BepInEx;
using BepInEx.Configuration;
using GoldensGorillaNametags.Utils;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TextCore.LowLevel;
using GFriends = GorillaFriends.Main;

namespace GoldensGorillaNametags
{
    [BepInPlugin(Constants.Guid, Constants.Name, Constants.Version)]
    public class Main : BaseUnityPlugin
    {
        #region Init
        public static Main Instance { get; private set; }

        private TMP_FontAsset _font;
        private Camera _cineCam;
        private Transform _mainCam;

        private ConfigEntry<float> _tagSize, _tagHeight, _updInt, _outlineThick, _iconSize;
        private ConfigEntry<bool> _outlineEnabled, _outlineQual, _chkPlat, _chkSpecial, _chkFps, _chkCos, _gf, _txtQual, _usePlatIcons, _platIconClr;
        private ConfigEntry<Color> _outlineClr;
        private ConfigEntry<string> _iconLocation;

        private Texture2D _cTex, _sTex, _mTex, _wCTex, _wSTex, _wMTex;

        private readonly Dictionary<VRRig, NametagData> _tagMap = new Dictionary<VRRig, NametagData>();
        private Dictionary<string, string> _specialCache;

        private float _lastCacheT, _lastUpdT;
        private const float cacheInt = 150f;
        private const float tagupdTime = 0.3f;
        private string giturl = "https://raw.githubusercontent.com/GoldenIsAProtogen/GoldensGorillaNametags/main/";

        private readonly Dictionary<VRRig, float> _lastTagUpd = new Dictionary<VRRig, float>();
        private static readonly Regex _clrTagRegex = new Regex(@"<color=[^>]+>|</color>", RegexOptions.Compiled);
        private static readonly WaitForEndOfFrame _waitForEOF = new WaitForEndOfFrame();

        private static readonly Vector3 ImgBasePos = new Vector3(0f, 0.85f, 0f);
        private static readonly Vector3 BaseScale = Vector3.one * 0.8f;

        private static readonly Dictionary<string, string> PlatClrs = new Dictionary<string, string>
        {
            { "SVR", "#ffff00" },
            { "PCVR", "#ff0000" },
            { "O", "#00ff00" }
        };

        private static readonly Dictionary<int, string> FpsClrs = new Dictionary<int, string>
        {
            { 250, "#800080" }, { 200, "#1E90FF" }, { 150, "#006400" },
            { 100, "#00FF00" }, { 75, "#ADFF2F" }, { 55, "#FFFF00" },
            { 45, "#FFA500" }, { 30, "#FF0000" }, { 29, "#8B0000" }
        };

        private static readonly Dictionary<string, string> CosTags = new Dictionary<string, string>
        {
            { "LBANI.", "[<color=#FCC200>AAC</color>]" }, { "LBADE.", "[<color=#FCC200>FP</color>]" },
            { "LBAGS.", "[<color=#FCC200>ILL</color>]" }, { "LBAAK.", "[<color=#FF0000>S</color>]" },
            { "LMAPY.", "[<color=#C80000>FS</color>]" }, { "LBAAD.", "[<color=#960000>A</color>]" },
            { "LMAGB.", "[<color=#ffffff>CG</color>]" }, { "LMAKH.", "[<color=#ffffff>ZC</color>]" },
            { "LMAJD.", "[<color=#ffffff>DK</color>]" }, { "LMAHF.", "[<color=#ffffff>CFP</color>]" },
            { "LMAAQ.", "[<color=#ffffff>ST</color>]" }, { "LMAAV.", "[<color=#ffffff>HTS</color>]" }
        };

        private class NametagData
        {
            public GameObject Container { get; set; }
            public TextMeshPro MainTxt { get; set; }
            public GameObject PlatIconObj { get; set; }
            public SpriteRenderer PlatIconRenderer { get; set; }
            public List<TextMeshPro> OutlineClones { get; set; } = new List<TextMeshPro>();
            public string LastTxt { get; set; } = string.Empty;
            public Coroutine ImgUpdCoroutine { get; set; }
            public Texture2D CurrentPlatTex { get; set; }
        }

        internal void Start()
        {
            Instance = this;
            InitCfg();
            InitFont();
            InitCam();
            InitHarmony();
            InitPlatIcons();
            RefreshCache();
        }

        private void InitCfg()
        {
            _tagSize = Config.Bind("Tags", "Size", 1f, "Nametag size");
            _tagHeight = Config.Bind("Tags", "Height", 0.65f, "Nametag height");
            _updInt = Config.Bind("Tags", "Update Int", 0.01f, "Tag update interval");
            _txtQual = Config.Bind("Tags", "Quality", false, "Nametag quality");

            _outlineEnabled = Config.Bind("Outlines", "Enabled", true, "Tag outlines");
            _outlineQual = Config.Bind("Outlines", "Quality", false, "Outline quality");
            _outlineClr = Config.Bind("Outlines", "Color", Color.black, "Outline color");
            _outlineThick = Config.Bind("Outlines", "Thickness", 0.0025f, "Outline thickness");

            _chkSpecial = Config.Bind("Checks", "Special", true, "Check special players");
            _chkFps = Config.Bind("Checks", "FPS", true, "Check FPS");
            _chkCos = Config.Bind("Checks", "Cosmetics", true, "Check cosmetics");
            _chkPlat = Config.Bind("Checks", "Platform", true, "Check platform");

            _usePlatIcons = Config.Bind("Platform", "UseIcons", true, "Show platform as icons instead of text");
            _iconSize = Config.Bind("Platform", "Icon Size", 0.015f, "Size of the platform icons");
            _platIconClr = Config.Bind("Platform", "Icon Colored", true, "If the icons platform icons are colored or not");
            _iconLocation = Config.Bind("Platform", "Icon Location", "left", "Platform icon position\nAcceptable Values: top, bottom, left, right");

            _gf = Config.Bind("Miscellaneous", "GFriends", false, "Use GFriends");
        }

        private void InitFont()
        {
            string fontDir = Path.Combine(Paths.BepInExRootPath, "Fonts");
            if (!Directory.Exists(fontDir))
                Directory.CreateDirectory(fontDir);

            string fontPath = Directory.EnumerateFiles(fontDir, "*.*")
                .FirstOrDefault(path => path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                        path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));

            try
            {
                if (fontPath != null)
                {
                    var unityFont = new Font(fontPath);
                    _font = _txtQual.Value
                        ? TMP_FontAsset.CreateFontAsset(unityFont, 90, 9, GlyphRenderMode.SDFAA, 4096, 4096, AtlasPopulationMode.Dynamic)
                        : TMP_FontAsset.CreateFontAsset(unityFont);
                    _font.material.shader = Shader.Find("TextMeshPro/Mobile/Distance Field");
                }
                else
                {
                    _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Arial SDF");
                }
            }
            catch
            {
                _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/Arial SDF");
            }
        }

        private void InitCam()
        {
            try { _cineCam = FindFirstObjectByType<CinemachineBrain>()?.GetComponent<Camera>(); }
            catch { _cineCam = null; }
        }

        private void InitHarmony()
        {
            var harmony = new Harmony(Constants.Guid);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private void InitPlatIcons()
        {
            StartCoroutine(ImageCoroutine($"{giturl}computer.png", tex => _cTex = tex));
            StartCoroutine(ImageCoroutine($"{giturl}steam.png", tex => _sTex = tex));
            StartCoroutine(ImageCoroutine($"{giturl}meta.png", tex => _mTex = tex));
            StartCoroutine(ImageCoroutine($"{giturl}Computer_White.png", tex => _wCTex = tex));
            StartCoroutine(ImageCoroutine($"{giturl}Steam_White.png", tex => _wSTex = tex));
            StartCoroutine(ImageCoroutine($"{giturl}Meta_White.png", tex => _wMTex = tex));
        }
        #endregion

        #region Upd
        public void Update()
        {
            if (Time.time - _lastCacheT >= cacheInt)
            {
                RefreshCache();
                _lastCacheT = Time.time;
            }
            if (_mainCam == null || Camera.main != null)
                _mainCam = Camera.main?.transform;

            float currentTime = Time.time;
            if (currentTime - _lastUpdT >= _updInt.Value)
            {
                foreach (var r in GorillaParent.instance.vrrigs)
                {
                    if (r == null || r.isOfflineVRRig || r.mainSkin?.material == null) continue;
                    if (r.mainSkin.material.name.Contains("gorilla_body") && r.mainSkin.material.shader == Shader.Find("GorillaTag/UberShader"))
                        r.mainSkin.material.color = r.playerColor;
                }
                var currentRigs = new HashSet<VRRig>(GorillaParent.instance.vrrigs ?? new List<VRRig>());
                CleanupTags(currentRigs);
                CreateTags(currentRigs);
                UpdTags();
                _lastUpdT = currentTime;
            }
        }
        #endregion

        #region Tag Management
        private void CleanupTags(HashSet<VRRig> validRigs)
        {
            var rigsToRemove = _tagMap.Where(kv =>
                kv.Key == null ||
                !validRigs.Contains(kv.Key) ||
                kv.Key.isOfflineVRRig ||
                kv.Key.OwningNetPlayer == null).Select(kv => kv.Key).ToList();

            foreach (var r in rigsToRemove)
            {
                if (_tagMap.TryGetValue(r, out var data))
                {
                    if (data.ImgUpdCoroutine != null)
                        StopCoroutine(data.ImgUpdCoroutine);

                    CleanupOutline(data);
                    if (data.Container != null)
                        Destroy(data.Container);
                }
                _tagMap.Remove(r);
                _lastTagUpd.Remove(r);
            }
        }

        private void CreateTags(HashSet<VRRig> validRigs)
        {
            foreach (var r in validRigs)
            {
                if (r == null || r.isOfflineVRRig || r.OwningNetPlayer == null)
                    continue;

                if (!_tagMap.ContainsKey(r))
                {
                    _tagMap[r] = CreateTags(r);
                }
            }
        }

        private NametagData CreateTags(VRRig r)
        {
            var data = new NametagData();

            data.Container = new GameObject("NametagContainer");
            data.Container.transform.SetParent(r.transform, false);
            data.Container.transform.localScale = BaseScale;
            data.Container.transform.localPosition = new Vector3(0f, _tagHeight.Value, 0f);

            var mainTxtGo = new GameObject("NametagMain");
            mainTxtGo.transform.SetParent(data.Container.transform, false);
            mainTxtGo.transform.localPosition = Vector3.zero;
            mainTxtGo.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);

            data.MainTxt = mainTxtGo.AddComponent<TextMeshPro>();
            TagTxt(data.MainTxt);

            data.PlatIconObj = new GameObject("PlatformIcon");
            data.PlatIconObj.transform.SetParent(data.Container.transform, false);
            data.PlatIconObj.transform.localPosition = IconPos(_iconLocation.Value);
            data.PlatIconObj.transform.localScale = new Vector3(_iconSize.Value, _iconSize.Value, _iconSize.Value);

            data.PlatIconRenderer = data.PlatIconObj.AddComponent<SpriteRenderer>();
            data.PlatIconRenderer.sortingOrder = 10;
            data.PlatIconRenderer.gameObject.SetActive(false);

            data.ImgUpdCoroutine = StartCoroutine(UpdPlatIconCoroutine(r, data));

            return data;
        }

        private Vector3 IconPos(string location)
        {
            switch (location.ToLower())
            {
                case "left":
                    return new Vector3(_tagSize.Value * -0.5f, _tagHeight.Value - .85f, 0f);
                case "right":
                    return new Vector3(_tagSize.Value * 0.5f, _tagHeight.Value - .85f, 0f);
                case "top":
                    return new Vector3(0f, _tagHeight.Value * 0.85f, 0f);
                case "bottom":
                    return new Vector3(0f, _tagHeight.Value - 1f, 0f);
                default:
                    return ImgBasePos;
            }
        }

        private void TagTxt(TextMeshPro txt)
        {
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontSize = _tagSize.Value;
            txt.font = _font;
            txt.textWrappingMode = TextWrappingModes.Normal;
            txt.richText = true;
        }

        private void UpdTags()
        {
            float currentTime = Time.time;

            foreach (var kv in _tagMap)
            {
                var r = kv.Key;
                var data = kv.Value;

                if (r == null || data?.Container == null || r.isOfflineVRRig || r.OwningNetPlayer == null)
                    continue;

                Cam(data.Container.transform);

                if (!_lastTagUpd.ContainsKey(r) || currentTime - _lastTagUpd[r] >= tagupdTime)
                {
                    UpdTagContent(r, data);
                    _lastTagUpd[r] = currentTime;
                }

                UpdPlatIcon(data);
            }
        }

        private void Cam(Transform tagTransform)
        {
            if (tagTransform == null) return;

            Transform cameraTransform = _cineCam != null ? _cineCam.transform : _mainCam;
            if (cameraTransform == null) return;

            tagTransform.LookAt(cameraTransform.position);
            tagTransform.Rotate(0f, 180f, 0f);

            foreach (Transform child in tagTransform)
            {
                child.localRotation = Quaternion.identity;
            }
        }

        private void UpdPlatIcon(NametagData data)
        {
            if (data.PlatIconRenderer != null)
            {
                bool shouldBeVisible = _usePlatIcons.Value && data.CurrentPlatTex != null;
                data.PlatIconRenderer.gameObject.SetActive(shouldBeVisible);
            }
        }

        private void UpdTagContent(VRRig r, NametagData data)
        {
            data.Container.transform.localPosition = new Vector3(0f, _tagHeight.Value, 0f);

            if (data.MainTxt.fontSize != _tagSize.Value)
                data.MainTxt.fontSize = _tagSize.Value;

            string txt = CreateTagTxt(r);

            if (data.LastTxt != txt)
            {
                data.MainTxt.text = txt;
                data.LastTxt = txt;
                UpdTxtClr(r, data.MainTxt);
                UpdOutline(data);
            }
        }

        private string CreateTagTxt(VRRig r)
        {
            var sb = new StringBuilder(128);

            if (_chkSpecial.Value)
            {
                string specialTag = SpecialTag(r);
                if (!string.IsNullOrEmpty(specialTag))
                    sb.AppendLine(specialTag);
            }

            if (_chkFps.Value)
            {
                int fps = (int)Traverse.Create(r).Field("fps").GetValue();
                sb.Append($"<color={FpsClr(fps)}>{fps}</color>\n");
            }

            string platformTag = _chkPlat.Value && !_usePlatIcons.Value ? PlatTag(r) : "";
            string cosmeticsTag = _chkCos.Value ? CosTag(r) : "";

            if (_chkPlat.Value && !_usePlatIcons.Value && _chkCos.Value)
            {
                if (!string.IsNullOrEmpty(platformTag) || !string.IsNullOrEmpty(cosmeticsTag))
                {
                    sb.Append($"<color=white>{platformTag}{cosmeticsTag}</color>\n");
                }
            }
            else if (_chkCos.Value && !string.IsNullOrEmpty(cosmeticsTag))
            {
                sb.Append($"<color=white>{cosmeticsTag}</color>\n");
            }
            else if (_chkPlat.Value && !_usePlatIcons.Value && !string.IsNullOrEmpty(platformTag))
            {
                sb.Append($"<color=white>{platformTag}</color>\n");
            }

            string plrName = r.OwningNetPlayer.NickName;
            sb.AppendLine(plrName.Length > 12 ? plrName.Substring(0, 12) + "..." : plrName);

            return sb.ToString();
        }

        private void UpdTxtClr(VRRig r, TextMeshPro txt)
        {
            Color clr = PlrClr(r);
            txt.color = clr;
        }

        private Color PlrClr(VRRig r)
        {
            if (_gf.Value && r.OwningNetPlayer != null)
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
            if (r.mainSkin.material.name.Contains("fected"))
                return new Color(1f, 0.5f, 0f);

            return r.playerColor;
        }

        private void UpdOutline(NametagData data)
        {
            CleanupOutline(data);

            if (!_outlineEnabled.Value || data.MainTxt == null)
                return;

            CreateOutlineClones(data);
        }

        private void CleanupOutline(NametagData data)
        {
            if (data.OutlineClones != null)
            {
                foreach (var outline in data.OutlineClones)
                {
                    if (outline != null && outline.gameObject != null)
                        Destroy(outline.gameObject);
                }
                data.OutlineClones.Clear();
            }
        }

        private void CreateOutlineClones(NametagData data)
        {
            float thickness = _outlineThick.Value;
            Vector3[] offsets = _outlineQual.Value ? CreateHighQualOutline(thickness) : CreateOutline(thickness);

            string plainTxt = StripClrTags(data.MainTxt.text);

            foreach (var offset in offsets)
            {
                var outline = CreateOutlineClone(data.MainTxt, offset, plainTxt);
                data.OutlineClones.Add(outline);
            }
        }

        private Vector3[] CreateOutline(float thickness) => new Vector3[]
        {
            new Vector3(0f, thickness, 0f), new Vector3(0f, -thickness, 0f),
            new Vector3(thickness, 0f, 0f), new Vector3(-thickness, 0f, 0f)
        };

        private Vector3[] CreateHighQualOutline(float thickness) => new Vector3[]
        {
            new Vector3(0f, thickness, 0f), new Vector3(0f, -thickness, 0f),
            new Vector3(thickness, 0f, 0f), new Vector3(-thickness, 0f, 0f),
            new Vector3(thickness, thickness, 0f), new Vector3(-thickness, thickness, 0f),
            new Vector3(thickness, -thickness, 0f), new Vector3(-thickness, -thickness, 0f)
        };

        private TextMeshPro CreateOutlineClone(TextMeshPro original, Vector3 offset, string txt)
        {
            var clone = Instantiate(original, original.transform.parent);
            clone.text = txt;
            clone.transform.localPosition = original.transform.localPosition + offset;
            clone.transform.localRotation = original.transform.localRotation;
            clone.transform.localScale = original.transform.localScale;
            clone.color = _outlineClr.Value;
            clone.sortingOrder = original.sortingOrder - 1;

            var canvasRenderer = clone.GetComponent<CanvasRenderer>();
            if (canvasRenderer != null)
                canvasRenderer.cull = false;

            return clone;
        }

        private string StripClrTags(string txt) => _clrTagRegex.Replace(txt, "");
        #endregion

        #region Utils
        private IEnumerator UpdPlatIconCoroutine(VRRig r, NametagData data)
        {
            while (_cTex == null || _sTex == null || _mTex == null || _wCTex == null || _wSTex == null || _wMTex == null)
                yield return null;

            yield return new WaitForSeconds(2f);

            while (r != null && data?.PlatIconRenderer != null)
            {
                if (_usePlatIcons.Value && _chkPlat.Value)
                {
                    UpdPlatIconTex(r, data);
                }
                else
                {
                    data.CurrentPlatTex = null;
                    data.PlatIconRenderer.sprite = null;
                    data.PlatIconRenderer.gameObject.SetActive(false);
                }

                yield return new WaitForSeconds(10f);
            }
        }

        private void UpdPlatIconTex(VRRig r, NametagData data)
        {
            Texture2D newPlatTex = PlatTex(r);

            if (newPlatTex != data.CurrentPlatTex)
            {
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
        }

        private string FpsClr(int fps)
        {
            foreach (var threshold in FpsClrs.OrderByDescending(kv => kv.Key))
            {
                if (fps >= threshold.Key)
                    return threshold.Value;
            }
            return "#600000";
        }

        private string SpecialTag(VRRig r)
        {
            if (!_chkSpecial.Value || r?.OwningNetPlayer == null || _specialCache == null)
                return string.Empty;

            return _specialCache.TryGetValue(r.OwningNetPlayer.UserId, out var tag) ? tag : string.Empty;
        }

        private string PlatTag(VRRig r)
        {
            if (!_chkPlat.Value) return string.Empty;

            string cosmetics = r.concatStringOfCosmeticsAllowed ?? "";
            string platformKey = PlatKey(cosmetics, r);

            return PlatClrs.TryGetValue(platformKey, out var clr) ? $"[<color={clr}>{platformKey}</color>]" : "[Unknown]";
        }

        private string PlatKey(string cosmetics, VRRig r)
        {
            if (string.IsNullOrEmpty(cosmetics) || r?.OwningNetPlayer == null) return "Unknown";

            int propCount = r.OwningNetPlayer.GetPlayerRef().CustomProperties.Count;

            if (cosmetics.Contains("S. FIRST LOGIN")) return "SVR";
            if (cosmetics.Contains("FIRST LOGIN") || propCount >= 2) return "PCVR";
            if (!cosmetics.Contains("FIRST LOGIN") || cosmetics.Contains("LMAKT.")) return "O";

            return "Unknown";
        }

        private Texture2D PlatTex(VRRig r)
        {
            if (r?.concatStringOfCosmeticsAllowed == null)
                return null;

            string cosmetics = r.concatStringOfCosmeticsAllowed;
            int propCount = r.OwningNetPlayer.GetPlayerRef().CustomProperties.Count;

            if (_platIconClr.Value)
            {
                if (cosmetics.Contains("S. FIRST LOGIN"))
                    return _sTex;
                else if (cosmetics.Contains("FIRST LOGIN") || propCount >= 2)
                    return _cTex;
                else
                    return _mTex;
            }
            else
            {
                if (cosmetics.Contains("S. FIRST LOGIN"))
                    return _wSTex;
                else if (cosmetics.Contains("FIRST LOGIN") || propCount >= 2)
                    return _wCTex;
                else
                    return _wMTex;
            }
        }

        private string CosTag(VRRig r)
        {
            if (!_chkCos.Value) return string.Empty;

            var sb = new StringBuilder(32);
            string cosmetics = r.concatStringOfCosmeticsAllowed ?? "";

            foreach (var cosmetic in CosTags)
            {
                if (cosmetics.Contains(cosmetic.Key))
                    sb.Append(cosmetic.Value);
            }

            return sb.ToString();
        }

        private void RefreshCache()
        {
            if (_chkSpecial.Value)
                StartCoroutine(UpdCacheCoroutine());
        }

        private IEnumerator UpdCacheCoroutine()
        {
            yield return _waitForEOF;

            if (_chkSpecial.Value)
                _specialCache = SpecialCache();
        }

        private Dictionary<string, string> SpecialCache()
        {
            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var client = new WebClient())
                {
                    string content = client.DownloadString($"{giturl}People.txt");
                    KeyValShit(content, cache);
                }
            }
            catch { }
            return cache;
        }

        private void KeyValShit(string content, Dictionary<string, string> dictionary)
        {
            var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                var parts = line.Split(new[] { '$' }, 2);
            }
        }

        private IEnumerator ImageCoroutine(string url, Action<Texture2D> onComplete)
        {
            using (var request = UnityWebRequestTexture.GetTexture(url))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var tex = DownloadHandlerTexture.GetContent(request);
                    tex.filterMode = FilterMode.Point;
                    onComplete(tex);
                }
            }
        }
        #endregion
    }
}
