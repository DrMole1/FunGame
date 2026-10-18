// Battant de la porte massive de la Zone 2 : fumée bleue ectoplasmique qui ondule, lueur pulsée.
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
                // Volutes plus claires qui remontent.
                float wisps = smoothstep(0.6, 0.9, Fbm(p * 7.0 - warp * 2.0 + float2(0, -t * 0.35)));
                col = lerp(col, _Light.rgb, wisps * 0.45);

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
