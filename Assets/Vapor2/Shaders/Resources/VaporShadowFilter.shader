Shader "Hidden/Vapor/ShadowFilterESM" {
	Properties{ }
	
	SubShader{
		ZTest Always Cull Off ZWrite Off Fog{ Mode Off }
	
		Pass{
			CGPROGRAM
				#pragma vertex vert_img
				#pragma fragment frag
				#include "UnityCG.cginc"

				sampler2D _ShadowMapTexture;

				//TODO: Do ESM Here
				float4 frag(v2f_img IN) : COLOR{
					float4 c = tex2D(_ShadowMapTexture, IN.uv);
					return c;
				}
			ENDCG
		}
	}
}