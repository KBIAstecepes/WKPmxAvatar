using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace WKPmxAvatar
{
    internal sealed class MultiplayerHooks
    {
        const string Dependency = "shenxl.MultiPlayerMod";
        const string MainHash = "C7196B6408E36CA5DD6E1A6D25DCB1B52E4C7C1B74077620FBD3C2D812A3F006";
        const string SharedHash = "EF700D59D64036E1EACE76DEAE0898470EE97252D958B994A61DCD6A3D4953DE";
        const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        const BindingFlags Callbacks = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Type DictionaryType = typeof(Dictionary<string, string>);
        readonly string guid;
        readonly List<Harmony> installed = new List<Harmony>();
        readonly Action<string> log;

        internal MultiplayerHooks(string guid, Action<string> log)
        {
            this.guid = guid;
            this.log = log;
        }

        internal static MethodInfo Find(Type type, string name, Type returns, BindingFlags flags, params Type[] parameters)
        {
            var matches = type?.GetMethods(flags).Where(method => method.Name == name &&
                !method.IsGenericMethod && method.ReturnType == returns &&
                method.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters)).Take(2).ToArray();
            return matches?.Length == 1 ? matches[0] : null;
        }

        static HarmonyMethod Callback(Type type, string method) => new HarmonyMethod(type.GetMethod(method, Callbacks));

        internal void Install(AvatarSelectionSync selection)
        {
            if (!Chainloader.PluginInfos.TryGetValue(Dependency, out var info) || info.Instance == null)
            {
                log("WKPMX compatibility: multiplayer GUID has no loaded plugin instance.");
                return;
            }
            var main = info.Instance.GetType().Assembly;
            Assembly shared = null;
            var reference = main.GetReferencedAssemblies().SingleOrDefault(name => name.Name == "WKMultiPlayerMod.Shared");
            if (reference != null)
            {
                try { shared = Assembly.Load(reference); }
                catch (Exception e) { log("WKPMX compatibility: Shared unavailable (" + e.GetType().Name + ")."); }
            }
            log("WKPMX compatibility: MP " + info.Metadata.Version + "; main " + main.GetName().Version +
                " (" + Path.GetFileName(main.Location) + "); Shared " +
                (shared == null ? "unavailable" : shared.GetName().Version + " (" + Path.GetFileName(shared.Location) +
                ", " + (InSameDirectory(main, shared) ? "from MP directory" : "outside MP directory") + ")") + ".");

            InstallTargets(main, shared, typeof(Plugin), selection,
                info.Metadata.Version == new Version(1, 8, 7, 2) && IsReviewedPair(main, shared));
        }

        internal void InstallTargets(Assembly main, Assembly shared, Type plugin, AvatarSelectionSync selection, bool reviewedPair)
        {
            var game = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            var ui = main.GetType("WKMPMod.UI.UI_Manager");
            var remote = main.GetType("WKMPMod.RemotePlayers.RPContainer");
            var local = main.GetType("WKMPMod.Components.LocalPlayer");
            var manager = main.GetType("WKMPMod.RemotePlayers.RPManager");
            var core = main.GetType("WKMPMod.Core.MPCore");
            var network = main.GetType("WKMPMod.NetWork.MPSteamworks");
            var playerData = shared?.GetType("WKMPMod.Data.PlayerData");
            var timer = shared?.GetType("WKMPMod.Util.TickTimer");
            var screen = ui?.GetField("_mpScreen", PrivateInstance);
            var root = remote?.GetProperty("PlayerObject", PublicInstance);
            var id = remote?.GetProperty("PlayerId", PublicInstance);
            bool remoteProperties = root?.PropertyType == typeof(GameObject) && root.GetMethod?.IsPublic == true &&
                id?.PropertyType == typeof(ulong) && id.GetMethod?.IsPublic == true;
            selection.Configure(ui, screen?.FieldType == typeof(GameObject) ? screen : null, network, remoteProperties ? id : null);

            var createScreen = screen?.FieldType == typeof(GameObject) ? Find(ui, "CreateLobbyScreen", typeof(void), PublicInstance) : null;
            Group("lobby", "periodic menu recovery", (createScreen, null, Callback(typeof(AvatarSelectionSync), nameof(AvatarSelectionSync.AfterLobbyScreen)), null));

            var initialize = remoteProperties ? Find(remote, "Initialize", typeof(bool), PublicInstance, typeof(GameObject), typeof(Transform)) : null;
            bool attached = Group("remote-model", "upstream model",
                (initialize, null, Callback(plugin, "AfterInitialize"), null));
            var pose = attached && playerData != null ? Find(remote, "HandlePlayerData", typeof(void), PublicInstance, playerData.MakeByRefType()) : null;
            Group("remote-pose", "native/procedural pose",
                (pose, null, Callback(plugin, "AfterPose"), null));

            var hand = game?.GetType("ENT_Player+Hand");
            Group("hand-sprites", "native sprites",
                (Find(hand, "GetPoseSprite", typeof(Sprite), PublicInstance, typeof(string)), null, Callback(plugin, "AfterHandPose"), null),
                (Find(hand, "SetInteractionSprite", typeof(void), PublicInstance, typeof(Sprite), typeof(Sprite)), Callback(plugin, "BeforeHandCursor"), null, null));

            bool lifecycle = Group("lifecycle", "polled session cleanup",
                (Find(core, "ResetStateVariables", typeof(void), PublicInstance), null, Callback(typeof(AvatarSelectionSync), nameof(AvatarSelectionSync.AfterResetSession)), null),
                (Find(manager, "PlayerRemove", typeof(void), PublicInstance, typeof(ulong)), null, Callback(typeof(AvatarSelectionSync), nameof(AvatarSelectionSync.AfterPlayerRemove)), null));
            var member = attached && selection.HasMemberChannel ? Find(remote, "ApplyMemberData", typeof(bool), PublicInstance, DictionaryType) : null;
            Group("member-identity", "periodic Steam member reads",
                (member, null, Callback(typeof(AvatarSelectionSync), nameof(AvatarSelectionSync.AfterMemberData)), null));

            var gamePlayer = game?.GetType("ENT_Player");
            var player = gamePlayer?.GetField("playerObject", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            var isCrouching = Find(gamePlayer, "IsCrouching", typeof(bool), PublicInstance);
            var isDead = Find(game?.GetType("GameEntity"), "IsDead", typeof(bool), PublicInstance);
            var lowRateTimer = local?.GetField("_minUpdateFrequencyTimer", PrivateInstance);
            bool canSample = player?.FieldType == gamePlayer && isCrouching != null && isDead != null;
            CrouchSync.Configure(player, isCrouching, isDead, lowRateTimer, root);
            var build = canSample ? Find(local, "BuildExtraPlayerData", DictionaryType, PublicInstance) : null;
            var receive = attached && lifecycle ? Find(remote, "HandleExtraPlayerData", typeof(void), PublicInstance, DictionaryType) : null;
            bool extra = Group("crouch-snapshots", "remote scale inference",
                (build, null, Callback(typeof(CrouchSync), nameof(CrouchSync.AfterBuild)), null),
                (receive, null, Callback(typeof(CrouchSync), nameof(CrouchSync.AfterReceive)), null));

            bool promote = reviewedPair && extra && timer != null && lowRateTimer?.FieldType == timer;
            var damageInfo = game?.GetType("Damageable+DamageInfo");
            Group("crouch-edges", "natural complete packets",
                (promote ? Find(local, "TrySendLocalPlayerData", typeof(void), PrivateInstance) : null,
                    Callback(typeof(CrouchSync), nameof(CrouchSync.BeforeSend)), null, Callback(typeof(CrouchSync), nameof(CrouchSync.EndSend))),
                (promote ? Find(timer, "TryTick", typeof(bool), PublicInstance) : null, null, Callback(typeof(CrouchSync), nameof(CrouchSync.AfterTimer)), null),
                (promote && damageInfo != null ? Find(gamePlayer, "Kill", typeof(void), PublicInstance, typeof(string), damageInfo) : null,
                    null, Callback(typeof(CrouchSync), nameof(CrouchSync.AfterLocalDeath)), null));
        }

        bool Group(string name, string fallback, params (MethodInfo Target, HarmonyMethod Prefix, HarmonyMethod Postfix, HarmonyMethod Finalizer)[] patches)
        {
            if (patches.Any(p => p.Target == null))
            {
                log("WKPMX " + name + ": unavailable target; " + fallback + ".");
                return false;
            }
            var owner = new Harmony(guid + "." + name);
            try
            {
                foreach (var patch in patches)
                    owner.Patch(patch.Target, prefix: patch.Prefix, postfix: patch.Postfix, finalizer: patch.Finalizer);
                installed.Add(owner);
                log("WKPMX " + name + ": installed " + string.Join(", ", patches.Select(p => p.Target.DeclaringType.Name + "." + p.Target.Name)) + ".");
                return true;
            }
            catch (Exception e)
            {
                owner.UnpatchSelf();
                log("WKPMX " + name + ": " + e.GetType().Name + "; " + fallback + ".");
                return false;
            }
        }

        static bool InSameDirectory(Assembly main, Assembly shared) =>
            string.Equals(Path.GetDirectoryName(main.Location), Path.GetDirectoryName(shared.Location), StringComparison.OrdinalIgnoreCase);

        static bool IsReviewedPair(Assembly main, Assembly shared)
        {
            if (shared == null || !InSameDirectory(main, shared)) return false;
            try
            {
                using var sha = SHA256.Create();
                using var first = File.OpenRead(main.Location);
                using var second = File.OpenRead(shared.Location);
                return BitConverter.ToString(sha.ComputeHash(first)).Replace("-", "") == MainHash &&
                    BitConverter.ToString(sha.ComputeHash(second)).Replace("-", "") == SharedHash;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal void Remove()
        {
            foreach (var group in installed) group.UnpatchSelf();
            installed.Clear();
        }
    }
}
