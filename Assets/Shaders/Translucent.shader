// Matière fantomatique (Note 05 de la Direction Artistique) : sphères du décor sans collision, noires ou blanches, qu'on
// devine pouvoir traverser. Presque invisible en son cœur, plus dense sur le contour (effet de Fresnel), parcourue de
// volutes qui montent lentement, et qui « respire » doucement. L'alpha de la couleur règle la transparence générale.
Shader "FunGame/Translucent"
{
    Properties
    {
        _Color ("Couleur", Color) = (0.9, 0.9, 0.9, 0.45)
        _RimPower ("Finesse du contour", Float) = 2.2
        _Wisps ("Échelle des volutes", Float) = 1.6
        _Breath ("Respiration", Float) = 0.025
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _RimPower, _Wisps, _Breath;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float3 normal : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            float hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float noise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash(i), hash(i + float3(1, 0, 0)), f.x),
                                 lerp(hash(i + float3(0, 1, 0)), hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(hash(i + float3(0, 0, 1)), hash(i + float3(1, 0, 1)), f.x),
                                 lerp(hash(i + float3(0, 1, 1)), hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            v2f vert(appdata v)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                // Respiration lente, décalée d'une sphère à l'autre (selon sa position).
                float3 center = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float breath = sin(_Time.y * 0.9 + dot(center, float3(0.37, 0.11, 0.53))) * _Breath;
                v.vertex.xyz += v.normal * breath;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = world;
                o.normal = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 view = normalize(_WorldSpaceCameraPos - i.world);
                float edgeness = 1.0 - saturate(abs(dot(n, view)));
                // Halo doux juste en deçà du contour, qui s'efface sur la silhouette même : pas de bord net de bulle.
                float rim = pow(edgeness, _RimPower) * (1.0 - smoothstep(0.82, 1.0, edgeness));

                // Volutes qui montent : bruit à deux échelles, déformé, défilant vers le haut.
                float3 q = i.world * _Wisps + float3(0.0, -_Time.y * 0.35, 0.0);
                float w = noise(q + noise(q * 0.5 + _Time.y * 0.1) * 1.5);
                w = 0.6 * w + 0.4 * noise(q * 2.3 + 5.0);
                float wisps = smoothstep(0.42, 0.72, w);

                float a = _Color.a;
                float alpha = a * (0.5 + 0.6 * wisps) * (0.7 + 0.3 * edgeness) + rim * a * 0.8;
                // Le halo des sphères noires tire à peine vers le gris (sinon invisibles dans la pénombre).
                float3 core = _Color.rgb;
                float3 edge = lerp(_Color.rgb, max(_Color.rgb, 0.16), 0.8);
                float3 col = lerp(core, edge, saturate(rim * 1.5));
                // En espace linéaire, un voile sombre semi-transparent paraît gris clair sur un fond blanc : les sphères
                // noires gagnent en densité pour rester une fumée noire.
                alpha = pow(saturate(alpha), lerp(0.4, 1.0, saturate(Luminance(_Color.rgb) * 1.5)));
                fixed4 o = fixed4(col, alpha);
                UNITY_APPLY_FOG(i.fogCoord, o);
                return o;
            }
            ENDCG
        }
    }
    Fallback "Transparent/Diffuse"
}
