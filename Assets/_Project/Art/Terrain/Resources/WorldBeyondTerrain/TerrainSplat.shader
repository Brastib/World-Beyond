// Gelände mit 10 Bodenschichten (Texture2DArray), gemischt über drei Steuerkarten (je Kachel ein Texel, bilinear =
// weiche Übergänge zwischen Kachelmitten). Aus der Ferne blendet die Farbkarte (WorldColorMap) über, damit die Karte
// lesbar bleibt (UI_STYLE_GUIDE §7). Beleuchtung über URP (UniversalFragmentPBR), Schatten/Tiefe aus URP Lit.
// Gebaut ohne Unity-Editor (Cloud): beim ersten Unity-Lauf auf Kompilierfehler prüfen. Fehlt der Shader, nutzt
// WorldRenderer das bisherige Material.
Shader "WorldBeyond/TerrainSplat"
{
    Properties
    {
        _ColorMap ("Farbkarte", 2D) = "white" {}
        _Splat0 ("Steuerkarte 0", 2D) = "black" {}
        _Splat1 ("Steuerkarte 1", 2D) = "black" {}
        _Splat2 ("Steuerkarte 2", 2D) = "black" {}
        _Albedo ("Schichten Farbe", 2DArray) = "" {}
        _Normal ("Schichten Normal", 2DArray) = "" {}
        _Macro ("Makro-Rauschen", 2D) = "gray" {}
        _MacroRepeat ("Makro-Wiederholung (m)", Float) = 64
        _NormalStrength ("Normalstärke", Range(0, 2)) = 1
        _Smoothness ("Glätte", Range(0, 1)) = 0.1
        _FarStart ("Farbkarte ab (m)", Float) = 350
        _FarEnd ("nur Farbkarte ab (m)", Float) = 1100
        _WorldSizeXZ ("Weltgröße m (x, z)", Vector) = (1024, 1024, 0, 0)
        _HasNormal ("Normalen vorhanden", Float) = 1
        // Für die übernommenen Lit-Pässe (Schatten, Tiefe)
        [HideInInspector] _BaseMap ("", 2D) = "white" {}
        [HideInInspector] _BaseColor ("", Color) = (1, 1, 1, 1)
        [HideInInspector] _Cutoff ("", Float) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #define LAYERS 10

            TEXTURE2D(_ColorMap); SAMPLER(sampler_ColorMap);
            TEXTURE2D(_Splat0); TEXTURE2D(_Splat1); TEXTURE2D(_Splat2);
            TEXTURE2D_ARRAY(_Albedo); SAMPLER(sampler_Albedo);
            TEXTURE2D_ARRAY(_Normal); SAMPLER(sampler_Normal);
            TEXTURE2D(_Macro); SAMPLER(sampler_Macro);

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorMap_ST;
                float4 _WorldSizeXZ;
                float _MacroRepeat;
                float _NormalStrength;
                float _Smoothness;
                float _FarStart;
                float _FarEnd;
                float _HasNormal;
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff;
                float _Repeat[LAYERS];
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogCoord : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fogCoord = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 ctrlUV = i.positionWS.xz / _WorldSizeXZ.xy;
                // Mit Makro-Rauschen leicht verschoben, damit Schichtgrenzen nicht dem Kachelraster folgen.
                half3 macro = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, i.positionWS.xz / _MacroRepeat).rgb;
                float2 jitter = (macro.gb - 0.5) * (1.6 / _WorldSizeXZ.xy) * 4.0;
                half4 s0 = SAMPLE_TEXTURE2D(_Splat0, sampler_ColorMap, ctrlUV + jitter);
                half4 s1 = SAMPLE_TEXTURE2D(_Splat1, sampler_ColorMap, ctrlUV + jitter);
                half4 s2 = SAMPLE_TEXTURE2D(_Splat2, sampler_ColorMap, ctrlUV + jitter);
                half w[LAYERS] = { s0.r, s0.g, s0.b, s0.a, s1.r, s1.g, s1.b, s1.a, s2.r, s2.g };

                half3 albedo = 0;
                half3 nTS = 0;
                half total = 0;
                [unroll]
                for (int l = 0; l < LAYERS; l++)
                {
                    half wl = w[l];
                    [branch]
                    if (wl > 0.004)
                    {
                        float2 uv = i.positionWS.xz / _Repeat[l];
                        albedo += SAMPLE_TEXTURE2D_ARRAY(_Albedo, sampler_Albedo, uv, l).rgb * wl;
                        if (_HasNormal > 0.5)
                            nTS += UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(_Normal, sampler_Normal, uv, l)) * wl;
                        total += wl;
                    }
                }
                albedo = total > 0.001 ? albedo / total : half3(0.5, 0.5, 0.5);
                albedo *= lerp(0.8, 1.15, macro.r);

                // Aus der Ferne Farbkarte (Weltbox-Lesbarkeit), nah die Texturen.
                half3 mapColor = SAMPLE_TEXTURE2D(_ColorMap, sampler_ColorMap, i.uv).rgb;
                float dist = distance(GetCameraPositionWS(), i.positionWS);
                half far = saturate((dist - _FarStart) / max(1.0, _FarEnd - _FarStart));
                albedo = lerp(albedo, mapColor, far);

                float3 n = normalize(i.normalWS);
                if (_HasNormal > 0.5 && total > 0.001)
                {
                    nTS = nTS / total;
                    nTS.xy *= _NormalStrength * (1.0 - far);
                    float3 t = normalize(float3(1, 0, 0) - n * n.x);   // Tangente entlang +X, Bitangente entlang +Z
                    float3 b = cross(n, t);
                    n = normalize(t * nTS.x + b * nTS.y + n * max(nTS.z, 0.001));
                }

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogCoord;
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = 0;
                surface.specular = 0;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, i.fogCoord);
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack Off
}
