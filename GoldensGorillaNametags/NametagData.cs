using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace GoldensGorillaNametags;

public class NametagData
{
    public GameObject        Container        { get; set; }
    public TextMeshPro       MainTxt          { get; set; }
    public GameObject        PlatIconObj      { get; set; }
    public SpriteRenderer    PlatIconRenderer { get; set; }
    public string            LastTxt          { get; set; } = string.Empty;
    public Coroutine         ImgUpdCoroutine  { get; set; }
    public Texture2D         CurrentPlatTex   { get; set; }
}