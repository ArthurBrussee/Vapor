using System;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    public sealed class HistogramMonitor : Monitor
    {
        public enum Channel
        {
            Red,
            Green,
            Blue,
            Master
        }

        public int width = 512;
        public int height = 256;
        public Channel channel = Channel.Master;

        ComputeBuffer m_Data;
        int m_NumBins;
        int m_ThreadGroupSizeX;
        int m_ThreadGroupSizeY;

        internal override void OnEnable()
        {
            m_ThreadGroupSizeX = 16;

            if (RuntimeUtilities.isAndroidOpenGL)
            {
                m_NumBins = 128;
                m_ThreadGroupSizeY = 8;
            }
            else
            {
                m_NumBins = 256;
                m_ThreadGroupSizeY = 16;
            }
        }

        internal override void OnDisable()
        {
            base.OnDisable();

            if (m_Data != null)
                m_Data.Release();

            m_Data = null;
        }

        internal override bool NeedsHalfRes()
        {
            return true;
        }

        internal override void Render(PostProcessRenderContext context)
        {
            CheckOutput(width, height);

            if (m_Data == null)
                m_Data = new ComputeBuffer(m_NumBins, sizeof(uint));

            var compute = context.resources.computeShaders.gammaHistogram;
            var cmd = context.command;
            cmd.BeginSample("GammaHistogram");

            // Clear the buffer on every frame as we use it to accumulate values on every frame
            int kernel = compute.FindKernel("KHistogramClear");
            cmd.SetComputeBufferParam(compute, kernel, "_HistogramBuffer", m_Data);
            cmd.DispatchCompute(compute, kernel, Mathf.CeilToInt(m_NumBins / (float)m_ThreadGroupSizeX), 1, 1);

            // Gather all pixels and fill in our histogram
            kernel = compute.FindKernel("KHistogramGather");
            var parameters = new Vector4(
                context.width / 2,
                context.height / 2,
                RuntimeUtilities.isLinearColorSpace ? 1 : 0,
                (int)channel
            );

            cmd.SetComputeVectorParam(compute, "_Params", parameters);
            cmd.SetComputeTextureParam(compute, kernel, "_Source", ShaderIDs.HalfResFinalCopy);
            cmd.SetComputeBufferParam(compute, kernel, "_HistogramBuffer", m_Data);
            cmd.DispatchCompute(compute, kernel, 
                Mathf.CeilToInt(parameters.x / m_ThreadGroupSizeX),
                Mathf.CeilToInt(parameters.y / m_ThreadGroupSizeY),
                1
            );

            // Generate the histogram texture
            var sheet = context.propertySheets.Get(context.resources.shaders.gammaHistogram);
            sheet.properties.SetVector(ShaderIDs.Params, new Vector4(width, height, 0f, 0f));
            sheet.properties.SetBuffer(ShaderIDs.HistogramBuffer, m_Data);
            cmd.BlitFullscreenTriangle(BuiltinRenderTextureType.None, output, sheet, 0);

            cmd.EndSample("GammaHistogram");
        }
    }
}
