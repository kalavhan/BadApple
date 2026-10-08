// Alpha-clipped world sprites share the wall depth buffer, including characters, doors and towers.
//  * Shirt recolour: pixels in the magenta band (every character wears a magenta shirt) are hue-shifted to _ShirtHue,
//    so each resident gets their own shirt colour from one set of art. _ShirtHue < 0 turns it off (bosses).
//  * Hit flash: _Flash blends the sprite toward _FlashColor (damage, healing, spawn).
//  * Dream summons: _Materialize < 1 dissolves the sprite from the floor up behind a glowing
//    front, and _DreamGlow lights its base in _DreamColor, as if it still rose out of the dream.
//    _Breathe (x amplitude, y phase, z rate, w recoil) gives still creature art a slow breath and
//    sway, and a short squash when it attacks. All of these default to off.
Shader "BadApple/CharacterSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ShirtHue ("Shirt hue (-1 = off)", Float) = -1
        _ShirtSat ("Shirt saturation", Float) = 1
        _ShirtVal ("Shirt value", Float) = 1
        _Flash ("Flash", Range(0,1)) = 0
        _FlashColor ("Flash colour", Color) = (1,1,1,1)
        _Materialize ("Materialize", Range(0,1)) = 1
        _DreamGlow ("Dream glow", Float) = 0
        _DreamColor ("Dream colour", Color) = (.3,1,.8,1)
        _Breathe ("Breathe (amp, phase, rate, recoil)", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "IgnoreProjector"="True" "RenderType"="TransparentCutout" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        Lighting Off
        ZWrite On
        ZTest LEqual
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "HotelLighting.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float2 ground : TEXCOORD1; float height : TEXCOORD2; };

            sampler2D _MainTex;
            fixed4 _Color;
            float _ShirtHue, _ShirtSat, _ShirtVal, _Flash, _Materialize, _DreamGlow;
            fixed4 _FlashColor, _DreamColor;
            float4 _Breathe;
            float _FxTime;

            v2f vert(appdata v)
            {
                v2f o;
                float4 world = mul(unity_ObjectToWorld, v.vertex);
                if (_Breathe.x > 0 || _Breathe.w > 0)
                {
                    // Height above the floor (negative z is up) scales the motion, so feet stay planted.
                    float h = max(0, -world.z), b = sin(_FxTime * _Breathe.z + _Breathe.y);
                    world.z -= h * (_Breathe.x * b - _Breathe.w * .09);
                    world.xy += float2(.7071, -.7071) * h * h * _Breathe.x * .35 * cos(_FxTime * _Breathe.z * .5 + _Breathe.y);
                }
                o.pos = mul(UNITY_MATRIX_VP, world);
                o.uv = v.uv;
                o.color = v.color * _Color;
                o.ground = mul(unity_ObjectToWorld,float4(0,0,0,1)).xy;
                o.height = -world.z;
                return o;
            }

            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 hsv2rgb(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                // Texture and renderer tint both participate in clipping. A fully faded sprite
                // must not leave invisible depth behind, while cloak alpha keeps its soft blend.
                clip(min(t.a - 0.08, t.a * i.color.a - 0.01));
                float3 rgb = t.rgb;
                if (_ShirtHue >= 0.0)
                {
                    float3 hsv = rgb2hsv(rgb);
                    float w = smoothstep(0.74, 0.79, hsv.x) * (1.0 - smoothstep(0.93, 0.97, hsv.x))
                            * smoothstep(0.22, 0.38, hsv.y) * smoothstep(0.08, 0.20, hsv.z);
                    hsv.x = lerp(hsv.x, _ShirtHue, w);
                    hsv.y = saturate(hsv.y * lerp(1.0, _ShirtSat, w));
                    hsv.z = saturate(hsv.z * lerp(1.0, _ShirtVal, w));
                    rgb = hsv2rgb(hsv);
                }
                rgb = lerp(rgb, _FlashColor.rgb, _Flash);
                float3 dream = 0;
                if (_Materialize < .999)
                {
                    // A noisy front climbs from the floor; above it the creature is still only dream.
                    // Smooth value noise on the sprite keeps the front wispy rather than pixel-grainy.
                    float2 g = i.uv * 40.0, gi = floor(g), gf = frac(g); gf = gf * gf * (3 - 2 * gf);
                    float n00 = frac(sin(dot(gi, float2(12.9898, 78.233))) * 43758.5453);
                    float n10 = frac(sin(dot(gi + float2(1, 0), float2(12.9898, 78.233))) * 43758.5453);
                    float n01 = frac(sin(dot(gi + float2(0, 1), float2(12.9898, 78.233))) * 43758.5453);
                    float n11 = frac(sin(dot(gi + float2(1, 1), float2(12.9898, 78.233))) * 43758.5453);
                    float grain = lerp(lerp(n00, n10, gf.x), lerp(n01, n11, gf.x), gf.y);
                    float front = _Materialize * 2.4 - .25 - (i.height + (grain - .5) * .45);
                    clip(front);
                    dream += _DreamColor.rgb * _DreamColor.rgb * (smoothstep(.3, 0, front) * .55 + smoothstep(.06, 0, front) * .6);
                }
                dream += _DreamColor.rgb * _DreamGlow * exp(-max(i.height, 0) * 2.4);
                fixed4 c = fixed4(rgb, t.a) * i.color;
                c.rgb *= lerp(1,.65,saturate(_HotelLightingEnabled))+HotelLampLight(i.ground,float2(0,0),1,.35)*.7;
                c.rgb += dream * t.a;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
