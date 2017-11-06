using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(ShadowVolumeRoot))]
public class ShadowVolumeRootEditor : ShadowVolumeNoTransformEditor<ShadowVolumeRoot>
{
    private void OnSceneGUI()
    {
        DoOnSceneGUI();
    }
}
