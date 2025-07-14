// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

// WaterSurface.shader
Shader "Custom/WaterSurface"
{
    Properties
    {
        _Color ("Water Color", Color) = (0.2, 0.4, 0.8, 1)
        _DisplacementScale ("Displacement Scale", Float) = 1.0
        _MinHeightColor ("Min Height Color", Color) = (0.1, 0.2, 0.4, 1)
        _MaxHeightColor ("Max Height Color", Color) = (0.3, 0.5, 0.9, 1)
        _HeightRange ("Height Range (Max - Min)", Float) = 2.0

        // Propiedades de Luz para depuración
        _LightDirection ("Light Direction (World)", Vector) = (0.0, -1.0, 0.0, 0.0)
        _LightColor ("Light Color", Color) = (1.0, 1.0, 1.0, 1.0)

        // Texturas de desplazamiento
        _DisplacementMapY ("Displacement Map Y", 2D) = "black" {}
        _DisplacementMapX ("Displacement Map X", 2D) = "black" {}
        _DisplacementMapZ ("Displacement Map Z", 2D) = "black" {}

        // Texturas de pendiente (Slope Maps)
        _SlopeMapX ("Slope Map X", 2D) = "black" {}
        _SlopeMapZ ("Slope Map Z", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float rawHeight : TEXCOORD1;
                float3 worldNormal : NORMAL;
                float3 worldPos : TEXCOORD2;
            };

            sampler2D _DisplacementMapY;
            sampler2D _DisplacementMapX;
            sampler2D _DisplacementMapZ;
            float _DisplacementScale;
            
            sampler2D _SlopeMapX;
            sampler2D _SlopeMapZ;

            fixed4 _Color;
            fixed4 _MinHeightColor;
            fixed4 _MaxHeightColor;
            float _HeightRange;

            float4 _LightDirection;
            fixed4 _LightColor;

            v2f vert (appdata v)
            {
                v2f o;
                
                // --- 1. Aplicar Desplazamiento de Vértices ---
                float dispY = tex2Dlod(_DisplacementMapY, float4(v.uv, 0, 0)).r;
                float dispX = tex2Dlod(_DisplacementMapX, float4(v.uv, 0, 0)).r;
                float dispZ = tex2Dlod(_DisplacementMapZ, float4(v.uv, 0, 0)).r;

                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                
                worldPos.y += dispY * _DisplacementScale;
                worldPos.x += dispX * _DisplacementScale;
                worldPos.z += dispZ * _DisplacementScale;

                o.worldPos = worldPos.xyz;
                o.vertex = UnityObjectToClipPos(worldPos);


                // --- 2. Calcular Normales de la Superficie ---
                float slopeX_val = tex2Dlod(_SlopeMapX, float4(v.uv, 0, 0)).r;
                float slopeZ_val = tex2Dlod(_SlopeMapZ, float4(v.uv, 0, 0)).r;

                float3 normal_unnormalized = float3(-slopeX_val, 1.0, -slopeZ_val);
                o.worldNormal = normalize(normal_unnormalized);
                
                // --- 3. Pasar datos para sombreado por altura (opcional, para debug) ---
                o.rawHeight = dispY;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // --- 1. Sombreado por Altura (DEBUG, similar a lo que ya tenías) ---
                float normalizedHeight = (i.rawHeight + (_HeightRange / 2.0)) / _HeightRange;
                normalizedHeight = saturate(normalizedHeight); 
                fixed4 interpolatedColor = lerp(_MinHeightColor, _MaxHeightColor, normalizedHeight);
                
                // --- 2. Sombreado Lambertiano Básico (usando Normales) ---

                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);

                float diff = max(0, dot(i.worldNormal, lightDir));

                fixed4 finalColor = interpolatedColor * diff * _LightColor; 

                return finalColor;
            }
            ENDCG
        }
    }
}