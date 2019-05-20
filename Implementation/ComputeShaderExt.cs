using UnityEngine;

public static class ComputeShaderExt {
	public static int TryFindKernel(this ComputeShader shader, string name) {
		return shader.HasKernel(name) ? shader.FindKernel(name) : -1;
	}

	public static void DispatchScaled(this ComputeShader shader, int kernel, int xCount, int yCount, int zCount) {
		uint xs, ys, zs;
		shader.GetKernelThreadGroupSizes(kernel, out xs, out ys, out zs);
		shader.Dispatch(kernel, Mathf.CeilToInt(xCount / (float) xs), Mathf.CeilToInt(yCount / (float) ys), Mathf.CeilToInt(zCount / (float) zs));
	}
}