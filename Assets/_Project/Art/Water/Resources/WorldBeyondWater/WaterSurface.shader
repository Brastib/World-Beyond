// Wasseroberfläche (eigener URP-Shader): Tiefenfarbe, prozedurale Wellen-Normalen, Fresnel/Himmelsreflexion,
// Sonnenglanz, weicher Uferschaum und Strömung in Flüssen. Daten pro Vertex aus ChunkMeshBuilder.BuildWater:
// uv0 = Farbkarte (Fernansicht), uv1 = (Tiefe m, 0, Strömung x, Strömung z). Keine Texturen nötig.
// Gebaut ohne Unity-Editor (Cloud): beim ersten Unity-Lauf auf Kompilierfehler prüfen. Fehlt der Shader, nutzt
// WorldRenderer das bisherige Lit-Material.
Shader "WorldBeyond/WaterSurface"
{
    Properties
    {
        _ColorMap ("Farbkarte (Ferne)", 2D) = "white" {}
        _ShallowColor ("Flachwasser", Color) = (0.27, 0.62, 0.62, 1)
        _DeepColor ("Tiefwasser", Color) = (0.05, 0.2, 0.32, 1)
        _DepthScale ("Tiefe bis voll dunkel (m)", Float) = 6
        _ShoreAlpha ("Deckkraft am Ufer", Range(0, 1)) = 0.35
        _DeepAlpha ("Deckkraft tief", Range(0, 1)) = 0.93
        _WaveScale ("Wellenlänge (m)", Float) = 7
        _WaveStrength ("Wellenstärke", Range(0, 1)) = 0.35
        _WaveSpeed ("Wellengeschwindigkeit", Float) = 0.6
        _FlowSpeed ("Strömungsfaktor", Float) = 1
        _SkyColor ("Himmel (Reflexion)", Color) = (0.62, 0.76, 0.88, 1)
        _FresnelPower ("Fresnel-Exponent", Range(1, 8)) = 4
        _Gloss ("Sonnenglanz-Schärfe", Range(8, 512)) = 160
        _SpecStrength ("Sonnenglanz-Stärke", Range(0, 2)) = 0.7
        _FoamColor ("Schaum", Color) = (0.92, 0.95, 0.95, 1)
        _FoamDepth ("Schaumbreite (m Tiefe)", Float) = 0.5
        _FoamStrength ("Schaumstärke", Range(0, 1)) = 0.7
        _FarStart ("Farbkarte ab (m)", Float) = 350
        _FarEnd ("nur Farbkarte ab (m)", Float) = 1100
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-100" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_ColorMap); SAMPLER(sampler_ColorMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorMap_ST;
                half4 _ShallowColor, _DeepColor, _SkyColor, _FoamColor;
                float _DepthScale, _ShoreAlpha, _DeepAlpha;
                float _WaveScale, _WaveStrength, _WaveSpeed, _FlowSpeed;
                float _FresnelPower, _Gloss, _SpecStrength;
                float _FoamDepth, _FoamStrength, _FarStart, _FarEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 water : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float4 water : TEXCOORD2;
                float fog : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.uv = v.uv;
                o.water = v.water;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // Summe gerichteter Sinuswellen: liefert die Steigung (dh/dx, dh/dz) der Oberfläche.
            float2 WaveSlope(float2 p, float t)
            {
                const float2 d0 = float2(0.8, 0.6), d1 = float2(-0.45, 0.89), d2 = float2(0.97, -0.24), d3 = float2(-0.7, -0.71);
                float2 s = 0;
                float k;
                k = 1.0; s += d0 * k * cos(dot(d0, p) * k + t * 1.0) * 0.50;
                k = 1.7; s += d1 * k * cos(dot(d1, p) * k + t * 1.3) * 0.30;
                k = 2.9; s += d2 * k * cos(dot(d2, p) * k + t * 1.9) * 0.16;
                k = 4.7; s += d3 * k * cos(dot(d3, p) * k + t * 2.6) * 0.09;
                return s;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float depth = max(i.water.x, 0.0);
                float2 flow = i.water.zw * _FlowSpeed;
                float t = _Time.y * _WaveSpeed;

                // Strömung: Wellenmuster mit der Fließrichtung verschieben (zwei Phasen überblendet, kein Springen).
                float2 p = i.positionWS.xz / max(_WaveScale, 0.01);
                float ph0 = frac(_Time.y * 0.25), ph1 = frac(_Time.y * 0.25 + 0.5);
                float2 fo = flow / max(_WaveScale, 0.01) * 4.0;
                float wBlend = abs(ph0 - 0.5) * 2.0;
                float2 slope = lerp(WaveSlope(p - fo * ph0, t), WaveSlope(p - fo * ph1 + 3.7, t), wBlend);
                // Feine zweite Lage für Glitzern
                slope += WaveSlope(p * 3.1 + 11.0 - fo * 2.0 * ph0, t * 1.7) * 0.25;
                float3 n = normalize(float3(-slope.x * _WaveStrength, 1.0, -slope.y * _WaveStrength));

                float3 viewDir = SafeNormalize(GetWorldSpaceViewDir(i.positionWS));
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);
                float shadow = lerp(0.55, 1.0, light.shadowAttenuation);

                // Tiefenfarbe
                float dk = saturate(depth / max(_DepthScale, 0.01));
                half3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, sqrt(dk));
                float ndl = saturate(dot(n, light.direction)) * 0.5 + 0.5;
                half3 col = water * (light.color * ndl * shadow + SampleSH(n) * 0.5);

                // Fresnel + Himmelsreflexion
                float fres = pow(1.0 - saturate(dot(n, viewDir)), _FresnelPower);
                col = lerp(col, _SkyColor.rgb * (0.6 + 0.4 * shadow), saturate(fres * 0.85));

                // Sonnenglanz
                float3 h = SafeNormalize(light.direction + viewDir);
                float spec = pow(saturate(dot(n, h)), _Gloss) * _SpecStrength * light.shadowAttenuation;
                col += light.color * spec;

                // Uferschaum: schmaler, gebrochener Saum dort, wo das Wasser flach wird; Strömung zieht ihn mit.
                float shore = 1.0 - saturate(depth / max(_FoamDepth, 0.01));
                float2 fp = i.positionWS.xz * 0.9 - flow * _Time.y * 0.6;
                float noise = ValueNoise(fp) * 0.6 + ValueNoise(fp * 2.3 + _Time.y * 0.3) * 0.4;
                float band = 0.5 + 0.5 * sin(depth / max(_FoamDepth, 0.01) * 9.0 - _Time.y * 1.5);
                float foam = saturate(shore * shore * (noise * 1.4 - 0.25 + band * 0.35)) * _FoamStrength;
                // leichter Schaum bei schneller Strömung
                foam += saturate(length(i.water.zw) - 0.9) * saturate(noise - 0.55) * 0.8 * _FoamStrength;
                col = lerp(col, _FoamColor.rgb * (0.7 + 0.3 * shadow), saturate(foam));

                float alpha = lerp(_ShoreAlpha, _DeepAlpha, saturate(depth / 1.5));
                alpha = saturate(max(alpha, foam) + fres * 0.3);
                // ganz am Uferrand weich auslaufen
                alpha *= saturate(depth / 0.12);

                // Fernansicht: Farbkarte überblenden, damit die Karte lesbar bleibt
                float dist = distance(GetCameraPositionWS(), i.positionWS);
                float far = saturate((dist - _FarStart) / max(_FarEnd - _FarStart, 1.0));
                half3 mapCol = SAMPLE_TEXTURE2D(_ColorMap, sampler_ColorMap, i.uv).rgb * (light.color * 0.5 + 0.5);
                col = lerp(col, mapCol, far * 0.85);
                alpha = lerp(alpha, max(alpha, 0.9), far);

                col = MixFog(col, i.fog);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
