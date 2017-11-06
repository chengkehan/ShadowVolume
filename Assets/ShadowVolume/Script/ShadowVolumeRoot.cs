using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShadowVolumeRoot : MonoBehaviour
{
    [SerializeField]
    public Material debugMtrl = null;
    public Material DebugMaterial
    {
        get
        {
            if(debugMtrl == null)
            {
                debugMtrl = new Material(Shader.Find("Hidden/ShadowVolume/Debug"));
            }
            return debugMtrl;
        }
    }
}
