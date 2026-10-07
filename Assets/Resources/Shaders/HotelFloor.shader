Shader "BadApple/HotelFloor"
{
    Properties
    {
        _WoodA ("Walnut A", 2D) = "gray" {}
        _WoodB ("Walnut B", 2D) = "gray" {}
        _Carpet ("Runner field", 2D) = "gray" {}
        _Binding ("Runner binding", 2D) = "gray" {}
        _WoodTiles ("Tiles per wood repeat", Float) = 4
        _CarpetTiles ("Tiles per carpet repeat", Float) = 3
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
            #include "HotelSpectral.cginc"
            sampler2D _WoodA, _WoodB, _Carpet, _Binding, _FloorCells, _HotelVision;
            float4 _HotelSize;
            float _HotelFog, _WoodTiles, _CarpetTiles;
            struct appdata { float4 vertex:POSITION; fixed4 color:COLOR; };
            // color: r finish, g plank direction, b zone seed, a room-owned (may carry energy).
            struct v2f { float4 pos:SV_POSITION; float2 world:TEXCOORD0; fixed4 zone:COLOR; };
            v2f vert(appdata v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.world=mul(unity_ObjectToWorld,v.vertex).xy; o.zone=v.color; return o; }

            float Corridor(float2 cell) { return tex2Dlod(_FloorCells,float4((cell+.5)/_HotelSize.xy,0,0)).r; }
            float Noise(float2 p)
            {
                float2 i=floor(p),f=frac(p); f=f*f*(3-2*f);
                float a=frac(sin(dot(i,float2(127.1,311.7)))*43758.5),b=frac(sin(dot(i+float2(1,0),float2(127.1,311.7)))*43758.5);
                float c=frac(sin(dot(i+float2(0,1),float2(127.1,311.7)))*43758.5),d=frac(sin(dot(i+float2(1,1),float2(127.1,311.7)))*43758.5);
                return lerp(lerp(a,b,f.x),lerp(c,d,f.x),f.y);
            }

            fixed4 frag(v2f i):SV_Target
            {
                float2 p=i.world;
                // Wood is mapped continuously in world space; each room keeps one board direction and offset.
                float2 w=i.zone.g>.5?p.yx:p;
                float2 uv=w/_WoodTiles+i.zone.b*float2(7.31,3.17);
                fixed3 c=lerp(tex2D(_WoodA,uv).rgb,tex2D(_WoodB,uv).rgb,step(.5,i.zone.r));
                // Broad, cell-independent wear: traffic sheen and darker patches that never stamp per square.
                float wear=Noise(p*.23+i.zone.b*13)*.6+Noise(p*.71)*.4;
                c*=lerp(.86,1.08,wear);

                // Corridor runner: the carpet stops short of walls and door thresholds, bound by a woven strip.
                float2 cell=floor(p),f=p-cell;
                if(Corridor(cell)>.5)
                {
                    float edge=9,along=p.x;
                    if(Corridor(cell+float2(-1,0))<.5&&f.x<edge){edge=f.x;along=p.y;}
                    if(Corridor(cell+float2(1,0))<.5&&1-f.x<edge){edge=1-f.x;along=p.y;}
                    if(Corridor(cell+float2(0,-1))<.5&&f.y<edge){edge=f.y;along=p.x;}
                    if(Corridor(cell+float2(0,1))<.5&&1-f.y<edge){edge=1-f.y;along=p.x;}
                    // Clipped inside corners where only the diagonal square is missing.
                    for(int k=0;k<4;k++)
                    {
                        float2 s=float2(k&1?1:-1,k&2?1:-1);
                        if(Corridor(cell+s)<.5)
                        {
                            float2 q=s>0?1-f:f;
                            float chamfer=(q.x+q.y)*.7071+.035;
                            if(chamfer<edge){edge=chamfer;along=(p.x+p.y)*.7071;}
                        }
                    }
                    const float inset=.06,band=.13;
                    if(edge>inset)
                    {
                        if(edge<inset+band) c=tex2D(_Binding,float2(along/(band*4),(edge-inset)/band)).rgb;
                        else c=tex2D(_Carpet,p/_CarpetTiles).rgb*lerp(.9,1.04,Noise(p*.4));
                        // A thin shadow seats the runner on the boards.
                        c*=lerp(.62,1,saturate((edge-inset)/.02));
                    }
                    else c*=lerp(1,.7,saturate((edge-inset+.03)/.03));
                }

                float sight=tex2D(_HotelVision,(cell+.5)/_HotelSize.xy).r;
                float seen=lerp(1,step(.9,sight),_HotelFog);
                float ambient=lerp(1,.30,saturate(_HotelLightingEnabled));
                fixed3 lamp=HotelLampLight(p,float2(0,0),seen);
                fixed3 lit=c*(ambient+lamp)*lerp(1,lerp(.06,1,sight),_HotelFog);
                // Spectral energy is added after lighting and only where the square is currently seen.
                if(i.zone.a>.5) lit+=SpectralFloor(p,cell,f)*seen;
                return fixed4(lit,1);
            }
            ENDCG
        }
    }
}
