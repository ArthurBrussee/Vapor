#ifndef VAPOR_FRAME
#define VAPOR_FRAME


#define UINT_MAX 4294967295.0f
#define ONE_OVER_4_PI (1.0f / 4.0f * 3.141596f)


#define X_RESOLUTION 160
#define Y_RESOLUTION 88
#define Z_RESOLUTION 256

float _DepthPow;
float4 _PlaneSettings;
float4x4 _VAPOR_I_VP;

float DeviceToLinearDepth(float device) {
	return (2 * _PlaneSettings.x) / (_PlaneSettings.x + _PlaneSettings.y - device * _PlaneSettings.z);
}

float LinearToDeviceDepth(float lin) {
	return (_PlaneSettings.x + _PlaneSettings.y - (2 * _PlaneSettings.x) / lin) / (_PlaneSettings.z);
}

float3 IDToUv(uint3 id, float3 center) {
	return saturate(float3((id.x + center.x) / X_RESOLUTION, (id.y + center.y) / Y_RESOLUTION, (id.z + center.z) / Z_RESOLUTION));
}

float3 UvToDevice(float3 uv) {
	float3 device;
	device.x = uv.x * 2.0f - 1.0f;
	device.y = uv.y * 2.0f - 1.0f;
	uv.z = pow(saturate(uv.z), _DepthPow);
	device.z = LinearToDeviceDepth(uv.z);

	return device;
}

float3 DeviceToUv(float3 device) {
	float3 uv;
	uv.z = DeviceToLinearDepth(device.z);
	uv.z = pow(saturate(uv.z), 1.0f / _DepthPow);
	uv.x = (device.x + 1.0f) * 0.5f;
	uv.y = (device.y + 1.0f) * 0.5f;
	return uv;
}

float DeviceToEye(float device) {
	return _PlaneSettings.w / (_PlaneSettings.y - device * _PlaneSettings.z);
}

float3 DeviceToWorld(float3 device) {
	float4 worldPosRaw = mul(_VAPOR_I_VP, float4(device, 1.0f));
	return worldPosRaw.xyz / worldPosRaw.w;
}

float3 UvToWorld(float3 uv) {
	float3 device = UvToDevice(uv);
	return DeviceToWorld(device);
}

float3 IDToWorld(uint3 id, float centerZ) {
	float3 uv = IDToUv(id, float3(0.5f, 0.5f, centerZ));
	return UvToWorld(uv);
}

Texture2D _NoiseTex;
SamplerState sampler_NoiseTex;

//Noise definitions
float4 _NoiseWeights;

float noise(float3 x)
{
	float3 f = frac(x);
	float3 p = floor(x);
	f = f * f * (3.0f - 2.0f * f);
	float2 uv = (p.xz + float2(37.0f, 17.0f) * p.y) + f.xz;
	float2 rg = _NoiseTex.SampleLevel(sampler_NoiseTex, uv / 256.0f, 0.0f).xy * 2.0f - 1.0f;
	return lerp(rg.x, rg.y, f.y);
}

float fractal_noise(float3 p)
{
	float f = _NoiseWeights.x * noise(p) +
		_NoiseWeights.y * noise(1.5f * p) +
		_NoiseWeights.z * noise(2.25f * p);
	//f += 0.12500 * noise(p);

	return f;
}

/*

float density(vec3 pos)
{
	float den = 3.0 * fractal_noise(pos * 0.3) - 2.0 + (pos.y - MIN_HEIGHT);
	//float edge = 1.0 - smoothstep(MIN_HEIGHT, MAX_HEIGHT, pos.y);
	edge *= edge;
	den *= edge;
	den = clamp(den, 0.0, 1.0);

	return den;
}*/

#endif //VAPOR_FRAME