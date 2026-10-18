// Étincelles (particules) : point lumineux additif à cœur chaud, teinté par la couleur de la particule.
// Prévu pour un rendu "Stretch" : la particule s'allonge en trait dans le sens de sa vitesse.
Shader "FunGame/Spark"
{
    Properties
    {
        _Intensity ("Intensité", Float) = 2.5
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Intensity;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv * 2.0 - 1.0;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float d = saturate(1.0 - length(i.uv));
                float glow = d * d;
                // Le centre tire vers le blanc, les bords gardent la couleur de la particule.
                float3 col = lerp(i.color.rgb, float3(1, 0.95, 0.8), saturate(glow * glow * 2.0));
                return fixed4(col * glow * i.color.a * _Intensity, 1);
            }
            ENDCG
        }
    }
}
