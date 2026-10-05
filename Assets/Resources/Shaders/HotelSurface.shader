Shader "BadApple/HotelSurface"
{
    Properties { _MainTex ("Pixel texture", 2D) = "white" {} _Color ("Tint", Color) = (1,1,1,1) _Fade ("Wall visibility", Range(0,1)) = 1 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off ZWrite On
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "HotelLighting.cginc"
            sampler2D _MainTex, _HotelVision;
            float4 _MainTex_ST, _Color, _HotelSize;
            float _Fade, _HotelFog;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 world:TEXCOORD1; fixed4 color:COLOR; };
            v2f vert(appdata v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=TRANSFORM_TEX(v.uv,_MainTex); o.world=mul(unity_ObjectToWorld,v.vertex).xy; o.color=v.color; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                // Ordered screen-door transparency keeps depth testing correct for sprites behind a faded wall.
                float threshold=frac(dot(floor(i.pos.xy),float2(0.75487766,0.56984029)));
                clip(_Fade-threshold);
                float sight=tex2D(_HotelVision,(floor(i.world)+0.5)/_HotelSize.xy).r;
                float light=lerp(1,lerp(0.06,1,sight),_HotelFog);
                fixed4 c=tex2D(_MainTex,i.uv)*_Color*i.color;
                float ambient=lerp(1,.45,saturate(_HotelLightingEnabled));
                fixed3 lamp=HotelLampLight(i.world,float2(0,0),lerp(1,step(.9,sight),_HotelFog));
                return fixed4(c.rgb*(ambient+lamp)*light,1);
            }
            ENDCG
        }
    }
}
