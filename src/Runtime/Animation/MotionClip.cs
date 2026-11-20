using System;using System.Collections.Generic;using System.IO;using System.Text;using UnityEngine;
namespace WKPmxAvatar {
// WKPMOT01 decoder and sampler. Rig names and limits are validated before use.
public sealed class MotionClip
        {
            static readonly BoundedWeakCache<MotionClip> shared=new BoundedWeakCache<MotionClip>(128);
            string[] names;
            public static int SharedLoads=>shared.Loads;public static int SharedHits=>shared.Hits;public static int SharedKeys=>shared.Keys;
            public int Frames, BoneCount; public float Rate;
            public Vector3[] Positions; public Quaternion[] Rotations;
            public float Duration => (Frames-1)/Rate;
            public static MotionClip Load(string path,Transform[] bones)
            {
                string full=Path.GetFullPath(path);var info=new FileInfo(full);
                string key=full+"\n"+info.Length+"\n"+info.LastWriteTimeUtc.Ticks;
                var clip=shared.Get(key,()=>Decode(full,bones));
                if(clip.BoneCount!=bones.Length)throw new InvalidDataException("Cached motion rig mismatch");
                for(int i=0;i<bones.Length;i++)if(clip.names[i]!=bones[i].name)throw new InvalidDataException("Cached motion requires matching bone names");
                return clip;
            }
            static MotionClip Decode(string path,Transform[] bones)
            {
                var info=new FileInfo(path);if(info.Length<20||info.Length>32*1024*1024)throw new InvalidDataException("Motion size outside limits");
                using(var r=new BinaryReader(File.OpenRead(path),Encoding.UTF8))
                {
                    if(Encoding.ASCII.GetString(r.ReadBytes(8))!="WKPMOT01")throw new InvalidDataException("Motion header mismatch");
                    var clip=new MotionClip {BoneCount=r.ReadInt32(),Frames=r.ReadInt32(),Rate=r.ReadSingle()};
                    if(clip.BoneCount!=bones.Length||clip.BoneCount<1||clip.BoneCount>2048||clip.Frames<2||clip.Frames>36000||float.IsNaN(clip.Rate)||float.IsInfinity(clip.Rate)||clip.Rate<=0||clip.Rate>240)throw new InvalidDataException("Motion rig/sample mismatch");
                    clip.names=new string[bones.Length];for(int b=0;b<bones.Length;b++){clip.names[b]=r.ReadString();if(clip.names[b]!=bones[b].name)throw new InvalidDataException("Motion requires the matching Rossi PMX rig");}
                    long count=(long)clip.BoneCount*clip.Frames;
                    if(count>1000000||r.BaseStream.Position+count*28!=r.BaseStream.Length)throw new InvalidDataException("Motion data length mismatch");
                    clip.Positions=new Vector3[(int)count];clip.Rotations=new Quaternion[(int)count];
                    for(int i=0;i<count;i++)
                    {
                        var p=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());var q=new Quaternion(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
                        float n=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;
                        if(!Finite(p.x)||!Finite(p.y)||!Finite(p.z)||p.sqrMagnitude>100||!Finite(n)||Mathf.Abs(n-1)>0.001f)throw new InvalidDataException("Invalid motion sample");
                        clip.Positions[i]=p;clip.Rotations[i]=q.normalized;
                    }
                    return clip;
                }
            }
            static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
            public void Sample(Transform[] bones,float normalizedTime,float heightScale=1)
            {
                if(bones.Length!=BoneCount)throw new InvalidOperationException("Motion target changed");
                float sample=Mathf.Clamp01(normalizedTime)*(Frames-1);int a=Mathf.FloorToInt(sample),b=Mathf.Min(a+1,Frames-1);float blend=sample-a;
                for(int i=0;i<BoneCount;i++){int first=a*BoneCount+i,last=b*BoneCount+i;bones[i].localPosition=Vector3.LerpUnclamped(Positions[first],Positions[last],blend)*heightScale;bones[i].localRotation=Quaternion.SlerpUnclamped(Rotations[first],Rotations[last],blend);}
            }
        }
}
