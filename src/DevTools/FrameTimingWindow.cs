using System;
namespace WKPmxAvatar
{
    internal sealed class FrameTimingWindow
    {
        public int Frames{get;private set;}public double Seconds{get;private set;}public float WorstSeconds{get;private set;}
        public double AverageFps=>Seconds>0?Frames/Seconds:0;
        public void Add(float dt){if(!float.IsFinite(dt)||dt<=0)return;Frames++;Seconds+=dt;if(dt>WorstSeconds)WorstSeconds=dt;}
        public void Reset(){Frames=0;Seconds=0;WorstSeconds=0;}
    }
}
