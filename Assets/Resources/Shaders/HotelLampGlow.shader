Shader "BadApple/HotelLampGlow"
{
    Properties { _WallStates ("Wall states",2D)="white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha One Cull Off ZWrite Off ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D_float _WallStates;
            sampler2D _HotelVision;
            float4 _HotelSize, _WallPeekBounds;
            float _HotelFog, _WallClock, _WallPeekEnabled;
            struct appdata { float4 vertex:POSITION;float2 uv:TEXCOORD0;float2 state:TEXCOORD1;float2 ground:TEXCOORD2; };
            struct v2f { float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float2 ground:TEXCOORD1;float2 rule:TEXCOORD2; };
            v2f vert(appdata v)
            {
                v2f o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.ground=v.ground;
                float4 state=tex2Dlod(_WallStates,float4(v.state.x,.25,0,0));
                float started=tex2Dlod(_WallStates,float4(v.state.x,.75,0,0)).r;
                float t=saturate((_WallClock-started)/.15);t=t*t*(3-2*t);
                o.rule=float2(lerp(state.r,state.g,t),lerp(state.b,state.a,t));return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                // A hidden/lowered/peeked-away lamp must not leave a floating light halo.
                clip(i.rule.x-1.65);
                if(_WallPeekEnabled>.5)
                {
                    float2 edge=min(i.ground-_WallPeekBounds.xy,_WallPeekBounds.zw-i.ground);
                    clip(-min(edge.x,edge.y)-.0001);
                }
                float sight=tex2D(_HotelVision,(floor(i.ground)+.5)/_HotelSize.xy).r;
                float visible=lerp(1,step(.9,sight),_HotelFog);
                float radius=dot(i.uv,i.uv);clip(1-radius);
                float halo=pow(1-radius,3)*.42+exp(-radius*35)*.35;
                return fixed4(1,.64,.23,halo*i.rule.y*visible);
            }
            ENDCG
        }
    }
}
