using UnityEngine;

public static class ComputeShaderExt {
    private static float[] s_matrixFloats = new float[16];

    public static void SetMatrix(this ComputeShader shader, string name, Matrix4x4 matrix) {
        for (int i = 0; i < 16; ++i) {
            s_matrixFloats[i] = matrix[i];
        }

        shader.SetFloats(name, s_matrixFloats);
    }
}