using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

// Diagnostic PMX adaptation: project iris vertices onto the sclera's XY
// triangles, keeping the original iris RGB UVs in UV0. UV1 holds face-atlas UV.
public static partial class EyeFaceUvTransfer
{
    sealed class Prepared{public bool valid;public Stats stats;}
    static readonly ConditionalWeakTable<Mesh,Prepared> prepared=new ConditionalWeakTable<Mesh,Prepared>();
    public static int ProjectionRuns{get;private set;}
    public static bool TryGetStatistics(Mesh mesh,out Stats stats)
    {
        stats=null;if(mesh==null||!prepared.TryGetValue(mesh,out var state)||!state.valid)return false;stats=state.stats;return stats!=null;
    }
    public static bool EnsurePrepared(Mesh mesh,int iris,int sclera)
    {
        if(mesh==null||iris<0||sclera<0)return false;
        if(prepared.TryGetValue(mesh,out var prior))return prior.valid;
        var state=new Prepared();prepared.Add(mesh,state);
        try
        {
            if((mesh.vertexCount&1)!=0 || mesh.uv2.Length!=0)throw new InvalidDataException("Unsupported PMX mesh layout or occupied UV1");
            int count=mesh.vertexCount/2;var v=mesh.vertices;
            for(int i=0;i<count;i++)if((v[i]-v[i+count]).sqrMagnitude>1e-12f)throw new InvalidDataException("PMX backface duplication differs");
            Apply(mesh,iris,sclera,count);state.valid=true;
        }
        catch(Exception ex){
#if WKPMX_DEV
            Debug.LogWarning("Rossi eye-face linkage left disabled: "+ex.Message);
#else
            _=ex;
            Debug.LogWarning("Rossi eye-face linkage unavailable for this mesh [R020].");
#endif
        }
        return state.valid;
    }
    [Serializable] public class Stats{public string scope;public int vertices,inside,nearestFallback;public float maxDistance,meanDistance;}
    public static Stats Apply(Mesh mesh,int iris,int sclera,int originalCount)
    {
        var positions=mesh.vertices;var uv=mesh.uv;var context=(Vector2[])uv.Clone();var source=mesh.GetTriangles(sclera);var targets=mesh.GetTriangles(iris);var seen=new bool[originalCount];
        var result=new Stats{scope="Diagnostic XY surface projection onto actual PMX sclera triangles. UV0 unchanged. This is a facial-context approximation, not original-game UV recovery."};
        Vector3 Nearest(Vector2 p,Vector2 a,Vector2 b,Vector2 c,out bool inside)
        {
            float det=(b.y-c.y)*(a.x-c.x)+(c.x-b.x)*(a.y-c.y);
            if(Mathf.Abs(det)>1e-12f)
            {
                float x=((b.y-c.y)*(p.x-c.x)+(c.x-b.x)*(p.y-c.y))/det;
                float y=((c.y-a.y)*(p.x-c.x)+(a.x-c.x)*(p.y-c.y))/det;
                float z=1-x-y;inside=x>=-1e-5f&&y>=-1e-5f&&z>=-1e-5f;
                if(inside)return new Vector3(x,y,z);
            }
            inside=false;float best=float.MaxValue;Vector3 weights=Vector3.right;
            for(int edge=0;edge<3;edge++)
            {
                Vector2 start=edge==0?a:edge==1?b:c,end=edge==0?b:edge==1?c:a,d=end-start;
                float t=Mathf.Clamp01(Vector2.Dot(p-start,d)/Mathf.Max(1e-12f,d.sqrMagnitude));float distance=(start+d*t-p).sqrMagnitude;
                if(distance>=best)continue;best=distance;weights=edge==0?new Vector3(1-t,t,0):edge==1?new Vector3(0,1-t,t):new Vector3(t,0,1-t);
            }
            return weights;
        }
        foreach(int raw in targets)
        {
            int index=raw%originalCount;if(seen[index])continue;seen[index]=true;
            var p=positions[index];float best=float.MaxValue;Vector2 chosen=Vector2.zero;bool hit=false;float chosenDistance=0;
            for(int i=0;i<source.Length;i+=3)
            {
                int a=source[i],b=source[i+1],c=source[i+2];if(a>=originalCount||b>=originalCount||c>=originalCount)continue;
                // Keep left/right eyes disconnected even when nearest projection
                // is used at the iris border.
                if(Mathf.Sign((positions[a].x+positions[b].x+positions[c].x)/3)!=Mathf.Sign(p.x))continue;
                bool inside;var w=Nearest(p,positions[a],positions[b],positions[c],out inside);
                Vector3 q=positions[a]*w.x+positions[b]*w.y+positions[c]*w.z;float xy=new Vector2(q.x-p.x,q.y-p.y).sqrMagnitude;
                float score=(inside?0:1)+xy*10000+Mathf.Abs(q.z-p.z)*.001f;
                if(score>=best)continue;best=score;hit=inside;chosen=uv[a]*w.x+uv[b]*w.y+uv[c]*w.z;chosenDistance=Mathf.Sqrt(xy);
            }
            if(best==float.MaxValue||chosen.x<0||chosen.x>1||chosen.y<0||chosen.y>1)throw new InvalidDataException("No bounded face UV projection for iris vertex");
            if(chosenDistance>.012f)throw new InvalidDataException("Iris/sclera projection exceeds diagnostic 12mm limit");
            context[index]=context[index+originalCount]=chosen;result.vertices++;if(hit)result.inside++;else result.nearestFallback++;
            result.maxDistance=Mathf.Max(result.maxDistance,chosenDistance);result.meanDistance+=chosenDistance;
        }
        result.meanDistance/=Mathf.Max(1,result.vertices);mesh.uv2=context;ProjectionRuns++;
        if(!prepared.TryGetValue(mesh,out var state)){state=new Prepared();prepared.Add(mesh,state);}state.valid=true;state.stats=result;
        return result;
    }
}
