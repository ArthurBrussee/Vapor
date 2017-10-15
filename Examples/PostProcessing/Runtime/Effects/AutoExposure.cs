using System;

namespace UnityEngine.Rendering.PostProcessing
{
    public enum EyeAdaptation
    {
        Progressive,
        Fixed
    }

    [Serializable]
    public sealed class EyeAdaptationParameter : ParameterOverride<EyeAdaptation> {}

    [Serializable]
    [PostProcess(typeof(AutoExposureRenderer), "Unity/Auto Exposure")]
    public sealed class AutoExposure : PostProcessEffectSettings
    {
        [MinMax(1f, 99f), DisplayName("Filtering (%)"), Tooltip("Filters the bright & dark part of the histogram when computing the average luminance to avoid very dark pixels & very bright pixels from contributing to the auto exposure. Unit is in percent.")]
        public Vector2Parameter filtering = new Vector2Parameter { value = new Vector2(50f, 95f) };

        [Range(LogHistogram.rangeMin, LogHistogram.rangeMax), DisplayName("Minimum (EV)"), Tooltip("Minimum average luminance to consider for auto exposure (in EV).")]
        public FloatParameter minLuminance = new FloatParameter { value = 0f };

        [Range(LogHistogram.rangeMin, LogHistogram.rangeMax), DisplayName("Maximum (EV)"), Tooltip("Maximum average luminance to consider for auto exposure (in EV).")]
        public FloatParameter maxLuminance = new FloatParameter { value = 0f };

        [Min(0f), Tooltip("Exposure bias. Use this to offset the global exposure of the scene.")]
        public FloatParameter keyValue = new FloatParameter { value = 1f };

        [DisplayName("Type"), Tooltip("Use \"Progressive\" if you want auto exposure to be animated. Use \"Fixed\" otherwise.")]
        public EyeAdaptationParameter eyeAdaptation = new EyeAdaptationParameter { value = EyeAdaptation.Progressive };

        [Min(0f), Tooltip("Adaptation speed from a dark to a light environment.")]
        public FloatParameter speedUp = new FloatParameter { value = 2f };

        [Min(0f), Tooltip("Adaptation speed from a light to a dark environment.")]
        public FloatParameter speedDown = new FloatParameter { value = 1f };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value
                && SystemInfo.supportsComputeShaders
                && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat);
        }
    }

    public sealed class AutoExposureRenderer : PostProcessEffectRenderer<AutoExposure>
    {
        const int k_NumEyes = 2;
        const int k_NumAutoExposureTextures = 2;

        readonly RenderTexture[][] m_AutoExposurePool = new RenderTexture[k_NumEyes][];
        int[] m_AutoExposurePingPong = new int[k_NumEyes];
        RenderTexture m_CurrentAutoExposure;

        public AutoExposureRenderer()
        {
            for (int eye = 0; eye < k_NumEyes; eye++)
            {
                m_AutoExposurePool[eye] = new RenderTexture[k_NumAutoExposureTextures];
                m_AutoExposurePingPong[eye] = 0;
            }
        }

        void CheckTexture(int eye, int id)
        {
            if (m_AutoExposurePool[eye][id] == null || !m_AutoExposurePool[eye][id].IsCreated())
            {
                m_AutoExposurePool[eye][id] = new RenderTexture(1, 1, 0, RenderTextureFormat.RFloat);
                m_AutoExposurePool[eye][id].Create();
            }
        }

        public override void Render(PostProcessRenderContext context)
        {
            var cmd = context.command;
            cmd.BeginSample("AutoExposureLookup");

            var sheet = context.propertySheets.Get(context.resources.shaders.autoExposure);
            sheet.ClearKeywords();

            // Prepare autoExpo texture pool
            CheckTexture(context.xrActiveEye, 0);
            CheckTexture(context.xrActiveEye, 1);

            // Make sure filtering values are correct to avoid apocalyptic consequences
            float lowPercent = settings.filtering.value.x;
            float highPercent = settings.filtering.value.y;
            const float kMinDelta = 1e-2f;
            highPercent = Mathf.Clamp(highPercent, 1f + kMinDelta, 99f);
            lowPercent = Mathf.Clamp(lowPercent, 1f, highPercent - kMinDelta);

            // Clamp min/max adaptation values as well
            float minLum = settings.minLuminance.value;
            float maxLum = settings.maxLuminance.value;
            settings.minLuminance.value = Mathf.Min(minLum, maxLum);
            settings.maxLuminance.value = Mathf.Max(minLum, maxLum);

            // Compute auto exposure
            sheet.properties.SetBuffer(ShaderIDs.HistogramBuffer, context.logHistogram.data);
            sheet.properties.SetVector(ShaderIDs.Params, new Vector4(lowPercent * 0.01f, highPercent * 0.01f, RuntimeUtilities.Exp2(settings.minLuminance.value), RuntimeUtilities.Exp2(settings.maxLuminance.value)));
            sheet.properties.SetVector(ShaderIDs.Speed, new Vector2(settings.speedDown.value, settings.speedUp.value));
            sheet.properties.SetVector(ShaderIDs.ScaleOffsetRes, context.logHistogram.GetHistogramScaleOffsetRes(context));
            sheet.properties.SetFloat(ShaderIDs.ExposureCompensation, settings.keyValue.value);

            if (m_ResetHistory || !Application.isPlaying)
            {
                // We don't want eye adaptation when not in play mode because the GameView isn't
                // animated, thus making it harder to tweak. Just use the final audo exposure value.
                m_CurrentAutoExposure = m_AutoExposurePool[context.xrActiveEye][0];
                cmd.BlitFullscreenTriangle(BuiltinRenderTextureType.None, m_CurrentAutoExposure, sheet, (int)EyeAdaptation.Fixed);

                // Copy current exposure to the other pingpong target to avoid adapting from black
                RuntimeUtilities.CopyTexture(cmd, m_AutoExposurePool[context.xrActiveEye][0], m_AutoExposurePool[context.xrActiveEye][1]);

                m_ResetHistory = false;
            }
            else
            {
                int pp = m_AutoExposurePingPong[context.xrActiveEye];
                var src = m_AutoExposurePool[context.xrActiveEye][++pp % 2];
                var dst = m_AutoExposurePool[context.xrActiveEye][++pp % 2];
                cmd.BlitFullscreenTriangle(src, dst, sheet, (int)settings.eyeAdaptation.value);
                m_AutoExposurePingPong[context.xrActiveEye] = ++pp % 2;
                m_CurrentAutoExposure = dst;
            }
            
            cmd.EndSample("AutoExposureLookup");

            context.autoExposureTexture = m_CurrentAutoExposure;
            context.autoExposure = settings;
        }

        public override void Release()
        {
            foreach (var rtEyeSet in m_AutoExposurePool)
            {
                foreach (var rt in rtEyeSet)
                    RuntimeUtilities.Destroy(rt);
            }
        }
    }
}
