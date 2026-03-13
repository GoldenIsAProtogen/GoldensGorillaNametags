using TMPro;
using UnityEngine;

namespace GoldensGorillaNametags;

public class NametagData
{
    public GameObject     Container            { get; set; }
    public TextMeshPro    MainText             { get; set; }
    public GameObject     PlatformIconObj      { get; set; }
    public SpriteRenderer PlatformIconRenderer { get; set; }
    public string         LastText             { get; set; } = string.Empty;
    public Coroutine      ImageUpdateCoroutine { get; set; }
    public Texture2D      CurrentPlatformTex   { get; set; }
}