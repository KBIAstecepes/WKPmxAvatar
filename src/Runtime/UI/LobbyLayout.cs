using System;
namespace WKPmxAvatar
{
    public static class LobbyLayout
    {
        public const float Width=620,Height=690;
        public static float Scale(float availableWidth,float availableHeight)
        {if(float.IsNaN(availableWidth)||float.IsNaN(availableHeight)||float.IsInfinity(availableWidth)||float.IsInfinity(availableHeight))return 1;return Math.Max(.01f,Math.Min(1,Math.Min(Math.Max(1,availableWidth-36)/Width,Math.Max(1,availableHeight-36)/Height)));}
    }
}
