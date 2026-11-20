using System;using UnityEngine;using UnityEngine.Profiling;
namespace WKPmxAvatar
{
    // Local aggregate diagnostics only. No forced GC, object enumeration,
    // input automation, network transmission or per-frame collection allocation.
    public sealed class PerformanceMonitor:MonoBehaviour
    {
        public Func<bool> Enabled;public Func<int> ActiveAvatars;public Action<string> Log;
        readonly FrameTimingWindow window=new FrameTimingWindow();float next;bool active;int lastGc;
        void Update()
        {
            if(Enabled==null||!Enabled()){active=false;window.Reset();return;}
            if(!active){active=true;next=Time.unscaledTime+30;lastGc=GC.CollectionCount(0);window.Reset();}
            window.Add(Time.unscaledDeltaTime);if(Time.unscaledTime<next)return;next=Time.unscaledTime+30;
            int gc=GC.CollectionCount(0);const double mb=1024*1024;
            Log?.Invoke("WKPMX_PERF avgFps="+window.AverageFps.ToString("F1")+" peakMs="+(window.WorstSeconds*1000).ToString("F1")+" managedMB="+(GC.GetTotalMemory(false)/mb).ToString("F1")+" unityAllocatedMB="+(Profiler.GetTotalAllocatedMemoryLong()/mb).ToString("F1")+" gc0Delta="+(gc-lastGc)+" avatars="+(ActiveAvatars?.Invoke()??0)+" clipDecodes="+MotionClip.SharedLoads+" clipCacheHits="+MotionClip.SharedHits+" clipCacheKeys="+MotionClip.SharedKeys);
            lastGc=gc;window.Reset();
        }
    }
}
