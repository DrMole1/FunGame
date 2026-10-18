// Brume de l'Abjuration : nappes horizontales superposées au ras du sol, bleu ectoplasmique.
// Bruit fractal en coordonnées monde qui dérive lentement, volutes plus claires, fondu près de la caméra
// (pas de plan net quand on la traverse) et selon _Abjuration (0 = absente, 1 = pleinement là, réglé par le jeu).
Shader "FunGame/Mist"
{
    Properties
    {
        _Color ("Brume", Color) = (0.25, 0.55, 1, 1)
        _Glow ("Volutes", Color) = (0.6, 0.85, 1, 1)
        _Density ("Opacité par nappe", Range(0, 1)) = 0.32
        _Scale ("Échelle", Float) = 0.35
        _Speed ("Dérive", Float) = 0.25
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
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _Color, _Glow;
            float _Density, _Scale, _Speed;
            float _Abjuration; // global

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                UNITY_FOG_COORDS(1)
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
                for (int k = 0; k < 4; k++) { n += a * Noise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return n;
            }

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _Speed;
                // Chaque nappe (selon sa hauteur) dérive dans une direction un peu différente.
                float2 p = i.worldPos.xz * _Scale + float2(i.worldPos.y * 3.7, i.worldPos.y * -2.3);
                float2 warp = float2(Fbm(p * 0.7 + t * 0.6), Fbm(p * 0.7 - t * 0.5 + 9.0));
                float n = Fbm(p + warp * 1.8 + float2(t, -t * 0.7));
                float density = smoothstep(0.3, 0.75, n);
                float wisps = smoothstep(0.62, 0.9, Fbm(p * 2.3 - warp + t * 1.3));

                float camDist = distance(i.worldPos, _WorldSpaceCameraPos);
                float nearFade = saturate((camDist - 0.4) / 1.6);
                float alpha = density * _Density * nearFade * saturate(_Abjuration);

                fixed4 col = fixed4(lerp(_Color.rgb, _Glow.rgb, wisps), alpha);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(0, 0, 0, 0));
                return col;
            }
            ENDCG
        }
    }
}
