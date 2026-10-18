// Voile blanc de La Dame (particules) : lambeaux de tissu vaporeux, bords effilochés qui ondulent.
// Teinte et transparence données par la couleur de la particule.
Shader "FunGame/Veil"
{
    Properties
    {
        _Softness ("Douceur", Range(0.05, 1)) = 0.6
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
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

            float _Softness;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float4 uv : TEXCOORD0; // xy : uv, z : graine aléatoire de la particule (custom vertex stream)
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float3 uv : TEXCOORD0;
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

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = float3(v.uv.xy, v.uv.z);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv.xy * 2.0 - 1.0;
                float seed = i.uv.z * 37.0 + i.color.a * 3.0;
                // Lambeau : forme allongée verticalement, déchirée par du bruit qui ondule.
                float tear = Noise(float2(p.x * 3.0 + seed, p.y * 1.5 - _Time.y * 0.8)) * 0.6
                           + Noise(float2(p.x * 7.0 - seed, p.y * 4.0 + _Time.y)) * 0.4;
                float shape = 1.0 - length(p * float2(1.0, 0.65));
                float a = smoothstep(0.0, _Softness, shape - (tear - 0.45) * 0.7);
                // Filaments plus denses.
                float threads = smoothstep(0.55, 0.8, Noise(float2(p.x * 14.0 + seed, p.y * 2.0)));
                fixed4 col = fixed4(i.color.rgb * (0.85 + threads * 0.3), a * i.color.a * (0.7 + threads * 0.3));
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(0, 0, 0, 0));
                return col;
            }
            ENDCG
        }
    }
}
