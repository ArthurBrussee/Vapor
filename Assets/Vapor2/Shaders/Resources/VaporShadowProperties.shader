// Upgrade NOTE: replaced 'unity_World2Shadow' with 'unity_WorldToShadow'

// Collects cascaded shadows into screen space buffer
Shader "Hidden/Vapor/ShadowProperties" {
	Properties{
		_MainTex("", any) = "" {}
	}

		CGINCLUDE
	#include "UnityCG.cginc"
	#include "VaporCommon.cginc"
	
	#pragma target 5.0

	// Configuration
	sampler2D _MainTex;



	fixed4 frag(v2f i) : SV_Target{
		uint index = floor(i.uv.x * 4.0f);
		uint y = floor(i.uv.y * 5.0f);

		if (y < 4) {
			return unity_WorldToShadow[y][index];
		}

		if (y == 4) {
			if (index == 0) {
				return _LightSplitsNear;
			}

			if (index == 1) {
				return _LightSplitsFar;
			}

			if (index == 2) {
				return _LightShadowData;
			}
		}
		return 0.0f;
	}
	
	//Spot lights only need VP matrix
	fixed4 frag_spot(v2f i) : SV_Target{
		uint index = floor(i.uv.x * 4.0f);
		return UNITY_MATRIX_VP[index];
	}

	ENDCG


	SubShader {
		//Pass for directional light properties
		Pass{
			ZWrite Off ZTest Always Cull Off

			CGPROGRAM
				#pragma vertex vert_vapor_fs
				#pragma fragment frag

			ENDCG
		}

		//Pass for spot light properties
		Pass{
			ZWrite Off ZTest Always Cull Off

			CGPROGRAM
				#pragma vertex vert_vapor_fs
				#pragma fragment frag_spot
			ENDCG
		}
	}

	Fallback Off
}
