using System;using System.IO;using UnityEngine;
namespace WKPmxAvatar
{
    // The recovered Wulfa backward-dodge poses, driven by observed travel rather than root motion.
    public sealed class NativeBackDodge
    {
        readonly MotionClip left,right;readonly Transform[] bones;readonly Vector3[] positions;readonly Quaternion[] rotations;readonly float scale;
        float weight,cycles;bool selected;public float Weight=>weight;public float Cycles=>cycles;
        public float MinimumSpeed=4.0f;
        public NativeBackDodge(string folder,Transform[] bones,float height){this.bones=bones;scale=height/2.1f;left=MotionClip.Load(Path.Combine(folder,"clip-72-pmx.wkpmotion"),bones);right=MotionClip.Load(Path.Combine(folder,"clip-73-pmx.wkpmotion"),bones);positions=new Vector3[bones.Length];rotations=new Quaternion[bones.Length];}
        public void Reset(){weight=cycles=0;selected=false;}
        public void Sample(float distance,float angle,float speed,float dt,bool allowed)
        {
            // No accumulated-distance or sustained-movement prerequisite: fast retreat is
            // eligible immediately. Slow/tiny travel retains the ordinary backwards gait.
            // Hysteresis avoids flicker near the speed boundary. Short taps only enter the
            // beginning of the distance envelope, and stopping never locks the whole clip.
            float threshold=float.IsFinite(MinimumSpeed)?Mathf.Clamp(MinimumSpeed,2,12):4.0f;
            selected=allowed&&Mathf.Abs(angle)>135&&speed>threshold*(selected?0.85f:1)*scale;
            bool active=selected;weight=Mathf.MoveTowards(weight,active?1:0,dt*(active?8:16));if(weight<=0){cycles=0;return;}
            if(active)cycles+=Mathf.Max(0,distance)/(2.7f*scale);float phase=Mathf.Repeat(cycles,1);
            for(int i=0;i<bones.Length;i++){positions[i]=bones[i].localPosition;rotations[i]=bones[i].localRotation;}
            // Drop the long idle tail. Crossfade through the native locomotion pose at both
            // ends, so continuous back travel never snaps from recovery to deep anticipation.
            float normalized=phase*0.68f;((Mathf.FloorToInt(cycles)&1)==0?left:right).Sample(bones,normalized,scale);
            float envelope=Mathf.SmoothStep(0,1,phase/0.10f)*Mathf.SmoothStep(0,1,(1-phase)/0.15f),blend=weight*envelope;
            for(int i=0;i<bones.Length;i++){bones[i].localPosition=Vector3.Lerp(positions[i],bones[i].localPosition,blend);bones[i].localRotation=Quaternion.Slerp(rotations[i],bones[i].localRotation,blend);}
        }
    }
}
