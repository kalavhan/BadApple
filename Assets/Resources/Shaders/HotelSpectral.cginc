#ifndef BADAPPLE_HOTEL_SPECTRAL_INCLUDED
#define BADAPPLE_HOTEL_SPECTRAL_INCLUDED
// Per-cell energy for claimed rooms, written by GameManager.FloorEnergy:
// r seam strength (1 open build square, lower once built on), g legal-build plus,
// b selected square. Sampled with point filtering, one texel per hotel cell.
sampler2D _SpectralCells;
float4 _SpectralSize;

static const fixed3 SpectralMint = fixed3(.30, 1, .80);
static const fixed3 SpectralViolet = fixed3(.62, .38, 1);

inline float4 SpectralCell(float2 cell) { return tex2Dlod(_SpectralCells, float4((cell + .5) / max(_SpectralSize.xy, float2(1, 1)), 0, 0)); }

// Glowing seams that divide a claimed room's floor squares, as if mana leaked up between them.
inline fixed3 SpectralFloor(float2 p, float2 cell, float2 f)
{
    float4 s = SpectralCell(cell);
    if (s.r <= 0) return 0;
    float t = _Time.y;
    float2 m = min(f, 1 - f);
    float e = min(m.x, m.y);
    // Energy flows along the seams with brighter beads drifting through, and gently breathes.
    float flow = .5 + .5 * sin((p.x + p.y) * 5.3 - t * 1.6 + sin(p.x * 2.1 - p.y * 1.7 + t * .7) * 2);
    float beads = smoothstep(.82, 1, frac((p.x - p.y) * .37 + (p.x + p.y) * .21 - t * .45));
    float breathe = .8 + .2 * sin(t * 1.3 + (cell.x * 3 + cell.y * 5));
    float corner = exp(-length(m) * 14);
    float seam = (exp(-e * 34) * (flow + beads * 1.4) * breathe + corner * .25) * s.r * (1 + s.b * 1.3);
    // Mint core with a violet fringe a little further out, like light refracting off miasma.
    fixed3 col = (SpectralMint * exp(-e * 60) + SpectralViolet * exp(-e * 18) * .35) * seam * .6;
    // The plus itself floats above the floor (HotelSpectralPlus); this is its glow pooled below.
    float2 c = f - .5;
    float pulse = .7 + .3 * sin(t * 2.2 + cell.x + cell.y);
    col += SpectralMint * s.g * exp(-dot(c, c) * 18) * .22 * pulse * (1 + s.b) + SpectralMint * s.b * .05;
    return col;
}
#endif
