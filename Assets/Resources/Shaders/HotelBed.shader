// HotelSurface lighting plus a small idle motion for the baked Tripo beds.
// Object space: XY footprint centered on the pivot, head toward +Y, negative Z up.
//  * _Motion 1: paper corner flutter. The four free corners rise and fall in short bursts.
//  * _Motion 2: cardboard flap. The foot-end flap occasionally lifts and settles.
//  * _Motion 3: levitating bed. The whole bed hovers .14 tiles up with a slow bob.
// The middle sleeping surface stays still, so the resident never floats off it.
Shader "BadApple/HotelBed"
{
    Properties
    {
        _MainTex ("Pixel texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Motion ("Idle motion (0 none, 1 corners, 2 flap, 3 float)", Float) = 0
        _Amount ("Lift in tiles", Float) = .06
        _Phase ("Per-bed phase", Float) = 0
        _HalfSize ("Footprint half size", Vector) = (.47,.95,0,0)
    }
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
            float4 _MainTex_ST, _Color, _HotelSize, _HalfSize;
            float _HotelFog, _Motion, _Amount, _Phase;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 world:TEXCOORD1; fixed4 color:COLOR; };

            float IdleLift(float3 p)
            {
                float t=_Time.y+_Phase*2.37;
                float2 edge=abs(p.xy)/_HalfSize.xy;
                if(_Motion>2.5)
                {
                    // Levitating bed: the whole frame hovers with a slow bob (lift in tiles, not a weight).
                    return (1+.25*sin(t*1.3))/max(_Amount,.001)*.14;
                }
                if(_Motion>1.5)
                {
                    // Foot-end flap: hinged weight grows toward the end; a brief lift every few seconds.
                    float w=saturate((-p.y/_HalfSize.y-.7)/.3); w*=w;
                    float pulse=pow(saturate(sin(t*.85)),6);
                    return w*pulse*(1+.15*sin(t*7));
                }
                if(_Motion>.5)
                {
                    // Corners only: both axes must be near an edge. Bursts of quick flutter.
                    float w=saturate((edge.x-.55)/.45)*saturate((edge.y-.7)/.3);
                    float gust=smoothstep(.2,.9,sin(t*.6)*.5+.5);
                    float side=sign(p.x)*sign(p.y);
                    return w*gust*(.55+.45*sin(t*9+side*1.7));
                }
                return 0;
            }
            v2f vert(appdata v)
            {
                v2f o;
                v.vertex.z-=_Amount*IdleLift(v.vertex.xyz);
                o.pos=UnityObjectToClipPos(v.vertex); o.uv=TRANSFORM_TEX(v.uv,_MainTex);
                o.world=mul(unity_ObjectToWorld,v.vertex).xy; o.color=v.color; return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float sight=tex2D(_HotelVision,(floor(i.world)+0.5)/_HotelSize.xy).r;
                float light=lerp(1,lerp(0.06,1,sight),_HotelFog);
                fixed4 c=tex2D(_MainTex,i.uv)*_Color*i.color;
                float ambient=lerp(1,.30,saturate(_HotelLightingEnabled));
                fixed3 lamp=HotelLampLight(i.world,float2(0,0),lerp(1,step(.9,sight),_HotelFog));
                return fixed4(c.rgb*(ambient+lamp)*light,1);
            }
            ENDCG
        }
    }
}
