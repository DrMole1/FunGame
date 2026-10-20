// Lueur additive, insensible au brouillard et à l'éclairage : se voit dans le noir (yeux de La Tour).
// Plus vive au centre de l'objet (face à la caméra), plus douce sur les bords.
Shader "FunGame/Glow"
{
    Properties
    {
        _Color ("Lueur", Color) = (1, 0.1, 0.05, 1)
        _Intensity ("Intensité", Float) = 3
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Intensity;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 view = normalize(_WorldSpaceCameraPos - i.worldPos);
                float facing = saturate(dot(normalize(i.normal), view));
                return fixed4(_Color.rgb * _Intensity * (0.35 + facing * facing), 1);
            }
            ENDCG
        }
    }
}
