//
// StreamEmber Live: shared between gtav-runtime-scripthook and rdr2-runtime-scripthook (keep identical).
//

using System;

namespace StreamEmber.Live
{
    public sealed class LiveActionEventArgs : EventArgs
    {
        internal LiveActionEventArgs(LiveAction action, bool handled)
        {
            Action = action;
            Handled = handled;
        }

        public LiveAction Action { get; }

        /// <summary>True when an <c>On(…)</c> / <c>[LiveAction]</c> handler of this script already ran for it.</summary>
        public bool Handled { get; }
    }

    public sealed class LiveSettingsEventArgs : EventArgs
    {
        internal LiveSettingsEventArgs(LiveValues settings, string revision)
        {
            Settings = settings;
            Revision = revision;
        }

        /// <summary>Schema defaults merged with the streamer's settings from Falcon.</summary>
        public LiveValues Settings { get; }

        /// <summary>Falcon revision; "schema" before the first read.</summary>
        public string Revision { get; }
    }

    public sealed class LiveConnectionEventArgs : EventArgs
    {
        internal LiveConnectionEventArgs(bool isConnected)
        {
            IsConnected = isConnected;
        }

        public bool IsConnected { get; }
    }

    /// <summary>
    /// Binds a method of a <see cref="LiveScript"/> to a schema action:
    /// <c>[LiveAction("player.heal")] void Heal(LiveAction action)</c> (the parameter is optional).
    /// Runs on the script thread, like Tick.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class LiveActionAttribute : Attribute
    {
        public LiveActionAttribute(string actionId)
        {
            ActionId = actionId;
        }

        public string ActionId { get; }
    }
}
