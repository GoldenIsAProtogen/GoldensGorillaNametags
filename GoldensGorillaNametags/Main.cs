using BepInEx;
using BepInEx.Configuration;
using GoldensGorillaNametags.Utilities;
using HarmonyLib;
using Photon.Pun;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
        #region Initialization
        public static Main Instance { get; private set; }

        private TMP_FontAsset _font;
        private Camera _cineCam;
        private Transform _mainCam;

        private ConfigEntry<float> _tagSize, _tagHeight, _updInt, _outlineThick, _iconSize;
        private ConfigEntry<bool> _outlineEnabled, _outlineQual, _chkMods, _chkPlat, _chkSpecial, _chkFps, _chkCos, _chkPing, _gf, _textQual, _usePlatIcons;
        private ConfigEntry<Color> _outlineColor;

        private Texture2D _computerTex, _steamTex, _metaTex;

        private readonly Dictionary<VRRig, NametagData> _tagMap = new Dictionary<VRRig, NametagData>();
        private readonly Dictionary<VRRig, int> _playerPing = new Dictionary<VRRig, int>();
        private Dictionary<string, string> _specialCache;

        private float _lastCacheT, _lastUpdT;
        private const float cacheInt = 150f;
        private const float tagupdcooldown = 0.3f;

        private readonly Dictionary<VRRig, float> _lastTagUpdate = new Dictionary<VRRig, float>();
        private static readonly Regex _colorTagRegex = new Regex(@"<color=[^>]+>|</color>", RegexOptions.Compiled);
        private static readonly WaitForEndOfFrame _waitForEndOfFrame = new WaitForEndOfFrame();

        private static readonly Vector3 ImageBasePosition = new Vector3(0f, 0.85f, 0f);
        private static readonly Vector3 BaseScale = Vector3.one * 0.8f;

        private static readonly Dictionary<string, string> PlatformColors = new Dictionary<string, string>
        {
            { "SVR", "#ffff00" },
            { "PCVR", "#ff0000" },
            { "O", "#00ff00" }
        };

        private static readonly Dictionary<int, string> FpsColors = new Dictionary<int, string>
        {
            { 250, "#800080" }, { 200, "#1E90FF" }, { 150, "#006400" },
            { 100, "#00FF00" }, { 75, "#ADFF2F" }, { 55, "#FFFF00" },
            { 45, "#FFA500" }, { 30, "#FF0000" }, { 29, "#8B0000" }
        };

        private static readonly Dictionary<int, string> PingColors = new Dictionary<int, string>
        {
            { 50, "#00FF00" }, { 100, "#ADFF2F" }, { 150, "#FFFF00" },
            { 200, "#FFA500" }, { 300, "#FF0000" }, { 301, "#8B0000" }
        };

        private static readonly Dictionary<string, string> CosmeticTags = new Dictionary<string, string>
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
            public TextMeshPro MainText { get; set; }
            public GameObject PlatIconObj { get; set; }
            public SpriteRenderer PlatIconRenderer { get; set; }
            public List<TextMeshPro> OutlineClones { get; set; } = new List<TextMeshPro>();
            public string LastText { get; set; } = string.Empty;
            public string LastPlatform { get; set; } = string.Empty;
            public Coroutine ImgUpdCoroutine { get; set; }
            public Texture2D CurrentPlatTexture { get; set; }
        }

        internal void Start()
        {
            Instance = this;
            InitializeConfig();
            LoadFont();
            InitializeCamera();
            InitializeHarmony();
            PreloadTextures();
            RefreshCache();
        }

        private void InitializeConfig()
        {
            _tagSize = Config.Bind("Tags", "Size", 1f, "Nametag size");
            _tagHeight = Config.Bind("Tags", "Height", 0.65f, "Nametag height");
            _textQual = Config.Bind("Tags", "Quality", false, "Nametag quality");
            _updInt = Config.Bind("Tags", "Update Int", 0.01f, "Tag update interval");

            _outlineEnabled = Config.Bind("Outlines", "Enabled", true, "Tag outlines");
            _outlineQual = Config.Bind("Outlines", "Quality", false, "Outline quality");
            _outlineColor = Config.Bind("Outlines", "Color", Color.black, "Outline color");
            _outlineThick = Config.Bind("Outlines", "Thickness", 0.0025f, "Outline thickness");

            _chkPlat = Config.Bind("Checks", "Platform", true, "Check platform");
            _chkSpecial = Config.Bind("Checks", "Special", true, "Check special players");
            _chkFps = Config.Bind("Checks", "FPS", true, "Check FPS");
            _chkCos = Config.Bind("Checks", "Cosmetics", true, "Check cosmetics");
            _chkPing = Config.Bind("Checks", "Ping", false, "Check ping (WIP (doesnt work))");

            _gf = Config.Bind("Other", "GFriends", false, "Use GFriends");
            _usePlatIcons = Config.Bind("Platform", "UseIcons", false, "Show platform as icons instead of text");
            _iconSize = Config.Bind("Platform", "Icon Size", 0.015f, "Size of the platform icons");
        }

        private void LoadFont()
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
                    _font = _textQual.Value
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

        private void InitializeCamera()
        {
            try { _cineCam = FindFirstObjectByType<CinemachineBrain>()?.GetComponent<Camera>(); }
            catch { _cineCam = null; }
        }

        private void InitializeHarmony()
        {
            new Harmony(Constants.Guid + ".ping").PatchAll();
        }

        private void PreloadTextures()
        {
            StartCoroutine(ImageCoroutine(
                "https://raw.githubusercontent.com/GoldenIsAProtogen/GoldensGorillaNametags/main/computer.png",
                tex => _computerTex = tex));
            StartCoroutine(ImageCoroutine(
                "https://raw.githubusercontent.com/GoldenIsAProtogen/GoldensGorillaNametags/main/steam.png",
                tex => _steamTex = tex));
            StartCoroutine(ImageCoroutine(
                "https://raw.githubusercontent.com/GoldenIsAProtogen/GoldensGorillaNametags/main/meta.png",
                tex => _metaTex = tex));
        }
        #endregion

        #region Updating Loop
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
                foreach (var rig in GorillaParent.instance.vrrigs)
                {
                    if (rig == null || rig.isOfflineVRRig || rig.mainSkin?.material == null) continue;
                    if (rig.mainSkin.material.name.Contains("gorilla_body") && rig.mainSkin.material.shader == Shader.Find("GorillaTag/UberShader"))
                        rig.mainSkin.material.color = rig.playerColor;
                }
                var currentRigs = new HashSet<VRRig>(GorillaParent.instance.vrrigs ?? new List<VRRig>());
                CleanupTags(currentRigs);
                CreateMissingTags(currentRigs);
                UpdAllTags();
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
                kv.Key.OwningNetPlayer == null)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var rig in rigsToRemove)
            {
                if (_tagMap.TryGetValue(rig, out var data))
                {
                    if (data.ImgUpdCoroutine != null)
                        StopCoroutine(data.ImgUpdCoroutine);

                    CleanupOutline(data);
                    if (data.Container != null)
                        Destroy(data.Container);
                }
                _tagMap.Remove(rig);
                _playerPing.Remove(rig);
                _lastTagUpdate.Remove(rig);
            }
        }

        private void CreateMissingTags(HashSet<VRRig> validRigs)
        {
            foreach (var rig in validRigs)
            {
                if (rig == null || rig.isOfflineVRRig || rig.OwningNetPlayer == null)
                    continue;

                if (!_tagMap.ContainsKey(rig))
                {
                    _tagMap[rig] = CreateNametag(rig);
                }
            }
        }

        private NametagData CreateNametag(VRRig rig)
        {
            var data = new NametagData();

            data.Container = new GameObject("NametagContainer");
            data.Container.transform.SetParent(rig.transform, false);
            data.Container.transform.localScale = BaseScale;
            data.Container.transform.localPosition = new Vector3(0f, _tagHeight.Value, 0f);

            var mainTextGo = new GameObject("NametagMain");
            mainTextGo.transform.SetParent(data.Container.transform, false);
            mainTextGo.transform.localPosition = Vector3.zero;
            mainTextGo.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);

            data.MainText = mainTextGo.AddComponent<TextMeshPro>();
            ConfigTxtComponent(data.MainText);

            data.PlatIconObj = new GameObject("PlatformIcon");
            data.PlatIconObj.transform.SetParent(data.Container.transform, false);
            data.PlatIconObj.transform.localPosition = ImageBasePosition;
            data.PlatIconObj.transform.localScale = new Vector3(_iconSize.Value, _iconSize.Value, _iconSize.Value);

            data.PlatIconRenderer = data.PlatIconObj.AddComponent<SpriteRenderer>();
            data.PlatIconRenderer.sortingOrder = 10;
            data.PlatIconRenderer.gameObject.SetActive(false);

            data.ImgUpdCoroutine = StartCoroutine(UpdPlatIconCoroutine(rig, data));

            return data;
        }

        private void ConfigTxtComponent(TextMeshPro text)
        {
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = _tagSize.Value;
            text.font = _font;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = true;
        }

        private void UpdAllTags()
        {
            float currentTime = Time.time;

            foreach (var kv in _tagMap)
            {
                var rig = kv.Key;
                var data = kv.Value;

                if (rig == null || data?.Container == null || rig.isOfflineVRRig || rig.OwningNetPlayer == null)
                    continue;

                Cam(data.Container.transform);

                if (!_lastTagUpdate.ContainsKey(rig) || currentTime - _lastTagUpdate[rig] >= tagupdcooldown)
                {
                    UpdTagContent(rig, data);
                    _lastTagUpdate[rig] = currentTime;
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
                bool shouldBeVisible = _usePlatIcons.Value && data.CurrentPlatTexture != null;
                data.PlatIconRenderer.gameObject.SetActive(shouldBeVisible);
            }
        }

        private void UpdTagContent(VRRig rig, NametagData data)
        {
            data.Container.transform.localPosition = new Vector3(0f, _tagHeight.Value, 0f);

            if (data.MainText.fontSize != _tagSize.Value)
                data.MainText.fontSize = _tagSize.Value;

            string txt = CreateTagTxt(rig);

            if (data.LastText != txt)
            {
                data.MainText.text = txt;
                data.LastText = txt;
                UpdTxtColor(rig, data.MainText);
                UpdOutlines(data);
            }
        }

        private string CreateTagTxt(VRRig rig)
        {
            var sb = new StringBuilder(128);

            if (_chkSpecial.Value)
            {
                string specialTag = GetSpecialTag(rig);
                if (!string.IsNullOrEmpty(specialTag))
                    sb.AppendLine(specialTag);
            }

            if (_chkFps.Value)
            {
                int fps = (int)Traverse.Create(rig).Field("fps").GetValue();
                sb.Append($"<color={GetFpsColor(fps)}>{fps}</color>\n");
            }

            if (_chkPing.Value)
            {
                int ping = GetPing(rig);
                sb.Append($"<color={GetPingColor(ping)}>{ping}ms</color>\n");
            }

            if (_chkPlat.Value && !_usePlatIcons.Value)
            {
                string platformTag = GetPlatTag(rig);
                string cosmeticsTag = _chkCos.Value ? GetCosTag(rig) : "";
                sb.Append($"<color=white>{platformTag}{cosmeticsTag}</color>\n");
            }
            else if (_chkCos.Value && !_usePlatIcons.Value)
            {
                sb.Append($"<color=white>{GetCosTag(rig)}</color>\n");
            }

            sb.AppendLine(rig.OwningNetPlayer.NickName);

            return sb.ToString();
        }

        private void UpdTxtColor(VRRig rig, TextMeshPro text)
        {
            Color color = DeterminePlayerColor(rig);
            text.color = color;
        }

        private Color DeterminePlayerColor(VRRig rig)
        {
            if (rig.mainSkin.material.name.Contains("It"))
                return new Color(1f, 0f, 0f);
            if (rig.mainSkin.material.name.Contains("fected"))
                return new Color(1f, 0.5f, 0f);

            if (_gf.Value && rig.OwningNetPlayer != null)
            {
                if (GFriendStuff.Verified(rig.OwningNetPlayer))
                    return GFriends.m_clrVerified;
                if (GFriendStuff.Friend(rig.OwningNetPlayer))
                    return GFriends.m_clrFriend;
                if (GFriendStuff.RecentlyPlayedWith(rig.OwningNetPlayer))
                    return GFriends.m_clrPlayedRecently;
            }

            return rig.playerColor;
        }

        private void UpdOutlines(NametagData data)
        {
            CleanupOutline(data);

            if (!_outlineEnabled.Value || data.MainText == null)
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
            Vector3[] offsets = _outlineQual.Value ? CreateHighQualityOffsets(thickness) : CreateStandardOffsets(thickness);

            string plainText = StripColorTags(data.MainText.text);

            foreach (var offset in offsets)
            {
                var outline = CreateOutlineClone(data.MainText, offset, plainText);
                data.OutlineClones.Add(outline);
            }
        }

        private Vector3[] CreateStandardOffsets(float thickness) => new Vector3[]
        {
            new Vector3(0f, thickness, 0f), new Vector3(0f, -thickness, 0f),
            new Vector3(thickness, 0f, 0f), new Vector3(-thickness, 0f, 0f)
        };

        private Vector3[] CreateHighQualityOffsets(float thickness) => new Vector3[]
        {
            new Vector3(0f, thickness, 0f), new Vector3(0f, -thickness, 0f),
            new Vector3(thickness, 0f, 0f), new Vector3(-thickness, 0f, 0f),
            new Vector3(thickness, thickness, 0f), new Vector3(-thickness, thickness, 0f),
            new Vector3(thickness, -thickness, 0f), new Vector3(-thickness, -thickness, 0f)
        };

        private TextMeshPro CreateOutlineClone(TextMeshPro original, Vector3 offset, string text)
        {
            var clone = Instantiate(original, original.transform.parent);
            clone.text = text;
            clone.transform.localPosition = original.transform.localPosition + offset;
            clone.transform.localRotation = original.transform.localRotation;
            clone.transform.localScale = original.transform.localScale;
            clone.color = _outlineColor.Value;
            clone.sortingOrder = original.sortingOrder - 1;

            var canvasRenderer = clone.GetComponent<CanvasRenderer>();
            if (canvasRenderer != null)
                canvasRenderer.cull = false;

            return clone;
        }

        private string StripColorTags(string text) => _colorTagRegex.Replace(text, "");
        #endregion

        #region Utilities
        private IEnumerator UpdPlatIconCoroutine(VRRig rig, NametagData data)
        {
            while (_computerTex == null || _steamTex == null || _metaTex == null)
                yield return null;

            yield return new WaitForSeconds(2f);

            while (rig != null && data?.PlatIconRenderer != null)
            {
                if (_usePlatIcons.Value && _chkPlat.Value)
                {
                    UpdPlatIconTexture(rig, data);
                }
                else
                {
                    data.CurrentPlatTexture = null;
                    data.PlatIconRenderer.sprite = null;
                    data.PlatIconRenderer.gameObject.SetActive(false);
                }

                yield return new WaitForSeconds(10f);
            }
        }

        private void UpdPlatIconTexture(VRRig rig, NametagData data)
        {
            Texture2D newPlatformTexture = PlatTexture(rig);

            if (newPlatformTexture != data.CurrentPlatTexture)
            {
                data.CurrentPlatTexture = newPlatformTexture;

                if (newPlatformTexture != null)
                {
                    data.PlatIconRenderer.sprite = Sprite.Create(newPlatformTexture,
                        new Rect(0, 0, newPlatformTexture.width, newPlatformTexture.height),
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

        private Texture2D PlatTexture(VRRig rig)
        {
            if (rig?.concatStringOfCosmeticsAllowed == null)
                return null;

            string cosmetics = rig.concatStringOfCosmeticsAllowed;

            if (cosmetics.Contains("S. FIRST LOGIN"))
                return _steamTex;
            else if (cosmetics.Contains("FIRST LOGIN"))
                return _computerTex;
            else
                return _metaTex;
        }

        public void PlrVelUpd(VRRig rig, float time)
        {
            if (rig != null) _playerPing[rig] = (int)Math.Abs((time * 1000) - PhotonNetwork.ServerTimestamp);
        }

        private int GetPing(VRRig rig) => rig == null || !_playerPing.TryGetValue(rig, out int ping) ? 0 : ping;

        private string GetPingColor(int ping)
        {
            foreach (var threshold in PingColors.OrderByDescending(kv => kv.Key))
            {
                if (ping >= threshold.Key)
                    return threshold.Value;
            }
            return "#600000";
        }

        private string GetFpsColor(int fps)
        {
            foreach (var threshold in FpsColors.OrderByDescending(kv => kv.Key))
            {
                if (fps >= threshold.Key)
                    return threshold.Value;
            }
            return "#600000";
        }

        private string GetSpecialTag(VRRig rig)
        {
            if (!_chkSpecial.Value || rig?.OwningNetPlayer == null || _specialCache == null)
                return string.Empty;

            return _specialCache.TryGetValue(rig.OwningNetPlayer.UserId, out var tag) ? tag : string.Empty;
        }

        private string GetPlatTag(VRRig rig)
        {
            if (!_chkPlat.Value) return string.Empty;

            string cosmetics = rig.concatStringOfCosmeticsAllowed ?? "";
            string platformKey = PlatKey(cosmetics);

            return PlatformColors.TryGetValue(platformKey, out var color) ? $"[<color={color}>{platformKey}</color>]" : "[Unknown]";
        }

        private string PlatKey(string cosmetics)
        {
            if (cosmetics.Contains("S. FIRST LOGIN")) return "SVR";
            if (cosmetics.Contains("FIRST LOGIN") || IsPCVR(cosmetics)) return "PCVR";
            if (!cosmetics.Contains("FIRST LOGIN") || cosmetics.Contains("LMAKT.")) return "O";
            return "Unknown";
        }

        private bool IsPCVR(string cosmetics) => cosmetics.Contains("FIRST LOGIN") || cosmetics.Length >= 2;

        private string GetCosTag(VRRig rig)
        {
            if (!_chkCos.Value) return string.Empty;

            var sb = new StringBuilder(32);
            string cosmetics = rig.concatStringOfCosmeticsAllowed ?? "";

            foreach (var cosmetic in CosmeticTags)
            {
                if (cosmetics.Contains(cosmetic.Key))
                    sb.Append(cosmetic.Value);
            }

            return sb.ToString();
        }

        private void RefreshCache()
        {
            if (_chkSpecial.Value || _chkMods.Value)
                StartCoroutine(UpdCacheCoroutine());
        }

        private IEnumerator UpdCacheCoroutine()
        {
            yield return _waitForEndOfFrame;

            if (_chkSpecial.Value)
                _specialCache = SpecialCache();
        }

        private Dictionary<string, string> SpecialCache()
        {
            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string url = "https://raw.githubusercontent.com/GoldenIsAProtogen/GoldensGorillaNametags/main/People.txt";
                using (var client = new WebClient())
                {
                    string content = client.DownloadString(url);
                    ParseKeyValue(content, cache);
                }
            }
            catch { }
            return cache;
        }

        private void ParseKeyValue(string content, Dictionary<string, string> dictionary)
        {
            var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                var parts = line.Split(new[] { '$' }, 2);
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim().Replace("\\n", "\n");
                    dictionary[key] = parts[1].Trim();
                }
            }
        }

        private IEnumerator ImageCoroutine(string url, Action<Texture2D> onComplete)
        {
            using (var request = UnityWebRequestTexture.GetTexture(url))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var texture = DownloadHandlerTexture.GetContent(request);
                    texture.filterMode = FilterMode.Point;
                    onComplete(texture);
                }
            }
        }
        #endregion
    }
}
