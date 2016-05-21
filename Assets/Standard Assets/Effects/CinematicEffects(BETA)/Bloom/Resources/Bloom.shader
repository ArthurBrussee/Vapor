Shader "Hidden/Image Effects/Cinematic/Bloom"
{
    Properties
    {
        _MainTex("", 2D) = "" {}
        _BaseTex("", 2D) = "" {}
    }

    CGINCLUDE

    #include "UnityCG.cginc"

    // Mobile: use RGBM instead of float/half RGB
    #define USE_RGBM defined(SHADER_API_MOBILE)

    sampler2D _MainTex;
    sampler2D _BaseTex;
    float2 _MainTex_TexelSize;
    float2 _BaseTex_TexelSize;

    float _PrefilterOffs;
    half _Threshold;
    half3 _Curve;
    float _SampleScale;
    half _Intensity;

    // Brightness function
    half Brightness(half3 c)
    {
        return max(max(c.r, c.g), c.b);
    }

    // 3-tap median filter
    half3 Median(half3 a, half3 b, half3 c)
    {
        return a + b + c - min(min(a, b), c) - max(max(a, b), c);
    }

    // Clamp HDR value within a safe range
    half3 SafeHDR(half3 c) { return min(c, 65000); }
    half4 SafeHDR(half4 c) { return min(c, 65000); }

    // RGBM encoding/decoding
    half4 EncodeHDR(float3 rgb)
    {
    #if USE_RGBM
        rgb *= 1.0 / 8;
        float m = max(max(rgb.r, rgb.g), max(rgb.b, 1e-6));
        m = ceil(m * 255) / 255;
        return half4(rgb / m, m);
    #else
        return half4(rgb, 0);
    #endif
    }

    float3 DecodeHDR(half4 rgba)
    {
    #if USE_RGBM
        return rgba.rgb * rgba.a * 8;
    #else
        return rgba.rgb;
    #endif
    }

    // Downsample with a 4x4 box filter
    half3 DownsampleFilter(float2 uv)
    {
        float4 d = _MainTex_TexelSize.xyxy * float4(-1, -1, +1, +1);

        half3 s;
        s  = DecodeHDR(tex2D(_MainTex, uv + d.xy));
        s += DecodeHDR(tex2D(_MainTex, uv + d.zy));
        s += DecodeHDR(tex2D(_MainTex, uv + d.xw));
        s += DecodeHDR(tex2D(_MainTex, uv + d.zw));

        return s * (1.0 / 4);
    }

    // Downsample with a 4x4 box filter + anti-flicker filter
    half3 DownsampleAntiFlickerFilter(float2 uv)
    {
        float4 d = _MainTex_TexelSize.xyxy * float4(-1, -1, +1, +1);

        half3 s1 = DecodeHDR(tex2D(_MainTex, uv + d.xy));
        half3 s2 = DecodeHDR(tex2D(_MainTex, uv + d.zy));
        half3 s3 = DecodeHDR(tex2D(_MainTex, uv + d.xw));
        half3 s4 = DecodeHDR(tex2D(_MainTex, uv + d.zw));

        // Karis's luma weighted average
        half s1w = 1 / (Brightness(s1) + 1);
        half s2w = 1 / (Brightness(s2) + 1);
        half s3w = 1 / (Brightness(s3) + 1);
        half s4w = 1 / (Brightness(s4) + 1);
        half one_div_wsum = 1.0 / (s1w + s2w + s3w + s4w);

        return (s1 * s1w + s2 * s2w + s3 * s3w + s4 * s4w) * one_div_wsum;
    }

    half3 UpsampleFilter(float2 uv)
    {
    #if HIGH_QUALITY
        // 9-tap bilinear upsampler (tent filter)
        float4 d = _MainTex_TexelSize.xyxy * float4(1, 1, -1, 0) * _SampleScale;

        half3 s;
        s  = DecodeHDR(tex2D(_MainTex, uv - d.xy));
        s += DecodeHDR(tex2D(_MainTex, uv - d.wy)) * 2;
        s += DecodeHDR(tex2D(_MainTex, uv - d.zy));

        s += DecodeHDR(tex2D(_MainTex, uv + d.zw)) * 2;
        s += DecodeHDR(tex2D(_MainTex, uv       )) * 4;
        s += DecodeHDR(tex2D(_MainTex, uv + d.xw)) * 2;

        s += DecodeHDR(tex2D(_MainTex, uv + d.zy));
        s += DecodeHDR(tex2D(_MainTex, uv + d.wy)) * 2;
        s += DecodeHDR(tex2D(_MainTex, uv + d.xy));

        return s * (1.0 / 16);
    #else
        // 4-tap bilinear upsampler
        float4 d = _MainTex_TexelSize.xyxy * float4(-1, -1, +1, +1) * (_SampleScale * 0.5);

        half3 s;
        s  = DecodeHDR(tex2D(_MainTex, uv + d.xy));
        s += DecodeHDR(tex2D(_MainTex, uv + d.zy));
        s += DecodeHDR(tex2D(_MainTex, uv + d.xw));
        s += DecodeHDR(tex2D(_MainTex, uv + d.zw));

        return s * (1.0 / 4);
    #endif
    }

    //
    // Vertex shader
    //

    struct v2f_multitex
    {
        float4 pos : SV_POSITION;
        float2 uvMain : TEXCOORD0;
        float2 uvBase : TEXCOORD1;
    };

    v2f_multitex vert_multitex(appdata_full v)
    {
        v2f_multitex o;
        o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
        o.uvMain = v.texcoord.xy;
        o.uvBase = v.texcoord.xy;
    #if UNITY_UV_STARTS_AT_TOP
        if (_BaseTex_TexelSize.y < 0.0)
            o.uvBase.y = 1.0 - v.texcoord.y;
    #endif
        return o;
    }

    //
    // fragment shader
    //

    half4 frag_prefilter(v2f_img i) : SV_Target
    {
        float2 uv = i.uv + _MainTex_TexelSize.xy * _PrefilterOffs;

    #if ANTI_FLICKER
        float3 d = _MainTex_TexelSize.xyx * float3(1, 1, 0);
        half4 s0 = SafeHDR(tex2D(_MainTex, uv));
        half3 s1 = SafeHDR(tex2D(_MainTex, uv - d.xz).rgb);
        half3 s2 = SafeHDR(tex2D(_MainTex, uv + d.xz).rgb);
        half3 s3 = SafeHDR(tex2D(_MainTex, uv - d.zy).rgb);
        half3 s4 = SafeHDR(tex2D(_MainTex, uv + d.zy).rgb);
        half3 m = Median(Median(s0.rgb, s1, s2), s3, s4);
    #else
        half4 s0 = SafeHDR(tex2D(_MainTex, uv));
        half3 m = s0.rgb;
    #endif

    #if UNITY_COLORSPACE_GAMMA
        m = GammaToLinearSpace(m);
    #endif
        // Pixel brightness
        half br = Brightness(m);

        // Under-threshold part: quadratic curve
        half rq = clamp(br - _Curve.x, 0, _Curve.y);
        rq = _Curve.z * rq * rq;

        // Combine and apply the brightness response curve.
        m *= max(rq, br - _Threshold) / (br + 1e-5);

        return EncodeHDR(m);
    }

    half4 frag_downsample1(v2f_img i) : SV_Target
    {
    #if ANTI_FLICKER
        return EncodeHDR(DownsampleAntiFlickerFilter(i.uv));
    #else
        return EncodeHDR(DownsampleFilter(i.uv));
    #endif
    }

    half4 frag_downsample2(v2f_img i) : SV_Target
    {
        return EncodeHDR(DownsampleFilter(i.uv));
    }

    half4 frag_upsample(v2f_multitex i) : SV_Target
    {
        half3 base = DecodeHDR(tex2D(_BaseTex, i.uvBase));
        half3 blur = UpsampleFilter(i.uvMain);
        return EncodeHDR(base + blur);
    }

    half4 frag_upsample_final(v2f_multitex i) : SV_Target
    {
        half4 base = tex2D(_BaseTex, i.uvBase);
        half3 blur = UpsampleFilter(i.uvMain);
    #if UNITY_COLORSPACE_GAMMA
        base.rgb = GammaToLinearSpace(base.rgb);
    #endif
        half3 cout = base.rgb + blur * _Intensity;
    #if UNITY_COLORSPACE_GAMMA
        cout = LinearToGammaSpace(cout);
    #endif
        return half4(cout, base.a);
    }

    ENDCG
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma multi_compile _ ANTI_FLICKER
            #pragma multi_compile _ UNITY_COLORSPACE_GAMMA
            #pragma vertex vert_img
            #pragma fragment frag_prefilter
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma multi_compile _ ANTI_FLICKER
            #pragma vertex vert_img
            #pragma fragment frag_downsample1
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag_downsample2
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma multi_compile _ HIGH_QUALITY
            #pragma vertex vert_multitex
            #pragma fragment frag_upsample
            #pragma target 3.0
            ENDCG
        }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma multi_compile _ HIGH_QUALITY
            #pragma multi_compile _ UNITY_COLORSPACE_GAMMA
            #pragma vertex vert_multitex
            #pragma fragment frag_upsample_final
            #pragma target 3.0
            ENDCG
        }
    }
}
