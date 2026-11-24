using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace WKPmxAvatar
{
    [BepInPlugin(Guid, "联机扩展-洛茜模组", "1.0.0")]
    [BepInDependency("shenxl.MultiPlayerMod")]
    public sealed class Plugin : BaseUnityPlugin
    {
        const string Guid = "local.whiteknuckle.pmxavatar";
        internal static Plugin Instance;
        internal ConfigEntry<float> Stride, Lift, GroundOffset, Yaw;
        internal ConfigEntry<int> GroundMask;
        internal ConfigEntry<bool> UnlitTextures;
        internal ConfigEntry<bool> EndfieldEnabled, SceneLighting;
        internal EndfieldResources RenderingResources;
        ConfigEntry<float> ambientFloor, minimumVisibility, exposure;internal ConfigEntry<float> CharacterBrightness;
        internal ConfigEntry<float> BodyTurnThreshold;
        internal ConfigEntry<bool> NativeRemoteMotion,BackwardDodge;
        internal ConfigEntry<float> BackwardDodgeSpeed;
        internal ConfigEntry<bool> AlignBodyGrab;
        internal ConfigEntry<bool> FirstPersonHands,PawPrompts;internal ConfigEntry<float> FirstPersonScale,FirstPersonBrightness;bool skinHookWarned;
        internal ConfigEntry<bool> SecondaryMotionEnabled,SkirtClearanceEnabled;

#if WKPMX_DEV
        internal ConfigEntry<bool> EyeTrackingEnabled;
        internal WKPmxAvatar.Gaze.EyeGazeWorld GazeWorld;
        readonly Dictionary<object,ulong> gazeIds = new Dictionary<object,ulong>();
#endif

        ConfigEntry<bool> featureEnabled;
        ConfigEntry<string> model, overrides;
        ConfigEntry<float> height;
        readonly Dictionary<string, AvatarAsset> assets = new Dictionary<string, AvatarAsset>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly List<AvatarMotion> instances = new List<AvatarMotion>();
        readonly Dictionary<object, GameObject> containers = new Dictionary<object, GameObject>();
        MultiplayerHooks multiplayer;
        string baseFolder;
        ConfigEntry<string> personalModel;
        internal ModelCatalog Models {get;private set;}=new ModelCatalog();
        AvatarSelectionSync selectionSync;bool selectionWarned,refreshing,refreshRequested;
        readonly HashSet<object> pendingPeers = new HashSet<object>();
        readonly Dictionary<object, ulong> containerIds = new Dictionary<object, ulong>();
        readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> retiredContainers =
            new System.Runtime.CompilerServices.ConditionalWeakTable<object, object>();
        internal AvatarSelectionSync SelectionSync => selectionSync;
        internal string PersonalModelId=>personalModel?.Value;
        internal bool RossiSelected=>Models.Find(PersonalModelId)!=null&&ModelProfiles.SupportsRossiOptions(PersonalModelId);
        internal object[] RemoteContainers=>containers.Where(p=>p.Value!=null).Select(p=>p.Key).ToArray();
        internal string MotionFolder => Path.Combine(baseFolder,"Motions");
        internal float ModelHeight => height.Value;
        internal string RemoteMotionStatus=>"Remote originals: "+instances.Count(r=>r!=null&&r.NativeReady&&r.gameObject.activeInHierarchy)+" active; fallback: "+instances.Count(r=>r!=null&&!r.NativeReady&&r.gameObject.activeInHierarchy);
        void Awake()
        {
            SkirtClearanceEnabled=Config.Bind("Physics","SkirtClearanceEnabled",true,"Fixed-length pose-driven skirt animation with local leg and boot clearance.");
            SecondaryMotionEnabled=Config.Bind("Physics","SecondaryMotionEnabled",true,"Enable cosmetic hair, ear, cloak and tail motion. Skirt clearance is controlled separately.");
            Instance = this; baseFolder = Path.GetDirectoryName(Info.Location);

#if WKPMX_DEV
            EyeTrackingEnabled=Config.Bind("Avatar.Rossi","EyeTrackingEnabled",true,"Display nearest visible player gaze on Rossi avatars in this client. No additional network messages.");
            GazeWorld=RossiGazeSources.CreateWorld(this);
#endif

            featureEnabled = Config.Bind("General", "Enabled", true, "Replace remote player visuals. Restart after changes.");
            AlignBodyGrab=Config.Bind("Interaction","AlignBodyGrab",true,"Fit remote body grab triggers to visible bones; blocking collisions and network movement unchanged. Disable to restore original trigger shape.");
            BackwardDodgeSpeed=Config.Bind("Animation","BackwardDodgeMinimumSpeed",4.0f,new ConfigDescription("Backward dodge entry speed for a 2.1-unit avatar. Below this use ordinary backward gait. Exit at 85% of this speed; scales with model height.",new AcceptableValueRange<float>(2,12)));
            model = Config.Bind("Model", "PmxPath", "Models/avatar.pmx", "PMX file, absolute or relative to this plugin folder.");
            overrides = Config.Bind("Model", "PlayerOverrides", "", "Optional SteamID=relative/path.pmx;SteamID=path.pmx. Local viewer mapping only.");
            Models.Scan(Path.Combine(baseFolder,"Models"),AbsoluteModel(model.Value));
            string initialChoice=Models.ForPath(AbsoluteModel(model.Value))?.Id??ModelCatalog.Original;
            personalModel=Config.Bind("Model","PersonalAvatarId",initialChoice,"Your chosen lobby avatar identity. Only installed local models are loaded; no remote paths or downloads.");
            if(!ModelCatalog.ValidId(personalModel.Value))personalModel.Value=initialChoice;
            height = Config.Bind("Model", "Height", 2.1f, new ConfigDescription("Model height in game units, including accessories.", new AcceptableValueRange<float>(0.5f, 4f)));
            GroundOffset = Config.Bind("Model", "FeetOffset", -1.125f, "Feet relative to RemotePlayer root; adjust if floating/sinking.");
            Yaw = Config.Bind("Model", "Yaw", 180f, "Model yaw relative to player. MMD normally faces -Z.");
            Stride = Config.Bind("Animation", "Stride", 1.35f, new ConfigDescription("Distance per full walk cycle.", new AcceptableValueRange<float>(0.3f, 4f)));
            Lift = Config.Bind("Animation", "FootLift", 0.16f, new ConfigDescription("Swing foot lift.", new AcceptableValueRange<float>(0f, 0.5f)));
            GroundMask = Config.Bind("Animation", "GroundLayerMask", -1, "Physics layer bitmask used for foot placement.");
            UnlitTextures = Config.Bind("Rendering", "UnlitTextures", true, "Display base textures without scene lighting; useful with White Knuckle's custom lighting.");
            #if WKPMX_DEV
            EndfieldEnabled=Config.Bind("EndfieldExperimental","Enabled",false,"Rossi r7 materials. Base textures remain available in F8.");
#else
            EndfieldEnabled=Config.Bind("EndfieldExperimental","Enabled",true,"Rossi r7 materials; requires locally supplied rendering assets. Base textures are the safe fallback.");
#endif
            SceneLighting=Config.Bind("EndfieldExperimental","SceneLighting",true,"Use scene lights, directional ambient, and reflection probes for experimental materials.");
            ambientFloor=Config.Bind("EndfieldExperimental","AmbientFloor",0.12f,new ConfigDescription("Minimum scene ambient irradiance before material shading.",new AcceptableValueRange<float>(0,1)));
            minimumVisibility=Config.Bind("EndfieldExperimental","MinimumVisibility",0.28f,new ConfigDescription("Cool global character visibility floor in scene-lighting mode, independent of exposure. Zero disables this fill.",new AcceptableValueRange<float>(0,1)));
            exposure=Config.Bind("EndfieldExperimental","Exposure",1f,new ConfigDescription("Experimental lighting multiplier.",new AcceptableValueRange<float>(0.1f,4)));
            CharacterBrightness=Config.Bind("EndfieldExperimental","Brightness",0.8f,new ConfigDescription("Overall experimental avatar brightness; preserves original source textures.",new AcceptableValueRange<float>(0.4f,1.2f)));
            CharacterBrightness.SettingChanged+=(_,__)=>SyncAppearance();
            ItemGripProfiles.Load(Path.Combine(baseFolder,"item-grips.json"),Warn);
            RenderingResources=new EndfieldResources(Path.Combine(baseFolder,"Rendering"));
            EndfieldEnabled.SettingChanged+=(_,__)=>SyncAppearance(); SceneLighting.SettingChanged+=(_,__)=>SyncAppearance();
            ambientFloor.SettingChanged+=(_,__)=>SyncAppearance(); minimumVisibility.SettingChanged+=(_,__)=>SyncAppearance(); exposure.SettingChanged+=(_,__)=>SyncAppearance(); SyncAppearance();
            BodyTurnThreshold = Config.Bind("Animation", "BodyTurnThreshold", 50f, new ConfigDescription("Standing look yaw before the feet/body follow.", new AcceptableValueRange<float>(15, 90)));
            NativeRemoteMotion=Config.Bind("Animation","NativeRemoteMotion",true,"Use Rossi native clips for remote locomotion and air states. Falls back to procedural motion for incompatible rigs; restart after changing.");
            BackwardDodge=Config.Bind("Animation","BackwardDodge",true,"Use recovered Rossi backward dodge poses for grounded back travel; visual only and interruptible.");
            Config.Bind("FirstPerson","HandsEnabled",false,"Legacy hand preference retained; WIP hands use Avatar.Rossi.HandsEnabled.");
            var legacyPaw=Config.Bind("FirstPerson","PawPrompts",false,"Legacy preference; migrated to Avatar.Rossi.PawPrompts.");
            FirstPersonHands=HandAppearanceOptions.Bind(Config);
            PawPrompts=Config.Bind("Avatar.Rossi","PawPrompts",legacyPaw.Value,"Rossi-specific paw interaction cursor. Active only while Rossi is selected.");
            FirstPersonScale=Config.Bind("FirstPerson","HandScale",0.85f,new ConfigDescription("Hand sprite size relative to the original pose.",new AcceptableValueRange<float>(0.55f,1.15f)));
            FirstPersonBrightness=Config.Bind("FirstPerson","Brightness",0.7f,new ConfigDescription("Multiply only custom hand art; retains game status tint and alpha.",new AcceptableValueRange<float>(0.35f,1f)));
            var skin=gameObject.AddComponent<FirstPersonSkin>();skin.Folder=Path.Combine(baseFolder,"Hands");skin.HandsEnabled=()=>Models.Find(PersonalModelId)!=null&&ModelProfiles.Enabled(PersonalModelId,FirstPersonHands.Value);skin.PawEnabled=()=>Models.Find(PersonalModelId)!=null&&ModelProfiles.Enabled(PersonalModelId,PawPrompts.Value);skin.HandScale=()=>FirstPersonScale.Value;skin.HandBrightness=()=>FirstPersonBrightness.Value;skin.Warn=Warn;

#if WKPMX_DEV
            gameObject.AddComponent<AvatarMenu>();
            var performanceLogging=Config.Bind("Diagnostics","PerformanceLogging",true,"Test build: record frame/memory/cache totals to the local BepInEx log every 30 seconds. No forced GC or network transmission.");
            var monitor=gameObject.AddComponent<PerformanceMonitor>();monitor.Enabled=()=>performanceLogging.Value;monitor.ActiveAvatars=()=>instances.Count(r=>r!=null&&r.gameObject.activeInHierarchy);monitor.Log=InfoLog;
#endif

            selectionSync=gameObject.AddComponent<AvatarSelectionSync>();selectionSync.Owner=this;

#if WKPMX_DEV
            var corner=gameObject.AddComponent<CornerAvatarView>();
            corner.CreateAvatar=CreateMirror;corner.ModelHeight=()=>ModelHeight;corner.FeetOffset=()=>GroundOffset.Value;
            corner.SelectionKey=()=>SelectedModel;
            corner.CaptureFolder=Path.Combine(baseFolder,"Diagnostics");
            if(Environment.GetCommandLineArgs().Any(a=>a=="-wkpmx-check"))StartCoroutine(CheckMaterialStartup());
#endif

            multiplayer = new MultiplayerHooks(Guid, Logger.LogInfo);
            try { multiplayer.Install(selectionSync); }
            catch (Exception ex) { Error("R001", "Multiplayer capability discovery failed.", ex); }
        }
        
#if WKPMX_DEV
System.Collections.IEnumerator CheckMaterialStartup()
        {
            yield return null;
            var asset=GetAsset(model.Value);
            Logger.LogInfo("WKPMX material self-check: "+(asset==null?"PMX load failed":AppearanceStatus));
        }
#endif

        static void AfterPose(object __instance, object[] __args)
        {
            if (Instance == null || __args == null || __args.Length == 0) return;
            try
            {
                var root = __instance.GetType().GetProperty("PlayerObject")?.GetValue(__instance) as GameObject;
                var rig = root != null ? root.GetComponentInChildren<AvatarMotion>(true) : null;
                if (rig == null) return;
                rig.ReadNetworkPose(__args[0]);
            }
            catch (Exception ex) { Instance.Warn("Optional pose state unavailable: " + ex.Message); }
        }
        static void AfterHandPose(object __instance,string __0,ref Sprite __result)
        {try{if(FirstPersonSkin.Current!=null)__result=FirstPersonSkin.Current.ReplacePose(__instance,__0,__result);}catch(Exception e){Instance?.SkinWarning(e);}}
        static void BeforeHandCursor(object __instance,ref Sprite __0)
        {try{if(FirstPersonSkin.Current!=null)__0=FirstPersonSkin.Current.ReplaceCursor(__instance,__0);}catch(Exception e){Instance?.SkinWarning(e);}}
        void SkinWarning(Exception e){if(!skinHookWarned){skinHookWarned=true;Warn("Optional first person replacement skipped: "+e.Message);}}
        static void AfterInitialize(object __instance, bool __result)
        {
            if (!__result || Instance == null || !Instance.featureEnabled.Value) return;
            try { Instance.Attach(__instance); }
            catch (Exception ex) { Instance.Error("R002","Avatar attachment failed; original visuals retained.",ex); }
        }
        void Attach(object container)
        {
            var type = container.GetType();
            var root = (GameObject)type.GetProperty("PlayerObject").GetValue(container);
            if (root == null) return;
            ulong id = Convert.ToUInt64(type.GetProperty("PlayerId").GetValue(container));
            var remoteChoice = selectionSync?.ForAttach(container, id);
            if (containers.TryGetValue(container, out var oldRoot) && oldRoot != null && oldRoot != root)
                RemoveVisual(oldRoot.GetComponentInChildren<AvatarMotion>(true));
            containers[container] = root;
            containerIds[container] = id;

#if WKPMX_DEV
            gazeIds[container]=id;
#endif

            if (root.GetComponentInChildren<AvatarMotion>(true) != null) return;
            instances.RemoveAll(rig => rig == null);
            string selected = model.Value;
            string manual=null;
            foreach (var entry in overrides.Value.Split(';'))
            {
                int eq = entry.IndexOf('=');
                if (eq > 0 && ulong.TryParse(entry.Substring(0, eq).Trim(), out var key) && key == id)
                    manual = entry.Substring(eq + 1).Trim();
            }
            selected=Models.Resolve(remoteChoice,selected,manual);
            if(selected==null)return; // The sender explicitly chose the MP mod's original model.
            var asset = GetAsset(selected);
            if (asset == null) return;
            var items = new List<Transform>();
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().Name != "RemoteHand") continue;
                var item = component.GetType().GetField("_item", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(component) as GameObject;
                if (item != null) items.Add(item.transform);
            }
            var original = root.GetComponentsInChildren<Renderer>(true).Where(r => !items.Any(item => r.transform.IsChildOf(item)) &&
                (r is MeshRenderer || r is SkinnedMeshRenderer) && !r.GetComponentsInParent<Component>(true).Any(c => c != null &&
                    (c.GetType().Name == "RemoteTag" || c.GetType().Namespace == "TMPro"))).ToArray();
            var go = Instantiate(asset.Template, root.transform, false);
            try
            {
                var motion = go.GetComponent<AvatarMotion>();
                motion.Initialize(root.transform, original);
                CrouchSync.ApplyToRig(container, motion);
                if(NativeRemoteMotion.Value)motion.EnableNativeMotion(MotionFolder,ModelHeight);

#if WKPMX_DEV
                if(ModelProfiles.SupportsRossiOptions(Models.ForPath(AbsoluteModel(selected))?.Id))GazeWorld?.Attach(go,motion.GazeHead,motion.Bones,root.transform,root.transform,id,()=>motion.enabled,false);
#endif

                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.layer;
                go.SetActive(true); instances.Add(motion);
#if WKPMX_DEV
                Logger.LogInfo("Attached PMX to remote player " + id);
#else
                Logger.LogInfo("PMX attached (session avatar " + (++sessionAvatar) + ").");
#endif
            }
            catch { Destroy(go); throw; }
        }
        AvatarAsset GetAsset(string selected)
        {
            string path = Path.GetFullPath(Path.IsPathRooted(selected) ? selected : Path.Combine(baseFolder, selected));
            if (failed.Contains(path)) return null;
            if (!assets.TryGetValue(path, out var asset))
            {
                try
                {
                    asset = AvatarAsset.Load(path, height.Value); assets.Add(path, asset);
                    foreach (string warning in asset.Warnings) Warn(warning);
                }
                catch (Exception ex) { failed.Add(path); Error("R003","PMX load failed; check the selected local model and textures.",ex); return null; }
            }
            return asset;
        }
        internal string ModelFolder => Path.Combine(baseFolder, "Models");
        internal string SelectedModel => PersonalModelId==ModelCatalog.Original?ModelCatalog.Original:Models.Find(PersonalModelId)?.Path??model.Value;
        internal void RetryLoads() {failed.Clear();RenderingResources?.Retry();}
        void SyncAppearance()
        {
            AppearanceOptions.Enabled=EndfieldEnabled.Value;AppearanceOptions.SceneLighting=SceneLighting.Value;
            AppearanceOptions.AmbientFloor=ambientFloor.Value;AppearanceOptions.MinimumVisibility=minimumVisibility.Value;AppearanceOptions.Exposure=exposure.Value;AppearanceOptions.Brightness=CharacterBrightness.Value;
        }
        internal string AppearanceStatus=>RenderingResources?.Status??"Base textures";
        internal void SetEndfield(bool enabled) {EndfieldEnabled.Value=enabled;Config.Save();}
        internal void SetSceneLighting(bool enabled) {SceneLighting.Value=enabled;Config.Save();}
        internal void SetUnlit(bool value)
        {
            UnlitTextures.Value = value; Config.Save();
            foreach (var asset in assets.Values) asset.ApplyRendering(value);
        }
#if !WKPMX_DEV
        [System.Diagnostics.Conditional("WKPMX_DEV")]
#endif
        internal void InfoLog(string message) => Logger.LogInfo(message);
        internal void SetFirstPersonHands(bool value){FirstPersonHands.Value=value;Config.Save();FirstPersonSkin.Current?.Sync();}
        internal void SetPawPrompts(bool value){PawPrompts.Value=value;Config.Save();FirstPersonSkin.Current?.Sync();}

#if WKPMX_DEV
        internal void SetEyeTracking(bool value){EyeTrackingEnabled.Value=value;Config.Save();}
#endif

        
#if WKPMX_DEV
internal void GetGazeRemotes(List<WKPmxAvatar.Gaze.GazeRemoteRoot> target)
        {
            target.Clear();foreach(var pair in containers){if(pair.Value==null||!gazeIds.TryGetValue(pair.Key,out var id))continue;var rig=pair.Value.GetComponentInChildren<AvatarMotion>(true);target.Add(new WKPmxAvatar.Gaze.GazeRemoteRoot(id,pair.Value.transform,rig!=null?rig.GazeHead:null));}
        }
#endif

        internal void SelectModel(string path)
        {
            if (GetAsset(path) == null) throw new InvalidOperationException("Cannot load model. See BepInEx/LogOutput.log.");
            model.Value = path; Config.Save();
            RescanModels();var entry=Models.ForPath(AbsoluteModel(path));if(entry!=null)ChoosePersonalModel(entry.Id);
            RequestRefreshPlayers();
        }
        string AbsoluteModel(string path)=>Path.GetFullPath(Path.IsPathRooted(path)?path:Path.Combine(baseFolder,path));
        internal void RescanModels(){Models.Scan(ModelFolder,AbsoluteModel(model.Value));RetryLoads();selectionSync?.Invalidate();RequestRefreshPlayers();}
        internal void ChoosePersonalModel(string id)
        {
            var entry=Models.Find(id);if(entry==null)throw new InvalidOperationException("Rescan and choose an installed model first.");
            if(id!=ModelCatalog.Original&&GetAsset(entry.Path)==null)throw new InvalidOperationException("Model loading failed. Check the model files and textures.");
            personalModel.Value=id;Config.Save();selectionSync?.PublishSelection();FirstPersonSkin.Current?.Sync();
        }
        internal void SelectionWarning(string message){if(selectionWarned)return;selectionWarned=true;Warn("Avatar selection: "+message);}
        internal bool IsRetiredContainer(object container) => retiredContainers.TryGetValue(container, out _);

        internal void RetireContainer(object container)
        {
            retiredContainers.GetValue(container, value => value);
            CrouchSync.Forget(container);
            pendingPeers.Remove(container);
            if (containers.TryGetValue(container, out var root) && root != null)
                RemoveVisual(root.GetComponentInChildren<AvatarMotion>(true));
            containers.Remove(container);
            containerIds.Remove(container);
#if WKPMX_DEV
            gazeIds.Remove(container);
#endif
        }

        internal void RemoveRemoteMember(ulong id)
        {
            foreach (var pair in containerIds.Where(pair => pair.Value == id).ToArray())
                RetireContainer(pair.Key);
        }

        internal void ClearRemoteSession()
        {
            refreshRequested = false;
            foreach (var container in containers.Keys.ToArray()) RetireContainer(container);
            pendingPeers.Clear();
        }

        void RemoveVisual(AvatarMotion rig)
        {
            if (rig == null) return;
            rig.Restore();
            rig.gameObject.SetActive(false);
            instances.Remove(rig);
            Destroy(rig.gameObject);
        }

        internal void RefreshSelection(object container)
        {
            if (!containers.ContainsKey(container)) return;
            pendingPeers.Add(container);
            StartRefresh();
        }

        void RequestRefreshPlayers()
        {
            refreshRequested = true;
            StartRefresh();
        }

        void StartRefresh()
        {
            if (refreshing) return;
            refreshing = true;
            StartCoroutine(RefreshPlayers());
        }

        System.Collections.IEnumerator RefreshPlayers()
        {
            try
            {
                // Coalesce multiple changes received in the same frame, then read the
                // last validated selection when reattaching just the affected peers.
                yield return null;
                while (refreshRequested || pendingPeers.Count != 0)
                {
                    var batch = (refreshRequested ? containers.Keys.AsEnumerable() : pendingPeers).ToArray();
                    refreshRequested = false;
                    pendingPeers.Clear();
                    foreach (var container in batch)
                    {
                        if (!containers.TryGetValue(container, out var root)) continue;
                        if (root == null) { RetireContainer(container); continue; }
                        RemoveVisual(root.GetComponentInChildren<AvatarMotion>(true));
                    }
                    yield return null;
                    foreach (var container in batch)
                    {
                        if (!containers.TryGetValue(container, out var root) || root == null) continue;
                        // A newer choice that arrived during destruction is used by Attach.
                        pendingPeers.Remove(container);
                        try { if (featureEnabled.Value) Attach(container); }
                        catch (Exception ex) { Error("R004", "Model switch failed; rescan local models and retry.", ex); }
                    }
                }
            }
            finally { refreshing = false; }
        }

        
#if WKPMX_DEV
internal void PopulatePreview(GameObject root)
        {
            if(SelectedModel==ModelCatalog.Original)throw new InvalidOperationException("Default multiplayer model selected; PMX preview is off.");
            var asset = GetAsset(SelectedModel);
            if (asset == null) throw new InvalidOperationException("Cannot load model. See BepInEx/LogOutput.log.");
            var go = Instantiate(asset.Template, root.transform, false);
            try
            {
                var motion = go.GetComponent<AvatarMotion>();
                motion.Initialize(root.transform, Array.Empty<Renderer>());
                go.SetActive(true);
            }
            catch { Destroy(go); throw; }
        }
#endif

#if !WKPMX_DEV
        int sessionAvatar;
        readonly HashSet<string> diagnosticCodes=new HashSet<string>();
#endif
        internal void Warn(string message)
        {
#if WKPMX_DEV
            Logger.LogWarning(message);
#else
            var diagnostic=RuntimeDiagnostics.Classify(message);
            if(diagnosticCodes.Add(diagnostic.Code))Logger.LogWarning(diagnostic.Message+" ["+diagnostic.Code+"]");
#endif
        }
        void Error(string code,string safeMessage,Exception exception)
        {
#if WKPMX_DEV
            Logger.LogError(safeMessage+"\n"+exception);
#else
            if(diagnosticCodes.Add(code))Logger.LogError(safeMessage+" ["+code+"]");
#endif
        }
        
#if WKPMX_DEV
internal void DriveMirrorGaze(AvatarMotion rig,float dt)
        {
            if(!RossiSelected||rig==null)return;
            var binding=rig.GetComponent<WKPmxAvatar.Gaze.EyeAvatarBinding>()??GazeWorld?.AttachLocal(rig.gameObject,rig.GazeHead,rig.Bones);
            binding?.TickManual(dt);
        }
#endif

        
#if WKPMX_DEV
AvatarMotion CreateMirror(GameObject root)
        {
            PopulatePreview(root);var rig=root.GetComponentInChildren<AvatarMotion>(true);
            rig.enabled=false;rig.EnableNativeMotion(MotionFolder,ModelHeight);if(RossiSelected)GazeWorld?.AttachLocal(rig.gameObject,rig.GazeHead,rig.Bones);return rig;
        }
#endif

        void OnDestroy()
        {

#if WKPMX_DEV
            GazeWorld?.Dispose();
#endif

            multiplayer?.Remove();
            selectionSync?.ResetSession();
            foreach (var rig in instances) if (rig != null) { rig.Restore(); Destroy(rig.gameObject); }
            foreach (var asset in assets.Values) asset.Dispose();
            RenderingResources?.Dispose(); AppearanceOptions.Enabled=false;
            if (Instance == this) Instance = null;
        }
    }
}

















