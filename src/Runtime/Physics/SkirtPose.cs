using System;using System.Numerics;
namespace WKPmxAvatar.RuntimePhysics
{
    // Pose-space prototype: rotate fixed-length skirt panels around their original roots.
    // This is a corrective animation pose, not a particle simulation or an envelope stretch.
    public sealed class SkirtPose
    {
        readonly Vector3[] roots=new Vector3[16],directions=new Vector3[16],ends=new Vector3[16];
        const int HingeRow = 3;
        readonly Vector3[] hinges=new Vector3[16];
        readonly float[] hingeBaseAngles=new float[16], inward=new float[16];
        readonly float[] baseAngles=new float[16],raw=new float[16];
        readonly Vector3[] spheres=new Vector3[42];
        readonly float[] radii=new float[42];
        readonly float scale;
        public readonly int[] Bones=new int[160];
        public readonly Vector3[] LocalPositions=new Vector3[160];
        public readonly Quaternion[] Rotations=new Quaternion[160];
        public readonly float[] Angles=new float[16];
        public SkirtPose(Profile p,float pmxScale)
        {
            scale=pmxScale;var origin=SecondaryRig.Vec(p.Rest[14]);
            for(int row=0;row<10;row++)for(int col=0;col<16;col++)
            {int at=row*16+col,b=Array.IndexOf(p.Names,"スカート_"+row+"_"+col);Bones[at]=b;LocalPositions[at]=(SecondaryRig.Vec(p.Rest[b])-SecondaryRig.Vec(p.Rest[p.Parents[b]]))*scale;Rotations[at]=Quaternion.Identity;if(row==0)roots[col]=(SecondaryRig.Vec(p.Rest[b])-origin)*scale;if(row==9)ends[col]=(SecondaryRig.Vec(p.Rest[b])-origin)*scale;}
            for(int col=0;col<16;col++)
            {
                var d=ends[col]-roots[col];directions[col]=Vector3.Normalize(new Vector3(d.X,0,d.Z));baseAngles[col]=MathF.Atan2(new Vector2(d.X,d.Z).Length(),-d.Y);
                hinges[col]=(SecondaryRig.Vec(p.Rest[Bones[HingeRow*16+col]])-origin)*scale;
                var lower=ends[col]-hinges[col];hingeBaseAngles[col]=MathF.Atan2(Vector3.Dot(lower,directions[col]),-lower.Y);
            }
        }
        public void Evaluate(Vector3 lh,Vector3 lk,Vector3 la,Vector3 rh,Vector3 rk,Vector3 ra,Vector3 lHeel,Vector3 lToe,Vector3 rHeel,Vector3 rToe)
        {
            if(!Finite(lh)||!Finite(lk)||!Finite(la)||!Finite(rh)||!Finite(rk)||!Finite(ra)||!Finite(lHeel)||!Finite(lToe)||!Finite(rHeel)||!Finite(rToe)||(lk-lh).LengthSquared()<1e-10f||(rk-rh).LengthSquared()<1e-10f)
                throw new ArgumentException("Invalid leg pose");
            var sum=Vector3.Normalize(lk-lh)+Vector3.Normalize(rk-rh);
            var average=sum.LengthSquared()<1e-8f?-Vector3.UnitY:Vector3.Normalize(sum);
            var axis=Vector3.Cross(-Vector3.UnitY,average);
            float commonAngle=MathF.Acos(Math.Clamp(Vector3.Dot(-Vector3.UnitY,average),-1,1))*.45f;
            var common=axis.LengthSquared()<1e-8f?Quaternion.Identity:Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis),Math.Min(commonAngle,.70f));
            var inverse=Quaternion.Inverse(common);
            // Boot radius fitted to all ankle/foot-weighted mesh vertices: max 1.028832 PMX units.
            int n=0;Add(lh,lk,.90f*scale,ref n);Add(lk,la,.75f*scale,ref n);Add(rh,rk,.90f*scale,ref n);Add(rk,ra,.75f*scale,ref n);Add(lHeel,lToe,1.03f*scale,ref n);Add(rHeel,rToe,1.03f*scale,ref n);
            // All panels use the same tilt frame. Convert the leg samples just once.
            for(int s=0;s<n;s++)spheres[s]=Vector3.Transform(spheres[s],inverse);
            for(int c=0;c<16;c++)
                raw[c]=Math.Clamp(ContactAngle(c,Vector3.Transform(roots[c],inverse),Vector3.Distance(ends[c],roots[c]),baseAngles[c],0),0,1.10f);
            var leftThigh=Vector3.Normalize(lk-lh);var rightThigh=Vector3.Normalize(rk-rh);
            float feetClear=Smooth((-4.3f*scale-Math.Max(la.Y,ra.Y))/scale);
            float fit=PoseDrapeKeys.Weight(lk-lh,la-lh,rk-rh,ra-rh,scale)*feetClear;
            for(int c=0;c<16;c++)
            {
                float lift=Math.Max(Vector3.Dot(leftThigh,directions[c]),Vector3.Dot(rightThigh,directions[c]));
                float drapeWeight=Smooth((lift-.10f)/.65f)*fit;
                float angle=RingMaximum(raw,c,.12f)-.07f*drapeWeight;Angles[c]=angle;
                var panelAxis=Vector3.Cross(-Vector3.UnitY,directions[c]);var opening=Quaternion.CreateFromAxisAngle(panelAxis,angle);
                Rotations[c]=Quaternion.Normalize(common*opening);
                // Only the panel facing a raised thigh curls towards it. A nearby boot
                // or knee can cancel the curl through the same bounded contact query.
                var maxDrop=.40f*drapeWeight;
                var hinge=Vector3.Transform(roots[c],inverse)+Vector3.Transform(hinges[c]-roots[c],opening);
                float lowerLimit=ContactAngle(c,hinge,Vector3.Distance(ends[c],hinges[c]),hingeBaseAngles[c],angle-maxDrop);
                inward[c]=Math.Clamp(lowerLimit-angle,-maxDrop,0);
            }
            for(int c=0;c<16;c++)
                Rotations[HingeRow*16+c]=Quaternion.CreateFromAxisAngle(Vector3.Cross(-Vector3.UnitY,directions[c]),RingMaximum(inward,c,.12f));
        }
        float ContactAngle(int c,Vector3 origin,float length,float restAngle,float minimum)
        {
                float angle=minimum;var radial=directions[c];var tangent=Vector3.Cross(-Vector3.UnitY,radial);
                for(int s=0;s<spheres.Length;s++)
                {
                    var relative=spheres[s]-origin;float sideways=Math.Abs(Vector3.Dot(relative,tangent));
                    // A small contact allowance; do not inflate the whole skirt for clearance.
                    float r=radii[s]+.08f*scale,band=.40f*scale,fade=.60f*scale;
                    if(sideways>r+band+fade)continue;
                    float projectedRadius=MathF.Sqrt(Math.Max(0,r*r-MathF.Pow(Math.Max(0,sideways-band),2)));
                    float x=Vector3.Dot(relative,radial),y=-relative.Y,d=MathF.Sqrt(x*x+y*y);
                    if(y<=0||d>length+projectedRadius+fade||d<1e-6f)continue;
                    float required=MathF.Atan2(x,y)+MathF.Asin(Math.Clamp(projectedRadius/d,0,1))-restAngle;
                    // Blend out at each pose region boundary, otherwise an entering knee
                    // can switch a whole panel by ~20 degrees in a single source frame.
                    float sideWeight=Smooth((r+band+fade-sideways)/fade);
                    float reachWeight=Smooth((length+projectedRadius+fade-d)/fade);
                    required=minimum+(required-minimum)*sideWeight*reachWeight;
                    angle=Math.Max(angle,required);
                }
                return angle;
        }
        static float RingMaximum(float[] values,int c,float slope)
        {float angle=values[c];for(int j=0;j<16;j++){int distance=Math.Min(Math.Abs(j-c),16-Math.Abs(j-c));angle=Math.Max(angle,values[j]-distance*slope);}return angle;}
        void Add(Vector3 a,Vector3 b,float radius,ref int n)
        {for(int i=0;i<7;i++){spheres[n]=Vector3.Lerp(a,b,i/6f);radii[n++]=radius;}}
        static float Smooth(float value){float x=Math.Clamp(value,0,1);return x*x*(3-2*x);}
        static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    }
}
