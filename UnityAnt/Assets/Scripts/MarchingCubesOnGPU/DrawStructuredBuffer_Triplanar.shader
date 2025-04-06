Shader "Custom/DrawStructuredBuffer_Triplanar"
{
    Properties
    {
        _TextureX ("Texture X", 2D) = "white" {}
        _TextureY ("Texture Y", 2D) = "white" {}
        _TextureZ ("Texture Z", 2D) = "white" {}
        _Tiling ("Tiling", Float) = 0.05
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off // <- can also try Cull Front or Cull Back to test

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct Vert {
                float4 position;
                float3 normal;
            };

            StructuredBuffer<Vert> _Buffer;

            sampler2D _TextureX;
            sampler2D _TextureY;
            sampler2D _TextureZ;
            float _Tiling;

            struct appdata
            {
                uint vertexID : SV_VertexID;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                Vert data = _Buffer[v.vertexID];
                o.vertex = UnityObjectToClipPos(data.position);
                o.worldPos = data.position.xyz;
                o.normal = normalize(data.normal);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // Normalize the normal to ensure smooth blending
                float3 normal = normalize(i.normal);
    
                // Compute absolute normal values
                float3 absNormal = abs(normal);
    
                // Get the absolute value of the normal to blend the textures along the X, Y, and Z axes
                float3 blend = abs(normal);
                blend = blend / (blend.x + blend.y + blend.z);  // Normalize the blend weights

                // Project the texture coordinates based on the world position
                float3 worldPos = i.worldPos * _Tiling;

                // Correct texture projection for each axis
                float3 xTex = tex2D(_TextureX, worldPos.yz).rgb; // Texture for X-axis
                float3 yTex = tex2D(_TextureY, worldPos.zx).rgb; // Texture for Y-axis
                float3 zTex = tex2D(_TextureZ, worldPos.xy).rgb; // Texture for Z-axis

                // Now blend the textures based on the normal's direction
                float3 baseColor = xTex * blend.x + yTex * blend.y + zTex * blend.z;

                // Final color with simple directional lighting (optional)
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float lightIntensity = max(dot(normal, lightDir), 0.0);
                float3 color = baseColor * lightIntensity;

                // Return the final color
                return float4(color, 1);  // Returning blended color
            }



            ENDHLSL
        }
    }
    FallBack "Diffuse"
}
