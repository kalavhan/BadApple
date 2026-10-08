Shader "BadApple/HotelDoorMat"
{
    Properties
    {
        _Color ("Room status", Color) = (1,.76,.16,1)
        _Intensity ("Glow strength", Range(0,1)) = 1
        _Visibility ("Door visible", Range(0,1)) = 1
        _Clock ("Animation clock", Float) = 0
        _Phase ("Room phase", Float) = 0
        _DoorGround ("Doorway vision anchor", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-6" "IgnoreProjector"="True" }
        Blend One One ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _HotelVision;
            float4 _HotelSize, _DoorGround, _Color;
            float _HotelFog, _Clock, _Phase, _Intensity, _Visibility;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o;
            }
            float RoundedBox(float2 p,float2 bounds,float radius)
            {
                float2 q=abs(p)-bounds+radius;
                return length(max(q,0))+min(max(q.x,q.y),0)-radius;
            }
            fixed4 frag(v2f i):SV_Target
            {
                clip(_Visibility-.001);
                float sight=tex2D(_HotelVision,(floor(_DoorGround.xy)+.5)/max(_HotelSize.xy,float2(1,1))).r;
                float seen=lerp(1,step(.9,sight),saturate(_HotelFog));
                float t=_Clock, phase=_Phase;
                float d=RoundedBox(i.uv,float2(.80,.70),.12);
                float edge=abs(d);
                float breathe=.86+.14*sin(t*1.6+phase);
                float flow=.78+.22*sin(i.uv.x*9+i.uv.y*6-t*2.1+phase);
                float border=exp(-edge*48)*flow;
                float halo=exp(-edge*12)*.22;
                float fill=(1-smoothstep(-.06,0,d))*(.085+.025*sin(i.uv.y*10-t*1.1+phase));
                // A second quiet inset line makes the rectangular pool read like a doormat.
                float inset=exp(-abs(RoundedBox(i.uv,float2(.65,.52),.08))*65)*.17;
                float boundsFade=1-smoothstep(.86,1,max(abs(i.uv.x),abs(i.uv.y)));
                float glow=(border*.78+halo+fill+inset)*breathe*boundsFade*_Intensity*seen;
                return fixed4(max(0,_Color.rgb*glow),1);
            }
            ENDCG
        }
    }
}
