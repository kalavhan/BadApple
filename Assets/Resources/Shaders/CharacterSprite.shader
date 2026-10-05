// Alpha-clipped world sprites share the wall depth buffer, including characters, doors and towers.
//  * Shirt recolour: pixels in the magenta band (every character wears a magenta shirt) are hue-shifted to _ShirtHue,
//    so each resident gets their own shirt colour from one set of art. _ShirtHue < 0 turns it off (bosses).
//  * Hit flash: _Flash blends the sprite toward _FlashColor (damage, healing, spawn).
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

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            fixed4 _Color;
            float _ShirtHue, _ShirtSat, _ShirtVal, _Flash;
            fixed4 _FlashColor;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
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
                fixed4 c = fixed4(rgb, t.a) * i.color;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
