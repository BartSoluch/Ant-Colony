Shader "Custom/TunnelRevealLitURP"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _RevealCenter ("Reveal Center", Vector) = (0, 0, 0, 0)
        _RevealRadius ("Reveal Radius", Float) = 10.0
        _EdgeFade ("Edge Fade", Float) = 2.0
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "UniversalMaterialType"="Lit" }
        LOD 300
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 worldPos    : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
            };

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            float4 _RevealCenter;
            float _RevealRadius;
            float _EdgeFade;
            float _Smoothness;
            float _Metallic;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS);
                OUT.worldPos = TransformObjectToWorld(IN.positionOS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.worldPos);
                float dist = distance(IN.worldPos, _RevealCenter.xyz);
                float alpha = saturate(1.0 - smoothstep(_RevealRadius - _EdgeFade, _RevealRadius, dist));

                // Always show the top (world Y close to 0)
                if (IN.worldPos.y > -0.1)
                    alpha = 1.0;

                float4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                float3 albedo = tex.rgb * alpha;

                InputData lightingInput = (InputData)0;
                lightingInput.positionWS = IN.worldPos;
                lightingInput.normalWS = normalize(IN.normalWS);
                lightingInput.viewDirectionWS = viewDir;
                lightingInput.shadowCoord = float4(0, 0, 0, 0);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.alpha = 1.0;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.normalTS = float3(0, 0, 1);
                surfaceData.occlusion = 1.0;
                surfaceData.emission = 0;

                return UniversalFragmentPBR(lightingInput, surfaceData);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/InternalErrorShader"
}