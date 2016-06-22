Shader "Hidden/Vapor/ShadowBlur" {
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
			half4 _MainTex_TexelSize;
			sampler2D _MainTex;
		ENDCG

			Pass{

			ZTest Always Cull Off ZWrite Off Blend Off
			Fog{ Mode off }


			CGPROGRAM


#pragma vertex vertBlur
#pragma fragment fragBlur8

			float _ShadowBlurSize;

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
			o.offs = float4(_MainTex_TexelSize.x * _ShadowBlurSize, 0.0f, 1, 1);

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



			Pass{

			ZTest Always Cull Off ZWrite Off Blend Off
			Fog{ Mode off }


			CGPROGRAM


				#pragma vertex vertBlur
				#pragma fragment fragBlur8

				float _ShadowBlurSize;

				struct v2f_withBlurCoords8 {
					float4 pos : SV_POSITION;
					float4 uv : TEXCOORD0;
					float4 offs : TEXCOORD1;
				};

				v2f_withBlurCoords8 vertBlur(appdata_img v) {
					v2f_withBlurCoords8 o;
					o.pos = mul(UNITY_MATRIX_MVP, v.vertex);

					o.uv = float4(v.texcoord.xy, 1, 1);
					o.offs = float4(0.0f, _MainTex_TexelSize.y * _ShadowBlurSize, 1, 1);

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
