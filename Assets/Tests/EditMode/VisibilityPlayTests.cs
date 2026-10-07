using System.Collections;
using System.Reflection;
using BadAppleHotel.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BadAppleHotel.Tests
{
    public class VisibilityPlayTests
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [UnitySetUp] public IEnumerator Enter() { yield return new EnterPlayMode(); }
        [UnityTest] public IEnumerator Walking_and_hotel_view_do_not_reveal_guests_behind_a_closed_door()
        {
            GameManager gm=null;
            for(int i=0;i<90;i++) { yield return null;gm=GameManager.Instance;if(gm?.Map!=null)break; }
            Assert.IsNotNull(gm?.Map);gm.StartMatch(Role.Resident,"stitchwork_chef");gm.SetSpeed(0);
            // Keep lonely-room gifts out of the way so only the normal sight rules apply.
            var own=gm.Map.Rooms[0];own.Isolated=false;
            Assert.IsTrue(gm.Claim(gm.Human,own,false));
            // Another guest's room, shut behind its door. The player's own room stays in view.
            var def=gm.Map.Rooms[1];def.Isolated=false;
            var neighbour=gm.Residents.Find(r=>r!=gm.Human&&r.Room==null);
            Assert.IsTrue(gm.Claim(neighbour,def,true));
            var room=gm.RoomsByDef[def];Assert.IsFalse(room.DoorOpen);
            gm.Human.Pos=HotelMap.Center(def.DoorOutside);
            Refresh(gm);Assert.IsTrue(gm.FogActive);Assert.IsFalse(gm.IsTileVisible(def.DoorInside));
            Assert.IsTrue(gm.IsTileVisible(own.DoorInside),"A claimed room stays visible to its guest.");
            Assert.IsFalse(gm.CanSee(gm.Human.Pos,HotelMap.Center(def.DoorInside),gm.SightRadius));
            gm.HotelView=true;Refresh(gm);Assert.IsFalse(gm.IsTileVisible(def.DoorInside));
            room.DoorOpen=true;Refresh(gm);Assert.IsTrue(gm.IsTileVisible(def.DoorInside));
            Assert.IsFalse(gm.CanSee(gm.Human.Pos,gm.Human.Pos+Vector2.right*(gm.SightRadius+1),gm.SightRadius));
            gm.ReturnToMenu();yield return new ExitPlayMode();
        }
        static void Refresh(GameManager gm)
        {
            typeof(GameManager).GetField("nextVisionUpdate",Private).SetValue(gm,0f);
            typeof(GameManager).GetMethod("UpdateVision",Private).Invoke(gm,null);
        }
    }
}
