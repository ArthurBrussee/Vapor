// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "Hidden/VaporNoiseVisualize" {
	Properties {
		_MainTex("Main tex", 2D) = "white" {}
		_NoiseTex0("Noise tex 0", 3D) = "white" {}
		_NoiseTex1("Noise tex 1", 3D) = "white" {}
		_NoiseTex2("Noise tex 2", 3D) = "white" {}

		_NoiseScale0("Scale 0", Vector) = (0, 0, 0, 0)
		_NoiseScale1("Scale 0", Vector) = (0, 0, 0, 0)
		_NoiseScale2("Scale 0", Vector) = (0, 0, 0, 0)

		_NoiseScroll0("Scale 0", Vector) = (0, 0, 0, 0)
		_NoiseScroll1("Scale 0", Vector) = (0, 0, 0, 0)
		_NoiseScroll2("Scale 0", Vector) = (0, 0, 0, 0)

		_Color("Color", Color) = (1, 1, 1, 1)
	}

	SubShader {
		Pass{
			Cull Off
			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off

			CGPROGRAM
							
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 5.0
					
			#include "UnityCG.cginc"
			half4 _MainTex_TexelSize;
			sampler2D _MainTex;

			sampler3D _NoiseTex0;
			sampler3D _NoiseTex1;
			sampler3D _NoiseTex2;
			
			float4 _Color;

			struct v2fFog {
				float4 pos : SV_POSITION;
				float4 texcoord : TEXCOORD0;
			};

			v2fFog vert(appdata_full v){
				v2fFog o;
				o.pos =  UnityObjectToClipPos(v.vertex);
				o.texcoord = float4(v.texcoord.xy, 0, 0);
				return o;
			}
			
			float4 frag(v2fFog i) : COLOR0 {
				float4 uv = float4(i.texcoord.xy, 0.5f, 0.0f);
				float alph =  tex3Dlod(_NoiseTex0, uv).a;

				return float4(alph, alph, alph, _Color.a);
			}
			
			ENDCG
		} 

		Pass{
			CGPROGRAM
							
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 5.0
					
			#include "UnityCG.cginc"
			half4 _MainTex_TexelSize;
			sampler2D _MainTex;

			sampler3D _NoiseTex0;
			sampler3D _NoiseTex1;
			sampler3D _NoiseTex2;
			

			float4 _NoiseScale0;
			float4 _NoiseScale1;
			float4 _NoiseScale2;


			float4 _NoiseScroll0;
			float4 _NoiseScroll1;
			float4 _NoiseScroll2;

			float4 _Color;

			struct v2fFog {
				float4 pos : SV_POSITION;
				float4 texcoord : TEXCOORD0;
				float3 worldPos : TEXCOORD1;
			};

			v2fFog vert(appdata_full v){
				v2fFog o;
				o.pos =  UnityObjectToClipPos(v.vertex);
				o.texcoord = float4(v.texcoord.xy, 0, 0);

				o.worldPos = mul(unity_ObjectToWorld, v.vertex);

				return o;
			}
			
			float4 _NoiseStrength;

			float4 frag(v2fFog i) : COLOR0 {
				float3 worldPos = i.worldPos;


				float3 coord0 = worldPos * _NoiseScale0 + _NoiseScroll0;
				float3 coord1 = worldPos * _NoiseScale1 + _NoiseScroll1;
				float3 coord2 = worldPos * _NoiseScale2 + _NoiseScroll2;

				float n0 = tex3Dlod(_NoiseTex0, float4(coord0, 0.0f)).a;
				float n1 = tex3Dlod(_NoiseTex1, float4(coord1, 0.0f)).a;
				float n2 = tex3Dlod(_NoiseTex2, float4(coord2, 0.0f)).a;
				
				float alph = dot(_NoiseStrength.xyz, float3(n0, n1, n2)) / (dot(_NoiseStrength.xyz, 1.0f));


				return float4(alph, alph, alph, _Color.a);
			}
			
			ENDCG
		}
	}
}
