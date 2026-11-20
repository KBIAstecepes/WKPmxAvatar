using System.IO;
using UnityEngine;
public static partial class EyeFaceUvTransfer
{
 public static Stats Apply(Mesh mesh,int iris,int sclera,int originalCount,string reportFile)
 {
  var result=Apply(mesh,iris,sclera,originalCount);
  if(reportFile!=null)File.WriteAllText(reportFile,JsonUtility.ToJson(result,true));
  return result;
 }
}
