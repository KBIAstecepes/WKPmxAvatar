using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Security.Cryptography;
namespace WKPmxAvatar
{
    public sealed class ModelEntry
    {
        public string Id {get;}public string Name {get;}public string Path {get;}
        public ModelEntry(string id,string name,string path){Id=id;Name=name;Path=path;}
    }
    // Network identities never contain file paths. Only a local scan supplies loadable files.
    public sealed class ModelCatalog
    {
        public const string Original="original";public const string WireKey="wkpmx.avatar.v1";
        readonly Dictionary<string,ModelEntry> byId=new Dictionary<string,ModelEntry>(StringComparer.Ordinal);
        public IReadOnlyList<ModelEntry> Entries {get;private set;}=Array.Empty<ModelEntry>();
        public readonly List<string> Warnings=new List<string>();
        public static bool ValidId(string id){if(id==Original)return true;if(id==null||id.Length!=68||!id.StartsWith("pmx:",StringComparison.Ordinal))return false;for(int i=4;i<id.Length;i++)if(!(id[i]>='0'&&id[i]<='9'||id[i]>='a'&&id[i]<='f'))return false;return true;}
        public static string Identify(string file){using(var sha=SHA256.Create())using(var stream=File.OpenRead(file))return "pmx:"+BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        public ModelEntry Find(string id)=>id!=null&&byId.TryGetValue(id,out var entry)?entry:null;
        public static string DisplayName(string file,int index)
        {
            string name=System.IO.Path.GetFileNameWithoutExtension(file);
            if(name.Contains("洛茜")||name.Contains("洛西"))return "Rossi";
            if(name.All(c=>c>=32&&c<=126)&&!string.IsNullOrWhiteSpace(name))return name;
            return "Custom model "+index;
        }
        public ModelEntry ForPath(string path){if(string.IsNullOrEmpty(path))return null;string full=System.IO.Path.GetFullPath(path);var entry=Entries.FirstOrDefault(e=>e.Path!=null&&string.Equals(e.Path,full,StringComparison.OrdinalIgnoreCase));return entry??(File.Exists(full)?Find(Identify(full)):null);}
        public void Scan(string root,string configuredFile)
        {
            byId.Clear();Warnings.Clear();byId.Add(Original,new ModelEntry(Original,"Default multiplayer model",null));var files=new List<string>();
            if(Directory.Exists(root)){var dirs=new Stack<string>();dirs.Push(System.IO.Path.GetFullPath(root));int visits=0;while(dirs.Count>0&&files.Count<64&&visits++<512){string folder=dirs.Pop();try{foreach(var file in Directory.EnumerateFiles(folder)){if(files.Count>=64)break;if(string.Equals(System.IO.Path.GetExtension(file),".pmx",StringComparison.OrdinalIgnoreCase)&&(File.GetAttributes(file)&FileAttributes.ReparsePoint)==0)files.Add(file);}foreach(var child in Directory.EnumerateDirectories(folder))if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)dirs.Push(child);}catch(Exception e)when(e is IOException||e is UnauthorizedAccessException){Warnings.Add("A model folder could not be read. Check local permissions.");}}if(dirs.Count>0)Warnings.Add("Scan limit: 64 PMX files and 512 folders.");}
            if(!string.IsNullOrEmpty(configuredFile)&&File.Exists(configuredFile)&&!files.Any(p=>string.Equals(System.IO.Path.GetFullPath(p),System.IO.Path.GetFullPath(configuredFile),StringComparison.OrdinalIgnoreCase)))files.Add(configuredFile);
            foreach(string file in files.OrderBy(p=>p,StringComparer.OrdinalIgnoreCase)){try{if(new FileInfo(file).Length>512L*1024*1024){Warnings.Add("A PMX file exceeds the 512 MB limit.");continue;}string id=Identify(file);if(!byId.ContainsKey(id))byId.Add(id,new ModelEntry(id,DisplayName(file,byId.Count),System.IO.Path.GetFullPath(file)));}catch(Exception e)when(e is IOException||e is UnauthorizedAccessException){Warnings.Add("A PMX file could not be read. Check local permissions.");}}
            Entries=byId.Values.OrderBy(e=>e.Id==Original?0:1).ThenBy(e=>e.Name,StringComparer.OrdinalIgnoreCase).ToArray();
        }
        // Manual viewer overrides remain authoritative; absent/unknown identities use the viewer fallback.
        public string Resolve(string remoteId,string fallback,string manualOverride)
        {if(!string.IsNullOrWhiteSpace(manualOverride))return manualOverride;if(remoteId==Original)return null;return Find(remoteId)?.Path??fallback;}
    }
}
