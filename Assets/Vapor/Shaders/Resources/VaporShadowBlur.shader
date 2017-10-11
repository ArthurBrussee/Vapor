// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Hidden/Vapor/ShadowBlur" {
	Properties{
		_MainTex("Main tex", 2D) = "white" {}
	}
	
	SubShader{

		CGINCLUDE
			#include "UnityCG.cginc"
			half4 _MainTex_TexelSize;
			sampler2D _MainTex;
		ENDCG

		Pass{

			ZTest Always Cull Off ZWrite Off Blend Off

			CGPROGRAM

			#pragma vertex vertBlur
			#pragma fragment fragBlur8

			float4 _ShadowBlurSize;

			struct v2f_withBlurCoords8 {
				float4 pos : SV_POSITION;
				float4 uv : TEXCOORD0;
				float4 offs : TEXCOORD1;
			};

			v2f_withBlurCoords8 vertBlur(appdata_img v)
			{
				v2f_withBlurCoords8 o;
				o.pos = UnityObjectToClipPos(v.vertex);

				o.uv = float4(v.texcoord.xy, 1, 1);
				o.offs = float4(_MainTex_TexelSize.xy * _ShadowBlurSize.xy, 1, 1);
				return o;
			}

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
