// Explosion (particules face caméra) : boule de feu ou fumée « volumétrique » dessinée par un bruit fractal animé.
// Chaque particule reçoit sa vie écoulée (0 à 1) et un nombre aléatoire stable par les flux de sommets
// (TEXCOORD0.z et TEXCOORD0.w, réglés par Fx) : le bruit diffère d'une particule à l'autre et ses bords se rongent
// à mesure qu'elle vieillit.
// _Fire = 1 : la chaleur du cœur (blanc-jaune, orange, rouge) retombe avec l'âge en fumée sombre, encore rougeoyante
// par endroits ; _Fire = 0 : fumée seule, éclairée par le dessus. Mélange prémultiplié : le feu émet de la lumière,
// la fumée masque ce qui est derrière.
Shader "FunGame/Explosion"
{
    Properties
    {
        _Fire ("Feu (1) ou fumée (0)", Float) = 1
        _Intensity ("Intensité du feu", Float) = 3
        _SmokeColor ("Couleur de la fumée", Color) = (0.09, 0.08, 0.075, 1)
        _NoiseScale ("Échelle du bruit", Float) = 2.4
        _Erosion ("Érosion en fin de vie", Float) = 0.8
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            float _Fire, _Intensity, _NoiseScale, _Erosion;
            fixed4 _SmokeColor;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float4 uv : TEXCOORD0;      // xy : coordonnées, z : âge (0 à 1), w : aléa stable
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float4 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = float4(v.uv.xy * 2.0 - 1.0, v.uv.z, v.uv.w);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x),
                            lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }

            // Bruit fractal « gonflé » (valeurs absolues) : volutes arrondies comme des boules de fumée.
            // (Octaves fines atténuées : des boules molles plutôt qu'une texture « poilue ».)
            float billow(float2 p)
            {
                float v = 0.0, a = 0.6;
                for (int k = 0; k < 3; k++)
                {
                    float b = noise(p);
                    v += a * (k == 0 ? b : 1.0 - abs(b * 2.0 - 1.0));
                    p = p * 2.07 + float2(17.1, 9.2);
                    a *= 0.4;
                }
                return v / 0.936;
            }

            // Chaleur (0 à 1) vers couleur de flamme : rouge sombre, orange, jaune, blanc.
            float3 FireRamp(float h)
            {
                float3 c = lerp(float3(0.35, 0.03, 0.0), float3(1.0, 0.32, 0.03), saturate(h * 2.5));
                c = lerp(c, float3(1.0, 0.72, 0.18), saturate(h * 2.5 - 1.0));
                return lerp(c, float3(1.0, 0.97, 0.85), saturate(h * 2.5 - 1.6));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv.xy;
                float age = saturate(i.uv.z), seed = i.uv.w;
                float r = length(p);

                // Volutes qui roulent sur elles-mêmes et montent avec l'âge.
                float2 q = p * _NoiseScale + seed * 37.0 + float2(0.0, -age * 1.2);
                float2 warp = float2(noise(q * 0.6 + 3.1), noise(q * 0.6 + 8.7)) - 0.5;
                float n = billow(q + warp * 0.9);
                // Petit décalage vers le haut : de quoi estimer l'éclairage venu d'en haut.
                float nUp = billow(q + warp * 0.9 + float2(0.0, 0.15));

                float shape = pow(saturate(1.0 - r), 1.3);
                float density = shape * (0.5 + 0.9 * n);
                float erode = lerp(0.08, _Erosion, age * age);
                float alpha = saturate((density - erode) * 2.6) * i.color.a;
                if (alpha <= 0.002) discard;

                // Fumée : ombrée en bas, plus claire là où le bruit monte vers le haut.
                float light = saturate(0.5 + (n - nUp) * 2.5 + p.y * 0.4);
                float3 smoke = _SmokeColor.rgb * lerp(0.5, 1.8, light) * i.color.rgb;

                // Feu : chaleur forte au cœur des volutes, qui retombe vite avec l'âge.
                float heat = saturate(density * 1.35 + (n - 0.5) * 0.6 - age * 1.9) * _Fire;
                float burn = smoothstep(0.06, 0.3, heat);
                float3 fire = FireRamp(heat) * _Intensity;

                float3 col = lerp(smoke, fire, burn);
                // Prémultiplié : le feu ajoute sa lumière (opacité réduite), la fumée couvre.
                float4 o = float4(col * alpha, alpha * lerp(1.0, 0.35, burn));
                UNITY_APPLY_FOG_COLOR(i.fogCoord, o, fixed4(0, 0, 0, 0));
                return o;
            }
            ENDCG
        }
    }
}
