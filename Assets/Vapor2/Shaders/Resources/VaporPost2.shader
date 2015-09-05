Shader "Hidden/VaporPost2" {
	Properties {
		_MainTex("Main tex", 2D) = "white" {}
		


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
	ENDCG

		Pass{		
			CGPROGRAM
					
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 5.0

			#define Z_POWER_CURVE 0.02

			float _MaskStrength;
			float _NearPlane;
			float _FarPlane;
			
			sampler2D_float _CameraDepthTexture;

			struct v2fFog {
				float4 pos : SV_POSITION;
				float4 texcoord : TEXCOORD0;
				float3 worldPos : TEXCOORD1;

				float4 cameraRay : TEXCOORD2;
			};

			float4x4 InverseProjectionMatrix;
			float4x4 InverseViewMatrix;

			v2fFog vert(appdata_full v){
				v2fFog o;
				
				float4 pos = mul(UNITY_MATRIX_MVP, v.vertex);
				o.pos = pos;
				o.texcoord = float4(v.texcoord.xy, 0, 0);
				o.worldPos = mul(_Object2World, v.vertex).xyz;


				float4 clipPos = float4(v.texcoord.xy * 2.0 - 1.0, 1.0, 1.0);
				float4 cameraRay = mul(InverseProjectionMatrix, clipPos);
				o.cameraRay = cameraRay / cameraRay.w;

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

			
			sampler3D _ScatterTex;

			float4 frag(v2fFog i) : COLOR0 {	
				// read low res depth and reconstruct world position
				float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.texcoord);
               	float4 color = tex2D(_MainTex, i.texcoord);
				depth = pow(depth, 1.0f / Z_POWER_CURVE);
				
				//TODO: Halton jitter

				float4 fog = tex3Dlod(_ScatterTex, float4(i.texcoord.xy, depth, 0.0f));


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

				fog.a = exp(-fog.a);
				return float4(color.rgb * fog.aaa + fog.rgb, color.a);
			}
			
			ENDCG
		} 

		Pass 	//1 Downsample
		{ 	
			CGPROGRAM	
			
					
			#pragma vertex vert4Tap
			#pragma fragment fragDownsample
			#pragma fragmentoption ARB_precision_hint_fastest 
			


		struct v2f_tap
		{
			float4 pos : SV_POSITION;
			float4 uv20 : TEXCOORD0;
			float4 uv21 : TEXCOORD1;
			float4 uv22 : TEXCOORD2;
			float4 uv23 : TEXCOORD3;
		};

		v2f_tap vert4Tap ( appdata_img v )
		{
			v2f_tap o;

			o.pos = mul (UNITY_MATRIX_MVP, v.vertex);
        	o.uv20 = float4(v.texcoord.xy + _MainTex_TexelSize.xy, 0.0f, 0.0f);				
			o.uv21 = float4(v.texcoord.xy + _MainTex_TexelSize.xy * float2(-0.5f, -0.5f), 0.0f, 0.0f);	
			o.uv22 = float4(v.texcoord.xy + _MainTex_TexelSize.xy * float2(0.5f, -0.5f), 0.0f, 0.0f);		
			o.uv23 = float4(v.texcoord.xy + _MainTex_TexelSize.xy * float2(-0.5f, 0.5f), 0.0f, 0.0f);		
  
			return o; 
		}		
		
		fixed4 fragDownsample ( v2f_tap i ) : COLOR {				
			fixed4 color = tex2D (_MainTex, i.uv20.xy);
			color = min(color, tex2D (_MainTex, i.uv21.xy));
			color = min(color, tex2D (_MainTex, i.uv22.xy));
			color = min(color, tex2D (_MainTex, i.uv23.xy));

			return max(color, 0);
		}
					
			ENDCG		 
		}
	}
}
