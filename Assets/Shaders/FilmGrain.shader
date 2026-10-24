// Grain d'écran : bruit gris dessiné après la scène, avant les interfaces (Canvas en Screen Space Overlay).
// Fusion multiplicative x2 : 0,5 laisse l'image intacte, au-dessus éclaircit, en dessous assombrit.
// _Intensity (0 à 1) est réglé par le jeu ; le motif change 24 fois par seconde, comme une pellicule.
// _Brightness assombrit la scène (1 = intacte), par exemple quand la souris survole un menu.
Shader "FunGame/FilmGrain"
{
    Properties
    {
        _Intensity ("Intensité", Range(0, 1)) = 0.18
        _GrainSize ("Taille du grain (pixels)", Float) = 1.5
        _Brightness ("Luminosité", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend DstColor SrcColor
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Intensity, _GrainSize, _Brightness;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(0.1031, 0.1030));
                p += dot(p, p.yx + 33.33);
                return frac((p.x + p.y) * p.x);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 cell = floor(i.uv * _ScreenParams.xy / _GrainSize);
                float frame = fmod(floor(_Time.y * 24.0), 97.0);
                float2 p = cell + frame * float2(37.0, 17.0);
                // Deux tirages moyennés : un grain plus doux, concentré autour du gris neutre.
                float n = (Hash(p) + Hash(p + 113.0)) * 0.5 - 0.5;
                float g = (0.5 + n * _Intensity) * _Brightness;
                return fixed4(g, g, g, 1);
            }
            ENDCG
        }
    }
}
