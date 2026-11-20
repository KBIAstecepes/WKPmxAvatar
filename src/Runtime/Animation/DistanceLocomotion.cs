using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WKPmxAvatar
{
    // Calibrate stance-foot travel on this exact rig, then advance cycles by measured root distance.
    public sealed class DistanceLocomotion
    {
        sealed class Profile {public MotionClip Clip,Left,Right;public float Stride,Speed,Offset,Floor;}
        readonly Transform model;readonly Transform[] bones;readonly Transform[] feet,knees,legs;
        readonly Transform pelvis,upperBody;
        readonly Vector3[] bufferP,leanP;readonly Quaternion[] bufferQ,leanQ;readonly Profile walk,run,sprint;
        readonly MotionClip idle;readonly float scale;
        Profile[] directions;float directionCycles,directionWeight,directionAngle;
        float runningTurn;public float BodyLookOffset;public float RunningTurn=>runningTurn;
        public float DirectionWeight=>directionWeight;public float DirectionCycles=>directionCycles;
        public float DirectionStride {get;private set;}public float DirectionStretch {get;private set;}
        readonly bool[] planted=new bool[2];readonly Vector3[] anchors=new Vector3[2],previousFeet=new Vector3[2];bool haveFeet;
        public float Cycles {get;private set;}public float Travel {get;private set;}public float RunWeight {get;private set;}
        public float WalkStride=>walk.Stride;public float RunStride=>run.Stride;public float CurrentStride {get;private set;}
        public float WalkReferenceSpeed=>walk.Speed;
        public float RunReferenceSpeed=>run.Speed;
        public float SprintWeight {get;private set;}
        public float LastCycleAdvance {get;private set;}public float MaxPlantCorrection {get;private set;}
        public string Status=>"Distance gait: run "+RunWeight.ToString("P0")+", sprint "+SprintWeight.ToString("P0");
        public DistanceLocomotion(Transform target,Transform[] rig,string folder,float height)
        {
            model=target;bones=rig;scale=height/2.1f;bufferP=new Vector3[rig.Length];bufferQ=new Quaternion[rig.Length];
            leanP=new Vector3[rig.Length];leanQ=new Quaternion[rig.Length];
            Transform find(string n)=>Array.Find(bones,b=>b.name==n)??throw new InvalidOperationException("Gait bone missing: "+n);
            feet=new[]{find("左足首"),find("右足首")};knees=new[]{find("左ひざ"),find("右ひざ")};legs=new[]{find("左足"),find("右足")};
            pelvis=find("腰");upperBody=find("上半身");
            MotionClip load(int id)=>MotionClip.Load(Path.Combine(folder,"clip-"+id+"-pmx.wkpmotion"),rig);
            Save();walk=Calibrate(load(10));run=Calibrate(load(19));sprint=Calibrate(load(28));idle=load(7);Restore();
            walk.Left=load(17);walk.Right=load(18);run.Left=load(26);run.Right=load(27);sprint.Left=load(35);sprint.Right=load(36);
        }
        void Save(){for(int i=0;i<bones.Length;i++){bufferP[i]=bones[i].localPosition;bufferQ[i]=bones[i].localRotation;}}
        void Restore(){for(int i=0;i<bones.Length;i++){bones[i].localPosition=bufferP[i];bones[i].localRotation=bufferQ[i];}}
        Profile Calibrate(MotionClip clip,Vector3? travelDirection=null)
        {
            int n=clip.Frames;var points=new Vector3[2,n];float[] floor={float.MaxValue,float.MaxValue};float minZ=float.MaxValue;int strike=0;
            Vector3 backAxis=-(travelDirection??Vector3.back);
            for(int j=0;j<n;j++){clip.Sample(bones,j/(float)(n-1),scale);for(int f=0;f<2;f++){points[f,j]=model.InverseTransformPoint(feet[f].position);floor[f]=Mathf.Min(floor[f],points[f,j].y);}float p=Vector3.Dot(points[0,j],backAxis);if(p<minZ){minZ=p;strike=j;}}
            var speeds=new List<float>();for(int f=0;f<2;f++)for(int j=1;j<n;j++){float back=Vector3.Dot(points[f,j]-points[f,j-1],backAxis)*clip.Rate;if(back>0.1f*scale&&points[f,j].y<floor[f]+0.08f*scale)speeds.Add(back);}
            if(speeds.Count<8)throw new InvalidOperationException("Insufficient planted-foot samples for gait calibration");speeds.Sort();float speed=speeds[speeds.Count/2];
            if(speed<0.3f*scale||speed>12*scale)throw new InvalidOperationException("Invalid calibrated gait speed");
            return new Profile{Clip=clip,Stride=speed*clip.Duration,Speed=speed,Offset=strike/(float)(n-1),Floor=Mathf.Min(floor[0],floor[1])};
        }
        public void Reset(){Cycles=Travel=directionCycles=directionWeight=directionAngle=runningTurn=0;ResetPlants();}
        public void ResetPlants(){planted[0]=planted[1]=false;haveFeet=false;}
        public void EnableDirections(string folder)
        {
            // The source fr/fl clips are diagonal, not cardinal strafes. Projecting their
            // planted-foot travel onto a side axis underestimated stride and raised cadence.
            Save();int[] ids={117,118,121,115};Vector3[] travel={Vector3.back,new Vector3(-1,0,-1).normalized,Vector3.forward,new Vector3(1,0,-1).normalized};directions=new Profile[4];
            for(int i=0;i<4;i++)directions[i]=Calibrate(MotionClip.Load(Path.Combine(folder,"clip-"+ids[i]+"-pmx.wkpmotion"),bones),travel[i]);Restore();
        }
        public void SampleDirectional(float distance,float angle,float dt,float presentationSpeed=-1)
        {
            if(directions==null)return;
            // At running speed, front diagonals and strafes use the long native run stride, with legs
            // oriented into actual travel and the upper body keeping its look direction.
            // Slowing retains directional walking. Fade back to backwards gait beyond strafing.
            float runDiagonal=RunWeight*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(95,120,Mathf.Abs(angle))));
            float goal=Mathf.SmoothStep(0,1,Mathf.InverseLerp(15,65,Mathf.Abs(angle)))*(1-runDiagonal);directionWeight=Mathf.MoveTowards(directionWeight,goal,dt*8);
            if(directionWeight<=0){directionCycles=Cycles;directionAngle=angle;TurnRunningLegs(angle,runDiagonal,dt);return;}
            // A tiny velocity near a reversal has an unstable heading. Do not swap
            // opposite 70-degree pelvis poses in one frame when the sign changes.
            directionAngle=Mathf.MoveTowardsAngle(directionAngle,angle,360*dt);
            float sector=Mathf.Repeat(directionAngle/90,4);int a=Mathf.FloorToInt(sector),b=(a+1)%4;float mix=sector-a;
            float baseStride=Mathf.Lerp(directions[a].Stride,directions[b].Stride,mix),speed=presentationSpeed>=0?presentationSpeed:distance/Mathf.Max(0.0001f,dt);
            DirectionStretch=Mathf.Clamp(speed/(baseStride*1.1f),1,1.30f);DirectionStride=baseStride*DirectionStretch;
            directionCycles+=Mathf.Max(0,distance)/DirectionStride;
            Save();directions[a].Clip.Sample(bones,Mathf.Repeat(directionCycles+directions[a].Offset,1),scale);
            for(int i=0;i<bones.Length;i++){leanP[i]=bones[i].localPosition;leanQ[i]=bones[i].localRotation;}
            directions[b].Clip.Sample(bones,Mathf.Repeat(directionCycles+directions[b].Offset,1),scale);
            for(int i=0;i<bones.Length;i++){
                string name=bones[i].name;bool lower=name=="腰"||name=="左足"||name=="右足"||name=="左ひざ"||name=="右ひざ"||name=="左足首"||name=="右足首"||name=="左つま先"||name=="右つま先";
                bones[i].localPosition=lower?Vector3.Lerp(bufferP[i],Vector3.Lerp(leanP[i],bones[i].localPosition,mix),directionWeight):bufferP[i];
                bones[i].localRotation=lower?Quaternion.Slerp(bufferQ[i],Quaternion.Slerp(leanQ[i],bones[i].localRotation,mix),directionWeight):bufferQ[i];
            }
            // Reduced cadence is paired with a longer visible foot excursion. Do not merely
            // slow a short-step clip and reintroduce sliding or invent a gameplay jump.
            Vector3 axis=model.TransformDirection(new Vector3(-Mathf.Sin(directionAngle*Mathf.Deg2Rad),0,-Mathf.Cos(directionAngle*Mathf.Deg2Rad)));
            Transform hips=Array.Find(bones,b=>b.name=="腰");
            for(int f=0;f<2;f++){Vector3 offset=feet[f].position-hips.position;Vector3 extension=axis*(Vector3.Dot(offset,axis)*(DirectionStretch-1)*directionWeight);extension=Vector3.ClampMagnitude(extension,0.09f*scale);ContactSolver.Solve(legs[f],knees[f],feet[f],feet[f].position+extension,model.parent!=null?model.parent.forward:-model.forward);}
            TurnRunningLegs(angle,runDiagonal,dt);
        }
        void TurnRunningLegs(float angle,float weight,float dt)
        {
            // Avoid a one-frame 160-degree pelvis flip on left/right reversals. Keep room
            // for a bounded chest/neck chain rather than demanding a fully front-facing torso.
            float goal=(Mathf.Clamp(angle,-80,80)+BodyLookOffset)*weight;
            runningTurn=Mathf.MoveTowardsAngle(runningTurn,goal,180*dt);
            if(Mathf.Abs(runningTurn)<0.001f)return;Quaternion upper=upperBody.rotation;
            pelvis.rotation=Quaternion.AngleAxis(runningTurn,Vector3.up)*pelvis.rotation;
            upperBody.rotation=upper;
        }
        void SampleProfile(Profile p,float turn)
        {
            float phase=Mathf.Repeat(Cycles+p.Offset,1);p.Clip.Sample(bones,phase,scale);float w=Mathf.Clamp01(Mathf.Abs(turn));if(w<0.001f)return;
            for(int i=0;i<bones.Length;i++){leanP[i]=bones[i].localPosition;leanQ[i]=bones[i].localRotation;}
            (turn<0?p.Left:p.Right).Sample(bones,phase,scale);
            for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(leanP[i],bones[i].localPosition,w);bones[i].localRotation=Quaternion.Slerp(leanQ[i],bones[i].localRotation,w);}
        }
        public void Sample(float distance,float speed,bool plantFeet,float turn=0)
        {
            RunWeight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(walk.Speed*1.05f,run.Speed*0.9f,speed));
            SprintWeight=Mathf.SmoothStep(0,1,Mathf.InverseLerp(run.Speed*1.05f,sprint.Speed*0.95f,speed));
            CurrentStride=Mathf.Lerp(Mathf.Lerp(walk.Stride,run.Stride,RunWeight),sprint.Stride,SprintWeight);LastCycleAdvance=Mathf.Max(0,distance)/CurrentStride;Cycles+=LastCycleAdvance;Travel+=Mathf.Max(0,distance);
            SampleProfile(walk,turn);Save();SampleProfile(run,turn);
            for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(bufferP[i],bones[i].localPosition,RunWeight);bones[i].localRotation=Quaternion.Slerp(bufferQ[i],bones[i].localRotation,RunWeight);}
            if(SprintWeight>0){Save();SampleProfile(sprint,turn);for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(bufferP[i],bones[i].localPosition,SprintWeight);bones[i].localRotation=Quaternion.Slerp(bufferQ[i],bones[i].localRotation,SprintWeight);}}
            float moving=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.03f,0.4f*scale,speed));
            if(moving<1){Save();idle.Sample(bones,0,scale);for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(bones[i].localPosition,bufferP[i],moving);bones[i].localRotation=Quaternion.Slerp(bones[i].localRotation,bufferQ[i],moving);}}
            float low=Mathf.Lerp(Mathf.Lerp(walk.Floor,run.Floor,RunWeight),sprint.Floor,SprintWeight)+0.065f*scale;
            for(int f=0;f<2;f++){
                Vector3 local=model.InverseTransformPoint(feet[f].position);bool stance=plantFeet&&moving>0.99f&&haveFeet&&local.y<low&&local.z>=previousFeet[f].z-0.002f*scale;
                if(!stance)planted[f]=false;
                else {if(!planted[f]){anchors[f]=feet[f].position;planted[f]=true;}Vector3 offset=anchors[f]-feet[f].position;offset.y=0;
                    if(offset.magnitude>0.12f*scale)planted[f]=false;
                    else {MaxPlantCorrection=Mathf.Max(MaxPlantCorrection,offset.magnitude);ContactSolver.Solve(legs[f],knees[f],feet[f],feet[f].position+offset,model.parent!=null?model.parent.forward:-model.forward);}}
                previousFeet[f]=local;
            }
            haveFeet=plantFeet;
        }
    }
}
