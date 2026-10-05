Shader "BadApple/HotelCarpet"
{
    Properties { _Rug ("Bed rug", Float)=0 }
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
            sampler2D _HotelVision;
            float4 _HotelSize;
            float _HotelFog, _Rug;
            struct a { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v { float4 pos:SV_POSITION; float2 world:TEXCOORD0; float2 uv:TEXCOORD1; };
            v vert(a i) { v o; o.pos=UnityObjectToClipPos(i.vertex); o.world=mul(unity_ObjectToWorld,i.vertex).xy; o.uv=i.uv; return o; }
            fixed4 frag(v i):SV_Target
            {
                float2 p=floor(i.world*80)/80;
                // Offset rows of nested hexagons: a seamless, muted 1980s carpet weave.
                float2 q=p*2; q.x+=floor(q.y/.8660254)*.5;
                float2 h=abs(float2(frac(q.x)-.5,frac(q.y/.8660254)*.8660254-.4330127));
                float d=max(h.x*.8660254+h.y*.5,h.y);
                fixed3 c=d>.38?fixed3(.17,.065,.045):d>.30?fixed3(.52,.245,.115):d>.245?fixed3(.24,.055,.055):d>.12?fixed3(.38,.12,.07):fixed3(.60,.32,.14);
                if(_Rug>.5)
                {
                    float2 edge=min(i.uv,1-i.uv); float border=min(edge.x,edge.y);
                    c=border<.035?fixed3(.25,.105,.035):border<.085?fixed3(.63,.40,.17):border<.11?fixed3(.18,.055,.055):fixed3(.30,.09,.095);
                    if(border>.12) c+=step(.89,frac((i.uv.x+i.uv.y)*12))*fixed3(.12,.075,.025);
                }
                float weave=.91+.09*frac(dot(floor(p*80),float2(.7549,.5698)));
                float sight=tex2D(_HotelVision,(floor(i.world)+.5)/_HotelSize.xy).r;
                float ambient=lerp(1,.45,saturate(_HotelLightingEnabled));
                fixed3 lamp=HotelLampLight(i.world,float2(0,0),lerp(1,step(.9,sight),_HotelFog));
                return fixed4(c*weave*(ambient+lamp)*lerp(1,lerp(.06,1,sight),_HotelFog),1);
            }
            ENDCG
        }
    }
}
