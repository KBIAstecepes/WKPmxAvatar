using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace WKPmxAvatar
{
    internal static class CrouchSync
    {
        const string Key = "wkpmx.crouched";
        const float Lifetime = 25f;
        static readonly CrouchSendState Sends = new CrouchSendState();
        sealed class PeerCrouch
        {
            internal bool Crouched;
            internal float Until;
        }
        static ConditionalWeakTable<object, PeerCrouch> peers = new ConditionalWeakTable<object, PeerCrouch>();
        static FieldInfo player, timer;
        static MethodInfo isCrouching, isDead;
        static PropertyInfo root;
        static bool warned;

        internal static void Configure(FieldInfo local, MethodInfo crouch, MethodInfo dead, FieldInfo lowRate, PropertyInfo remoteRoot)
        {
            player = local;
            isCrouching = crouch;
            isDead = dead;
            timer = lowRate;
            root = remoteRoot;
            warned = false;
        }

        static bool Sample(out object actor, out bool crouched)
        {
            actor = player?.GetValue(null);
            crouched = false;
            if (actor == null || actor is UnityEngine.Object native && native == null ||
                !(isDead?.Invoke(actor, null) is bool dead) || dead) return false;
            if (!(isCrouching?.Invoke(actor, null) is bool sampled)) return false;
            crouched = sampled;
            return true;
        }

        internal static void BeforeSend(object __instance, out bool __state)
        {
            __state = false;
            try
            {
                if (!Sample(out object actor, out bool crouched))
                {
                    Sends.Invalidate();
                    return;
                }
                __state = Sends.Begin(__instance, actor, crouched, timer.GetValue(__instance));
            }
            catch (Exception e) { Warn(e); }
        }

        internal static Exception EndSend(Exception __exception, bool __state)
        {
            if (__state) Sends.End();
            return __exception;
        }

        internal static void AfterTimer(object __instance, ref bool __result)
        {
            if (Sends.ConsumePromotion(__instance)) __result = true;
        }

        internal static void AfterLocalDeath(object __instance)
        {
            try
            {
                if (ReferenceEquals(__instance, player?.GetValue(null)) && isDead?.Invoke(__instance, null) is true)
                    Sends.Invalidate();
            }
            catch (Exception e) { Warn(e); }
        }

        internal static void AfterBuild(object __instance, Dictionary<string, string> __result)
        {
            try
            {
                if (__result == null || !Sample(out object actor, out bool crouched)) return;
                __result[Key] = crouched ? "1" : "0";
                Sends.Written(__instance, actor, crouched);
            }
            catch (Exception e) { Warn(e); }
        }

        internal static void AfterReceive(object __instance, Dictionary<string, string> __0)
        {
            try
            {
                if (__0 == null || !__0.TryGetValue(Key, out string value) || (value != "0" && value != "1") ||
                    Plugin.Instance?.IsRetiredContainer(__instance) == true) return;
                var state = peers.GetValue(__instance, _ => new PeerCrouch());
                state.Crouched = value == "1";
                state.Until = Time.unscaledTime + Lifetime;
                var gameObject = root?.GetValue(__instance) as GameObject;
                if (gameObject != null) ApplyToRig(__instance, gameObject.GetComponentInChildren<AvatarMotion>(true));
            }
            catch (Exception e) { Warn(e); }
        }

        internal static void ApplyToRig(object container, AvatarMotion rig)
        {
            if (rig == null) return;
            if (peers.TryGetValue(container, out var state) && Time.unscaledTime < state.Until)
                rig.ReadCrouch(state.Crouched, state.Until);
            else rig.ClearCrouch();
        }

        internal static void Forget(object container) => peers.Remove(container);

        internal static void ResetSession()
        {
            peers = new ConditionalWeakTable<object, PeerCrouch>();
            Sends.Reset();
        }

        static void Warn(Exception e)
        {
            if (warned) return;
            warned = true;
            Plugin.Instance?.Warn("Crouch sync unavailable: " + e.GetBaseException().GetType().Name);
        }
    }

    internal sealed class CrouchSendState
    {
        object sender, player, promotionTimer;
        bool? written;
        bool active;
        int sendThread;

        internal bool Begin(object currentSender, object currentPlayer, bool crouched, object timer)
        {
            if (active) return false;
            if (!ReferenceEquals(sender, currentSender) || !ReferenceEquals(player, currentPlayer))
            {
                sender = currentSender;
                player = currentPlayer;
                written = null;
            }
            active = true;
            sendThread = Environment.CurrentManagedThreadId;
            promotionTimer = written != crouched ? timer : null;
            return true;
        }

        internal bool ConsumePromotion(object timer)
        {
            if (timer == null || sendThread != Environment.CurrentManagedThreadId || !ReferenceEquals(timer, promotionTimer)) return false;
            promotionTimer = null;
            return true;
        }

        internal void Written(object currentSender, object currentPlayer, bool crouched)
        {
            if (ReferenceEquals(sender, currentSender) && ReferenceEquals(player, currentPlayer))
                written = crouched;
        }

        internal void End()
        {
            promotionTimer = null;
            active = false;
            sendThread = 0;
        }

        internal void Invalidate()
        {
            if (!active) Reset();
        }

        internal void Reset()
        {
            End();
            sender = player = null;
            written = null;
        }
    }
}
