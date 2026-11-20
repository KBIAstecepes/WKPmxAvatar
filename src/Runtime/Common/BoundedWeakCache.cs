using System;using System.Collections.Generic;
namespace WKPmxAvatar
{
    // Shared decoded data may be reused while a rig owns it. The cache itself
    // must not keep a destroyed rig's large motion arrays alive indefinitely.
    internal sealed class BoundedWeakCache<T> where T:class
    {
        readonly int capacity;readonly Dictionary<string,WeakReference<T>> entries=new Dictionary<string,WeakReference<T>>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> dead=new List<string>();public int Hits{get;private set;}public int Loads{get;private set;}public int Keys=>entries.Count;
        public BoundedWeakCache(int capacity){if(capacity<1)throw new ArgumentOutOfRangeException(nameof(capacity));this.capacity=capacity;}
        public T Get(string key,Func<T> factory)
        {
            if(key==null||factory==null)throw new ArgumentNullException();
            if(entries.TryGetValue(key,out var weak)&&weak.TryGetTarget(out var found)){Hits++;return found;}
            var value=factory()??throw new InvalidOperationException("Resource factory returned null");Loads++;
            if(entries.Count>=capacity){dead.Clear();foreach(var pair in entries)if(!pair.Value.TryGetTarget(out _))dead.Add(pair.Key);foreach(var stale in dead)entries.Remove(stale);dead.Clear();}
            if(entries.Count<capacity||entries.ContainsKey(key))entries[key]=new WeakReference<T>(value);
            return value;
        }
    }
}
