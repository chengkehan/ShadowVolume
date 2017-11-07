using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class ShadowVolumeImageEffect : MonoBehaviour
{
    public bool available = true;

    abstract public void DrawImageEffect(RenderTexture source, RenderTexture destination);
}
