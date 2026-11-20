using System;
namespace WKPmxAvatar
{
    public static class LobbyVisibility
    {
        public static bool Show(bool screenActive,bool screenInteractable,float screenAlpha)
        {return screenActive&&screenInteractable&&!float.IsNaN(screenAlpha)&&!float.IsInfinity(screenAlpha)&&screenAlpha>.01f;}
        // Closing keeps the entry attached to the native exit tween, but never clickable.
        public static bool DuringAnimation(bool active,bool animationOpen,float exitTimeRemaining)
        {return active&&(animationOpen||(!float.IsNaN(exitTimeRemaining)&&!float.IsInfinity(exitTimeRemaining)&&exitTimeRemaining>0));}
    }
}
