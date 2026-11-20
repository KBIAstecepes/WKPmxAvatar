using System;using System.IO;using System.Collections.Generic;using Newtonsoft.Json;using UnityEngine;
namespace WKPmxAvatar {
 [Serializable] public sealed class AuthoredAssemblyGrip {
  public int schema,boneCount;public string item,sourceRound,modelSHA256;
  public AssemblyPartIdentity[] parts;public AuthoredAssemblyHand[] hands;
 }
 [Serializable] public sealed class AuthoredAssemblyHand {
  public bool left;public float referencePalmSpan;public string[] bones;
  public float[][] rotations,bindPositions;public float[] itemRootPosition,itemRootRotation,itemRootScale;
 }
 public static class AuthoredAssemblyGrips {
  public const string ModelSHA256="2ddc88f62ea9c801910904aadbb773833c82d0dd0afad6f614c670235de91e46";
  static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
  static void Values(float[] a,int length){if(a==null||a.Length!=length)throw new InvalidDataException("Assembly grip vector length");foreach(float x in a)if(!Finite(x))throw new InvalidDataException("Non-finite assembly grip value");}
  static void Rotation(float[] a){Values(a,4);float norm=0;foreach(float x in a)norm+=x*x;if(Mathf.Abs(norm-1)>.001f)throw new InvalidDataException("Assembly grip rotation");}
  public static AuthoredAssemblyGrip[] Load(string json){if(json==null||json.Length>1024*1024)throw new InvalidDataException("Assembly grip size cap");var items=JsonConvert.DeserializeObject<AuthoredAssemblyGrip[]>(json);if(items==null||items.Length>128)throw new InvalidDataException("Assembly grip count");var names=new HashSet<string>(StringComparer.Ordinal);foreach(var item in items){Validate(item);if(!names.Add(item.item))throw new InvalidDataException("Duplicate assembly grip item");}return items;}
  public static void Validate(AuthoredAssemblyGrip item){
   if(item==null||item.schema!=2||item.boneCount!=498||item.modelSHA256!=ModelSHA256||string.IsNullOrWhiteSpace(item.item)||item.item.Length>128||string.IsNullOrWhiteSpace(item.sourceRound)||item.parts==null||item.parts.Length<1||item.parts.Length>128||item.hands?.Length!=2)throw new InvalidDataException("Assembly grip identity");
   var paths=new HashSet<string>(StringComparer.Ordinal);foreach(var p in item.parts){if(p==null||p.Path==null||p.Path.Length>512||p.Path.StartsWith("/",StringComparison.Ordinal)||p.Path.Contains("..")||p.Path.Contains("\\")||p.Path.Contains("\0")||!paths.Add(p.Path)||string.IsNullOrWhiteSpace(p.Mesh)||p.Mesh.Length>128||p.Vertices<1||p.Vertices>200000||p.Submeshes<1||p.Submeshes>32)throw new InvalidDataException("Assembly part identity");}
   bool[] sides=new bool[2];foreach(var hand in item.hands){if(hand==null||sides[hand.left?1:0]||!Finite(hand.referencePalmSpan)||hand.referencePalmSpan<.02f||hand.referencePalmSpan>.2f||hand.bones?.Length!=15||hand.rotations?.Length!=15||hand.bindPositions?.Length!=15)throw new InvalidDataException("Assembly hand layout");sides[hand.left?1:0]=true;int at=0;foreach(string digit in new[]{"人指","中指","薬指","小指","親指"})foreach(string number in digit=="親指"?new[]{"０","１","２"}:new[]{"１","２","３"}){if(hand.bones[at]!=(hand.left?"左":"右")+digit+number)throw new InvalidDataException("Assembly digit identity");Rotation(hand.rotations[at]);Values(hand.bindPositions[at],3);at++;}
    Values(hand.itemRootPosition,3);Rotation(hand.itemRootRotation);Values(hand.itemRootScale,3);if(new Vector3(hand.itemRootPosition[0],hand.itemRootPosition[1],hand.itemRootPosition[2]).magnitude>.5f)throw new InvalidDataException("Assembly root position");foreach(float value in hand.itemRootScale)if(Mathf.Abs(value)<.02f||Mathf.Abs(value)>2)throw new InvalidDataException("Assembly root scale");
   }
  }
 }
}
