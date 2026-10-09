//
// StreamEmber Live: shared between gtav-runtime-scripthook and rdr2-runtime-scripthook (keep identical).
//

using System;
using System.Collections.Generic;
using System.Globalization;
using StreamEmber.Live.Internal;

namespace StreamEmber.Live
{
    /// <summary>
    /// A GameAction v2 packet from EventFabric/GCore (EventContracts/docs/GAME_ACTION.md). GCore did all the math
    /// (matching, aggregation, cooldown, limits); the game runs the action as delivered.
    /// </summary>
    public sealed class LiveAction
    {
        private readonly Dictionary<string, object> _json;

        internal LiveAction(Dictionary<string, object> json, bool isTest)
        {
            _json = json ?? Json.NewObject();
            IsTest = isTest;
            Arguments = new LiveValues(Json.Obj(_json, "arguments"));
            Trigger = new LiveTrigger(Json.Obj(_json, "trigger"));
            Viewer = new LiveViewer(Json.Obj(_json, "viewer"));
            Dictionary<string, object> part;
            Gift = (part = Json.Obj(_json, "gift")) != null ? new LiveGift(part) : null;
            Subscription = (part = Json.Obj(_json, "subscription")) != null ? new LiveSubscription(part) : null;
            Reward = (part = Json.Obj(_json, "reward")) != null ? new LiveReward(part) : null;
            Aggregation = (part = Json.Obj(_json, "aggregation")) != null ? new LiveAggregation(part) : null;
        }

        /// <summary>Deterministic action id; the same on redelivery (the runtime runs each id once).</summary>
        public string Id => Json.Text(_json, "id");

        /// <summary>Schema action id, e.g. <c>enemy.spawn</c>.</summary>
        public string Action => Json.Text(_json, "action");

        public string RuleId => Json.Text(_json, "ruleId");
        public string CustomerUuid => Json.Text(_json, "customerUuid");
        public string ApplicationUuid => Json.Text(_json, "applicationUuid");

        /// <summary>Publish time (RFC 3339 text), empty for test actions.</summary>
        public string CreatedAt => Json.Text(_json, "createdAt");

        /// <summary>Arguments the streamer set for this action, completed with the schema defaults.</summary>
        public LiveValues Arguments { get; private set; }

        /// <summary>Units in the packet; at least 1. Multiply with long and clamp.</summary>
        public int Quantity
        {
            get
            {
                if (!_json.TryGetValue("quantity", out object value) || !Json.TryNumber(value, out double number) || number < 1) return 1;
                return number > int.MaxValue ? int.MaxValue : (int)number;
            }
        }

        /// <summary>Platform, streamer (target), room and event kind that caused the action.</summary>
        public LiveTrigger Trigger { get; }

        /// <summary>The viewer who caused it (not the streamer, not the game character). Fields may be empty (anonymous).</summary>
        public LiveViewer Viewer { get; }

        /// <summary>gift / donation triggers; otherwise null.</summary>
        public LiveGift Gift { get; }

        /// <summary>subscribe / resubscribe / subscription_gift triggers; otherwise null.</summary>
        public LiveSubscription Subscription { get; }

        /// <summary>reward_redeem triggers; otherwise null.</summary>
        public LiveReward Reward { get; }

        /// <summary>Only for aggregated matches (thresholds); otherwise null.</summary>
        public LiveAggregation Aggregation { get; }

        /// <summary>Comment text for comment triggers; otherwise empty.</summary>
        public string Comment => Json.Text(Json.Obj(_json, "comment"), "text");

        /// <summary>Like count for like triggers; otherwise 0.</summary>
        public long LikeCount => Math.Max(0, Json.Long(Json.Obj(_json, "like"), "count"));

        /// <summary>Triggered with a test hotkey or the launcher's "try in game".</summary>
        public bool IsTest { get; }

        /// <summary>The whole packet as JSON (for fields this class does not expose yet).</summary>
        public string ToJson() => Json.Write(_json);

        /// <summary>
        /// Fills {user} (viewer display name), {username}, {nickname}, {quantity}, {gift}, {comment}, {streamer}, {platform}.
        /// </summary>
        public string Render(string template)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            return template
                .Replace("{user}", Viewer.DisplayName)
                .Replace("{username}", Viewer.Username)
                .Replace("{nickname}", Viewer.Nickname)
                .Replace("{quantity}", Quantity.ToString(CultureInfo.InvariantCulture))
                .Replace("{gift}", Gift?.Name ?? string.Empty)
                .Replace("{comment}", Comment)
                .Replace("{streamer}", Trigger.Target)
                .Replace("{platform}", Trigger.Platform);
        }

        /// <summary>Short text: "Ayşe · Rose ×3".</summary>
        public string Describe()
        {
            string who = Viewer.DisplayName.Length > 0 ? Viewer.DisplayName : "StreamEmber";
            if (Gift != null && Gift.Name.Length > 0) return who + " · " + Gift.Name + (Quantity > 1 ? " ×" + Quantity : string.Empty);
            return who;
        }

        internal void ApplyArgumentDefaults(Dictionary<string, object> defaults)
        {
            if (defaults == null || defaults.Count == 0) return;
            Dictionary<string, object> merged = Json.Merge(defaults, Json.Obj(_json, "arguments"));
            _json["arguments"] = merged;
            Arguments = new LiveValues(merged);
        }

        public override string ToString() => "LiveAction[" + Action + " id=" + Id + " x" + Quantity + (IsTest ? " test" : string.Empty) + "]";
    }

    /// <summary>What caused the action (<c>trigger</c>).</summary>
    public sealed class LiveTrigger
    {
        internal LiveTrigger(Dictionary<string, object> json)
        {
            Platform = Json.Text(json, "platform");
            Target = Json.Text(json, "target");
            RoomId = Json.Text(json, "roomId");
            Kind = Json.Text(json, "kind");
            Category = Json.Text(json, "category");
            Event = Json.Text(json, "event");
            EventId = Json.Text(json, "eventId");
        }

        /// <summary>tiktok, twitch, youtube…; "streamember" for manual / test actions.</summary>
        public string Platform { get; }

        /// <summary>The streamer's username on that platform.</summary>
        public string Target { get; }

        /// <summary>Live room / stream id on that platform.</summary>
        public string RoomId { get; }

        /// <summary>Canonical kind: gift, like, comment, follow, share, subscribe, manual… (EVENT_IDENTITY.md).</summary>
        public string Kind { get; }

        /// <summary>Kind category: support, audience, chat…</summary>
        public string Category { get; }

        /// <summary>Dotted catalog id: tiktok.gift.5655, tiktok.like, streamember.manual.</summary>
        public string Event { get; }

        /// <summary>Normalized event id.</summary>
        public string EventId { get; }
    }

    /// <summary>A viewer (<c>viewer</c>, or a subscription gifter).</summary>
    public sealed class LiveViewer
    {
        internal LiveViewer(Dictionary<string, object> json)
        {
            Id = Json.Text(json, "id");
            Username = Json.Text(json, "username");
            Nickname = Json.Text(json, "nickname");
            AvatarUrl = Json.Text(json, "avatarUrl");
        }

        public string Id { get; }
        public string Username { get; }
        public string Nickname { get; }
        public string AvatarUrl { get; }

        /// <summary>Nickname, else username.</summary>
        public string DisplayName => Nickname.Length > 0 ? Nickname : Username;
    }

    /// <summary>Gift / donation (<c>gift</c>).</summary>
    public sealed class LiveGift
    {
        internal LiveGift(Dictionary<string, object> json)
        {
            Id = Json.Text(json, "id");
            Name = Json.Text(json, "name");
            ImageUrl = Json.Text(json, "imageUrl");
            Coins = Math.Max(0, Json.Long(json, "coins"));
            Count = Math.Max(0, Json.Long(json, "count"));
            TotalCoins = Math.Max(0, Json.Long(json, "totalCoins"));
        }

        /// <summary>Platform gift id.</summary>
        public string Id { get; }

        public string Name { get; }
        public string ImageUrl { get; }

        /// <summary>Value of one gift in the platform's unit.</summary>
        public long Coins { get; }

        public long Count { get; }
        public long TotalCoins { get; }
    }

    /// <summary>Subscription (<c>subscription</c>).</summary>
    public sealed class LiveSubscription
    {
        internal LiveSubscription(Dictionary<string, object> json)
        {
            Tier = Json.Text(json, "tier");
            Months = Math.Max(0, Json.Long(json, "months"));
            Dictionary<string, object> gifter = Json.Obj(json, "gifter");
            Gifter = gifter != null ? new LiveViewer(gifter) : null;
        }

        public string Tier { get; }
        public long Months { get; }

        /// <summary>Who gifted it (subscription_gift); otherwise null.</summary>
        public LiveViewer Gifter { get; }
    }

    /// <summary>Channel reward (<c>reward</c>).</summary>
    public sealed class LiveReward
    {
        internal LiveReward(Dictionary<string, object> json)
        {
            Id = Json.Text(json, "id");
            Text = Json.Text(json, "text");
            Coins = Math.Max(0, Json.Long(json, "coins"));
        }

        public string Id { get; }
        public string Text { get; }
        public long Coins { get; }
    }

    /// <summary>Aggregated match details (<c>aggregation</c>), e.g. for a progress bar.</summary>
    public sealed class LiveAggregation
    {
        internal LiveAggregation(Dictionary<string, object> json)
        {
            Unit = Json.Text(json, "unit");
            Scope = Json.Text(json, "scope");
            Threshold = Math.Max(0, Json.Long(json, "threshold"));
            FireIndex = Math.Max(0, Json.Long(json, "fireIndex"));
            FireCount = Math.Max(0, Json.Long(json, "fireCount"));
            Remainder = Math.Max(0, Json.Long(json, "remainder"));
        }

        /// <summary>count or coins.</summary>
        public string Unit { get; }

        /// <summary>viewer or stream.</summary>
        public string Scope { get; }

        public long Threshold { get; }
        public long FireIndex { get; }
        public long FireCount { get; }

        /// <summary>Carried over to the next fire.</summary>
        public long Remainder { get; }
    }
}
