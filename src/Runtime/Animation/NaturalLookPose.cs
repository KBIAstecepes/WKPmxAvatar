using System;
using UnityEngine;
namespace WKPmxAvatar
{
    // Joint limits are relative to the moving parent frame, including acrobatic body tilt.
    public sealed class NaturalLookPose
    {
        public const float MaxSpineYaw=40,MaxNeckYaw=45;
        readonly Transform pelvis,chest,head;readonly Quaternion pelvisAxis,chestAxis,headAxis;
        bool initialized;float chestYaw,headYaw;
        public float SpineTwist {get;private set;}public float NeckTwist {get;private set;}
        public NaturalLookPose(Transform model,Transform[] bones,float modelYaw)
        {
            pelvis=Array.Find(bones,b=>b.name=="腰");chest=Array.Find(bones,b=>b.name=="上半身");head=Array.Find(bones,b=>b.name=="頭");
            if(pelvis==null||chest==null||head==null)throw new InvalidOperationException("Look chain missing");
            Quaternion facing=model.rotation*Quaternion.Euler(0,-modelYaw,0);
            pelvisAxis=Quaternion.Inverse(pelvis.rotation)*facing;chestAxis=Quaternion.Inverse(chest.rotation)*facing;headAxis=Quaternion.Inverse(head.rotation)*facing;
        }
        static float Twist(Quaternion q)=>q.y*q.y+q.w*q.w>0.00000001f?Mathf.DeltaAngle(0,2*Mathf.Atan2(q.y,q.w)*Mathf.Rad2Deg):0;
        static float LookInFrame(Quaternion frame,Vector3 look,float fallback){Vector3 p=Quaternion.Inverse(frame)*look;return p.x*p.x+p.z*p.z>0.00001f?Mathf.Atan2(p.x,p.z)*Mathf.Rad2Deg:fallback;}
        public void Reset(){initialized=false;}
        public void Apply(float look,float speed,float dt,float tracking=1)
        {
            if(dt<=0)return;Quaternion pelvisFrame=pelvis.rotation*pelvisAxis,chestFrame=chest.rotation*chestAxis,headFrame=head.rotation*headAxis;
            float nativeChest=Twist(Quaternion.Inverse(pelvisFrame)*chestFrame),nativeHead=Twist(Quaternion.Inverse(chestFrame)*headFrame);
            if(!initialized){chestYaw=Mathf.Clamp(nativeChest,-MaxSpineYaw,MaxSpineYaw);headYaw=Mathf.Clamp(nativeHead,-MaxNeckYaw,MaxNeckYaw);initialized=true;}
            Vector3 lookDirection=Quaternion.Euler(0,look,0)*Vector3.forward;
            // Do not force an upright world-space look through a native dodge/large body tilt.
            float upright=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.1f,0.75f,Vector3.Dot(pelvisFrame*Vector3.up,Vector3.up)));
            float aim=Mathf.Clamp01(tracking)*upright;
            float delta=LookInFrame(pelvisFrame,lookDirection,nativeChest);
            float offset=speed>0.2f?delta*0.5f:delta-Mathf.Clamp(delta,-24,24);
            float chestGoal=Mathf.Lerp(Mathf.Clamp(nativeChest,-MaxSpineYaw,MaxSpineYaw),Mathf.Clamp(offset,-MaxSpineYaw,MaxSpineYaw),aim);
            chestYaw=Mathf.Clamp(Mathf.MoveTowardsAngle(chestYaw,chestGoal,160*dt),-MaxSpineYaw,MaxSpineYaw);
            chest.rotation=Quaternion.AngleAxis(Mathf.DeltaAngle(nativeChest,chestYaw),pelvisFrame*Vector3.up)*chest.rotation;
            chestFrame=chest.rotation*chestAxis;headFrame=head.rotation*headAxis;
            nativeHead=Twist(Quaternion.Inverse(chestFrame)*headFrame);
            float headGoal=Mathf.Lerp(Mathf.Clamp(nativeHead,-MaxNeckYaw,MaxNeckYaw),Mathf.Clamp(LookInFrame(chestFrame,lookDirection,nativeHead),-MaxNeckYaw,MaxNeckYaw),aim);
            headYaw=Mathf.Clamp(Mathf.MoveTowardsAngle(headYaw,headGoal,220*dt),-MaxNeckYaw,MaxNeckYaw);
            head.rotation=Quaternion.AngleAxis(Mathf.DeltaAngle(nativeHead,headYaw),chestFrame*Vector3.up)*head.rotation;
            Measure();
        }
        public void Measure(){Quaternion p=pelvis.rotation*pelvisAxis,c=chest.rotation*chestAxis,h=head.rotation*headAxis;SpineTwist=Twist(Quaternion.Inverse(p)*c);NeckTwist=Twist(Quaternion.Inverse(c)*h);}
    }
}