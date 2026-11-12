// Sol Para Todos - Cielo en degradado (zenit / horizonte / suelo) con sol-luna y estrellas.
// Lo controla SolarFieldController cada frame: el color de horizonte es EL MISMO que el de la niebla,
// asi el terreno se funde con el cielo sin linea de horizonte visible.
Shader "SolParaTodos/SkyGradient"
{
    Properties
    {
        _Zenith    ("Zenit",     Color) = (0.02, 0.07, 0.30, 1)
        _Horizon   ("Horizonte", Color) = (0.40, 0.60, 0.85, 1)
        _GroundCol ("Suelo",     Color) = (0.08, 0.10, 0.06, 1)
        _SunColor  ("Color del sol/luna", Color) = (1, 0.93, 0.78, 1)
        _SunDir    ("Direccion hacia el sol", Vector) = (0, 1, 0, 0)
        _SunSize   ("Tamano angular (grados)", Float) = 1.4
        _SunGlow   ("Intensidad del halo", Float) = 0.45
        _SunGlowPower ("Concentracion del halo", Float) = 24
        _Exponent  ("Curva del degradado", Float) = 0.45
        _Night     ("Noche (0-1)", Range(0,1)) = 0
        _Stars     ("Estrellas", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Zenith;
            float4 _Horizon;
            float4 _GroundCol;
            float4 _SunColor;
            float4 _SunDir;
            float  _SunSize;
            float  _SunGlow;
            float  _SunGlowPower;
            float  _Exponent;
            float  _Night;
            float  _Stars;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float Hash31 (float3 p)
            {
                return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
            }

            half4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;

                // Degradado: horizonte -> zenit (arriba) y horizonte -> suelo (abajo).
                float up = pow(saturate(h), _Exponent);
                float3 col = lerp(_Horizon.rgb, _Zenith.rgb, up);
                float below = saturate(-h * 6.0);
                col = lerp(col, _GroundCol.rgb, below);

                // Sol / luna: halo + disco.
                float3 sdir = normalize(_SunDir.xyz + float3(0.0, 0.00001, 0.0));
                float sd = dot(d, sdir);
                float glow = pow(saturate(sd), _SunGlowPower) * _SunGlow;
                col += _SunColor.rgb * glow * saturate(h * 4.0 + 0.6);
                float ang = degrees(acos(clamp(sd, -1.0, 1.0)));
                float disk = 1.0 - smoothstep(_SunSize * 0.85, _SunSize, ang);
                col = lerp(col, _SunColor.rgb * 1.6, disk * step(-0.05, h));

                // Estrellas (solo de noche y sobre el horizonte).
                float3 cell = floor(d * 700.0);
                float n = Hash31(cell);
                float star = step(0.9972, n) * (0.55 + 0.45 * frac(n * 91.7));
                col += star * _Night * _Stars * saturate(h * 3.0);

                return half4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
