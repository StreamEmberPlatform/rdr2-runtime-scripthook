//
// StreamEmber Live: shared between gtav-runtime-scripthook and rdr2-runtime-scripthook (keep identical).
// The base class (GTA.Script / RDR2.Script) is declared in StreamEmber.Live.Game/GameBridge.cs.
//

using System;
using System.Collections.Generic;
using System.Reflection;
using StreamEmber.Live.Internal;

namespace StreamEmber.Live
{
    /// <summary>
    /// Base class of live (stream-driven) mods. Use it like a normal script: subscribe to <c>Tick</c>, and to the
    /// live events below. The runtime owns identity, EventFabric, Falcon settings and presence; a mod never does HTTP.
    /// <para>
    /// The mod assembly must embed <c>streamember-module.json</c> and <c>game-schema.json</c> (see <see cref="LiveManifest"/>).
    /// Every event and handler runs on the script thread, like Tick, so game APIs are safe to call.
    /// </para>
    /// <code>
    /// public sealed class MyMod : LiveScript
    /// {
    ///     public MyMod()
    ///     {
    ///         On("player.heal", action => Game.Player.Character.Health = 200);
    ///         SettingsChanged += (s, e) => _max = e.Settings.GetInt("battle.maxHumans", 40, 1, 100);
    ///     }
    ///
    ///     [LiveAction("enemy.spawn")]
    ///     private void Spawn(LiveAction action) { /* action.Quantity, action.Viewer.DisplayName, action.Arguments… */ }
    /// }
    /// </code>
    /// </summary>
    public abstract partial class LiveScript
    {
        internal const int StatePending = 0;
        internal const int StateActive = 1;
        internal const int StateIgnored = 2;

        private readonly Dictionary<string, Action<LiveAction>> _handlers = new Dictionary<string, Action<LiveAction>>(StringComparer.Ordinal);

        protected LiveScript()
        {
            Manifest = LiveManifest.For(GetType().Assembly);
            BindAttributeHandlers();
            Tick += (sender, args) => LiveService.OnTick(this);
            KeyDown += (sender, args) => LiveService.OnKeyDown(this, args);
            Aborted += (sender, args) => LiveService.OnAborted(this);
            LiveService.Register(this);
        }

        /// <summary>Manifest of this script's assembly.</summary>
        public LiveManifest Manifest { get; }

        /// <summary>True while this script's module is the active live module.</summary>
        public bool IsLiveActive { get; internal set; }

        /// <summary>The module became active (first tick after the game loaded). Settings are the schema defaults until Falcon answers.</summary>
        public event EventHandler LiveStarted;

        /// <summary>The script is stopping (abort / reload).</summary>
        public event EventHandler LiveStopped;

        /// <summary>
        /// Every action for this module, after the <c>On(…)</c> / <c>[LiveAction]</c> handler (if any).
        /// Use it for generic handling (logging, overlays, actions without a dedicated handler).
        /// </summary>
        public event EventHandler<LiveActionEventArgs> ActionReceived;

        /// <summary>Settings changed in Falcon; also raised once right after <see cref="LiveStarted"/>.</summary>
        public event EventHandler<LiveSettingsEventArgs> SettingsChanged;

        /// <summary>EventFabric connection opened or lost.</summary>
        public event EventHandler<LiveConnectionEventArgs> ConnectionChanged;

        internal int LiveState { get; set; }

        internal IEnumerable<string> HandlerIds => _handlers.Keys;

        internal bool ListensToAllActions => ActionReceived != null;

        /// <summary>Registers the handler of a schema action (one per action id and script).</summary>
        protected void On(string actionId, Action<LiveAction> handler)
        {
            if (string.IsNullOrWhiteSpace(actionId)) throw new ArgumentException("Action id is empty.", nameof(actionId));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (_handlers.ContainsKey(actionId)) throw new InvalidOperationException("Duplicate live action handler: " + actionId);
            _handlers[actionId] = handler;
        }

        /// <summary>True when a handler or <see cref="ActionReceived"/> took the action.</summary>
        internal bool Deliver(LiveAction action)
        {
            bool handled = false;
            if (_handlers.TryGetValue(action.Action, out Action<LiveAction> handler))
            {
                handled = true;
                Guard("action '" + action.Action + "'", () => handler(action));
            }
            EventHandler<LiveActionEventArgs> listeners = ActionReceived;
            if (listeners != null) Guard("ActionReceived", () => listeners(this, new LiveActionEventArgs(action, handled)));
            return handled || listeners != null;
        }

        internal void RaiseStarted() => Guard("LiveStarted", () => LiveStarted?.Invoke(this, EventArgs.Empty));

        internal void RaiseStopped() => Guard("LiveStopped", () => LiveStopped?.Invoke(this, EventArgs.Empty));

        internal void RaiseSettingsChanged(LiveSettingsEventArgs args) => Guard("SettingsChanged", () => SettingsChanged?.Invoke(this, args));

        internal void RaiseConnectionChanged(bool connected) =>
            Guard("ConnectionChanged", () => ConnectionChanged?.Invoke(this, new LiveConnectionEventArgs(connected)));

        private void Guard(string stage, Action work)
        {
            try
            {
                work();
            }
            catch (Exception error)
            {
                LiveLog.Error("Script " + GetType().FullName + " failed in " + stage, error);
            }
        }

        private void BindAttributeHandlers()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type type = GetType(); type != null && type != typeof(LiveScript); type = type.BaseType)
            {
                foreach (MethodInfo method in type.GetMethods(flags))
                {
                    foreach (LiveActionAttribute attribute in method.GetCustomAttributes<LiveActionAttribute>(false))
                    {
                        ParameterInfo[] parameters = method.GetParameters();
                        Action<LiveAction> handler;
                        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(LiveAction))
                            handler = (Action<LiveAction>)Delegate.CreateDelegate(typeof(Action<LiveAction>), this, method);
                        else if (parameters.Length == 0)
                        {
                            var call = (Action)Delegate.CreateDelegate(typeof(Action), this, method);
                            handler = action => call();
                        }
                        else
                        {
                            LiveLog.Error("[LiveAction(\"" + attribute.ActionId + "\")] " + type.FullName + "." + method.Name
                                          + " must take no parameter or one LiveAction; ignored.");
                            continue;
                        }
                        On(attribute.ActionId, handler);
                    }
                }
            }
        }
    }
}
