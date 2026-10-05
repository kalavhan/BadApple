Shader "BadApple/ContactShadow"
{
    Properties { _Opacity ("Opacity", Range(0,1)) = 1 }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _HotelVision;
            float4 _HotelSize;
            float _HotelFog, _Opacity;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 world:TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float radius = length(i.uv * 2 - 1);
                float alpha = (1 - smoothstep(.15, 1, radius)) * .35 * _Opacity;
                float sight = tex2D(_HotelVision, (floor(i.world) + .5) / max(_HotelSize.xy, 1)).r;
                alpha *= lerp(1, sight, _HotelFog);
                return fixed4(.025, .012, .015, alpha);
            }
            ENDCG
        }
    }
}
