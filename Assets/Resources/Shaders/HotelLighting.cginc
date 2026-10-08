#ifndef BADAPPLE_HOTEL_LIGHTING_INCLUDED
#define BADAPPLE_HOTEL_LIGHTING_INCLUDED
sampler2D _HotelLightMap;
float4 _HotelLightSize;
fixed4 _HotelLampColor;
float _HotelLightingEnabled;
// Brief dream-effect lights (muzzle flashes, impacts, summons) from DreamFx: xy position,
// z inverse squared radius; rgb colour. A handful at most, cleared when nothing flashes.
float4 _FxLightPos[6];
float4 _FxLightColor[6];
float _FxLightCount;

inline fixed3 HotelFxLight(float2 p)
{
    fixed3 c = 0;
    for (int i = 0; i < 6; i++)
    {
        if (i >= (int)_FxLightCount) break;
        float2 d = p - _FxLightPos[i].xy;
        float k = saturate(1 - dot(d, d) * _FxLightPos[i].z);
        c += _FxLightColor[i].rgb * k * k;
    }
    // Overlapping flashes roll off softly instead of blowing the floor out to white.
    float m = max(c.r, max(c.g, c.b));
    return m > .8 ? c * ((.8 + (m - .8) / (1 + (m - .8) * 1.5)) / m) : c;
}

// XY is the hotel floor plane. Walls pass their outward world normal.xy; floors
// pass zero. The offset leaves the thin slab so each wall side samples its own
// adjacent space. Visibility gates the added light, including explored dark fog.
// fxGain scales the effect lights: upright sprites take a fraction so a creature's own
// muzzle flash cannot wash it out.
inline fixed3 HotelLampLight(float2 worldXY, float2 outwardNormalXY, float visible, float fxGain)
{
    float normalLength = length(outwardNormalXY);
    float2 samplePosition = worldXY;
    if (normalLength > .1) samplePosition += outwardNormalXY / normalLength * .36;
    float2 uv = samplePosition / max(_HotelLightSize.xy, float2(1, 1));
    float inside = step(0, uv.x) * step(0, uv.y) * step(uv.x, 1) * step(uv.y, 1);
    float light = tex2D(_HotelLightMap, uv).r;
    fixed3 lamp = lerp(fixed3(.72,.67,.60), _HotelLampColor.rgb, .3) * light * .65 * inside;
    return (lamp + HotelFxLight(samplePosition) * fxGain) * saturate(visible) * saturate(_HotelLightingEnabled);
}

inline fixed3 HotelLampLight(float2 worldXY, float2 outwardNormalXY, float visible)
{
    return HotelLampLight(worldXY, outwardNormalXY, visible, 1);
}
#endif
