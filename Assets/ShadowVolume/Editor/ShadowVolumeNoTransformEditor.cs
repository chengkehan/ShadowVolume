using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class ShadowVolumeNoTransformEditor<T> : Editor 
    where T:MonoBehaviour
{
    protected T svo = default(T);

    private Vector3 pos;
    private Vector3 euler;
    private Vector3 scale;
    private Transform parent;

    protected virtual void OnEnable()
    {
        Tools.hidden = true;
    }

    protected virtual void OnDisable()
    {
        Tools.hidden = false;
    }

    protected void DoOnSceneGUI()
    {
        if (svo == null)
        {
            svo = target as T;
            parent = svo.transform.parent;
            pos = svo.transform.localPosition;
            euler = svo.transform.localEulerAngles;
            scale = svo.transform.localScale;
        }

        Color c = GUI.skin.label.normal.textColor;
        GUI.skin.label.normal.textColor = Color.red;
        Handles.Label(svo.transform.position, "The GameObject is Locked.\nYou cannot tranform it.", GUI.skin.label);
        GUI.skin.label.normal.textColor = c;

        if (svo.transform.parent != parent)
        {
            svo.transform.parent = parent;
        }
        if (svo.transform.localPosition != pos)
        {
            svo.transform.localPosition = pos;
        }
        if (svo.transform.localEulerAngles != euler)
        {
            svo.transform.localEulerAngles = euler;
        }
        if (svo.transform.localScale != scale)
        {
            svo.transform.localScale = scale;
        }
    }
}
