using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using WKPmxAvatar.Gaze;

namespace WKPmxAvatar
{
    internal sealed class RossiGazeSources : IGazeSceneSource
    {
        readonly Plugin plugin;Type playerType;FieldInfo playerObject,cameraField;MethodInfo crouch;
        PropertyInfo userId;Component local;Transform root,view;ulong id;
        Vector3 standingOffset;bool haveOffset;float nextResolve,nextStanding,nextIdentity;
        public bool Enabled=>plugin!=null&&plugin.EyeTrackingEnabled.Value;
        public ulong LocalId=>id;public Transform LocalRoot=>root;public Transform LocalView=>view;
        [Serializable] sealed class ScaleInfo{public float referenceIrisSpanPmx=0;}
        internal static EyeGazeWorld CreateWorld(Plugin plugin)
        {
            try{
                using(var stream=typeof(Plugin).Assembly.GetManifestResourceStream("WKPmxAvatar.EyeProfile.json"))
                using(var reader=new StreamReader(stream)){
                    string json=reader.ReadToEnd();return new EyeGazeWorld(new RossiGazeSources(plugin),JsonUtility.FromJson<EyeDirectionProfile>(json),JsonUtility.FromJson<ScaleInfo>(json).referenceIrisSpanPmx);
                }
            }catch(Exception e){plugin.Warn("Optional Rossi eye data unavailable: "+e.GetBaseException().Message);return null;}
        }
        RossiGazeSources(Plugin plugin){this.plugin=plugin;}
        public bool TryStandingEyeOffset(out Vector3 offset){offset=standingOffset;return haveOffset&&root!=null&&view!=null;}
        public void Warn(string message){plugin.Warn(message);}
        public void Refresh(List<GazeRemoteRoot> target)
        {
            plugin.GetGazeRemotes(target);
            if((playerType==null||userId==null)&&Time.unscaledTime>=nextResolve)
            {
                nextResolve=Time.unscaledTime+1;
                foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies()){
                    var t=assembly.GetType("ENT_Player");if(t!=null){playerType=t;playerObject=t.GetField("playerObject",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);cameraField=t.GetField("camTransform",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);crouch=t.GetMethod("IsCrouching",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,Type.EmptyTypes,null);}
                    var network=assembly.GetType("WKMPMod.NetWork.MPSteamworks");if(network!=null)userId=network.GetProperty("UserSteamId",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                }
            }
            var current=playerObject?.GetValue(null) as Component;
            if(current!=local){local=current;root=local!=null?local.transform:null;view=null;haveOffset=false;nextStanding=0;}
            if(local==null||!local.gameObject.activeInHierarchy){root=view=null;return;}
            root=local.transform;view=cameraField?.GetValue(local) as Transform;
            if(view==null)return; // No Camera.main fallback: never select an F6 camera.
            if(id==0&&userId!=null&&Time.unscaledTime>=nextIdentity){nextIdentity=Time.unscaledTime+.5f;try{id=Convert.ToUInt64(userId.GetValue(null));}catch{}}
            if(!haveOffset&&Time.unscaledTime>=nextStanding){nextStanding=Time.unscaledTime+.25f;
                // Until a standing calibration is available, unsupported remote
                // head shapes are omitted rather than aimed at an invented point.
                if(crouch!=null&&crouch.Invoke(local,null) is bool crouched&&!crouched){standingOffset=root.InverseTransformPoint(view.position);haveOffset=true;}
            }
        }
    }
}
