// Aura fantomatique additive (orbes activés) : halo en fresnel qui ondule, flammèches qui montent.
// Posée sur une sphère un peu plus grande que l'objet entouré.
Shader "FunGame/GhostAura"
{
    Properties
    {
        _Color ("Aura", Color) = (0.3, 0.65, 1, 1)
        _Intensity ("Intensité", Float) = 1.6
        _Power ("Finesse du bord", Float) = 2.2
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Intensity, _Power;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 local : TEXCOORD2;
            };

            float Hash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            v2f vert(appdata_base v)
            {
                v2f o;
                // Le halo ondule.
                float wobble = Noise(v.vertex.xyz * 4.0 + _Time.y * float3(0.3, 1.2, 0.2)) - 0.5;
                v.vertex.xyz += v.normal * wobble * 0.08;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.local = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float rim = pow(1.0 - saturate(abs(dot(normalize(i.normal), v))), _Power);
                float flames = Noise(i.local * 6.0 + float3(0, -_Time.y * 1.8, 0));
                float glow = rim * (0.6 + flames) + flames * flames * 0.25;
                return fixed4(_Color.rgb * glow * _Intensity, 1);
            }
            ENDCG
        }
    }
}
