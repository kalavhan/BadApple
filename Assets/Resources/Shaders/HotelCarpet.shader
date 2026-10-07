Shader "BadApple/HotelCarpet"
{
    // Bed rugs: a woven fabric field in world space, framed by the antique binding.
    Properties
    {
        _FieldA ("Burgundy field", 2D) = "gray" {}
        _FieldB ("Charcoal field", 2D) = "gray" {}
        _Binding ("Binding", 2D) = "gray" {}
        _Size ("Rug size in tiles", Vector) = (.94,1.88,0,0)
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
            sampler2D _HotelVision, _FieldA, _FieldB, _Binding;
            float4 _HotelSize, _Size;
            float _HotelFog;
            struct a { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct v { float4 pos:SV_POSITION; float2 world:TEXCOORD0; float2 uv:TEXCOORD1; fixed4 color:COLOR; };
            v vert(a i) { v o; o.pos=UnityObjectToClipPos(i.vertex); o.world=mul(unity_ObjectToWorld,i.vertex).xy; o.uv=i.uv; o.color=i.color; return o; }
            fixed4 frag(v i):SV_Target
            {
                // Distances to the rug edge in tiles, so the binding keeps its width on both axes.
                float2 d=min(i.uv,1-i.uv)*_Size.xy;
                float border=min(d.x,d.y);
                const float band=.11;
                fixed3 field=lerp(tex2D(_FieldA,i.world/2.4).rgb,tex2D(_FieldB,i.world/2.4).rgb,step(.5,i.color.r));
                float along=d.x<d.y?i.uv.y*_Size.y:i.uv.x*_Size.x;
                fixed3 c=border<band?tex2D(_Binding,float2(along/(band*4),border/band)).rgb:field;
                c*=lerp(.7,1,saturate(border/.015));
                float sight=tex2D(_HotelVision,(floor(i.world)+.5)/_HotelSize.xy).r;
                float ambient=lerp(1,.25,saturate(_HotelLightingEnabled));
                fixed3 lamp=HotelLampLight(i.world,float2(0,0),lerp(1,step(.9,sight),_HotelFog));
                return fixed4(c*(ambient+lamp)*lerp(1,lerp(.06,1,sight),_HotelFog),1);
            }
            ENDCG
        }
    }
}
