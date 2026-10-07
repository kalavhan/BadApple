Shader "BadApple/HotelSpectralRibbon"
{
    // Aurora ribbons rising from a claimed room's floor seams. Additive, depth tested
    // against walls, and gated by sight so hidden rooms never show their energy.
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "IgnoreProjector"="True" }
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
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float2 cell:TEXCOORD1; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 cell:TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                float t=_Time.y, h=saturate(v.uv.y);
                // Tops sway and the whole curtain breathes up and down a little.
                v.vertex.xy+=float2(sin(v.uv.x*3.1+t*1.3),cos(v.uv.x*2.3+t*1.1))*.05*h;
                v.vertex.z-=(.5+.5*sin(v.uv.x*1.7+t*.9))*.12*h;
                o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.cell=v.cell; return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float4 s=SpectralCell(i.cell);
                float sight=tex2D(_HotelVision,(i.cell+.5)/_HotelSize.xy).r;
                float seen=lerp(1,step(.9,sight),_HotelFog);
                float t=_Time.y, h=saturate(i.uv.y), r=1-h;
                // Curtain folds drift sideways while bright wisps climb from the floor.
                float folds=.5+.5*sin(i.uv.x*7.3-t*1.9+h*4)*sin(i.uv.x*2.3+t*.8);
                float wisps=smoothstep(.75,1,frac(h*1.4-t*.55+sin(i.uv.x*4.1)*.5));
                float rise=r*r*smoothstep(0,.1,h+.03);
                fixed3 col=lerp(SpectralMint,SpectralViolet,h);
                return fixed4(max(0,col*rise*(folds*.7+wisps*1.1)*s.r*(1+s.b)*.5*seen),1);
            }
            ENDCG
        }
    }
}
