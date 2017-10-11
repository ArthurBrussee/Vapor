#ifndef VAPOR_FRAME
#define VAPOR_FRAME

float _VaporDepthPow;
uint3 _VaporResolution;
float4 _VaporPlaneSettings;

float4x4 _VAPOR_I_VP;
float4x4 _VAPOR_VP;

//Device is device coordinates as vi
float VaporDeviceToLinearDepth(float device){
	return device / (_VaporPlaneSettings.y - device * _VaporPlaneSettings.z);
}

float LinearToVaporDeviceDepth(float lin) {
	return lin * _VaporPlaneSettings.y / (1 + lin * _VaporPlaneSettings.z);
}

inline float VaporDeviceToEyeDepth( float device ) {
	return 1.0 / (_ZBufferParams.z * device + _ZBufferParams.w);
}

float3 UvToLinearVaporDevice(float3 uv){
	float3 device;
	device.x = uv.x * 2.0f - 1.0f;
	device.y = uv.y * 2.0f - 1.0f;
	uv.z = pow(saturate(uv.z), _VaporDepthPow);
	device.z = uv.z;

	return device;
}

float3 UvToVaporDevice(float3 uv) {
	float3 device;
	device.x = uv.x * 2.0f - 1.0f;
	device.y = uv.y * 2.0f - 1.0f;
	uv.z = pow(saturate(uv.z), _VaporDepthPow);
	device.z = LinearToVaporDeviceDepth(uv.z);

	return device;
}

float3 VaporDeviceToUv(float3 device) {
	float3 uv;
	uv.z = VaporDeviceToLinearDepth(device.z);
	uv.z = pow(saturate(uv.z), 1.0f / _VaporDepthPow);
	uv.x = (device.x + 1.0f) * 0.5f;
	uv.y = (device.y + 1.0f) * 0.5f;
	return uv;
}

float3 VaporDeviceToWorld(float3 device) {
	float4 worldPosRaw = mul(_VAPOR_I_VP, float4(device, 1.0f));
	return worldPosRaw.xyz / worldPosRaw.w;
}

float3 WorldToVaporDevice(float3 world) {
	float4 deviceRaw = mul(_VAPOR_VP, float4(world, 1.0f));
	return deviceRaw.xyz / deviceRaw.w;
}

float3 VaporUvToWorld(float3 uv) {
	float3 device = UvToVaporDevice(uv);
	return VaporDeviceToWorld(device);
}

float3 WorldToVaporUv(float3 world) {
	float3 device = WorldToVaporDevice(world);
	return VaporDeviceToUv(device);
}


#if !defined(VAPOR_TRANSLUCENT_FOG_ON)
	#define VAPOR_TRANSLUCENT_FOG_ON 1
#endif

sampler3D _VaporFogTexture;

float4 VaporApplyFog(float3 world, float4 color){
	#if VAPOR_TRANSLUCENT_FOG_ON
		float3 uv = WorldToVaporUv(world);
		float4 fog = tex3Dlod(_VaporFogTexture, float4(uv, 0));
		return float4(color.rgb * fog.a + fog.rgb, color.a);
	#else
		return color;
	#endif
}

float4 VaporApplyFogAdd(float3 world, float4 color){
	#if VAPOR_TRANSLUCENT_FOG_ON
		float3 uv = WorldToVaporUv(world);
		float4 fog = tex3Dlod(_VaporFogTexture, float4(uv, 0));
		return float4(color.rgb * fog.a, color.a);
	#else
		return color;
	#endif
}

float4 VaporApplyFogFade(float3 world, float4 color, float3 fade) {
#if VAPOR_TRANSLUCENT_FOG_ON
	float3 uv = WorldToVaporUv(world);
	float4 fog = tex3Dlod(_VaporFogTexture, float4(uv, 0));

	return float4(lerp(color.rgb, fade, fog.a), color.a);
#else
	return color;
#endif
}

float _VaporFlipSkybox;
float _VaporForward;

float4 VaporApplyFogSky(float3 worldPos, float4 color){
	if (_VaporForward < 0.5f){
		return color;	
	}
	

	float3 ray = normalize(worldPos - _WorldSpaceCameraPos.xyz);
	float lambda = _ProjectionParams.z / dot(ray, unity_CameraWorldClipPlanes[5].xyz);
	
	worldPos = _WorldSpaceCameraPos.xyz + ray * lambda;
	
	return VaporApplyFog(worldPos, color);
}


#endif //VAPOR_FRAME