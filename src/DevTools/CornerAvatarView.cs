using System;using System.Linq;using System.Reflection;using UnityEngine;using UnityEngine.SceneManagement;
#if !UNITY_EDITOR
using UnityEngine.InputSystem;
#endif
namespace WKPmxAvatar
{
    // A local-only mirror using the same AvatarMotion component as remote players.
    // Renderers are hidden except within a synchronous private-camera render.
    [DefaultExecutionOrder(11500)]
    public sealed class CornerAvatarView:MonoBehaviour
    {
        public Func<GameObject,AvatarMotion> CreateAvatar;public Func<float> ModelHeight,FeetOffset;
        public bool Visible {get;private set;}public int View {get;private set;}
        readonly string[] viewNames={"Front","Back","Left","Right"};
        GameObject actor,cameraObject;AvatarMotion rig;Camera camera;RenderTexture texture;Renderer[] renderers;
        Transform left,right;Type playerType;Component localPlayer;float retry;string status="F6: toggle | F7: change view";string selected;
        SkinnedMeshRenderer[] skinned;Mesh[] snapshots;MeshRenderer[] snapshotRenderers;
        MaterialPropertyBlock snapshotBlock;readonly System.Collections.Generic.List<Material> snapshotMaterials=new System.Collections.Generic.List<Material>();
        public string Status=>status;public bool HasAvatar=>rig!=null;
        public RenderTexture Output=>texture;
        public bool Frozen {get;private set;}bool saveRequested;
        public string CaptureFolder;
        public void Freeze(){Frozen=!Frozen;}
        public void RequestCapture(){saveRequested=true;}
        void Awake(){SceneManager.activeSceneChanged+=SceneChanged;}
        void SceneChanged(Scene a,Scene b){Release();localPlayer=null;}
        public void Toggle(){Visible=!Visible;if(!Visible)Release();retry=0;}
        public void Cycle(){View=(View+1)%viewNames.Length;}
        void Update()
        {
#if !UNITY_EDITOR
            var keys=Keyboard.current;if(keys==null)return;
            if(keys.f6Key.wasPressedThisFrame)Toggle();if(Visible&&keys.f7Key.wasPressedThisFrame)Cycle();
            if(Visible&&keys.f9Key.wasPressedThisFrame)RequestCapture();if(Visible&&keys.f10Key.wasPressedThisFrame)Freeze();
#endif
        }
        void LateUpdate(){Tick(Time.deltaTime);}
        public void Tick(float dt)
        {
            if(!Visible||dt<=0)return;
            if(Frozen&&texture!=null){if(saveRequested)Capture();return;}
            try{
                if(playerType==null)playerType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("ENT_Player")).FirstOrDefault(t=>t!=null);
                localPlayer=playerType?.GetField("playerObject",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(null) as Component;
                if(localPlayer==null||!localPlayer.gameObject.activeInHierarchy){Release();status="Waiting for local player";return;}
                // A new model selection is reflected on the next frame without retaining an old rig.
                string current=SelectionKey?.Invoke();if(current==ModelCatalog.Original){Release();status="Original multiplayer model selected (PMX mirror off)";return;}if(rig!=null&&selected!=current)Release();
                if(rig==null){retry-=dt;if(retry>0)return;retry=2;Create();selected=current;}
                actor.transform.SetPositionAndRotation(localPlayer.transform.position,localPlayer.transform.rotation);
                var hands=playerType.GetField("hands")?.GetValue(localPlayer) as Array;
                int state(Transform target,int index){if(hands==null||hands.Length<=index||hands.GetValue(index)==null)return 0;object hand=hands.GetValue(index);Type t=hand.GetType();var hold=t.GetMethod("GetHoldWorldPosition",Type.EmptyTypes);if(hold!=null&&hold.ReturnType==typeof(Vector3))target.position=(Vector3)hold.Invoke(hand,null);else if(t.GetField("holdPosition")?.GetValue(hand) is Vector3 point)target.position=point;return Convert.ToInt32(t.GetField("interactState")?.GetValue(hand)??0);}
                int ls=state(left,0),rs=state(right,1);rig.MirrorCrouched=playerType.GetMethod("IsCrouching",Type.EmptyTypes)?.Invoke(localPlayer,null) as bool?;rig.MirrorTick(dt,left,right,ls,rs);
                Plugin.Instance?.DriveMirrorGaze(rig,dt);
                float h=ModelHeight?.Invoke()??2.1f;Vector3 center=actor.transform.position+Vector3.up*((FeetOffset?.Invoke()??-1.07f)+h*0.5f);
                Vector3 front=rig.transform.rotation*Quaternion.Euler(0,-180,0)*Vector3.forward;
                Vector3 direction=View==0?front:View==1?-front:View==2?-Vector3.Cross(Vector3.up,front):Vector3.Cross(Vector3.up,front);
                camera.orthographicSize=h*0.65f;camera.transform.position=center+direction*(h*3);camera.transform.LookAt(center);
                // Freeze this exact pose into ordinary meshes. A manually rendered camera must
                // not depend on skinning/motion-vector history from the main camera.
                if(snapshotBlock==null)snapshotBlock=new MaterialPropertyBlock();
                for(int i=0;i<skinned.Length;i++){skinned[i].BakeMesh(snapshots[i],false);skinned[i].GetSharedMaterials(snapshotMaterials);snapshotRenderers[i].SetSharedMaterials(snapshotMaterials);for(int m=0;m<snapshotMaterials.Count;m++){snapshotBlock.Clear();skinned[i].GetPropertyBlock(snapshotBlock,m);snapshotRenderers[i].SetPropertyBlock(snapshotBlock,m);}}
                Vector3 saved=actor.transform.position,offset=new Vector3(0,10000,0);
                try{
                    actor.transform.position=saved+offset;camera.transform.position+=offset;
                    foreach(var r in renderers)if(r!=null&&!(r is SkinnedMeshRenderer))r.forceRenderingOff=false;
                    var prior=RenderTexture.active;try{RenderTexture.active=texture;GL.Clear(true,true,camera.backgroundColor);}finally{RenderTexture.active=prior;}
                    camera.Render();
                }
                finally{actor.transform.position=saved;foreach(var r in renderers)if(r!=null)r.forceRenderingOff=true;}
                status=rig.CurrentState;
                if(saveRequested)Capture();
            }catch(Exception e){Release();status="Mirror unavailable: "+e.GetBaseException().Message;retry=2;}
        }
        public Func<string> SelectionKey;
        void Capture()
        {
            saveRequested=false;if(texture==null||string.IsNullOrEmpty(CaptureFolder))return;
            System.IO.Directory.CreateDirectory(CaptureFolder);string stem=System.IO.Path.Combine(CaptureFolder,"mirror-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            var old=RenderTexture.active;var shot=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            try{RenderTexture.active=texture;shot.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);shot.Apply();System.IO.File.WriteAllBytes(stem+".png",shot.EncodeToPNG());System.IO.File.WriteAllText(stem+".txt","Direct RenderTexture capture; view="+viewNames[View]+"; frozen="+Frozen+"; rawSpeed="+(rig?.NativeObservedSpeed??0).ToString("F3")+"; poseSpeed="+(rig?.NativePoseSpeed??0).ToString("F3")+"; "+status);}
            finally{RenderTexture.active=old;Destroy(shot);}
        }
        void Create()
        {
            if(CreateAvatar==null)throw new InvalidOperationException("Mirror factory unavailable");
            actor=new GameObject("WKPmx_LocalMirror_VisualOnly");actor.transform.SetPositionAndRotation(localPlayer.transform.position,localPlayer.transform.rotation);
            rig=CreateAvatar(actor);if(rig==null)throw new InvalidOperationException("Avatar unavailable");rig.enabled=false;
            foreach(var t in actor.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            skinned=actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);snapshots=new Mesh[skinned.Length];snapshotRenderers=new MeshRenderer[skinned.Length];
            for(int i=0;i<skinned.Length;i++){
                var go=new GameObject("Mirror_PoseSnapshot");go.layer=31;go.transform.SetParent(skinned[i].transform,false);
                snapshots[i]=new Mesh();go.AddComponent<MeshFilter>().sharedMesh=snapshots[i];snapshotRenderers[i]=go.AddComponent<MeshRenderer>();
                snapshotRenderers[i].motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
                snapshotRenderers[i].shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            renderers=actor.GetComponentsInChildren<Renderer>(true);foreach(var r in renderers)r.forceRenderingOff=true;
            left=new GameObject("Mirror_LeftTarget").transform;left.SetParent(actor.transform,false);right=new GameObject("Mirror_RightTarget").transform;right.SetParent(actor.transform,false);
            texture=new RenderTexture(512,640,16,RenderTextureFormat.ARGB32){name="WKPmx_CornerView",antiAliasing=1};texture.Create();
            cameraObject=new GameObject("WKPmx_CornerCamera");camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.targetTexture=texture;
            camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0.055f,0.065f,0.085f,1);camera.orthographic=true;camera.nearClipPlane=0.01f;camera.farClipPlane=30;camera.allowHDR=false;camera.allowMSAA=false;camera.useOcclusionCulling=false;
        }
        void OnGUI()
        {
            if(!Visible)return;float w=Mathf.Clamp(Screen.width*0.22f,220,350),h=w*1.25f;Rect box=new Rect(Screen.width-w-15,15,w,h+66);
            GUI.Box(box,"");GUI.Label(new Rect(box.x+8,box.y+4,w-16,22),"Rossi mirror - "+viewNames[View]+(Frozen?" [frozen]":"")+" | F6 / F7");
            if(texture!=null)GUI.DrawTexture(new Rect(box.x+6,box.y+27,w-12,h-5),texture,ScaleMode.ScaleToFit,false);
            GUI.Label(new Rect(box.x+8,box.y+h+23,w-16,38),"Local input / remote animation\n"+status);
        }
        public void Release()
        {
            if(camera!=null)camera.targetTexture=null;if(texture!=null){texture.Release();Destroy(texture);}if(cameraObject!=null)Destroy(cameraObject);if(actor!=null)Destroy(actor);
            if(snapshots!=null)foreach(var mesh in snapshots)if(mesh!=null)Destroy(mesh);snapshots=null;skinned=null;snapshotRenderers=null;
            texture=null;camera=null;cameraObject=actor=null;rig=null;renderers=null;
        }
        void OnDisable(){Release();}void OnDestroy(){SceneManager.activeSceneChanged-=SceneChanged;Release();}
    }
}

