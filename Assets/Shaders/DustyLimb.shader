// Membres du décor (bras, mains, têtes, bustes) : plâtre gris mat, couvert de poussière.
// Tout est procédural en coordonnées monde (les modèles Meshy n'ont pas de texture) : marbrures du plâtre,
// crasse plus sombre dans les creux, poussière claire accumulée sur les faces tournées vers le haut, grain fin.
Shader "FunGame/DustyLimb"
{
    Properties
    {
        _BaseColor ("Plâtre", Color) = (0.36, 0.35, 0.34, 1)
        _GrimeColor ("Crasse", Color) = (0.13, 0.125, 0.12, 1)
        _DustColor ("Poussière", Color) = (0.62, 0.6, 0.56, 1)
        _DustAmount ("Quantité de poussière", Range(0, 1)) = 0.65
        _Scale ("Échelle du motif", Float) = 3.5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma multi_compile_instancing
        #pragma target 3.0

        fixed4 _BaseColor, _GrimeColor, _DustColor;
        float _DustAmount, _Scale;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        float Hash13(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float ValueNoise(float3 p)
        {
            float3 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = lerp(Hash13(i), Hash13(i + float3(1, 0, 0)), f.x);
            float b = lerp(Hash13(i + float3(0, 1, 0)), Hash13(i + float3(1, 1, 0)), f.x);
            float c = lerp(Hash13(i + float3(0, 0, 1)), Hash13(i + float3(1, 0, 1)), f.x);
            float d = lerp(Hash13(i + float3(0, 1, 1)), Hash13(i + float3(1, 1, 1)), f.x);
            return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
        }

        float Fbm(float3 p)
        {
            float n = 0.0, amp = 0.5;
            for (int k = 0; k < 4; k++)
            {
                n += amp * ValueNoise(p);
                p *= 2.03;
                amp *= 0.5;
            }
            return n;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 p = IN.worldPos * _Scale;
            float3 n = normalize(IN.worldNormal);

            // Plâtre légèrement marbré, assombri par endroits (crasse).
            float marble = Fbm(p);
            float grime = smoothstep(0.38, 0.62, Fbm(p * 0.5 + 17.0));
            float3 col = _BaseColor.rgb * (0.7 + 0.6 * marble);
            col = lerp(col, _GrimeColor.rgb, grime * 0.85);
            // Coulures sombres verticales.
            float streaks = smoothstep(0.55, 0.8, ValueNoise(float3(p.x * 3.0, p.y * 0.25, p.z * 3.0)));
            col *= 1.0 - streaks * 0.45;

            // Poussière : dépôt sur les faces orientées vers le haut, avec des bords irréguliers.
            float up = saturate(n.y * 0.5 + 0.5);
            float dustNoise = Fbm(p * 2.2 + 5.0);
            float dust = smoothstep(0.5, 0.8, up * (0.6 + _DustAmount) + (dustNoise - 0.5) * 0.9 - (1.0 - _DustAmount) * 0.4);
            // Voile de poussière par plaques, même sur les faces verticales.
            dust = max(dust, _DustAmount * 0.5 * smoothstep(0.5, 0.7, dustNoise));
            col = lerp(col, _DustColor.rgb * (0.9 + 0.2 * dustNoise), dust);

            // Grain fin (particules de poussière).
            float speck = ValueNoise(IN.worldPos * 140.0);
            col *= 0.8 + 0.4 * speck;

            o.Albedo = col;
            o.Metallic = 0;
            // Plâtre presque mat ; la poussière l'est complètement.
            o.Smoothness = lerp(0.12, 0.02, dust);
            o.Occlusion = lerp(0.75, 1.0, marble);
        }
        ENDCG
    }
    FallBack "Diffuse"
}
