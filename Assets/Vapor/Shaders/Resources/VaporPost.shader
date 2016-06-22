// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "Hidden/VaporPost" {
	Properties{
		_MainTex("Main tex", 2D) = "white" {}


		_ShadowBlurSize("Shadow blur", Vector) = (0, 0, 0, 0)
			/*
			_Bloom0 ("Bloom0 (RGB)", 2D) = "black" {}
			_Bloom1 ("Bloom1 (RGB)", 2D) = "black" {}
			_Bloom2 ("Bloom2 (RGB)", 2D) = "black" {}
			_Bloom3 ("Bloom3 (RGB)", 2D) = "black" {}
			_Bloom4 ("Bloom4 (RGB)", 2D) = "black" {}
			_Bloom5 ("Bloom5 (RGB)", 2D) = "black" {}
			_MaskStrength("Mask strength", Float) = 0
			_MaskTex("Mask Tex", 2D) = "white" {}

			*/
	}
		SubShader{

		CGINCLUDE
			#include "UnityCG.cginc"
			#include "VaporFramework.cginc"
			half4 _MainTex_TexelSize;
			sampler2D _MainTex;

#define DEBUG 0

		ENDCG

			Pass{
				CGPROGRAM

				#pragma vertex vert
				#pragma fragment frag
				#pragma target 5.0


				sampler2D_float _CameraDepthTexture;
				sampler3D _IntegratedTexture;

				struct v2fFog {
					float4 pos : SV_POSITION;
					float4 texcoord : TEXCOORD0;
					float3 worldPos : TEXCOORD1;
				};

				v2fFog vert(appdata_full v) {
					v2fFog o;

					float4 pos = mul(UNITY_MATRIX_MVP, v.vertex);
					o.pos = pos;
					o.texcoord = float4(v.texcoord.xy, 0, 0);
					o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

					return o;
				}

				float4 frag(v2fFog i) : COLOR0 {
					// read low res depth and reconstruct world position
					float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.texcoord);
					float3 uv = DeviceToUv(float3(i.texcoord.x * 2.0f - 1.0f, i.texcoord.y * 2.0f - 1.0f, depth));

#if DEBUG
					float d = pow(saturate(1.0f - frac(uv.z * Z_RESOLUTION)), 12.0f);
					d += pow(saturate(1.0f - frac(i.texcoord.x * X_RESOLUTION)), 12.0f) * 0.25f;
					d += pow(saturate(1.0f - frac(i.texcoord.y * Y_RESOLUTION)), 12.0f) * 0.25f;

					if (uv.z < 0.33f) {
						return float4(d, 0, 0, 1);

					}
					else if (uv.z < 0.66f) {
						return float4(0, d, 0, 1);

					}
					else {
						return float4(0, 0, d, 1);
					}
#endif

					float4 fog = tex3Dlod(_IntegratedTexture, float4(uv, 0.0f));
					return fog;
				}

				ENDCG
			}


			Pass {
				CGPROGRAM


				#pragma vertex vert
				#pragma fragment frag

				sampler2D _FogTex;

				struct v2fFog {
					float4 pos : SV_POSITION;
					float4 texcoord : TEXCOORD0;
					float3 worldPos : TEXCOORD1;
				};



				v2fFog vert(appdata_full v) {
					v2fFog o;

					float4 pos = mul(UNITY_MATRIX_MVP, v.vertex);
					o.pos = pos;
					o.texcoord = float4(v.texcoord.xy, 0, 0);
					o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

					return o;
				}

				float4 frag(v2fFog i) : COLOR0{
					float4 fog = tex2D(_FogTex, i.texcoord);

#if DEBUG
					return fog;
#endif

					float4 color = tex2D(_MainTex, i.texcoord);

					return float4(color.rgb * fog.aaa + fog.rgb, color.a);
				}
				ENDCG
			}


			Pass{
				ZTest Always Cull Off ZWrite Off Blend Off
				Fog{ Mode off }


				CGPROGRAM

				//TODO: Unroll loop - make function


				static const float4 curve4_0 = float4(0.0205f, 0.0205f, 0.0205f, 0.0f);
				static const float4 curve4_1 = float4(0.0855f, 0.0855f, 0.0855f, 0.0f);
				static const float4 curve4_2 = float4(0.232f, 0.232f, 0.232f, 0.0f);
				static const float4 curve4_3 = float4(0.324f, 0.324f, 0.324f, 1.0f);

				#pragma vertex vertBlur
				#pragma fragment fragBlur8

				float4 _BlurSize;

				struct v2f_withBlurCoords8 {
					float4 pos : SV_POSITION;
					float4 uv : TEXCOORD0;
					float4 offs : TEXCOORD1;
				};

				v2f_withBlurCoords8 vertBlur(appdata_img v)
				{
					v2f_withBlurCoords8 o;
					o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
					o.uv = float4(v.texcoord.xy, 1, 1);
					o.offs = float4(_MainTex_TexelSize.x * _BlurSize.x, _MainTex_TexelSize.y * _BlurSize.y, 1, 1);

					return o;
				}

				float4 GaussianDirection(float2 uv, float2 filterWidth) {
					float4 color = tex2D(_MainTex, uv - filterWidth * 3) * curve4_0;
					color += tex2D(_MainTex, uv - filterWidth * 2) * curve4_1;
					color += tex2D(_MainTex, uv - filterWidth) * curve4_2;
					color += tex2D(_MainTex, uv) * curve4_3;
					color += tex2D(_MainTex, uv + filterWidth) * curve4_2;
					color += tex2D(_MainTex, uv + filterWidth * 2) * curve4_1;
					color += tex2D(_MainTex, uv + filterWidth * 3) * curve4_0;
					return color;
				}

				float4 fragBlur8(v2f_withBlurCoords8 i) : COLOR{
					return GaussianDirection(i.uv.xy, i.offs.xy);
				}
				ENDCG
			}
		}
}