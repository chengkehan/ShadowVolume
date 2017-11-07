using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class ShadowVolumeCamera : MonoBehaviour
{
    [SerializeField]
    [HideInInspector]
    public Color shadowColor = new Color(0.5f, 0.5f, 0.5f, 1.0f);

    [SerializeField]
    [HideInInspector]
    public bool isTwoSideStencil = false;

    [SerializeField]
    [HideInInspector]
    public bool isRenderTextureComposite = false;

    [SerializeField]
    [HideInInspector]
    public bool anti_aliasing = true;

    [SerializeField]
    [HideInInspector]
    public float shadowDistance = 0.0f;

    private const string CB_NAME = "Shadow Volume Drawing CommandBuffer";

    private Material drawingMtrl = null;

    private ACommandBuffer cbBeforeOpaque = null;

    private ACommandBuffer cbAfterAlpha = null;

    private Camera mainCam = null;

    private Mesh screenMesh = null;

    private int shadowColorUniformName = 0;

    private RenderTexture sceneViewRT = null;

    private RenderTexture mainCamRT = null;

    private RenderTexture compositeRT = null;

    private int shadowVolumeRT = 0;

	private int shadowVolumeColorRT = 0;

    private int shadowDistanceUniformId = 0;

    private SMAA smaa = null;

    private bool sceneViewFirstTime = false;

    private List<ImageEffectItem> imageEffects = null;

    private ShadowVolumeObject[] static_svos = null;

    private ShadowVolumeCombined[] static_combinedSVOs = null;

    private bool boundsNeedUpdate = true;

    private TRI_VALUE isSceneViewCam = TRI_VALUE.UNDEFINED;

#if UNITY_EDITOR
    public static void DrawAllCameras_Editor()
    {
        ShadowVolumeCamera asvc = null;
        ShadowVolumeCamera[] svcs = FindObjectsOfType<ShadowVolumeCamera>();
        foreach (var svc in svcs)
        {
            svc.Update();
            svc.UpdateCommandBuffers();
            asvc = svc;
        }

        Camera[] sceneViewCams = SceneView.GetAllSceneCameras();
        foreach (var sceneViewCam in sceneViewCams)
        {
            ShadowVolumeCamera svc = sceneViewCam.GetComponent<ShadowVolumeCamera>();
            if (svc != null)
            {
                if(asvc != null)
                {
                    svc.shadowColor = asvc.shadowColor;
                    svc.isTwoSideStencil = asvc.isTwoSideStencil;
                    svc.isRenderTextureComposite = asvc.isRenderTextureComposite;
                    svc.anti_aliasing = asvc.anti_aliasing;
                    svc.shadowDistance = asvc.shadowDistance;
                }
                svc.Update();
                svc.UpdateCommandBuffers();
            }
        }
    }
#endif

	private void UpdateCommandBuffers()
    {
        ACommandBuffer cbBeforeOpaque = GetBeforeOpaqueCB();

        CollectImageEffects();

        cbBeforeOpaque.AddToCamera(mainCam);

        mainCam.allowMSAA = isRenderTextureComposite ? false : anti_aliasing;
        mainCam.allowHDR = false; // HDR is not supported

        cbBeforeOpaque.CB.Clear();
        if (isRenderTextureComposite)
        {
            cbBeforeOpaque.CB.SetRenderTarget(GetMainCamRT());
            cbBeforeOpaque.CB.ClearRenderTarget(true, true, new Color(0, 0, 0, 0), 1.0f);
            if (IsSceneViewCamera())
            { 
                cbBeforeOpaque.CB.Blit(sceneViewRT, GetMainCamRT());
            }
        }

        UpdateCommandBuffer_AfterAlpha(null);

        if (!isRenderTextureComposite)
        {
            ReleaseRenderTextureCompositeResources();
        }
    }

    private void UpdateCommandBuffer_AfterAlpha(ShadowVolumeObject[] svos)
    {
        ACommandBuffer cbAfterAlpha = GetAfterAlphaCB();

        cbAfterAlpha.AddToCamera(mainCam);

        cbAfterAlpha.CB.Clear();
        if (isRenderTextureComposite)
        {
            RenderTexture mainCamRT = GetMainCamRT();
            cbAfterAlpha.CB.GetTemporaryRT(shadowVolumeRT, mainCamRT.width, mainCamRT.height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            cbAfterAlpha.CB.SetRenderTarget(shadowVolumeRT, mainCamRT);
            cbAfterAlpha.CB.ClearRenderTarget(false, true, Color.white);
        }

        ReleaseSVOs();

        ShadowVolumeCombined[] combinedObjs = static_combinedSVOs == null || !Application.isPlaying ? FindObjectsOfType<ShadowVolumeCombined>() : static_combinedSVOs;
        static_combinedSVOs = combinedObjs;
        if (combinedObjs != null && combinedObjs.Length > 0)
        {
            cbAfterAlpha.CB.DrawMesh(screenMesh, Matrix4x4.identity, drawingMtrl, 0, 3);
            foreach (var combinedObj in combinedObjs)
            {
                MeshFilter mf = combinedObj.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    if (isTwoSideStencil)
                    {
                        cbAfterAlpha.CB.DrawMesh(mf.sharedMesh, Matrix4x4.identity, drawingMtrl, 0, 4);
                    }
                    else
                    {
                        cbAfterAlpha.CB.DrawMesh(mf.sharedMesh, Matrix4x4.identity, drawingMtrl, 0, 0);
                        cbAfterAlpha.CB.DrawMesh(mf.sharedMesh, Matrix4x4.identity, drawingMtrl, 0, 1);
                    }
                }
            }
            cbAfterAlpha.CB.DrawMesh(screenMesh, Matrix4x4.identity, drawingMtrl, 0, isRenderTextureComposite ? 6 : 2);
        }
        else
        {
            cbAfterAlpha.CB.DrawMesh(screenMesh, Matrix4x4.identity, drawingMtrl, 0, 3);
            ShadowVolumeObject[] svObjs = svos == null || !Application.isPlaying ? FindObjectsOfType<ShadowVolumeObject>() : svos;
            static_svos = svObjs;
            UpdateBounds();
            if (svObjs != null)
            {
                Vector3 camWPos = mainCam.transform.position;
                bool isShadowDistanceEnabled = IsShadowDistanceEnabled();

                foreach (var svObj in svObjs)
                {
                    if(IsShadowVolulmeObjectVisible(svObj, isShadowDistanceEnabled, ref camWPos))
                    {
                        MeshFilter mf = svObj.meshFilter;
                        if (mf != null && mf.sharedMesh != null)
                        {
							Matrix4x4 l2w = svObj.l2w;
                            bool twoSubMeshes = mf.sharedMesh.subMeshCount == 2;
                            if (isTwoSideStencil)
                            {
                                cbAfterAlpha.CB.DrawMesh(mf.sharedMesh, l2w, drawingMtrl, 0, 4);
                                if (twoSubMeshes)
                                {
                                    cbAfterAlpha.CB.DrawMesh(mf.sharedMesh, l2w, drawingMtrl, 1, 4);
                                }
                            }
                            else
                            {
                                cbAfterAlpha.CB.DrawMesh(mf.sharedMesh, l2w, drawingMtrl, 0, 0);
                                if (twoSubMeshes)
                                {
                                    cbAfterAlpha.CB.DrawMesh(mf.sharedMesh, l2w, drawingMtrl, 1, 0);
                                }
                                cbAfterAlpha.CB.DrawMesh(mf.sharedMesh, l2w, drawingMtrl, 0, 1);
                                if (twoSubMeshes)
                                {
                                    cbAfterAlpha.CB.DrawMesh(mf.sharedMesh, l2w, drawingMtrl, 1, 1);
                                }
                            }
                        }
                    }
                }
            }
            cbAfterAlpha.CB.DrawMesh(screenMesh, Matrix4x4.identity, drawingMtrl, 0, isRenderTextureComposite ? 6 : 2);
        }

        if (isRenderTextureComposite)
        {
            cbAfterAlpha.CB.SetGlobalTexture(shadowVolumeColorRT, GetMainCamRT());
            cbAfterAlpha.CB.Blit(null, GetCompositeRT(), drawingMtrl, 5);
            cbAfterAlpha.CB.ReleaseTemporaryRT(shadowVolumeRT);
        }
    }

    private void OnEnable()
    {
        if (mainCam != null)
        {
            Update();
            UpdateCommandBuffers();
            SetSceneViewCamsEnabled(true);
        }
    }

    private void OnDisable()
    {
        RemoveCBFromCamera();
        ReleaseRenderTextureCompositeResources();
        SetSceneViewCamsEnabled(false);
    }

    private void OnPreRender()
    {
        if(mainCam == null)
        {
            return;
        }

        if (isRenderTextureComposite)
        {
            InitSceneViewRT();

			bool sizeChanged = false;
			mainCam.targetTexture = GetMainCamRT(out sizeChanged);
			if(sizeChanged || sceneViewFirstTime)
			{
                sceneViewFirstTime = false;
				UpdateCommandBuffers();
			}
            else
            {
                UpdateSVOS();
            }
        }
        else
        {
            UpdateSVOS();
        }
    }

    private void OnPostRender()
    {
        if (mainCam == null)
        {
            return;
        }

        if (isRenderTextureComposite)
        {
            if (IsSceneViewCamera())
            {
                mainCam.targetTexture = sceneViewRT;
                RenderTexture imageEffectRT = DrawImageEffects(compositeRT);
                if (anti_aliasing)
                {
                    GetSMAA().OnRenderImage(mainCam, imageEffectRT, sceneViewRT);
                }
                else
                {
                    Graphics.Blit(imageEffectRT, sceneViewRT);
                }
                if(imageEffectRT != compositeRT)
                {
                    RenderTexture.ReleaseTemporary(imageEffectRT);
                }
            }
            else
            {
                RenderTexture mainCamRT = GetMainCamRT();

                mainCam.targetTexture = null;
                RenderTexture imageEffectRT = DrawImageEffects(compositeRT);
                if (anti_aliasing)
                {
                    GetSMAA().OnRenderImage(mainCam, imageEffectRT, mainCamRT);
                }
                else
                {
                    Graphics.Blit(imageEffectRT, mainCamRT);
                }
                Graphics.Blit(mainCamRT, null as RenderTexture);
                if(imageEffectRT != compositeRT)
                {
                    RenderTexture.ReleaseTemporary(imageEffectRT);
                }
            }
        }
    }

    private void UpdateSVOS()
    {
        if (static_svos == null)
        {
            return;
        }

        UpdateCommandBuffer_AfterAlpha(static_svos);
    }

    private void UpdateBounds()
    {
        if(static_svos == null || !boundsNeedUpdate || !Application.isPlaying)
        {
            return;
        }

        boundsNeedUpdate = false;

        int numSVOs = static_svos.Length;
        for(int i = 0; i < numSVOs; ++i)
        {
            ShadowVolumeObject svo = static_svos[i];
            if(svo.sourceMeshFilter != null && svo.sourceMeshFilter.sharedMesh != null && 
                svo.meshFilter != null && svo.meshFilter.sharedMesh != null)
            {
                svo.sourceMeshFilter.sharedMesh.bounds = svo.meshFilter.sharedMesh.bounds;
            }
        }
    }

    private void OnValidate()
    {
        SetupSceneViewCameras();
    }

    private void Update()
    { 
        UpdateMaterialUniforms();
         
#if UNITY_EDITOR
        ImageEffectsChecking();
#endif
    }

#if UNITY_EDITOR
    private void ImageEffectsChecking()
    {
        if(imageEffects == null)
        {
            return;
        }

        // Delete destroied ImageEffects
        int numEffects = imageEffects.Count;
        for(int i = 0; i < numEffects; ++i)
        {
            if(imageEffects[i].mono == null)
            {
                imageEffects.RemoveAt(i);
                --i;
                --numEffects;
            }
        }

        // Add new ImageEffects
        if(Selection.activeGameObject == gameObject)
        {
            CollectImageEffects();

            Camera[] sceneViewCams = SceneView.GetAllSceneCameras();
            foreach(var sceneViewCam in sceneViewCams)
            {
                ShadowVolumeCamera svc = sceneViewCam.GetComponent<ShadowVolumeCamera>();
                if(svc != null)
                {
                    svc.CollectImageEffectsDelay_Editor();
                }
            }
        }
    }
#endif

    private void Start()
    {
        InitMaterialUniformNames();

        cbAfterAlpha = new ACommandBuffer("Shadow Volume After Alpha CB", CameraEvent.AfterForwardAlpha);
        cbBeforeOpaque = new ACommandBuffer("Shadow Volume Before Opaque CB", CameraEvent.BeforeForwardOpaque);

        drawingMtrl = new Material(Shader.Find("Hidden/ShadowVolume/Drawing"));
        drawingMtrl.name = "Shadow Volume Drawing Material";

        shadowVolumeRT = Shader.PropertyToID("_ShadowVolumeRT");
		shadowVolumeColorRT = Shader.PropertyToID("_ShadowVolumeColorRT");
        shadowDistanceUniformId = Shader.PropertyToID("_ShadowVolumeDistance");

        mainCam = GetComponent<Camera>();

        sceneViewFirstTime = IsSceneViewCamera();

        InitSceneViewRT();

        CreateScreenMesh();

        UpdateCommandBuffers();

        SetupSceneViewCameras();
    }

    private void OnDestroy()
    {
        if(drawingMtrl != null)
        {
            DestroyImmediate(drawingMtrl);
            drawingMtrl = null;
        }

        if (mainCam != null)
        {
            DestroySceneViewCameras();
            mainCam.targetTexture = null;
            mainCam = null;
        }

        DestroyScreenMesh();
        ReleaseRenderTextureCompositeResources();
        ReleaseImageEffects();

        if(cbAfterAlpha != null)
        {
            cbAfterAlpha.Destroy();
            cbAfterAlpha = null;
        }

        if(cbBeforeOpaque != null)
        {
            cbBeforeOpaque.Destroy();
            cbBeforeOpaque = null;
        }
    }

    private void SetupSceneViewCameras()
    {
#if UNITY_EDITOR
        if (IsSceneViewCamera())
        {
            return;
        }

        Camera[] sceneViewCams = SceneView.GetAllSceneCameras();
        foreach (var sceneViewCam in sceneViewCams)
        {
            ShadowVolumeCamera svc = sceneViewCam.GetComponent<ShadowVolumeCamera>();
            if (svc == null)
            {
                svc = sceneViewCam.gameObject.AddComponent<ShadowVolumeCamera>();
            }
            svc.shadowColor = shadowColor;
            svc.isTwoSideStencil = isTwoSideStencil;
            svc.isRenderTextureComposite = isRenderTextureComposite;
            svc.anti_aliasing = anti_aliasing;
            svc.shadowDistance = shadowDistance;
        }
#endif
    }

    private void DestroySceneViewCameras()
    {
#if UNITY_EDITOR
        if (IsSceneViewCamera())
        {
            return;
        }

        Camera[] sceneViewCams = SceneView.GetAllSceneCameras();
        foreach (var sceneViewCam in sceneViewCams)
        {
            ShadowVolumeCamera svc = sceneViewCam.GetComponent<ShadowVolumeCamera>();
            if (svc != null)
            {
                DestroyImmediate(svc);
            }
        }
#endif
    }

    private void SetSceneViewCamsEnabled(bool enabled)
    {
#if UNITY_EDITOR
        if(IsSceneViewCamera())
        {
            return;
        }

        Camera[] sceneViewCams = SceneView.GetAllSceneCameras();
        foreach (var sceneViewCam in sceneViewCams)
        {
            ShadowVolumeCamera svc = sceneViewCam.GetComponent<ShadowVolumeCamera>();
            if (svc != null)
            {
                svc.enabled = enabled;
            }
        }
#endif
    }

    private void InitMaterialUniformNames()
    {
        shadowColorUniformName = Shader.PropertyToID("_ShadowColor");
    }

    private void UpdateMaterialUniforms()
    {
        if(drawingMtrl != null)
        {
            if (IsShadowDistanceEnabled())
            {
                drawingMtrl.SetFloat(shadowDistanceUniformId, shadowDistance);
            }
            drawingMtrl.SetColor(shadowColorUniformName, shadowColor);
        }
    }

    private void CreateScreenMesh()
    {
        if (screenMesh == null)
        {
            screenMesh = new Mesh();
            screenMesh.name = "ShadowVolume ScreenQuad";
            screenMesh.vertices = new Vector3[] {
                new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0)
            };
            screenMesh.triangles = new int[] { 0, 1, 2, 2, 3, 0 };
        }
    }

    private void DestroyScreenMesh()
    {
        if (screenMesh != null)
        {
            DestroyImmediate(screenMesh);
            screenMesh = null;
        }
    }

    private ACommandBuffer GetAfterAlphaCB()
    {
        if(cbAfterAlpha == null)
        {
            cbAfterAlpha = new ACommandBuffer("Shadow Volume After Alpha CB", CameraEvent.AfterForwardAlpha);
        }
        return cbAfterAlpha;
    }

    private ACommandBuffer GetBeforeOpaqueCB()
    {
        if (cbBeforeOpaque == null)
        {
            cbBeforeOpaque = new ACommandBuffer("Shadow Volume Before Opaque CB", CameraEvent.BeforeForwardOpaque);
        }
        return cbBeforeOpaque;
    }

    private void RemoveCBFromCamera()
    {
        if(cbAfterAlpha != null)
        {
            cbAfterAlpha.RemoveFromCamera();
        }
        if(cbBeforeOpaque != null)
        {
            cbBeforeOpaque.RemoveFromCamera();
        }
    }

    private void InitSceneViewRT()
    {
        sceneViewRT = IsSceneViewCamera() ? mainCam.targetTexture : null;
    }

	private RenderTexture GetMainCamRT()
	{
		bool sizeChanged = false;
		RenderTexture rt = GetMainCamRT(out sizeChanged);
		return rt;
	}

	private RenderTexture GetMainCamRT(out bool sizeChanged)
    {
		sizeChanged = mainCamRT != null && (mainCamRT.width != mainCam.pixelWidth || mainCamRT.height != mainCam.pixelHeight);
		if (mainCamRT == null || sizeChanged)
        {
            ReleaseMainCamRT();
			mainCamRT = new RenderTexture(mainCam.pixelWidth, mainCam.pixelHeight, 24, RenderTextureFormat.ARGB32);
            mainCamRT.name = "Shadow Volume Main Camera RT";
        }
        return mainCamRT;
    }

    private void ReleaseMainCamRT()
    {
        if (mainCamRT != null)
        {
            DestroyImmediate(mainCamRT);
            mainCamRT = null;
        }
    }

    private RenderTexture GetCompositeRT()
    {
		if(compositeRT == null || (compositeRT.width != mainCam.pixelWidth || compositeRT.height != mainCam.pixelHeight))
        {
            ReleaseCompositeRT();
			compositeRT = new RenderTexture(mainCam.pixelWidth, mainCam.pixelHeight, 0, RenderTextureFormat.ARGB32);
            compositeRT.name = "Shadow Volume Composite RT";
        }
        return compositeRT;
    }

    private void ReleaseCompositeRT()
    {
        if(compositeRT != null)
        {
            DestroyImmediate(compositeRT);
            compositeRT = null;
        }
    }

    private SMAA GetSMAA()
    {
        if(smaa == null)
        {
            smaa = new SMAA();
        }
        return smaa;
    }

    private void ReleaseSMAA()
    {
        if(smaa != null)
        {
            smaa.Destroy();
            smaa = null;
        }
    }

    private void ReleaseRenderTextureCompositeResources()
    {
        ReleaseMainCamRT();
        ReleaseCompositeRT();
        ReleaseSMAA();
    }

    private RenderTexture DrawImageEffects(RenderTexture source)
    {
        if(imageEffects == null || imageEffects.Count == 0)
        {
            return source;
        }
        else
        {
            RenderTexture src = source;
            RenderTexture dest = null;

            int numEffects = imageEffects.Count;
            for(int i = 0; i < numEffects; ++i)
            {
                MonoBehaviour mono = imageEffects[i].mono;
                if(mono == null)
                {
                    continue;
                }

                if (mono.enabled)
                {
                    if(src == null)
                    {
                        src = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                    }
                    if(dest == null)
                    {
                        dest = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                    }

                    ShadowVolumeImageEffect imageEffect = imageEffects[i].effect;
                    imageEffect.DrawImageEffect(src, dest);

                    RenderTexture temp = src;
                    src = dest;
                    dest = src;
                }
            }

            if(src == null || dest == null)
            {
                return source;
            }
            else
            {
                return src == source ? dest : src;
            }
        }
    }

#if UNITY_EDITOR
    private void CollectImageEffectsDelay_Editor()
    {
        EditorApplication.delayCall += _CollectImageEffectsDelay_Editor;
    }

    private void _CollectImageEffectsDelay_Editor()
    {
        EditorApplication.delayCall -= _CollectImageEffectsDelay_Editor;
        CollectImageEffects();
        SceneView.RepaintAll();
    }
#endif

    private void CollectImageEffects()
    {
        if(mainCam == null)
        {
            return;
        }
        if(imageEffects == null)
        {
            imageEffects = new List<ImageEffectItem>();
        }
        imageEffects.Clear();

        MonoBehaviour[] monos = gameObject.GetComponents<MonoBehaviour>();
        if(monos != null)
        {
            foreach(var mono in monos)
            {
                if(mono is ShadowVolumeImageEffect)
                {
                    ImageEffectItem iei = new ImageEffectItem();
                    iei.effect = mono as ShadowVolumeImageEffect;
                    iei.mono = mono;
                    imageEffects.Add(iei);
                }
            }
        }
    }

    private void ReleaseImageEffects()
    {
        if(imageEffects != null)
        {
            imageEffects.Clear();
            imageEffects = null;
        }
    }

    private bool IsShadowDistanceEnabled()
    {
        return shadowDistance > 0.0001f;
    }

    private bool IsShadowVolulmeObjectVisible(ShadowVolumeObject svo, bool isShadowDistanceEnabled, ref Vector3 camWPos)
    {
        bool visible = svo.IsVisible();

        if(isShadowDistanceEnabled)
        {
            float dist = (camWPos - svo.wPos).magnitude;
            visible = dist < shadowDistance;
        }

        return visible;
    }

    private void ReleaseSVOs()
    {
        static_svos = null;
    }

    private bool IsSceneViewCamera()
    {
#if UNITY_EDITOR
        if (isSceneViewCam == TRI_VALUE.UNDEFINED)
        {
            Camera[] sceneViewCames = SceneView.GetAllSceneCameras();
            isSceneViewCam = System.Array.IndexOf(sceneViewCames, mainCam) != -1 ? TRI_VALUE.YES : TRI_VALUE.NO;
        }
        return isSceneViewCam == TRI_VALUE.YES ? true : false;
#else
        return false;
#endif
    }

    private enum TRI_VALUE
    {
        UNDEFINED,
        YES,
        NO
    }

    private class ImageEffectItem
    {
        public ShadowVolumeImageEffect effect = null;

        public MonoBehaviour mono = null;
    }

    private class ACommandBuffer
    {
        private CommandBuffer cb = null;
        public CommandBuffer CB
        {
            get
            {
                return cb;
            }
        }

        private bool isAdded = false;

        private Camera cam = null;

        private CameraEvent camEvent;

        public ACommandBuffer(string name, CameraEvent camEvent)
        {
            cb = new CommandBuffer();
            cb.name = name;
            this.camEvent = camEvent;
        }

        public void AddToCamera(Camera cam)
        {
            if(cam == null)
            {
                return;
            }
            if(cam != this.cam && isAdded)
            {
                RemoveFromCamera();
            }
            if(isAdded)
            {
                return;
            }

            isAdded = true;
            this.cam = cam;
            cam.AddCommandBuffer(camEvent, cb);
        }

        public void RemoveFromCamera()
        {
            if(!isAdded)
            {
                return;
            }

            isAdded = false;

            cam.RemoveCommandBuffer(camEvent, cb);
            cam = null;
        }

        public void Destroy()
        {
            if(cb != null)
            {
                RemoveFromCamera();

                cb.Clear();
                cb.Release();
                cb.Dispose();
                cb = null;
            }
        }
    }
}
