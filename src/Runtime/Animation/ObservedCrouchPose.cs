using System;using UnityEngine;
namespace WKPmxAvatar
{
    // Visual-only articulated crouch. Feet and limb lengths stay fixed, unlike root Y scaling.
    public sealed class ObservedCrouchPose
    {
        readonly Transform hips,spine;readonly Transform[] legs,knees,feet;readonly float scale;float weight;
        public float Weight=>weight;
        public ObservedCrouchPose(Transform[] bones,float height)
        {
            Transform find(string n)=>Array.Find(bones,b=>b.name==n)??throw new InvalidOperationException("Crouch bone missing: "+n);
            hips=find("腰");spine=find("上半身");legs=new[]{find("左足"),find("右足")};knees=new[]{find("左ひざ"),find("右ひざ")};feet=new[]{find("左足首"),find("右足首")};scale=height/2.1f;
        }
        public void Apply(bool crouched,float dt,Vector3 forward,float size=1)
        {
            weight=Mathf.MoveTowards(weight,crouched?1:0,Mathf.Max(0,dt)*12);if(weight<=0)return;
            Vector3 l=feet[0].position,r=feet[1].position;Quaternion lq=feet[0].rotation,rq=feet[1].rotation;
            float s=scale*Mathf.Max(0.05f,size);hips.position+=(-Vector3.up*0.43f-forward*0.10f)*s*weight;
            spine.rotation=Quaternion.AngleAxis(17*weight,Vector3.Cross(Vector3.up,forward))*spine.rotation;
            ContactSolver.Solve(legs[0],knees[0],feet[0],l,forward);ContactSolver.Solve(legs[1],knees[1],feet[1],r,forward);
            feet[0].rotation=lq;feet[1].rotation=rq;
        }
        public static Vector3 UniformCompensation(Vector3 parentScale)
        {float uniform=Mathf.Sqrt(Mathf.Abs(parentScale.x*parentScale.z));return new Vector3(uniform/Mathf.Max(0.001f,Mathf.Abs(parentScale.x)),uniform/Mathf.Max(0.001f,Mathf.Abs(parentScale.y)),uniform/Mathf.Max(0.001f,Mathf.Abs(parentScale.z)));}
    }
}
