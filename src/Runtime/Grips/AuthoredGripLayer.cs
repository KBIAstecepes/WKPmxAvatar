using System;using System.IO;using System.Collections.Generic;using Newtonsoft.Json;using UnityEngine;
namespace WKPmxAvatar {
 // Fixed hand poses only. The existing animation supplies arm/wrist/body motion.
 public sealed class AuthoredGripLayer {
  enum State {Idle,Entering,Holding,Releasing}
  sealed class Pose {internal AcceptedGripDefinition definition;internal AcceptedHandDefinition hand;internal AuthoredAssemblyGrip assembly;internal AuthoredAssemblyHand assemblyHand;internal Quaternion[] target;internal float ratio;}
  sealed class Hand {internal Transform wrist,elbow;internal Vector3 restForward;internal Quaternion wristBefore,wristWritten;internal bool ownsWrist;internal float wristBlend;internal Transform[] joints;internal Quaternion[] before,written,from;internal readonly Dictionary<string,Pose> poses=new Dictionary<string,Pose>(StringComparer.Ordinal);internal Pose requested;internal State state;internal float phase;internal bool owns,hasWritten;}
  readonly Hand left,right;public const float EnterSeconds=.16f,ExitSeconds=.18f;
  static Vector3 V(float[] a)=>new Vector3(a[0],a[1],a[2]);static Quaternion Q(float[] a)=>new Quaternion(a[0],a[1],a[2],a[3]).normalized;
  static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
  static void Array(float[] a,int n){if(a==null||a.Length!=n)throw new InvalidDataException("Grip array size");foreach(float x in a)if(!Finite(x))throw new InvalidDataException("Non-finite grip data");}
  public static AcceptedGripDefinition[] Load(string json){
   if(json==null||json.Length>128*1024)throw new InvalidDataException("Authored grip size cap");var ds=JsonConvert.DeserializeObject<AcceptedGripDefinition[]>(json);if(ds?.Length!=3)throw new InvalidDataException("Expected three authored items");var items=new HashSet<string>();
   foreach(var d in ds){string mesh;int count;if(d?.item=="Item_Floppy_T1"){mesh="Item_Floppy_T1_01";count=48;if(d.sourceRound!="Run08")throw new InvalidDataException("Approved disk baseline changed");}else if(d?.item=="Item_Handgun"){mesh="Item_handgun";count=1002;}else if(d?.item=="Item_Injector"){mesh="Lowrez_injector";count=772;}else throw new InvalidDataException("Unsupported authored item");
    if(!items.Add(d.item)||d.schema!=1||d.mesh!=mesh||d.meshVertices!=count||d.meshSubmeshes!=2||d.boneCount!=498||d.hands?.Length!=2)throw new InvalidDataException("Authored item identity");bool[] sides=new bool[2];foreach(var h in d.hands){if(h==null||sides[h.left?1:0])throw new InvalidDataException("Authored hand sides");sides[h.left?1:0]=true;
     if(!Finite(h.referencePalmSpan)||h.referencePalmSpan<.02f||h.referencePalmSpan>.2f||h.bones?.Length!=15||h.rotations?.Length!=15||h.bindPositions?.Length!=15)throw new InvalidDataException("Authored hand layout");
     int at=0;foreach(string digit in new[]{"人指","中指","薬指","小指","親指"})foreach(string number in digit=="親指"?new[]{"０","１","２"}:new[]{"１","２","３"}){if(h.bones[at]!=(h.left?"左":"右")+digit+number)throw new InvalidDataException("Authored finger identity");Array(h.rotations[at],4);Array(h.bindPositions[at],3);float norm=0;foreach(float x in h.rotations[at])norm+=x*x;if(Mathf.Abs(norm-1)>.001f)throw new InvalidDataException("Authored quaternion");at++;}
     Array(h.meshPosition,3);Array(h.meshRotation,4);Array(h.meshScale,3);float qn=0;foreach(float x in h.meshRotation)qn+=x*x;if(Mathf.Abs(qn-1)>.001f||V(h.meshPosition).magnitude>.5f)throw new InvalidDataException("Authored mesh transform");foreach(float s in h.meshScale)if(Mathf.Abs(s)<.02f||Mathf.Abs(s)>2)throw new InvalidDataException("Authored scale");
    }
   }return ds;
  }
  public AuthoredGripLayer(Transform[] bones,AcceptedGripDefinition[] definitions):this(bones,definitions,null){}
  public AuthoredGripLayer(Transform[] bones,AcceptedGripDefinition[] definitions,AuthoredAssemblyGrip[] assemblies){if(definitions==null)throw new InvalidDataException("Legacy grip definitions missing");if(assemblies!=null)foreach(var d in assemblies)AuthoredAssemblyGrips.Validate(d);if(bones?.Length!=498)throw new InvalidDataException("Authored grips require matching Rossi rig");
   Hand Bind(bool isLeft){string prefix=isLeft?"左":"右";Transform Find(string name){var b=System.Array.Find(bones,t=>t!=null&&t.name==name);return b??throw new InvalidDataException("Grip joint missing");}
    var h=new Hand{wrist=Find(prefix+"手首"),elbow=Find(prefix+"ひじ"),joints=new Transform[15],before=new Quaternion[15],written=new Quaternion[15],from=new Quaternion[15]};h.restForward=h.wrist.InverseTransformDirection(Find(prefix+"中指１").position-h.wrist.position).normalized;bool first=true;
    void Add(string name,Pose p,float span,string[] names,float[][] bind,float[][] rotations){float ratio=h.wrist.InverseTransformPoint(Find(prefix+"中指１").position).magnitude/span;if(!Finite(ratio)||ratio<.25f||ratio>4)throw new InvalidDataException("Authored rig scale");p.ratio=ratio;p.target=new Quaternion[15];for(int i=0;i<15;i++){var b=Find(names[i]);if(!b.IsChildOf(h.wrist)||Vector3.Distance(b.localPosition,V(bind[i])*ratio)>.0005f*ratio)throw new InvalidDataException("Authored bind mismatch");if(first)h.joints[i]=b;else if(h.joints[i]!=b)throw new InvalidDataException("Inconsistent authored joint order");p.target[i]=Q(rotations[i]);}h.poses.Add(name,p);first=false;}
    foreach(var d in definitions){var hd=System.Array.Find(d.hands,x=>x.left==isLeft);Add(d.item,new Pose{definition=d,hand=hd},hd.referencePalmSpan,hd.bones,hd.bindPositions,hd.rotations);}
    if(assemblies!=null)foreach(var d in assemblies){var hd=System.Array.Find(d.hands,x=>x.left==isLeft);Add(d.item,new Pose{assembly=d,assemblyHand=hd},hd.referencePalmSpan,hd.bones,hd.bindPositions,hd.rotations);}
    return h;
   }left=Bind(true);right=Bind(false);
  }
  public bool Matches(string name,bool active)=>active&&name!=null&&left.poses.ContainsKey(name);
  static void Restore(Hand h){if(h.ownsWrist){if(h.wrist!=null&&Mathf.Abs(Quaternion.Dot(h.wrist.localRotation,h.wristWritten))>1-1e-6f)h.wrist.localRotation=h.wristBefore;h.ownsWrist=false;}if(!h.owns)return;for(int i=0;i<15;i++)if(h.joints[i]!=null&&Mathf.Abs(Quaternion.Dot(h.joints[i].localRotation,h.written[i]))>1-1e-6f)h.joints[i].localRotation=h.before[i];h.owns=false;}
  public void Restore(){Restore(left);Restore(right);}
  public void Reset(){Restore();foreach(var h in new[]{left,right}){h.state=State.Idle;h.phase=0;h.wristBlend=0;h.requested=null;h.hasWritten=false;}}
  public float Weight(bool isLeft){var h=isLeft?left:right;return h.state==State.Holding?1:h.state==State.Entering?Mathf.SmoothStep(0,1,h.phase):h.state==State.Releasing?1-Mathf.SmoothStep(0,1,h.phase):0;}
  public void Tick(float dt,string leftItem,string rightItem){if(!Finite(dt)||dt<0||dt>.25f){Reset();return;}if(dt==0)return;Tick(left,dt,leftItem);Tick(right,dt,rightItem);}
  static void Tick(Hand h,float dt,string name){Restore(h);Pose next=null;if(name!=null)h.poses.TryGetValue(name,out next);
   if(next!=h.requested){for(int i=0;i<15;i++)h.from[i]=h.hasWritten?h.written[i]:h.joints[i].localRotation;h.requested=next;h.phase=0;h.state=next==null?State.Releasing:State.Entering;}
   h.wristBlend=Mathf.MoveTowards(h.wristBlend,next!=null?1:0,dt/(next!=null?EnterSeconds:ExitSeconds));
   if(h.wristBlend>0){Vector3 forearm=(h.wrist.position-h.elbow.position).normalized,forward=h.wrist.TransformDirection(h.restForward);if(forearm.sqrMagnitude>.5f&&Vector3.Angle(forearm,forward)>20){Vector3 limited=Vector3.RotateTowards(forearm,forward,20*Mathf.Deg2Rad,0);h.wristBefore=h.wrist.localRotation;var target=Quaternion.FromToRotation(forward,limited)*h.wrist.rotation;h.wrist.rotation=Quaternion.Slerp(h.wrist.rotation,target,Mathf.SmoothStep(0,1,h.wristBlend));h.wristWritten=h.wrist.localRotation;h.ownsWrist=true;}}
   if(h.state==State.Idle){for(int i=0;i<15;i++)h.written[i]=h.joints[i].localRotation;h.hasWritten=true;return;}
   if(h.state==State.Entering){h.phase=Mathf.Min(1,h.phase+dt/EnterSeconds);if(h.phase>=1)h.state=State.Holding;}
   else if(h.state==State.Releasing){h.phase=Mathf.Min(1,h.phase+dt/ExitSeconds);if(h.phase>=1){h.state=State.Idle;for(int i=0;i<15;i++)h.written[i]=h.joints[i].localRotation;h.hasWritten=true;return;}}
   float w=Mathf.SmoothStep(0,1,h.phase);for(int i=0;i<15;i++){h.before[i]=h.joints[i].localRotation;h.joints[i].localRotation=h.state==State.Holding?h.requested.target[i]:h.state==State.Entering?Quaternion.Slerp(h.from[i],h.requested.target[i],w):Quaternion.Slerp(h.from[i],h.before[i],w);h.written[i]=h.joints[i].localRotation;}h.owns=h.hasWritten=true;
  }
  public AcceptedItemPose? ItemPose(bool isLeft){var h=isLeft?left:right;if(h.requested==null||h.requested.assembly!=null||h.state==State.Idle||h.state==State.Releasing)return null;var p=h.requested;var d=p.hand;return new AcceptedItemPose{Item=p.definition.item,Position=h.wrist.TransformPoint(V(d.meshPosition)*p.ratio),Rotation=h.wrist.rotation*Q(d.meshRotation),Scale=Vector3.Scale(h.wrist.lossyScale,V(d.meshScale)*p.ratio),Weight=Weight(isLeft),Mesh=p.definition.mesh,Vertices=p.definition.meshVertices,Submeshes=p.definition.meshSubmeshes};}
  public AssemblyGripPose? AssemblyPose(bool isLeft){var h=isLeft?left:right;if(h.requested?.assembly==null||h.state==State.Idle||h.state==State.Releasing)return null;var p=h.requested;var d=p.assemblyHand;return new AssemblyGripPose{Item=p.assembly.item,Position=h.wrist.TransformPoint(V(d.itemRootPosition)*p.ratio),Rotation=h.wrist.rotation*Q(d.itemRootRotation),Scale=Vector3.Scale(h.wrist.lossyScale,V(d.itemRootScale)*p.ratio),Weight=Weight(isLeft),Parts=p.assembly.parts};}

 }
}
