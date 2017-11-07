using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShadowVolumeObject : MonoBehaviour
{
    public GameObject source = null;

	[SerializeField]
	[HideInInspector]
    public MeshFilter sourceMeshFilter = null;

	[SerializeField]
	[HideInInspector]
    public MeshRenderer sourceMeshRenderer = null;

	[SerializeField]
	[HideInInspector]
    public MeshFilter meshFilter = null;

	[SerializeField]
	[HideInInspector]
	public Matrix4x4 l2w;

    [SerializeField]
    [HideInInspector]
    public Vector3 wPos;

    public bool IsVisible()
    {
        return sourceMeshFilter == null || sourceMeshRenderer == null || meshFilter == null ? false : sourceMeshRenderer.isVisible;
    }
}
