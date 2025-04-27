Shader "Custom/MarchingCubesPBR_URP"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _SpecMap ("Specular Map", 2D) = "black" {}
        _AOMap ("Ambient Occlusion", 2D) = "white" {}
        _SpecPower ("Specular Power", Range(8, 64)) = 32
        _Tiling ("Texture Tiling", Range(0.001, 3)) = 0.05
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off // Render both front and back faces

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            TEXTURE2D(_SpecMap);
            SAMPLER(sampler_SpecMap);

            TEXTURE2D(_AOMap);
            SAMPLER(sampler_AOMap);

            float _SpecPower;
            float _Tiling;

            struct Vert
            {
                float4 position;
                float3 normal;
            };

            StructuredBuffer<Vert> _Buffer;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Vert v = _Buffer[IN.vertexID];
                Varyings o;
                o.worldPos = TransformObjectToWorld(v.position.xyz);
                o.worldNormal = normalize(TransformObjectToWorldNormal(v.normal));
                o.positionCS = TransformWorldToHClip(o.worldPos);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = frac(i.worldPos.xz * (_Tiling / 10));

                float3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                float3 normalMap = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv));
                float specStrength = SAMPLE_TEXTURE2D(_SpecMap, sampler_SpecMap, uv).r;
                float ao = SAMPLE_TEXTURE2D(_AOMap, sampler_AOMap, uv).r;

                float3 normal = normalize(i.worldNormal + normalMap * 0.5);

                // Get URP main light + shadows
                Light mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);
                float3 lightColor = mainLight.color;
                float shadowAtten = mainLight.shadowAttenuation;

                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);

                float diff = max(dot(normal, -lightDir), 0.0);

                float3 reflectDir = reflect(lightDir, normal);
                float spec = pow(max(dot(viewDir, reflectDir), 0.0), _SpecPower) * specStrength;

                // Combine it all
                float3 litColor = (albedo * diff + spec) * lightColor * shadowAtten;
                litColor *= ao;

                // Optional: tone down intensity slightly
                litColor *= 0.9;

                return float4(litColor, 1.0);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
