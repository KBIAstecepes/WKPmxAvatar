using System;
using System.Collections.Generic;
using UnityEngine;

namespace WKPmxAvatar.Gaze
{
    public struct GazeRemoteRoot
    {
        public ulong Id;public Transform Root,Head;
        public GazeRemoteRoot(ulong id,Transform root,Transform head){Id=id;Root=root;Head=head;}
    }
    public interface IGazeSceneSource
    {
        bool Enabled{get;}ulong LocalId{get;}Transform LocalRoot{get;}Transform LocalView{get;}
        bool TryStandingEyeOffset(out Vector3 offset);
        void Refresh(List<GazeRemoteRoot> target);void Warn(string message);
    }
    public sealed class EyeGazeWorld : IDisposable
    {
        readonly IGazeSceneSource source;readonly EyeDirectionProfile profile;readonly float referenceSpan;
        readonly List<GazeRemoteRoot> remotes=new List<GazeRemoteRoot>(16);
        readonly List<GazeParticipant> participants=new List<GazeParticipant>(16);
        readonly List<EyeAvatarBinding> bindings=new List<EyeAvatarBinding>(16);
        readonly Dictionary<ulong,Transform> hints=new Dictionary<ulong,Transform>();
        readonly HashSet<ulong> seen=new HashSet<ulong>();readonly List<ulong> stale=new List<ulong>();
        int lastFrame=-1;bool disposed,warned;
        public bool Enabled=>!disposed&&source.Enabled;
        public IReadOnlyList<GazeParticipant> Participants{get{Refresh();return participants;}}
        public EyeGazeWorld(IGazeSceneSource sceneSource,EyeDirectionProfile eyeProfile,float referenceIrisSpan)
        {source=sceneSource;profile=eyeProfile;referenceSpan=referenceIrisSpan;}
        public void Invalidate(){lastFrame=-1;}
        public void Refresh(bool force=false)
        {
            if(disposed||(!force&&lastFrame==Time.frameCount))return;lastFrame=Time.frameCount;
            source.Refresh(remotes);participants.Clear();seen.Clear();
            if(source.LocalId!=0&&source.LocalRoot!=null&&source.LocalView!=null){participants.Add(new GazeParticipant(source.LocalId,source.LocalRoot,source.LocalView));seen.Add(source.LocalId);}
            foreach(var remote in remotes)
            {
                if(remote.Id==0||remote.Root==null||!seen.Add(remote.Id))continue;
                Transform head=remote.Head;
                if(head!=null&&head!=remote.Root&&!head.IsChildOf(remote.Root))head=null;
                if(head!=null){RemoveHint(remote.Id);}
                else if(source.TryStandingEyeOffset(out var offset))
                {
                    if(!hints.TryGetValue(remote.Id,out head)||head==null||head.parent!=remote.Root)
                    {
                        RemoveHint(remote.Id);var go=new GameObject("WKPmx_GazeHeadAnchor");head=go.transform;head.SetParent(remote.Root,false);hints[remote.Id]=head;
                    }
                    // Calibrated from the game's own standing camera/root pair,
                    // not a guessed model height or a floating name tag.
                    head.localPosition=offset;head.localRotation=Quaternion.identity;
                }
                if(head!=null)participants.Add(new GazeParticipant(remote.Id,remote.Root,head));
            }
            stale.Clear();foreach(var item in hints)if(!seen.Contains(item.Key)||item.Value==null)stale.Add(item.Key);
            foreach(ulong id in stale)RemoveHint(id);
        }
        void RemoveHint(ulong id){if(hints.TryGetValue(id,out var hint)){if(hint!=null)UnityEngine.Object.Destroy(hint.gameObject);hints.Remove(id);}}
        public EyeAvatarBinding Attach(GameObject avatar,Transform head,Transform[] bones,Transform owner,Transform view,ulong id,Func<bool> automatic,bool manual)
        {
            if(disposed||id==0||head==null||avatar==null||owner==null||view==null)return null;
            var existing=avatar.GetComponent<EyeAvatarBinding>();if(existing!=null)return existing;
            EyeAvatarBinding binding=null;
            try
            {
                var left=Array.Find(bones,b=>b!=null&&b.name=="瞳左");var right=Array.Find(bones,b=>b!=null&&b.name=="瞳右");
                if(left==null||right==null||referenceSpan<=0)return null;
                float scale=Vector3.Distance(avatar.transform.InverseTransformPoint(left.position),avatar.transform.InverseTransformPoint(right.position))/referenceSpan;
                if(scale<=0||float.IsNaN(scale)||float.IsInfinity(scale))return null;
                var layer=new EyePoseLayer(profile,bones,scale);var core=new NearestPlayerGaze(id,owner,view,head,layer);
                binding=avatar.AddComponent<EyeAvatarBinding>();binding.Initialize(this,core,owner,automatic,manual);bindings.Add(binding);return binding;
            }
            catch(Exception e){if(binding!=null)UnityEngine.Object.Destroy(binding);Warn("Eye tracking skipped: "+e.GetBaseException().Message);return null;}
        }
        public EyeAvatarBinding AttachLocal(GameObject avatar,Transform head,Transform[] bones)
        {Refresh(true);return Attach(avatar,head,bones,source.LocalRoot,source.LocalView,source.LocalId,null,true);}
        internal void Unregister(EyeAvatarBinding binding){bindings.Remove(binding);}
        internal void Warn(string message){if(warned)return;warned=true;source.Warn(message);}
        public void Dispose()
        {
            if(disposed)return;disposed=true;foreach(var binding in bindings)if(binding!=null)binding.ResetPose();bindings.Clear();
            foreach(var pair in hints)if(pair.Value!=null)UnityEngine.Object.Destroy(pair.Value.gameObject);hints.Clear();participants.Clear();remotes.Clear();seen.Clear();stale.Clear();
        }
    }
    // Runs after AvatarMotion (10000) and before appearance (10900). F6 is
    // explicitly manual, after MirrorTick and before BakeMesh.
    [DefaultExecutionOrder(10800)]
    public sealed class EyeAvatarBinding : MonoBehaviour
    {
        EyeGazeWorld world;NearestPlayerGaze core;Transform owner;Func<bool> automatic;bool manual,reset=true,stopped;
        Vector3 lastPosition;
        public ulong CurrentTarget=>core?.TargetId??0;
        internal void Initialize(EyeGazeWorld world,NearestPlayerGaze core,Transform owner,Func<bool> automatic,bool manual)
        {this.world=world;this.core=core;this.owner=owner;this.automatic=automatic;this.manual=manual;lastPosition=owner.position;}
        void OnEnable(){reset=true;stopped=false;}
        void LateUpdate(){if(!manual&&(automatic==null||automatic()))Tick(Time.deltaTime);}
        public void TickManual(float dt){if(manual)Tick(dt);}
        void Tick(float dt)
        {
            if(core==null||world==null||owner==null||stopped)return;
            try{
                bool fresh=reset||(owner.position-lastPosition).sqrMagnitude>9;lastPosition=owner.position;reset=false;
                core.Tick(world.Participants,dt,world.Enabled,fresh);
            }catch(Exception e){core.Reset();stopped=true;world.Warn("Eye tracking paused for this avatar: "+e.GetBaseException().Message);}
        }
        public void ResetPose(){core?.Reset();reset=true;}
        void OnDisable(){ResetPose();}
        void OnDestroy(){ResetPose();world?.Unregister(this);}
    }
}
