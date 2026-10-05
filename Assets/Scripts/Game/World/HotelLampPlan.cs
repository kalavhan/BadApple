using System.Collections.Generic;
using UnityEngine;

namespace BadAppleHotel.Game
{
    /// <summary>Deterministic, spaced wall sconces selected against actual occluded floor coverage.</summary>
    public static class HotelLampPlan
    {
        public const float MinimumSpacing=3f, RoomCoverage=.90f, CorridorCoverage=.85f;
        // Plan above the .25 useful-light threshold to leave room for texture quantization.
        const float UsefulLight=.30f;
        public readonly struct Candidate
        {
            public readonly HotelLighting.Lamp Lamp;
            public readonly int Group;
            public readonly bool StaysUp;
            public Candidate(Vector2 position,Vector2 normal,int group,bool staysUp,float radius=8f)
            { Lamp=new HotelLighting.Lamp(position,normal,radius);Group=group;StaysUp=staysUp; }
        }
        readonly struct Sample
        {
            public readonly Vector2 Point;public readonly int Group;
            public Sample(Vector2 point,int group){Point=point;Group=group;}
        }

        public static HashSet<int> Select(HotelMap map,IReadOnlyList<Candidate> candidates)
        {
            int groups=map.Rooms.Count+1;
            var samples=new List<Sample>();var totals=new int[groups];
            // Four inset samples per square measure floor area, including dim wall edges.
            for(int y=0;y<map.H;y++)for(int x=0;x<map.W;x++)
            {
                var tile=map.Get(x,y);if(tile!=Tile.RoomFloor&&tile!=Tile.Corridor)continue;
                int group=tile==Tile.Corridor?map.Rooms.Count:map.RoomContaining(new Vector2Int(x,y)).Index;
                foreach(float dx in new[]{.25f,.75f})foreach(float dy in new[]{.25f,.75f})
                {samples.Add(new Sample(new Vector2(x+dx,y+dy),group));totals[group]++;}
            }
            bool Opaque(int x,int y)=>map.Get(x,y)!=Tile.Corridor&&map.Get(x,y)!=Tile.RoomFloor;
            var contributions=new List<KeyValuePair<int,float>>[candidates.Count];
            for(int i=0;i<candidates.Count;i++)
            {
                contributions[i]=new List<KeyValuePair<int,float>>();var candidate=candidates[i];
                var origin=HotelLighting.LampOrigin(candidate.Lamp,Opaque);
                for(int j=0;j<samples.Count;j++)
                {
                    if(samples[j].Group!=candidate.Group ||
                        (samples[j].Point-candidate.Lamp.Position).sqrMagnitude>(candidate.Lamp.Radius+.2f)*(candidate.Lamp.Radius+.2f))continue;
                    // Match the bilinear field, including partial shadows at floor edges.
                    var point=samples[j].Point;
                    float light=(HotelLighting.Contribution(candidate.Lamp,origin,point+new Vector2(-.125f,-.125f),Opaque)+
                        HotelLighting.Contribution(candidate.Lamp,origin,point+new Vector2(.125f,-.125f),Opaque)+
                        HotelLighting.Contribution(candidate.Lamp,origin,point+new Vector2(-.125f,.125f),Opaque)+
                        HotelLighting.Contribution(candidate.Lamp,origin,point+new Vector2(.125f,.125f),Opaque))*.25f;
                    if(light>.005f)contributions[i].Add(new KeyValuePair<int,float>(j,light));
                }
            }
            var selected=new HashSet<int>();var allowed=new bool[candidates.Count];
            for(int i=0;i<allowed.Length;i++)allowed[i]=true;
            var illumination=new float[samples.Count];var covered=new int[groups];
            int Target(int group)=>Mathf.CeilToInt(totals[group]*(group<map.Rooms.Count?RoomCoverage:CorridorCoverage));
            float Gain(int i)
            {
                float score=0;
                foreach(var pair in contributions[i])
                {
                    float before=illumination[pair.Key];
                    if(before<UsefulLight)score+=Mathf.Min(UsefulLight-before,pair.Value);
                }
                return score;
            }
            void Choose(int i)
            {
                selected.Add(i);
                foreach(var pair in contributions[i])
                {
                    float before=illumination[pair.Key];illumination[pair.Key]=Mathf.Min(1,before+pair.Value);
                    if(before<UsefulLight&&illumination[pair.Key]>=UsefulLight)covered[samples[pair.Key].Group]++;
                }
                for(int j=0;j<allowed.Length;j++)
                    if((candidates[i].Lamp.Position-candidates[j].Lamp.Position).sqrMagnitude<MinimumSpacing*MinimumSpacing-.0001f)allowed[j]=false;
            }
            // Small irregular rooms need a set of compatible mounting positions. Trying
            // each far-wall anchor avoids a central first lamp blocking both useful corners.
            for(int group=0;group<map.Rooms.Count;group++)
            {
                var options=new List<int>();for(int index=0;index<candidates.Count;index++)
                    if(allowed[index]&&candidates[index].Group==group)options.Add(index);
                List<int> best=null;int bestCovered=-1;
                void Trial(int first,int second=-1)
                {
                    var trialAllowed=(bool[])allowed.Clone();var trialLight=(float[])illumination.Clone();
                    var picks=new List<int>();int lit=covered[group];
                    void Add(int i)
                    {
                        picks.Add(i);
                        foreach(var pair in contributions[i])
                        {
                            float before=trialLight[pair.Key];trialLight[pair.Key]=Mathf.Min(1,before+pair.Value);
                            if(before<UsefulLight&&trialLight[pair.Key]>=UsefulLight)lit++;
                        }
                        foreach(int j in options)if((candidates[i].Lamp.Position-candidates[j].Lamp.Position).sqrMagnitude<MinimumSpacing*MinimumSpacing-.0001f)trialAllowed[j]=false;
                    }
                    Add(first);if(second>=0){if(!trialAllowed[second])return;Add(second);}
                    while(lit<Target(group))
                    {
                        int next=-1;float score=0;
                        foreach(int i in options)if(trialAllowed[i])
                        {
                            float gain=0;foreach(var pair in contributions[i])if(trialLight[pair.Key]<UsefulLight)
                                gain+=Mathf.Min(UsefulLight-trialLight[pair.Key],pair.Value);
                            if(gain>score){score=gain;next=i;}
                        }
                        if(next<0)break;Add(next);
                    }
                    bool success=lit>=Target(group),bestSuccess=bestCovered>=Target(group);
                    if(best==null || (success&&!bestSuccess) || (!success&&!bestSuccess&&lit>bestCovered) ||
                        (success&&bestSuccess&&(picks.Count<best.Count || picks.Count==best.Count&&lit<bestCovered)))
                    {best=picks;bestCovered=lit;}
                }
                foreach(int first in options)if(candidates[first].StaysUp)Trial(first);
                if(bestCovered<Target(group))
                    foreach(int first in options)if(candidates[first].StaysUp)
                        foreach(int second in options)if(second!=first)Trial(first,second);
                if(best!=null)foreach(int index in best)Choose(index);
            }
            int corridor=map.Rooms.Count;
            while(covered[corridor]<Target(corridor))
            {
                int best=-1;float score=0;
                for(int i=0;i<candidates.Count;i++)if(allowed[i]&&candidates[i].Group==corridor)
                {float gain=Gain(i);if(gain>score){score=gain;best=i;}}
                if(best<0)break;Choose(best);
            }
            return selected;
        }
    }
}
