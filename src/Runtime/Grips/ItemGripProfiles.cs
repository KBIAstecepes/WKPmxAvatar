using System;using System.IO;using System.Collections.Generic;using System.Reflection;using Newtonsoft.Json;using UnityEngine;
namespace WKPmxAvatar
{
    [Serializable] public sealed class ItemGripProfile
    {
        public string item,shape;public float maxDimension=0.2f,edge=0.3f;public float[] curls;
        // Optional anchor in the largest source mesh's normalized bounds coordinates.
        public float[] meshAnchor;
    }
    public static class ItemGripProfiles
    {
        static readonly Dictionary<string,ItemGripProfile> profiles=new Dictionary<string,ItemGripProfile>(StringComparer.OrdinalIgnoreCase);
        public static int Count=>profiles.Count;
        public static void Load(string file,Action<string> warning)
        {
            profiles.Clear();try{if(new FileInfo(file).Length>256*1024)throw new InvalidDataException("Grip profile size cap");var entries=JsonConvert.DeserializeObject<ItemGripProfile[]>(File.ReadAllText(file));if(entries==null||entries.Length>256)throw new InvalidDataException("Grip profile count");foreach(var p in entries){if(string.IsNullOrWhiteSpace(p.item)||profiles.ContainsKey(p.item)||p.curls==null||p.curls.Length!=5||!float.IsFinite(p.maxDimension)||p.maxDimension<0.03f||p.maxDimension>1||!float.IsFinite(p.edge)||p.edge<0||p.edge>0.48f||(p.shape!="pinch"&&p.shape!="syringe"&&p.shape!="cylinder"&&p.shape!="handle"))throw new InvalidDataException("Invalid grip profile");if(p.meshAnchor!=null){if(p.meshAnchor.Length!=3)throw new InvalidDataException("Invalid mesh grip anchor");foreach(float a in p.meshAnchor)if(!float.IsFinite(a)||Math.Abs(a)>0.5f)throw new InvalidDataException("Invalid mesh grip anchor");}foreach(float c in p.curls)if(!float.IsFinite(c)||c<0||c>1)throw new InvalidDataException("Invalid finger curl");profiles.Add(p.item,p);}}catch(Exception e){profiles.Clear();warning?.Invoke("Item grip profiles unavailable; generic grip retained: "+e.Message);}
        }
        public static ItemGripProfile Find(string item){if(item==null)return null;item=item.Replace("(Clone)","").Trim();return profiles.TryGetValue(item,out var p)?p:null;}
        public static string Name(Component hand,GameObject item){var name=hand?.GetType().GetField("_itemPrefabName",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(hand) as string;return string.IsNullOrEmpty(name)?item?.name:name;}
    }
}
