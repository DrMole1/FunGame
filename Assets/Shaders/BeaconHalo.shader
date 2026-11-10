// Halo des Balises : même rendu que l'aura fantomatique (bord lumineux, flammèches), mais sur un simple carré toujours face à la
// caméra, qui dessine lui-même une sphère. La respiration est calculée ici : le script n'a rien à animer, et tous les halos d'une
// même couleur sont dessinés d'un seul coup (instanciation).
Shader "FunGame/BeaconHalo"
{
    Properties
    {
        _Color ("Aura", Color) = (0.3, 0.65, 1, 1)
        _Intensity ("Intensité", Float) = 1.6
        _Power ("Finesse du bord", Float) = 2.2
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "DisableBatching" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Intensity, _Power;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 disc : TEXCOORD0;
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

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 origin = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float size = length(float3(unity_ObjectToWorld[0].x, unity_ObjectToWorld[1].x, unity_ObjectToWorld[2].x));
                // Le halo respire doucement, chacun à son rythme.
                size *= 1.0 + 0.12 * sin(_Time.y * 2.4 + dot(origin.xz, float2(1.7, 2.3)));
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                // Avancé vers la caméra, à la place de l'avant de la sphère : le cristal ne le coupe pas.
                float3 world = origin + normalize(_WorldSpaceCameraPos - origin) * size * 0.5 + (right * v.vertex.x + up * v.vertex.y) * size;
                o.pos = UnityWorldToClipPos(world);
                o.disc = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float len = length(i.disc);
                // Le contour ondule.
                float wobble = Noise(float3(i.disc / max(len, 0.001) * 2.0, _Time.y * 1.2)) - 0.5;
                float r = len / (1.0 + wobble * 0.16);
                if (r >= 1.0) return fixed4(0, 0, 0, 1);
                float3 normal = float3(i.disc / max(len, 0.001) * r, sqrt(1.0 - r * r));
                float rim = pow(1.0 - normal.z, _Power);
                float flames = Noise(normal * 3.0 + float3(0, -_Time.y * 1.8, 0));
                float glow = rim * (0.6 + flames) + flames * flames * 0.25;
                return fixed4(_Color.rgb * glow * _Intensity, 1);
            }
            ENDCG
        }
    }
}
