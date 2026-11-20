using System;
using System.Collections.Generic;
using System.Numerics;
using WKPmxAvatar.SecondaryMotion;

namespace WKPmxAvatar.RuntimePhysics
{
    // Units in the embedded profile are PMX units. Parameters retain their prototype provenance.
    public sealed class Profile
    {
        public string[] Names;
        public int[] Parents;
        public float[][] Rest;
        public ChainDefinition[] Chains;
        public ColliderDefinition[] Colliders;
    }
    public sealed class ChainDefinition
    {
        public string Name, Source;
        public int[] Bones, Colliders;
        public float[] Tip, Damping, Radii, Gravity;
        public float WorldInertia, Restoring, MaxDisplacement, VelocityAttenuation, ParticleSpeedLimit, MovementSpeedLimit, AngleDegrees;
        public bool Skirt;
    }
    public sealed class ColliderDefinition
    {
        public int Bone, MinimumPoint;
        public float[] A, B;
        public float RA, RB;
    }

    // Shared by the Unity adapter and the executable offline tests. No Unity or game APIs.
    // Only dynamic local rotations are returned; animation positions and gameplay remain owned by their callers.
    public sealed class SecondaryRig
    {
        public const int MaxSteps = 6;
        public const int SurfacePasses = 12;
        public const float MaximumFrameDelta = MaxSteps * BoneChain.FixedDelta;
        sealed class Chain
        {
            internal ChainDefinition Definition;
            internal BoneChain Solver;
            internal Vector3[] Target;
            internal Capsule[] Shapes;
        }
        readonly Profile profile;
        readonly Chain[] chains;
        readonly int[][] children;
        readonly int[] roots;
        readonly Vector3[] priorP, workingP, worldP;
        readonly Quaternion[] priorQ, animationQ, workingQ, worldQ;
        readonly BoneChain[] skirtSolvers;
        readonly Vector3[][] skirtTargets;
        readonly Capsule[][] skirtShapes;
        readonly float unitScale;
        Vector3 priorOrigin;
        Quaternion priorFrame;
        float accumulator;
        bool ready;
        public int StepsLastFrame { get; private set; }
        public int ResetCount { get; private set; }
        public int[] Owned { get; }
        public Quaternion[] Delta { get; }
        public int[] Required { get; }
        public float MaximumLengthError { get; private set; }

        public SecondaryRig(Profile definition, float pmxToWorld)
        {
            profile=definition;
            if(!float.IsFinite(pmxToWorld)||pmxToWorld<=0)throw new ArgumentException("Invalid model scale");
            unitScale=pmxToWorld;
            int count=profile.Names.Length;
            priorP=new Vector3[count]; workingP=new Vector3[count]; worldP=new Vector3[count];
            priorQ=new Quaternion[count];animationQ=new Quaternion[count];workingQ=new Quaternion[count];worldQ=new Quaternion[count];
            Delta=new Quaternion[count];
            var rootList=new List<int>();var childLists=new List<int>[count];
            for(int i=0;i<count;i++)childLists[i]=new List<int>();
            for(int i=0;i<count;i++){int parent=profile.Parents[i];if(parent<0)rootList.Add(i);else childLists[parent].Add(i);Delta[i]=Quaternion.Identity;}
            roots=rootList.ToArray();children=new int[count][];for(int i=0;i<count;i++)children[i]=childLists[i].ToArray();
            chains=new Chain[profile.Chains.Length];var owned=new List<int>();var seen=new HashSet<int>();
            var skirts=new List<Chain>();
            for(int i=0;i<chains.Length;i++)
            {
                var d=profile.Chains[i];int n=d.Bones.Length+1;
                var settings=new ChainSettings{Damping=(float[])d.Damping.Clone(),Radii=new float[n],Gravity=Vec(d.Gravity)*unitScale,
                    WorldInertia=d.WorldInertia,Restoring=d.Restoring,MaxDisplacement=d.MaxDisplacement*unitScale,
                    VelocityAttenuation=d.VelocityAttenuation,ParticleSpeedLimit=d.ParticleSpeedLimit*unitScale,
                    MovementSpeedLimit=d.MovementSpeedLimit*unitScale,MaxAngleRadians=d.AngleDegrees*MathF.PI/180};
                for(int p=0;p<n;p++)settings.Radii[p]=d.Radii[p]*unitScale;
                foreach(int bone in d.Bones){if(!seen.Add(bone))throw new ArgumentException("Duplicate bone owner");owned.Add(bone);}
                var c=new Chain{Definition=d,Solver=new BoneChain(n,settings),Target=new Vector3[n],Shapes=new Capsule[d.Colliders.Length]};chains[i]=c;
                if(d.Skirt)skirts.Add(c);
            }
            Owned=owned.ToArray();
            var needed=new HashSet<int>();
            void Require(int b){while(b>=0&&needed.Add(b))b=profile.Parents[b];}
            foreach(int b in Owned)Require(b);
            foreach(var c in profile.Chains)foreach(int id in c.Colliders)Require(profile.Colliders[id].Bone);
            Required=new int[needed.Count];needed.CopyTo(Required);Array.Sort(Required);skirtSolvers=new BoneChain[skirts.Count];skirtTargets=new Vector3[skirts.Count][];skirtShapes=new Capsule[skirts.Count][];
            for(int i=0;i<skirts.Count;i++){skirtSolvers[i]=skirts[i].Solver;skirtTargets[i]=skirts[i].Target;skirtShapes[i]=skirts[i].Shapes;}
        }
        public static Vector3 Vec(float[] v)=>new Vector3(v[0],v[1],v[2]);
        static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
        static bool Finite(Quaternion q)=>float.IsFinite(q.X)&&float.IsFinite(q.Y)&&float.IsFinite(q.Z)&&float.IsFinite(q.W)&&q.LengthSquared()>.5f&&q.LengthSquared()<1.5f;
        public void Invalidate(){ready=false;accumulator=0;foreach(int bone in Owned)Delta[bone]=Quaternion.Identity;}
        void Resolve(int bone,Vector3 parentP,Quaternion parentQ)
        {
            worldP[bone]=parentP+Vector3.Transform(workingP[bone],parentQ);
            worldQ[bone]=Quaternion.Normalize(parentQ*workingQ[bone]);
            foreach(int child in children[bone])Resolve(child,worldP[bone],worldQ[bone]);
        }
        void ResolveChildren(int bone)
        {foreach(int child in children[bone])Resolve(child,worldP[bone],worldQ[bone]);}
        void SetWorld(int bone,Quaternion rotation,Quaternion frame)
        {
            int parent=profile.Parents[bone];worldQ[bone]=rotation;
            workingQ[bone]=Quaternion.Normalize(Quaternion.Inverse(parent<0?frame:worldQ[parent])*rotation);
            ResolveChildren(bone);
        }
        static Quaternion FromTo(Vector3 a,Vector3 b)
        {
            if(a.LengthSquared()<1e-12f||b.LengthSquared()<1e-12f)return Quaternion.Identity;
            a=Vector3.Normalize(a);b=Vector3.Normalize(b);float dot=Math.Clamp(Vector3.Dot(a,b),-1,1);
            if(dot>.999999f)return Quaternion.Identity;
            if(dot<-.999999f){var axis=Vector3.Cross(a,Vector3.UnitX);if(axis.LengthSquared()<1e-8f)axis=Vector3.Cross(a,Vector3.UnitY);return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis),MathF.PI);}
            return Quaternion.Normalize(new Quaternion(Vector3.Cross(a,b),1+dot));
        }
        void Targets(Chain c)
        {
            var ids=c.Definition.Bones;
            for(int i=0;i<ids.Length;i++)c.Target[i]=worldP[ids[i]];
            c.Target[ids.Length]=worldP[ids[ids.Length-1]]+Vector3.Transform(Vec(c.Definition.Tip)*unitScale,worldQ[ids[ids.Length-1]]);
            for(int i=0;i<c.Shapes.Length;i++)
            {
                var d=profile.Colliders[c.Definition.Colliders[i]];var q=worldQ[d.Bone];var p=worldP[d.Bone];
                c.Shapes[i]=new Capsule(p+Vector3.Transform(Vec(d.A)*unitScale,q),p+Vector3.Transform(Vec(d.B)*unitScale,q),d.RA*unitScale,d.RB*unitScale,d.MinimumPoint);
            }
        }
        void Fit(Chain c,Quaternion frame)
        {
            // Cache every target rotation before ancestors are written. Otherwise parent changes
            // would be applied twice to descendants during rotation-only reconstruction.
            for(int i=0;i<c.Definition.Bones.Length;i++)
            {
                int bone=c.Definition.Bones[i];
                // Current world orientation already includes modified ancestors, so use
                // the cached pre-write world orientation for this chain instead.
                Quaternion original=fitRotations[i];
                var delta=FromTo(c.Target[i+1]-c.Target[i],c.Solver.Point(i+1)-c.Solver.Point(i));
                SetWorld(bone,Quaternion.Normalize(delta*original),frame);
            }
        }
        readonly Quaternion[] fitRotations=new Quaternion[64];
        void CacheRotations(Chain c){for(int i=0;i<c.Definition.Bones.Length;i++)fitRotations[i]=worldQ[c.Definition.Bones[i]];}
        void Solve(Quaternion frame,bool reset)
        {
            // Skirt chains are independent in the skeleton and share a ring constraint.
            foreach(var c in chains)if(c.Definition.Skirt){Targets(c);if(reset)c.Solver.Reset(c.Target);else c.Solver.Step(c.Target,c.Shapes);}
            if(!reset&&skirtSolvers.Length>=3)SkirtSurface.Solve(skirtSolvers,skirtTargets,skirtShapes,SurfacePasses);
            foreach(var c in chains)
            {
                if(!c.Definition.Skirt){Targets(c);if(reset)c.Solver.Reset(c.Target);else c.Solver.Step(c.Target,c.Shapes);}
                if(!reset){CacheRotations(c);Fit(c,frame);c.Solver.FinishStep();MaximumLengthError=Math.Max(MaximumLengthError,c.Solver.MaximumLengthError());}
            }
        }
        void CopyPrevious(Vector3[] p,Quaternion[] q,Vector3 origin,Quaternion frame)
        {Array.Copy(p,priorP,p.Length);Array.Copy(q,priorQ,q.Length);priorOrigin=origin;priorFrame=frame;}
        void Prepare(Vector3[] p,Quaternion[] q,float amount,Vector3 origin,Quaternion frame,float worldScale)
        {
            for(int i=0;i<p.Length;i++){workingP[i]=Vector3.Lerp(priorP[i],p[i],amount)*worldScale;animationQ[i]=workingQ[i]=Quaternion.Normalize(Quaternion.Slerp(priorQ[i],q[i],amount));}
            foreach(int root in roots)Resolve(root,origin,frame);
        }
        public void Tick(float dt,Vector3[] localP,Quaternion[] localQ,Vector3 origin,Quaternion frame,float worldScale,bool reset=false)
        {
            StepsLastFrame=0;
            if(localP.Length!=priorP.Length||localQ.Length!=priorQ.Length)throw new ArgumentException("Rig size changed");
            if(!float.IsFinite(dt)||!Finite(origin)||!Finite(frame)||!float.IsFinite(worldScale)||worldScale<=0)throw new ArgumentException("Invalid frame input");
            for(int i=0;i<localP.Length;i++)if(!Finite(localP[i])||!Finite(localQ[i]))throw new ArgumentException("Invalid animated pose");
            if(dt<=0){Invalidate();return;}
            reset|=!ready||dt>MaximumFrameDelta+1e-6f||Vector3.Distance(origin,priorOrigin)>2||Math.Abs(Quaternion.Dot(priorFrame,frame))<.7071067f;
            if(reset)
            {
                CopyPrevious(localP,localQ,origin,frame);Prepare(localP,localQ,1,origin,frame,worldScale);Solve(frame,true);
                foreach(int bone in Owned)Delta[bone]=Quaternion.Identity;
                ready=true;accumulator=0;ResetCount++;return;
            }
            float oldAccumulator=accumulator;accumulator+=dt;
            int steps=Math.Min(MaxSteps,(int)MathF.Floor((accumulator+1e-7f)/BoneChain.FixedDelta));
            for(int step=0;step<steps;step++)
            {
                float t=Math.Clamp(((step+1)*BoneChain.FixedDelta-oldAccumulator)/dt,0,1);
                var stepFrame=Quaternion.Normalize(Quaternion.Slerp(priorFrame,frame,t));
                Prepare(localP,localQ,t,Vector3.Lerp(priorOrigin,origin,t),stepFrame,worldScale);Solve(stepFrame,false);
                foreach(int bone in Owned)Delta[bone]=Quaternion.Normalize(Quaternion.Inverse(animationQ[bone])*workingQ[bone]);
            }
            accumulator=Math.Max(0,accumulator-steps*BoneChain.FixedDelta);StepsLastFrame=steps;
            CopyPrevious(localP,localQ,origin,frame);
        }
    }
}
