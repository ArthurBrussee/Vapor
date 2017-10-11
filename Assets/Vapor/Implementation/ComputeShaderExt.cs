using UnityEngine;

public static class ComputeShaderExt {
    private static float[] s_matrixFloats = new float[16];

    public static void SetMatrix(this ComputeShader shader, string name, Matrix4x4 matrix) {
        for (int i = 0; i < 16; ++i) {
            s_matrixFloats[i] = matrix[i];
        }

        shader.SetFloats(name, s_matrixFloats);
    }

	public static int TryFindKernel(this ComputeShader shader, string name) {
		return shader.HasKernel(name) ? shader.FindKernel(name) : - 1;
	}

	public static void DispatchScaled(this ComputeShader shader, int kernel, int xCount, int yCount, int zCount) {
		uint xs, ys, zs;
		shader.GetKernelThreadGroupSizes(kernel, out xs, out ys, out zs);
		shader.Dispatch(kernel, Mathf.CeilToInt(xCount / (float)xs), Mathf.CeilToInt(yCount / (float)ys), Mathf.CeilToInt(zCount / (float)zs));
	}
}