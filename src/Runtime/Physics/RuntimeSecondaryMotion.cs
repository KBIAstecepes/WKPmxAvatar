using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using WKPmxAvatar.RuntimePhysics;
using NVector=System.Numerics.Vector3;
using NQuaternion=System.Numerics.Quaternion;

namespace WKPmxAvatar
{
    // One solver per animated avatar; remote players and F6 use AvatarMotion.Animate.
    // No independent LateUpdate: mirror poses must be solved before BakeMesh and before
    // the mirror temporarily moves its render copy 10,000 units away.
    internal sealed class RuntimeSecondaryMotion
    {
        static Profile shared;
        readonly Transform root;
        readonly Transform[] bones;
        readonly NVector[] positions;
        readonly NQuaternion[] rotations;
        readonly Quaternion[] saved;
        readonly Action<string> warn;
        readonly float pmxScale;
        SecondaryRig solver;
        readonly SkirtPose skirt;
        readonly int[] legs;
        float worldScale;
        bool reset=true,failed;
        bool clearanceFailed;
        int lastFrame=-1;
        static NVector Vec(Vector3 v)=>new NVector(v.x,v.y,v.z);
        static NQuaternion Quat(Quaternion q)=>new NQuaternion(q.x,q.y,q.z,q.w);
        static Quaternion Unity(NQuaternion q)=>new Quaternion(q.X,q.Y,q.Z,q.W);
        static Profile Load()
        {
            if(shared!=null)return shared;
            using(var stream=typeof(RuntimeSecondaryMotion).Assembly.GetManifestResourceStream("WKPmxAvatar.PhysicsProfile.json"))
            using(var reader=new StreamReader(stream??throw new InvalidDataException("Missing physics profile")))
                return shared=JsonConvert.DeserializeObject<Profile>(reader.ReadToEnd());
        }
        internal static RuntimeSecondaryMotion TryCreate(Transform root,Transform[] bones,Action<string> warn)
        {
            try
            {
                var p=Load();if(bones.Length!=p.Names.Length)return null;
                for(int i=0;i<bones.Length;i++)if(bones[i]==null||bones[i].name!=p.Names[i]||bones[i].parent!=(p.Parents[i]<0?root:bones[p.Parents[i]]))return null;
                float reference=NVector.Distance(SecondaryRig.Vec(p.Rest[18]),SecondaryRig.Vec(p.Rest[14]));
                float scale=Vector3.Distance(root.InverseTransformPoint(bones[18].position),root.InverseTransformPoint(bones[14].position))/reference;
                if(!float.IsFinite(scale)||scale<=0)return null;
                // Names alone are not enough: decline a differently proportioned rig.
                for(int i=0;i<bones.Length;i++)if(p.Parents[i]>=0)
                {
                    var expected=(SecondaryRig.Vec(p.Rest[i])-SecondaryRig.Vec(p.Rest[p.Parents[i]]))*scale;
                    if(NVector.Distance(Vec(bones[i].localPosition),expected)>.002f*scale/.10575f)return null;
                }
                return new RuntimeSecondaryMotion(root,bones,scale,warn);
            }
            catch(Exception e){warn("Secondary motion unavailable; animation retained: "+e.Message);return null;}
        }
        RuntimeSecondaryMotion(Transform root,Transform[] bones,float scale,Action<string> warn)
        {
            this.root=root;this.bones=bones;pmxScale=scale;this.warn=warn;
            positions=new NVector[bones.Length];rotations=new NQuaternion[bones.Length];saved=new Quaternion[bones.Length];
            for(int i=0;i<rotations.Length;i++)rotations[i]=NQuaternion.Identity;
            skirt=new SkirtPose(Load(),scale);
            legs=new[]{Array.IndexOf(Load().Names,"左足"),Array.IndexOf(Load().Names,"左ひざ"),Array.IndexOf(Load().Names,"左足首"),Array.IndexOf(Load().Names,"右足"),Array.IndexOf(Load().Names,"右ひざ"),Array.IndexOf(Load().Names,"右足首"),Array.IndexOf(Load().Names,"左つま先"),Array.IndexOf(Load().Names,"右つま先")};
        }
        void ApplyClearance()
        {
            var anchor=bones[14];
            NVector Leg(int i)=>Vec(anchor.InverseTransformPoint(bones[legs[i]].position));
            // Foot proxy is centred on the boot mesh, including its heel and sole,
            // rather than on the ankle-to-toe skeleton line above the sole.
            NVector Boot(int leg,float x,float z)=>Vec(anchor.InverseTransformPoint(bones[legs[leg]].TransformPoint(new Vector3(x*pmxScale,-.55f*pmxScale,z*pmxScale))));
            skirt.Evaluate(Leg(0),Leg(1),Leg(2),Leg(3),Leg(4),Leg(5),Boot(2,.1f,.25f),Boot(2,.1f,-.95f),Boot(5,-.1f,.25f),Boot(5,-.1f,-.95f));
            for(int i=0;i<skirt.Bones.Length;i++)
            {
                var p=skirt.LocalPositions[i];
                bones[skirt.Bones[i]].localPosition=new Vector3(p.X,p.Y,p.Z);
                bones[skirt.Bones[i]].localRotation=Unity(skirt.Rotations[i]);
            }
        }
        internal void RequestReset(){reset=true;}
        internal void Tick(float dt,bool enabled,bool clearance=true)
        {
            if(dt<=0){reset=true;solver?.Invalidate();return;}
            if(clearance&&!clearanceFailed)
            {
                try{ApplyClearance();}
                catch(Exception e){clearanceFailed=true;warn("Skirt clearance disabled for this avatar; animation retained: "+e.Message);}
            }
            if(failed)return;
            if(!enabled){reset=true;solver?.Invalidate();return;}
            // F10 freezes the mirror before Animate is called, so dt alone cannot detect
            // the missing frames. Resume from animation instead of using stale particle history.
            int frame=Time.frameCount;
            if(lastFrame>=0&&frame-lastFrame>1)reset=true;
            lastFrame=frame;
            bool captured=false;
            try
            {
                var s=root.lossyScale;
                if(s.x<=0||Mathf.Abs(s.x-s.y)>.001f||Mathf.Abs(s.x-s.z)>.001f)throw new InvalidOperationException("Secondary motion requires a uniform positive avatar scale");
                if(solver==null||Mathf.Abs(s.x-worldScale)>.0001f){worldScale=s.x;solver=new SecondaryRig(Load(),pmxScale*worldScale);reset=true;}
                foreach(int i in solver.Required){positions[i]=Vec(bones[i].localPosition);saved[i]=bones[i].localRotation;rotations[i]=Quat(saved[i]);}
                captured=true;
                solver.Tick(dt,positions,rotations,Vec(root.position),Quat(root.rotation),worldScale,reset);reset=false;
                foreach(int bone in solver.Owned)bones[bone].localRotation=saved[bone]*Unity(solver.Delta[bone]);
            }
            catch(Exception e)
            {
                if(captured)foreach(int bone in solver.Owned)if(bones[bone]!=null)bones[bone].localRotation=saved[bone];
                failed=true;warn("Secondary motion disabled for this avatar; animation retained: "+e.Message);
            }
        }
    }
}
