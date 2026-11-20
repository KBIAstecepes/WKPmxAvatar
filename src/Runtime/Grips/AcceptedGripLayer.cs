using System;using System.Collections.Generic;using System.IO;using Newtonsoft.Json;using UnityEngine;
namespace WKPmxAvatar {
 [Serializable] public sealed class AcceptedGripDefinition {
  public int schema;public string item,mesh,sourceRound,modelSHA256;public int boneCount,meshVertices,meshSubmeshes;public AcceptedHandDefinition[] hands;
 }
 [Serializable] public sealed class AcceptedHandDefinition {
  public bool left;public float referencePalmSpan;public string[] bones;public float[][] rotations,bindPositions;public float[] meshPosition,meshRotation,meshScale;
 }
 public struct AcceptedItemPose {public string Item;public Vector3 Position,Scale;public Quaternion Rotation;public float Weight;public string Mesh;public int Vertices,Submeshes;}
 // A fixed authored hand pose, composed after the current animation. No solver, networking or physics.
 public sealed class AcceptedGripLayer {
  sealed class Hand {internal Transform wrist;internal Transform[] joints;internal Quaternion[] target,before,written;internal bool owns,matching;internal float weight,ratio;internal AcceptedHandDefinition definition;}
  readonly Hand left,right;readonly AcceptedGripDefinition definition;
  public const float EnterSeconds=.16f,ExitSeconds=.18f;
  static Vector3 V(float[] v)=>new Vector3(v[0],v[1],v[2]);
  static Quaternion Q(float[] v)=>new Quaternion(v[0],v[1],v[2],v[3]).normalized;
  static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
  static void Array(float[] a,int length){if(a==null||a.Length!=length)throw new InvalidDataException("Grip array length");foreach(float x in a)if(!Finite(x))throw new InvalidDataException("Non-finite grip value");}
  public static AcceptedGripDefinition Load(string json){
   if(json==null||json.Length>64*1024)throw new InvalidDataException("Grip definition limit");var d=JsonConvert.DeserializeObject<AcceptedGripDefinition>(json);
   if(d==null||d.schema!=1||d.item!="Item_Floppy_T1"||d.mesh!="Item_Floppy_T1_01"||d.sourceRound!="Run08"||d.boneCount!=498||d.meshVertices!=48||d.meshSubmeshes!=2||d.hands?.Length!=2)throw new InvalidDataException("Unsupported accepted grip");
   bool[] sides=new bool[2];foreach(var h in d.hands){if(h==null||sides[h.left?1:0])throw new InvalidDataException("Duplicate grip side");sides[h.left?1:0]=true;
    if(!Finite(h.referencePalmSpan)||h.referencePalmSpan<.02f||h.referencePalmSpan>.2f||h.bones?.Length!=15||h.rotations?.Length!=15||h.bindPositions?.Length!=15)throw new InvalidDataException("Grip hand layout");var names=new HashSet<string>();
    string prefix=h.left?"左":"右";for(int i=0;i<15;i++){if(h.bones[i]==null||!h.bones[i].StartsWith(prefix,StringComparison.Ordinal)||!names.Add(h.bones[i]))throw new InvalidDataException("Grip joint names");Array(h.rotations[i],4);Array(h.bindPositions[i],3);float norm=0;foreach(float x in h.rotations[i])norm+=x*x;if(Mathf.Abs(norm-1)>.001f)throw new InvalidDataException("Grip quaternion");}
    Array(h.meshPosition,3);Array(h.meshRotation,4);Array(h.meshScale,3);if(V(h.meshPosition).magnitude>.5f||V(h.meshScale).magnitude>3)throw new InvalidDataException("Grip mesh transform");}
   return d;
  }
  public AcceptedGripLayer(Transform[] bones,AcceptedGripDefinition d){
   if(bones==null||bones.Length!=d.boneCount)throw new InvalidDataException("Accepted grip requires matching Rossi rig");definition=d;
   Hand Bind(AcceptedHandDefinition hd){string prefix=hd.left?"左":"右";Transform Find(string name){var b=System.Array.Find(bones,t=>t!=null&&t.name==name);return b??throw new InvalidDataException("Grip joint missing: "+name);}
    var h=new Hand{definition=hd,wrist=Find(prefix+"手首"),joints=new Transform[15],target=new Quaternion[15],before=new Quaternion[15],written=new Quaternion[15]};
    h.ratio=h.wrist.InverseTransformPoint(Find(prefix+"中指１").position).magnitude/hd.referencePalmSpan;
    if(!Finite(h.ratio)||h.ratio<.25f||h.ratio>4)throw new InvalidDataException("Grip model size mismatch");
    for(int i=0;i<15;i++){h.joints[i]=Find(hd.bones[i]);if(!h.joints[i].IsChildOf(h.wrist)||Vector3.Distance(h.joints[i].localPosition,V(hd.bindPositions[i])*h.ratio)>.0005f*h.ratio)throw new InvalidDataException("Grip bind layout mismatch: "+hd.bones[i]);h.target[i]=Q(hd.rotations[i]);}return h;
   }
   foreach(var hd in d.hands)if(hd.left)left=Bind(hd);else right=Bind(hd);if(left==null||right==null)throw new InvalidDataException("Missing grip side");
  }
  public bool Matches(string name,bool active)=>active&&string.Equals(name,definition.item,StringComparison.Ordinal);
  public float Weight(bool isLeft)=>(isLeft?left:right).weight;
  static void Restore(Hand h){if(!h.owns)return;for(int i=0;i<15;i++)if(h.joints[i]!=null&&Mathf.Abs(Quaternion.Dot(h.joints[i].localRotation,h.written[i]))>1-1e-6f)h.joints[i].localRotation=h.before[i];h.owns=false;}
  public void Restore(){Restore(left);Restore(right);}
  public void Reset(){Restore();left.weight=right.weight=0;left.matching=right.matching=false;}
  public void Tick(float dt,bool leftHeld,bool rightHeld){if(!Finite(dt)||dt<0||dt>.25f){Reset();return;}Tick(left,dt,leftHeld);Tick(right,dt,rightHeld);}
  static void Tick(Hand h,float dt,bool held){Restore(h);h.matching=held;h.weight=Mathf.MoveTowards(h.weight,held?1:0,dt/(held?EnterSeconds:ExitSeconds));float w=Mathf.SmoothStep(0,1,h.weight);if(w<=0)return;
   for(int i=0;i<15;i++){h.before[i]=h.joints[i].localRotation;h.joints[i].localRotation=Quaternion.Slerp(h.before[i],h.target[i],w);h.written[i]=h.joints[i].localRotation;}h.owns=true;
  }
  public AcceptedItemPose? ItemPose(bool isLeft){var h=isLeft?left:right;if(!h.matching||h.weight<=0)return null;var d=h.definition;
   return new AcceptedItemPose{Item=definition.item,Position=h.wrist.TransformPoint(V(d.meshPosition)*h.ratio),Rotation=h.wrist.rotation*Q(d.meshRotation),Scale=Vector3.Scale(h.wrist.lossyScale,V(d.meshScale)*h.ratio),Weight=Mathf.SmoothStep(0,1,h.weight),Mesh=definition.mesh,Vertices=definition.meshVertices,Submeshes=definition.meshSubmeshes};
  }
 }
}
