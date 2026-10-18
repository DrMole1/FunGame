// Rayon d'énergie bleu entre deux Balises chargées (LineRenderer, texture étirée) : cœur clair au centre,
// bords qui s'estompent, et paquets d'énergie qui circulent du début du rayon (Balise la plus éloignée)
// vers sa fin (Balise plus proche du Hub). _Length (longueur du rayon en mètres) garde un rythme constant.
Shader "FunGame/EnergyBeam"
{
    Properties
    {
        _Color ("Couleur", Color) = (0.15, 0.45, 1, 1)
        _CoreColor ("Cœur", Color) = (0.75, 0.9, 1, 1)
        _Intensity ("Intensité", Float) = 1.6
        _FlowSpeed ("Défilement (m/s)", Float) = 3
        _Spacing ("Écart entre paquets (m)", Float) = 1.2
        _Length ("Longueur (m)", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color, _CoreColor;
            float _Intensity, _FlowSpeed, _Spacing, _Length;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float across = abs(i.uv.y - 0.5) * 2.0;
                float core = exp(-across * across * 9.0);
                float halo = saturate(1.0 - across) * 0.35;

                // Paquets d'énergie : position en mètres le long du rayon, qui avance avec le temps.
                float d = i.uv.x * _Length - _Time.y * _FlowSpeed;
                float wave = frac(d / _Spacing);
                float packet = pow(saturate(1.0 - abs(wave - 0.5) * 2.0), 6.0);
                float shimmer = 0.85 + 0.15 * sin(_Time.y * 37.0 + i.uv.x * _Length * 5.0);

                // Extrémités adoucies.
                float ends = saturate(i.uv.x * _Length * 4.0) * saturate((1.0 - i.uv.x) * _Length * 4.0);

                float3 col = _Color.rgb * (halo + core * 0.6) + _CoreColor.rgb * core * packet * 1.4;
                return fixed4(col * _Intensity * shimmer * ends * i.color.a, 1);
            }
            ENDCG
        }
    }
}
