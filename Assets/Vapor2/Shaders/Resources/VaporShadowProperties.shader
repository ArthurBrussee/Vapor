// Collects cascaded shadows into screen space buffer
Shader "Hidden/Vapor/ShadowProperties" {
	Properties{
		_MainTex("", any) = "" {}
	}

		CGINCLUDE
#include "UnityCG.cginc"
#pragma target 5.0

		// Configuration

		sampler2D _MainTex;


	struct appdata {
		float4 vertex : POSITION;
		float2 texcoord : TEXCOORD0;
		float3 normal : NORMAL;
	};

	struct v2f {
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
	};

	v2f vert(appdata v)
	{
		v2f o;
		o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
		o.uv = v.texcoord;

		return o;
	}

	fixed4 frag_hard(v2f i) : SV_Target
	{

		int index = floor(i.uv.x * 4.0f);
		int y = floor(i.uv.y * 5.0f);

		if (y < 4) {
			return unity_World2Shadow[y][index];
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

		//More properties?
		return 0.0f;
	}
	
	ENDCG


		// ----------------------------------------------------------------------------------------
		// Subshader for hard shadows:
		// Just collect shadows into the buffer. Used on pre-SM3 GPUs and when hard shadows are picked.

		SubShader {
		Pass{
			ZWrite Off ZTest Always Cull Off

			CGPROGRAM
#pragma vertex vert
#pragma fragment frag_hard

			ENDCG
		}
	}

	Fallback Off
}
