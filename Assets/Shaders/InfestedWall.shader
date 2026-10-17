// Murs des couloirs du labyrinthe : béton sombre et humide, couvert d'insectes qui grouillent.
// Tout est procédural (coordonnées monde) : trois couches d'insectes de tailles, directions et vitesses
// différentes, regroupés en essaims qui dérivent lentement et plus denses près du sol.
// Ils fuient les joueurs (moins loin si le joueur marche discrètement) et se tassent autour d'eux.
Shader "FunGame/InfestedWall"
{
    Properties
    {
        _MainTex ("Grain", 2D) = "white" {}
        _WallColor ("Mur", Color) = (0.2, 0.19, 0.17, 1)
        _StainColor ("Taches", Color) = (0.05, 0.045, 0.035, 1)
        _BugColor ("Insectes", Color) = (0.018, 0.012, 0.008, 1)
        _BugSpeed ("Vitesse (m/s)", Float) = 0.28
        _Density ("Densité", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _WallColor, _StainColor, _BugColor;
        float _BugSpeed, _Density;
        // Joueurs que les insectes fuient (xyz : position, w : rayon, 0 = inactif). Global, mis à jour par GameSession.
        float4 _InsectFlee[8];

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        float Hash12(float2 p)
        {
            float3 p3 = frac(p.xyx * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        float3 Hash32(float2 p)
        {
            float3 p3 = frac(p.xyx * float3(0.1031, 0.1030, 0.0973));
            p3 += dot(p3, p3.yxz + 33.33);
            return frac((p3.xxy + p3.yzz) * p3.zyx);
        }

        float ValueNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(Hash12(i), Hash12(i + float2(1, 0)), f.x),
                        lerp(Hash12(i + float2(0, 1)), Hash12(i + float2(1, 1)), f.x), f.y);
        }

        float Segment(float2 p, float2 a, float2 b)
        {
            float2 pa = p - a, ba = b - a;
            return length(pa - ba * saturate(dot(pa, ba) / dot(ba, ba)));
        }

        // Une couche d'insectes. Chaque colonne de cellules défile dans son propre sens, par à-coups ;
        // au plus un insecte par cellule. Retourne (couverture, reflet de la carapace).
        float2 BugLayer(float2 uv, float angle, float scale, float density, float t, float seed)
        {
            float s = sin(angle), c = cos(angle);
            float2 p = float2(c * uv.x - s * uv.y, s * uv.x + c * uv.y) * scale;

            float col = floor(p.x);
            float h = Hash12(float2(col, seed));
            float dir = h < 0.5 ? -1.0 : 1.0;
            float rate = _BugSpeed * scale * (0.6 + h);
            // Vitesse qui varie (arrêts, accélérations) sans jamais reculer.
            float travel = rate * (t + 0.8 * sin(t * 2.3 + h * 40.0) / 2.3);
            p.y -= travel * dir;

            float aa = max(fwidth(p.y), 1e-4) * 1.2;
            float3 r = Hash32(float2(col, floor(p.y)) + seed * 7.13);
            float2 q = frac(p) - 0.5;
            q -= float2((r.y - 0.5) * 0.2, (r.z - 0.5) * 0.12);
            q.x += sin(t * 11.0 + r.y * 50.0) * 0.045;
            q.y *= dir;

            // Corps, tête, pattes (marche en tripode) et antennes.
            float d = (length(q / float2(0.1, 0.19)) - 1.0) * 0.1;
            d = min(d, length(q - float2(0.0, 0.22)) - 0.06);
            float gait = sin(t * 28.0 + r.z * 6.28) * 0.05;
            for (int i = 0; i < 3; i++)
            {
                float y0 = -0.08 + i * 0.08;
                float swing = (i == 1 ? -gait : gait);
                d = min(d, Segment(q, float2(0.06, y0), float2(0.21, y0 + (i - 1) * 0.07 + swing)) - 0.011);
                d = min(d, Segment(q, float2(-0.06, y0), float2(-0.21, y0 + (i - 1) * 0.07 - swing)) - 0.011);
            }
            float wave = sin(t * 7.0 + r.y * 9.0) * 0.03;
            d = min(d, Segment(q, float2(0.02, 0.26), float2(0.11 + wave, 0.38)) - 0.006);
            d = min(d, Segment(q, float2(-0.02, 0.26), float2(-0.11 - wave, 0.38)) - 0.006);

            float cover = saturate(0.5 - d / aa);
            // Les insectes apparaissent et disparaissent en fondu selon la densité de l'essaim.
            cover *= smoothstep(r.x, r.x + 0.05, density);
            float shine = cover * saturate(1.0 - abs(q.x) / 0.05) * step(abs(q.y), 0.2);
            return float2(cover, shine);
        }

        float2 WallUV(float3 p, float3 an)
        {
            return an.y > 0.5 ? p.xz : float2(p.x * an.z + p.z * an.x, p.y);
        }

        // Les insectes s'écartent des joueurs : zone vide autour d'eux, insectes repoussés et tassés en anneau au bord.
        float2 Flee(float2 uv, float3 wp, float3 nrm, float3 an, out float clear, out float crowd)
        {
            clear = 1.0;
            crowd = 0.0;
            for (int i = 0; i < 8; i++)
            {
                float4 f = _InsectFlee[i];
                if (f.w <= 0.0) continue;
                float plane = dot(wp - f.xyz, nrm);
                float r2 = f.w * f.w - plane * plane;
                if (r2 <= 0.0) continue;
                float radius = sqrt(r2);
                float2 delta = uv - WallUV(f.xyz, an);
                float d = length(delta);
                float k = saturate(1.0 - d / radius);
                float s = d - radius * 0.45 * k * k;
                uv = WallUV(f.xyz, an) + delta / max(d, 1e-4) * s;
                float x = d / radius;
                clear *= smoothstep(0.35, 0.6, x);
                crowd = max(crowd, smoothstep(0.4, 0.65, x) * (1.0 - smoothstep(0.85, 1.15, x)));
            }
            return uv;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 nrm = normalize(IN.worldNormal);
            float3 n = abs(nrm);
            float3 wp = IN.worldPos;
            float2 uv = WallUV(wp, n);
            float t = _Time.y;

            // Mur : grain, coulures d'humidité, crasse en bas.
            float grain = tex2D(_MainTex, uv * 0.35).r;
            float stain = smoothstep(0.45, 0.85, ValueNoise(float2(uv.x * 2.5, uv.y * 0.25 + 3.1)));
            float3 wall = _WallColor.rgb * (0.65 + 0.5 * grain);
            wall = lerp(wall, _StainColor.rgb, stain * 0.8);
            wall *= lerp(0.55, 1.0, saturate(uv.y / 1.2));

            // Essaims qui dérivent lentement, plus denses près du sol.
            float swarm = smoothstep(0.3, 0.75, ValueNoise(uv * 0.45 + float2(t * 0.03, -t * 0.05)));
            swarm = saturate(swarm + saturate(1.2 - uv.y) * 0.35);
            wall *= 1.0 - swarm * 0.35;

            float clear, crowd;
            float2 bugUV = Flee(uv, wp, nrm, n, clear, crowd);
            float density = saturate(_Density * swarm + crowd * 0.5);
            float2 big = BugLayer(bugUV, 0.2, 3.0, density * 0.6, t, 1.0);
            float2 mid = BugLayer(bugUV, -0.5, 6.0, density, t, 7.0);
            float2 tiny = BugLayer(bugUV, 1.3, 11.0, saturate(density * swarm + crowd * 0.6), t, 13.0);
            big *= clear;
            mid *= clear;
            tiny *= clear;
            float cover = max(big.x, max(mid.x, tiny.x));
            float shine = max(big.y, max(mid.y, tiny.y));

            float3 bug = _BugColor.rgb + shine * float3(0.03, 0.016, 0.006);
            o.Albedo = lerp(wall, bug, cover);
            // Carapaces luisantes, sans reflets parasites sur les plus petits insectes.
            o.Smoothness = lerp(0.12 + stain * 0.35, 0.3, cover) + max(big.x, mid.x) * 0.35;
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
