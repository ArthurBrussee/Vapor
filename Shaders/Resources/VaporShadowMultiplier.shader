Shader "Hidden/Vapor/VaporShadowMultiplier"
{
	Properties
	{
		_Color("Main Color", Color) = (1,1,1,1)
		_MainTex("Main Texture", 2D) = "white" {}
		_Range("_Range", Float) = 1.0
	}
	SubShader
	{
		LOD 100
		Pass
		{
			Tags {"LightMode" = "ForwardBase"}
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma shader_feature CustomMap

			#include "UnityCG.cginc"
			#include "AutoLight.cginc"
			struct appdata
			{
			float4 vertex : POSITION;
			float2 uv : TEXCOORD0;
			};
			struct v2f
			{
			float4 pos : SV_POSITION;
			float2 uv : TEXCOORD0;
			float4 shadowCoord : TEXCOORD1;
			};
			sampler2D _MainTex;
			float4 _MainTex_ST;
			float4 _Color;
			uniform sampler2D _VaporCustomLightShadow;
			uniform sampler2D _VaporCustomShadowMap;

			uniform float _Range;

			v2f vert(appdata v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.uv;
				o.shadowCoord = mul(unity_WorldToShadow[0], mul(unity_ObjectToWorld, v.vertex));
				return o;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				float2 screenUV = i.shadowCoord.xy / i.shadowCoord.w;
				fixed shadow = tex2D(_VaporCustomLightShadow, i.uv).r * _Range;
				#if CustomMap
				shadow = tex2D(_VaporCustomShadowMap, i.uv).r * _Range;
				#endif
				return float4(shadow,shadow,shadow,shadow);
			}
			ENDCG
		}
	}
}