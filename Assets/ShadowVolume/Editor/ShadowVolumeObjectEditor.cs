using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;

[CustomEditor(typeof(ShadowVolumeObject))]
public class ShadowVolumeObjectEditor : ShadowVolumeNoTransformEditor<ShadowVolumeObject>
{
    private class Data : ScriptableObject
    {
        public Vector3 center = Vector3.zero;
        public Vector3 exts = Vector3.zero;
    }

    private Data data = null;

    private void OnSceneGUI()
    {
        DoOnSceneGUI();

        OnSceneGUI_Bounds();
    }

    private void OnSceneGUI_Bounds()
    {
        if (svo != null && 
            svo.meshFilter != null && svo.meshFilter.sharedMesh != null && 
            svo.sourceMeshFilter != null && svo.sourceMeshFilter.sharedMesh != null && 
            !Application.isPlaying)
        {
            {
                EditorGUI.BeginChangeCheck();

                Mesh mesh = svo.meshFilter.sharedMesh;
                Bounds bounds = mesh.bounds;

                if(data == null)
                {
                    data = new Data();
                    data.exts = bounds.extents;
                    data.center = bounds.center;
                }

                Handles.matrix = svo.transform.localToWorldMatrix;

                Vector3 center = data.center;
                Vector3 exts = data.exts;

                exts = Handles.ScaleHandle(exts, center, Quaternion.identity, 1);

                center.z -= exts.z;
                center = Handles.PositionHandle(center, Quaternion.identity);
                center.z += exts.z;

                DrawBounds(Color.black, svo.transform.localToWorldMatrix, center, exts);

                exts.x = Mathf.Max(exts.x, 0.01f);
                exts.y = Mathf.Max(exts.y, 0.01f);
                exts.z = Mathf.Max(exts.z, 0.01f);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(data, "Shadow Volume Bounds Edit");
                    data.exts = exts;
                    data.center = center;
                    bounds.extents = data.exts;
                    bounds.center = data.center;
                    mesh.bounds = bounds;
                    ShadowVolumeSetting.MarkSceneAsDirty();
                }
            }

            {
                Mesh mesh = svo.sourceMeshFilter.sharedMesh;
                Bounds bounds = mesh.bounds;
                Vector3 exts = bounds.extents;
                Vector3 center = bounds.center;

                DrawBounds(Color.white, svo.source.transform.localToWorldMatrix, center, exts);
            }

            //{
            //    DrawBounds(Color.gray, Matrix4x4.identity, svo.boundsAABB.center, svo.boundsAABB.extents);
            //}
        }
    }

    private void DrawBounds(Color c, Matrix4x4 mat, Vector3 center, Vector3 exts)
    {
        BeginHandlesColor(c);

        Handles.matrix = mat;

        Vector3 b1 = center - new Vector3(exts.x, exts.y, -exts.z);
        Vector3 b2 = center - new Vector3(exts.x, -exts.y, -exts.z);
        Vector3 b3 = center - new Vector3(-exts.x, -exts.y, -exts.z);
        Vector3 b4 = center - new Vector3(-exts.x, exts.y, -exts.z);

        Vector3 b5 = center - new Vector3(exts.x, exts.y, exts.z);
        Vector3 b6 = center - new Vector3(exts.x, -exts.y, exts.z);
        Vector3 b7 = center - new Vector3(-exts.x, -exts.y, exts.z);
        Vector3 b8 = center - new Vector3(-exts.x, exts.y, exts.z);

        Handles.DrawLine(b1, b2);
        Handles.DrawLine(b2, b3);
        Handles.DrawLine(b3, b4);
        Handles.DrawLine(b4, b1);

        Handles.DrawLine(b5, b6);
        Handles.DrawLine(b6, b7);
        Handles.DrawLine(b7, b8);
        Handles.DrawLine(b8, b5);

        Handles.DrawLine(b1, b5);
        Handles.DrawLine(b2, b6);
        Handles.DrawLine(b3, b7);
        Handles.DrawLine(b4, b8);

        EndHandlesColor();
    }

    private Color handlesColor;
    private void BeginHandlesColor(Color c)
    {
        handlesColor = Handles.color;
        Handles.color = c;
    }
    private void EndHandlesColor()
    {
        Handles.color = handlesColor;
    }
}
