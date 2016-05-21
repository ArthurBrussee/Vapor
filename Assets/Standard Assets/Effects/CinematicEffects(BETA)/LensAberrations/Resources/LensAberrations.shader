Shader "Hidden/LensAberrations"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Fog { Mode off }

        CGINCLUDE

            #pragma fragmentoption ARB_precision_hint_fastest
            #include "UnityCG.cginc"
            #pragma target 3.0

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            half4 _ChromaticAberration;

            half4 chromaticAberration(half2 uv)
            {
                half2 coords = 2.0 * uv - 1.0;
                half2 cd = coords * dot(coords, coords);
                half4 color = tex2D(_MainTex, uv);
                half3 fringe = tex2D(_MainTex, uv - cd * _ChromaticAberration.a).rgb;
                color.rgb = lerp(color.rgb, fringe, _ChromaticAberration.rgb);
                return color;
            }

            half4 _DistCenterScale;
            half3 _DistAmount;

            half2 distort(half2 uv)
            {
                uv = (uv - 0.5) * _DistAmount.z + 0.5;
                half2 ruv = _DistCenterScale.zw * (uv - 0.5 - _DistCenterScale.xy);
                half ru = length(ruv);

                #if DISTORT

                half wu = ru * _DistAmount.x;
                ru = tan(wu) * (1.0 / (ru * _DistAmount.y));
                uv = uv + ruv * (ru - 1.0);

                #elif UNDISTORT

                ru = (1.0 / ru) * _DistAmount.x * atan(ru * _DistAmount.y);
                uv = uv + ruv * (ru - 1.0);

                #endif

                return uv;
            }

            half3 _VignetteColor;
            half3 _VignetteSettings;
            half2 _VignetteCenter;
            half _VignetteBlur;
            half _VignetteDesat;
            sampler2D _BlurTex;

            half4 vignette(half4 color, half2 uv)
            {
                #define _Intensity      _VignetteSettings.x
                #define _Smoothness     _VignetteSettings.y
                #define _Roundness      _VignetteSettings.z

                half vfactor = 1.0;

                #if VIGNETTE_CLASSIC

                    half2 d = (uv - _VignetteCenter) * _Intensity;
                    vfactor = pow(saturate(1.0 - dot(d, d)), _Smoothness);

                #else

                    half2 d = abs(uv - _VignetteCenter) * _Intensity;
                    d = pow(d, _Roundness);

                #endif

                vfactor = pow(saturate(1.0 - dot(d, d)), _Smoothness);

                #if VIGNETTE_BLUR

                    half2 coords = 2.0 * uv - 1.0;
                    half3 blur = tex2D(_BlurTex, uv).rgb;
                    color.rgb = lerp(color.rgb, blur, saturate(_VignetteBlur * dot(coords, coords)));

                #endif

                #if VIGNETTE_DESAT

                    half lum = Luminance(color);
                    color.rgb = lerp(lerp(lum.xxx, color.rgb, _VignetteDesat), color.rgb, vfactor);

                #endif

                color.rgb *= lerp(_VignetteColor, (1.0).xxx, vfactor);

                return color;
            }

        ENDCG

        // (0) Blur pre-pass
        Pass
        {
            CGPROGRAM
                #pragma vertex vert_blur_prepass
                #pragma fragment frag_blur_prepass
                #pragma multi_compile __ CHROMATIC_ABERRATION
                #pragma multi_compile __ DISTORT UNDISTORT
                #pragma

                half2 _BlurPass;

                struct v2f
                {
                    half4 pos : SV_POSITION;
                    half2 uv : TEXCOORD0;
                    half4 uv1 : TEXCOORD1;
                    half4 uv2 : TEXCOORD2;
                };

                v2f vert_blur_prepass(appdata_img v)
                {
                    v2f o;
                    o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
                    o.uv = v.texcoord.xy;

                    #if UNITY_UV_STARTS_AT_TOP
                    if (_MainTex_TexelSize.y < 0)
                        o.uv.y = 1.0 - o.uv.y;
                    #endif

                    half2 d1 = 1.3846153846 * _BlurPass;
                    half2 d2 = 3.2307692308 * _BlurPass;
                    o.uv1 = half4(o.uv + d1, o.uv - d1);
                    o.uv2 = half4(o.uv + d2, o.uv - d2);
                    return o;
                }

                half4 fetch(half2 uv)
                {
                    #if (DISTORT || UNDISTORT)
                    uv = distort(uv);
                    #endif

                    #if CHROMATIC_ABERRATION
                    return chromaticAberration(uv);
                    #else
                    return tex2D(_MainTex, uv);
                    #endif
                }

                half4 frag_blur_prepass(v2f i) : SV_Target
                {
                    half4 c = fetch(i.uv) * 0.2270270270;
                    c += fetch(i.uv1.xy) * 0.3162162162;
                    c += fetch(i.uv1.zw) * 0.3162162162;
                    c += fetch(i.uv2.xy) * 0.0702702703;
                    c += fetch(i.uv2.zw) * 0.0702702703;
                    return c;
                }
            ENDCG
        }

        // (1) Chroma
        Pass
        {
            CGPROGRAM
                #pragma vertex vert_img
                #pragma fragment frag

                half4 frag(v2f_img i) : SV_Target
                {
                    return chromaticAberration(i.uv);
                }
            ENDCG
        }

        // (2) Distort
        Pass
        {
            CGPROGRAM
                #pragma vertex vert_img
                #pragma fragment frag
                #pragma multi_compile DISTORT UNDISTORT

                half4 frag(v2f_img i) : SV_Target
                {
                    half2 uv = distort(i.uv);
                    return tex2D(_MainTex, uv);
                }
            ENDCG
        }

        // (3) Vignette
        Pass
        {
            CGPROGRAM
                #pragma vertex vert_img
                #pragma fragment frag
                #pragma multi_compile VIGNETTE_CLASSIC VIGNETTE_FILMIC
                #pragma multi_compile __ VIGNETTE_BLUR
                #pragma multi_compile __ VIGNETTE_DESAT

                half4 frag(v2f_img i) : SV_Target
                {
                    half4 color = tex2D(_MainTex, i.uv);
                    return vignette(color, i.uv);
                }
            ENDCG
        }

        // (4) Chroma / Distort
        Pass
        {
            CGPROGRAM
                #pragma vertex vert_img
                #pragma fragment frag
                #pragma multi_compile DISTORT UNDISTORT

                half4 frag(v2f_img i) : SV_Target
                {
                    half2 uv = distort(i.uv);
                    return chromaticAberration(uv);
                }
            ENDCG
        }

        // (5) Chroma / Vignette
        Pass
        {
            CGPROGRAM
                #pragma vertex vert_img
                #pragma fragment frag
                #pragma multi_compile VIGNETTE_CLASSIC VIGNETTE_FILMIC
                #pragma multi_compile __ VIGNETTE_BLUR
                #pragma multi_compile __ VIGNETTE_DESAT

                half4 frag(v2f_img i) : SV_Target
                {
                    return vignette(chromaticAberration(i.uv), i.uv);
                }
            ENDCG
        }

        // (6) Distort / Vignette
        Pass
        {
            CGPROGRAM
                #pragma vertex vert_img
                #pragma fragment frag
                #pragma multi_compile DISTORT UNDISTORT
                #pragma multi_compile VIGNETTE_CLASSIC VIGNETTE_FILMIC
                #pragma multi_compile __ VIGNETTE_BLUR
                #pragma multi_compile __ VIGNETTE_DESAT

                half4 frag(v2f_img i) : SV_Target
                {
                    half2 uv = distort(i.uv);
                    return vignette(tex2D(_MainTex, uv), i.uv);
                }
            ENDCG
        }

        // (6) Chroma / Distort / Vignette
        Pass
        {
            CGPROGRAM
                #pragma vertex vert_img
                #pragma fragment frag
                #pragma multi_compile DISTORT UNDISTORT
                #pragma multi_compile VIGNETTE_CLASSIC VIGNETTE_FILMIC
                #pragma multi_compile __ VIGNETTE_BLUR
                #pragma multi_compile __ VIGNETTE_DESAT

                half4 frag(v2f_img i) : SV_Target
                {
                    half2 uv = distort(i.uv);
                    half4 chroma = chromaticAberration(uv);
                    return vignette(chroma, i.uv);
                }
            ENDCG
        }
    }
    FallBack off
}
