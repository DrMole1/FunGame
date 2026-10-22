// Matière légèrement transparente, éclairée (Note 05 de la Direction Artistique, V0.4.7) : sphères du décor sans
// collision, noires ou blanches, qu'on devine pouvoir traverser. L'alpha vient de la couleur.
Shader "FunGame/Translucent"
{
    Properties
    {
        _Color ("Couleur", Color) = (0.9, 0.9, 0.9, 0.45)
        _Glossiness ("Brillance", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off

        CGPROGRAM
        #pragma surface surf Standard alpha:fade
        #pragma target 3.0

        fixed4 _Color;
        half _Glossiness;

        struct Input
        {
            float3 worldPos;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = _Color.rgb;
            o.Smoothness = _Glossiness;
            o.Metallic = 0;
            o.Alpha = _Color.a;
        }
        ENDCG
    }
    Fallback "Transparent/Diffuse"
}
