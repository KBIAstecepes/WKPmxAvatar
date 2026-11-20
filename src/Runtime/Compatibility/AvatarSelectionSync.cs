using System;using System.Collections.Generic;using System.Linq;using System.Reflection;using UnityEngine;using HarmonyLib;
namespace WKPmxAvatar
{
    // Uses the MP mod's existing member-data and player-dictionary channels, without changing packets.
    public sealed class AvatarSelectionSync:MonoBehaviour
    {
        internal Plugin Owner;Type network,uiManager;object previousNetwork;ulong previousLobby;string published;float next,nextPublish;
        static WeakReference<GameObject> mountedScreen;float nextUiRecovery;
        public static void AfterDictionary(ref Dictionary<string,string> __result){var p=Plugin.Instance;if(p!=null&&__result!=null&&ModelCatalog.ValidId(p.PersonalModelId))__result[ModelCatalog.WireKey]=p.PersonalModelId;}
        public static void AfterReceive(object __instance,Dictionary<string,string> __0){var p=Plugin.Instance;if(p==null||__0==null)return;__0.TryGetValue(ModelCatalog.WireKey,out var id);p.ReceiveSelection(__instance,id);}
        public void Invalidate(){published=null;next=0;}
        internal static object ExistingNetwork(Type t)
        {
            // Do not access MonoSingleton.Instance: its getter can create a new network manager.
            for(var current=t;current!=null;current=current.BaseType){var field=current.GetField("_instance",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.DeclaredOnly);if(field!=null)return field.GetValue(null);}return null;
        }
        void Update()
        {
            if(Owner==null||Time.unscaledTime<next)return;next=Time.unscaledTime+1;
            try{
                if(uiManager==null)uiManager=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("WKMPMod.UI.UI_Manager")).FirstOrDefault(t=>t!=null);
                bool haveScreen=mountedScreen!=null&&mountedScreen.TryGetTarget(out var screen)&&screen!=null;
                if(!haveScreen&&Time.unscaledTime>=nextUiRecovery){nextUiRecovery=Time.unscaledTime+5;if(uiManager!=null)foreach(var ui in Resources.FindObjectsOfTypeAll(uiManager))if(ui is Component component&&component.gameObject.scene.IsValid())AfterLobbyScreen(ui);}
                if(network==null)network=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("WKMPMod.NetWork.MPSteamworks")).FirstOrDefault(t=>t!=null);
                object instance=ExistingNetwork(network);if(instance==null||(instance is UnityEngine.Object dead&&dead==null)){Reset();return;}
                if(!(network.GetProperty("IsInLobby")?.GetValue(instance) is bool active)||!active){Reset();return;}
                ulong lobby=Convert.ToUInt64(network.GetProperty("LobbyId").GetValue(instance));if(!ReferenceEquals(previousNetwork,instance)||previousLobby!=lobby){Owner.ClearSelections();published=null;previousNetwork=instance;previousLobby=lobby;}
                var get=network.GetMethod("GetMemberData",new[]{typeof(ulong),typeof(string)});if(get==null)return;
                string selected=Owner.PersonalModelId;if(ModelCatalog.ValidId(selected)&&(selected!=published||Time.unscaledTime>=nextPublish)){
                    ulong self=Convert.ToUInt64(network.GetProperty("UserSteamId",BindingFlags.Public|BindingFlags.Static)?.GetValue(null)??0UL);
                    string stored=self==0?null:get.Invoke(instance,new object[]{self,ModelCatalog.WireKey}) as string;
                    if(stored!=selected)network.GetMethod("SetMemberData",new[]{typeof(string),typeof(string)}).Invoke(instance,new object[]{ModelCatalog.WireKey,selected});
                    published=selected;nextPublish=Time.unscaledTime+5;
                }
                foreach(var container in Owner.RemoteContainers){ulong id=Convert.ToUInt64(container.GetType().GetProperty("PlayerId").GetValue(container));string value=get.Invoke(instance,new object[]{id,ModelCatalog.WireKey}) as string;if(!string.IsNullOrEmpty(value))Owner.ReceiveSelection(container,value);}
            }catch(Exception e){Owner.SelectionWarning(e.GetBaseException().Message);next=Time.unscaledTime+5;}
        }
        void Reset(){if(previousNetwork!=null||previousLobby!=0)Owner.ClearSelections();previousNetwork=null;previousLobby=0;published=null;}
        public static void AfterLobbyScreen(object __instance){var p=Plugin.Instance;if(p==null)return;try{var field=AccessTools.Field(__instance.GetType(),"_mpScreen");var screen=field?.GetValue(__instance) as GameObject;if(screen!=null){LobbyAvatarMenu.Mount(screen,p);mountedScreen=new WeakReference<GameObject>(screen);}}catch(Exception e){p.SelectionWarning(e.Message);}}
    }
}
