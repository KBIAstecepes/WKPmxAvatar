using System;using System.Numerics;
namespace WKPmxAvatar.SecondaryMotion
{
    // Independent position-based prototype. Native parameters are reference inputs;
    // this does not reproduce BeyondBoneCloth's full solver, cloth mesh or scheduling.
    public readonly struct Capsule
    {
        public readonly Vector3 A,B;public readonly float RadiusA,RadiusB;public readonly int MinimumPoint;
        public Capsule(Vector3 a,Vector3 b,float ra,float rb,int minimumPoint=1){A=a;B=b;RadiusA=ra;RadiusB=rb;MinimumPoint=Math.Max(1,minimumPoint);}
        public Vector3 Correction(Vector3 point,float particleRadius,Vector3 fallback)
        {var axis=B-A;float t=axis.LengthSquared()>1e-10f?Math.Clamp(Vector3.Dot(point-A,axis)/axis.LengthSquared(),0,1):0;var center=A+axis*t;float radius=RadiusA+(RadiusB-RadiusA)*t+particleRadius;var delta=point-center;float length=delta.Length();if(length>=radius)return Vector3.Zero;var normal=length>1e-6f?delta/length:Unit(fallback,Vector3.UnitZ);return normal*(radius-length);}
        static Vector3 Unit(Vector3 v,Vector3 fallback)=>v.LengthSquared()>1e-10f?Vector3.Normalize(v):fallback;
    }
    public sealed class ChainSettings
    {
        public Vector3 Gravity;public float WorldInertia=1,Restoring=0.2f,MaxDisplacement=.08f;
        public float VelocityAttenuation,ParticleSpeedLimit=float.PositiveInfinity,MovementSpeedLimit=float.PositiveInfinity;
        public float[] Damping,Radii;public float MaxAngleRadians=MathF.PI*.45f;
    }
    public sealed class BoneChain
    {
        public const float FixedDelta=1f/90;
        readonly ChainSettings settings;readonly Vector3[] points,previous;readonly float[] lengths;
        Vector3 lastRoot,stepRootDelta;bool ready,pendingVelocity;public int Count=>points.Length;public Vector3 Point(int i)=>points[i];
        public BoneChain(int count,ChainSettings settings)
        {if(count<2||count>64||settings==null||settings.Damping?.Length!=count||settings.Radii?.Length!=count)throw new ArgumentException("Chain dimensions");this.settings=settings;points=new Vector3[count];previous=new Vector3[count];lengths=new float[count];}
        static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
        static Vector3 Unit(Vector3 p,Vector3 fallback)=>p.LengthSquared()>1e-12f?Vector3.Normalize(p):fallback;
        Vector3 LimitDirection(Vector3 direction,Vector3 baseline)
        {float angle=MathF.Acos(Math.Clamp(Vector3.Dot(direction,baseline),-1,1));if(angle<=settings.MaxAngleRadians)return direction;var axis=Vector3.Cross(baseline,direction);if(axis.LengthSquared()<1e-10f)axis=Vector3.Cross(baseline,Math.Abs(baseline.X)<.9f?Vector3.UnitX:Vector3.UnitY);return Vector3.Transform(baseline,Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis),settings.MaxAngleRadians));}
        public void MovePoint(int index,Vector3 correction)
        {if(index<=0||index>=Count||!Finite(correction))throw new ArgumentException("Invalid constrained point");points[index]+=correction;previous[index]+=correction;}
        public void ProjectShape(Vector3[] target,Capsule[] colliders)
        {
            points[0]=target[0];
            for(int i=1;i<Count;i++){
                var baseline=Unit(target[i]-target[i-1],Vector3.UnitY);
                if(colliders!=null)foreach(var c in colliders)if(i>=c.MinimumPoint)points[i]+=c.Correction(points[i],settings.Radii[i],baseline);
                points[i]=points[i-1]+LimitDirection(Unit(points[i]-points[i-1],baseline),baseline)*lengths[i];
            }
        }
        public void Reset(Vector3[] target)
        {if(target.Length!=Count)throw new ArgumentException("Target count");for(int i=0;i<Count;i++){if(!Finite(target[i]))throw new ArgumentException("Non-finite animation point");points[i]=previous[i]=target[i];if(i>0){lengths[i]=Vector3.Distance(target[i-1],target[i]);if(lengths[i]<1e-6f)throw new ArgumentException("Zero-length segment");}}lastRoot=target[0];stepRootDelta=Vector3.Zero;pendingVelocity=false;ready=true;}
        // Called after all shape/surface constraints. Explicit prototype interpretation:
        // attenuation is a removed fraction per 60-Hz reference step; speed is relative
        // to the anchor so fast player travel does not freeze clothing in world space.
        public void FinishStep()
        {if(!pendingVelocity)return;float retained=MathF.Pow(1-Math.Clamp(settings.VelocityAttenuation,0,.999f),FixedDelta*60);for(int i=1;i<Count;i++){var relative=points[i]-previous[i]-stepRootDelta;relative*=retained;float cap=settings.ParticleSpeedLimit*FixedDelta;if(relative.Length()>cap)relative=Vector3.Normalize(relative)*cap;previous[i]=points[i]-stepRootDelta-relative;}pendingVelocity=false;}
        public bool Step(Vector3[] target,Capsule[] colliders)
        {
            if(target.Length!=Count)throw new ArgumentException("Target count");foreach(var p in target)if(!Finite(p))throw new ArgumentException("Non-finite target");
            FinishStep();
            if(!ready||Vector3.Distance(target[0],lastRoot)>2){Reset(target);return false;}
            var rootDelta=target[0]-lastRoot;lastRoot=target[0];stepRootDelta=rootDelta;pendingVelocity=true;float h=FixedDelta;
            var inertialDelta=rootDelta;float inertialCap=settings.MovementSpeedLimit*h;if(inertialDelta.Length()>inertialCap)inertialDelta=Vector3.Normalize(inertialDelta)*inertialCap;
            // Transport the non-inertial fraction with the animated anchor, without changing game movement.
            for(int i=1;i<Count;i++){var transport=rootDelta-inertialDelta*Math.Clamp(settings.WorldInertia,0,1);points[i]+=transport;previous[i]+=transport;var velocity=(points[i]-previous[i])*MathF.Pow(1-Math.Clamp(settings.Damping[i],0,.99f),h*60);previous[i]=points[i];points[i]+=velocity+settings.Gravity*(h*h);}
            points[0]=previous[0]=target[0];
            float restore=1-MathF.Exp(-Math.Max(0,settings.Restoring)*18*h);
            for(int pass=0;pass<10;pass++){
                for(int i=1;i<Count;i++){
                    var baseDirection=Unit(target[i]-target[i-1],Vector3.UnitY);var desired=points[i-1]+baseDirection*lengths[i];points[i]=Vector3.Lerp(points[i],desired,restore/10);
                    var displacement=points[i]-target[i];float maximum=settings.MaxDisplacement;if(maximum>0&&displacement.LengthSquared()>maximum*maximum)points[i]=target[i]+Vector3.Normalize(displacement)*maximum;
                    var direction=LimitDirection(Unit(points[i]-points[i-1],baseDirection),baseDirection);
                    points[i]=points[i-1]+direction*lengths[i];
                    if(colliders!=null)foreach(var c in colliders)if(i>=c.MinimumPoint)points[i]+=c.Correction(points[i],settings.Radii[i],baseDirection);
                }
            }
            // Rotation-only skinning cannot reproduce stretched particle segments.
            // Keep exact animated segment lengths; collision remains best-effort
            // when the two constraints conflict (reported separately by the study).
            for(int i=1;i<Count;i++){var baseline=Unit(target[i]-target[i-1],Vector3.UnitY);points[i]=points[i-1]+LimitDirection(Unit(points[i]-points[i-1],baseline),baseline)*lengths[i];}
            for(int i=0;i<Count;i++)if(!Finite(points[i])){Reset(target);return false;}
            return true;
        }
        public float MaximumLengthError(){float error=0;for(int i=1;i<Count;i++)error=Math.Max(error,Math.Abs(Vector3.Distance(points[i-1],points[i])-lengths[i]));return error;}
    }
}
