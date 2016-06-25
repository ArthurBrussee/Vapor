// Upgrade NOTE: commented out 'float4x4 _WorldToCamera', a built-in variable
// Upgrade NOTE: replaced '_WorldToCamera' with 'unity_WorldToCamera'

Shader "Hidden/Image Effects/Cinematic/AmbientOcclusion"
{
    Properties
    {
        _MainTex("", 2D) = ""{}
        _OcclusionTexture("", 2D) = ""{}
    }
    CGINCLUDE

    // --------
    // Additional options for further customization
    // --------

    // By default, a fixed sampling pattern is used in the AO estimator.
    // Although this gives preferable results in most cases, a completely
    // random sampling pattern could give aesthetically good results in some
    // cases. Comment out the line below to use the random pattern instead of
    // the fixed one.
    #define FIXED_SAMPLING_PATTERN 1

    // The constant below determines the contrast of occlusion. Altough this
    // allows intentional over/under occlusion, currently is not exposed to the
    // editor, because it’s thought to be rarely useful.
    static const float kContrast = 0.6;

    // The constant below controls the geometry-awareness of the blur filter.
    // The higher value, the more sensitive it is.
    static const float kGeom = 50;

    // The constants below are used in the AO estimator. Beta is mainly used
    // for suppressing self-shadowing noise, and Epsilon is used to prevent
    // calculation underflow. See the paper (Morgan 2011 http://goo.gl/2iz3P)
    // for further details of these constants.
    static const float kBeta = 0.002;
    static const float kEpsilon = 1e-4;

    // --------

    #include "UnityCG.cginc"

    #if _SAMPLECOUNT_LOWEST
    static const int _SampleCount = 3;
    #else
    int _SampleCount;
    #endif

    // Global shader properties
    #if _SOURCE_GBUFFER
    sampler2D _CameraGBufferTexture2;
    sampler2D_float _CameraDepthTexture;
    // float4x4 _WorldToCamera;
    #else
    #if _SOURCE_DEPTH
    sampler2D_float _CameraDepthTexture;
    #endif
    sampler2D_float _CameraDepthNormalsTexture;
    #endif

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;

    sampler2D _OcclusionTexture;

    // Material shader properties
    half _Intensity;
    float _Radius;
    float _TargetScale;
    float2 _BlurVector;

    // Utility for sin/cos
    float2 CosSin(float theta)
    {
        float sn, cs;
        sincos(theta, sn, cs);
        return float2(cs, sn);
    }

    // Gamma encoding function for AO value
    // (do nothing if in the linear mode)
    half EncodeAO(half x)
    {
        // Gamma encoding
        half x_g = 1 - pow(1 - x, 1 / 2.2);
        // ColorSpaceLuminance.w is 0 (gamma) or 1 (linear).
        return lerp(x_g, x, unity_ColorSpaceLuminance.w);
    }

    // Pseudo random number generator with 2D argument
    float UVRandom(float u, float v)
    {
        float f = dot(float2(12.9898, 78.233), float2(u, v));
        return frac(43758.5453 * sin(f));
    }

    // Interleaved gradient function from Jimenez 2014 http://goo.gl/eomGso
    float GradientNoise(float2 uv)
    {
        uv = floor(uv * _ScreenParams.xy);
        float f = dot(float2(0.06711056f, 0.00583715f), uv);
        return frac(52.9829189f * frac(f));
    }

    // Boundary check for depth sampler
    // (returns a very large value if it lies out of bounds)
    float CheckBounds(float2 uv, float d)
    {
        float ob = any(uv < 0) + any(uv > 1);
    #if defined(UNITY_REVERSED_Z)
        ob += (d <= 0.00001);
    #else
        ob += (d >= 0.99999);
    #endif
        return ob * 1e8;
    }

    // Depth/normal sampling functions
    float SampleDepth(float2 uv)
    {
    #if _SOURCE_GBUFFER || _SOURCE_DEPTH
        float d = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
        return LinearEyeDepth(d) + CheckBounds(uv, d);
    #else
        float4 cdn = tex2D(_CameraDepthNormalsTexture, uv);
        float d = DecodeFloatRG(cdn.zw);
        return d * _ProjectionParams.z + CheckBounds(uv, d);
    #endif
    }

    float3 SampleNormal(float2 uv)
    {
    #if _SOURCE_GBUFFER
        float3 norm = tex2D(_CameraGBufferTexture2, uv).xyz * 2 - 1;
        return mul((float3x3)unity_WorldToCamera, norm);
    #else
        float4 cdn = tex2D(_CameraDepthNormalsTexture, uv);
        return DecodeViewNormalStereo(cdn) * float3(1, 1, -1);
    #endif
    }

    float SampleDepthNormal(float2 uv, out float3 normal)
    {
    #if _SOURCE_GBUFFER || _SOURCE_DEPTH
        normal = SampleNormal(uv);
        return SampleDepth(uv);
    #else
        float4 cdn = tex2D(_CameraDepthNormalsTexture, uv);
        normal = DecodeViewNormalStereo(cdn) * float3(1, 1, -1);
        float d = DecodeFloatRG(cdn.zw);
        return d * _ProjectionParams.z + CheckBounds(uv, d);
    #endif
    }

    // Reconstruct view-space position from UV and depth.
    // p11_22 = (unity_CameraProjection._11, unity_CameraProjection._22)
    // p13_31 = (unity_CameraProjection._13, unity_CameraProjection._23)
    float3 ReconstructViewPos(float2 uv, float depth, float2 p11_22, float2 p13_31)
    {
        return float3((uv * 2 - 1 - p13_31) / p11_22, 1) * depth;
    }

    // Normal vector comparer (for geometry-aware weighting)
    half CompareNormal(half3 d1, half3 d2)
    {
        return pow((dot(d1, d2) + 1) * 0.5, kGeom);
    }

    // Final combiner function
    half3 CombineOcclusion(half3 src, half3 ao)
    {
        return lerp(src, 0, EncodeAO(ao));
    }

    // Sample point picker
    float3 PickSamplePoint(float2 uv, float index)
    {
        // Uniformaly distributed points on a unit sphere http://goo.gl/X2F1Ho
    #if FIXED_SAMPLING_PATTERN
        float gn = GradientNoise(uv * _TargetScale);
        float u = frac(UVRandom(0, index) + gn) * 2 - 1;
        float theta = (UVRandom(1, index) + gn) * UNITY_PI * 2;
    #else
        float u = UVRandom(uv.x + _Time.x, uv.y + index) * 2 - 1;
        float theta = UVRandom(-uv.x - _Time.x, uv.y + index) * UNITY_PI * 2;
    #endif
        float3 v = float3(CosSin(theta) * sqrt(1 - u * u), u);
        // Make them distributed between [0, _Radius]
        float l = sqrt((index + 1) / _SampleCount) * _Radius;
        return v * l;
    }

    // Occlusion estimator function
    float EstimateOcclusion(float2 uv)
    {
        // Parameters used in coordinate conversion
        float3x3 proj = (float3x3)unity_CameraProjection;
        float2 p11_22 = float2(unity_CameraProjection._11, unity_CameraProjection._22);
        float2 p13_31 = float2(unity_CameraProjection._13, unity_CameraProjection._23);

        // View space normal and depth
        float3 norm_o;
        float depth_o = SampleDepthNormal(uv, norm_o);

    #if _SOURCE_DEPTHNORMALS
        // Offset the depth value to avoid precision error.
        // (depth in the DepthNormals mode has only 16-bit precision)
        depth_o -= _ProjectionParams.z / 65536;
    #endif

        // Reconstruct the view-space position.
        float3 vpos_o = ReconstructViewPos(uv, depth_o, p11_22, p13_31);

        // Distance-based AO estimator based on Morgan 2011 http://goo.gl/2iz3P
        float ao = 0.0;

        for (int s = 0; s < _SampleCount; s++)
        {
            // Sample point
    #if SHADER_API_D3D11
            float3 v_s1 = PickSamplePoint(uv, floor(1.0001 * s)); // Nvidia fix
    #else
            float3 v_s1 = PickSamplePoint(uv, s);
    #endif
            v_s1 = faceforward(v_s1, -norm_o, v_s1);
            float3 vpos_s1 = vpos_o + v_s1;

            // Reproject the sample point
            float3 spos_s1 = mul(proj, vpos_s1);
            float2 uv_s1 = (spos_s1.xy / vpos_s1.z + 1) * 0.5;

            // Depth at the sample point
            float depth_s1 = SampleDepth(uv_s1);

            // Relative position of the sample point
            float3 vpos_s2 = ReconstructViewPos(uv_s1, depth_s1, p11_22, p13_31);
            float3 v_s2 = vpos_s2 - vpos_o;

            // Estimate the obscurance value
            float a1 = max(dot(v_s2, norm_o) - kBeta * depth_o, 0);
            float a2 = dot(v_s2, v_s2) + kEpsilon;
            ao += a1 / a2;
        }

        ao *= _Radius; // intensity normalization

        // Apply other parameters.
        return pow(ao * _Intensity / _SampleCount, kContrast);
    }

    // Geometry-aware separable blur filter (large kernel)
    half SeparableBlurLarge(sampler2D tex, float2 uv, float2 delta)
    {
    #if !SHADER_API_MOBILE
        // 9-tap Gaussian blur with adaptive sampling
        float2 uv1a = uv - delta;
        float2 uv1b = uv + delta;
        float2 uv2a = uv - delta * 2;
        float2 uv2b = uv + delta * 2;
        float2 uv3a = uv - delta * 3.2307692308;
        float2 uv3b = uv + delta * 3.2307692308;

        half3 n0 = SampleNormal(uv);

        half w0 = 0.37004405286;
        half w1a = CompareNormal(n0, SampleNormal(uv1a)) * 0.31718061674;
        half w1b = CompareNormal(n0, SampleNormal(uv1b)) * 0.31718061674;
        half w2a = CompareNormal(n0, SampleNormal(uv2a)) * 0.19823788546;
        half w2b = CompareNormal(n0, SampleNormal(uv2b)) * 0.19823788546;
        half w3a = CompareNormal(n0, SampleNormal(uv3a)) * 0.11453744493;
        half w3b = CompareNormal(n0, SampleNormal(uv3b)) * 0.11453744493;

        half s = tex2D(_MainTex, uv).r * w0;
        s += tex2D(_MainTex, uv1a).r * w1a;
        s += tex2D(_MainTex, uv1b).r * w1b;
        s += tex2D(_MainTex, uv2a).r * w2a;
        s += tex2D(_MainTex, uv2b).r * w2b;
        s += tex2D(_MainTex, uv3a).r * w3a;
        s += tex2D(_MainTex, uv3b).r * w3b;

        return s / (w0 + w1a + w1b + w2a + w2b + w3a + w3b);
    #else
        // 9-tap Gaussian blur with linear sampling
        // (less quality but slightly fast)
        float2 uv1a = uv - delta * 1.3846153846;
        float2 uv1b = uv + delta * 1.3846153846;
        float2 uv2a = uv - delta * 3.2307692308;
        float2 uv2b = uv + delta * 3.2307692308;

        half3 n0 = SampleNormal(uv);

        half w0 = 0.2270270270;
        half w1a = CompareNormal(n0, SampleNormal(uv1a)) * 0.3162162162;
        half w1b = CompareNormal(n0, SampleNormal(uv1b)) * 0.3162162162;
        half w2a = CompareNormal(n0, SampleNormal(uv2a)) * 0.0702702703;
        half w2b = CompareNormal(n0, SampleNormal(uv2b)) * 0.0702702703;

        half s = tex2D(_MainTex, uv).r * w0;
        s += tex2D(_MainTex, uv1a).r * w1a;
        s += tex2D(_MainTex, uv1b).r * w1b;
        s += tex2D(_MainTex, uv2a).r * w2a;
        s += tex2D(_MainTex, uv2b).r * w2b;

        return s / (w0 + w1a + w1b + w2a + w2b);
    #endif
    }

    // Geometry-aware separable blur filter (small kernel)
    half SeparableBlurSmall(sampler2D tex, float2 uv, float2 delta)
    {
        float2 uv1 = uv - delta;
        float2 uv2 = uv + delta;

        half3 n0 = SampleNormal(uv);

        half w0 = 2;
        half w1 = CompareNormal(n0, SampleNormal(uv1));
        half w2 = CompareNormal(n0, SampleNormal(uv2));

        half s = tex2D(_MainTex, uv).r * w0;
        s += tex2D(_MainTex, uv1).r * w1;
        s += tex2D(_MainTex, uv2).r * w2;

        return s / (w0 + w1 + w2);
    }

    // Pass 0: Occlusion estimation
    half4 frag_ao(v2f_img i) : SV_Target
    {
        return EstimateOcclusion(i.uv);
    }

    // Pass 1: Primary blur filter
    half4 frag_blur1(v2f_img i) : SV_Target
    {
        float2 delta = _MainTex_TexelSize.xy * _BlurVector;
        return SeparableBlurLarge(_MainTex, i.uv, delta);
    }

    // Pass 2: Secondary blur filter
    half4 frag_blur2(v2f_img i) : SV_Target
    {
        float2 delta = _MainTex_TexelSize.xy * _BlurVector;
        return SeparableBlurSmall(_MainTex, i.uv, delta);
    }

    // Pass 3: Combiner for the forward mode
    struct v2f_multitex
    {
        float4 pos : SV_POSITION;
        float2 uv0 : TEXCOORD0;
        float2 uv1 : TEXCOORD1;
    };

    v2f_multitex vert_multitex(appdata_img v)
    {
        // Handles vertically-flipped case.
        float vflip = sign(_MainTex_TexelSize.y);

        v2f_multitex o;
        o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
        o.uv0 = v.texcoord.xy;
        o.uv1 = (v.texcoord.xy - 0.5) * float2(1, vflip) + 0.5;
        return o;
    }

    half4 frag_combine(v2f_multitex i) : SV_Target
    {
        half4 src = tex2D(_MainTex, i.uv0);
        half ao = tex2D(_OcclusionTexture, i.uv1).r;
        return half4(CombineOcclusion(src.rgb, ao), src.a);
    }

    // Pass 4: Combiner for the ambient-only mode
    v2f_img vert_gbuffer(appdata_img v)
    {
        v2f_img o;
        o.pos = v.vertex * float4(2, 2, 0, 0) + float4(0, 0, 0, 1);
    #if UNITY_UV_STARTS_AT_TOP
        o.uv = v.texcoord * float2(1, -1) + float2(0, 1);
    #else
        o.uv = v.texcoord;
    #endif
        return o;
    }

#if !SHADER_API_GLES // excluding the MRT pass under GLES2

    struct CombinerOutput
    {
        half4 gbuffer0 : SV_Target0;
        half4 gbuffer3 : SV_Target1;
    };

    CombinerOutput frag_gbuffer_combine(v2f_img i)
    {
        half ao = tex2D(_OcclusionTexture, i.uv).r;
        CombinerOutput o;
        o.gbuffer0 = half4(0, 0, 0, ao);
        o.gbuffer3 = half4((half3)EncodeAO(ao), 0);
        return o;
    }

#else

    fixed4 frag_gbuffer_combine(v2f_img i) : SV_Target0
    {
        return 0;
    }

#endif

    // Pass 5: Debug blit
    half4 frag_blit_ao(v2f_multitex i) : SV_Target
    {
        half4 src = tex2D(_MainTex, i.uv0);
        half ao = tex2D(_OcclusionTexture, i.uv1).r;
        return half4(CombineOcclusion(1, ao), src.a);
    }

    ENDCG

    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma multi_compile _SOURCE_DEPTH _SOURCE_DEPTHNORMALS _SOURCE_GBUFFER
            #pragma multi_compile _ _SAMPLECOUNT_LOWEST
            #pragma vertex vert_img
            #pragma fragment frag_ao
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma multi_compile _ _SOURCE_GBUFFER
            #pragma vertex vert_img
            #pragma fragment frag_blur1
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma multi_compile _ _SOURCE_GBUFFER
            #pragma vertex vert_img
            #pragma fragment frag_blur2
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert_multitex
            #pragma fragment frag_combine
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            Blend Zero OneMinusSrcColor, Zero OneMinusSrcAlpha
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert_gbuffer
            #pragma fragment frag_gbuffer_combine
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert_multitex
            #pragma fragment frag_blit_ao
            #pragma target 3.0
            ENDCG
        }
    }
}
