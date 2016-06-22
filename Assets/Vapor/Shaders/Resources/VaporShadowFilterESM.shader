Shader "Hidden/Vapor/ShadowFilterESM" {
	Properties{ 
		_MainTex("Texture", any) = "" {}
		_ShadowSoft("Shadow soft", Float) = 0
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

					//return _MainTex.SampleLevel(sampler_MainTex, IN.uv, 0);
				
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