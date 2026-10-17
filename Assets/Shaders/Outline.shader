// Contour lumineux (coque inversée) des objets interactifs visés. Une passe, sans éclairage.
Shader "FunGame/Outline"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.85, 0.35, 1)
        _Width ("Width", Float) = 0.008
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+10" "RenderType" = "Opaque" }
        Pass
        {
            Cull Front
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Width;

            struct v2f { float4 pos : SV_POSITION; };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // Décalage en espace écran : épaisseur constante quelle que soit la distance.
                float3 n = mul((float3x3)UNITY_MATRIX_IT_MV, v.normal);
                float2 off = normalize(TransformViewToProjection(n.xy) + 1e-5);
                off.x *= _ScreenParams.y / _ScreenParams.x;
                o.pos.xy += off * _Width * o.pos.w;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return _Color * (1.2 + 0.4 * sin(_Time.y * 6.0));
            }
            ENDCG
        }
    }
}
