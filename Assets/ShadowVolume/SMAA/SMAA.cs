using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// SMAA
// https://bitbucket.org/Unity-Technologies/cinematic-image-effects
public class SMAA
{
    private int m_AreaTex;
    private int m_SearchTex;
    private int m_Metrics;
    private int m_Params1;
    private int m_Params2;
    private int m_Params3;
    private int m_ReprojectionMatrix;
    private int m_SubsampleIndices;
    private int m_BlendTex;
    private int m_AccumulationTex;

    private RenderTexture m_Accumulation;

    private float m_FlipFlop = 1.0f;

    private Matrix4x4 m_PreviousViewProjectionMatrix;

    public SMAA()
    {
        m_AreaTex = Shader.PropertyToID("_AreaTex");
        m_SearchTex = Shader.PropertyToID("_SearchTex");
        m_Metrics = Shader.PropertyToID("_Metrics");
        m_Params1 = Shader.PropertyToID("_Params1");
        m_Params2 = Shader.PropertyToID("_Params2");
        m_Params3 = Shader.PropertyToID("_Params3");
        m_ReprojectionMatrix = Shader.PropertyToID("_ReprojectionMatrix");
        m_SubsampleIndices = Shader.PropertyToID("_SubsampleIndices");
        m_BlendTex = Shader.PropertyToID("_BlendTex");
        m_AccumulationTex = Shader.PropertyToID("_AccumulationTex");
    }

    public void Destroy()
    {
        if (m_Accumulation != null)
        {
            Object.DestroyImmediate(m_Accumulation);
            m_Accumulation = null;
        }
    }

    public void OnRenderImage(Camera camera, RenderTexture source, RenderTexture dest)
    {
        int width = camera.pixelWidth;
        int height = camera.pixelHeight;

        QualitySettings preset = QualitySettings.presetQualitySettings[0];

        // Pass IDs
        int passEdgeDetection = (int)GlobalSettings.defaultSettings.edgeDetectionMethod;
        int passBlendWeights = 4;
        int passNeighborhoodBlending = 5;
        int passResolve = 6;

        // Reprojection setup
        var viewProjectionMatrix = GL.GetGPUProjectionMatrix(Matrix4x4.zero, true) * camera.worldToCameraMatrix;

        // Uniforms
        material.SetTexture(m_AreaTex, areaTexture);
        material.SetTexture(m_SearchTex, searchTexture);

        material.SetVector(m_Metrics, new Vector4(1f / width, 1f / height, width, height));
        material.SetVector(m_Params1, new Vector4(preset.threshold, preset.depthThreshold, preset.maxSearchSteps, preset.maxDiagonalSearchSteps));
        material.SetVector(m_Params2, new Vector2(preset.cornerRounding, preset.localContrastAdaptationFactor));

        material.SetMatrix(m_ReprojectionMatrix, m_PreviousViewProjectionMatrix * Matrix4x4.Inverse(viewProjectionMatrix));

        float subsampleIndex = (m_FlipFlop < 0.0f) ? 2.0f : 1.0f;
        material.SetVector(m_SubsampleIndices, new Vector4(subsampleIndex, subsampleIndex, subsampleIndex, 0.0f));

        // Handle predication & depth-based edge detection
        Shader.DisableKeyword("USE_PREDICATION");

        if (GlobalSettings.defaultSettings.edgeDetectionMethod == EdgeDetectionMethod.Depth)
        {
            camera.depthTextureMode |= DepthTextureMode.Depth;
        }
        else if (PredicationSettings.defaultSettings.enabled)
        {
            camera.depthTextureMode |= DepthTextureMode.Depth;
            Shader.EnableKeyword("USE_PREDICATION");
            material.SetVector(m_Params3, new Vector3(PredicationSettings.defaultSettings.threshold, PredicationSettings.defaultSettings.scale, PredicationSettings.defaultSettings.strength));
        }

        // Diag search & corner detection
        Shader.DisableKeyword("USE_DIAG_SEARCH");
        Shader.DisableKeyword("USE_CORNER_DETECTION");

        if (preset.diagonalDetection)
            Shader.EnableKeyword("USE_DIAG_SEARCH");

        if (preset.cornerDetection)
            Shader.EnableKeyword("USE_CORNER_DETECTION");

        // UV-based reprojection (up to Unity 5.x)
        Shader.DisableKeyword("USE_UV_BASED_REPROJECTION");

        // Persistent textures and lazy-initializations
        if (m_Accumulation == null || (m_Accumulation.width != width || m_Accumulation.height != height))
        {
            if (m_Accumulation)
                RenderTexture.ReleaseTemporary(m_Accumulation);

            m_Accumulation = RenderTexture.GetTemporary(width, height, 0, source.format, RenderTextureReadWrite.Linear);
            m_Accumulation.hideFlags = HideFlags.HideAndDontSave;
        }

        RenderTexture rt1 = TempRT(width, height, source.format);
        Graphics.Blit(null, rt1, material, 0); // Clear

        // Edge Detection
        Graphics.Blit(source, rt1, material, passEdgeDetection);

        if (GlobalSettings.defaultSettings.debugPass == DebugPass.Edges)
        {
            Graphics.Blit(rt1, dest);
        }
        else
        {
            RenderTexture rt2 = TempRT(width, height, source.format);
            Graphics.Blit(null, rt2, material, 0); // Clear

            // Blend Weights
            Graphics.Blit(rt1, rt2, material, passBlendWeights);

            if (GlobalSettings.defaultSettings.debugPass == DebugPass.Weights)
            {
                Graphics.Blit(rt2, dest);
            }
            else
            {
                // Neighborhood Blending
                material.SetTexture(m_BlendTex, rt2);

                Graphics.Blit(source, dest, material, passNeighborhoodBlending);
            }

            RenderTexture.ReleaseTemporary(rt2);
        }

        RenderTexture.ReleaseTemporary(rt1);

        // Store the future-previous frame's view-projection matrix
        m_PreviousViewProjectionMatrix = viewProjectionMatrix;
    }

    private RenderTexture TempRT(int width, int height, RenderTextureFormat format)
    {
        // Skip the depth & stencil buffer creation when DebugPass is set to avoid flickering
        // int depthStencilBits = DebugPass == DebugPass.Off ? 24 : 0;
        int depthStencilBits = 0;
        return RenderTexture.GetTemporary(width, height, depthStencilBits, format, RenderTextureReadWrite.Linear);
    }

    private Shader m_Shader;
    public Shader shader
    {
        get
        {
            if (m_Shader == null)
                m_Shader = Shader.Find("Hidden/Subpixel Morphological Anti-aliasing");

            return m_Shader;
        }
    }

    private Texture2D m_AreaTexture;
    private Texture2D areaTexture
    {
        get
        {
            if (m_AreaTexture == null)
                m_AreaTexture = Resources.Load<Texture2D>("AreaTex");
            return m_AreaTexture;
        }
    }

    private Texture2D m_SearchTexture;
    private Texture2D searchTexture
    {
        get
        {
            if (m_SearchTexture == null)
                m_SearchTexture = Resources.Load<Texture2D>("SearchTex");
            return m_SearchTexture;
        }
    }

    private Material m_Material;
    private Material material
    {
        get
        {
            if (m_Material == null)
            {
                if(shader == null || !shader.isSupported)
                {
                    return null;
                }
                m_Material = new Material(shader);
                m_Material.hideFlags = HideFlags.DontSave;
            }

            return m_Material;
        }
    }

    public enum DebugPass
    {
        Off,
        Edges,
        Weights,
        Accumulation
    }

    public enum QualityPreset
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Ultra = 3,
        Custom
    }

    public enum EdgeDetectionMethod
    {
        Luma = 1,
        Color = 2,
        Depth = 3
    }

    public struct PredicationSettings
    {
        public bool enabled;

        public float threshold;

        public float scale;

        public float strength;

        public static PredicationSettings defaultSettings
        {
            get
            {
                return new PredicationSettings
                {
                    enabled = false,
                    threshold = 0.01f,
                    scale = 2f,
                    strength = 0.4f
                };
            }
        }
    }

    public struct GlobalSettings
    {
        public DebugPass debugPass;

        public QualityPreset quality;

        public EdgeDetectionMethod edgeDetectionMethod;

        public static GlobalSettings defaultSettings
        {
            get
            {
                return new GlobalSettings
                {
                    debugPass = DebugPass.Off,
                    quality = QualityPreset.High,
                    edgeDetectionMethod = EdgeDetectionMethod.Color
                };
            }
        }
    }

    public struct QualitySettings
    {
        public bool diagonalDetection;

        public bool cornerDetection;

        public float threshold;

        public float depthThreshold;

        public int maxSearchSteps;

        public int maxDiagonalSearchSteps;

        public int cornerRounding;

        public float localContrastAdaptationFactor;

        public static QualitySettings[] presetQualitySettings =
        {
                // Low
                new QualitySettings
                {
                    diagonalDetection = false,
                    cornerDetection = false,
                    threshold = 0.15f,
                    depthThreshold = 0.01f,
                    maxSearchSteps = 4,
                    maxDiagonalSearchSteps = 8,
                    cornerRounding = 25,
                    localContrastAdaptationFactor = 2f
                },

                // Medium
                new QualitySettings
                {
                    diagonalDetection = false,
                    cornerDetection = false,
                    threshold = 0.1f,
                    depthThreshold = 0.01f,
                    maxSearchSteps = 8,
                    maxDiagonalSearchSteps = 8,
                    cornerRounding = 25,
                    localContrastAdaptationFactor = 2f
                },

                // High
                new QualitySettings
                {
                    diagonalDetection = true,
                    cornerDetection = true,
                    threshold = 0.1f,
                    depthThreshold = 0.01f,
                    maxSearchSteps = 16,
                    maxDiagonalSearchSteps = 8,
                    cornerRounding = 25,
                    localContrastAdaptationFactor = 2f
                },

                // Ultra
                new QualitySettings
                {
                    diagonalDetection = true,
                    cornerDetection = true,
                    threshold = 0.05f,
                    depthThreshold = 0.01f,
                    maxSearchSteps = 32,
                    maxDiagonalSearchSteps = 16,
                    cornerRounding = 25,
                    localContrastAdaptationFactor = 2f
                },
            };
    }
}
