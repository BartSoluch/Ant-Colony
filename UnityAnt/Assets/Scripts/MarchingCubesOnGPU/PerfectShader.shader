Shader "CustomRenderTexture/PerfectShader"
{
    Properties
    {
        // Grass
        _GrassTex ("Grass Albedo", 2D) = "white" {}
        _GrassNormal ("Grass Normal Map", 2D) = "bump" {}
        _GrassSpecular ("Grass Specular Map", 2D) = "white" {}
        _GrassAO ("Grass AO Map", 2D) = "white" {}

        // Dry Dirt
        _DryDirtTex ("Dry Dirt Albedo", 2D) = "white" {}
        _DryDirtNormal ("Dry Dirt Normal Map", 2D) = "bump" {}
        _DryDirtSpecular ("Dry Dirt Specular Map", 2D) = "white" {}
        _DryDirtAO ("Dry Dirt AO Map", 2D) = "white" {}

        // Wet Dirt
        _WetDirtTex ("Wet Dirt Albedo", 2D) = "white" {}
        _WetDirtNormal ("Wet Dirt Normal Map", 2D) = "bump" {}
        _WetDirtSpecular ("Wet Dirt Specular Map", 2D) = "white" {}
        _WetDirtAO ("Wet Dirt AO Map", 2D) = "white" {}

        // Controls
        _Tiling ("Texture Tiling", Float) = 3.0
        _MinHeight ("Min Height", Float) = 0.0
        _MaxHeight ("Max Height", Float) = 20.0
        _BlendSharpness ("Blend Sharpness", Float) = 4.0
        _NormalBlendStrength ("Normal Blend Strength", Range(0, 1)) = 0.8
        _SpecularStrength ("Specular Strength", Range(0, 2)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off

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

            // Textures
            // Grass
            TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
            TEXTURE2D(_GrassNormal); SAMPLER(sampler_GrassNormal);
            TEXTURE2D(_GrassSpecular); SAMPLER(sampler_GrassSpecular);
            TEXTURE2D(_GrassAO); SAMPLER(sampler_GrassAO);

            // Dry Dirt
            TEXTURE2D(_DryDirtTex); SAMPLER(sampler_DryDirtTex);
            TEXTURE2D(_DryDirtNormal); SAMPLER(sampler_DryDirtNormal);
            TEXTURE2D(_DryDirtSpecular); SAMPLER(sampler_DryDirtSpecular);
            TEXTURE2D(_DryDirtAO); SAMPLER(sampler_DryDirtAO);

            // Wet Dirt
            TEXTURE2D(_WetDirtTex); SAMPLER(sampler_WetDirtTex);
            TEXTURE2D(_WetDirtNormal); SAMPLER(sampler_WetDirtNormal);
            TEXTURE2D(_WetDirtSpecular); SAMPLER(sampler_WetDirtSpecular);
            TEXTURE2D(_WetDirtAO); SAMPLER(sampler_WetDirtAO);

            // Voxel normals from compute shader
            TEXTURE3D(_NormalVolume); SAMPLER(sampler_NormalVolume);

            float3 _VolumeWorldSize;

            // Material controls
            float _Tiling;
            float _MinHeight;
            float _MaxHeight;
            float _BlendSharpness;
            float _NormalBlendStrength;
            float _SpecularStrength;

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

            struct MaterialSample
            {
                float3 albedo;
                float3 normal;
                float specular;
                float ao;
            };

            // Triplanar sampling function
            MaterialSample sampleTriplanar(float3 position, float3 normal,
                TEXTURE2D_PARAM(albedoTex, albedoSamp),
                TEXTURE2D_PARAM(normalTex, normalSamp),
                TEXTURE2D_PARAM(specularTex, specularSamp),
                TEXTURE2D_PARAM(aoTex, aoSamp))
            {
                // Blend weights for axis projections
                float3 blend = pow(abs(normal), 4.0);
                blend /= (blend.x + blend.y + blend.z + 0.0001);

                // Scale position
                float3 scale = float3(_Tiling / _VolumeWorldSize.x,
                                      _Tiling / _VolumeWorldSize.y,
                                      _Tiling / _VolumeWorldSize.z);

                float3 scaledPosition = position * scale;

                // Proper projections
                float2 xProj = scaledPosition.zy; // X projection (YZ plane)
                float2 yProj = scaledPosition.xz; // Y projection (XZ plane)
                float2 zProj = scaledPosition.xy; // Z projection (XY plane)

                // Sample textures and blend
                float3 albedo =
                    SAMPLE_TEXTURE2D(albedoTex, albedoSamp, xProj).rgb * blend.x +
                    SAMPLE_TEXTURE2D(albedoTex, albedoSamp, yProj).rgb * blend.y +
                    SAMPLE_TEXTURE2D(albedoTex, albedoSamp, zProj).rgb * blend.z;

                float3 normalSample =
                    SAMPLE_TEXTURE2D(normalTex, normalSamp, xProj).rgb * blend.x +
                    SAMPLE_TEXTURE2D(normalTex, normalSamp, yProj).rgb * blend.y +
                    SAMPLE_TEXTURE2D(normalTex, normalSamp, zProj).rgb * blend.z;

                float specular =
                    SAMPLE_TEXTURE2D(specularTex, specularSamp, xProj).r * blend.x +
                    SAMPLE_TEXTURE2D(specularTex, specularSamp, yProj).r * blend.y +
                    SAMPLE_TEXTURE2D(specularTex, specularSamp, zProj).r * blend.z;

                float ao =
                    SAMPLE_TEXTURE2D(aoTex, aoSamp, xProj).r * blend.x +
                    SAMPLE_TEXTURE2D(aoTex, aoSamp, yProj).r * blend.y +
                    SAMPLE_TEXTURE2D(aoTex, aoSamp, zProj).r * blend.z;

                // Unpack normal map
                float3 unpackedNormal = normalSample * 2.0 - 1.0;

                MaterialSample sample;
                sample.albedo = albedo;
                sample.normal = unpackedNormal;
                sample.specular = specular;
                sample.ao = ao;

                return sample;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Voxel normal
                float3 voxelNormal = SAMPLE_TEXTURE3D(_NormalVolume, sampler_NormalVolume, i.worldPos / _VolumeWorldSize);
                voxelNormal = normalize(voxelNormal);

                // Sample materials
                MaterialSample grass = sampleTriplanar(i.worldPos, voxelNormal, _GrassTex, sampler_GrassTex, _GrassNormal, sampler_GrassNormal, _GrassSpecular, sampler_GrassSpecular, _GrassAO, sampler_GrassAO);
                MaterialSample dryDirt = sampleTriplanar(i.worldPos, voxelNormal, _DryDirtTex, sampler_DryDirtTex, _DryDirtNormal, sampler_DryDirtNormal, _DryDirtSpecular, sampler_DryDirtSpecular, _DryDirtAO, sampler_DryDirtAO);
                MaterialSample wetDirt = sampleTriplanar(i.worldPos, voxelNormal, _WetDirtTex, sampler_WetDirtTex, _WetDirtNormal, sampler_WetDirtNormal, _WetDirtSpecular, sampler_WetDirtSpecular, _WetDirtAO, sampler_WetDirtAO);

                // Height-based blending
                float height = saturate((i.worldPos.y - _MinHeight) / (_MaxHeight - _MinHeight));
                float grassBlend = smoothstep(0.5, 1.0, height);
                float dryBlend = smoothstep(0.2, 0.8, height);
                float wetBlend = 1.0 - dryBlend;

                // Blend materials
                MaterialSample blended;
                blended.albedo = lerp(lerp(wetDirt.albedo, dryDirt.albedo, dryBlend), grass.albedo, grassBlend);
                blended.normal = normalize(lerp(lerp(wetDirt.normal, dryDirt.normal, dryBlend), grass.normal, grassBlend));
                blended.specular = lerp(lerp(wetDirt.specular, dryDirt.specular, dryBlend), grass.specular, grassBlend);
                blended.ao = lerp(lerp(wetDirt.ao, dryDirt.ao, dryBlend), grass.ao, grassBlend);

                // Transform normal map normal to world space
                float3 textureNormal = normalize(
                    blended.normal.x * float3(1, 0, 0) +
                    blended.normal.y * float3(0, 1, 0) +
                    blended.normal.z * float3(0, 0, 1)
                );

                // Final normal: blend voxel normal and texture normal
                float3 finalNormal = normalize(lerp(voxelNormal, textureNormal, _NormalBlendStrength));

                // Lighting
                Light mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);

                float diff = max(dot(finalNormal, -lightDir), 0.0);
                float3 reflectDir = reflect(lightDir, finalNormal);
                float spec = pow(max(dot(viewDir, reflectDir), 0.0), 32.0) * blended.specular * _SpecularStrength;

                float3 litColor = (blended.albedo * diff + spec) * mainLight.color * mainLight.shadowAttenuation;
                litColor *= blended.ao;
                //litColor *= 0.9; // Optional final tweak

                return float4(litColor, 1.0);
                //return float4(voxelNormal * 0.5 + 0.5, 1.0);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
