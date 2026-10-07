Shader "BadApple/HotelSpectralPlus"
{
    // A camera-facing spectral plus hovering over each legal empty build square of the
    // local guest's room. It bobs, breathes and shimmers; cell energy (g) switches it on.
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-5" "IgnoreProjector"="True" }
        Blend One One ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "HotelSpectral.cginc"
            sampler2D _HotelVision;
            float4 _HotelSize;
            float _HotelFog;
            // uv: corner in -1..1; uv1: cell; uv2/uv3: billboard half-axes, for the breathing scale.
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float2 cell:TEXCOORD1; float3 right:TEXCOORD2; float3 up:TEXCOORD3; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 cell:TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                float t=_Time.y, phase=v.cell.x*1.7+v.cell.y*2.3;
                float scale=1+.08*sin(t*2.6+phase);
                v.vertex.xyz+=(v.right*v.uv.x+v.up*v.uv.y)*(scale-1);
                v.vertex.z-=.06*sin(t*2.2+phase); // negative z is up
                o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.cell=v.cell; return o;
            }
            float Box(float2 q,float2 b){float2 d=q-b;return length(max(d,0))+min(max(d.x,d.y),0);}
            fixed4 frag(v2f i):SV_Target
            {
                float4 s=SpectralCell(i.cell);
                float sight=tex2D(_HotelVision,(i.cell+.5)/_HotelSize.xy).r;
                float seen=lerp(1,step(.9,sight),_HotelFog);
                float t=_Time.y;
                float2 q=abs(i.uv);
                float d=min(Box(q,float2(.62,.13)),Box(q,float2(.13,.62)));
                // A shimmer runs outward along the arms; the halo pulses.
                float shimmer=.75+.25*sin(length(i.uv)*7-t*4.5);
                float core=smoothstep(.06,0,d)*shimmer;
                float halo=exp(-max(d,0)*7)*(.35+.15*sin(t*2.2+i.cell.x+i.cell.y))*smoothstep(1,.7,max(q.x,q.y));
                fixed3 col=SpectralMint*core*.9+lerp(SpectralMint,SpectralViolet,saturate(d*2.5))*halo*.45;
                return fixed4(max(0,col*s.g*(1+s.b*.7)*seen),1);
            }
            ENDCG
        }
    }
}
