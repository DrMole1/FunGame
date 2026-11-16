// GDD 12 Post-Process (V0.7.8) : effets de rendu plein écran appliqués par PostFxCamera (OnRenderImage), avant les effets
// de vieux film (la vignette et le reste se posent par-dessus). Désactivés par défaut ; "/fx10" et "/fx11" au Terminal.
// Passe 0 : FX10 Cell Shading : la lumière est découpée en aplats (quelques paliers de luminosité, teinte conservée) et
//           les contours (fortes variations de luminosité) sont tracés en noir.
// Passe 1 : FX11 Soft Shading : les ombres et la lumière se fondent (flou doux mêlé à l'image), les transitions sont
//           adoucies et les zones claires diffusent un léger halo.
// Tout est calculé en espace gamma (le projet est en espace linéaire), pour que les paliers suivent l'œil.
Shader "FunGame/Shading"
{
    Properties
    {
        _MainTex ("Image", 2D) = "white" {}
        _Bands ("FX10 Paliers de lumière", Float) = 5
        _Edge ("FX10 Force des contours", Range(0, 1)) = 0.6
        _Strength ("FX10 Intensité (mélange avec l'image)", Range(0, 1)) = 0.65
        _Soft ("FX11 Douceur", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float _Bands, _Edge, _Soft, _Strength;

        float3 ToGamma(float3 c) { return pow(max(c, 0.0), 1.0 / 2.2); }
        float3 ToLinear(float3 c) { return pow(max(c, 0.0), 2.2); }
        float3 Tap(float2 uv) { return ToGamma(tex2D(_MainTex, uv).rgb); }
        float Luma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
        ENDCG

        // ----- FX10 : Cell Shading -----
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 t = _MainTex_TexelSize.xy;
                // Débruitage léger (le grain de la pellicule ferait scintiller les paliers) : moyenne en croix.
                float3 col = (Tap(i.uv) * 2.0 + Tap(i.uv + float2(t.x, 0)) + Tap(i.uv - float2(t.x, 0))
                            + Tap(i.uv + float2(0, t.y)) + Tap(i.uv - float2(0, t.y))) / 6.0;
                float l = Luma(col);

                // Paliers comptés sur la racine de la luminosité : l'ambiance très sombre garde plusieurs aplats au lieu d'être
                // écrasée dans le premier. Arrondi au palier le plus proche (bords légèrement lissés) ; la teinte reste.
                float scaled = sqrt(l) * _Bands;
                float band = (floor(scaled) + smoothstep(0.35, 0.65, frac(scaled))) / _Bands;
                band *= band;
                float3 cel = col * (band / max(l, 1e-4));

                // Contours : Sobel sur la racine de la luminosité, échantillons à 2 pixels (moins sensibles au grain).
                float2 o = t * 2.0;
                float tl = sqrt(Luma(Tap(i.uv + float2(-o.x,  o.y)))), tc = sqrt(Luma(Tap(i.uv + float2(0,  o.y)))), tr = sqrt(Luma(Tap(i.uv + float2(o.x,  o.y))));
                float ml = sqrt(Luma(Tap(i.uv + float2(-o.x, 0)))),                                                     mr = sqrt(Luma(Tap(i.uv + float2(o.x, 0))));
                float bl = sqrt(Luma(Tap(i.uv + float2(-o.x, -o.y)))), bc = sqrt(Luma(Tap(i.uv + float2(0, -o.y)))), br = sqrt(Luma(Tap(i.uv + float2(o.x, -o.y))));
                float gx = (tr + 2.0 * mr + br) - (tl + 2.0 * ml + bl);
                float gy = (tl + 2.0 * tc + tr) - (bl + 2.0 * bc + br);
                float edge = smoothstep(0.22, 0.42, sqrt(gx * gx + gy * gy));
                cel *= 1.0 - edge * _Edge;
                // Effet modéré : les aplats et les contours se mêlent à l'image d'origine.
                cel = lerp(Tap(i.uv), cel, _Strength);
                return fixed4(ToLinear(saturate(cel)), 1.0);
            }
            ENDCG
        }

        // ----- FX11 : Soft Shading -----
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 t = _MainTex_TexelSize.xy;
                float3 col = Tap(i.uv);

                // Flou doux sur 13 échantillons (deux anneaux), à l'échelle de la résolution.
                float r = 2.5 * max(1.0, _MainTex_TexelSize.w / 720.0);
                float3 sum = col * 0.2;
                float w = 0.2;
                [unroll] for (int k = 0; k < 6; k++)
                {
                    float a = k * 1.0472;
                    float2 d = float2(cos(a), sin(a)) * t * r;
                    sum += (Tap(i.uv + d) + Tap(i.uv - d * 2.1)) * 0.0667;
                    w += 0.1333;
                }
                float3 blur = sum / w;

                // Les transitions d'ombre et de lumière se fondent : l'image tend vers le flou là où elle change le plus.
                float3 soft = lerp(col, blur, _Soft * 0.7);
                // Courbe douce : contraste un peu réduit dans les tons moyens, sans éclaircir le noir.
                float l = Luma(soft);
                float target = l + (smoothstep(0.0, 1.0, l) - l) * -0.35 * _Soft;
                soft *= target / max(l, 1e-4);
                // Halo léger autour des zones claires (lampes, néons, écrans).
                soft += max(blur - 0.55, 0.0) * 0.6 * _Soft;
                return fixed4(ToLinear(saturate(soft)), 1.0);
            }
            ENDCG
        }
    }
}
