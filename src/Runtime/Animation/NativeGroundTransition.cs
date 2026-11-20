using System;using System.Collections.Generic;using UnityEngine;
namespace WKPmxAvatar
{
    public enum GroundTransitionKind {None,Start,Stop,Slowdown,Turn}
    // Visual transition layer over distance-driven gait; never moves the actual actor.
    public sealed class NativeGroundTransition
    {
        public static readonly int[] Required={40,41,44,45,49,50,53,54,57,58,63,64,82,83,86,87,88,89,90,91,92,93,141,142,146,147};
        readonly MotionPlayer motion;readonly Transform[] bones;readonly Dictionary<int,float> durations=new Dictionary<int,float>();
        readonly Vector3[] baseP,firstP;readonly Quaternion[] baseQ,firstQ;
        float phase,mix,referenceSpeed,timeBudget;int a,b;
        public float Phase=>phase;
        public GroundTransitionKind Kind {get;private set;}
        public int ClipA=>a;public int ClipB=>b;public float AngleBlend=>mix;
        public float DurationOf(int clip)=>durations[clip];
        public string Status=>Kind==GroundTransitionKind.None?"Ground loop":Kind+" (original "+a+(a==b?"":" / "+b)+")";
        public NativeGroundTransition(MotionPlayer player,Transform[] rig,string folder)
        {motion=player;bones=rig;baseP=new Vector3[rig.Length];firstP=new Vector3[rig.Length];baseQ=new Quaternion[rig.Length];firstQ=new Quaternion[rig.Length];foreach(int id in Required)durations[id]=motion.PreloadExternal(folder,id,rig);}
        public void Reset(){Kind=GroundTransitionKind.None;phase=0;}
        public void Begin(GroundTransitionKind kind,int tier,float angle,bool left,float nominalSpeed,float remainingTime=0)
        {
            Kind=kind;phase=0;mix=0;referenceSpeed=Mathf.Max(0.1f,nominalSpeed);timeBudget=Mathf.Max(0,remainingTime);
            if(kind==GroundTransitionKind.Start&&tier<=1){
                int[] clips=angle>0.01f?new[]{54,63,64}:angle< -0.01f?new[]{41,50,49}:left?new[]{54,63,64}:new[]{41,50,49};
                float turns=Mathf.Clamp(Mathf.Abs(angle)/90,0,2);int lo=Mathf.Min(1,Mathf.FloorToInt(turns));a=clips[lo];b=clips[lo+1];mix=turns-lo;
            }
            else if(kind==GroundTransitionKind.Start){a=b=tier==3?(left?57:44):Mathf.Abs(angle)>100?(left?58:45):(left?53:40);}
            else if(kind==GroundTransitionKind.Stop){a=b=tier==3?(left?87:86):tier==2?(left?89:88):(left?91:90);}
            else if(kind==GroundTransitionKind.Slowdown){a=b=left?83:82;}
            else if(kind==GroundTransitionKind.Turn){a=b=angle<0?(Mathf.Abs(angle)>135?146:147):(Mathf.Abs(angle)>135?142:141);}
            else {Reset();}
        }
        public bool Apply(float dt,float speed)
        {
            if(Kind==GroundTransitionKind.None)return false;
            float duration=Mathf.Lerp(durations[a],durations[b],mix);
            // Retiming affects only this transitional pose; loop phase remains distance-driven.
            float rate=Kind==GroundTransitionKind.Start?Mathf.Max(0.35f,speed/referenceSpeed):1;
            bool timedStop=Kind==GroundTransitionKind.Stop&&timeBudget>0;
            phase+=Mathf.Max(0,dt)*(timedStop?1/timeBudget:rate/duration);
            // A committed stop owns this stage until contact speed reaches zero. Never re-enter
            // the moving loop just because the source clip was shorter than the braking interval.
            if(phase>=1){if(timedStop)phase=1;else {Reset();return false;}}
            float w=Kind==GroundTransitionKind.Start?1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.25f,0.65f,phase)):
                Mathf.SmoothStep(0,1,Mathf.Clamp01(phase/0.15f))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.75f,1,phase)));
            if(w<0.001f)return false;
            Vector3 originalRoot=motion.transform.localPosition;
            for(int i=0;i<bones.Length;i++){baseP[i]=bones[i].localPosition;baseQ[i]=bones[i].localRotation;}
            motion.SampleExternal(a,phase,true);
            if(a!=b&&mix>0){for(int i=0;i<bones.Length;i++){firstP[i]=bones[i].localPosition;firstQ[i]=bones[i].localRotation;}motion.SampleExternal(b,phase,true);
                for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(firstP[i],bones[i].localPosition,mix);bones[i].localRotation=Quaternion.Slerp(firstQ[i],bones[i].localRotation,mix);}}
            for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(baseP[i],bones[i].localPosition,w);bones[i].localRotation=Quaternion.Slerp(baseQ[i],bones[i].localRotation,w);}
            motion.transform.localPosition=Vector3.Lerp(originalRoot,motion.transform.localPosition,w);return true;
        }
    }
}
