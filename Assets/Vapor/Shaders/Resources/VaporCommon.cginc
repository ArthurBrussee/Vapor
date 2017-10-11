#ifndef VAPOR_COMMON
#define VAPOR_COMMON

struct appdata {
	float4 vertex : POSITION;
	float2 texcoord : TEXCOORD0;
};

struct v2f {
	float4 pos : SV_POSITION;
	float2 uv : TEXCOORD0;
};

v2f vert_vapor_fs(appdata v)
{
	v2f o;

	//hack to make quad draw fullscreen - just convert UV into n. device coordinates
	o.pos = float4(float2(v.texcoord.x, 1.0f - v.texcoord.y) * 2.0f - 1.0f, 0.0f, 1.0f);
	o.uv = v.texcoord;


	return o;
}

#endif