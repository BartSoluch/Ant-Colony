Shader "Custom/SimpleTriplanar"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Tiling ("Texture Tiling", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            // Include URP core libraries for lighting functions
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _Tiling;

            struct Attributes
            {
                float3 position   : POSITION;
                float3 normal     : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION; // Must be float4
                float3 worldPos   : TEXCOORD0;
                float3 worldNormal: TEXCOORD1;
                float2 uv         : TEXCOORD2;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                // Convert object space to world space.
                float4 worldPos = TransformObjectToWorld(float4(IN.position, 1.0));
                OUT.worldPos = worldPos.xyz;
                // Transform object normal to world space.
                OUT.worldNormal = normalize(TransformObjectToWorldNormal(IN.normal));
                // Convert world position to clip space; this function should return float4.
                OUT.positionCS = TransformWorldToHClip(worldPos.xyz);
    
                // Generate simple planar UVs on XZ plane (adjust as needed)
                OUT.uv = frac(OUT.worldPos.xz * _Tiling);
    
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Sample the albedo texture using the computed UVs.
                float3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).rgb;
                
                // Get main light info from URP (you can keep this basic or simplify further)
                Light mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);
                
                // Compute diffuse lighting using the surface normal.
                float diff = max(dot(IN.worldNormal, -lightDir), 0.0);
                
                // Combine albedo with diffuse term and apply main light color.
                float3 color = albedo * diff * mainLight.color * mainLight.shadowAttenuation;
                
                // Optional: Apply a simple ambient term (modify intensity as needed)
                color += albedo * 0.2;
                
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Diffuse"
}