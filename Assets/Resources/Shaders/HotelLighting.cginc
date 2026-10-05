#ifndef BADAPPLE_HOTEL_LIGHTING_INCLUDED
#define BADAPPLE_HOTEL_LIGHTING_INCLUDED
sampler2D _HotelLightMap;
float4 _HotelLightSize;
fixed4 _HotelLampColor;
float _HotelLightingEnabled;

// XY is the hotel floor plane. Walls pass their outward world normal.xy; floors
// pass zero. The offset leaves the thin slab so each wall side samples its own
// adjacent space. Visibility gates the added light, including explored dark fog.
inline fixed3 HotelLampLight(float2 worldXY, float2 outwardNormalXY, float visible)
{
    float normalLength = length(outwardNormalXY);
    float2 samplePosition = worldXY;
    if (normalLength > .1) samplePosition += outwardNormalXY / normalLength * .36;
    float2 uv = samplePosition / max(_HotelLightSize.xy, float2(1, 1));
    float inside = step(0, uv.x) * step(0, uv.y) * step(uv.x, 1) * step(uv.y, 1);
    float light = tex2D(_HotelLightMap, uv).r;
    return _HotelLampColor.rgb * light * inside * saturate(visible) * saturate(_HotelLightingEnabled);
}
#endif
