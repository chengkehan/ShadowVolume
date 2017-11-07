using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
[ImageEffectAllowedInSceneView]
public class TestImageEffect : ShadowVolumeImageEffect
{
    private Material _mtrl = null;
    private Material Mtrl
    {
        get
        {
            if(_mtrl == null)
            {
                _mtrl = new Material(Shader.Find("Hidden/ShadowVolume/TestImageEffect"));
            }
            return _mtrl;
        }
    }

    protected virtual void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Graphics.Blit(source, destination, Mtrl);
    }

    private void Update()
    {
        // Do nothing
    }

    private void OnDestroy()
    {
        if (_mtrl != null)
        {
            DestroyImmediate(_mtrl);
            _mtrl = null;
        }
    }

    public override void DrawImageEffect(RenderTexture source, RenderTexture destination)
    {
        OnRenderImage(source, destination);
    }
}
