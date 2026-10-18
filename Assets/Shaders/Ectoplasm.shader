// Battant de la porte massive de la Zone 2 : bleu ectoplasmique, avec des crânes et des mains qui se pressent
// contre la surface depuis l'autre côté (formes procédurales qui dérivent, apparaissent et s'effacent).
// UV : (0, 0) en bas à gauche du battant, (1, 1) en haut à droite. _Open (0 à 1) fait se dissiper la porte.
Shader "FunGame/Ectoplasm"
{
    Properties
    {
        _Deep ("Fond", Color) = (0.02, 0.08, 0.22, 1)
        _Mid ("Ectoplasme", Color) = (0.15, 0.45, 0.95, 1)
        _Light ("Lueur", Color) = (0.7, 0.92, 1, 1)
        _Aspect ("Largeur / hauteur", Float) = 0.95
        _Open ("Ouverture", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Deep, _Mid, _Light;
            float _Aspect, _Open;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float Hash(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float n = 0.0, a = 0.5;
                for (int k = 0; k < 5; k++) { n += a * Noise(p); p = p * 2.02 + 11.3; a *= 0.5; }
                return n;
            }

            float Circle(float2 p, float r) { return length(p) - r; }
            float Box(float2 p, float2 b) { float2 d = abs(p) - b; return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0); }
            float Capsule(float2 p, float2 a, float2 b, float r)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h) - r;
            }

            // Crâne (silhouette) : boîte crânienne, mâchoire ; renvoie aussi les orbites (creux).
            float Skull(float2 p, out float holes)
            {
                float head = min(Circle(p - float2(0, 0.05), 0.2), Box(p - float2(0, -0.13), float2(0.11, 0.08)) - 0.02);
                holes = min(Circle(p - float2(-0.075, 0.0), 0.055), Circle(p - float2(0.075, 0.0), 0.055));
                holes = min(holes, Box(p - float2(0, -0.08), float2(0.02, 0.03)));
                return head;
            }

            // Main ouverte, doigts écartés vers le haut.
            float Hand(float2 p)
            {
                float d = Circle(p * float2(1.0, 1.2), 0.12);
                d = min(d, Capsule(p, float2(0, -0.1), float2(0, -0.3), 0.07));
                d = min(d, Capsule(p, float2(-0.08, 0.06), float2(-0.2, 0.28), 0.03));
                d = min(d, Capsule(p, float2(-0.03, 0.09), float2(-0.07, 0.36), 0.032));
                d = min(d, Capsule(p, float2(0.03, 0.09), float2(0.06, 0.37), 0.032));
                d = min(d, Capsule(p, float2(0.08, 0.06), float2(0.17, 0.3), 0.03));
                d = min(d, Capsule(p, float2(0.1, -0.05), float2(0.27, 0.06), 0.032));
                return d;
            }

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 p = float2(i.uv.x * _Aspect, i.uv.y);
                float2 warp = float2(Fbm(p * 3.0 + t * 0.15), Fbm(p * 3.0 - t * 0.12 + 5.0)) - 0.5;
                float smoke = Fbm(p * 4.0 + warp * 1.5 + float2(0, -t * 0.2));

                float3 col = lerp(_Deep.rgb, _Mid.rgb, smoothstep(0.25, 0.85, smoke));
                float shapes = 0.0;

                // Trois crânes et cinq mains qui dérivent lentement et se pressent tour à tour contre la porte.
                for (int k = 0; k < 8; k++)
                {
                    float seed = k * 7.31;
                    float cycle = 6.0 + Hash(float2(seed, 1)) * 5.0;
                    float phase = frac(t / cycle + Hash(float2(seed, 2)));
                    float presence = smoothstep(0.0, 0.3, phase) * smoothstep(1.0, 0.65, phase);
                    float2 center = float2(Hash(float2(seed, 3)) * 0.7 + 0.12, Hash(float2(seed, 4)) * 0.75 + 0.15);
                    center.x *= _Aspect;
                    center += float2(sin(t * 0.3 + seed), cos(t * 0.23 + seed)) * 0.04 + warp * 0.05;
                    float size = 0.55 + Hash(float2(seed, 5)) * 0.35;
                    float2 q = (p - center) / size;
                    float angle = (Hash(float2(seed, 6)) - 0.5) * 0.8;
                    q = float2(q.x * cos(angle) - q.y * sin(angle), q.x * sin(angle) + q.y * cos(angle));
                    float holes = 1.0;
                    float d = k < 3 ? Skull(q, holes) : Hand(q);
                    // Plus nette quand elle se colle à la porte, floue quand elle s'éloigne.
                    float blur = lerp(0.12, 0.015, presence);
                    float body = (1.0 - smoothstep(-blur, blur, d)) * presence;
                    float hole = (1.0 - smoothstep(-0.02, 0.02, holes)) * body;
                    shapes = max(shapes, body);
                    col = lerp(col, _Light.rgb * (0.7 + 0.3 * smoke), body * 0.85);
                    col = lerp(col, _Deep.rgb * 0.3, hole);
                }

                // Lueur pulsée et bords plus sombres.
                float edge = smoothstep(0.0, 0.08, i.uv.x) * smoothstep(1.0, 0.92, i.uv.x) * smoothstep(0.0, 0.05, i.uv.y) * smoothstep(1.0, 0.95, i.uv.y);
                col *= lerp(0.5, 1.0, edge) * (0.9 + 0.1 * sin(t * 2.1));

                // Ouverture : la porte se dissipe en volutes, du bas vers le haut.
                float dissolve = smoke + (1.0 - i.uv.y) * 0.4;
                float alpha = lerp(0.97, 0.0, smoothstep(dissolve - 0.15, dissolve + 0.15, _Open * 1.6));
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}
