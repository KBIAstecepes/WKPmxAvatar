using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WKPmxAvatar
{
    public sealed class AvatarSelectionSync : MonoBehaviour
    {
        internal Plugin Owner;
        readonly PeerSelections peers = new PeerSelections();
        Type uiManager;
        FieldInfo screenField, networkField;
        PropertyInfo lobbyActive, lobbyId, steamId, playerId;
        MethodInfo getMember, setMember;
        object currentNetwork;
        ulong currentLobby;
        string published;
        float nextUpdate, nextPublish, nextUiRecovery;

        internal bool HasMemberChannel => networkField != null && lobbyActive != null && lobbyId != null &&
            steamId != null && getMember != null && setMember != null;

        internal void Configure(Type ui, FieldInfo screen, Type network, PropertyInfo containerId)
        {
            uiManager = ui;
            screenField = screen;
            playerId = containerId;
            for (var type = network; type != null && networkField == null; type = type.BaseType)
                networkField = type.GetField("_instance", BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.NonPublic);
            if (networkField?.FieldType != network) networkField = null;
            lobbyActive = Property(network, "IsInLobby", typeof(bool), false);
            lobbyId = Property(network, "LobbyId", typeof(ulong), false);
            steamId = Property(network, "UserSteamId", typeof(ulong), true);
            const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance;
            getMember = MultiplayerHooks.Find(network, "GetMemberData", typeof(string), flags, typeof(ulong), typeof(string));
            setMember = MultiplayerHooks.Find(network, "SetMemberData", typeof(void), flags, typeof(string), typeof(string));
        }

        static PropertyInfo Property(Type type, string name, Type value, bool isStatic)
        {
            var flags = BindingFlags.Public | BindingFlags.DeclaredOnly | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            var property = type?.GetProperty(name, flags);
            return property?.PropertyType == value && property.GetMethod?.IsPublic == true ? property : null;
        }

        internal void Invalidate()
        {
            published = null;
            nextUpdate = 0;
        }

        internal void PublishSelection()
        {
            Invalidate();
            try { if (SyncSession()) Publish(); }
            catch (Exception e) { Warn(e); }
        }

        bool SyncSession()
        {
            if (!HasMemberChannel) return false;
            var network = networkField.GetValue(null);
            if (network == null || network is UnityEngine.Object native && native == null ||
                !(lobbyActive.GetValue(network) is bool active) || !active ||
                !(lobbyId.GetValue(network) is ulong lobby) || lobby == 0)
            {
                if (currentNetwork != null) ResetSession();
                return false;
            }
            if (!ReferenceEquals(network, currentNetwork) || lobby != currentLobby)
            {
                if (currentNetwork != null) ResetSession();
                currentNetwork = network;
                currentLobby = lobby;
                published = null;
                nextPublish = 0;
            }
            return true;
        }

        void Publish()
        {
            string choice = Owner.PersonalModelId;
            if (!ModelCatalog.ValidId(choice) || (published == choice && Time.unscaledTime < nextPublish)) return;
            ulong self = (ulong)steamId.GetValue(null);
            if (self == 0) return;
            var stored = ReadMember(self);
            if (stored != choice) setMember.Invoke(currentNetwork, new object[] { ModelCatalog.WireKey, choice });
            published = choice;
            nextPublish = Time.unscaledTime + 5f;
        }

        string ReadMember(ulong id) => getMember.Invoke(currentNetwork, new object[] { id, ModelCatalog.WireKey }) as string;
        ulong Id(object container) => playerId?.DeclaringType.IsInstanceOfType(container) == true ? (ulong)playerId.GetValue(container) : 0;

        internal string ForAttach(object container, ulong id)
        {
            try
            {
                if (id != 0 && SyncSession())
                {
                    string current = ReadMember(id);
                    if (ModelCatalog.ValidId(current)) peers.Accept(id, container, current);
                }
            }
            catch (Exception e) { Warn(e); }
            return peers.Find(id, container);
        }

        void ApplyMember(object container, Dictionary<string, string> data)
        {
            if (data == null || !data.TryGetValue(ModelCatalog.WireKey, out var choice) ||
                !ModelCatalog.ValidId(choice) || !SyncSession()) return;
            ulong id = Id(container);
            if (id == 0 || Owner.IsRetiredContainer(container)) return;

            // A member reply can arrive before the remote root. For conflicting replies
            // reconcile against the live Steam key, keeping valid guesses out of the wire.
            string previous = peers.Find(id, container);
            if (previous != null && previous != choice)
            {
                string current = ReadMember(id);
                if (ModelCatalog.ValidId(current)) choice = current;
            }
            if (peers.Accept(id, container, choice)) Owner.RefreshSelection(container);
        }

        void Update()
        {
            if (Owner == null || Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + 1f;
            try
            {
                if (Time.unscaledTime >= nextUiRecovery)
                {
                    nextUiRecovery = Time.unscaledTime + 5f;
                    if (uiManager != null && screenField != null)
                        foreach (var ui in Resources.FindObjectsOfTypeAll(uiManager))
                            if (ui is Component component && component.gameObject.scene.IsValid()) Mount(ui);
                }
            }
            catch (Exception e) { Warn(e); }

            try
            {
                if (!SyncSession()) return;
                Publish();
                foreach (var container in Owner.RemoteContainers)
                {
                    ulong id = Id(container);
                    if (id != 0 && peers.Accept(id, container, ReadMember(id)))
                        Owner.RefreshSelection(container);
                }
            }
            catch (Exception e) { Warn(e); }
        }

        internal void ResetSession()
        {
            currentNetwork = null;
            currentLobby = 0;
            published = null;
            nextPublish = 0;
            foreach (var container in peers.Containers) Owner?.RetireContainer(container);
            peers.Clear();
            CrouchSync.ResetSession();
            Owner?.ClearRemoteSession();
        }

        bool Mount(object ui) => screenField?.GetValue(ui) is GameObject screen && screen != null &&
            LobbyAvatarMenu.Mount(screen, Owner);

        void Warn(Exception e) => Owner?.SelectionWarning(e.GetBaseException().GetType().Name);

        internal static void AfterLobbyScreen(object __instance)
        {
            var sync = Plugin.Instance?.SelectionSync;
            try { sync?.Mount(__instance); }
            catch (Exception e) { sync?.Warn(e); }
        }

        internal static void AfterMemberData(object __instance, Dictionary<string, string> __0)
        {
            var sync = Plugin.Instance?.SelectionSync;
            try { sync?.ApplyMember(__instance, __0); }
            catch (Exception e) { sync?.Warn(e); }
        }

        internal static void AfterResetSession()
        {
            var sync = Plugin.Instance?.SelectionSync;
            try { sync?.ResetSession(); }
            catch (Exception e) { sync?.Warn(e); }
        }

        internal static void AfterPlayerRemove(ulong __0)
        {
            var owner = Plugin.Instance;
            var sync = owner?.SelectionSync;
            try
            {
                object pending = sync?.peers.Remove(__0);
                if (pending != null) owner.RetireContainer(pending);
                owner?.RemoveRemoteMember(__0);
            }
            catch (Exception e) { sync?.Warn(e); }
        }
    }

    internal sealed class PeerSelections
    {
        readonly Dictionary<ulong, (object Container, string Choice)> selections =
            new Dictionary<ulong, (object Container, string Choice)>();

        internal string Find(ulong id, object container) =>
            selections.TryGetValue(id, out var entry) && ReferenceEquals(entry.Container, container) ? entry.Choice : null;

        internal bool Accept(ulong id, object container, string choice)
        {
            if (id == 0 || container == null || !ModelCatalog.ValidId(choice) || Find(id, container) == choice) return false;
            selections[id] = (container, choice);
            return true;
        }

        internal IEnumerable<object> Containers
        {
            get
            {
                foreach (var entry in selections.Values) yield return entry.Container;
            }
        }

        internal object Remove(ulong id) => selections.Remove(id, out var entry) ? entry.Container : null;
        internal void Clear() => selections.Clear();
    }
}
