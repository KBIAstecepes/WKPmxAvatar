using System;using System.Collections;using System.Collections.Generic;using System.Reflection;using UnityEngine;
namespace WKPmxAvatar
{
    public sealed class HeldHandSkin:IDisposable
    {
        readonly HeldHandAssets assets;public string Status=>assets.Ready?"held: "+assets.Count+" hand/context mappings":"held: original visuals (resources unavailable)";
        sealed class Binding{public SpriteRenderer Renderer;}
        sealed class ControllerCache{public Component[] Controllers;public float RefreshAt;}
        readonly Dictionary<SpriteRenderer,Binding> bindings=new Dictionary<SpriteRenderer,Binding>();readonly HashSet<SpriteRenderer> seen=new HashSet<SpriteRenderer>();readonly List<SpriteRenderer> stale=new List<SpriteRenderer>();
        readonly Dictionary<object,ControllerCache> controllerCache=new Dictionary<object,ControllerCache>();readonly HashSet<object> currentItems=new HashSet<object>();readonly List<object> expired=new List<object>();
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        static object Field(object instance,string name){if(instance==null)return null;for(Type t=instance.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,Flags|BindingFlags.DeclaredOnly);if(f!=null)return f.GetValue(instance);}return null;}
        public HeldHandSkin(string folder){assets=new HeldHandAssets(folder);}
        IEnumerable Controllers(object item){if(!(item is Component c))return Field(item,"handSprites") as IEnumerable;if(!controllerCache.TryGetValue(item,out var cached)||Time.unscaledTime>=cached.RefreshAt){var type=item.GetType().Assembly.GetType("Hand_SpriteController");if(type==null)return Field(item,"handSprites") as IEnumerable;cached=new ControllerCache{Controllers=c.GetComponentsInChildren(type,true),RefreshAt=Time.unscaledTime+1};controllerCache[item]=cached;}return cached.Controllers;}
        Sprite AncestorBackground(SpriteRenderer r,Component owner){for(var tr=r.transform.parent;tr!=null;tr=tr.parent){if(owner!=null&&tr==owner.transform.parent)break;var parent=tr.GetComponent<SpriteRenderer>();var s=parent==null?null:assets.Original(parent.sprite);if(s!=null&&HeldHandAssets.IsBackgroundSource(s.name))return s;}return null;}
        // Only the Sprite changes. Native material, property block, tint and feedback stay owned by the game.
        void Apply(SpriteRenderer r,string item,Sprite background,float brightness){if(r==null)return;var source=assets.Original(r.sprite);var replacement=assets.Replace(item,source,background);if(replacement==source)return;seen.Add(r);if(!bindings.ContainsKey(r))bindings.Add(r,new Binding{Renderer=r});r.sprite=replacement;}
        public void Sync(IEnumerable hands,float brightness)
        {
            seen.Clear();currentItems.Clear();if(!float.IsFinite(brightness))brightness=.7f;brightness=Mathf.Clamp(brightness,.35f,1);
            foreach(object hand in hands){if(hand==null)continue;object item=hand.GetType().GetMethod("GetHandItem",Flags,null,Type.EmptyTypes,null)?.Invoke(hand,null);if(item is UnityEngine.Object dead&&dead==null)item=null;string id=Field(Field(item,"item"),"prefabName") as string;
                if(item!=null){currentItems.Add(item);if(id!=null&&Controllers(item) is IEnumerable controllers)foreach(object controller in controllers){var r=Field(controller,"sprite") as SpriteRenderer;if(r==null&&controller is Component child)r=child.GetComponent<SpriteRenderer>();if(r!=null)Apply(r,id,AncestorBackground(r,item as Component),brightness);}}
                // SetSprite also assigns the real player renderers directly when no HandItem exists.
                // Read BG identity before changing either layer, and never touch native PRK FX.
                var bg=Field(hand,"handSprite") as SpriteRenderer;var fg=Field(hand,"handForegroundSprite") as SpriteRenderer;var bgSource=bg==null?null:assets.Original(bg.sprite);
                Apply(bg,id??"*",null,brightness);Apply(fg,id??"*",bgSource,brightness);
            }
            stale.Clear();foreach(var p in bindings)if(p.Key==null||!seen.Contains(p.Key))stale.Add(p.Key);foreach(var r in stale){Restore(bindings[r]);bindings.Remove(r);}
            expired.Clear();foreach(var item in controllerCache.Keys)if(!currentItems.Contains(item))expired.Add(item);foreach(var item in expired)controllerCache.Remove(item);
        }
        void Restore(Binding b){if(b.Renderer!=null&&assets.IsReplacement(b.Renderer.sprite))b.Renderer.sprite=assets.Original(b.Renderer.sprite);}
        public void Restore(){foreach(var b in bindings.Values)Restore(b);bindings.Clear();controllerCache.Clear();currentItems.Clear();}
        public void Dispose(){Restore();assets.Dispose();}
    }
}
