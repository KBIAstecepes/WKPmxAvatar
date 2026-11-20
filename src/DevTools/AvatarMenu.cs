using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using HarmonyLib;

namespace WKPmxAvatar
{
    // The preview is a local visual object, with no networking, collider or gameplay components.
    [DefaultExecutionOrder(11000)]
    public sealed class AvatarMenu : MonoBehaviour
    {
        bool open;
        int demo;
        Rect window = new Rect(30, 40, 560, 650);
        Vector2 scroll;
        Vector2 panelScroll;
        string[] models = Array.Empty<string>();
        string status = "Load a level, then create a preview. F8 opens/closes this menu.";
        GameObject preview;
        Vector3 anchor;
        Quaternion facing;
        float angle, clock;
        bool switching;
        GameObject previewProp;
        bool originalMode;
        bool showAdvanced,heavyLanding;
        bool showFirstPerson;
        bool freeHang;float heldPropScale=0.38f;ContactSolver interaction;
        ContinuousRoutePreview route;bool routeGuides=true,extendedRoute=true,nativeStopPreview;
        readonly System.Reflection.MethodInfo updateCursor = AccessTools.Method(AccessTools.TypeByName("InputManager"), "UpdateCursorVisibility");
        readonly System.Reflection.PropertyInfo gameCursor = AccessTools.Property(AccessTools.TypeByName("CL_GameManager"), "showCursor");
        readonly System.Reflection.MethodInfo consoleVisible = AccessTools.Method(AccessTools.TypeByName("CommandConsole"), "IsConsoleVisible");
        void Start()
        {
            Scan();
            SceneManager.activeSceneChanged += SceneChanged;
        }
        void SceneChanged(Scene from, Scene to) => RemovePreview();
        void Scan()
        {
            try
            {
                var folder = Plugin.Instance.ModelFolder;
                models = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.pmx", SearchOption.AllDirectories) : Array.Empty<string>();
                Array.Sort(models, StringComparer.OrdinalIgnoreCase);
                Plugin.Instance.RetryLoads();
            }
            catch (Exception ex) { status = ex.Message; }
        }
        void SetOpen(bool value)
        {
            if (open == value) return;
            open = value;
            if (open) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            else
            {
                // Query the current game/console state, rather than restoring a snapshot from before F8.
                updateCursor?.Invoke(null, null);
                bool wantsCursor = (gameCursor?.GetValue(null) is bool show && show) ||
                    (consoleVisible?.Invoke(null, null) is bool console && console);
                Cursor.lockState = wantsCursor ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = wantsCursor;
            }
        }
        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame) SetOpen(!open);
            if (preview == null) return;
            if(route!=null&&(route.Active||route.Finished)){
                try{route.Tick(Time.deltaTime);if(previewProp!=null)previewProp.SetActive(route.Holding);}
                catch(Exception e){StopRoute();PlayOriginal(7);status="Route stopped: "+e.Message;Plugin.Instance.Warn(status);}
                return;
            }
            clock += Time.deltaTime;
            var rig = preview.GetComponentInChildren<AvatarMotion>(true);
            if(originalMode)
            {
                preview.transform.SetPositionAndRotation(anchor+Vector3.up*(demo==4?(freeHang?0.5f:0.2f):0),facing*Quaternion.Euler(0,angle,0));
                if(rig!=null){rig.GetComponent<MotionPlayer>().BaseFeetOffset=Plugin.Instance.GroundOffset.Value;rig.transform.localRotation=Quaternion.Euler(0,Plugin.Instance.Yaw.Value,0);}
                if(previewProp!=null)previewProp.SetActive(demo==5);
                return;
            }
            Vector3 displacement = Vector3.zero;
            if (demo == 4) displacement.y = 0.35f + Mathf.Sin(clock * 1.4f) * 0.25f;
            preview.transform.position = anchor + displacement;
            preview.transform.rotation = facing * Quaternion.Euler(0, angle, 0);
            if (rig != null)
            {
                rig.IsPreview = true;
                rig.PreviewGrounded = demo != 4;
                rig.PreviewAction = demo == 4 ? 3 : demo == 5 ? 4 : 0;
            }
            if (previewProp != null) previewProp.SetActive(demo == 5);
        }
        void LateUpdate()
        {
            if (open) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
        void OnGUI()
        {
            if (open) window = GUILayout.Window(839271, window, Draw, "Rossi 0.10.2 - F8");
        }
        void Draw(int id)
        {
            panelScroll=GUILayout.BeginScrollView(panelScroll,GUILayout.Height(Mathf.Clamp(Screen.height-140,400,900)));
            GUILayout.Label("Selected: " + Path.GetFileName(Plugin.Instance.SelectedModel));
            GUILayout.Label("Models: " + Plugin.Instance.ModelFolder);
            GUILayout.Label("The menu does not pause gameplay. Open it while standing safely.");
            if (GUILayout.Button("Rescan Models folder")) Scan();
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(100));
            foreach (string path in models)
            {
                if (GUILayout.Button(Path.GetFileName(Path.GetDirectoryName(path)) + "/" + Path.GetFileName(path)) && !switching)
                {
                    try
                    {
                        switching = true;
                        Plugin.Instance.SelectModel(path);
                        if (preview != null) CreatePreview();
                        status = "Selected and saved. Existing remote avatars updated (per-player overrides still apply).";
                    }
                    catch (Exception ex) { status = ex.Message; }
                    finally { switching = false; }
                }
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Create / move preview here")) CreatePreview();
            if (GUILayout.Button("Remove preview")) RemovePreview();
            GUILayout.EndHorizontal();
            GUILayout.Label("Local preview: Idle / Walk / Run / Jump use Endfield clips.");
            GUILayout.Label(Plugin.Instance.RemoteMotionStatus);
            var corner=Plugin.Instance.GetComponent<CornerAvatarView>();
            GUILayout.BeginHorizontal();if(GUILayout.Button("F6: local mirror"))corner?.Toggle();if(GUILayout.Button("F7: mirror view"))corner?.Cycle();GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();if(GUILayout.Button("F9: capture raw mirror"))corner?.RequestCapture();if(GUILayout.Button("F10: freeze mirror"))corner?.Freeze();GUILayout.EndHorizontal();
            showFirstPerson=GUILayout.Toggle(showFirstPerson,"First person hands and paw prompts (expand)");
            if(showFirstPerson&&Plugin.Instance.RossiSelected){
                var plugin=Plugin.Instance;
                bool hands=GUILayout.Toggle(plugin.FirstPersonHands.Value,"Rossi first-person hands (WIP)");if(hands!=plugin.FirstPersonHands.Value)plugin.SetFirstPersonHands(hands);
                bool paw=GUILayout.Toggle(plugin.PawPrompts.Value,"Paw interaction prompts");if(paw!=plugin.PawPrompts.Value)plugin.SetPawPrompts(paw);
                GUILayout.Label("First person hand size: "+plugin.FirstPersonScale.Value.ToString("F2"));float size=Mathf.Round(GUILayout.HorizontalSlider(plugin.FirstPersonScale.Value,0.55f,1.15f)*40)/40;if(Mathf.Abs(size-plugin.FirstPersonScale.Value)>0.01f){plugin.FirstPersonScale.Value=size;plugin.Config.Save();}
                GUILayout.Label("First person lighting: Game controlled");
                GUILayout.Label(FirstPersonSkin.Current?.Status??"First person module unavailable");
            }
            GUILayout.Label("Experimental character brightness: "+Plugin.Instance.CharacterBrightness.Value.ToString("F2"));float characterBrightness=Mathf.Round(GUILayout.HorizontalSlider(Plugin.Instance.CharacterBrightness.Value,0.4f,1.2f)*40)/40;if(Mathf.Abs(characterBrightness-Plugin.Instance.CharacterBrightness.Value)>0.01f){Plugin.Instance.CharacterBrightness.Value=characterBrightness;Plugin.Instance.Config.Save();}
            bool backwardDodge=GUILayout.Toggle(Plugin.Instance.BackwardDodge.Value,"Rossi original backward dodge (experimental)");if(backwardDodge!=Plugin.Instance.BackwardDodge.Value){Plugin.Instance.BackwardDodge.Value=backwardDodge;Plugin.Instance.Config.Save();}
            GUILayout.Label("Backward dodge minimum speed: "+Plugin.Instance.BackwardDodgeSpeed.Value.ToString("F1"));float dodgeSpeed=GUILayout.HorizontalSlider(Plugin.Instance.BackwardDodgeSpeed.Value,2,12);if(Mathf.Abs(dodgeSpeed-Plugin.Instance.BackwardDodgeSpeed.Value)>0.02f){Plugin.Instance.BackwardDodgeSpeed.Value=dodgeSpeed;Plugin.Instance.Config.Save();}
            bool bodyGrab=GUILayout.Toggle(Plugin.Instance.AlignBodyGrab.Value,"Align body grab volume to Rossi");if(bodyGrab!=Plugin.Instance.AlignBodyGrab.Value){Plugin.Instance.AlignBodyGrab.Value=bodyGrab;Plugin.Instance.Config.Save();}
            string[] actions={"Idle","Walk","Run","Jump / land","Climb pose","Hold hammer"};
            for(int row=0;row<2;row++){GUILayout.BeginHorizontal();for(int column=0;column<3;column++){int action=row*3+column;var old=GUI.backgroundColor;if(action==demo)GUI.backgroundColor=new Color(0.65f,0.9f,1);if(GUILayout.Button(actions[action]))SelectAction(action);GUI.backgroundColor=old;}GUILayout.EndHorizontal();}
            extendedRoute=GUILayout.Toggle(extendedRoute,"Extended route (sprint, braking and restarts; applies on replay)");
            nativeStopPreview=GUILayout.Toggle(nativeStopPreview,"Native stop clips (comparison only; off by default; replay to apply)");
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Start / replay route"))StartRoute();
            if(GUILayout.Button(route!=null&&route.Paused?"Resume route":"Pause route")&&route!=null)route.Paused=!route.Paused;
            if(GUILayout.Button("Stop route")){StopRoute();SelectAction(0);}
            GUILayout.EndHorizontal();
            if(route!=null){GUILayout.Label(route.GaitStatus);GUILayout.Label(route.NativeStatus);GUILayout.Label(route.Description+"  "+route.Elapsed.ToString("F1")+" / "+route.Duration.ToString("F1")+"s; speed "+route.PlanarSpeed.ToString("F2")+" m/s");GUILayout.BeginHorizontal();foreach(float speed in new[]{0.25f,0.5f,1f})if(GUILayout.Button(speed.ToString("0.##")+"x"))route.Speed=speed;GUILayout.EndHorizontal();}
            bool nextGuides=GUILayout.Toggle(routeGuides,"Show route guides (visual only; choose an open area)");if(nextGuides!=routeGuides){routeGuides=nextGuides;route?.ShowGuides(routeGuides);}
            GUILayout.Label("Preview facing: " + Mathf.RoundToInt(angle));
            angle = GUILayout.HorizontalSlider(angle, -180, 180);
            showAdvanced=GUILayout.Toggle(showAdvanced,"Advanced: interactions, feet and materials");
            if(showAdvanced){
            bool nextHang=GUILayout.Toggle(freeHang,"Free hang (off: wall support)");if(nextHang!=freeHang){freeHang=nextHang;if(route==null&&demo==4&&interaction!=null)interaction.Climb(freeHang);}
            GUILayout.Label("Held prop visual scale: "+heldPropScale.ToString("F2"));
            float nextScale=GUILayout.HorizontalSlider(heldPropScale,0.2f,1.25f);if(Mathf.Abs(nextScale-heldPropScale)>0.001f){heldPropScale=nextScale;if(previewProp!=null){previewProp.transform.localScale=Vector3.one*heldPropScale;if(route!=null?route.Holding:demo==5)try{StartHammer();}catch(Exception e){status="Grip recalibration failed: "+e.Message;}}}
            heavyLanding=GUILayout.Toggle(heavyLanding,"Use heavy landing for the next jump");
            GUILayout.Label("Feet offset: " + Plugin.Instance.GroundOffset.Value.ToString("F2"));
            Plugin.Instance.GroundOffset.Value = GUILayout.HorizontalSlider(Plugin.Instance.GroundOffset.Value, -2, 1);
            bool unlit = GUILayout.Toggle(Plugin.Instance.UnlitTextures.Value, "Base texture mode (experimental materials off)");
            if (unlit != Plugin.Instance.UnlitTextures.Value) Plugin.Instance.SetUnlit(unlit);
            bool endfield=GUILayout.Toggle(Plugin.Instance.EndfieldEnabled.Value,"Experimental Rossi materials");
            if(endfield!=Plugin.Instance.EndfieldEnabled.Value)Plugin.Instance.SetEndfield(endfield);
            bool sceneLighting=GUILayout.Toggle(Plugin.Instance.SceneLighting.Value,"Use scene lights (experimental)");
            if(sceneLighting!=Plugin.Instance.SceneLighting.Value)Plugin.Instance.SetSceneLighting(sceneLighting);
            GUILayout.Label(Plugin.Instance.AppearanceStatus);
            }
            var rig = preview != null ? preview.GetComponentInChildren<AvatarMotion>(true) : null;
            var original=rig!=null?rig.GetComponent<MotionPlayer>():null;
            GUILayout.Label("Animation state: " + (route!=null&&(route.Active||route.Finished)?route.Description:interaction!=null&&interaction.Active?interaction.Description:originalMode&&original!=null?original.Description:rig!=null?rig.CurrentState:"No preview"));
            GUILayout.Label(status);
            if (GUILayout.Button("Close (F8)")) SetOpen(false);
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, 560, 22));
        }
        void CreatePreview()
        {
            var cam = Camera.main;
            if (cam == null) { status = "No main camera. Enter a playable level first."; return; }
            GameObject next = null;
            try
            {
                Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                Vector3 feet = cam.transform.position + forward * 3.5f;
                if (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out var hit, 10,
                    Plugin.Instance.GroundMask.Value, QueryTriggerInteraction.Ignore)) feet = hit.point;
                else feet.y = cam.transform.position.y - 1.7f;
                Vector3 location = feet - Vector3.up * Plugin.Instance.GroundOffset.Value;
                Quaternion rotation = Quaternion.LookRotation(-forward, Vector3.up);
                next = new GameObject("WKPmxAvatar_LocalPreview");
                next.transform.SetPositionAndRotation(location, rotation);
                Plugin.Instance.PopulatePreview(next);
                var nextRig=next.GetComponentInChildren<AvatarMotion>(true);ContactSolver nextInteraction=null;
                if(nextRig!=null){try{nextInteraction=nextRig.gameObject.AddComponent<ContactSolver>();nextInteraction.Initialize(nextRig.Bones,nextRig.GrantSources,nextRig.GrantFlags,nextRig.GrantWeights);}catch(Exception e){if(nextInteraction!=null)Destroy(nextInteraction);nextInteraction=null;Plugin.Instance.Warn("Interaction preview unavailable: "+e.Message);}}
                RemovePreview(); preview = next; anchor = location; facing = rotation; clock = 0;
                interaction=nextInteraction;
                var motion = preview.GetComponentInChildren<AvatarMotion>(true);
                if (motion != null) motion.IsPreview = true;
                if(demo<4)PlayOriginal(demo==0?7:demo==1?10:demo==2?19:164);
                else SelectAction(demo);
                status = "Local preview ready. Jump / land plays once, then returns to idle.";
            }
            catch (Exception ex)
            {
                if (next != null) Destroy(next);
                status = "Preview failed: " + ex.Message;
                Plugin.Instance.Warn(status);
            }
        }
        void CreateProp()
        {
            if (preview == null) return;
            if(previewProp!=null){StartHammer();return;}
            var rig = preview.GetComponentInChildren<AvatarMotion>(true);
            if (rig?.RightWrist == null) return;
            try
            {
                var util = AccessTools.TypeByName("WKMPMod.Util.MPUtil");
                var getPrefab = AccessTools.Method(util, "TryGetItemPrefab");
                object[] args = { "Item_Hammer", null };
                if (getPrefab == null || !(getPrefab.Invoke(null, args) is bool found) || !found)
                { status = "Hammer prefab unavailable in this level; hand pose only."; return; }
                var prefab = args[1] as GameObject ?? (args[1] as Component)?.gameObject;
                if (prefab == null) { status = "Hammer prefab unavailable in this level; hand pose only."; return; }
                previewProp = new GameObject("WKPmxAvatar_PreviewHammer_VisualOnly");
                previewProp.transform.SetParent(rig.transform, false);
                previewProp.transform.localScale=Vector3.one*heldPropScale;
                int count = 0;
                // Copy only mesh and material data. Do not instantiate game item behaviours or colliders.
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    var sourceRenderer = filter.GetComponent<MeshRenderer>();
                    if (filter.sharedMesh == null || sourceRenderer == null) continue;
                    var visual = new GameObject(filter.name);
                    visual.transform.SetParent(previewProp.transform, false);
                    visual.transform.localPosition = prefab.transform.InverseTransformPoint(filter.transform.position);
                    visual.transform.localRotation = Quaternion.Inverse(prefab.transform.rotation) * filter.transform.rotation;
                    Vector3 sourceScale = prefab.transform.lossyScale;
                    Vector3 childScale = filter.transform.lossyScale;
                    visual.transform.localScale = new Vector3(childScale.x / Mathf.Max(0.0001f, sourceScale.x), childScale.y / Mathf.Max(0.0001f, sourceScale.y), childScale.z / Mathf.Max(0.0001f, sourceScale.z));
                    visual.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    visual.AddComponent<MeshRenderer>().sharedMaterials = sourceRenderer.sharedMaterials;
                    count++;
                }
                StartHammer();status = "Hammer handle aligned; fingers close around the grip. Local preview only.";
            }
            catch (Exception ex) { status = "Hammer preview unavailable: " + ex.Message; }
        }
        void StartHammer()
        {
            if(interaction==null||previewProp==null){status="Interaction rig unavailable.";return;}
            MeshFilter filter=null;float length=0;foreach(var candidate in previewProp.GetComponentsInChildren<MeshFilter>(true))if(candidate.sharedMesh!=null&&candidate.sharedMesh.bounds.size.z>length){filter=candidate;length=candidate.sharedMesh.bounds.size.z;}
            if(filter==null)throw new InvalidOperationException("No hammer mesh");
            ContactSolver.HammerLandmarks(filter,previewProp.transform,out var grip,out var shaft,out var strike);interaction.HoldHammer(previewProp.transform,grip,shaft,strike);
        }
        void PlayOriginal(int index)
        {
            if(preview==null){status="Create a preview first.";return;}
            var rig=preview.GetComponentInChildren<AvatarMotion>(true);
            if(rig==null){status="Preview rig unavailable.";return;}
            try
            {
                interaction?.Stop();
                var original=rig.GetComponent<MotionPlayer>()??rig.gameObject.AddComponent<MotionPlayer>();
                original.BaseFeetOffset=Plugin.Instance.GroundOffset.Value;
                original.Play(Plugin.Instance.MotionFolder,index,rig.Bones,Plugin.Instance.ModelHeight,heavyLanding);
                rig.enabled=false;originalMode=true;clock=0;
                status="Original motion loaded. This test affects only the local preview.";
            }
            catch(Exception e){status="Original motion unavailable: "+e.Message;}
        }
        void SelectAction(int action)
        {
            try{
            StopRoute();
            if(preview==null){status="Create a preview first.";return;}
            if(action<4)PlayOriginal(action==0?7:action==1?10:action==2?19:164);
            else if(interaction!=null){PlayOriginal(7);if(action==5)CreateProp();else interaction.Climb(freeHang);}
            else {status="This model has no supported interaction rig.";return;}
            demo=action;clock=0;
            }catch(Exception e){status="Interaction preview unavailable: "+e.Message;Plugin.Instance.Warn(status);}
        }
        void StopOriginal()
        {
            if(preview!=null){var rig=preview.GetComponentInChildren<AvatarMotion>(true);if(rig!=null){rig.GetComponent<MotionPlayer>()?.Stop();rig.enabled=true;}}
            originalMode=false;
        }
        void StartRoute()
        {
            if(preview==null)CreatePreview();if(preview==null||interaction==null){status="Create a supported Rossi preview first.";return;}
            try{StopRoute();var rig=preview.GetComponentInChildren<AvatarMotion>(true);PlayOriginal(7);var playback=rig.GetComponent<MotionPlayer>();
                route=new ContinuousRoutePreview(preview.transform,rig.Bones,playback,interaction,Plugin.Instance.MotionFolder,Plugin.Instance.ModelHeight,anchor,facing,CreateProp,extendedRoute:extendedRoute,nativeStops:nativeStopPreview);
                route.Start();route.ShowGuides(routeGuides);status="Scripted local route. Guides have no collision; player and network are unchanged.";
            }catch(Exception e){StopRoute();PlayOriginal(7);status="Route unavailable: "+e.Message;}
        }
        void StopRoute(){route?.Stop();route=null;}
        void RemovePreview() { StopRoute();originalMode=false;interaction=null; if (preview != null) Destroy(preview); preview = null; previewProp = null; }
        void OnDestroy()
        {
            SceneManager.activeSceneChanged -= SceneChanged;
            RemovePreview();
            if (open) SetOpen(false);
        }
    }
}



















