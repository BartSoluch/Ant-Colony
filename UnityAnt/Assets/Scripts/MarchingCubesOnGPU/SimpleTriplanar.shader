Shader "Custom/SimpleTriplanar"
{
    Properties
    {
        // Albedo textures
        _GrassTex ("Grass Albedo", 2D) = "white" {}
        _DryDirtTex ("Dry Dirt Albedo", 2D) = "white" {}
        _WetDirtTex ("Wet Dirt Albedo", 2D) = "white" {}

        // Controls
        _TileDensity ("Tile Density", Range(0.1, 100)) = 3.0
        _MinHeight ("Min Height", Float) = 0.0
        _MaxHeight ("Max Height", Float) = 20.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Albedo textures
            TEXTURE2D(_GrassTex);   SAMPLER(sampler_GrassTex);
            TEXTURE2D(_DryDirtTex); SAMPLER(sampler_DryDirtTex);
            TEXTURE2D(_WetDirtTex); SAMPLER(sampler_WetDirtTex);

            // Voxel normals from compute shader for lighting
            TEXTURE3D(_NormalVolume); SAMPLER(sampler_NormalVolume);

            float _TileDensity;
            float _MinHeight;
            float _MaxHeight;

            struct Vert
            {
                float4 positionOS; // object-space position
                float3 normalOS;   // object-space normal
            };

            // The structured buffer containing the mesh data
            StructuredBuffer<Vert> _Buffer;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS    : SV_POSITION;
                float3 worldPos      : TEXCOORD0;
                float3 geometryNormal: TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Vert v = _Buffer[IN.vertexID];

                Varyings OUT;

                // Transform to world space
                OUT.worldPos = TransformObjectToWorld(v.positionOS.xyz);

                // Geometry normal in world space
                OUT.geometryNormal = normalize(TransformObjectToWorldNormal(v.normalOS));

                // Standard clip-space position
                OUT.positionCS = TransformWorldToHClip(OUT.worldPos);

                return OUT;
            }

            //------------------------------------------------------------------
            // Triplanar sampling for albedo: uses geometryNormal to decide 
            // how to project the texture, so steep walls aren't "top-down."
            //------------------------------------------------------------------
            float3 SampleTriplanarAlbedo(float3 positionWS, float3 geometryNormalWS,
                                         TEXTURE2D_PARAM(tex, texSampler))
            {
                // Blend weights based on how much the surface normal points
                // along each axis, raised to a power for sharper transitions.
                float3 blend = pow(abs(geometryNormalWS), 4.0);
                blend /= (blend.x + blend.y + blend.z + 1e-4);

                // Uniform scale based on _TileDensity
                float3 scaledPos = positionWS * _TileDensity;

                // Project each axis
                float2 projX = scaledPos.zy; // Surfaces facing ±X use YZ
                float2 projY = scaledPos.xz; // Surfaces facing ±Y use XZ
                float2 projZ = scaledPos.xy; // Surfaces facing ±Z use XY

                // Sample each projection
                float3 colX = SAMPLE_TEXTURE2D(tex, texSampler, projX).rgb;
                float3 colY = SAMPLE_TEXTURE2D(tex, texSampler, projY).rgb;
                float3 colZ = SAMPLE_TEXTURE2D(tex, texSampler, projZ).rgb;

                // Combine using blend weights
                return colX * blend.x + colY * blend.y + colZ * blend.z;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                //------------------------------------------------------------------
                // 1) Texturing (Triplanar)
                //    Use the *geometry* normal to figure out how to project textures.
                //------------------------------------------------------------------
                float3 grassAlbedo = SampleTriplanarAlbedo(IN.worldPos, IN.geometryNormal, _GrassTex, sampler_GrassTex);
                float3 dryAlbedo   = SampleTriplanarAlbedo(IN.worldPos, IN.geometryNormal, _DryDirtTex, sampler_DryDirtTex);
                float3 wetAlbedo   = SampleTriplanarAlbedo(IN.worldPos, IN.geometryNormal, _WetDirtTex, sampler_WetDirtTex);

                // Height-based blend factor for wet/dry/grass
                float height = saturate((IN.worldPos.y - _MinHeight) / (_MaxHeight - _MinHeight));
                float blendDry   = smoothstep(0.2, 0.8, height);
                float blendGrass = smoothstep(0.5, 1.0, height);

                // Final albedo from the three maps
                float3 blendedAlbedo = lerp(
                                            lerp(wetAlbedo, dryAlbedo, blendDry),
                                            grassAlbedo,
                                            blendGrass
                                         );

                //------------------------------------------------------------------
                // 2) Lighting 
                //    Use the *voxel normal* from the volume texture.
                //------------------------------------------------------------------
                float3 voxelNormal = SAMPLE_TEXTURE3D(_NormalVolume, sampler_NormalVolume, IN.worldPos).rgb;
                voxelNormal = normalize(voxelNormal);

                // Simple Lambert, but clamp at 0.5 to avoid dark shadows
                Light mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);

                float NdotL = dot(voxelNormal, -lightDir);
                float diff = max(NdotL, 0.5); // clamp darks at 0.5

                float3 litColor = blendedAlbedo * diff * mainLight.color * mainLight.shadowAttenuation;

                return float4(litColor, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
