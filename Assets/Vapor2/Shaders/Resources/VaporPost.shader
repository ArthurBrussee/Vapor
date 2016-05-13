// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "Hidden/VaporPost" {
	Properties {
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
	SubShader {

	CGINCLUDE
		#include "UnityCG.cginc"
		half4 _MainTex_TexelSize;
		sampler2D _MainTex;

		#define DEPTH_POW 6


		#define UINT_MAX 4294967295.0f



	ENDCG

		Pass{		
			CGPROGRAM
					
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 5.0




			float _MaskStrength;
			
			sampler2D_float _CameraDepthTexture;

			float4x4 InverseProjectionMatrix;
			float4x4 InverseViewMatrix;


			sampler3D _ScatterTex;
			uint _Frame;


			struct v2fFog {
				float4 pos : SV_POSITION;
				float4 texcoord : TEXCOORD0;
				float3 worldPos : TEXCOORD1;
			};



			v2fFog vert(appdata_full v){
				v2fFog o;
				
				float4 pos = mul(UNITY_MATRIX_MVP, v.vertex);
				o.pos = pos;
				o.texcoord = float4(v.texcoord.xy, 0, 0);
				o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

				return o;
			}	


			/*
			sampler2D _MaskTex;
			sampler2D _Bloom0;
			sampler2D _Bloom1;
			sampler2D _Bloom2;
			sampler2D _Bloom3;
			sampler2D _Bloom4;
			sampler2D _Bloom5;
			
			float _FogAvgDensity;
			float4 _FogColor;
			float4 _ColSettings;
			sampler2D _ColGradient;
			*/
			


			float4 frag(v2fFog i) : COLOR0 {	
				// read low res depth and reconstruct world position
				float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.texcoord);
				float linearDepth = Linear01Depth(depth);
				linearDepth = pow(linearDepth, 1.0f / DEPTH_POW);

				float4 fog = tex3Dlod(_ScatterTex, float4(i.texcoord.xy, linearDepth, 0.0f));

				return fog;
			}
			
			ENDCG
		} 








		Pass 	//1 Downsample
		{
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
				float4 color = tex2D(_MainTex, i.texcoord);
		

				
				//TODO: Restore masking?
				//return fog.rgba;

				/*
				float3 b0 = tex2D(_Bloom0, screenUv).rgb;
				float3 b1 = tex2D(_Bloom1, screenUv).rgb;
				float3 b2 = tex2D(_Bloom2, screenUv).rgb;
				float3 b3 = tex2D(_Bloom3, screenUv).rgb;
				float3 b4 = tex2D(_Bloom4, screenUv).rgb;
				float3 b5 = tex2D(_Bloom5, screenUv).rgb;

				const float div = 2.2f;
				float3 bloom = b0 * 0.5f / div
				+ b1 * 0.8f * 0.75f / div
				+ b2 * 0.6f / div
				+ b3 * 0.45f / div
				+ b4 * 0.35f / div
				+ b5 * 0.35f / div;

				float worldFogDepth = max(0, worldDepth - _VaporMaxZ);
				float worldFogStr = worldFogDepth * _FogAvgDensity;

				fog.rgb = _FogColor.rgb;
				fog.a += worldFogStr;

				float4 gradientUv = float4((worldDepth - _ColSettings.x) * _ColSettings.y, (i.worldPos.y - _ColSettings.z) * _ColSettings.w, 0.0f, 0.0f);
				fog *= tex2Dlod(_ColGradient, gradientUv);
				*/


				return float4(color.rgb * fog.aaa + fog.rgb, color.a);
			}
			ENDCG		 
		}


			Pass{

				ZTest Always Cull Off ZWrite Off Blend Off
				Fog{ Mode off }


				CGPROGRAM


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
				//TODO: Unroll loop - make function


				static const float4 curve4_0 = float4(0.0205f, 0.0205f, 0.0205f, 0.0f);
				static const float4 curve4_1 = float4(0.0855f, 0.0855f, 0.0855f, 0.0f);
				static const float4 curve4_2 = float4(0.232f, 0.232f, 0.232f, 0.0f);
				static const float4 curve4_3 = float4(0.324f, 0.324f, 0.324f, 1.0f);


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
