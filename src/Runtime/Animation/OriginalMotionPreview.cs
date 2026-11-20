using System;using System.Collections.Generic;using System.IO;using UnityEngine;
namespace WKPmxAvatar {
 // Runtime owns clip sampling and floor correction; preview playback is a separate partial.
 [DefaultExecutionOrder(10500)] public sealed partial class MotionPlayer:MonoBehaviour {
  Transform[] bones;float heightScale,groundFootY;Transform leftFoot,rightFoot;
  readonly Dictionary<int,MotionClip> cache=new Dictionary<int,MotionClip>();
  public float BaseFeetOffset;public float VisualLift {get;private set;}
  public float PreloadExternal(string folder,int index,Transform[] target){if(!cache.TryGetValue(index,out var c)){c=MotionClip.Load(Path.Combine(folder,"clip-"+index+"-pmx.wkpmotion"),target);cache.Add(index,c);}return c.Duration;}
  // Matches old Play(idle, dt=0): measure the idle floor, then restore the incoming pose.
  public void Initialize(string folder,Transform[] target,float height){
   PreloadExternal(folder,7,target);bones=target;heightScale=height/2.1f;
   var beforeP=new Vector3[target.Length];var beforeQ=new Quaternion[target.Length];
   for(int i=0;i<target.Length;i++){beforeP[i]=target[i].localPosition;beforeQ[i]=target[i].localRotation;}
   leftFoot=Array.Find(bones,b=>b.name=="左足首");rightFoot=Array.Find(bones,b=>b.name=="右足首");
   cache[7].Sample(bones,0,heightScale);groundFootY=FeetY();
   for(int i=0;i<bones.Length;i++){bones[i].localPosition=beforeP[i];bones[i].localRotation=beforeQ[i];}
   VisualLift=0;transform.localPosition=Vector3.up*BaseFeetOffset;enabled=true;
  }
  float FeetY()=>leftFoot!=null&&rightFoot!=null?Mathf.Min(transform.InverseTransformPoint(leftFoot.position).y,transform.InverseTransformPoint(rightFoot.position).y):0;
  public void SampleExternal(int index,float normalized,bool onGround){
   if(bones==null||!cache.TryGetValue(index,out var external))throw new InvalidOperationException("External motion not loaded");
   external.Sample(bones,normalized,heightScale);VisualLift=onGround?Mathf.Clamp(groundFootY-FeetY(),0,0.4f*heightScale):0;
   transform.localPosition=Vector3.up*(BaseFeetOffset+VisualLift);
  }
  public void Stop(){
#if WKPMX_DEV
   clip=null;Phase="Stopped";Description="Original motion stopped";
#endif
   enabled=false;VisualLift=0;transform.localPosition=Vector3.up*BaseFeetOffset;
  }
 }
}
