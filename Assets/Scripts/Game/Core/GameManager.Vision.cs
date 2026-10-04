using UnityEngine;

namespace BadAppleHotel.Game
{
    public partial class GameManager
    {
        Texture2D fogTex;
        Color32[] fogPx;
        bool[] visible, explored;
        float nextVisionUpdate;
        public bool FogActive => InMatch && !(HumanRole == Role.Resident && Human != null &&
            (!Human.Alive || (Human.Room != null && Human.Room.HasClairvoyance())));
        public Vector2 ViewOrigin => HumanRole == Role.Monster && Monster != null ? Monster.Pos : Human != null ? Human.Pos : HotelMap.Center(Map.Lobby);
        public float SightRadius => Cfg.residents.visionRadiusTiles + (HumanRole == Role.Monster && Monster != null ? RevealRadius(Monster) : 0);

        void CreateFog()
        {
            if(fogTex!=null)RemoveObject(fogTex);
            fogTex=new Texture2D(Map.W,Map.H,TextureFormat.RGBA32,false) { filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp };
            fogPx=new Color32[Map.W*Map.H]; visible=new bool[fogPx.Length];explored=new bool[fogPx.Length];nextVisionUpdate=0;
            Shader.SetGlobalTexture("_HotelVision",fogTex);
            Shader.SetGlobalVector("_HotelSize",new Vector4(Map.W,Map.H,0,0));
            Shader.SetGlobalFloat("_HotelFog",0);
        }
        public bool IsVisible(Vector2 world)
        {
            if(!FogActive||visible==null)return true;
            int x=Mathf.FloorToInt(world.x),y=Mathf.FloorToInt(world.y);
            return Map.InBounds(x,y)&&visible[y*Map.W+x];
        }
        public bool IsTileVisible(Vector2Int t)=>IsVisible(HotelMap.Center(t));
        bool Opaque(int x,int y)
        {
            var tile=Map.Get(x,y);
            if(tile==Tile.Wall||tile==Tile.Void)return true;
            if(tile!=Tile.Door)return false;
            var def=Map.RoomAtDoor(new Vector2Int(x,y));
            return def!=null&&RoomsByDef.TryGetValue(def,out var room)&&room.DoorBlocks;
        }
        public bool CanSee(Vector2 from,Vector2 to,float radius)=>Vector2.Distance(from,to)<=radius&&Sight.Clear(from,to,Opaque);
        void UpdateVision()
        {
            if(Simulation||fogTex==null||Map==null)return;
            bool fog=FogActive; Shader.SetGlobalFloat("_HotelFog",fog?1:0);
            if(Time.unscaledTime>=nextVisionUpdate)
            {
                nextVisionUpdate=Time.unscaledTime+0.06f;
                System.Array.Clear(visible,0,visible.Length);
                if(fog)
                {
                    var origin=ViewOrigin; float radius=SightRadius;
                    int x0=Mathf.Max(0,Mathf.FloorToInt(origin.x-radius)),x1=Mathf.Min(Map.W-1,Mathf.CeilToInt(origin.x+radius));
                    int y0=Mathf.Max(0,Mathf.FloorToInt(origin.y-radius)),y1=Mathf.Min(Map.H-1,Mathf.CeilToInt(origin.y+radius));
                    for(int x=x0;x<=x1;x++)for(int y=y0;y<=y1;y++)
                    {
                        var center=HotelMap.Center(new Vector2Int(x,y));
                        if(Vector2.Distance(center,origin)<=radius&&Sight.Clear(origin,center,Opaque,true))visible[y*Map.W+x]=true;
                    }
                    // A visible floor exposes its bordering wall face, including oblique corners.
                    for(int x=x0;x<=x1;x++)for(int y=y0;y<=y1;y++)
                        if(visible[y*Map.W+x]&&Map.Get(x,y)!=Tile.Wall)
                            foreach(var d in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right})
                                if(Map.Get(x+d.x,y+d.y)==Tile.Wall)visible[(y+d.y)*Map.W+x+d.x]=true;
                    for(int i=0;i<visible.Length;i++)
                    {
                        explored[i]|=visible[i];byte value=visible[i]?(byte)255:explored[i]?(byte)48:(byte)0;
                        fogPx[i]=new Color32(value,value,value,255);
                    }
                    fogTex.SetPixels32(fogPx);fogTex.Apply(false);
                }
            }
            foreach(var resident in Residents)if(resident.Sr!=null)resident.Sr.enabled=resident==Human||!fog||IsVisible(resident.Pos);
            foreach(var part in Parts)if(part.Sr!=null)part.Sr.enabled=!fog||IsTileVisible(part.Tile);
            foreach(var room in RoomsByDef.Values)
            {
                bool known=room.Owner==Human||!fog||IsVisible(room.Def.BedCenter);
                if(room.BedSr!=null)room.BedSr.enabled=known;
                foreach(var tower in room.Slots)if(tower?.Sr!=null)tower.Sr.enabled=room.Owner==Human||!fog||IsTileVisible(tower.Tile);
            }
        }
    }
}
