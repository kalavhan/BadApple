using System.Collections.Generic;
using UnityEngine;
namespace BadAppleHotel.Game
{
    public static class HudLayout
    {
        public static float Overlap(Rect a, Rect b) => Mathf.Max(0, Mathf.Min(a.xMax,b.xMax)-Mathf.Max(a.xMin,b.xMin)) * Mathf.Max(0, Mathf.Min(a.yMax,b.yMax)-Mathf.Max(a.yMin,b.yMin));
        public static Rect Popup(Vector2 anchor, Vector2 size, Rect safe, IList<Rect> occupied)
        {
            size.x = Mathf.Min(size.x, safe.width); size.y = Mathf.Min(size.y, safe.height);
            var candidates = new[] {
                new Rect(anchor.x + 24, anchor.y-size.y/2, size.x,size.y),
                new Rect(anchor.x-size.x-24, anchor.y-size.y/2,size.x,size.y),
                new Rect(anchor.x-size.x/2,anchor.y-size.y-24,size.x,size.y),
                new Rect(anchor.x-size.x/2,anchor.y+24,size.x,size.y),
                new Rect(safe.center.x-size.x/2,safe.yMax-size.y,size.x,size.y) };
            Rect best = candidates[0]; float score = float.MaxValue;
            foreach (var candidate in candidates)
            {
                var r = candidate;
                r.x = Mathf.Clamp(r.x, safe.xMin, safe.xMax-r.width);
                r.y = Mathf.Clamp(r.y, safe.yMin, safe.yMax-r.height);
                float cost = 0;
                foreach (var obstacle in occupied) cost += Overlap(r, obstacle);
                if (cost < score) { best = r; score = cost; }
                if (cost == 0) break;
            }
            return best;
        }
    }
}
