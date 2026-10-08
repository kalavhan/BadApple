// Baked door leaf: X [0,.94] from the hinge, Y depth (front faces -Y), Z [-1.4,0].
// All animation uses the supplied gameplay clock, so pause and capture freeze the leaf.
Shader "BadApple/HotelDoor"
{
    Properties
    {
        _MainTex ("Door albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Tier ("Door design", Float) = 1
        _Phase ("Per-door phase", Float) = 0
        _Clock ("Animation clock", Float) = 0
        _Power ("Supernatural power", Range(0,1)) = 0
        _Hit ("Impact reaction", Range(0,1)) = 0
        _Pulse ("Upgrade or rebuild pulse", Range(0,1)) = 0
        _Damage ("Damage fraction", Range(0,1)) = 0
        _Visibility ("Door visible", Range(0,1)) = 1
        _DoorGround ("Closed doorway vision anchor", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off ZWrite On
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "HotelLighting.cginc"

            sampler2D _MainTex, _HotelVision;
            float4 _MainTex_ST, _Color, _HotelSize, _DoorGround;
            float _HotelFog, _Tier, _Phase, _Clock, _Power, _Hit, _Pulse;
            float _Damage, _Visibility;
            static const float3 DoorMint = float3(.30,1,.80);
            static const float3 DoorViolet = float3(.62,.38,1);

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 world : TEXCOORD1;
                float3 local : TEXCOORD2;
                float3 normal : TEXCOORD3;
                float3 worldNormal : TEXCOORD4;
                fixed4 color : COLOR;
            };

            float Window(float p, float lo, float hi, float feather)
            {
                return smoothstep(lo,lo+feather,p)*(1-smoothstep(hi-feather,hi,p));
            }
            float Stroke(float2 p, float2 a, float2 b, float width)
            {
                float2 delta=b-a;
                float along=saturate(dot(p-a,delta)/max(dot(delta,delta),.0001));
                float distanceToLine=length(p-a-delta*along);
                return 1-smoothstep(width,width+.004,distanceToLine);
            }
            float Ellipse(float2 p, float2 center, float2 radius)
            {
                return 1-smoothstep(.85,1.15,length((p-center)/radius));
            }

            v2f vert(appdata v)
            {
                v2f o;
                float2 p=saturate(float2(v.vertex.x/.94,-v.vertex.z/1.4));
                float t=_Clock+_Phase*2.37;
                float freeEdge=p.x*p.x;
                if(_Tier<1.5)
                {
                    // Occasional cardboard flutter bends only in depth; the hinge stays put.
                    float gust=pow(saturate(sin(t*.67)),8);
                    v.vertex.y+=.012*freeEdge*gust*sin(t*8.3+p.y*4);
                }
                else if(_Tier>4.5 && _Tier<5.5)
                {
                    // Six separately phased plate faces move a few millimetres in depth.
                    // Taper at every seam and perimeter: retain the opaque backing/silhouette.
                    // The imported slab seams are slightly right of centre and below thirds.
                    float2 plate=float2(p.x<.528?p.x/.528:1+(p.x-.528)/.472,
                        p.y<.35?p.y/.35:(p.y<.664?1+(p.y-.35)/.314:2+(p.y-.664)/.336));
                    float2 cell=floor(min(plate,float2(1.999,2.999)));
                    float2 within=frac(plate);
                    float interior=sin(within.x*UNITY_PI)*sin(within.y*UNITY_PI);
                    float fixedBorder=Window(p.x,.06,.96,.09)*Window(p.y,.025,.98,.045);
                    float front=smoothstep(.05,.6,-v.normal.y);
                    float phase=cell.x*2.1+cell.y*1.43;
                    v.vertex.y+=.008*interior*fixedBorder*front*_Power*sin(t*.9+phase);
                }
                // Impact flex is physical, even on the three ordinary doors; it fixes the hinge.
                float flex=(_Tier<1.5?.012:.004)*saturate(_Hit);
                v.vertex.y+=flex*freeEdge*sin(t*31+p.y*4.5);
                o.pos=UnityObjectToClipPos(v.vertex);
                o.uv=TRANSFORM_TEX(v.uv,_MainTex);
                o.world=mul(unity_ObjectToWorld,v.vertex).xy;
                o.local=v.vertex.xyz;
                o.normal=v.normal;
                o.worldNormal=UnityObjectToWorldNormal(v.normal);
                o.color=v.color;
                return o;
            }

            float3 DoorEnergy(float2 p, float3 albedo, float front, float t)
            {
                float luma=dot(albedo,float3(.299,.587,.114));
                float chroma=max(albedo.r,max(albedo.g,albedo.b))-min(albedo.r,min(albedo.g,albedo.b));
                float neutral=1-smoothstep(.10,.29,chroma);
                float breathe=.72+.18*sin(t*1.13);
                float impact=saturate(_Hit);
                float pulse=saturate(_Pulse);
                float3 energy=0;
                if(_Tier>3.5 && _Tier<4.5)
                {
                    // The chalk is brighter and less saturated than the walnut and brass.
                    // Albedo gating preserves the actual imported runes, without painting the wood.
                    // Fit the baked circle, including the left hinge in the leaf's X bounds.
                    float2 seal=(p-float2(.527,.644))/float2(.391,.206);
                    float radius=length(seal);
                    float inSeal=1-smoothstep(.94,1.07,radius);
                    float chalk=neutral*smoothstep(.32,.57,luma)*inSeal;
                    // Narrow light follows the inset tracks; the raised brass stays brass.
                    float ring=max(exp(-abs(radius-.97)*150),exp(-abs(radius-.35)*220));
                    ring*=1-smoothstep(.47,.68,luma);
                    float traveling=exp(-abs(radius-(1-impact)*1.2)*22)*impact;
                    float trace=.75+.25*sin(atan2(seal.y,seal.x)*3-t*.8);
                    energy=DoorMint*chalk*(.68*breathe+.50*traveling+.35*pulse);
                    energy+=(DoorMint*.27+DoorViolet*.09)*ring*trace*(breathe+.75*impact+.5*pulse);
                }
                else if(_Tier>4.5 && _Tier<5.5)
                {
                    float vertical=exp(-abs(p.x-.528)*145);
                    float horizontal=exp(-min(abs(p.y-.35),abs(p.y-.664))*185);
                    float seams=max(vertical,horizontal);
                    float inside=Window(p.x,.06,.96,.035)*Window(p.y,.035,.97,.025);
                    float darkSeam=1-smoothstep(.23,.46,luma);
                    float flow=.7+.3*sin(p.y*19-p.x*13-t*1.8);
                    // Restrict the pale-stone test to the three authored inlays. Bright chips
                    // elsewhere on the slab must remain ordinary stone when power comes on.
                    float upperRunes=max(Window(p.x,.19,.345,.015),Window(p.x,.72,.90,.015))
                        *Window(p.y,.72,.93,.015);
                    float diamond=max(max(Stroke(p,float2(.527,.674),float2(.262,.519),.035),
                        Stroke(p,float2(.262,.519),float2(.527,.35),.035)),
                        max(Stroke(p,float2(.527,.35),float2(.805,.519),.035),
                        Stroke(p,float2(.805,.519),float2(.527,.674),.035)));
                    float arrow=Window(p.x,.445,.595,.02)*Window(p.y,.37,.635,.015);
                    float inlay=neutral*smoothstep(.38,.61,luma)*max(upperRunes,max(diamond,arrow))*inside;
                    energy=(DoorMint*.48+DoorViolet*.14)*seams*inside*darkSeam*flow*(breathe+impact+pulse*.6);
                    energy+=DoorMint*inlay*(.61*breathe+.35*impact+.27*pulse);
                }
                else if(_Tier>5.5 && _Tier<6.5)
                {
                    // Opaque silver with slow abstract light below its surface, never a live reflection.
                    float glass=Window(p.x,.285,.755,.018)*Window(p.y,.19,.873,.025);
                    float silver=glass*neutral*smoothstep(.18,.48,luma);
                    float mintPath=.445+.026*sin(p.y*14-t*.52)+.017*sin(p.y*6+t*.2);
                    float violetPath=.595+.023*sin(p.y*12-t*.47+1.7);
                    float mintRibbon=exp(-pow((p.x-mintPath)/.045,2));
                    float violetRibbon=exp(-pow((p.x-violetPath)/.042,2));
                    float shimmer=.78+.22*sin(p.y*15-t*.7);
                    // The export retains an upper-right fork and short lower-left fractures.
                    // Follow those painted paths; no arbitrary diagonal across intact glass.
                    float crack=max(max(Stroke(p,float2(.346,.721),float2(.505,.76),.006),
                        Stroke(p,float2(.505,.76),float2(.68,.824),.006)),
                        max(Stroke(p,float2(.68,.824),float2(.62,.757),.008),
                        Stroke(p,float2(.62,.757),float2(.55,.705),.006)));
                    crack=max(crack,max(Stroke(p,float2(.55,.705),float2(.345,.598),.006),
                        Stroke(p,float2(.295,.227),float2(.359,.276),.005)));
                    float crackTone=1-smoothstep(.28,.59,luma);
                    float radius=length((p-float2(.5,.55))*float2(1,1.3));
                    float ripple=exp(-abs(radius-(1-impact)*.7)*65)*impact;
                    energy=(DoorMint*mintRibbon*.12+DoorViolet*violetRibbon*.075)*silver*shimmer*breathe;
                    energy+=DoorMint*silver*(.12*ripple+.055*pulse);
                    energy+=(DoorMint*.46+DoorViolet*.10)*glass*neutral*crack*crackTone*(breathe+.65*impact+.4*pulse);
                }
                else if(_Tier>6.5)
                {
                    // Only cracks and narrow carving channels carry energy; ivory remains hotel-lit.
                    float forehead=max(max(Stroke(p,float2(.344,.697),float2(.414,.739),.009),
                        Stroke(p,float2(.414,.739),float2(.655,.816),.009)),
                        max(Stroke(p,float2(.655,.816),float2(.74,.872),.009),
                        Stroke(p,float2(.655,.816),float2(.652,.781),.006)));
                    // Three actual recessed flutes carry narrow rising threads, not four
                    // evenly spaced stripes on the ivory columns.
                    float drift=.006*sin(p.y*22-t*.75);
                    float channel=max(max(exp(-abs(p.x-(.207+drift))*100)*Window(p.y,.09,.32,.035),
                        exp(-abs(p.x-(.523+drift))*110)*Window(p.y,.09,.29,.03)),
                        exp(-abs(p.x-(.838+drift))*95)*Window(p.y,.09,.34,.035));
                    float jaw=max(max(Stroke(p,float2(.252,.535),float2(.311,.425),.015),
                        Stroke(p,float2(.311,.425),float2(.401,.363),.015)),
                        max(Stroke(p,float2(.776,.529),float2(.730,.423),.015),
                        Stroke(p,float2(.730,.423),float2(.655,.371),.015)));
                    float toothSeam=exp(-abs(p.y-.431)*190)*Window(p.x,.36,.715,.025);
                    float recess=1-smoothstep(.26,.53,luma);
                    // A generous keep-dark mask protects both sockets and the nose, including rims.
                    float eyes=max(Ellipse(p,float2(.344,.644),float2(.14,.055)),
                        Ellipse(p,float2(.695,.644),float2(.14,.055)));
                    float nose=Ellipse(p,float2(.524,.552),float2(.075,.065));
                    float keepDark=1-saturate(max(eyes,nose));
                    float breath=.6+.4*sin(t*.8+p.x*7);
                    float flow=.58+.42*pow(.5+.5*sin(p.y*26-t*1.35+p.x*17),2);
                    float mask=saturate(forehead*.75+channel*flow+jaw*.55+toothSeam*.10)*recess*keepDark;
                    energy=(DoorMint*.53+DoorViolet*.15)*mask*(breathe+impact*.9+pulse*.55);
                    energy+=DoorViolet*toothSeam*recess*keepDark*.045*pow(saturate(breath),5);
                }
                // Reverse faces remain quiet; the front-normal mask also keeps edges from becoming a halo.
                float damageFlicker=1-saturate(_Damage)*.18*(.5+.5*sin(t*7.7));
                return energy*front*saturate(_Power)*damageFlicker;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                clip(_Visibility-.001);
                // Sample the logical doorway rather than the moving leaf, which can swing into fog.
                float2 visionUv=(floor(_DoorGround.xy)+.5)/max(_HotelSize.xy,float2(1,1));
                float sight=tex2D(_HotelVision,visionUv).r;
                float visible=lerp(1,step(.9,sight),saturate(_HotelFog));
                float light=lerp(1,lerp(.06,1,sight),saturate(_HotelFog));
                fixed4 texel=tex2D(_MainTex,i.uv);
                fixed3 baseColor=texel.rgb*_Color.rgb*i.color.rgb;
                float ambient=lerp(1,.30,saturate(_HotelLightingEnabled));
                fixed3 lamp=HotelLampLight(i.world,i.worldNormal.xy,visible);
                float2 p=saturate(float2(i.local.x/.94,-i.local.z/1.4));
                float front=smoothstep(.05,.65,-normalize(i.normal).y);
                float t=_Clock+_Phase*2.37;
                float3 emission=DoorEnergy(p,texel.rgb,front,t);
                if(_Tier>2.5 && _Tier<3.5)
                {
                    // A tiny impact glint at the latch on the otherwise nonmagical hotel door.
                    float latch=Ellipse(p,float2(.83,.48),float2(.08,.065));
                    float brass=saturate((texel.r-texel.b)*4)*smoothstep(.3,.65,texel.r);
                    emission+=float3(1,.72,.32)*latch*brass*front*saturate(_Hit)*.25;
                }
                return fixed4(baseColor*(ambient+lamp)*light+emission*visible,1);
            }
            ENDCG
        }
    }
}
