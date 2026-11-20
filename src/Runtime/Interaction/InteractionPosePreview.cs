using System;
using System.Collections.Generic;
using UnityEngine;

namespace WKPmxAvatar
{
    // Local visual constraints layered over the original idle clip. Does not access networking or physics.
    [DefaultExecutionOrder(10600)]
    public sealed partial class ContactSolver : MonoBehaviour
    {
        sealed class Hand
        {
            internal Transform Upper,Lower,Wrist;internal Transform[][] Fingers;
            internal Quaternion Frame;internal Vector3 Grip,RestGrip,Middle,Normal;internal bool Left;
        }
        Transform[] bones;Hand left,right;Transform leftLeg,leftKnee,leftAnkle,rightLeg,rightKnee,rightAnkle,hips,spine;
        Quaternion[] rest;Vector3[] restP;int[] grants,flags;float[] weights;





        public Vector3 LeftPalm=>left.Wrist.TransformPoint(left.Grip);public Vector3 RightPalm=>right.Wrist.TransformPoint(right.Grip);
        public void ApplyItemGrip(bool isLeft,ItemGripProfile profile)
        {
            if(profile==null)return;var h=isLeft?left:right;Vector3 normal=h.Wrist.TransformDirection(h.Normal),axis=h.Wrist.TransformDirection(Vector3.Cross(h.Frame*Vector3.forward,h.Normal)).normalized;
            for(int f=0;f<4;f++)for(int j=0;j<3;j++)h.Fingers[f][j].rotation=Quaternion.AngleAxis((j==0?58:j==1?76:45)*profile.curls[f],axis)*h.Fingers[f][j].rotation;
            var thumb=h.Fingers[4];Vector3 target=profile.shape=="pinch"?Tip(h.Fingers[0]):h.Wrist.TransformPoint(h.Grip);thumb[0].rotation=Quaternion.Slerp(Quaternion.identity,Quaternion.FromToRotation(thumb[1].position-thumb[0].position,target-thumb[0].position),profile.curls[4]*0.85f)*thumb[0].rotation;
            for(int j=1;j<3;j++){Vector3 a=Vector3.Cross(thumb[j].position-thumb[j-1].position,normal).normalized;thumb[j].rotation=Quaternion.AngleAxis(32*profile.curls[4],a)*thumb[j].rotation;}
        }
        static Vector3 Tip(Transform[] fingers)=>fingers[2].position+(fingers[2].position-fingers[1].position)*0.65f;
        public Vector3 ItemContact(bool isLeft,ItemGripProfile profile){var h=isLeft?left:right;if(profile?.shape=="pinch")return (Tip(h.Fingers[0])+Tip(h.Fingers[4]))*0.5f;return h.Wrist.TransformPoint(h.Grip);}
        public Quaternion ItemFrame(bool isLeft){var h=isLeft?left:right;return Quaternion.LookRotation(h.Wrist.TransformDirection(h.Normal),h.Wrist.TransformDirection(h.Frame*Vector3.forward));}
        public void ApplyGrip(int leftState,int rightState,bool leftHeld,bool rightHeld)
        {if(leftState!=4&&(leftState==2||leftState==3||leftHeld))Curl(left,0.85f);if(rightState!=4&&(rightState==2||rightState==3||rightHeld))Curl(right,0.85f);}
        public void Initialize(Transform[] target,int[] sources,int[] grantFlags,float[] grantWeights)
        {
            bones=target;grants=sources;flags=grantFlags;weights=grantWeights;rest=new Quaternion[bones.Length];restP=new Vector3[bones.Length];
            var map=new Dictionary<string,Transform>();for(int i=0;i<bones.Length;i++){map[bones[i].name]=bones[i];rest[i]=bones[i].localRotation;restP[i]=bones[i].localPosition;}
            Transform find(string name){if(!map.TryGetValue(name,out var t))throw new InvalidOperationException("Interaction bone missing: "+name);return t;}
            Hand hand(bool isLeft)
            {
                string p=isLeft?"左":"右";var h=new Hand{Upper=find(p+"腕"),Lower=find(p+"ひじ"),Wrist=find(p+"手首"),Left=isLeft};
                h.Fingers=new[]{new[]{find(p+"人指１"),find(p+"人指２"),find(p+"人指３")},new[]{find(p+"中指１"),find(p+"中指２"),find(p+"中指３")},new[]{find(p+"薬指１"),find(p+"薬指２"),find(p+"薬指３")},new[]{find(p+"小指１"),find(p+"小指２"),find(p+"小指３")},new[]{find(p+"親指０"),find(p+"親指１"),find(p+"親指２")}};
                Vector3 forward=h.Wrist.InverseTransformDirection(h.Fingers[1][0].position-h.Wrist.position).normalized;
                Vector3 across=h.Wrist.InverseTransformDirection(h.Fingers[3][0].position-h.Fingers[0][0].position).normalized;
                across=Vector3.ProjectOnPlane(across,forward).normalized;h.Frame=Quaternion.LookRotation(forward,across);
                h.Normal=Vector3.Cross(forward,across).normalized*(isLeft?1:-1);
                Vector3 middle=h.Wrist.InverseTransformPoint(h.Fingers[1][0].position);h.Grip=middle*0.78f+h.Normal*(middle.magnitude*0.28f);
                h.Middle=middle;h.RestGrip=h.Grip;return h;
            }
            left=hand(true);right=hand(false);hips=find("腰");spine=find("上半身");leftLeg=find("左足");leftKnee=find("左ひざ");leftAnkle=find("左足首");rightLeg=find("右足");rightKnee=find("右ひざ");rightAnkle=find("右足首");
            enabled=false;
        }
        
        
        
        public static void Solve(Transform a,Transform b,Transform c,Vector3 target,Vector3 pole)
        {
            float ab=Vector3.Distance(a.position,b.position),bc=Vector3.Distance(b.position,c.position);Vector3 to=target-a.position;if(ab<1e-4f||bc<1e-4f||to.sqrMagnitude<1e-8f)return;
            float d=Mathf.Clamp(to.magnitude,Mathf.Abs(ab-bc)+0.001f,ab+bc-0.001f);Vector3 axis=to.normalized,bend=Vector3.ProjectOnPlane(pole,axis).normalized;
            if(bend.sqrMagnitude<0.01f)bend=Vector3.ProjectOnPlane(Vector3.right,axis).normalized;if(bend.sqrMagnitude<0.01f)bend=Vector3.ProjectOnPlane(Vector3.forward,axis).normalized;
            float along=(ab*ab-bc*bc+d*d)/(2*d);Vector3 elbow=a.position+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,ab*ab-along*along));Quaternion q=c.rotation;
            a.rotation=Quaternion.FromToRotation(b.position-a.position,elbow-a.position)*a.rotation;b.rotation=Quaternion.FromToRotation(c.position-b.position,a.position+axis*d-b.position)*b.rotation;c.rotation=q;
        }
        void Curl(Hand h,float amount)
        {
            Vector3 normal=h.Wrist.TransformDirection(h.Normal);
            Vector3 curlAxis=h.Wrist.TransformDirection(Vector3.Cross(h.Frame*Vector3.forward,h.Normal)).normalized;
            for(int f=0;f<4;f++)for(int j=0;j<3;j++)
            {
                var t=h.Fingers[f][j];Vector3 direction=j<2?h.Fingers[f][j+1].position-t.position:t.position-h.Fingers[f][j-1].position;
                float angle=(j==0?58:j==1?76:45)*amount;
                t.rotation=Quaternion.AngleAxis(angle,curlAxis)*t.rotation;
            }
            var thumb=h.Fingers[4];Vector3 desired=h.Wrist.TransformPoint(h.Grip)-thumb[0].position;
            thumb[0].rotation=Quaternion.Slerp(Quaternion.identity,Quaternion.FromToRotation(thumb[1].position-thumb[0].position,desired),amount*0.72f)*thumb[0].rotation;
            for(int j=1;j<3;j++){Vector3 d=thumb[j].position-thumb[j-1].position,axis=Vector3.Cross(d,normal).normalized;thumb[j].rotation=Quaternion.AngleAxis(32*amount,axis)*thumb[j].rotation;}
        }
        Quaternion WristRotation(Hand h,Vector3 forward,Vector3 across)=>Quaternion.LookRotation(forward,across)*Quaternion.Inverse(h.Frame);
        Quaternion LimitWrist(Hand h,Quaternion desired)
        {
            Vector3 forearm=(h.Wrist.position-h.Lower.position).normalized;
            Vector3 fingers=desired*(h.Frame*Vector3.forward),across=desired*(h.Frame*Vector3.up);
            Vector3 limited=Vector3.RotateTowards(forearm,fingers,55*Mathf.Deg2Rad,0);
            // Transport the complete frame: projecting its across axis can flip at a parallel direction.
            return Quaternion.FromToRotation(fingers,limited)*desired;
        }
        void Arm(Hand h,Vector3 contact,Quaternion wrist,Vector3 forward,Vector3 side)
        {
            for(int pass=0;pass<4;pass++){Vector3 target=contact-wrist*h.Grip;Solve(h.Upper,h.Lower,h.Wrist,target,-forward+side*(h.Left?-0.55f:0.55f));wrist=LimitWrist(h,wrist);h.Wrist.rotation=wrist;}
        }
        void ApplyGrants()
        {
            if(grants==null)return;for(int i=0;i<bones.Length;i++){int s=grants[i];if(s<0||s>=bones.Length)continue;if((flags[i]&0x100)!=0)bones[i].localRotation=rest[i]*Quaternion.SlerpUnclamped(Quaternion.identity,Quaternion.Inverse(rest[s])*bones[s].localRotation,weights[i]);if((flags[i]&0x200)!=0)bones[i].localPosition=restP[i]+(bones[s].localPosition-restP[s])*weights[i];}
        }
        
        
        
        public void RefreshGrants()=>ApplyGrants();
        float contactBlend,supportLean,leftFreeBlend,rightFreeBlend;
        public void ApplyContacts(float dt,Transform leftTarget,Transform rightTarget,bool leftActive,bool rightActive,float risingSpeed,Vector3 forward)
        {
            bool active=leftActive||rightActive;contactBlend=Mathf.MoveTowards(contactBlend,active?1:0,Mathf.Max(0,dt)*7);
            supportLean=Mathf.MoveTowards(supportLean,leftActive==rightActive?0:leftActive?-1:1,Mathf.Max(0,dt)*6);
            leftFreeBlend=Mathf.MoveTowards(leftFreeBlend,active&&!leftActive?1:0,Mathf.Max(0,dt)*7);
            rightFreeBlend=Mathf.MoveTowards(rightFreeBlend,active&&!rightActive?1:0,Mathf.Max(0,dt)*7);
            if(!active)return;Vector3 side=Vector3.Cross(Vector3.up,forward).normalized;
            // Lift the torso slightly while actually pulling, rather than inventing an idle
            // climbing cycle. World-space hand contacts still come from the game/network.
            hips.position+=Vector3.up*Mathf.Clamp(risingSpeed*0.045f,0,0.09f)*contactBlend;
            spine.rotation=Quaternion.AngleAxis(Mathf.Clamp(risingSpeed*5,-3,10)*contactBlend,side)*spine.rotation;
            hips.position+=side*(supportLean*0.055f);
            spine.rotation=Quaternion.AngleAxis(-supportLean*9,forward)*Quaternion.AngleAxis(supportLean*16,Vector3.up)*spine.rotation;
            void freeHand(Hand h,float weight){if(weight<=0)return;Vector3 goal=h.Upper.position-Vector3.up*0.16f+forward*0.20f+side*(h.Left?-0.23f:0.23f);var wrist=WristRotation(h,(forward+Vector3.up*0.30f).normalized,side*(h.Left?-1:1));Arm(h,Vector3.Lerp(h.Wrist.TransformPoint(h.Grip),goal,weight),Quaternion.Slerp(h.Wrist.rotation,wrist,weight),forward,side);}
            freeHand(left,leftFreeBlend);freeHand(right,rightFreeBlend);
            if(leftActive&&leftTarget!=null){var wrist=WristRotation(left,Vector3.up,-side);Arm(left,Vector3.Lerp(left.Wrist.TransformPoint(left.Grip),leftTarget.position,contactBlend),Quaternion.Slerp(left.Wrist.rotation,wrist,contactBlend),forward,side);}
            if(rightActive&&rightTarget!=null){var wrist=WristRotation(right,Vector3.up,side);Arm(right,Vector3.Lerp(right.Wrist.TransformPoint(right.Grip),rightTarget.position,contactBlend),Quaternion.Slerp(right.Wrist.rotation,wrist,contactBlend),forward,side);}
            if(Mathf.Abs(supportLean)>0.01f){
                float weight=Mathf.Abs(supportLean);bool leftSupport=supportLean<0;
                Vector3 leftGoal=leftLeg.position-Vector3.up*(leftSupport?0.76f:0.53f)+forward*(leftSupport?0.10f:0.25f)-side*0.08f;
                Vector3 rightGoal=rightLeg.position-Vector3.up*(leftSupport?0.53f:0.76f)+forward*(leftSupport?0.25f:0.10f)+side*0.08f;
                Solve(leftLeg,leftKnee,leftAnkle,Vector3.Lerp(leftAnkle.position,leftGoal,weight),forward);Solve(rightLeg,rightKnee,rightAnkle,Vector3.Lerp(rightAnkle.position,rightGoal,weight),forward);
            }

            var lq=left.Wrist.rotation;var rq=right.Wrist.rotation;ApplyGrants();
            if(leftActive){left.Wrist.rotation=LimitWrist(left,lq);Curl(left,0.55f*contactBlend);}
            if(rightActive){right.Wrist.rotation=LimitWrist(right,rq);Curl(right,0.55f*contactBlend);}
            if(!leftActive){left.Wrist.rotation=LimitWrist(left,lq);Curl(left,0.12f*leftFreeBlend);}
            if(!rightActive){right.Wrist.rotation=LimitWrist(right,rq);Curl(right,0.12f*rightFreeBlend);}
        }
        
    }
}


