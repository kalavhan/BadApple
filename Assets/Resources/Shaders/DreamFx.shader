Shader "BadApple/DreamFx"
{
    // Every dream effect (DreamFx.cs) in one additive pass. Each quad picks a procedural
    // shape with uv1.x; uv1.y is its normalized age, uv1.z a seed and uv1.w a per-shape
    // value (level, band length). uv2 is the floor point used for the sight gate, so a
    // summon in an unseen room never gives itself away. _FxTime is the game's clock, so
    // frame-by-frame captures animate exactly as play does.
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Blend One One ZWrite Off ZTest LEqual Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "HotelSpectral.cginc"
            sampler2D _HotelVision;
            float4 _HotelSize;
            float _HotelFog, _FxTime;
            struct appdata { float4 vertex:POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float4 data:TEXCOORD1; float2 ground:TEXCOORD2; };
            struct v2f { float4 pos:SV_POSITION; fixed4 color:COLOR; float2 uv:TEXCOORD0; float4 data:TEXCOORD1; float seen:TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.data=v.data; o.color=v.color;
                float sight=tex2Dlod(_HotelVision,float4((floor(v.ground)+.5)/max(_HotelSize.xy,float2(1,1)),0,0)).r;
                o.seen=lerp(1,step(.9,sight),_HotelFog);
                return o;
            }

            float Hash(float n) { return frac(sin(n)*43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p),f=frac(p); f=f*f*(3-2*f);
                float n=i.x+i.y*57;
                return lerp(lerp(Hash(n),Hash(n+1),f.x),lerp(Hash(n+57),Hash(n+58),f.x),f.y);
            }
            float Sq(float x) { return x*x; }

            fixed4 frag(v2f i):SV_Target
            {
                int shape=(int)(i.data.x+.5);
                float age=i.data.y, seed=i.data.z, extra=i.data.w, t=_FxTime;
                float2 uv=i.uv;
                float3 tint=i.color.rgb*i.color.a;
                float3 col=0;

                if(shape==0)
                {
                    // Glow: a soft orb with a white-hot core, fading over its life.
                    float d2=dot(uv,uv);
                    float a=exp(-d2*5)*.75+exp(-d2*26)*.9;
                    col=lerp(tint,float3(1,1,1)*i.color.a,exp(-d2*40)*.45)*a*Sq(1-age)*smoothstep(1,.7,sqrt(d2));
                }
                else if(shape==1)
                {
                    // Streak: tail at uv.x=0, head at 1. Bright core narrows toward the tail.
                    float along=uv.x*uv.x;
                    float core=exp(-uv.y*uv.y*16), glow=exp(-uv.y*uv.y*3)*.35;
                    col=lerp(tint,float3(1,1,1)*i.color.a,core*.5*along)*(core+glow)*along*pow(1-age,1.2);
                }
                else if(shape==2)
                {
                    // Ring: a floor shockwave that rolls outward with a slightly ragged front.
                    float r=length(uv), ang=atan2(uv.y,uv.x);
                    float front=sqrt(age)*.88+.04*sin(ang*7+seed*9)*age;
                    float w=.05+.12*age;
                    float band=exp(-Sq((r-front)/w));
                    float fill=smoothstep(front,0,r)*.18;
                    col=lerp(SpectralMint,tint,.65)*(band+fill)*Sq(1-age)*smoothstep(1,.94,r);
                }
                else if(shape==3)
                {
                    // Sigil: the summoning circle under every creature. age draws it in, extra is the level.
                    float r=length(uv), ang=atan2(uv.y,uv.x), a01=frac(ang/6.2832+.25);
                    float draw=smoothstep(a01-.04,a01,age*1.08);
                    float breathe=.78+.22*sin(t*1.6+seed*3);
                    float spin=t*.12+seed;
                    // A thin mint-violet ring with a soft halo, like the floor seams; a fainter inner ring.
                    float outer=exp(-Sq((r-.84)/.018))*.75+exp(-Sq((r-.84)/.09))*.2;
                    float inner=exp(-Sq((r-.6)/.014))*.4+exp(-Sq((r-.6)/.06))*.08;
                    // Faint runes between the rings, each glyph flickering on its own.
                    float cellA=(ang/6.2832+spin*.5)*14, glyph=floor(cellA), f=frac(cellA);
                    float on=step(.35,Hash(glyph+seed*13))*(.55+.45*sin(t*2.3+glyph*1.7));
                    float stroke=smoothstep(.2,.26,f)*smoothstep(.62,.56,f)*smoothstep(.67,.69,r)*smoothstep(.77,.75,r);
                    float tick=exp(-Sq((f-.5)*9))*smoothstep(.64,.66,r)*smoothstep(.8,.78,r)*.5;
                    float runes=(stroke*.6+tick)*on;
                    float pips=0;
                    for(int k=0;k<4;k++)
                    {
                        if(k>=(int)(extra+.5)) break;
                        float pa=6.2832*k/max(extra,1)-spin*2;
                        float2 pp=uv-float2(cos(pa),sin(pa))*.84;
                        pips+=exp(-dot(pp,pp)*700);
                    }
                    // The creature's light pooled on the floor beneath it.
                    float pool=exp(-r*r*3.2)*.3;
                    float3 ringCol=lerp(SpectralMint,SpectralViolet,.5+.5*sin(ang*2+t*.6+seed));
                    col=(ringCol*(outer+inner)*breathe+i.color.rgb*runes*breathe*.7+i.color.rgb*pool*(.85+.15*breathe)+float3(1,1,1)*pips)*draw*i.color.a;
                    col*=smoothstep(1,.93,r);
                }
                else if(shape==4)
                {
                    // Pillar: summoning light rising from the floor, streaked by climbing wisps.
                    float core=exp(-uv.x*uv.x*14), glow=exp(-uv.x*uv.x*2.2)*.45;
                    float climb=.55+.45*Noise(float2(uv.x*3+seed,uv.y*5-t*4));
                    float fall=pow(saturate(1-uv.y),1.6)*smoothstep(0,.06,uv.y);
                    col=lerp(tint,float3(1,1,1)*i.color.a,core*.6)*(core+glow)*climb*fall*pow(1-age,1.5)*smoothstep(0,.04,age);
                }
                else if(shape==5)
                {
                    // Bolt: crackling lightning with a flickering white core.
                    float flick=.65+.35*Hash(floor(t*32)+seed);
                    float core=exp(-uv.y*uv.y*28), glow=exp(-uv.y*uv.y*2.6)*.5;
                    col=lerp(tint,float3(1,1,1)*i.color.a,core*.75)*(core+glow)*flick*pow(1-age,.8);
                }
                else if(shape==6)
                {
                    // Flame: a licking teardrop with a hot core, cooling to the tint at its edges.
                    float2 q=uv; q.y=q.y*.8+.18; q.x*=1+saturate(q.y)*1.1;
                    float n=Noise(float2(uv.x*2.6+seed,uv.y*2.4-t*5))*.6+Noise(float2(uv.x*5+seed*2,uv.y*5-t*8))*.4;
                    float body=saturate(1.15-(length(q)+(n-.5)*.7)*1.5);
                    float life=sin(3.1416*sqrt(saturate(age)));
                    col=(tint*body+float3(1,.86,.55)*i.color.a*pow(body,3)*1.4)*life;
                }
                else if(shape==7)
                {
                    // Hex: a slowing sigil of nested hexagons and turning spokes.
                    float c=cos(t*.8+seed), s=sin(t*.8+seed);
                    float2 p=float2(uv.x*c-uv.y*s,uv.x*s+uv.y*c), q=abs(p);
                    float h=max(q.x*.866+q.y*.5,q.y);
                    float rings=exp(-Sq((h-.78)/.03))+exp(-Sq((h-.5)/.025))*.7;
                    float ang=atan2(p.y,p.x);
                    float spokes=exp(-Sq(frac(ang/6.2832*6)-.5)*120)*smoothstep(.5,.55,h)*smoothstep(.78,.73,h)*.6;
                    float life=smoothstep(0,.12,age)*(1-smoothstep(.65,1,age));
                    col=lerp(tint,float3(.55,.85,.35)*i.color.a,.3+.3*sin(t*3))*(rings+spokes+exp(-h*h*4)*.2)*life*smoothstep(1,.9,h);
                }
                else
                {
                    // Thread: the dream line from a sleeper to their creature, with beads flowing outward
                    // and a brighter pulse (age >= 0) whenever the creature draws on the dream to attack.
                    float core=exp(-uv.y*uv.y*12)*.55+exp(-uv.y*uv.y*2)*.2;
                    float beads=smoothstep(.8,1,frac(uv.x*max(extra,.5)*1.2-t*.7+seed));
                    float pulse=age>=0?exp(-Sq((uv.x-age)*7))*2.2:0;
                    float ends=smoothstep(0,.08,uv.x)*smoothstep(1,.86,uv.x);
                    col=lerp(SpectralViolet,i.color.rgb,uv.x)*core*(1+beads*1.6+pulse)*ends*i.color.a;
                }
                return fixed4(max(0,col)*i.seen,1);
            }
            ENDCG
        }
    }
}
