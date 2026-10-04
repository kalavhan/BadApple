using System;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Supercover grid ray: even an exact diagonal cannot see through two touching wall corners.</summary>
    public static class Sight
    {
        public static bool Clear(Vector2 from, Vector2 to, Func<int,int,bool> opaque, bool seeTargetWall = false)
        {
            var p=HotelMap.ToTile(from);var end=HotelMap.ToTile(to);var delta=to-from;
            int sx=delta.x>=0?1:-1,sy=delta.y>=0?1:-1;
            float dx=Mathf.Abs(delta.x)<0.00001f?float.PositiveInfinity:Mathf.Abs(1/delta.x);
            float dy=Mathf.Abs(delta.y)<0.00001f?float.PositiveInfinity:Mathf.Abs(1/delta.y);
            float tx=dx*((sx>0?p.x+1-from.x:from.x-p.x)),ty=dy*((sy>0?p.y+1-from.y:from.y-p.y));
            for(int guard=0;p!=end&&guard<1024;guard++)
            {
                if(Mathf.Abs(tx-ty)<0.00001f)
                {
                    if(opaque(p.x+sx,p.y)||opaque(p.x,p.y+sy))return false;
                    p.x+=sx;p.y+=sy;tx+=dx;ty+=dy;
                }
                else if(tx<ty){p.x+=sx;tx+=dx;}else{p.y+=sy;ty+=dy;}
                if(p==end&&seeTargetWall)return true;
                if(opaque(p.x,p.y))return false;
            }
            return p==end;
        }
    }
}
