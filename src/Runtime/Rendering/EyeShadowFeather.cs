using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

// PMX-specific visual adaptation, not reconstructed HGRP hair/eyelid shadows.
// UV2 stores bind-pose distance from the authored overlay's lower boundary.
// Skinning then carries this scalar with the original geometry. Positions,
// normals, UV0, iris UV1 and triangle topology are never modified.
public static class EyeShadowFeather
{
    sealed class State { public bool valid; public Stats stats; }
    struct Edge { public int a,b,opposite,count; }
    [Serializable] public sealed class Stats
    {
        public int vertices,boundaryEdges,lowerEdges;
        public float maximumDistance;
    }
    static readonly ConditionalWeakTable<Mesh,State> cache=new ConditionalWeakTable<Mesh,State>();
    public static int PreparationRuns {get;private set;}
    public static bool TryGetStatistics(Mesh mesh,out Stats stats)
    {
        stats=null;if(mesh==null||!cache.TryGetValue(mesh,out var state)||!state.valid)return false;stats=state.stats;return true;
    }
    public static bool EnsurePrepared(Mesh mesh,int materialIndex)
    {
        if(mesh==null||materialIndex<0||materialIndex>=mesh.subMeshCount)return false;
        if(cache.TryGetValue(mesh,out var prior))return prior.valid;
        var state=new State();cache.Add(mesh,state);
        try
        {
            if((mesh.vertexCount&1)!=0||mesh.uv3.Length!=0)throw new InvalidOperationException("Unsupported mesh layout or occupied UV2");
            int count=mesh.vertexCount/2;var positions=mesh.vertices;
            for(int i=0;i<count;i++)if((positions[i]-positions[i+count]).sqrMagnitude>1e-12f)throw new InvalidOperationException("Backface duplication differs");
            var edges=new Dictionary<ulong,Edge>();var vertices=new HashSet<int>();
            void Add(int a,int b,int opposite)
            {
                ulong key=((ulong)(uint)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
                if(edges.TryGetValue(key,out var edge)){edge.count++;edges[key]=edge;}
                else edges.Add(key,new Edge{a=a,b=b,opposite=opposite,count=1});
            }
            var triangles=mesh.GetTriangles(materialIndex);
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                if(a>=count||b>=count||c>=count)continue;
                vertices.Add(a);vertices.Add(b);vertices.Add(c);Add(a,b,c);Add(b,c,a);Add(c,a,b);
            }
            if(vertices.Count<6||vertices.Count>256)throw new InvalidOperationException("Unsupported overlay topology");
            var lower=new List<Edge>();var stats=new Stats{vertices=vertices.Count};
            foreach(var edge in edges.Values)
            {
                if(edge.count>2)throw new InvalidOperationException("Non-manifold overlay");
                if(edge.count!=1)continue;stats.boundaryEdges++;
                Vector2 a=positions[edge.a],b=positions[edge.b],c=positions[edge.opposite],d=b-a;
                float cross=d.x*(c.y-a.y)-d.y*(c.x-a.x);
                if(Mathf.Abs(cross)<1e-12f)continue;
                float outwardY=cross>0?-d.x:d.x;
                if(outwardY<-.2f*d.magnitude)lower.Add(edge);
            }
            stats.lowerEdges=lower.Count;
            if(lower.Count<4)throw new InvalidOperationException("No continuous lower overlay boundary");
            var uv=new Vector2[mesh.vertexCount];
            foreach(int index in vertices)
            {
                Vector2 p=positions[index];float distance=float.MaxValue;
                foreach(var edge in lower)
                {
                    Vector2 a=positions[edge.a],d=(Vector2)positions[edge.b]-a;
                    float t=Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(1e-12f,d.sqrMagnitude));
                    distance=Mathf.Min(distance,Vector2.Distance(p,a+d*t));
                }
                if(float.IsNaN(distance)||float.IsInfinity(distance))throw new InvalidOperationException("Non-finite boundary distance");
                uv[index]=uv[index+count]=new Vector2(distance,0);stats.maximumDistance=Mathf.Max(stats.maximumDistance,distance);
            }
            if(stats.maximumDistance<1e-6f)throw new InvalidOperationException("Overlay has no interior depth");
            foreach(int index in vertices)uv[index]=uv[index+count]=new Vector2(uv[index].x/stats.maximumDistance,0);
            // Only commit after every topology and range check passed.
            mesh.uv3=uv;state.stats=stats;state.valid=true;PreparationRuns++;
        }
        catch(Exception ex){
#if WKPMX_DEV
            Debug.LogWarning("Rossi eye shadow feather left disabled: "+ex.Message);
#else
            _=ex;
            Debug.LogWarning("Rossi eye-shadow adaptation unavailable for this mesh [R021].");
#endif
        }
        return state.valid;
    }
}
