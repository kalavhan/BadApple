Shader "BadApple/HotelWall"
{
    Properties { [HideInInspector] _WallPeekBounds ("Peek window", Vector) = (0,0,0,0) [HideInInspector] _WallPeekEnabled ("Peek enabled", Float) = 0 _MainTex ("Hotel atlas", 2D) = "white" {} _WallStates ("Run heights and visibility", 2D) = "white" {} [HideInInspector] _WallInstance ("Instance data", Vector) = (0,0,0,0) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off ZWrite On ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma multi_compile_instancing
            // Two transform matrices per instance fit the GLES3 minimum 16KB block at 128.
            // Match WallInstances.MaxBatchCapacity for every backend and MPB array upload.
            #pragma instancing_options forcemaxcount:128
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "HotelLighting.cginc"
            sampler2D _MainTex, _HotelVision;
            sampler2D_float _WallStates; // Heights and timestamps require full precision on mobile GPUs.
            float4 _HotelSize;
            float _HotelFog, _WallClock;
            float4 _WallPeekBounds;
            float _WallPeekEnabled;
            UNITY_INSTANCING_BUFFER_START(WallProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _WallInstance)
            UNITY_INSTANCING_BUFFER_END(WallProps)
            struct appdata { UNITY_VERTEX_INPUT_INSTANCE_ID float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; float2 state:TEXCOORD1; float2 mode:TEXCOORD2; fixed4 color:COLOR; };
            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 world:TEXCOORD1; float4 rule:TEXCOORD2; float light:TEXCOORD3; float2 normal:TEXCOORD4; fixed4 color:COLOR; };
            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                float4 instance=UNITY_ACCESS_INSTANCED_PROP(WallProps,_WallInstance);
                float2 coordinates=instance.w>.5?instance.xy:v.state;
                float mode=instance.w>.5?instance.z:v.mode.x;
                float4 state=tex2Dlod(_WallStates,float4(coordinates.x,.25,0,0));
                float start=tex2Dlod(_WallStates,float4(coordinates.x,.75,0,0)).r;
                float t=saturate((_WallClock-start)/.15); t=t*t*(3-2*t);
                float height=lerp(state.r,state.g,t), fade=lerp(state.b,state.a,t);
                // Modes 5/6 are the structural base/decorative cap shown only in a peek.
                if(mode>4.5)height=.1;
                float4 world=mul(unity_ObjectToWorld,v.vertex);
                float originalHeight=.04-world.z;
                // The lintel is clipped, never squashed into the doorway in cutaway mode.
                if(mode<2.5 || mode>3.5)world.z=.04-originalHeight*height/max(.001,coordinates.y);
                if(mode>3.5 && mode<5.5)world.z+=.014; // substrate sits below the detailed moulding cap
                o.pos=mul(UNITY_MATRIX_VP,world);o.world=world.xy;
                o.uv=v.uv;o.color=v.color;o.rule=float4(height,fade,mode,originalHeight);
                float3 normal=UnityObjectToWorldNormal(v.normal);
                o.normal=normal.xy;
                o.light=.72+.28*saturate(dot(normal,normalize(float3(-.3,-.5,-1))));
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                // Sight opens at most the monster's adjacent wall cell and its two neighbours.
                // Per-pixel clipping keeps all remaining sections of a merged run intact.
                float2 edge=min(i.world-_WallPeekBounds.xy,_WallPeekBounds.zw-i.world);
                float inside=step(-.0001,min(edge.x,edge.y))*step(.5,_WallPeekEnabled);
                if(i.rule.z>4.5)clip(inside-.5);
                else clip(.5-inside);
                float threshold=frac(dot(floor(i.pos.xy),float2(.75487766,.56984029)));
                clip(i.rule.y-threshold);
                if(i.rule.z>.5 && i.rule.z<1.5) clip(i.rule.x-.46);
                if(i.rule.z>1.5 && i.rule.z<2.5) clip(.46-i.rule.x);
                if(i.rule.z>2.5 && i.rule.z<3.5) clip(i.rule.x-i.rule.w);
                float sight=tex2D(_HotelVision,(floor(i.world)+.5)/_HotelSize.xy).r;
                float fog=lerp(1,lerp(.06,1,sight),_HotelFog);
                fixed3 albedo=(i.rule.z>3.5 && i.rule.z<5.5)?i.color.rgb:tex2D(_MainTex,i.uv).rgb*i.color.rgb;
                float ambient=lerp(1,.30,saturate(_HotelLightingEnabled));
                fixed3 lamp=HotelLampLight(i.world,i.normal,lerp(1,step(.9,sight),_HotelFog));
                // Emission still obeys fog: hidden sconces cannot reveal unexplored rooms.
                return fixed4((albedo*i.light*(ambient+lamp)+fixed3(1.9,1.15,.35)*i.color.a)*fog,1);
            }
            ENDCG
        }
    }
}
