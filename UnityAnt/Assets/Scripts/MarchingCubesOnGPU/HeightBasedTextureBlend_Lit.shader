Shader "Custom/HeightBasedTextureBlend_Lit"
{
    Properties
    {
        _GrassTex ("Grass Texture", 2D) = "white" {}
        _DirtTex ("Dirt Texture", 2D) = "white" {}
        _HeightThreshold ("Height Threshold", Range(0.0, 1.0)) = 0.5
        _Smoothness ("Smoothness", Range(0.0, 1.0)) = 0.5
        _Metallic ("Metallic", Range(0.0, 1.0)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma surface surf Standard

            sampler2D _GrassTex;
            sampler2D _DirtTex;
            float _HeightThreshold; // Height at which to blend
            float _Smoothness;
            float _Metallic;

            struct Input
            {
                float2 uv_MainTex : TEXCOORD0;
                float3 worldPos : TEXCOORD1; // World position
                float3 worldNormal : NORMAL;
            };

            void surf(Input IN, inout SurfaceOutputStandard o)
            {
                // Blend based on world height
                float height = IN.worldPos.y;
                float blendFactor = smoothstep(_HeightThreshold - 0.05, _HeightThreshold + 0.05, height);

                // Sample the textures
                float4 grassColor = tex2D(_GrassTex, IN.uv_MainTex);
                float4 dirtColor = tex2D(_DirtTex, IN.uv_MainTex);

                // Blend the textures based on height
                o.Albedo = lerp(dirtColor.rgb, grassColor.rgb, blendFactor);
                o.Smoothness = _Smoothness;
                o.Metallic = _Metallic;

                // Set the alpha based on the textures
                o.Alpha = 1.0;
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
