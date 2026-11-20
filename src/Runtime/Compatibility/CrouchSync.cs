using System;using System.Collections.Generic;using System.Reflection;using UnityEngine;
namespace WKPmxAvatar
{
    public static class CrouchSync
    {
        public const string Key="wkpmx.crouched";
        static object lastSender;static bool previous;static readonly HashSet<object> pending=new HashSet<object>();
        public static bool Local(out bool crouched)
        {crouched=false;foreach(var asm in AppDomain.CurrentDomain.GetAssemblies()){var t=asm.GetType("ENT_Player");if(t==null)continue;var p=t.GetField("playerObject",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)?.GetValue(null);if(p==null)return false;var result=t.GetMethod("IsCrouching",Type.EmptyTypes)?.Invoke(p,null);if(result is bool b){crouched=b;return true;}return false;}return false;}
        public static void BeforeSend(object __instance)
        {
            if(!Local(out bool current))return;if(ReferenceEquals(lastSender,__instance)&&current==previous)return;
            object timer=__instance.GetType().GetField("_minUpdateFrequencyTimer",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(__instance);if(timer==null)return;
            pending.Add(timer);lastSender=__instance;previous=current;
        }
        // Promote only the existing low-frequency dictionary send on a state transition.
        // No protocol layout change; all ordinary timers retain their original results.
        public static void AfterTimer(object __instance,ref bool __result){if(pending.Remove(__instance))__result=true;}
        public static void AfterDictionary(ref Dictionary<string,string> __result){if(__result!=null&&Local(out bool state))__result[Key]=state?"1":"0";}
        public static void AfterReceive(object __instance,Dictionary<string,string> __0)
        {
            if(__0==null||!__0.TryGetValue(Key,out string value)||(value!="0"&&value!="1"))return;
            var root=__instance.GetType().GetProperty("PlayerObject")?.GetValue(__instance) as GameObject;root?.GetComponentInChildren<AvatarMotion>(true)?.ReadCrouch(value=="1");
        }
    }
}
