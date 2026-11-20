using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WKPmxAvatar
{
    // A scripted visual route, deliberately independent of gameplay and network state.
    public sealed class ContinuousRoutePreview
    {
        sealed class Stage
        {
            public string Name;public float Duration;public int Clip,Interaction,Flight;
            public Vector3 A,B,V0,V1;
        }
        readonly Transform root;readonly Transform[] bones;readonly MotionPlayer motion;
        readonly ContactSolver interaction;readonly string folder;readonly float height;
        readonly Vector3 anchor;readonly Quaternion facing;readonly Action prepareHammer;
        readonly List<Stage> stages=new List<Stage>();readonly Vector3[] fromP;readonly Quaternion[] fromQ;
        readonly DistanceLocomotion gait;readonly Vector3[] gaitP;readonly Quaternion[] gaitQ;
        readonly float gravity,jumpHeight;Vector3 lastPosition;float landingDuration;
        readonly Dictionary<int,float> nativeDurations=new Dictionary<int,float>();readonly Transform leftFoot,rightFoot;
        readonly NativeGroundTransition ground;bool stopBegan;Quaternion turnStart;float directionalAmount;
        public GroundTransitionKind GroundKind=>ground.Kind;public float DirectionalAmount=>directionalAmount;
        public float GroundPhase=>ground.Phase;public float PlanarSpeed {get;private set;}
        public bool UseNativeStops {get;private set;}
        public bool Braking=>Active&&stages[stage].Clip!=7&&stages[stage].Flight==0&&stages[stage].V1.sqrMagnitude<0.01f&&stages[stage].Clip!=182;
        int takeoffClip=164,fallClip=162,landingClip=182;bool leadLeft;
        public int TakeoffClip=>takeoffClip;public int FallClip=>fallClip;public int LandingClip=>landingClip;
        public string NativeStatus=>ground.Status+"; original air "+takeoffClip+" / "+fallClip+" / "+landingClip;
        public float GaitCycles=>gait.Cycles;public float GaitTravel=>gait.Travel;public float LastCycleAdvance=>gait.LastCycleAdvance;public float CurrentStride=>gait.CurrentStride;
        public string GaitStatus=>gait.Status;
        int stage;float stageTime,elapsed;GameObject guides;Material guideMaterial;
        public bool Active {get;private set;}public bool Paused;public float Speed=1;
        public bool Holding=>Active&&stages[stage].Interaction==3;
        public float Elapsed=>elapsed;public float Duration {get;private set;}
        public string Description=>Active?stages[stage].Name:Finished?"Route finished":"Route stopped";
        public bool Finished {get;private set;}
        public int StageIndex=>stage;
        public ContinuousRoutePreview(Transform actor,Transform[] target,MotionPlayer playback,ContactSolver constraints,string motionFolder,float modelHeight,Vector3 origin,Quaternion rotation,Action hammer,float exampleJumpHeight=0.75f,float exampleGravity=9.81f,bool extendedRoute=false,bool nativeStops=false)
        {
            root=actor;bones=target;motion=playback;interaction=constraints;folder=motionFolder;height=modelHeight;anchor=origin;facing=rotation;prepareHammer=hammer;UseNativeStops=nativeStops;
            fromP=new Vector3[bones.Length];fromQ=new Quaternion[bones.Length];
            gaitP=new Vector3[bones.Length];gaitQ=new Quaternion[bones.Length];gait=new DistanceLocomotion(playback.transform,bones,folder,height);
            if(exampleJumpHeight<=0||exampleGravity<=0||!float.IsFinite(exampleJumpHeight)||!float.IsFinite(exampleGravity))throw new ArgumentOutOfRangeException("Invalid example flight parameters");
            jumpHeight=exampleJumpHeight;gravity=exampleGravity;
            leftFoot=Array.Find(bones,b=>b.name=="左足首");rightFoot=Array.Find(bones,b=>b.name=="右足首");
            foreach(int id in NativeAirSelection.Required)nativeDurations.Add(id,motion.PreloadExternal(folder,id,bones));
            ground=new NativeGroundTransition(motion,bones,folder);
            float duration(int id)=>MotionClip.Load(Path.Combine(folder,"clip-"+id+"-pmx.wkpmotion"),bones).Duration;
            // Validate required files before taking control of the preview.
            duration(164);landingDuration=duration(182);duration(7);duration(10);duration(19);
            float jump=2*Mathf.Sqrt(2*jumpHeight/gravity),drop=Mathf.Sqrt(2*0.9f/gravity);
            Vector3 p=Vector3.zero,v=Vector3.zero;
            void add(string name,float seconds,int clip,Vector3 end,Vector3 endVelocity,int pose=0,int flight=0){stages.Add(new Stage{Name=name,Duration=seconds,Clip=clip,Interaction=pose,Flight=flight,A=p,B=end,V0=v,V1=endVelocity});p=end;v=endVelocity;Duration+=seconds;}
            add("Idle / start",1.2f,7,p,Vector3.zero);
            add("Walk / accelerate",3,10,new Vector3(3,0,0),Vector3.right*1.6f);
            add("Run",2.5f,19,new Vector3(9,0,0),Vector3.right*2.8f);
            add("Run / turn",2,19,new Vector3(12,0,3),Vector3.forward*2);
            add("Jump / airborne",jump,164,new Vector3(12,0,3+2*jump),Vector3.forward*2,0,1);
            add("Light landing / keep moving",0.22f,182,p+Vector3.forward*0.44f,Vector3.forward*2);
            add("Walk / approach",1.5f,10,new Vector3(12,0,7),Vector3.zero);
            add("Reach / free hang",0.8f,7,new Vector3(12,0.9f,7),Vector3.zero,1);
            add("Free hang",1.8f,7,p,Vector3.zero,1);
            add("Wall support",1.8f,7,p,Vector3.zero,2);
            add("Release / drop",drop,7,new Vector3(12,0,7),Vector3.zero,0,2);
            add("Landing",0.25f,182,p,Vector3.zero);
            Vector3 brakePoint=new Vector3(3,0,1);float brakeSpeed=2.4f;
            add("Run / return",4,19,brakePoint,-brakePoint.normalized*brakeSpeed);
            // Constant deceleration: distance = initialSpeed * brakingTime / 2.
            // Matching the endpoint and its tangent prevents the Hermite curve from speeding up.
            add("Walk / stop",2*brakePoint.magnitude/brakeSpeed,10,Vector3.zero,Vector3.zero);
            if(extendedRoute){
                add("Stopped / observe",0.7f,7,p,Vector3.zero);
                add("Walk / restart opposite",2.5f,10,new Vector3(0,0,-2.5f),Vector3.back*1.5f);
                add("Run / build speed",2.5f,19,new Vector3(0,0,-8),Vector3.back*3.5f);
                add("Sprint / sustained",2.5f,19,new Vector3(0,0,-20),Vector3.back*5.5f);
                add("Sprint / wide turn",1.5f,19,new Vector3(-4,0,-24),Vector3.left*4.5f);
                add("Run / ease down",2,19,new Vector3(-10,0,-24),Vector3.left*2.5f);
                add("Walk / ease down",1.5f,10,new Vector3(-13,0,-24),Vector3.left*1.4f);
                add("Walk / second stop",2,10,new Vector3(-14.4f,0,-24),Vector3.zero);
                add("Stopped / restart pause",0.7f,7,p,Vector3.zero);
                Vector3 returnBrake=new Vector3(-3,0,-1);
                add("Run / long return",8.5f,19,returnBrake,-returnBrake.normalized*brakeSpeed);
                add("Walk / final stop",2*returnBrake.magnitude/brakeSpeed,10,Vector3.zero,Vector3.zero);
            }
            add("Turn in place",ground.DurationOf(141),7,p,Vector3.zero);
            add("Hold hammer",2.5f,7,p,Vector3.zero,3);
            add("Idle / finish",1.2f,7,p,Vector3.zero);
        }
        Vector3 Position(Stage s,float u)
        {float u2=u*u,u3=u2*u;Vector3 p=(2*u3-3*u2+1)*s.A+(u3-2*u2+u)*s.V0*s.Duration+(-2*u3+3*u2)*s.B+(u3-u2)*s.V1*s.Duration;
            float t=u*s.Duration;if(s.Flight==1)p.y=s.A.y+Mathf.Sqrt(2*gravity*jumpHeight)*t-0.5f*gravity*t*t;else if(s.Flight==2)p.y=Mathf.Max(s.B.y,s.A.y-0.5f*gravity*t*t);return p;}
        static Vector3 Velocity(Stage s,float u)
        {float u2=u*u;return ((6*u2-6*u)*s.A+(3*u2-4*u+1)*s.V0*s.Duration+(-6*u2+6*u)*s.B+(3*u2-2*u)*s.V1*s.Duration)/s.Duration;}
        void Capture(){for(int i=0;i<bones.Length;i++){fromP[i]=bones[i].localPosition;fromQ[i]=bones[i].localRotation;}}
        void Enter()
        {
            Capture();interaction.Stop();gait.ResetPlants();ground.Reset();stopBegan=false;var s=stages[stage];
            if(s.Flight!=0){
                leadLeft=Vector3.Dot(leftFoot.position-rightFoot.position,root.forward)>=0;fallClip=NativeAirSelection.Fall(leadLeft);
                takeoffClip=NativeAirSelection.Takeoff(NativeAirSelection.SpeedTier(s.V0.magnitude,gait.WalkReferenceSpeed,gait.RunReferenceSpeed),leadLeft);
                float expectedSpeed=stage+1<stages.Count?stages[stage+1].V0.magnitude:0;
                float impactSpeed=s.Flight==1?Mathf.Sqrt(2*gravity*jumpHeight):Mathf.Sqrt(2*gravity*(s.A.y-s.B.y));
                landingClip=NativeAirSelection.Landing(NativeAirSelection.SpeedTier(expectedSpeed,gait.WalkReferenceSpeed,gait.RunReferenceSpeed),leadLeft,impactSpeed>Mathf.Sqrt(2*gravity*2));
            }
            int clip=s.Flight==1?takeoffClip:s.Flight==2?fallClip:s.Clip==182?landingClip:s.Clip;
            if(s.Clip==182)landingDuration=nativeDurations[landingClip];
            motion.Play(folder,clip,bones,height);
            bool left=Vector3.Dot(leftFoot.position-rightFoot.position,root.forward)>=0;
            if(s.Name=="Turn in place"){turnStart=root.rotation;ground.Begin(GroundTransitionKind.Turn,0,90,left,gait.WalkReferenceSpeed);}
            else if(s.Clip==10||s.Clip==19){
                float estimate=Mathf.Max(s.V0.magnitude,s.V1.magnitude,Vector3.Distance(s.A,s.B)/s.Duration);
                int tier=NativeAirSelection.SpeedTier(estimate,gait.WalkReferenceSpeed,gait.RunReferenceSpeed);
                if(stage==0||stages[stage-1].V1.sqrMagnitude<0.01f){Vector3 travel=facing*(s.B-s.A);travel.y=0;ground.Begin(GroundTransitionKind.Start,tier,Vector3.SignedAngle(root.forward,travel,Vector3.up),left,tier>=2?gait.RunReferenceSpeed:gait.WalkReferenceSpeed);}
                // Stopping is one committed transition; do not start a separate slowdown
                // clip which would be interrupted by the stop a moment later.
                else if(s.Clip==10&&stages[stage-1].Clip==19&&s.V1.sqrMagnitude>0.01f)ground.Begin(GroundTransitionKind.Slowdown,1,0,left,gait.WalkReferenceSpeed);
            }
            int pose=stages[stage].Interaction;if(pose==1||pose==2)interaction.Climb(pose==1);else if(pose==3)prepareHammer?.Invoke();
        }
        public void Start()
        {
            stage=0;stageTime=elapsed=0;Paused=false;Finished=false;Active=true;motion.ManualDrive=interaction.ManualDrive=true;
            gait.Reset();PlanarSpeed=0;root.SetPositionAndRotation(anchor,facing);lastPosition=anchor;Enter();Render(0);
        }
        public void Tick(float dt)
        {
            if(!Active||Paused)return;
            float remaining=Mathf.Clamp(dt,0,0.25f)*Mathf.Clamp(Speed,0.1f,2);
            while(remaining>1e-7f&&Active)
            {
                float step=Mathf.Min(remaining,1f/60,stages[stage].Duration-stageTime);stageTime+=step;elapsed+=step;remaining-=step;Render(step);
                if(stageTime>=stages[stage].Duration-1e-6f){if(stage+1==stages.Count){Active=false;Finished=true;interaction.Stop();motion.ManualDrive=true;break;}stage++;stageTime=0;Enter();}
            }
        }
        void Render(float dt)
        {
            var s=stages[stage];float u=Mathf.Clamp01(stageTime/s.Duration);Vector3 velocity=facing*Velocity(s,u);velocity.y=0;
            Quaternion heading=root.rotation;if(velocity.sqrMagnitude>0.0025f)heading=Quaternion.RotateTowards(heading,Quaternion.LookRotation(velocity),180*dt);
            if(s.Name=="Turn in place")heading=Quaternion.Slerp(turnStart,turnStart*Quaternion.Euler(0,90,0),Mathf.SmoothStep(0,1,u));
            directionalAmount=velocity.sqrMagnitude>0.0025f?Mathf.Clamp(Vector3.SignedAngle(heading*Vector3.forward,velocity,Vector3.up)/60,-1,1):0;
            root.SetPositionAndRotation(anchor+facing*Position(s,u),heading);
            Vector3 travelled=root.position-lastPosition;travelled.y=0;float distance=travelled.magnitude,speed=dt>0?distance/dt:0;lastPosition=root.position;PlanarSpeed=velocity.magnitude;
            // Solve the target constraint pose first; this timeline owns the full-pose crossfade.
            // Blending the IK target itself through the shoulder can cross a singular arm configuration.
            motion.Advance(dt);
            if(s.Flight==1){
                motion.SampleExternal(takeoffClip,Mathf.Clamp01(stageTime/nativeDurations[takeoffClip]),false);
                float verticalSpeed=Mathf.Sqrt(2*gravity*jumpHeight)-gravity*stageTime;
                float fallBlend=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,-1.2f*height/2.1f,verticalSpeed));
                if(fallBlend>0){
                    for(int i=0;i<bones.Length;i++){gaitP[i]=bones[i].localPosition;gaitQ[i]=bones[i].localRotation;}
                    float descentTime=Mathf.Max(0,stageTime-Mathf.Sqrt(2*jumpHeight/gravity));
                    motion.SampleExternal(fallClip,Mathf.Repeat(descentTime/nativeDurations[fallClip],1),false);
                    for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(gaitP[i],bones[i].localPosition,fallBlend);bones[i].localRotation=Quaternion.Slerp(gaitQ[i],bones[i].localRotation,fallBlend);}
                }
                float prepare=Mathf.SmoothStep(0,1,Mathf.InverseLerp(s.Duration-0.12f,s.Duration,stageTime));
                if(prepare>0){for(int i=0;i<bones.Length;i++){gaitP[i]=bones[i].localPosition;gaitQ[i]=bones[i].localRotation;}motion.SampleExternal(landingClip,0,false);
                    for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(gaitP[i],bones[i].localPosition,prepare);bones[i].localRotation=Quaternion.Slerp(gaitQ[i],bones[i].localRotation,prepare);}}
            }
            else if(s.Flight==2)motion.SampleExternal(fallClip,Mathf.Repeat(stageTime/nativeDurations[fallClip],1),false);
            else if(s.Clip==10||s.Clip==19){
                if(UseNativeStops&&!stopBegan&&s.V1.sqrMagnitude<0.01f&&stageTime>s.Duration*0.45f&&speed<gait.WalkReferenceSpeed){
                    stopBegan=true;bool left=Vector3.Dot(leftFoot.position-rightFoot.position,root.forward)>=0;
                    // Apply() below advances this same frame, so include it in the budget.
                    ground.Begin(GroundTransitionKind.Stop,NativeAirSelection.SpeedTier(speed,gait.WalkReferenceSpeed,gait.RunReferenceSpeed),0,left,gait.WalkReferenceSpeed,s.Duration-stageTime+dt);
                }
                gait.Sample(distance,speed,stageTime>0.28f&&ground.Kind==GroundTransitionKind.None,directionalAmount);interaction.RefreshGrants();motion.transform.localPosition=Vector3.up*motion.BaseFeetOffset;ground.Apply(dt,speed);
            }
            else if(s.Clip==182){
                gait.Sample(distance,speed,false);for(int i=0;i<bones.Length;i++){gaitP[i]=bones[i].localPosition;gaitQ[i]=bones[i].localRotation;}
                motion.SampleExternal(landingClip,stageTime/landingDuration,true);float recover=Mathf.SmoothStep(0,1,Mathf.InverseLerp(0.03f,0.20f,stageTime));
                for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(bones[i].localPosition,gaitP[i],recover);bones[i].localRotation=Quaternion.Slerp(bones[i].localRotation,gaitQ[i],recover);}
                motion.transform.localPosition=Vector3.up*(motion.BaseFeetOffset+motion.VisualLift*(1-recover));
            }
            if(s.Name=="Turn in place")ground.Apply(dt,0);
            interaction.Apply(1);
            // Blend the entire evaluated pose, including outgoing IK, to avoid snapping when releasing a hold.
            float w=Mathf.SmoothStep(0,1,Mathf.Clamp01(stageTime/(s.Clip==182?0.18f:0.28f)));
            if(w<1)for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(fromP[i],bones[i].localPosition,w);bones[i].localRotation=Quaternion.Slerp(fromQ[i],bones[i].localRotation,w);}
            interaction.AlignVisualProp();
        }
        public void Stop()
        {Active=false;Finished=false;interaction.Stop();motion.ManualDrive=interaction.ManualDrive=false;RemoveGuides();}
        public void ShowGuides(bool visible)
        {
            if(guides!=null){guides.SetActive(visible);return;}if(!visible)return;
            var shader=Shader.Find("Sprites/Default")??Shader.Find("Unlit/Color");if(shader==null)return;
            guideMaterial=new Material(shader);guides=new GameObject("WKPmxAvatar_RouteGuides_VisualOnly");
            guides.transform.SetPositionAndRotation(anchor+Vector3.up*motion.BaseFeetOffset,facing);
            void line(string name,Vector3[] points,Color color){var go=new GameObject(name);go.transform.SetParent(guides.transform,false);var lr=go.AddComponent<LineRenderer>();lr.useWorldSpace=false;lr.sharedMaterial=guideMaterial;lr.startColor=lr.endColor=color;lr.startWidth=lr.endWidth=0.035f;lr.positionCount=points.Length;lr.SetPositions(points);}
            var path=new List<Vector3>();foreach(var s in stages)for(int i=0;i<=12;i++)path.Add(Position(s,i/12f)+Vector3.up*0.025f);
            line("Route",path.ToArray(),new Color(0.25f,0.9f,1));
            line("Support frame",new[]{new Vector3(11,0,7.4f),new Vector3(11,3,7.4f),new Vector3(13,3,7.4f),new Vector3(13,0,7.4f)},new Color(1,0.7f,0.2f));
            line("Start",new[]{new Vector3(-0.4f,0.03f,0),new Vector3(0.4f,0.03f,0)},Color.green);
        }
        void RemoveGuides(){if(guides!=null)UnityEngine.Object.Destroy(guides);if(guideMaterial!=null)UnityEngine.Object.Destroy(guideMaterial);guides=null;guideMaterial=null;}
    }
}

