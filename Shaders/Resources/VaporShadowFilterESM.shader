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
				Texture2D<float> _ShadowMap;
				SamplerState sampler_ShadowMap;


				float4 frag(v2f IN) : COLOR{
					float depth = _ShadowMap.SampleLevel(sampler_ShadowMap, IN.uv, 0).r;

					//Disabled atm
					return depth;
					//return float4(depth, depth * depth, 0.0f, 1.0f);
				}
			ENDCG
		}
	}
}