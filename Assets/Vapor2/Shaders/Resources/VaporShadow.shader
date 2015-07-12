// Collects cascaded shadows into screen space buffer
Shader "Hidden/Vapor/Shadow" {
	Properties{
		_MainTex("", any) = "" {}
	}

		CGINCLUDE
#include "UnityCG.cginc"

		// Configuration



	struct appdata {
		float4 vertex : POSITION;
		float2 texcoord : TEXCOORD0;
		float3 normal : NORMAL;
	};

	struct v2f {
		float2 uv : TEXCOORD0;

		// View space ray, for perspective case
		float4 ray : TEXCOORD1;
		// Orthographic view space position, xy regular, z position at near plane, w position at far plane
		float4 orthoPos : TEXCOORD2;

		float4 pos : SV_POSITION;
	};

	v2f vert(appdata v)
	{
		v2f o;
		o.uv = v.texcoord;
		float4 clipPos = mul(UNITY_MATRIX_MVP, v.vertex);
		o.pos = clipPos;

		o.ray = clipPos;


		// To compute view space position from Z buffer for orthographic case,
		// we need different code than for perspective case. We want to avoid
		// doing matrix multiply in the pixel shader: less operations, and less
		// constant registers used. Particularly with constant registers, having
		// unity_CameraInvProjection in the pixel shader would push the PS over SM2.0
		// limits.

		clipPos.y *= _ProjectionParams.x;
		float4 orthoNearPos = mul(unity_CameraInvProjection, float4(clipPos.x, clipPos.y, -1, 1));
		float4 orthoFarPos = mul(unity_CameraInvProjection, float4(clipPos.x, clipPos.y, 1, 1));
		o.orthoPos = float4(orthoNearPos.x, orthoNearPos.y, -orthoNearPos.z, -orthoFarPos.z);

		return o;
	}

	sampler2D_float _CameraDepthTexture;
	// sizes of cascade projections, relative to first one
	float4 unity_ShadowCascadeScales;

	CBUFFER_START(UnityPerCamera2)
		float4x4 _CameraToWorld;
	CBUFFER_END

		UNITY_DECLARE_SHADOWMAP(_MainTex);
	float4 _MainTex_TexelSize;

	//
	// Keywords based defines
	//
#if defined (SHADOWS_SPLIT_SPHERES)
#define GET_CASCADE_WEIGHTS(wpos, z)    getCascadeWeights_splitSpheres(wpos)
#define GET_SHADOW_FADE(wpos, z)		getShadowFade_SplitSpheres(wpos)
#else
#define GET_CASCADE_WEIGHTS(wpos, z)	getCascadeWeights( wpos, z )
#define GET_SHADOW_FADE(wpos, z)		getShadowFade(z)
#endif

#if defined (SHADOWS_SINGLE_CASCADE)
#define GET_SHADOW_COORDINATES(wpos,cascadeWeights)	getShadowCoord_SingleCascade(wpos)
#else
#define GET_SHADOW_COORDINATES(wpos,cascadeWeights)	getShadowCoord(wpos,cascadeWeights)
#endif

	// prototypes 
	inline fixed4 getCascadeWeights(float3 wpos, float z);		// calculates the cascade weights based on the world position of the fragment and plane positions
	inline fixed4 getCascadeWeights_splitSpheres(float3 wpos);	// calculates the cascade weights based on world pos and split spheres positions
	inline float  getShadowFade_SplitSpheres(float3 wpos);
	inline float  getShadowFade(float3 wpos, float z);
	inline float4 getShadowCoord_SingleCascade(float4 wpos);	// converts the shadow coordinates for shadow map using the world position of fragment (optimized for single fragment)
	inline float4 getShadowCoord(float4 wpos, fixed4 cascadeWeights);// converts the shadow coordinates for shadow map using the world position of fragment
	half 		  sampleShadowmap_PCF5x5(float4 coord);		// samples the shadowmap based on PCF filtering (5x5 kernel)
	half 		  unity_sampleShadowmap(float4 coord);		// sample shadowmap SM2.0+

															/**
															* Gets the cascade weights based on the world position of the fragment.
															* Returns a float4 with only one component set that corresponds to the appropriate cascade.
															*/
	inline fixed4 getCascadeWeights(float3 wpos, float z)
	{
		fixed4 zNear = float4(z >= _LightSplitsNear);
		fixed4 zFar = float4(z < _LightSplitsFar);
		fixed4 weights = zNear * zFar;
		return weights;
	}

	/**
	* Gets the cascade weights based on the world position of the fragment and the poisitions of the split spheres for each cascade.
	* Returns a float4 with only one component set that corresponds to the appropriate cascade.
	*/
	inline fixed4 getCascadeWeights_splitSpheres(float3 wpos)
	{
		float3 fromCenter0 = wpos.xyz - unity_ShadowSplitSpheres[0].xyz;
		float3 fromCenter1 = wpos.xyz - unity_ShadowSplitSpheres[1].xyz;
		float3 fromCenter2 = wpos.xyz - unity_ShadowSplitSpheres[2].xyz;
		float3 fromCenter3 = wpos.xyz - unity_ShadowSplitSpheres[3].xyz;
		float4 distances2 = float4(dot(fromCenter0, fromCenter0), dot(fromCenter1, fromCenter1), dot(fromCenter2, fromCenter2), dot(fromCenter3, fromCenter3));
		fixed4 weights = float4(distances2 < unity_ShadowSplitSqRadii);
		weights.yzw = saturate(weights.yzw - weights.xyz);
		return weights;
	}

	/**
	* Returns the shadow fade based on the 'z' position of the fragment
	*/
	inline float getShadowFade(float z)
	{
		return saturate(z * _LightShadowData.z + _LightShadowData.w);
	}

	/**
	* Returns the shadow fade based on the world position of the fragment, and the distance from the shadow fade center
	*/
	inline float getShadowFade_SplitSpheres(float3 wpos)
	{
		float sphereDist = distance(wpos.xyz, unity_ShadowFadeCenterAndType.xyz);
		half shadowFade = saturate(sphereDist * _LightShadowData.z + _LightShadowData.w);
		return shadowFade;
	}

	/**
	* Returns the shadowmap coordinates for the given fragment based on the world position and z-depth.
	* These coordinates belong to the shadowmap atlas that contains the maps for all cascades.
	*/
	inline float4 getShadowCoord(float4 wpos, fixed4 cascadeWeights)
	{
		float3 sc0 = mul(unity_World2Shadow[0], wpos).xyz;
		float3 sc1 = mul(unity_World2Shadow[1], wpos).xyz;
		float3 sc2 = mul(unity_World2Shadow[2], wpos).xyz;
		float3 sc3 = mul(unity_World2Shadow[3], wpos).xyz;
		return float4(sc0 * cascadeWeights[0] + sc1 * cascadeWeights[1] + sc2 * cascadeWeights[2] + sc3 * cascadeWeights[3], 1);
	}

	/**
	* Same as the getShadowCoord; but optimized for single cascade
	*/
	inline float4 getShadowCoord_SingleCascade(float4 wpos)
	{
		return float4(mul(unity_World2Shadow[0], wpos).xyz, 0);
	}

	/**
	* Computes the receiver plane depth bias for the given shadow coord in screen space.
	* Inspirations:
	*		http://mynameismjp.wordpress.com/2013/09/10/shadow-maps/
	*		http://amd-dev.wpengine.netdna-cdn.com/wordpress/media/2012/10/Isidoro-ShadowMapping.pdf
	*/
	float2 getReceiverPlaneDepthBias(float3 shadowCoord)
	{
		float2 biasUV;
		float3 dx = ddx(shadowCoord);
		float3 dy = ddy(shadowCoord);

		biasUV.x = dy.y * dx.z - dx.y * dy.z;
		biasUV.y = dx.x * dy.z - dy.x * dx.z;
		biasUV *= 1.0f / ((dx.x * dy.y) - (dx.y * dy.x));
		return biasUV;
	}

	/**
	* Combines the different components of a shadow coordinate and returns the final coordinate.
	*/
	inline float3 combineShadowcoordComponents(float2 baseUV, float2 deltaUV, float depth, float2 receiverPlaneDepthBias)
	{
		float3 uv = float3(baseUV + deltaUV, depth);
		uv.z += dot(deltaUV, receiverPlaneDepthBias); // apply the depth bias
		return uv;
	}
	
	/**
	*	Samples the shadowmap at the given coordinates.
	*/
	half unity_sampleShadowmap(float4 coord)
	{
		half shadow = UNITY_SAMPLE_SHADOW(_MainTex, coord);
		shadow = lerp(_LightShadowData.r, 1.0, shadow);
		return shadow;
	}

	float4x4 _VaporItVP;
	

	fixed4 frag_hard(v2f i) : SV_Target
	{		
		float zdepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
		
		//return float4(i.uv.x, i.uv.y, 0, 1);
		// 0..1 linear depth, 0 at near plane, 1 at far plane.
		//float depth = lerp(Linear01Depth(zdepth), zdepth, unity_OrthoParams.w);

		/*
		// view position calculation for perspective & ortho cases
		float3 vposPersp = i.ray * depth;
		float3 vposOrtho = i.orthoPos.xyz;
		vposOrtho.z = lerp(i.orthoPos.z, i.orthoPos.w, zdepth);
		// pick the perspective or ortho position as needed
		float3 vpos = lerp(vposPersp, vposOrtho, unity_OrthoParams.w);

		float4 wpos = mul(_CameraToWorld, float4(vpos,1));

		fixed4 cascadeWeights = GET_CASCADE_WEIGHTS(wpos, vpos.z);
		*/
		
		fixed4 cascadeWeights = 0;
		float4 wpos = mul(_VaporItVP, float4(i.ray.x, i.ray.y, zdepth, 1.0f));	



		float4 coord = mul(unity_World2Shadow[0], wpos);
		half shadow = tex2D(_MainTex, coord.xy / coord.w);


		return shadow;

		return float4(coord.xy / coord.w, 0.0f, 1.0f);

		return float4(wpos.xyz / wpos.w, 1.0f);
		
		//shadow += GET_SHADOW_FADE(wpos, vpos.z);
	
		//half shadow = unity_sampleShadowmap(coord);
	
		return shadow;
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
