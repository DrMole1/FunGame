// Effets de vieux film plein écran (GDD 12 Post-Process, V0.6.13), appliqués par PostFx (OnRenderImage) :
// FX1 vignette irrégulière, FX4 image qui tremble (pendant les pulsations cardiaques), FX5 couleurs qui se séparent sur les bords (près d'un Cauchemar). Chaque effet a son poids (0 à 1),
// et ils se superposent.
Shader "FunGame/OldFilm"
{
    Properties
    {
        _MainTex ("Image", 2D) = "white" {}
        _Vignette ("FX1 Vignette (au-delà de 1 : accentuée)", Range(0, 2)) = 1
        _Shake ("FX4 Tremblement (pixels)", Float) = 0
        _Split ("FX5 Séparation des couleurs", Range(0, 1)) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Vignette, _Shake, _Split;

            float Hash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }
            float Hash2(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash2(i), Hash2(i + float2(1, 0)), f.x), lerp(Hash2(i + float2(0, 1)), Hash2(i + float2(1, 1)), f.x), f.y);
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                // Image de la pellicule : 24 par seconde.
                float frame = floor(_Time.y * 24.0);
                float2 uv = i.uv;

                // FX4 : la pellicule mal tenue décale l'image d'un pixel ou deux.
                if (_Shake > 0.0)
                {
                    float2 jump = float2(Hash(frame * 1.7) - 0.5, Hash(frame * 2.3 + 4.0) - 0.5) * 2.0 * _Shake;
                    uv += round(jump) * _MainTex_TexelSize.xy;
                }

                // FX5 : rouge et bleu s'écartent du vert, de plus en plus vers les bords.
                fixed3 col;
                if (_Split > 0.0)
                {
                    float2 fromCenter = uv - 0.5;
                    float edge = smoothstep(0.15, 0.75, length(fromCenter) * 1.4);
                    float2 offset = fromCenter * edge * _Split * 0.018;
                    col.r = tex2D(_MainTex, uv + offset).r;
                    col.g = tex2D(_MainTex, uv).g;
                    col.b = tex2D(_MainTex, uv - offset).b;
                }
                else col = tex2D(_MainTex, uv).rgb;

                // FX1 : bords assombris, de façon inégale, comme un vieil objectif. Au-delà de 1 (menu du lobby), le sombre
                // gagne un peu vers le centre et devient plus profond sur les bords.
                if (_Vignette > 0.0)
                {
                    float2 d = (i.uv - 0.5) * float2(1.0, 0.8);
                    float uneven = Noise(i.uv * 3.0 + 5.0) * 0.18 + Noise(i.uv * 7.0 + 17.0) * 0.07;
                    float extra = max(0.0, _Vignette - 1.0);
                    float v = smoothstep(0.32 - extra * 0.25 + uneven, 0.82 - extra * 0.2 + uneven, length(d) * 1.35);
                    col *= 1.0 - v * min(0.85 * _Vignette, 0.97);
                }
                return fixed4(saturate(col), 1.0);
            }
            ENDCG
        }
    }
}
