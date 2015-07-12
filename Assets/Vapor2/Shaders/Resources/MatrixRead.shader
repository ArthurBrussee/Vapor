Shader "Hidden/Vapor/MatrixRead" {
	Properties{
	}


		SubShader{

			Pass{

			ZTest Always
			Cull Off
			ZWrite Off
		ColorMask RGBA
			
			

			CGPROGRAM

				#pragma vertex vert
				#pragma fragment frag
				#pragma target 5.0	


				#include "UnityCG.cginc"


				RWStructuredBuffer<float4x4> ShadowProperties;



				struct v2f {
					float4 pos : SV_POSITION;
				};

				v2f vert(uint id : SV_VertexID, uint inst : SV_InstanceID) {
					v2f o;
					o.pos = 0.5f;


					if (id == 0) {
						o.pos = float4(0.25f, 0.25f, 0.0f, 1.0f);
					}
					else if (id == 1) {
						o.pos = float4(0.75f, 0.25f, 0.0f, 1.0f);

					}
					else if (id == 2) {
						o.pos = float4(0.25f, 0.75f, 0.0f, 1.0f);

					}
					else if (id == 3) {
						o.pos = float4(0.75f, 0.75f, 0.0f, 1.0f);

					}

					//if (id == 0){
					ShadowProperties[0] = 1.0f;
	

						//ShadowProperties[0] = float4x4(float4(1, 0, 0, 0), float4(0, 1, 0, 0), float4(0, 0, 1, 0), float4(0, 0, 0, 1));
					//}

					return o;
				}

				float4 frag(v2f i) : COLOR0{
					return float4(1.0f, 0.0f, 1.0f, 1.0f);
				}
			ENDCG
		}
	}
}
