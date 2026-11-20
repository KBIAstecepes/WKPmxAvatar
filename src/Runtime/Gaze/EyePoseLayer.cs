using System;
using System.Collections.Generic;
using UnityEngine;

namespace WKPmxAvatar.Gaze
{
    [Serializable] public sealed class EyeDirectionProfile
    {
        public float[] linear3x3;public EyeDirection[] directions;
    }
    [Serializable] public sealed class EyeDirection
    {
        public string name,pmxName;public float[] deltaPosition,sourceDeltaEuler,basePoseEuler,nativeParentQuaternion;
    }
    // Original authored data + explicit adaptation for additive Euler mixing.
    // This layer writes only the two iris parents; current animation is the base.
    public sealed class EyePoseLayer
    {
        sealed class Eye
        {
            internal Transform Bone;internal EyeDirection[] Controls=new EyeDirection[4];
            internal Vector3 BasePosition,WrittenPosition;internal Quaternion BaseRotation,WrittenRotation,RestFrameInverse;
            internal bool Written;
        }
        readonly Eye[] eyes=new Eye[2];readonly float[] map;readonly float scale;
        static Vector3 V(float[] v)=>new Vector3(v[0],v[1],v[2]);
        static Quaternion Q(float[] v)=>new Quaternion(v[0],v[1],v[2],v[3]);
        public static Quaternion AuthoredEuler(Vector3 e)=>Quaternion.AngleAxis(-e.z,Vector3.forward)*Quaternion.AngleAxis(-e.y,Vector3.up)*Quaternion.AngleAxis(e.x,Vector3.right);
        Vector3 Map(Vector3 v)=>new Vector3(map[0]*v.x+map[1]*v.y+map[2]*v.z,map[3]*v.x+map[4]*v.y+map[5]*v.z,map[6]*v.x+map[7]*v.y+map[8]*v.z);
        Quaternion Frame(Quaternion q)=>Quaternion.LookRotation(Map(q*Vector3.forward),Map(q*Vector3.up));
        public Transform Left=>eyes[0].Bone;public Transform Right=>eyes[1].Bone;
        public EyePoseLayer(EyeDirectionProfile profile,Transform[] bones,float pmxUnitScale)
        {
            if(profile==null||profile.linear3x3==null||profile.linear3x3.Length!=9||profile.directions==null||profile.directions.Length!=8||pmxUnitScale<=0)throw new ArgumentException("Invalid original eye profile");
            map=profile.linear3x3;scale=pmxUnitScale;
            string[] names={"瞳左","瞳右"};
            for(int side=0;side<2;side++)
            {
                var bone=Array.Find(bones,b=>b!=null&&b.name==names[side]);if(bone==null)throw new ArgumentException("Rossi iris parent missing: "+names[side]);
                var eye=new Eye{Bone=bone};int matched=0;
                foreach(var d in profile.directions)if(d.pmxName==names[side])
                {
                    int index=d.name.Contains("_u_")?0:d.name.Contains("_d_")?1:d.name.Contains("_l_")?2:d.name.Contains("_r_")?3:-1;
                    if(index<0||eye.Controls[index]!=null)throw new ArgumentException("Duplicate/unknown original eye direction");
                    eye.Controls[index]=d;matched++;
                }
                if(matched!=4)throw new ArgumentException("Incomplete eye directions");
                var first=eye.Controls[0];eye.RestFrameInverse=Quaternion.Inverse(Frame(Q(first.nativeParentQuaternion)*AuthoredEuler(V(first.basePoseEuler))));
                eyes[side]=eye;
            }
        }
        public void Apply(float left,float up)
        {
            Restore();
            foreach(var e in eyes)
            {
                Vector3 position=Vector3.zero,euler=Vector3.zero;
                for(int i=0;i<4;i++)
                {
                    float weight=i==0?Mathf.Max(0,up):i==1?Mathf.Max(0,-up):i==2?Mathf.Max(0,left):Mathf.Max(0,-left);
                    position+=V(e.Controls[i].deltaPosition)*weight;
                    euler+=V(e.Controls[i].sourceDeltaEuler)*weight;
                }
                e.BasePosition=e.Bone.localPosition;e.BaseRotation=e.Bone.localRotation;
                // Sum both directional rotations before conversion; neither axis
                // overwrites the other on diagonal gaze.
                var d=e.Controls[0];Quaternion delta=Frame(Q(d.nativeParentQuaternion)*AuthoredEuler(V(d.basePoseEuler)+euler))*e.RestFrameInverse;
                if(left==0&&up==0)delta=Quaternion.identity;
                e.WrittenPosition=e.BasePosition+position*scale;e.WrittenRotation=delta*e.BaseRotation;
                e.Bone.SetLocalPositionAndRotation(e.WrittenPosition,e.WrittenRotation);e.Written=true;
            }
        }
        public void Restore()
        {
            foreach(var e in eyes)if(e!=null&&e.Written)
            {
                // A newer animator/pose write wins; never restore an older base
                // over a transform already replaced by another writer.
                if(e.Bone!=null&&Vector3.SqrMagnitude(e.Bone.localPosition-e.WrittenPosition)<1e-12f&&Same(e.Bone.localRotation,e.WrittenRotation))
                    e.Bone.SetLocalPositionAndRotation(e.BasePosition,e.BaseRotation);
                e.Written=false;
            }
        }
        static bool Same(Quaternion a,Quaternion b)
        {
            var av=new Vector4(a.x,a.y,a.z,a.w);var bv=new Vector4(b.x,b.y,b.z,b.w);
            return (av-bv).sqrMagnitude<1e-12f||(av+bv).sqrMagnitude<1e-12f;
        }
    }
}
