using UnityEngine;
using System;

namespace UnityStandardAssets.CinematicEffects
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Image Effects/Cinematic/Lens Aberrations")]
    public class LensAberrations : MonoBehaviour
    {
        #region Attributes
        [AttributeUsage(AttributeTargets.Field)]
        public class SettingsGroup : Attribute
        {}

        [AttributeUsage(AttributeTargets.Field)]
        public class AdvancedSetting : Attribute
        {}
        #endregion

        #region Settings
        [Serializable]
        public struct DistortionSettings
        {
            public bool enabled;

            [Range(-100f, 100f), Tooltip("Distortion amount.")]
            public float amount;

            [Range(-1f, 1f), Tooltip("Distortion center point (X axis).")]
            public float centerX;

            [Range(-1f, 1f), Tooltip("Distortion center point (Y axis).")]
            public float centerY;

            [Range(0f, 1f), Tooltip("Amount multiplier on X axis. Set it to 0 to disable distortion on this axis.")]
            public float amountX;

            [Range(0f, 1f), Tooltip("Amount multiplier on Y axis. Set it to 0 to disable distortion on this axis.")]
            public float amountY;

            [Range(0.01f, 5f), Tooltip("Global screen scaling.")]
            public float scale;

            public static DistortionSettings defaultSettings
            {
                get
                {
                    return new DistortionSettings
                    {
                        enabled = false,
                        amount = 0f,
                        centerX = 0f,
                        centerY = 0f,
                        amountX = 1f,
                        amountY = 1f,
                        scale = 1f
                    };
                }
            }
        }

        [Serializable]
        public struct VignetteSettings
        {
            public bool enabled;

            [ColorUsage(false)]
            [Tooltip("Vignette color. Use the alpha channel for transparency.")]
            public Color color;

            [Tooltip("Sets the vignette center point (screen center is [0.5,0.5]).")]
            public Vector2 center;

            [Range(0f, 3f), Tooltip("Amount of vignetting on screen.")]
            public float intensity;

            [Range(0.01f, 3f), Tooltip("Smoothness of the vignette borders.")]
            public float smoothness;
            
            [AdvancedSetting, Range(0f, 1f), Tooltip("Lower values will make a square-ish vignette.")]
            public float roundness;

            [Range(0f, 1f), Tooltip("Blurs the corners of the screen. Leave this at 0 to disable it.")]
            public float blur;

            [Range(0f, 1f), Tooltip("Desaturate the corners of the screen. Leave this to 0 to disable it.")]
            public float desaturate;

            public static VignetteSettings defaultSettings
            {
                get
                {
                    return new VignetteSettings
                    {
                        enabled = false,
                        color = new Color(0f, 0f, 0f, 1f),
                        center = new Vector2(0.5f, 0.5f),
                        intensity = 1.4f,
                        smoothness = 0.8f,
                        roundness = 1f,
                        blur = 0f,
                        desaturate = 0f
                    };
                }
            }
        }

        [Serializable]
        public struct ChromaticAberrationSettings
        {
            public bool enabled;

            [ColorUsage(false)]
            [Tooltip("Channels to apply chromatic aberration to.")]
            public Color color;

            [Range(-50f, 50f)]
            [Tooltip("Amount of tangential distortion.")]
            public float amount;

            public static ChromaticAberrationSettings defaultSettings
            {
                get
                {
                    return new ChromaticAberrationSettings
                    {
                        enabled = false,
                        color = Color.green,
                        amount = 0f
                    };
                }
            }
        }
        #endregion

        [SettingsGroup]
        public DistortionSettings distortion = DistortionSettings.defaultSettings;

        [SettingsGroup]
        public VignetteSettings vignette = VignetteSettings.defaultSettings;

        [SettingsGroup]
        public ChromaticAberrationSettings chromaticAberration = ChromaticAberrationSettings.defaultSettings;

        private enum Pass
        {
            BlurPrePass,
            Chroma,
            Distort,
            Vignette,
            ChromaDistort,
            ChromaVignette,
            DistortVignette,
            ChromaDistortVignette
        }

        [SerializeField]
        private Shader m_Shader;
        public Shader shader
        {
            get
            {
                if (m_Shader == null)
                    m_Shader = Shader.Find("Hidden/LensAberrations");

                return m_Shader;
            }
        }

        private Material m_Material;
        public Material material
        {
            get
            {
                if (m_Material == null)
                    m_Material = ImageEffectHelper.CheckShaderAndCreateMaterial(shader);

                return m_Material;
            }
        }

        private RenderTextureUtility m_RTU;

        private void OnEnable()
        {
            if (!ImageEffectHelper.IsSupported(shader, false, false, this))
                enabled = false;

            m_RTU = new RenderTextureUtility();
        }

        private void OnDisable()
        {
            if (m_Material != null)
                DestroyImmediate(m_Material);

            m_Material = null;
            m_RTU.ReleaseAllTemporaryRenderTextures();
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!vignette.enabled && !chromaticAberration.enabled && !distortion.enabled)
            {
                Graphics.Blit(source, destination);
                return;
            }

            material.shaderKeywords = null;
            
            if (distortion.enabled)
            {
                float amount = 1.6f * Math.Max(Mathf.Abs(distortion.amount), 1f);
                float theta = 0.01745329251994f * Math.Min(160f, amount);
                float sigma = 2f * Mathf.Tan(theta * 0.5f);
                var p0 = new Vector4(distortion.centerX, distortion.centerY, Mathf.Max(distortion.amountX, 1e-4f), Mathf.Max(distortion.amountY, 1e-4f));
                var p1 = new Vector3(distortion.amount >= 0f ? theta : 1f / theta, sigma, 1f / distortion.scale);
                material.EnableKeyword(distortion.amount >= 0f ? "DISTORT" : "UNDISTORT");
                material.SetVector("_DistCenterScale", p0);
                material.SetVector("_DistAmount", p1);
            }

            if (chromaticAberration.enabled)
            {
                material.EnableKeyword("CHROMATIC_ABERRATION");
                var chromaParams = new Vector4(chromaticAberration.color.r, chromaticAberration.color.g, chromaticAberration.color.b, chromaticAberration.amount * 0.001f);
                material.SetVector("_ChromaticAberration", chromaParams);
            }

            if (vignette.enabled)
            {
                material.SetColor("_VignetteColor", vignette.color);

                if (vignette.blur > 0f)
                {
                    // Downscale + gaussian blur (2 passes)
                    int w = source.width / 2;
                    int h = source.height / 2;
                    var rt1 = m_RTU.GetTemporaryRenderTexture(w, h, 0, source.format);
                    var rt2 = m_RTU.GetTemporaryRenderTexture(w, h, 0, source.format);

                    material.SetVector("_BlurPass", new Vector2(1f / w, 0f));
                    Graphics.Blit(source, rt1, material, (int)Pass.BlurPrePass);

                    if (distortion.enabled)
                    {
                        material.DisableKeyword("DISTORT");
                        material.DisableKeyword("UNDISTORT");
                    }

                    material.SetVector("_BlurPass", new Vector2(0f, 1f / h));
                    Graphics.Blit(rt1, rt2, material, (int)Pass.BlurPrePass);

                    material.SetVector("_BlurPass", new Vector2(1f / w, 0f));
                    Graphics.Blit(rt2, rt1, material, (int)Pass.BlurPrePass);
                    material.SetVector("_BlurPass", new Vector2(0f, 1f / h));
                    Graphics.Blit(rt1, rt2, material, (int)Pass.BlurPrePass);

                    material.SetTexture("_BlurTex", rt2);
                    material.SetFloat("_VignetteBlur", vignette.blur * 3f);
                    material.EnableKeyword("VIGNETTE_BLUR");
                    
                    if (distortion.enabled)
                        material.EnableKeyword(distortion.amount >= 0f ? "DISTORT" : "UNDISTORT");
                }

                if (vignette.desaturate > 0f)
                {
                    material.EnableKeyword("VIGNETTE_DESAT");
                    material.SetFloat("_VignetteDesat", 1f - vignette.desaturate);
                }
                
                material.SetVector("_VignetteCenter", vignette.center);

                if (Mathf.Approximately(vignette.roundness, 1f))
                {
                    material.EnableKeyword("VIGNETTE_CLASSIC");
                    material.SetVector("_VignetteSettings", new Vector2(vignette.intensity, vignette.smoothness));
                }
                else
                {
                    material.EnableKeyword("VIGNETTE_FILMIC");
                    float roundness = (1f - vignette.roundness) * 6f + vignette.roundness;
                    material.SetVector("_VignetteSettings", new Vector3(vignette.intensity, vignette.smoothness, roundness));
                }
            }
            
            int pass = 0;

            if (vignette.enabled && chromaticAberration.enabled && distortion.enabled)
                pass = (int)Pass.ChromaDistortVignette;
            else if (vignette.enabled && chromaticAberration.enabled)
                pass = (int)Pass.ChromaVignette;
            else if (vignette.enabled && distortion.enabled)
                pass = (int)Pass.DistortVignette;
            else if (chromaticAberration.enabled && distortion.enabled)
                pass = (int)Pass.ChromaDistort;
            else if (vignette.enabled)
                pass = (int)Pass.Vignette;
            else if (chromaticAberration.enabled)
                pass = (int)Pass.Chroma;
            else if (distortion.enabled)
                pass = (int)Pass.Distort;
            
            Graphics.Blit(source, destination, material, pass);

            m_RTU.ReleaseAllTemporaryRenderTextures();
        }
    }
}
