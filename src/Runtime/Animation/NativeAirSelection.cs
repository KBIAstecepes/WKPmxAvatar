using System;
using UnityEngine;
namespace WKPmxAvatar
{
    // Selection from observed motion. No movement, collider, gravity or gameplay writes.
    public static class NativeAirSelection
    {
        public static readonly int[] Required={162,163,164,165,168,169,167,171,178,179,180,181,182,183,184,185,187,188,189,190,191,192,193,194};
        public static int SpeedTier(float speed,float walkReference,float runReference=0)
        {if(!float.IsFinite(speed)||!float.IsFinite(walkReference)||walkReference<=0)throw new ArgumentOutOfRangeException();return speed<0.15f?0:speed<walkReference*1.1f?1:speed<(runReference>0?runReference*1.05f:walkReference*2.3f)?2:3;}
        public static int Takeoff(int tier,bool left)=>tier==0?(left?168:164):tier==3?(left?171:167):(left?169:165);
        public static int Fall(bool left)=>left?163:162;
        public static int Landing(int tier,bool left,bool heavy)
        {
            if(left)return tier==0?(heavy?194:193):tier==1?(heavy?192:191):tier==2?(heavy?188:187):(heavy?190:189);
            return tier==0?(heavy?185:182):tier==1?(heavy?184:183):tier==2?(heavy?179:178):(heavy?181:180);
        }
    }
}
