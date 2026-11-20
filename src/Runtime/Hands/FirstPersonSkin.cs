using System;using System.Collections;using System.Collections.Generic;using System.Linq;using System.Reflection;using UnityEngine;using UnityEngine.UI;
namespace WKPmxAvatar
{
    [DefaultExecutionOrder(12000)]
    public sealed class FirstPersonSkin:MonoBehaviour
    {
        public static FirstPersonSkin Current;public Func<bool> HandsEnabled,PawEnabled;public Func<float> HandScale,HandBrightness;public Action<string> Warn;public string Folder;
        FirstPersonAssets assets;HeldHandSkin heldSkin;bool heldFailed;Type playerType;object player;readonly HashSet<object> hands=new HashSet<object>();readonly Dictionary<Sprite,bool> cursorMap=new Dictionary<Sprite,bool>();
        readonly Dictionary<Image,Sprite> cursorOriginals=new Dictionary<Image,Sprite>();
        readonly HashSet<SpriteRenderer> genericRenderers=new HashSet<SpriteRenderer>();readonly HashSet<SpriteRenderer> seenGeneric=new HashSet<SpriteRenderer>();readonly List<SpriteRenderer> staleGeneric=new List<SpriteRenderer>();bool failed,handsWereEnabled;
        sealed class PoseRequest{public string Pose;public Sprite Source;}
        readonly Dictionary<object,PoseRequest> poseRequests=new Dictionary<object,PoseRequest>();
        public string Status=>failed?"First person: resource load failed; original visuals retained":assets==null?"First person: not loaded":"First person: 5 poses; "+(heldFailed?"held resource error":heldSkin?.Status??"held not loaded");
        void Awake(){Current=this;}
        bool Enabled(Func<bool> flag)=>flag!=null&&flag();
        bool Ensure()
        {
            if(assets!=null)return true;if(failed||string.IsNullOrEmpty(Folder))return false;
            try{assets=new FirstPersonAssets(Folder);return true;}catch(Exception e){failed=true;Warn?.Invoke("First person skin unavailable: "+e.Message);return false;}
        }
        void Refresh()
        {
            if(playerType==null)playerType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("ENT_Player")).FirstOrDefault(t=>t!=null);
            object current=playerType?.GetField("playerObject",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(null);
            if(current is UnityEngine.Object u&&u==null)current=null;
            if(ReferenceEquals(player,current)&&hands.Count>0&&cursorMap.Count>0)return;
            if(!ReferenceEquals(player,current)){poseRequests.Clear();handsWereEnabled=false;}
            player=current;hands.Clear();cursorMap.Clear();if(player==null)return;
            if(playerType.GetField("hands")?.GetValue(player) is IEnumerable list)foreach(var h in list)if(h!=null)hands.Add(h);
            if(playerType.GetField("uiSpriteDict")?.GetValue(player) is IDictionary icons)foreach(DictionaryEntry entry in icons){string key=entry.Key as string;if(key!="hand"&&key!="hand-grabbed"&&key!="hand-open"&&key!="hand-grab"&&key!="hand-grabbable")continue;var s=entry.Value?.GetType().GetField("sprite")?.GetValue(entry.Value) as Sprite;if(s!=null)cursorMap[s]=key=="hand-grabbed"||key=="hand-grab";}
        }
        public Sprite ReplacePose(object hand,string pose,Sprite original)
        {
            if(original==null)return original;
            if(!Enabled(HandsEnabled)){
                // Retain requests only for previously verified local hands, without loading
                // resources or adding work for remote/untracked hands while disabled.
                if(hand!=null&&poseRequests.TryGetValue(hand,out var previous)){previous.Pose=pose;previous.Source=assets.Original(original);}
                return original;
            }
            if(!Ensure())return original;Refresh();if(!hands.Contains(hand))return original;
            return SelectPose(hand,pose,original);
        }
        Sprite SelectPose(object hand,string pose,Sprite original)
        {
            Type t=hand.GetType();int id=Convert.ToInt32(t.GetField("id")?.GetValue(hand)??1);var renderer=t.GetField("handSprite")?.GetValue(hand) as SpriteRenderer;
            // Vanilla mirrors the RIGHT sprite with localScale.x=-1. Include every ancestor,
            // not only SpriteRenderer.flipX, so the final visible hand matches its actual side.
            bool transformFlip=false;if(renderer!=null)for(var tr=renderer.transform;tr!=null;tr=tr.parent)if(tr.localScale.x<0)transformFlip=!transformFlip;
            bool flip=(id==0)^(renderer!=null&&renderer.flipX)^transformFlip;var result=assets.Replace(pose,original,flip,HandScale?.Invoke()??0.85f);
            if(assets.IsHand(result)){if(!poseRequests.TryGetValue(hand,out var request)){request=new PoseRequest();poseRequests.Add(hand,request);}request.Pose=pose;request.Source=assets.Original(original);}
            return result;
        }
        void ResumeGenericPoses()
        {
            foreach(var h in hands){
                var type=h.GetType();var bg=type.GetField("handSprite")?.GetValue(h) as SpriteRenderer;var fg=type.GetField("handForegroundSprite")?.GetValue(h) as SpriteRenderer;
                // A matching last native pose with no foreground is the only state we own.
                // Respect later item/skill/FX or third-party changes instead of replaying a snapshot.
                if(bg==null||(fg!=null&&fg.sprite!=null))continue;
                if(poseRequests.TryGetValue(h,out var request)&&bg.sprite==request.Source&&Array.IndexOf(FirstPersonAssets.Poses,request.Pose)>=0){bg.sprite=SelectPose(h,request.Pose,request.Source);continue;}
                // First enable or a new player has no recorded hook request yet. Resolve
                // only an exact Sprite reference from that local hand's native pose table.
                if(type.GetField("handPoses",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(h) is IEnumerable poses)foreach(var entry in poses){
                    if(entry==null)continue;var entryType=entry.GetType();string pose=entryType.GetField("id")?.GetValue(entry) as string;var source=entryType.GetField("sprite")?.GetValue(entry) as Sprite;
                    if(source!=null&&source==bg.sprite&&Array.IndexOf(FirstPersonAssets.Poses,pose)>=0){bg.sprite=SelectPose(h,pose,source);break;}
                }
            }
        }
        public Sprite ReplaceCursor(object hand,Sprite original)
        {
            if(!Enabled(PawEnabled)||original==null||!Ensure())return original;Refresh();if(!hands.Contains(hand)||!cursorMap.TryGetValue(original,out bool held))return original;
            var image=hand.GetType().GetField("uiInteract")?.GetValue(hand) as Image;if(image!=null)cursorOriginals[image]=original;return assets.Paw(held);
        }
        void LateUpdate(){Sync();}
        static readonly string[] HandRendererFields={"handSprite","handForegroundSprite"};
        void RestoreGeneric(SpriteRenderer r){if(r!=null&&assets!=null&&assets.IsHand(r.sprite))r.sprite=assets.Original(r.sprite);}
        public void Sync()
        {
            bool handsEnabled=Enabled(HandsEnabled);
            if(!handsEnabled){RestoreHands();}else if(Ensure()){
                Refresh();if(!handsWereEnabled)ResumeGenericPoses();if(heldSkin==null&&!heldFailed)try{heldSkin=new HeldHandSkin(System.IO.Path.Combine(Folder,"Held"));}catch(Exception e){heldFailed=true;Warn?.Invoke("Held hand skin unavailable: "+e.Message);}
                heldSkin?.Sync(hands,HandBrightness?.Invoke()??0.7f);seenGeneric.Clear();
                foreach(var h in hands)foreach(string field in HandRendererFields){var r=h.GetType().GetField(field)?.GetValue(h) as SpriteRenderer;if(r==null||!assets.IsHand(r.sprite))continue;genericRenderers.Add(r);seenGeneric.Add(r);}
                staleGeneric.Clear();foreach(var r in genericRenderers)if(r==null||!seenGeneric.Contains(r))staleGeneric.Add(r);foreach(var r in staleGeneric){RestoreGeneric(r);genericRenderers.Remove(r);}
                // Do not assign Sprites/Default, a fixed _Color, or a material snapshot.
                // Hand_Base -> Hand_SpriteController keeps writing live exhaustion and cosmetic feedback.
            }
            handsWereEnabled=handsEnabled;if(!Enabled(PawEnabled))RestoreCursors();
        }
        void RestoreHands()
        {
            heldSkin?.Restore();if(assets==null)return;foreach(var r in genericRenderers)RestoreGeneric(r);genericRenderers.Clear();
            foreach(var h in hands)foreach(string field in HandRendererFields)RestoreGeneric(h.GetType().GetField(field)?.GetValue(h) as SpriteRenderer);
        }
        void RestoreCursors(){if(assets==null)return;foreach(var p in cursorOriginals)if(p.Key!=null&&assets.IsPaw(p.Key.sprite))p.Key.sprite=p.Value;cursorOriginals.Clear();}
        void OnDestroy(){RestoreHands();RestoreCursors();poseRequests.Clear();heldSkin?.Dispose();assets?.Dispose();if(Current==this)Current=null;}
    }
}
