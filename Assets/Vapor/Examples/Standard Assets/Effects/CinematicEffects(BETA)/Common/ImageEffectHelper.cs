using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityStandardAssets.CinematicEffects
{
    public static class ImageEffectHelper
    {
        public static bool IsSupported(Shader s, bool needDepth, bool needHdr, MonoBehaviour effect)
        {
#if UNITY_EDITOR
            // Don't check for shader compatibility while it's building as it would disable most effects
            // on build farms without good-enough gaming hardware.
            if (!BuildPipeline.isBuildingPlayer)
            {
#endif
                if (s == null || !s.isSupported)
                {
                    Debug.LogWarningFormat("Missing shader for image effect {0}", effect);
                    return false;
                }

                if (!SystemInfo.supportsImageEffects || !SystemInfo.supportsRenderTextures)
                {
                    Debug.LogWarningFormat("Image effects aren't supported on this device ({0})", effect);
                    return false;
                }

                if (needDepth && !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Depth))
                {
                    Debug.LogWarningFormat("Depth textures aren't supported on this device ({0})", effect);
                    return false;
                }

                if (needHdr && !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
                {
                    Debug.LogWarningFormat("Floating point textures aren't supported on this device ({0})", effect);
                    return false;
                }
#if UNITY_EDITOR
            }
#endif

            return true;
        }

        public static Material CheckShaderAndCreateMaterial(Shader s)
        {
            if (s == null || !s.isSupported)
                return null;

            var material = new Material(s);
            material.hideFlags = HideFlags.DontSave;
            return material;
        }

        public static bool supportsDX11
        {
            get { return SystemInfo.graphicsShaderLevel >= 50 && SystemInfo.supportsComputeShaders; }
        }
    }
}
