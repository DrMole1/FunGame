// Personnage dont le maillage a des trous : les faces sont aussi rendues de dos (normale retournée,
// un peu assombries), si bien qu'à travers un trou on voit l'intérieur du tissu et non le décor.
Shader "FunGame/DoubleSidedCharacter"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.2
        _BackShade ("Back Face Shade", Range(0,1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness, _BackShade;

        struct Input
        {
            float2 uv_MainTex;
            float facing : VFACE;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            bool back = IN.facing < 0;
            o.Albedo = back ? c.rgb * _BackShade : c.rgb;
            o.Normal = back ? float3(0, 0, -1) : float3(0, 0, 1);
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
