using System;using System.Collections.Generic;using System.Reflection;using UnityEngine;using Object=UnityEngine.Object;
namespace WKPmxAvatar
{
    // Only duplicate render geometry. Never move/reparent a synchronized item or its collider.
    public sealed class VisualHeldItem:IDisposable
    {
        sealed class Part {public Renderer Source;public MeshRenderer View;public bool WasOff;public Mesh Baked,Geometry;public MeshFilter Filter;public string Path;public readonly List<Material> Materials=new List<Material>();}
        sealed class AttachedSprite {public SpriteRenderer Source,View;public bool WasOff;}
        readonly List<AttachedSprite> attachedSprites=new List<AttachedSprite>();
        sealed class Node {public Transform Source,Copy,Parent;}
        readonly Dictionary<Transform,Transform> nodeCopies=new Dictionary<Transform,Transform>();readonly List<Node> nodes=new List<Node>();
        readonly Dictionary<Transform,Transform> sourceParents=new Dictionary<Transform,Transform>();readonly List<Renderer> sourceRenderers=new List<Renderer>();
        public bool AssemblyActive {get;private set;}
        MaterialPropertyBlock block;
        readonly List<Part> parts=new List<Part>();bool spritesInspected;GameObject source,visual;Bounds bounds,anchorBounds;Transform anchorTransform;float anchorVolume;
        GameObject Item(Component hand)=>hand?.GetType().GetField("_item",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(hand) as GameObject;
        public string ActiveItemName(Component hand){var item=Item(hand);return item!=null&&item.activeInHierarchy?ItemGripProfiles.Name(hand,item)?.Replace("(Clone)","").Trim():null;}
        public bool ItemActive(Component hand){var item=Item(hand);return item!=null&&item.activeInHierarchy;}
        public ItemGripProfile Profile(Component hand){var item=Item(hand);return item!=null&&item.activeInHierarchy?ItemGripProfiles.Find(ItemGripProfiles.Name(hand,item)):null;}
        public void Sync(Component hand,Transform networkHand,Vector3 palm,Quaternion? palmFrame=null)=>Sync(hand,networkHand,palm,palmFrame,null);
        public void Sync(Component hand,Transform networkHand,Vector3 palm,Quaternion? palmFrame,AcceptedItemPose? acceptedPose)
            =>Sync(hand,networkHand,palm,palmFrame,acceptedPose,null);
        public void Sync(Component hand,Transform networkHand,Vector3 palm,Quaternion? palmFrame,AcceptedItemPose? acceptedPose,AssemblyGripPose? assemblyPose)
        {
            var item=Item(hand);if(item==null||!item.activeInHierarchy||networkHand==null){Dispose();return;}
            if(item!=source){Dispose();source=item;visual=new GameObject("WKPmx_HeldVisualOnly");bool haveBounds=false;
                foreach(var r in item.GetComponentsInChildren<Renderer>(true)){
                    Mesh mesh=null;Mesh baked=null;if(r is SkinnedMeshRenderer sk){baked=new Mesh();sk.BakeMesh(baked);mesh=baked;}else if(r is MeshRenderer)mesh=r.GetComponent<MeshFilter>()?.sharedMesh;
                    if(mesh==null)continue;for(var t=r.transform;t!=null&&t!=item.transform;t=t.parent)if(!sourceParents.ContainsKey(t))sourceParents.Add(t,t.parent);var b=mesh.bounds;
                    float volume=b.size.x*b.size.y*b.size.z*Mathf.Abs(r.transform.lossyScale.x*r.transform.lossyScale.y*r.transform.lossyScale.z);
                    if(anchorTransform==null||volume>anchorVolume){anchorTransform=r.transform;anchorBounds=b;anchorVolume=volume;}
                    for(int c=0;c<8;c++){var v=b.center+Vector3.Scale(b.extents,new Vector3((c&1)==0?-1:1,(c&2)==0?-1:1,(c&4)==0?-1:1));v=item.transform.InverseTransformPoint(r.transform.TransformPoint(v));if(!haveBounds){bounds=new Bounds(v,Vector3.zero);haveBounds=true;}else bounds.Encapsulate(v);}
                    var go=new GameObject("HeldPart");go.layer=r.gameObject.layer;go.transform.SetParent(visual.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var view=go.AddComponent<MeshRenderer>();view.sharedMaterials=r.sharedMaterials;view.shadowCastingMode=r.shadowCastingMode;view.receiveShadows=r.receiveShadows;view.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;parts.Add(new Part{Source=r,View=view,WasOff=r.forceRenderingOff,Baked=baked,Geometry=r is SkinnedMeshRenderer original?original.sharedMesh:mesh,Filter=r.GetComponent<MeshFilter>(),Path=RelativePath(r.transform,item.transform)});
                }
            }
            if(!spritesInspected&&source!=null){spritesInspected=true;string itemName=ItemGripProfiles.Name(hand,item);foreach(var sprite in item.GetComponentsInChildren<SpriteRenderer>(true)){string path=RelativePath(sprite.transform,item.transform);if(!((itemName=="Item_Artifact_CraftCube"||itemName=="Item_Artifact_CraftCube_Dev")&&path=="Items_CraftCube/FX_RunningLight_Red"||itemName=="Item_Trinket_CalmingBuddy"&&path=="FX_RunningLight_Red"))continue;var go=new GameObject("HeldAttachedLight");go.layer=sprite.gameObject.layer;go.transform.SetParent(visual.transform,false);attachedSprites.Add(new AttachedSprite{Source=sprite,View=go.AddComponent<SpriteRenderer>(),WasOff=sprite.forceRenderingOff});for(var t=sprite.transform;t!=null&&t!=item.transform;t=t.parent)if(!sourceParents.ContainsKey(t))sourceParents.Add(t,t.parent);}}
            var profile=Profile(hand);Vector3 shift=palm-networkHand.position;float size=1;Quaternion rotate=Quaternion.identity;Vector3 grip=networkHand.position;
            if(profile!=null&&parts.Count>0){
                int longest=bounds.size.y>bounds.size.x?1:0;if(bounds.size.z>bounds.size[longest])longest=2;int thin=bounds.size.x<bounds.size.y?0:1;if(bounds.size.z<bounds.size[thin])thin=2;if(thin==longest)thin=(longest+1)%3;
                Vector3 axis(int i)=>i==0?Vector3.right:i==1?Vector3.up:Vector3.forward;Vector3 anchor=bounds.center-axis(longest)*(bounds.size[longest]*profile.edge);
                var scaled=Vector3.Scale(bounds.size,item.transform.lossyScale);float max=Mathf.Max(Mathf.Abs(scaled.x),Mathf.Max(Mathf.Abs(scaled.y),Mathf.Abs(scaled.z)));size=Mathf.Clamp(profile.maxDimension/Mathf.Max(0.0001f,max),0.05f,1.2f);grip=item.transform.TransformPoint(anchor);
                if(profile.meshAnchor!=null&&anchorTransform!=null){var a=profile.meshAnchor;grip=anchorTransform.TransformPoint(anchorBounds.center+Vector3.Scale(anchorBounds.size,new Vector3(a[0],a[1],a[2])));}
                if(profile.shape=="pinch"&&palmFrame.HasValue){var desired=palmFrame.Value*Quaternion.Inverse(Quaternion.LookRotation(axis(thin),axis(longest)));rotate=desired*Quaternion.Inverse(item.transform.rotation);}
                else if(profile.shape=="syringe"&&palmFrame.HasValue){var desired=palmFrame.Value*Quaternion.Inverse(Quaternion.LookRotation(axis(thin),axis(longest)));rotate=desired*Quaternion.Inverse(item.transform.rotation);}
            }
            Vector3 genericPosition=profile==null?item.transform.position+shift:palm+rotate*((item.transform.position-grip)*size);
            bool assembly=assemblyPose.HasValue&&!acceptedPose.HasValue&&TryAssembly(assemblyPose.Value,ItemGripProfiles.Name(hand,item),genericPosition,rotate,size);
            if(!assembly&&AssemblyActive){foreach(var p in parts)if(p.View!=null)p.View.transform.SetParent(visual.transform,false);visual.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);visual.transform.localScale=Vector3.one;AssemblyActive=false;}
            if(block==null)block=new MaterialPropertyBlock();
            foreach(var sprite in attachedSprites){if(sprite.Source==null||sprite.View==null)continue;if(!assembly){sprite.View.enabled=false;sprite.Source.forceRenderingOff=sprite.WasOff;continue;}var node=CopyNode(sprite.Source.transform);node.localPosition=sprite.Source.transform.localPosition;node.localRotation=sprite.Source.transform.localRotation;node.localScale=sprite.Source.transform.localScale;if(sprite.View.transform.parent!=node)sprite.View.transform.SetParent(node,false);sprite.View.transform.localPosition=Vector3.zero;sprite.View.transform.localRotation=Quaternion.identity;sprite.View.transform.localScale=Vector3.one;sprite.View.sprite=sprite.Source.sprite;sprite.View.sharedMaterial=sprite.Source.sharedMaterial;sprite.View.color=sprite.Source.color;sprite.View.flipX=sprite.Source.flipX;sprite.View.flipY=sprite.Source.flipY;sprite.View.drawMode=sprite.Source.drawMode;sprite.View.size=sprite.Source.size;sprite.View.maskInteraction=sprite.Source.maskInteraction;sprite.View.sortingLayerID=sprite.Source.sortingLayerID;sprite.View.sortingOrder=sprite.Source.sortingOrder;sprite.View.enabled=sprite.Source.enabled&&sprite.Source.gameObject.activeInHierarchy;block.Clear();sprite.Source.GetPropertyBlock(block);sprite.View.SetPropertyBlock(block);sprite.Source.forceRenderingOff=true;}
            foreach(var p in parts){if(p.Source==null||p.View==null)continue;if(p.Baked!=null&&(p.Source is SkinnedMeshRenderer sk))sk.BakeMesh(p.Baked);
                p.Source.GetSharedMaterials(p.Materials);p.View.SetSharedMaterials(p.Materials);
                p.View.enabled=p.Source.enabled&&p.Source.gameObject.activeInHierarchy;if(!assembly){p.View.transform.SetPositionAndRotation(profile==null?p.Source.transform.position+shift:palm+rotate*((p.Source.transform.position-grip)*size),rotate*p.Source.transform.rotation);p.View.transform.localScale=p.Source.transform.lossyScale*size;}
                if(acceptedPose.HasValue){var a=acceptedPose.Value;if(profile?.item==a.Item&&p.Geometry!=null&&p.Geometry.name==a.Mesh&&p.Geometry.vertexCount==a.Vertices&&p.Geometry.subMeshCount==a.Submeshes){float w=Mathf.Clamp01(a.Weight);p.View.transform.position=Vector3.Lerp(p.View.transform.position,a.Position,w);p.View.transform.rotation=Quaternion.Slerp(p.View.transform.rotation,a.Rotation,w);p.View.transform.localScale=Vector3.Lerp(p.View.transform.localScale,a.Scale,w);}}
                for(int m=0;m<p.Materials.Count;m++){block.Clear();p.Source.GetPropertyBlock(block,m);p.View.SetPropertyBlock(block,m);}p.Source.forceRenderingOff=true;
            }
        }
        static string RelativePath(Transform t,Transform root){if(t==root)return "";var name=t.name;for(t=t.parent;t!=null&&t!=root;t=t.parent)name=t.name+"/"+name;return t==root?name:null;}
        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
        bool TryAssembly(AssemblyGripPose a,string itemName,Vector3 genericPosition,Quaternion deltaRotation,float genericSize){
            if(a.Item!=itemName||a.Parts==null||a.Parts.Length!=parts.Count||parts.Count==0||parts.Count>128||!Finite(a.Weight)||a.Weight<0||a.Weight>1||!Finite(a.Position)||!Finite(a.Scale))return false;
            var q=a.Rotation;float norm=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;if(!Finite(norm)||Mathf.Abs(norm-1)>.001f)return false;
            // X- and Y-mirrored roots occur in the upstream mod. Express the
            // native root in the target's signed-scale convention without a
            // zero-scale interpolation. Preserve reflection parity, not axis.
            var matrix=source.transform.localToWorldMatrix;Vector3 x=deltaRotation*(Vector3)matrix.GetColumn(0)*genericSize,y=deltaRotation*(Vector3)matrix.GetColumn(1)*genericSize,z=deltaRotation*(Vector3)matrix.GetColumn(2)*genericSize;
            Vector3 genericScale=new Vector3(x.magnitude*Mathf.Sign(a.Scale.x),y.magnitude*Mathf.Sign(a.Scale.y),z.magnitude*Mathf.Sign(a.Scale.z));
            for(int axis=0;axis<3;axis++)if(!Finite(genericScale[axis])||Mathf.Abs(genericScale[axis])<.00001f||Mathf.Abs(a.Scale[axis])<.00001f||Mathf.Abs(a.Scale[axis])>10)return false;
            x/=genericScale.x;y/=genericScale.y;z/=genericScale.z;
            if(Mathf.Abs(Vector3.Dot(x,y))>.001f||Mathf.Abs(Vector3.Dot(x,z))>.001f||Mathf.Abs(Vector3.Dot(y,z))>.001f||Vector3.Dot(Vector3.Cross(x,y),z)<.999f)return false;
            Quaternion genericRotation=Quaternion.LookRotation(z,y);
            foreach(var n in sourceParents)if(n.Key==null||n.Key.parent!=n.Value)return false;
            source.GetComponentsInChildren(true,sourceRenderers);int rendererCount=0;foreach(var r in sourceRenderers){if(r is SkinnedMeshRenderer nativeSkin){if(nativeSkin.sharedMesh==null)return false;}else if(r is MeshRenderer){var filter=r.GetComponent<MeshFilter>();if(filter==null||filter.sharedMesh==null)continue;}else continue;bool known=false;foreach(var p in parts)if(p.Source==r){known=true;break;}if(!known)return false;rendererCount++;}if(rendererCount!=parts.Count)return false;
            foreach(var p in parts){if(p.Source==null||p.View==null||(p.Source is SkinnedMeshRenderer nativeSkin?(p.Baked==null||nativeSkin.sharedMesh!=p.Geometry):(p.Filter==null||p.Filter.sharedMesh!=p.Geometry))||p.Path==null)return false;int match=0;foreach(var id in a.Parts)if(id!=null&&id.Path==p.Path&&id.Mesh==p.Geometry.name&&id.Vertices==p.Geometry.vertexCount&&id.Submeshes==p.Geometry.subMeshCount)match++;if(match!=1)return false;}
            if(nodeCopies.Count==0){nodeCopies.Add(source.transform,visual.transform);foreach(var p in parts)CopyNode(p.Source.transform);}
            if(!AssemblyActive){foreach(var p in parts){p.View.transform.SetParent(nodeCopies[p.Source.transform],false);p.View.transform.localPosition=Vector3.zero;p.View.transform.localRotation=Quaternion.identity;p.View.transform.localScale=Vector3.one;}AssemblyActive=true;}
            foreach(var n in nodes){n.Copy.localPosition=n.Source.localPosition;n.Copy.localRotation=n.Source.localRotation;n.Copy.localScale=n.Source.localScale;}
            visual.transform.SetPositionAndRotation(Vector3.Lerp(genericPosition,a.Position,a.Weight),Quaternion.Slerp(genericRotation,a.Rotation,a.Weight));visual.transform.localScale=Vector3.Lerp(genericScale,a.Scale,a.Weight);return true;
        }
        Transform CopyNode(Transform t){if(nodeCopies.TryGetValue(t,out var found))return found;if(t==null||!t.IsChildOf(source.transform))throw new InvalidOperationException("Invalid assembly hierarchy");var parent=CopyNode(t.parent);var copy=new GameObject("HeldNode_"+t.name).transform;copy.SetParent(parent,false);nodeCopies.Add(t,copy);nodes.Add(new Node{Source=t,Copy=copy,Parent=t.parent});return copy;}
        public void Dispose(){foreach(var s in attachedSprites)if(s.Source!=null)s.Source.forceRenderingOff=s.WasOff;attachedSprites.Clear();spritesInspected=false;foreach(var p in parts){if(p.Source!=null)p.Source.forceRenderingOff=p.WasOff;if(p.Baked!=null)Kill(p.Baked);}parts.Clear();nodes.Clear();nodeCopies.Clear();sourceParents.Clear();sourceRenderers.Clear();AssemblyActive=false;if(visual!=null)Kill(visual);source=visual=null;anchorTransform=null;anchorVolume=0;}
        static void Kill(Object o){if(Application.isPlaying)Object.Destroy(o);else Object.DestroyImmediate(o);}
    }
}
