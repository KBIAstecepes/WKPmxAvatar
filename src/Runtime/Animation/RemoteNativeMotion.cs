using System;
using UnityEngine;

namespace WKPmxAvatar
{
    public enum RemoteMotionPhase { Ground, Rising, Falling, Landing, Suspended }

    // Input is observed remote motion, not an animation trajectory. This class never writes
    // the remote root, its physics, network data, hand targets or item transforms.
    public sealed class RemoteNativeMotion
    {
        readonly MotionPlayer motion;readonly DistanceLocomotion gait;readonly Transform[] bones;readonly NativeBackDodge backDodge;
        public bool UseBackwardDodge=true;public float BackDodgeWeight=>backDodge?.Weight??0;
        public float BackwardDodgeSpeed{get=>backDodge?.MinimumSpeed??4;set{if(backDodge!=null)backDodge.MinimumSpeed=value;}}
        public float BodyLookOffset{get=>gait.BodyLookOffset;set=>gait.BodyLookOffset=value;}
        readonly Transform leftFoot,rightFoot;readonly float height;
        readonly float[] durations=new float[200];
        readonly Vector3[] previousP,fromP,workP;readonly Quaternion[] previousQ,fromQ,workQ;
        bool initialized,havePrevious,left;float phaseTime,blendTime,offContact,airTime,lastDownSpeed;
        int takeoff=164,fall=162,land=182;
        public RemoteMotionPhase Phase {get;private set;}
        public float Cycles=>gait.Cycles;public float Travel=>gait.Travel;public float Stride=>gait.CurrentStride;
        public int LandingClip=>land;public int TakeoffClip=>takeoff;
        public float PoseSpeed {get;private set;}public float ObservedSpeed {get;private set;}
        public string Status=>"Remote original: "+Phase+"; "+gait.Status;
        public void Suspend(){initialized=false;gait.ResetPlants();}
        public RemoteNativeMotion(MotionPlayer player,Transform[] target,string folder,float modelHeight)
        {
            motion=player;bones=target;height=modelHeight;
            previousP=new Vector3[bones.Length];fromP=new Vector3[bones.Length];workP=new Vector3[bones.Length];
            previousQ=new Quaternion[bones.Length];fromQ=new Quaternion[bones.Length];workQ=new Quaternion[bones.Length];
            leftFoot=Array.Find(bones,b=>b.name=="左足首");rightFoot=Array.Find(bones,b=>b.name=="右足首");
            if(leftFoot==null||rightFoot==null)throw new InvalidOperationException("Native remote foot bones missing");
            #if WKPMX_DEV
            motion.ManualDrive=true;
            motion.Play(folder,7,bones,height);
#else
            motion.Initialize(folder,bones,height);
#endif
            durations[7]=motion.PreloadExternal(folder,7,bones);
            foreach(int id in NativeAirSelection.Required)durations[id]=motion.PreloadExternal(folder,id,bones);
            gait=new DistanceLocomotion(motion.transform,bones,folder,height);Capture();
            gait.EnableDirections(folder);if(System.IO.File.Exists(System.IO.Path.Combine(folder,"clip-72-pmx.wkpmotion"))&&System.IO.File.Exists(System.IO.Path.Combine(folder,"clip-73-pmx.wkpmotion")))backDodge=new NativeBackDodge(folder,bones,height);
        }
        public void Capture()
        {for(int i=0;i<bones.Length;i++){previousP[i]=bones[i].localPosition;previousQ[i]=bones[i].localRotation;}havePrevious=true;}
        void Change(RemoteMotionPhase next)
        {
            Phase=next;phaseTime=blendTime=0;gait.ResetPlants();
            if(havePrevious)for(int i=0;i<bones.Length;i++){fromP[i]=previousP[i];fromQ[i]=previousQ[i];}
        }
        public bool Sample(float dt,Vector3 displacement,float verticalSpeed,bool contact,bool hanging,bool reset,Vector3 forward,float turn,float presentationSpeed=-1)
        {
            if(dt<=0||dt>0.25f||!float.IsFinite(dt)||!float.IsFinite(displacement.sqrMagnitude)||!float.IsFinite(verticalSpeed)){initialized=false;return false;}
            Vector3 planar=Vector3.ProjectOnPlane(displacement,Vector3.up);float distance=planar.magnitude;
            ObservedSpeed=distance/dt;
            float speed=presentationSpeed>=0&&float.IsFinite(presentationSpeed)?presentationSpeed:ObservedSpeed;
            bool fresh=reset||!initialized||displacement.sqrMagnitude>9;
            if(fresh){initialized=true;gait.Reset();backDodge?.Reset();offContact=airTime=lastDownSpeed=0;Change(hanging?RemoteMotionPhase.Suspended:contact&&verticalSpeed<0.5f?RemoteMotionPhase.Ground:verticalSpeed>0.3f?RemoteMotionPhase.Rising:RemoteMotionPhase.Falling);distance=0;speed=0;ChooseAir(speed,forward);}
            PoseSpeed=speed;
            offContact=contact?0:offContact+dt;
            // Ignore brief contact loss on stairs; a real upward departure bypasses the grace.
            bool grounded=!hanging&&contact&&verticalSpeed<0.5f;
            if(!grounded&&!hanging&&Phase==RemoteMotionPhase.Ground&&offContact<0.06f&&Mathf.Abs(verticalSpeed)<0.5f)grounded=true;
            if(hanging){backDodge?.Reset();if(Phase!=RemoteMotionPhase.Suspended)Change(RemoteMotionPhase.Suspended);airTime=0;return false;}
            bool wasAir=Phase==RemoteMotionPhase.Rising||Phase==RemoteMotionPhase.Falling;
            if(grounded){
                if(wasAir&&airTime>0.08f&&!fresh){land=NativeAirSelection.Landing(NativeAirSelection.SpeedTier(speed,gait.WalkReferenceSpeed,gait.RunReferenceSpeed),left,lastDownSpeed>6.2f*height/2.1f);Change(RemoteMotionPhase.Landing);}
                else if(Phase==RemoteMotionPhase.Suspended||wasAir)Change(RemoteMotionPhase.Ground);
                airTime=0;
            }else{
                var next=verticalSpeed>0.3f&&Phase!=RemoteMotionPhase.Falling?RemoteMotionPhase.Rising:RemoteMotionPhase.Falling;
                if(!wasAir){ChooseAir(speed,forward);airTime=0;lastDownSpeed=0;}
                if(Phase!=next)Change(next);
                airTime+=dt;lastDownSpeed=Mathf.Max(lastDownSpeed,-verticalSpeed);
            }
            phaseTime+=dt;blendTime+=dt;
            if(Phase==RemoteMotionPhase.Landing&&phaseTime>=0.20f)Change(RemoteMotionPhase.Ground);
            if(Phase==RemoteMotionPhase.Ground||Phase==RemoteMotionPhase.Landing){
                // A +/-180 direction wrap is the same backwards travel, not a request to
                // switch between opposite full-weight lean clips. Keep lean continuous.
                gait.Sample(distance,speed,false,Mathf.Sin(turn*Mathf.PI)*0.5f);motion.transform.localPosition=Vector3.up*motion.BaseFeetOffset;
                if(speed>0.05f)gait.SampleDirectional(distance,turn*180,dt,speed);
                if(Phase==RemoteMotionPhase.Landing){
                    for(int i=0;i<bones.Length;i++){workP[i]=bones[i].localPosition;workQ[i]=bones[i].localRotation;}
                    motion.SampleExternal(land,Mathf.Clamp01(phaseTime/durations[land]),true);
                    float recovery=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.025f,0.18f,phaseTime));
                    for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(bones[i].localPosition,workP[i],recovery);bones[i].localRotation=Quaternion.Slerp(bones[i].localRotation,workQ[i],recovery);}
                    motion.transform.localPosition=Vector3.up*(motion.BaseFeetOffset+motion.VisualLift*(1-recovery));
                }
            }else if(Phase==RemoteMotionPhase.Rising)motion.SampleExternal(takeoff,Mathf.Clamp01(phaseTime/durations[takeoff]),false);
            else motion.SampleExternal(fall,Mathf.Repeat(phaseTime/durations[fall],1),false);
            if(Phase==RemoteMotionPhase.Ground)backDodge?.Sample(distance,turn*180,speed,dt,UseBackwardDodge);else backDodge?.Reset();
            float w=fresh?1:Mathf.SmoothStep(0,1,Mathf.Clamp01(blendTime/0.10f));
            if(w<1&&havePrevious)for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(fromP[i],bones[i].localPosition,w);bones[i].localRotation=Quaternion.Slerp(fromQ[i],bones[i].localRotation,w);}
            return true;
        }
        void ChooseAir(float speed,Vector3 forward)
        {left=Vector3.Dot(leftFoot.position-rightFoot.position,forward)>=0;takeoff=NativeAirSelection.Takeoff(NativeAirSelection.SpeedTier(speed,gait.WalkReferenceSpeed,gait.RunReferenceSpeed),left);fall=NativeAirSelection.Fall(left);}
    }
}

