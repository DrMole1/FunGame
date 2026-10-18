// Faisceau laser additif posé sur un cylindre (axe Y local) : cœur presque blanc au centre, bords rouges
// qui s'estompent (fresnel), énergie qui défile le long du faisceau en filaments, et léger scintillement.
// Le même shader sert au cœur (_CorePower élevé) et au halo qui l'entoure (_CorePower bas, intensité faible).
Shader "FunGame/LaserBeam"
{
    Properties
    {
        _Color ("Couleur", Color) = (1, 0.06, 0.04, 1)
        _CoreColor ("Cœur", Color) = (1, 0.85, 0.75, 1)
        _Intensity ("Intensité", Float) = 2
        _CorePower ("Concentration", Float) = 2.5
        _FlowSpeed ("Défilement (m/s)", Float) = 6
        _NoiseScale ("Échelle des filaments", Float) = 6
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
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color, _CoreColor;
            float _Intensity, _CorePower, _FlowSpeed, _NoiseScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float2 coord : TEXCOORD2; // x : distance le long du faisceau (m), y : angle autour de l'axe
            };

            float Hash12(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash12(i), Hash12(i + float2(1, 0)), f.x),
                            lerp(Hash12(i + float2(0, 1)), Hash12(i + float2(1, 1)), f.x), f.y);
            }

            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 axis = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 1, 0)));
                o.coord = float2(dot(o.worldPos, axis), atan2(v.vertex.z, v.vertex.x));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float facing = saturate(abs(dot(n, v)));
                float core = pow(facing, _CorePower);

                // Filaments d'énergie qui défilent, plus un grain fin plus rapide en sens inverse.
                float t = _Time.y;
                float along = i.coord.x * _NoiseScale;
                float strands = ValueNoise(float2(along - t * _FlowSpeed * _NoiseScale * 0.15, i.coord.y * 1.5));
                float grain = ValueNoise(float2(along * 4.0 + t * _FlowSpeed * 2.0, i.coord.y * 3.0 + 7.0));
                float energy = 0.55 + 0.7 * strands * strands + 0.25 * grain;

                // Scintillement global et pulsations qui courent le long du faisceau.
                float flicker = 0.9 + 0.1 * sin(t * 53.0) * sin(t * 31.0 + 1.7);
                float pulse = 0.85 + 0.15 * sin(i.coord.x * 9.0 - t * 22.0);

                float3 col = lerp(_Color.rgb, _CoreColor.rgb, saturate(core * core * 1.5));
                col *= core * energy * flicker * pulse * _Intensity;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
