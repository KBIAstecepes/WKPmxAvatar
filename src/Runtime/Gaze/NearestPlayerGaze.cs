using System;
using System.Collections.Generic;
using UnityEngine;

namespace WKPmxAvatar.Gaze
{
    public struct GazeParticipant
    {
        public ulong Id;public Transform Root,Head;public bool Active;
        public GazeParticipant(ulong id,Transform root,Transform head){Id=id;Root=root;Head=head;Active=true;}
    }
    // Candidate core, shared by offline dynamic tests and future runtime adapter.
    // It observes colliders but never creates/modifies game collision or movement.
    public sealed class NearestPlayerGaze
    {
        readonly Transform owner,view,head;readonly ulong self;readonly EyePoseLayer layer;
        readonly Vector3 localOrigin;readonly RaycastHit[] hits=new RaycastHit[64];
        Vector3 worldDirection;bool initialized;
        public float HorizontalHalfView=60,VerticalHalfView=40,ResponseSeconds=.25f;
        public float HorizontalControlLimit=.5f,VerticalControlLimit=.35f;
        public int OcclusionMask=~0;public ulong TargetId{get;private set;}
        public float LeftControl{get;private set;}public float UpControl{get;private set;}
        public int RaysThisTick{get;private set;}public int SaturatedRayQueries{get;private set;}
        public Vector3 Origin=>head.TransformPoint(localOrigin);
        public Vector3 EyeForward=>head.TransformDirection(Vector3.back).normalized;
        public NearestPlayerGaze(ulong ownerId,Transform ownerRoot,Transform viewFrame,Transform headFrame,EyePoseLayer poseLayer)
        {
            if(ownerId==0||ownerRoot==null||viewFrame==null||headFrame==null||poseLayer==null)throw new ArgumentException("Explicit owner/view/head required");
            self=ownerId;owner=ownerRoot;view=viewFrame;head=headFrame;layer=poseLayer;
            localOrigin=head.InverseTransformPoint((layer.Left.position+layer.Right.position)*.5f);
        }
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
        bool Visible(Vector3 origin,Vector3 target,Transform targetRoot)
        {
            Vector3 delta=target-origin;float distance=delta.magnitude;if(distance<.00001f)return false;
            RaysThisTick++;
            int count=Physics.RaycastNonAlloc(origin,delta/distance,hits,distance,OcclusionMask,QueryTriggerInteraction.Ignore);
            if(count==hits.Length){SaturatedRayQueries++;return false;}
            for(int i=0;i<count;i++)
            {
                var c=hits[i].collider;if(c==null)continue;var t=c.transform;
                if(t==owner||t.IsChildOf(owner)||t==targetRoot||t.IsChildOf(targetRoot))continue;
                return false;
            }
            return true;
        }
        public void Tick(IReadOnlyList<GazeParticipant> players,float dt,bool enabled,bool reset=false)
        {
            RaysThisTick=0;
            if(!enabled){Reset();return;}
            if(!Finite(dt)||dt<0){Reset();return;}
            if(dt==0)return;
            Vector3 origin=Origin,forward=EyeForward;
            if(!Finite(origin)||!Finite(forward)||ResponseSeconds<=0||!Finite(ResponseSeconds)){Reset();return;}
            if(!initialized||reset||dt>.25f){worldDirection=forward;initialized=true;dt=Mathf.Min(dt,1f/30);}
            Vector3 bestPoint=Vector3.zero;float bestDistance=float.MaxValue;ulong bestId=0;
            Vector3 vf=view.forward.normalized,vu=view.up.normalized,vr=Vector3.Cross(vu,vf).normalized;
            for(int i=0;i<players.Count;i++)
            {
                var p=players[i];if(p.Id==0||p.Id==self||!p.Active||p.Root==null||p.Head==null||!p.Root.gameObject.activeInHierarchy||!p.Head.gameObject.activeInHierarchy)continue;
                Vector3 point=p.Head.position,delta=point-origin;if(!Finite(point))continue;
                float distance=delta.sqrMagnitude;if(distance<1e-10f||!Finite(distance)||distance>bestDistance||(distance==bestDistance&&bestId!=0&&p.Id>=bestId))continue;
                float z=Vector3.Dot(delta,vf);if(z<=0)continue;
                float x=Vector3.Dot(delta,vr),y=Vector3.Dot(delta,vu);
                if(Mathf.Abs(Mathf.Atan2(x,z)*Mathf.Rad2Deg)>HorizontalHalfView||Mathf.Abs(Mathf.Atan2(y,Mathf.Sqrt(x*x+z*z))*Mathf.Rad2Deg)>VerticalHalfView)continue;
                if(!Visible(origin,point,p.Root))continue;
                bestId=p.Id;bestDistance=distance;bestPoint=point;
            }
            TargetId=bestId;
            if(bestId==0){Relax(dt);layer.Apply(LeftControl,UpControl);return;}
            Vector3 desired=(bestPoint-origin).normalized;
            // Store direction in world space, then reproject using this frame's
            // head pose. Turning the head must not rotate old local gaze twice.
            worldDirection=Vector3.Slerp(worldDirection,desired,1-Mathf.Exp(-dt/ResponseSeconds)).normalized;
            float front=Vector3.Dot(worldDirection,forward),left=Vector3.Dot(worldDirection,head.right),up=Vector3.Dot(worldDirection,head.up);
            if(front<=.0001f){Relax(dt);}
            else {
                LeftControl=Mathf.Clamp(Mathf.Atan2(left,front)*Mathf.Rad2Deg/45,-HorizontalControlLimit,HorizontalControlLimit);
                UpControl=Mathf.Clamp(Mathf.Atan2(up,Mathf.Sqrt(left*left+front*front))*Mathf.Rad2Deg/30,-VerticalControlLimit,VerticalControlLimit);
            }
            // Keep history at the actual constrained gaze direction; otherwise
            // a long out-of-range target leaves invisible angular lag on return.
            worldDirection=DirectionFromControls();
            layer.Apply(LeftControl,UpControl);
        }
        Vector3 DirectionFromControls()
        {
            float yaw=LeftControl*45*Mathf.Deg2Rad,pitch=UpControl*30*Mathf.Deg2Rad;
            return (EyeForward*(Mathf.Cos(pitch)*Mathf.Cos(yaw))+head.right*(Mathf.Cos(pitch)*Mathf.Sin(yaw))+head.up*Mathf.Sin(pitch)).normalized;
        }
        void Relax(float dt)
        {
            float decay=Mathf.Exp(-dt/ResponseSeconds);LeftControl*=decay;UpControl*=decay;
            if(Mathf.Abs(LeftControl)<.0001f)LeftControl=0;if(Mathf.Abs(UpControl)<.0001f)UpControl=0;
            // A head turn or lost target changes the frame, not the old local
            // eye amount. Decay that amount instead of snapping at 90 degrees.
            worldDirection=DirectionFromControls();
        }
        public void Reset()
        {
            layer.Restore();initialized=false;TargetId=0;LeftControl=UpControl=0;worldDirection=Vector3.zero;
        }
    }
}
