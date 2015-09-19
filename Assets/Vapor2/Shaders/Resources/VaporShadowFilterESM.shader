Shader "Hidden/Vapor/ShadowFilterESM" {
	Properties{ 
		_ShadowSoft("Shadow soft", Float) = 0
		_MainTex("Shadow soft", 2D) = "black" {}
	}
	
	SubShader{
		ZTest Always Cull Off ZWrite Off Fog{ Mode Off }
	
		Pass{
			CGPROGRAM
				#pragma vertex vert_vapor_fs
				#pragma fragment frag
				#pragma target 5.0

				#include "UnityCG.cginc"
				#include "VaporCommon.cginc"

				float _ShadowSoft;
				Texture2D<float> _MainTex;
				SamplerState sampler_MainTex;

				float4 frag(v2f IN) : COLOR{
					//float c = _MainTex.Sample(sampler_MainTex, IN.uv).r;
					//return c;
					float4 accum = 0.0f;
					accum += exp(_ShadowSoft * _MainTex.GatherRed(sampler_MainTex, IN.uv, int2(0, 0)));
					accum += exp(_ShadowSoft * _MainTex.GatherRed(sampler_MainTex, IN.uv, int2(2, 0)));
					accum += exp(_ShadowSoft * _MainTex.GatherRed(sampler_MainTex, IN.uv, int2(0, 2)));
					accum += exp(_ShadowSoft * _MainTex.GatherRed(sampler_MainTex, IN.uv, int2(2, 2)));

					return dot(accum, 1.0f / 16.0f);
				}
			ENDCG
		}
	}
}