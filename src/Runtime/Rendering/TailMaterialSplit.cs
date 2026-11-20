using System;using System.Collections.Generic;using UnityEngine;using Object=UnityEngine.Object;
namespace WKPmxAvatar
{
    public static class TailMaterialSplit
    {
        public static int Apply(PmxData data,Mesh mesh,ref Material[] materials,ref Texture2D[] maps,ref Color[] tints,ref bool[] alpha,List<Object> owned)
        {
            var isTail=new bool[data.Bones.Length];for(int b=0;b<isTail.Length;b++)isTail[b]=data.Bones[b].Name.IndexOf("tail",StringComparison.OrdinalIgnoreCase)>=0||data.Bones[b].Name.Contains("尾");
            var tailVertex=new bool[data.Vertices.Length];for(int v=0;v<tailVertex.Length;v++){var vert=data.Vertices[v];float w=0;for(int j=0;j<4;j++)if(vert.Bones[j]>=0&&vert.Bones[j]<isTail.Length&&isTail[vert.Bones[j]])w+=vert.Weights[j];tailVertex[v]=w>0.2f;}
            int moved=0;for(int m=0;m<data.Materials.Length;m++){
                var keep=new List<int>();var tail=new List<int>();var triangles=mesh.GetTriangles(m);for(int i=0;i<triangles.Length;i+=3){bool hit=tailVertex[triangles[i]%tailVertex.Length]||tailVertex[triangles[i+1]%tailVertex.Length]||tailVertex[triangles[i+2]%tailVertex.Length];var list=hit?tail:keep;list.Add(triangles[i]);list.Add(triangles[i+1]);list.Add(triangles[i+2]);}
                if(tail.Count==0)continue;int index=materials.Length;Array.Resize(ref materials,index+1);Array.Resize(ref maps,index+1);Array.Resize(ref tints,index+1);Array.Resize(ref alpha,index+1);
                var material=new Material(materials[m]){name="Tail_"+materials[m].name};owned.Add(material);materials[index]=material;maps[index]=maps[m];tints[index]=tints[m];alpha[index]=alpha[m];mesh.subMeshCount=index+1;mesh.SetTriangles(keep,m);mesh.SetTriangles(tail,index);moved+=tail.Count/3;
            }
            return moved;
        }
    }
}
