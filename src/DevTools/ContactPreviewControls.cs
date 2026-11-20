using System;using UnityEngine;
namespace WKPmxAvatar {
public sealed partial class ContactSolver {
int mode;bool freeHang;Transform prop;Vector3 gripLocal,shaftLocal,strikeLocal;float blend;
public Vector3 LeftContact {get;private set;}public Vector3 RightContact {get;private set;}
public float GripError {get;private set;}public string Description {get;private set;}="Interactions stopped";
public bool Active=>mode!=0;public bool ManualDrive;
public void HoldHammer(Transform visual,Vector3 meshGrip,Vector3 meshShaft,Vector3 meshStrike)
        {if(bones==null)throw new InvalidOperationException("Interaction rig not initialized");if(visual==null)throw new ArgumentNullException(nameof(visual));prop=visual;gripLocal=meshGrip;shaftLocal=meshShaft.normalized;strikeLocal=Vector3.ProjectOnPlane(meshStrike,shaftLocal).normalized;if(shaftLocal.sqrMagnitude<0.5f||strikeLocal.sqrMagnitude<0.5f)throw new InvalidOperationException("Invalid hammer axes");
            // Keep the palm outside the handle. Measure the shaft below the grip, excluding the head.
            float radius=0;foreach(var mf in visual.GetComponentsInChildren<MeshFilter>(true))if(mf.sharedMesh!=null){if(mf.sharedMesh.isReadable){foreach(var vertex in mf.sharedMesh.vertices){Vector3 p=visual.InverseTransformPoint(mf.transform.TransformPoint(vertex))-meshGrip;if(Vector3.Dot(p,shaftLocal)<0)radius=Mathf.Max(radius,visual.TransformVector(Vector3.ProjectOnPlane(p,shaftLocal)).magnitude);}}else radius=Mathf.Max(radius,mf.transform.TransformVector(Vector3.up*mf.sharedMesh.bounds.extents.y).magnitude);}
            float localGap=(Mathf.Clamp(radius,0.015f,0.09f)+0.006f)/right.Wrist.TransformVector(right.Normal).magnitude;
            right.Grip=right.Middle*0.85f+right.Normal*localGap;
            mode=1;blend=0;enabled=true;Description="Hammer: handle grip and closed fingers";}
public void Climb(bool suspended){if(bones==null)throw new InvalidOperationException("Interaction rig not initialized");right.Grip=right.RestGrip;mode=2;freeHang=suspended;blend=0;enabled=true;Description=suspended?"Climb: free hang":"Climb: wall support";}
public void Stop(){mode=0;blend=0;prop=null;enabled=false;Description="Interactions stopped";}
public void Apply(float dt)
        {
            if(mode==0)return;blend=Mathf.MoveTowards(blend,1,Mathf.Max(0,dt)*5);
            Vector3 forward=transform.parent!=null?transform.parent.forward:Vector3.forward,side=Vector3.Cross(Vector3.up,forward).normalized;
            if(mode==1)
            {
                Vector3 shaft=(Vector3.up+forward*0.85f).normalized;
                var wrist=WristRotation(right,(forward-Vector3.up*0.85f).normalized,-shaft);Vector3 target=right.Upper.position-Vector3.up*0.27f+forward*0.34f+side*0.06f;
                var from=right.Wrist.position;Vector3 contact=Vector3.Lerp(from+right.Wrist.rotation*right.Grip,target+wrist*right.Grip,blend);
                Arm(right,contact,Quaternion.Slerp(right.Wrist.rotation,wrist,blend),forward,side);var solvedWrist=right.Wrist.rotation;ApplyGrants();right.Wrist.rotation=LimitWrist(right,solvedWrist);Curl(right,0.9f*blend);
                RightContact=right.Wrist.TransformPoint(right.Grip);
                var frame=Quaternion.LookRotation(shaftLocal,strikeLocal);prop.rotation=Quaternion.LookRotation(shaft,Vector3.ProjectOnPlane(forward,shaft))*Quaternion.Inverse(frame);prop.position=RightContact-prop.TransformVector(gripLocal);
                GripError=Vector3.Distance(prop.TransformPoint(gripLocal),RightContact);
            }
            else
            {
                hips.position+=forward*(freeHang?-0.01f:0.05f)*blend;spine.rotation=Quaternion.AngleAxis((freeHang?0:12)*blend,side)*spine.rotation;
                LeftContact=left.Upper.position+Vector3.up*(freeHang?0.38f:0.26f)+forward*(freeHang?0.17f:0.36f)-side*0.08f;
                RightContact=right.Upper.position+Vector3.up*(freeHang?0.38f:0.26f)+forward*(freeHang?0.17f:0.36f)+side*0.08f;
                var lq=WristRotation(left,Vector3.up,-Vector3.Cross(Vector3.up,forward));var rq=WristRotation(right,Vector3.up,Vector3.Cross(Vector3.up,forward));
                if(freeHang){
                    float leftReach=Vector3.Distance(left.Upper.position,left.Lower.position)+Vector3.Distance(left.Lower.position,left.Wrist.position);
                    float rightReach=Vector3.Distance(right.Upper.position,right.Lower.position)+Vector3.Distance(right.Lower.position,right.Wrist.position);
                    LeftContact=left.Upper.position+Vector3.up*(leftReach*0.985f)+forward*0.03f-side*0.025f+lq*left.Grip;
                    RightContact=right.Upper.position+Vector3.up*(rightReach*0.985f)+forward*0.03f+side*0.025f+rq*right.Grip;
                }
                Arm(left,Vector3.Lerp(left.Wrist.TransformPoint(left.Grip),LeftContact,blend),Quaternion.Slerp(left.Wrist.rotation,lq,blend),forward,side);
                Arm(right,Vector3.Lerp(right.Wrist.TransformPoint(right.Grip),RightContact,blend),Quaternion.Slerp(right.Wrist.rotation,rq,blend),forward,side);
                Vector3 leftTarget=hips.position-Vector3.up*(freeHang?0.48f:0.63f)+forward*(freeHang?0.08f:0.30f)-side*0.11f;
                Vector3 rightTarget=hips.position-Vector3.up*(freeHang?0.60f:0.73f)+forward*(freeHang?0.04f:0.30f)+side*0.11f;
                if(freeHang){
                    float leftLength=Vector3.Distance(leftLeg.position,leftKnee.position)+Vector3.Distance(leftKnee.position,leftAnkle.position);
                    float rightLength=Vector3.Distance(rightLeg.position,rightKnee.position)+Vector3.Distance(rightKnee.position,rightAnkle.position);
                    leftTarget=leftLeg.position-Vector3.up*(leftLength*0.985f)+forward*0.01f-side*0.005f;
                    rightTarget=rightLeg.position-Vector3.up*(rightLength*0.977f)+forward*0.015f+side*0.005f;
                }
                Solve(leftLeg,leftKnee,leftAnkle,Vector3.Lerp(leftAnkle.position,leftTarget,blend),forward);Solve(rightLeg,rightKnee,rightAnkle,Vector3.Lerp(rightAnkle.position,rightTarget,blend),forward);
                if(freeHang){leftAnkle.rotation=Quaternion.AngleAxis(15*blend,side)*leftAnkle.rotation;rightAnkle.rotation=Quaternion.AngleAxis(15*blend,side)*rightAnkle.rotation;}
                var solvedLeft=left.Wrist.rotation;var solvedRight=right.Wrist.rotation;ApplyGrants();left.Wrist.rotation=LimitWrist(left,solvedLeft);right.Wrist.rotation=LimitWrist(right,solvedRight);Curl(left,0.38f*blend);Curl(right,0.38f*blend);
                GripError=Mathf.Max(Vector3.Distance(left.Wrist.TransformPoint(left.Grip),LeftContact),Vector3.Distance(right.Wrist.TransformPoint(right.Grip),RightContact));
            }
        }
void LateUpdate(){if(!ManualDrive)Apply(Time.deltaTime);}
public void AlignVisualProp(){if(mode==1&&prop!=null){RightContact=right.Wrist.TransformPoint(right.Grip);prop.position=RightContact-prop.TransformVector(gripLocal);}}
public static void HammerLandmarks(MeshFilter filter,Transform root,out Vector3 grip,out Vector3 shaft,out Vector3 strike)
        {
            var mesh=filter.sharedMesh;if(mesh==null)throw new InvalidOperationException("Hammer mesh missing");var b=mesh.bounds;
            if(b.size.z<Mathf.Max(b.size.x,b.size.y))throw new InvalidOperationException("Hammer shaft axis not recognized");
            float loX=float.MaxValue,hiX=float.MinValue,loY=float.MaxValue,hiY=float.MinValue;int n=0;
            if(mesh.isReadable){foreach(var p in mesh.vertices)if(p.z<b.min.z+b.size.z*0.3f){loX=Mathf.Min(loX,p.x);hiX=Mathf.Max(hiX,p.x);loY=Mathf.Min(loY,p.y);hiY=Mathf.Max(hiY,p.y);n++;}}
            else {loX=hiX=Mathf.Clamp(0,b.min.x,b.max.x);loY=hiY=b.center.y;n=4;}
            if(n<4)throw new InvalidOperationException("Hammer handle not recognized");Vector3 local=new Vector3((loX+hiX)*0.5f,(loY+hiY)*0.5f,b.min.z+b.size.z*0.3f);
            grip=root.InverseTransformPoint(filter.transform.TransformPoint(local));shaft=root.InverseTransformDirection(filter.transform.TransformDirection(Vector3.forward));strike=root.InverseTransformDirection(filter.transform.TransformDirection(Vector3.left));
        }
}
}
