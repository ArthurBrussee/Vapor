using UnityEngine;

public struct VaporKernel {
	int[] m_kern;

	public enum ShadowMode {
		None,
		Shadowed,
		Cascaded
	}

	public VaporKernel(ComputeShader compute, string baseName) {
		string shadowName = baseName + "Shadow";
		string cascadeName = baseName + "ShadowCascade";

		m_kern = new int[3];

		m_kern[0] = compute.TryFindKernel(baseName);
		m_kern[1] = compute.TryFindKernel(shadowName);
		m_kern[2] = compute.TryFindKernel(cascadeName);
	}

	public int GetKernel(ShadowMode shadow) {
		return m_kern[(int) shadow];
	}
}