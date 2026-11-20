using System;
using System.Numerics;
namespace WKPmxAvatar.RuntimePhysics
{
    // Compact pose keys fitted offline to the Rossi motion set. Outside their
    // neighbourhood the skirt uses its normal fixed-length clearance pose.
    internal static class PoseDrapeKeys
    {
        struct Key
        {
            public Vector3 LeftKnee, LeftFoot, RightKnee, RightFoot;
            public float Radius;
            public Key(Vector3 lk, Vector3 lf, Vector3 rk, Vector3 rf, float radius)
            { LeftKnee=lk; LeftFoot=lf; RightKnee=rk; RightFoot=rf; Radius=radius; }
        }
        static readonly Key[] Keys={
            new Key(new Vector3(0.4579671f, -3.5081233f, -2.7396401f), new Vector3(0.5952800f, -7.3819656f, -0.5744711f), new Vector3(-0.0025369f, -3.6100600f, -2.6438158f), new Vector3(-0.2948091f, -7.4223678f, -0.3867472f), 2.0000000f), // crouch-045
            new Key(new Vector3(0.0025369f, -3.6100600f, -2.6438158f), new Vector3(0.2948091f, -7.4223678f, -0.3867472f), new Vector3(-0.4579671f, -3.5081233f, -2.7396401f), new Vector3(-0.5952800f, -7.3819656f, -0.5744711f), 2.0000000f), // crouch-045 mirror
            new Key(new Vector3(0.2390194f, -4.4607023f, 0.2593954f), new Vector3(0.4047299f, -7.9118064f, 3.0479212f), new Vector3(0.5588123f, -2.5854970f, -3.6090495f), new Vector3(0.9587534f, -7.0062956f, -3.7093562f), 2.0000000f), // run-009
            new Key(new Vector3(0.1861370f, -4.4589125f, 0.3251826f), new Vector3(0.4368890f, -7.8382690f, 3.1940743f), new Vector3(0.1980288f, -0.0430340f, -4.4700374f), new Vector3(0.6854685f, -4.2729094f, -5.7286217f), 1.2872511f), // sprint-008
            new Key(new Vector3(-0.1980288f, -0.0430340f, -4.4700374f), new Vector3(-0.6854685f, -4.2729094f, -5.7286217f), new Vector3(-0.1861370f, -4.4589125f, 0.3251826f), new Vector3(-0.4368890f, -7.8382690f, 3.1940743f), 1.2872640f), // sprint-008 mirror
        };
        internal static float Weight(Vector3 leftKnee, Vector3 leftFoot,
                                     Vector3 rightKnee, Vector3 rightFoot, float scale)
        {
            leftKnee/=scale; leftFoot/=scale; rightKnee/=scale; rightFoot/=scale;
            float strongest=0;
            foreach(var key in Keys)
            {
                float left=Math.Max(Vector3.DistanceSquared(leftKnee,key.LeftKnee),Vector3.DistanceSquared(leftFoot,key.LeftFoot));
                float right=Math.Max(Vector3.DistanceSquared(rightKnee,key.RightKnee),Vector3.DistanceSquared(rightFoot,key.RightFoot));
                float t=Math.Max(0,1-Math.Max(left,right)/(key.Radius*key.Radius));
                strongest=Math.Max(strongest,t*t*(3-2*t));
            }
            return strongest;
        }
    }
}
