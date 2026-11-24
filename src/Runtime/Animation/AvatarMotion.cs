using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WKPmxAvatar
{
    [DefaultExecutionOrder(10000)]
    public sealed class AvatarMotion : MonoBehaviour
    {
        public Transform[] Bones;
        public int[] GrantSources, GrantFlags;
        public float[] GrantWeights;
        RuntimeSecondaryMotion secondary;
        AuthoredGripLayer acceptedGrip;
        Transform player, leftHand, rightHand;
        Renderer[] originals;
        bool[] originalForceOff;
        Quaternion[] restRot;
        Vector3[] restPos;
        readonly Dictionary<string, Transform> lookup = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        readonly RaycastHit[] hits = new RaycastHit[32];
        Limb leftLeg, rightLeg, leftArm, rightArm;
        Vector3 lastPosition, velocity;
        float phase, blend;
        bool ready, reset = true, restored;
        Transform spine, head;
        internal Transform GazeHead => head;
        internal Transform GazeOwner => player;
        Component leftHandComponent, rightHandComponent;
        float bodyYaw, landing, airTime;
        bool wasGrounded;
        int leftState, rightState;
        RemoteNativeMotion native;MotionPlayer nativePlayer;bool usingNative;
        ContactSolver contactPose;ObservedCrouchPose crouchPose;bool? observedCrouch;
        float observedCrouchUntil;
        VisualHeldItem leftItem=new VisualHeldItem(),rightItem=new VisualHeldItem();
        BodyGrabAlignment bodyGrab;
        NaturalLookPose lookPose;
        public float SpineYawTwist=>lookPose?.SpineTwist??0;public float NeckYawTwist=>lookPose?.NeckTwist??0;
#if WKPMX_DEV
        public bool? MirrorCrouched;
#endif
        public float CrouchWeight=>crouchPose?.Weight??0;
        public void ReadCrouch(bool crouched, float validUntil)
        {
            observedCrouch = crouched;
            observedCrouchUntil = validUntil;
        }

        public void ClearCrouch() => observedCrouch = null;
        public string NativeStatus=>native?.Status??"Procedural remote motion";
        public bool NativeReady=>native!=null;
        public float NativePoseSpeed=>native?.PoseSpeed??0;public float NativeObservedSpeed=>native?.ObservedSpeed??0;
        
#if WKPMX_DEV
internal void MirrorTick(float dt,Transform left,Transform right,int leftInteraction,int rightInteraction)
        {leftHand=left;rightHand=right;leftState=leftInteraction;rightState=rightInteraction;Animate(dt);}
#endif

        internal void EnableNativeMotion(string folder,float modelHeight)
        {
            try{
                contactPose=gameObject.AddComponent<ContactSolver>();contactPose.Initialize(Bones,GrantSources,GrantFlags,GrantWeights);
#if WKPMX_DEV
                contactPose.ManualDrive=true;
#endif

                try{using(var stream=typeof(AuthoredGripLayer).Assembly.GetManifestResourceStream("WKPmxAvatar.AuthoredGrips.json")){if(stream==null)throw new System.IO.InvalidDataException("Accepted grip resource missing");using(var reader=new System.IO.StreamReader(stream)){var definitions=AuthoredGripLayer.Load(reader.ReadToEnd());AuthoredAssemblyGrip[] assemblies=null;try{using(var rootStream=typeof(AuthoredGripLayer).Assembly.GetManifestResourceStream("WKPmxAvatar.AuthoredAssemblyGrips.json")){if(rootStream==null)throw new System.IO.InvalidDataException("Assembly grip resource missing");using(var rootReader=new System.IO.StreamReader(rootStream))assemblies=AuthoredAssemblyGrips.Load(rootReader.ReadToEnd());}}catch(Exception rootError){Plugin.Instance.Warn("Assembly grips unavailable; retaining existing authored grips: "+rootError.Message);}try{acceptedGrip=new AuthoredGripLayer(Bones,definitions,assemblies);}catch(Exception bindError)when(assemblies!=null&&assemblies.Length>0){Plugin.Instance.Warn("Assembly bind mismatch; retaining existing authored grips: "+bindError.Message);acceptedGrip=new AuthoredGripLayer(Bones,definitions);}}}}catch(Exception e){acceptedGrip=null;Plugin.Instance.Warn("Accepted disk grip unavailable; retaining generic grip: "+e.Message);}
                nativePlayer=gameObject.AddComponent<MotionPlayer>();
                nativePlayer.BaseFeetOffset=Plugin.Instance.GroundOffset.Value;
                native=new RemoteNativeMotion(nativePlayer,Bones,folder,modelHeight);
            }catch(Exception e){native=null;if(contactPose!=null){Destroy(contactPose);contactPose=null;}if(nativePlayer!=null){nativePlayer.Stop();Destroy(nativePlayer);}Plugin.Instance.Warn("Native remote unavailable; using procedural animation: "+e.Message);}
        }
#if WKPMX_DEV
        public bool IsPreview;
        public bool PreviewGrounded = true;
        public int PreviewAction;
#else
        const bool IsPreview=false;
#endif
        public string CurrentState { get; private set; } = "Idle";
        public Transform RightWrist => rightArm?.End;
        public Vector3 RightGripPosition => rightArm != null ? rightArm.End.position : transform.position;
        public void ReadNetworkPose(object data)
        {
            if (data == null) return;
            var type = data.GetType();
            object left = type.GetField("LeftHand")?.GetValue(data), right = type.GetField("RightHand")?.GetValue(data);
            if (left != null) leftState = Convert.ToInt32(left.GetType().GetField("interactState").GetValue(left));
            if (right != null) rightState = Convert.ToInt32(right.GetType().GetField("interactState").GetValue(right));
            if (type.GetField("IsTeleport")?.GetValue(data) is bool teleport && teleport) reset = true;
        }

        sealed class Limb
        {
            internal Transform Upper, Lower, End;
            internal Vector3 RestFoot, Planted, SwingStart;
            internal bool Stance;
            internal Limb(Transform a, Transform b, Transform c, Transform root)
            { Upper = a; Lower = b; End = c; RestFoot = root.InverseTransformPoint(c.position); }
        }
        Transform Find(params string[] names)
        {
            foreach (string name in names) if (lookup.TryGetValue(name, out var t)) return t;
            return null;
        }
        Limb Chain(Transform a, Transform b, Transform c)
        {
            if (a == null || b == null || c == null || !b.IsChildOf(a) || !c.IsChildOf(b)) return null;
            if (Vector3.Distance(a.position, b.position) < 0.001f || Vector3.Distance(b.position, c.position) < 0.001f) return null;
            return new Limb(a, b, c, transform);
        }
        internal void Initialize(Transform root, Renderer[] old)
        {
            player = root; originals = old;
            originalForceOff = new bool[old.Length];
            for (int i = 0; i < old.Length; i++) originalForceOff[i] = old[i].forceRenderingOff;
            restRot = new Quaternion[Bones.Length]; restPos = new Vector3[Bones.Length];
            for (int i = 0; i < Bones.Length; i++)
            {
                lookup[Bones[i].name] = Bones[i];
                restRot[i] = Bones[i].localRotation; restPos[i] = Bones[i].localPosition;
            }
            leftLeg = Chain(Find("左足", "左足D", "Left leg", "leg_L"), Find("左ひざ", "左ひざD", "Left knee", "knee_L"), Find("左足首", "左足首D", "Left ankle", "ankle_L"));
            rightLeg = Chain(Find("右足", "右足D", "Right leg", "leg_R"), Find("右ひざ", "右ひざD", "Right knee", "knee_R"), Find("右足首", "右足首D", "Right ankle", "ankle_R"));
            leftArm = Chain(Find("左腕", "Left arm", "arm_L"), Find("左ひじ", "Left elbow", "elbow_L"), Find("左手首", "Left wrist", "wrist_L"));
            rightArm = Chain(Find("右腕", "Right arm", "arm_R"), Find("右ひじ", "Right elbow", "elbow_R"), Find("右手首", "Right wrist", "wrist_R"));
            spine = Find("上半身", "Upper body", "spine");
            head = Find("頭", "Head", "head");
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c.GetType().Name != "RemoteHand") continue;
                var field = c.GetType().GetField("handType");
                if (field == null) continue;
                if (Convert.ToByte(field.GetValue(c)) == 0) { leftHand = c.transform; leftHandComponent = c; }
                else { rightHand = c.transform; rightHandComponent = c; }
            }
            if (leftLeg == null || rightLeg == null) throw new InvalidOperationException("Required MMD leg bones not found; keeping original avatar. Check bone names.");
            if (leftArm == null || rightArm == null) Plugin.Instance.Warn("Arm bones missing: arm IK disabled for this model.");
            crouchPose=new ObservedCrouchPose(Bones,Plugin.Instance.ModelHeight);
            lookPose=new NaturalLookPose(transform,Bones,Plugin.Instance.Yaw.Value);
            if(!IsPreview)bodyGrab=new BodyGrabAlignment(root,Bones);
            secondary=RuntimeSecondaryMotion.TryCreate(transform,Bones,Plugin.Instance.Warn);
            Plugin.Instance.InfoLog(secondary!=null?"WKPMX_PHYSICS bound: 14 secondary chains / 71 bones + stateless skirt clearance; Physics.SecondaryMotionEnabled="+Plugin.Instance.SecondaryMotionEnabled.Value:"WKPMX_PHYSICS skipped: model rig does not match Rossi profile");ready = true;
        }
        void OnEnable() { reset = true; restored = false; secondary?.RequestReset(); }
        void OnDisable() { secondary?.RequestReset(); Restore(); }
        void OnDestroy() { Restore(); }
        internal void Restore()
        {
            acceptedGrip?.Reset();
            if (restored || originals == null || originalForceOff == null) return;
            for (int i = 0; i < originals.Length; i++) if (originals[i] != null) originals[i].forceRenderingOff = originalForceOff[i];
            leftItem?.Dispose();rightItem?.Dispose();bodyGrab?.Dispose();restored = true;
        }
        void LateUpdate()
        {
            if (observedCrouch.HasValue && Time.unscaledTime >= observedCrouchUntil) ClearCrouch();
            if (!ready || player == null || Plugin.Instance == null) return;
            try { Animate(Time.deltaTime); }
            catch (Exception ex)
            {
                Plugin.Instance.Warn("Animation disabled, original model restored: " + ex.Message);
                Restore(); gameObject.SetActive(false);
            }
        }
        void Animate(float dt)
        {
            var config = Plugin.Instance;
            if (dt <= 0) { secondary?.RequestReset(); return; }
            acceptedGrip?.Restore();
            Vector3 delta = player.position - lastPosition;
            bool fresh = reset || delta.sqrMagnitude > 9f || dt > 0.25f;
            if (fresh)
            {
                velocity = Vector3.zero; phase = 0; blend = 0;
                leftLeg.Stance = rightLeg.Stance = false;
                reset = false;
                bodyYaw = player.eulerAngles.y; landing = 0; airTime = 0;
                lookPose?.Reset();
                secondary?.RequestReset();acceptedGrip?.Reset();
            }
            else velocity = Vector3.Lerp(velocity, delta / dt, 1 - Mathf.Exp(-12 * dt));
            lastPosition = player.position;
            Vector3 parentScale=player.lossyScale;float uniform=Mathf.Sqrt(Mathf.Abs(parentScale.x*parentScale.z));
            transform.localScale=ObservedCrouchPose.UniformCompensation(parentScale);
            transform.position=player.position+Vector3.up*(config.GroundOffset.Value*uniform);
            float speed = Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude;
            float lookYaw = player.eulerAngles.y;
            float moveYaw = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg;
            float desiredBody = MotionMath.BodyHeading(bodyYaw, lookYaw, moveYaw, speed, config.BodyTurnThreshold.Value);
            if(native!=null&&!IsPreview)desiredBody=native.BackDodgeWeight>0?lookYaw:MotionMath.BodyHeading(bodyYaw,lookYaw,moveYaw,0,speed>0.2f?Mathf.Min(20,config.BodyTurnThreshold.Value):config.BodyTurnThreshold.Value);
            bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, desiredBody, (speed > 0.2f ? 220 : 150) * dt);
            transform.rotation = Quaternion.Euler(0, bodyYaw + config.Yaw.Value, 0);
            for (int i = 0; i < Bones.Length; i++)
            { Bones[i].localRotation = restRot[i]; Bones[i].localPosition = restPos[i]; }
            if (fresh)
            {
                leftLeg.SwingStart = transform.TransformPoint(leftLeg.RestFoot);
                rightLeg.SwingStart = transform.TransformPoint(rightLeg.RestFoot);
            }
#if WKPMX_DEV
            bool grounded = IsPreview ? PreviewGrounded : Ground(transform.position, 0.14f, out _);
#else
            bool grounded = Ground(transform.position, 0.14f, out _);
#endif
            // Geometric estimate: the existing protocol has no explicit grounded/climbing animation flags.
#if WKPMX_DEV
            bool hanging = IsPreview ? PreviewAction == 3 : leftState == 4 || rightState == 4;
            bool crouched=MirrorCrouched??observedCrouch??(Mathf.Abs(parentScale.y)<uniform*0.8f);
#else
            bool hanging = leftState == 4 || rightState == 4;
            bool crouched=observedCrouch??(Mathf.Abs(parentScale.y)<uniform*0.8f);
#endif
            if (hanging) grounded = false;
            if (!grounded) airTime += dt;
            if (grounded && !wasGrounded && airTime > 0.08f && !fresh) landing = Mathf.Clamp01(0.4f + Mathf.Abs(velocity.y) * 0.12f);
            if (grounded) airTime = 0;
            wasGrounded = grounded;
            landing = Mathf.MoveTowards(landing, 0, dt * 3);
            CurrentState = hanging ? "Climbing" : !grounded ? (velocity.y > 0.3f ? "Jump / rising" : "Falling") : landing > 0.05f ? "Landing" : speed > 3 ? "Running" : speed > 0.15f ? "Walking" : "Idle";
            float goal = grounded && speed > 0.12f ? Mathf.Clamp01(speed / 1.2f) : 0;
            blend = Mathf.MoveTowards(blend, goal, dt * 6);
            phase = (phase + Mathf.Min(speed, 8) / config.Stride.Value * dt) % 1;
            // Upper-body yaw preserves the look direction while the lower body turns with a delay.
            float twist = Mathf.Clamp(Mathf.DeltaAngle(bodyYaw, lookYaw), -65, 65);
            float bob = grounded ? -landing * 0.16f - Mathf.Abs(Mathf.Sin(phase * Mathf.PI * 2)) * blend * 0.025f : 0;
            transform.position += Vector3.up * bob;
            Step(leftLeg, phase, grounded, hanging);
            Step(rightLeg, (phase + 0.5f) % 1, grounded, hanging);
            usingNative=false;
            if(native!=null&&!IsPreview){
                try{
                    nativePlayer.BaseFeetOffset=config.GroundOffset.Value;
                    Vector3 rawVelocity=fresh?Vector3.zero:delta/dt;
                    // Classify movement against the synchronized look heading. Body turn
                    // smoothing is presentation only and must not turn forward travel into strafing.
                    Vector3 forward=Quaternion.Euler(0,lookYaw,0)*Vector3.forward;
                    float moveAngle=speed>0.15f?Vector3.SignedAngle(forward,Vector3.ProjectOnPlane(velocity,Vector3.up),Vector3.up):0;
                    float turn=Mathf.Clamp(moveAngle/180,-1,1);
                    // Forward native loops cannot represent sustained backwards travel correctly.
                    // Keep the existing backwards gait until a suitable native branch is recovered.
                    // Pose selection must not alternate idle/sprint when a fixed-step source
                    // stays still for one render frame, then advances on the next. Distance
                    // remains unfiltered so phase still follows the actual path exactly.
                    float nativeOffset=config.GroundOffset.Value*uniform/Mathf.Max(0.001f,Mathf.Abs(parentScale.y));nativePlayer.BaseFeetOffset=nativeOffset;
                    native.UseBackwardDodge=config.BackwardDodge.Value&&!crouched;
                    native.BackwardDodgeSpeed=config.BackwardDodgeSpeed.Value;
                    native.BodyLookOffset=Mathf.DeltaAngle(bodyYaw,lookYaw);
                    usingNative=native.Sample(dt,fresh?Vector3.zero:delta,rawVelocity.y,grounded,hanging,fresh,forward,turn,speed);
                    if(usingNative){
                        CurrentState=native.Status;
                    }
                }catch(Exception e){native=null;nativePlayer.Stop();Plugin.Instance.Warn("Native remote failed; continuing procedural motion: "+e.Message);}
            }
            crouchPose?.Apply(crouched&&!hanging,dt,Quaternion.Euler(0,bodyYaw,0)*Vector3.forward,uniform);
            if(crouchPose!=null&&crouchPose.Weight>0.01f)CurrentState="Crouching / "+CurrentState;
            // Preserve the accepted native dodge pose, including its head/chest motion.
            if((native?.BackDodgeWeight??0)>0)lookPose?.Reset();
            else lookPose?.Apply(lookYaw,speed,dt);
            Arm(leftArm, leftHand, leftHandComponent, leftState, -1, grounded, hanging, speed);
            Arm(rightArm, rightHand, rightHandComponent, rightState, 1, grounded, hanging, speed);
            ApplyGrants(usingNative);
            if(contactPose!=null&&!IsPreview)contactPose.ApplyContacts(dt,leftHand,rightHand,leftState==4,rightState==4,velocity.y,Quaternion.Euler(0,bodyYaw,0)*Vector3.forward);
            if(contactPose!=null&&!IsPreview){var lp=leftItem.Profile(leftHandComponent);var rp=rightItem.Profile(rightHandComponent);bool la=leftItem.ItemActive(leftHandComponent),ra=rightItem.ItemActive(rightHandComponent);string leftName=lp?.item??leftItem.ActiveItemName(leftHandComponent),rightName=rp?.item??rightItem.ActiveItemName(rightHandComponent);bool al=acceptedGrip?.Matches(leftName,la)??false,ar=acceptedGrip?.Matches(rightName,ra)??false;
                contactPose.ApplyGrip(leftState,rightState,la&&lp==null&&!al,ra&&rp==null&&!ar);if(la&&!al)contactPose.ApplyItemGrip(true,lp);if(ra&&!ar)contactPose.ApplyItemGrip(false,rp);
                // Capture the fresh animation/contact pose before adding the accepted hand layer.
                native?.Capture();acceptedGrip?.Tick(dt,al?leftName:null,ar?rightName:null);
                leftItem.Sync(leftHandComponent,leftHand,contactPose.ItemContact(true,lp),contactPose.ItemFrame(true),al?acceptedGrip?.ItemPose(true):null,al?acceptedGrip?.AssemblyPose(true):null);rightItem.Sync(rightHandComponent,rightHand,contactPose.ItemContact(false,rp),contactPose.ItemFrame(false),ar?acceptedGrip?.ItemPose(false):null,ar?acceptedGrip?.AssemblyPose(false):null);
            }else native?.Capture();
            lookPose?.Measure();
            bodyGrab?.Apply(config.AlignBodyGrab.Value);
            secondary?.Tick(dt,config.SecondaryMotionEnabled.Value,config.SkirtClearanceEnabled.Value);
            foreach (var r in originals) if (r != null) r.forceRenderingOff = true;
        }
        void ApplyGrants(bool clearBaked=false)
        {
            // MMD D bones and twist helpers commonly copy local source-bone deltas.
            // This does not evaluate full MMD deform layers, local-axis constraints or physics.
            if (GrantSources == null) return;
            for (int i = 0; i < Bones.Length; i++)
            {
                int source = GrantSources[i];
                if (source < 0 || source >= Bones.Length) continue;
                if(clearBaked){if((GrantFlags[i]&0x100)!=0)Bones[i].localRotation=restRot[i];if((GrantFlags[i]&0x200)!=0)Bones[i].localPosition=restPos[i];}
                float weight = GrantWeights[i];
                if ((GrantFlags[i] & 0x100) != 0)
                {
                    Quaternion delta = Quaternion.Inverse(restRot[source]) * Bones[source].localRotation;
                    Bones[i].localRotation *= Quaternion.SlerpUnclamped(Quaternion.identity, delta, weight);
                }
                if ((GrantFlags[i] & 0x200) != 0)
                    Bones[i].localPosition += (Bones[source].localPosition - restPos[source]) * weight;
            }
        }
        bool Ground(Vector3 point, float depth, out Vector3 result)
        {
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 0.35f, Vector3.down, hits,
                depth + 0.35f, Plugin.Instance.GroundMask.Value, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue; result = point;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.collider == null || hit.transform.IsChildOf(player) || hit.normal.y < 0.4f) continue;
                // Do not treat another player's body/hands as walkable terrain.
                bool remote = false;
                foreach (var c in hit.collider.GetComponentsInParent<Component>())
                    if (c != null && (c.GetType().Name == "RemotePlayer" || c.GetType().Name == "RemoteHand")) { remote = true; break; }
                if (!remote && hit.distance < nearest) { nearest = hit.distance; result = hit.point; }
            }
            return nearest != float.MaxValue;
        }
        void Step(Limb leg, float cycle, bool grounded, bool hanging)
        {
            Vector3 rest = transform.TransformPoint(leg.RestFoot);
            // Ground contact is at the sole, while IK controls the ankle above the sole.
            float ankleHeight = Mathf.Max(0.03f, leg.RestFoot.y * Mathf.Abs(transform.lossyScale.y));
            Vector3 sole = rest - Vector3.up * ankleHeight;
            Vector3 target = rest;
            if (!grounded)
            {
                leg.Stance = false;
                target += Vector3.up * (hanging ? 0.28f : 0.12f) - player.forward * 0.12f;
            }
            else if (blend < 0.05f)
            {
                leg.Stance = false;
                if (Ground(sole, 0.5f, out var hit)) target = hit + Vector3.up * ankleHeight;
            }
            else
            {
                Vector3 travel = Vector3.ProjectOnPlane(velocity, Vector3.up);
                travel = travel.sqrMagnitude > 0.01f ? travel.normalized : player.forward;
                float stride = Mathf.Min(Plugin.Instance.Stride.Value * 0.25f, 0.4f) * blend;
                bool stance = cycle < 0.5f;
                if (stance)
                {
                    if (!leg.Stance || Vector3.Distance(leg.Planted, rest) > 1.2f)
                    {
                        leg.Planted = rest + travel * stride;
                        if (Ground(leg.Planted - Vector3.up * ankleHeight, 0.6f, out var hit)) leg.Planted = hit + Vector3.up * ankleHeight;
                    }
                    target = leg.Planted; leg.SwingStart = target;
                }
                else
                {
                    if (leg.Stance) leg.SwingStart = leg.Planted;
                    float t = (cycle - 0.5f) * 2;
                    Vector3 end = rest + travel * stride;
                    if (Ground(end - Vector3.up * ankleHeight, 0.6f, out var hit)) end = hit + Vector3.up * ankleHeight;
                    target = Vector3.Lerp(leg.SwingStart, end, t * t * (3 - 2 * t));
                    target += Vector3.up * (Mathf.Sin(t * Mathf.PI) * Plugin.Instance.Lift.Value * blend);
                }
                leg.Stance = stance;
            }
            Vector3 bodyForward = Quaternion.Euler(0, bodyYaw, 0) * Vector3.forward;
            if (hanging)
            {
                target += bodyForward * 0.18f;
                if (!IsPreview)
                {
                    int count = Physics.RaycastNonAlloc(rest + Vector3.up * 0.2f, bodyForward, hits, 0.85f,
                        Plugin.Instance.GroundMask.Value, QueryTriggerInteraction.Ignore);
                    float nearest = float.MaxValue;
                    for (int i = 0; i < count; i++)
                    {
                        var hit = hits[i];
                        if (hit.collider == null || hit.transform.IsChildOf(player) || Mathf.Abs(hit.normal.y) > 0.65f || hit.distance >= nearest) continue;
                        nearest = hit.distance;
                        target = hit.point + hit.normal * 0.06f;
                    }
                }
            }
            Solve(leg, target, bodyForward);
        }
        void Arm(Limb arm, Transform target, Component hand, int interaction, float side, bool grounded, bool hanging, float speed)
        {
            if (arm == null) return;
            Vector3 forward = Quaternion.Euler(0, bodyYaw, 0) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0, bodyYaw, 0) * Vector3.right;
            bool holding = false;
            if (hand != null)
            {
                var item = hand.GetType().GetField("_item", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(hand) as GameObject;
                holding = item != null && item.activeInHierarchy;
            }
            // Preserve the native free arm swing; actual interaction targets override only
            // the involved arm. This does not move the networked hand or its item.
            if(usingNative&&interaction==0&&!holding)return;
            Vector3 point;
            if (!IsPreview && target != null && (interaction != 0 || holding)) point = target.position;
            else if (hanging)
                point = arm.Upper.position - Vector3.up * 0.16f + forward * 0.20f + right * side * 0.22f;
#if WKPMX_DEV
            else if (IsPreview && PreviewAction == 4 && side > 0)
                point = arm.Upper.position - Vector3.up * 0.25f + forward * 0.43f;
#endif
            else
            {
                float cycle = Mathf.Sin(phase * Mathf.PI * 2 + (side < 0 ? Mathf.PI : 0));
                float run = Mathf.Clamp01((speed - 1.6f) / 2.8f);
                float reach = (0.1f + run * 0.1f) + cycle * blend * (0.16f + run * 0.08f);
                point = arm.Upper.position - Vector3.up * (0.57f - run * 0.16f - landing * 0.06f)
                      + forward * reach + right * side * 0.08f;
                if (!grounded) point += forward * 0.14f + Vector3.up * 0.13f;
            }
            Solve(arm, point, -forward + right * side * 0.5f);
        }
        static void Solve(Limb limb, Vector3 target, Vector3 pole)
        {
            var a = limb.Upper; var b = limb.Lower; var c = limb.End;
            float ab = Vector3.Distance(a.position, b.position), bc = Vector3.Distance(b.position, c.position);
            Vector3 to = target - a.position;
            float distance = Mathf.Clamp(to.magnitude, Mathf.Abs(ab - bc) + 0.001f, ab + bc - 0.001f);
            if (to.sqrMagnitude < 0.000001f || ab < 0.001f || bc < 0.001f) return;
            Vector3 axis = to.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(pole, axis).normalized;
            if (bend.sqrMagnitude < 0.01f) bend = Vector3.ProjectOnPlane(Vector3.right, axis).normalized;
            float along = (ab * ab - bc * bc + distance * distance) / (2 * distance);
            Vector3 elbow = a.position + axis * along + bend * Mathf.Sqrt(Mathf.Max(0, ab * ab - along * along));
            Quaternion endRotation = c.rotation;
            a.rotation = Quaternion.FromToRotation(b.position - a.position, elbow - a.position) * a.rotation;
            Vector3 reachable = a.position + axis * distance;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, reachable - b.position) * b.rotation;
            c.rotation = endRotation;
        }
    }
}

