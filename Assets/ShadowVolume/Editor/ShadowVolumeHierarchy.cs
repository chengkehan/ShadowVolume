using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public class ShadowVolumeHierarchy : MonoBehaviour {

    private static readonly EditorApplication.HierarchyWindowItemCallback hiearchyItemCallback;

    private static Texture2D icon;
    private static Texture2D Icon
    {
        get
        {
            if (icon == null)
            {
                icon = (Texture2D)Resources.Load("ShadowVolumeHierarchyIcon");
            }
            return icon;
        }
    }

    static ShadowVolumeHierarchy()
    {
        ShadowVolumeHierarchy.hiearchyItemCallback = new EditorApplication.HierarchyWindowItemCallback(ShadowVolumeHierarchy.DrawHierarchyIcon);
        EditorApplication.hierarchyWindowItemOnGUI = (EditorApplication.HierarchyWindowItemCallback)System.Delegate.Combine(
            EditorApplication.hierarchyWindowItemOnGUI,
            ShadowVolumeHierarchy.hiearchyItemCallback);
    }

    private static void DrawHierarchyIcon(int instanceID, Rect selectionRect)
    {
        GameObject gameObject = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
        if (gameObject != null && 
            (gameObject.GetComponent<ShadowVolumeRoot>() != null || 
             gameObject.GetComponent<ShadowVolumeCombined>() != null || 
             gameObject.GetComponent<ShadowVolumeObject>() != null || 
             gameObject.GetComponent<ShadowVolumeCamera>() != null))
        {
            Rect rect = new Rect(selectionRect.x + selectionRect.width - 16f, selectionRect.y, 16f, 16f);
            GUI.DrawTexture(rect, ShadowVolumeHierarchy.Icon);
        }
    }
}
