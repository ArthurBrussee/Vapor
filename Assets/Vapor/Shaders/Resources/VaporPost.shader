// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Hidden/VaporPost" {
	Properties{
		_MainTex("Main tex", 2D) = "white" {}
		_ShadowBlurSize("Shadow blur", Vector) = (0, 0, 0, 0)
	}

	SubShader{
		CGINCLUDE
			#include "UnityCG.cginc"
			#include "../VaporFramework.cginc"
			half4 _MainTex_TexelSize;
			sampler2D _MainTex;
			#define DEBUG 0
		ENDCG


		Pass{
			ZWrite Off
			ZTest Always

			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			#pragma target 5.0


			sampler2D_float _CameraDepthTexture;


			struct v2fFog {
				float4 pos : SV_POSITION;
				float4 texcoord : TEXCOORD0;
				float3 worldPos : TEXCOORD1;
			};

			v2fFog vert(appdata_full v) {
				v2fFog o;

				float4 pos = UnityObjectToClipPos(v.vertex);
				o.pos = pos;
				o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

				o.texcoord.xy = v.texcoord.xy;

#if UNITY_UV_STARTS_AT_TOP
				if (_MainTex_TexelSize.y < 0) {
					o.texcoord.zw = float2(v.texcoord.x, 1.0f - v.texcoord.y);
				}
				else {
					o.texcoord.zw = v.texcoord.xy;
				}
#endif
				return o;
			}

			float4 _MainTex_ST;


			float3 GetWorldPos(float2 coord, float2 view) {
				float depth = tex2Dlod(_CameraDepthTexture, float4(coord.x, coord.y, 0.0, 0.0)).x;

			#if defined(UNITY_REVERSED_Z)
				depth = 1.0f -depth;
			#endif


				float4 viewCoord = float4(view.x * 2.0f - 1.0f, view.y * 2.0f - 1.0f, (2 * depth - 1), 1.0);
				float4 viewSpacePosition = mul(unity_CameraInvProjection, viewCoord);
				viewSpacePosition /= viewSpacePosition.w;
				viewSpacePosition.z *= -1;

				float4x4 camWorld = unity_CameraToWorld;
				float4 wpos = mul(camWorld, viewSpacePosition);
				wpos.xyz /= wpos.w;

				return wpos;
			}

			float4 frag(v2fFog i) : COLOR0 {
				float4 coord = UnityStereoScreenSpaceUVAdjust(i.texcoord, _MainTex_ST);
				float3 world = GetWorldPos(coord.xy, i.texcoord.xy);

				float4 color = tex2D(_MainTex, coord);

				float3 uv = WorldToVaporUv(world);
				float4 fog = tex3Dlod(_VaporFogTexture, float4(uv, 0));

				return float4(color.rgb * fog.a + fog.rgb, color.a);
			}
			ENDCG
		}
	}
}