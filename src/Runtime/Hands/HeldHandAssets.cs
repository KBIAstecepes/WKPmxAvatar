using System;using System.IO;using System.Collections.Generic;using Newtonsoft.Json.Linq;using UnityEngine;using Object=UnityEngine.Object;
namespace WKPmxAvatar
{
    // Explicit native hand names only. No item art, PRK effects or unknown sprites.
    public sealed class HeldHandAssets:IDisposable
    {
        readonly Dictionary<string,Texture2D> maps=new Dictionary<string,Texture2D>(StringComparer.Ordinal);
        readonly Dictionary<string,Texture2D> contexts=new Dictionary<string,Texture2D>(StringComparer.Ordinal);
        readonly Dictionary<string,Sprite> sprites=new Dictionary<string,Sprite>();
        readonly Dictionary<Sprite,Sprite> originals=new Dictionary<Sprite,Sprite>();
        readonly List<Object> owned=new List<Object>();public int Count=>maps.Count+contexts.Count;public bool Ready=>maps.Count>0;
        static string Key(string item,string source)=>item+"\n"+source;
        static bool Part(string s)=>!string.IsNullOrEmpty(s)&&s.Length<=128&&s.IndexOfAny(new[]{'\r','\n','\0'})<0;
        public static bool IsHandSource(string s)=>s=="H3A4"||s=="HotCocoaHands_"||s=="HotCocoafingers_1"||(s!=null&&s.Length==8&&s.StartsWith("HND_",StringComparison.Ordinal)&&(s[4]=='B'||s[4]=='F')&&s[5]>='1'&&s[5]<='3'&&s[6]>='A'&&s[6]<='D'&&s[7]>='1'&&s[7]<='4');
        public static bool IsBackgroundSource(string s)=>IsHandSource(s)&&(s=="H3A4"||s=="HotCocoaHands_"||s.StartsWith("HND_B",StringComparison.Ordinal));
        static string LocalFile(string folder,string file){if(string.IsNullOrEmpty(file)||Path.GetFileName(file)!=file||file.Contains("..")||file.IndexOfAny(new[]{'\r','\n','\0'})>=0)throw new InvalidDataException("Invalid held asset filename");return Path.Combine(folder,file);}
        Texture2D Load(string folder,string file,string label){string p=LocalFile(folder,file);var info=new FileInfo(p);if(!info.Exists||info.Length>16*1024*1024)throw new InvalidDataException("Held image missing or too large");var t=new Texture2D(2,2,TextureFormat.RGBA32,false);owned.Add(t);if(!t.LoadImage(File.ReadAllBytes(p))||t.width>2048||t.height>2048)throw new InvalidDataException("Invalid held PNG");t.name="RossiHeld/"+label;t.wrapMode=TextureWrapMode.Clamp;t.filterMode=FilterMode.Bilinear;return t;}
        public HeldHandAssets(string folder)
        {
            try{
                string manifest=Path.Combine(folder,"manifest.json");if(!File.Exists(manifest))return;if(new FileInfo(manifest).Length>65536)throw new InvalidDataException("Held manifest too large");
                var root=JObject.Parse(File.ReadAllText(manifest));if((int?)root["schema"]!=1)throw new InvalidDataException("Unsupported held manifest");var rows=root["entries"] as JArray;var contextRows=root["contextEntries"] as JArray;
                if(rows==null||(root["contextEntries"]!=null&&contextRows==null)||rows.Count+(contextRows?.Count??0)>128)throw new InvalidDataException("Invalid held entries");
                foreach(var row in rows){string item=(string)row["item"],source=(string)row["source"];if(!Part(item)||!IsHandSource(source))throw new InvalidDataException("Invalid held mapping");string key=Key(item,source);if(maps.ContainsKey(key))throw new InvalidDataException("Duplicate held mapping");maps.Add(key,Load(folder,(string)row["file"],item+"/"+source));}
                if(contextRows!=null)foreach(var row in contextRows){string item=(string)row["item"],bg=(string)row["background"],source=(string)row["source"];if(!Part(item)||!IsBackgroundSource(bg)||!IsHandSource(source)||IsBackgroundSource(source))throw new InvalidDataException("Invalid held context");string key=Key(item,bg)+"\n"+source;if(contexts.ContainsKey(key))throw new InvalidDataException("Duplicate held context");contexts.Add(key,Load(folder,(string)row["file"],item+"/"+bg+"/"+source));}
                // Schema1 keeps accepting its legacy shaderBundle field, but rendering stays
                // on each game's live native material. No custom Shader bundle is loaded.
            }catch{Dispose();throw;}
        }
        public Sprite Original(Sprite s)=>s!=null&&originals.TryGetValue(s,out var native)?native:s;
        public bool IsReplacement(Sprite s)=>s!=null&&originals.ContainsKey(s);
        public Sprite Replace(string item,Sprite source,Sprite background=null)
        {
            source=Original(source);background=Original(background);if(!Ready||source==null||item==null||!IsHandSource(source.name))return source;Texture2D texture=null;
            if(background!=null){string suffix=background.name+"\n"+source.name;if(!contexts.TryGetValue(Key(item,suffix),out texture))contexts.TryGetValue(Key("*",suffix),out texture);}
            if(texture==null&&!maps.TryGetValue(Key(item,source.name),out texture)&&!maps.TryGetValue(Key("*",source.name),out texture))return source;
            string key=texture.GetInstanceID()+"\n"+source.GetInstanceID();if(sprites.TryGetValue(key,out var cached))return cached;if(sprites.Count>=256)return source;
            var rect=source.rect;float ratio=texture.width/rect.width;if(Mathf.Abs(texture.height/rect.height-ratio)>.001f)return source;
            var result=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(source.pivot.x/rect.width,source.pivot.y/rect.height),source.pixelsPerUnit*ratio,0,SpriteMeshType.FullRect);result.name="RossiHeld/"+item+"/"+source.name;owned.Add(result);sprites.Add(key,result);originals.Add(result,source);return result;
        }
        public void Dispose(){for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null){if(Application.isPlaying)Object.Destroy(owned[i]);else Object.DestroyImmediate(owned[i]);}owned.Clear();maps.Clear();contexts.Clear();sprites.Clear();originals.Clear();}
    }
}
