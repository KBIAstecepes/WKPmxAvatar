using System;using System.Collections.Generic;using System.IO;using UnityEngine;
namespace WKPmxAvatar {
// Development playback controls and synthetic preview height arcs.
public sealed partial class MotionPlayer {
MotionClip clip,idle,landing;float time;Vector3[] startPositions;Quaternion[] startRotations;bool jump,landingOnly;int clipIndex;

        public bool ManualDrive;
        public string Phase {get;private set;}="Stopped";
        public string Description {get;private set;}="Original motion stopped";
        public bool Playing=>clip!=null;
        
        
        public void Play(string folder,int index,Transform[] target,float height,bool heavyLanding=false)
        {
            MotionClip load(int id){if(!cache.TryGetValue(id,out var c)){c=MotionClip.Load(Path.Combine(folder,"clip-"+id+"-pmx.wkpmotion"),target);cache.Add(id,c);}return c;}
            // Validate the whole sequence before changing a working pose.
            var loaded=load(index);var loadedIdle=load(7);bool isJump=index==164;bool isLanding=index==182||index==185;
            var loadedLanding=isJump?load(heavyLanding?185:182):null;
            var beforeP=new Vector3[target.Length];var beforeQ=new Quaternion[target.Length];
            for(int i=0;i<target.Length;i++){beforeP[i]=target[i].localPosition;beforeQ[i]=target[i].localRotation;}
            clip=loaded;idle=loadedIdle;landing=loadedLanding;clipIndex=index;bones=target;time=0;heightScale=height/2.1f;jump=isJump;landingOnly=isLanding;
            startPositions=beforeP;startRotations=beforeQ;
            leftFoot=Array.Find(bones,b=>b.name=="左足首");rightFoot=Array.Find(bones,b=>b.name=="右足首");
            idle.Sample(bones,0,heightScale);groundFootY=FeetY();
            for(int i=0;i<bones.Length;i++){bones[i].localPosition=beforeP[i];bones[i].localRotation=beforeQ[i];}
            enabled=true;Advance(0);
        }
        void BlendPrevious(MotionClip previous,float blend)
        {
            blend=Mathf.SmoothStep(0,1,Mathf.Clamp01(blend));int at=(previous.Frames-1)*previous.BoneCount;
            for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(previous.Positions[at+i]*heightScale,bones[i].localPosition,blend);bones[i].localRotation=Quaternion.Slerp(previous.Rotations[at+i],bones[i].localRotation,blend);}
        }
        public void Advance(float dt)
        {
            if(clip==null)return;time+=Mathf.Max(0,dt);VisualLift=0;
            if(jump&&time<clip.Duration)
            {
                Phase="Takeoff";Description="Jump / land: takeoff";clip.Sample(bones,time/clip.Duration,heightScale);
                float u=Mathf.Clamp01((time-0.18f)/Mathf.Max(0.01f,clip.Duration-0.18f));VisualLift=4*0.75f*heightScale*u*(1-u);
            }
            else if(jump&&time<clip.Duration+landing.Duration)
            {
                float t=time-clip.Duration;Phase="Landing";Description="Jump / land: landing";landing.Sample(bones,t/landing.Duration,heightScale);BlendPrevious(clip,t/0.18f);
            }
            else if((jump&&time>=clip.Duration+landing.Duration)||(landingOnly&&time>=clip.Duration))
            {
                float t=time-(jump?clip.Duration+landing.Duration:clip.Duration);Phase="Idle";Description="Idle (jump / landing finished)";idle.Sample(bones,Mathf.Repeat(t/idle.Duration,1),heightScale);BlendPrevious(jump?landing:clip,t/0.2f);
            }
            else
            {
                Phase=landingOnly?"Landing":clip==idle?"Idle":"Locomotion";
                Description=landingOnly?"Landing":clip==idle?"Idle":clipIndex==10?"Walk":"Run";
                clip.Sample(bones,landingOnly?Mathf.Clamp01(time/clip.Duration):Mathf.Repeat(time/clip.Duration,1),heightScale);
            }
            if(time<0.16f){float w=Mathf.SmoothStep(0,1,time/0.16f);for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(startPositions[i],bones[i].localPosition,w);bones[i].localRotation=Quaternion.Slerp(startRotations[i],bones[i].localRotation,w);}}
            // A local preview height arc and whole-body floor correction; not network physics or full foot IK.
            if(jump||landingOnly)VisualLift+=Mathf.Clamp(groundFootY-FeetY()-VisualLift,0,0.4f*heightScale);
            transform.localPosition=Vector3.up*(BaseFeetOffset+VisualLift);
        }
        void LateUpdate(){if(!ManualDrive)Advance(Time.deltaTime);}
        

}
}
