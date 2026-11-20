using System;
using System.Collections.Generic;
using UnityEngine;

namespace WKPmxAvatar
{
    // Fit existing body interaction triggers to the displayed rig. The network root,
    // blocking colliders, tags, ownership and hand/item colliders remain unchanged.
    public sealed class BodyGrabAlignment : IDisposable
    {
        sealed class Saved {public CapsuleCollider Collider;public Vector3 Center;public float Height,Radius;public int Direction;}
        readonly List<Saved> saved=new List<Saved>();
        readonly Transform head,leftFoot,rightFoot,leftArm,rightArm,hips;
        public int Count=>saved.Count;
        public BodyGrabAlignment(Transform root,Transform[] bones)
        {
            Transform Find(string name)=>Array.Find(bones,b=>b.name==name);
            head=Find("頭");leftFoot=Find("左足首");rightFoot=Find("右足首");leftArm=Find("左腕");rightArm=Find("右腕");hips=Find("腰");
            if(head==null||leftFoot==null||rightFoot==null||leftArm==null||rightArm==null||hips==null)return;
            foreach(var c in root.GetComponentsInChildren<CapsuleCollider>(true)){
                if(!c.isTrigger)continue;
                bool handOrItem=false;foreach(var component in c.GetComponentsInParent<Component>(true))if(component!=null&&(component.GetType().Name=="RemoteHand"||component.GetType().Name=="AvatarMotion")){handOrItem=true;break;}
                // Only human-sized body capsules, never item or hand triggers.
                if(handOrItem||c.height*Mathf.Abs(c.transform.lossyScale[c.direction])<0.6f)continue;
                saved.Add(new Saved{Collider=c,Center=c.center,Height=c.height,Radius=c.radius,Direction=c.direction});
            }
        }
        public void Apply(bool enabled)
        {
            if(!enabled){Restore();return;}
            foreach(var s in saved){var c=s.Collider;if(c==null)continue;var t=c.transform;
                Vector3 up=t.InverseTransformDirection(Vector3.up);int axis=0;if(Mathf.Abs(up.y)>Mathf.Abs(up.x))axis=1;if(Mathf.Abs(up.z)>Mathf.Abs(up[axis]))axis=2;
                Vector3 foot=(leftFoot.position+rightFoot.position)*0.5f;
                float reference=Vector3.Distance(head.position,hips.position)+Vector3.Distance(hips.position,foot);
                float radius=Mathf.Clamp(Vector3.Distance(leftArm.position,rightArm.position)*0.55f,reference*0.07f,reference*0.16f);
                Vector3 top=t.InverseTransformPoint(head.position+Vector3.up*(reference*0.075f));
                Vector3 bottom=t.InverseTransformPoint(foot-Vector3.up*(reference*0.025f));
                Vector3 center=t.InverseTransformPoint(hips.position);center[axis]=(top[axis]+bottom[axis])*0.5f;
                Vector3 scale=t.lossyScale;float perpendicular=0;for(int i=0;i<3;i++)if(i!=axis)perpendicular=Mathf.Max(perpendicular,Mathf.Abs(scale[i]));
                if(perpendicular<0.0001f)continue;
                c.direction=axis;c.center=center;c.radius=radius/perpendicular;c.height=Mathf.Max(Mathf.Abs(top[axis]-bottom[axis]),2*c.radius);
            }
        }
        void Restore(){foreach(var s in saved)if(s.Collider!=null){s.Collider.center=s.Center;s.Collider.height=s.Height;s.Collider.radius=s.Radius;s.Collider.direction=s.Direction;}}
        public void Dispose(){Restore();}
    }
}
