using System;using System.Numerics;
namespace WKPmxAvatar.SecondaryMotion
{
    // Cross-column distance constraints for a closed skirt. This is a PMX surface
    // adaptation, not a reconstruction of the original six-chain cloth mesh.
    public static class SkirtSurface
    {
        static void Edge(BoneChain[] chains,Vector3[][] targets,int a,int levelA,int b,int levelB,float strength)
        {
            var delta=chains[b].Point(levelB)-chains[a].Point(levelA);float length=delta.Length(),rest=Vector3.Distance(targets[a][levelA],targets[b][levelB]);
            if(length<1e-7f)return;float inverseMass=(levelA==0?0:1)+(levelB==0?0:1);if(inverseMass==0)return;
            var correction=delta*((length-rest)/length*strength/inverseMass);
            if(levelA>0)chains[a].MovePoint(levelA,correction);if(levelB>0)chains[b].MovePoint(levelB,-correction);
        }
        public static void Solve(BoneChain[] chains,Vector3[][] targets,Capsule[][] colliders,int passes=48)
        {
            if(chains.Length<3||targets.Length!=chains.Length||colliders.Length!=chains.Length)throw new ArgumentException("Surface dimensions");
            int levels=chains[0].Count-1; // terminal is an unweighted guide, outside the skirt mesh
            for(int pass=0;pass<passes;pass++){
                for(int level=1;level<levels;level++)for(int offset=0;offset<chains.Length;offset++){
                    int a=(offset+pass)%chains.Length,b=(a+1)%chains.Length;
                    Edge(chains,targets,a,level,b,level,.7f);
                    // Both diagonals constrain shear in each reconstructed PMX panel.
                    Edge(chains,targets,a,level,b,level-1,.35f);
                    Edge(chains,targets,b,level,a,level-1,.35f);
                }
                for(int i=0;i<chains.Length;i++)chains[i].ProjectShape(targets[i],colliders[i]);
            }
        }
        public static float MaximumRelativeEdgeError(BoneChain[] chains,Vector3[][] target)
        {float maximum=0;for(int a=0;a<chains.Length;a++){int b=(a+1)%chains.Length;for(int level=1;level<chains[a].Count-1;level++){float rest=Vector3.Distance(target[a][level],target[b][level]);if(rest>1e-6f)maximum=Math.Max(maximum,Math.Abs(Vector3.Distance(chains[a].Point(level),chains[b].Point(level))-rest)/rest);}}return maximum;}
        public static (float error,int column,int level) WorstEdge(BoneChain[] chains,Vector3[][] target)
        {var result=(error:0f,column:0,level:0);for(int a=0;a<chains.Length;a++)for(int level=1;level<chains[a].Count-1;level++){int b=(a+1)%chains.Length;float error=Math.Abs(Vector3.Distance(chains[a].Point(level),chains[b].Point(level))-Vector3.Distance(target[a][level],target[b][level]));if(error>result.error)result=(error,a,level);}return result;}
    }
}
