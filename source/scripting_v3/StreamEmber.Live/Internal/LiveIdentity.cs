//
// StreamEmber Live: shared between gtav-runtime-scripthook and rdr2-runtime-scripthook (keep identical).
//

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StreamEmber.Live.Internal
{
    /// <summary>Identity v2 runtime token (ES256 JWT from Falcon). Only the payload is read; services verify it.</summary>
    internal sealed class RuntimeToken
    {
        private RuntimeToken(string value, string subject, string application, DateTime expiresUtc, string scopes)
        {
            Value = value;
            Subject = subject;
            Application = application;
            ExpiresUtc = expiresUtc;
            Scopes = scopes;
        }

        public string Value { get; }

        /// <summary>Customer UUID (<c>sub</c>).</summary>
        public string Subject { get; }

        /// <summary>Application UUID (<c>app</c>); empty when the token is not bound to one.</summary>
        public string Application { get; }

        public DateTime ExpiresUtc { get; }
        public string Scopes { get; }

        public bool IsUsable(string applicationUuid, TimeSpan margin) =>
            DateTime.UtcNow + margin < ExpiresUtc
            && (Application.Length == 0 || string.Equals(Application, applicationUuid, StringComparison.OrdinalIgnoreCase));

        /// <summary>Null when the text is not a JWT with a <c>sub</c>.</summary>
        public static RuntimeToken FromJwt(string jwt, long fallbackExpiresIn = 0)
        {
            if (string.IsNullOrWhiteSpace(jwt)) return null;
            string[] parts = jwt.Trim().Split('.');
            if (parts.Length != 3) return null;
            Dictionary<string, object> payload;
            try
            {
                payload = Json.ParseObject(Encoding.UTF8.GetString(Base64Url(parts[1])));
            }
            catch (Exception)
            {
                return null;
            }
            string subject = LiveIdentity.NormalizeUuid(Json.Text(payload, "sub"));
            if (subject == null) return null;
            long exp = Json.Long(payload, "exp");
            DateTime expires = exp > 0
                ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(exp)
                : DateTime.UtcNow.AddSeconds(fallbackExpiresIn > 0 ? fallbackExpiresIn : 300);
            return new RuntimeToken(jwt.Trim(), subject, LiveIdentity.NormalizeUuid(Json.Text(payload, "app")) ?? string.Empty,
                                    expires, Json.Text(payload, "scope"));
        }

        private static byte[] Base64Url(string text)
        {
            string base64 = text.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }
    }

    /// <summary>
    /// Who the streamer is and what proves it.
    /// <list type="bullet">
    /// <item>Customer UUID (reading events and settings): Runtime.ini LiveCustomerUuid (development) → launcher
    /// <c>GET /api/customer</c> → runtime token <c>sub</c>. UUIDs are identifiers, not secrets.</item>
    /// <item>Runtime token (every write to EventFabric: presence, GCore reset): launcher <c>POST /api/runtime-token</c>
    /// → <c>STREAMEMBER_RUNTIME_TOKEN</c> (set by the launcher at start, valid 10 min). Without a token nothing is written.</item>
    /// </list>
    /// Thread-safe; network calls run on background threads.
    /// </summary>
    internal sealed class LiveIdentity
    {
        public const string TokenVariable = "STREAMEMBER_RUNTIME_TOKEN";

        private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(120);
        private static readonly TimeSpan UseMargin = TimeSpan.FromSeconds(30);

        private readonly LiveConfig _config;
        private readonly string _applicationUuid;
        private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
        private readonly RuntimeToken _environmentToken;
        private volatile RuntimeToken _token;

        public LiveIdentity(LiveConfig config, string applicationUuid)
        {
            _config = config;
            _applicationUuid = applicationUuid;
            _environmentToken = RuntimeToken.FromJwt(Environment.GetEnvironmentVariable(TokenVariable));
        }

        /// <summary>Why the last token request failed: launcher_offline, no_session, token_error; empty when it worked.</summary>
        public string TokenProblem { get; private set; } = string.Empty;

        /// <summary>Why the last customer lookup failed: launcher_offline, signed_out; empty when it worked.</summary>
        public string CustomerProblem { get; private set; } = string.Empty;

        public async Task<KeyValuePair<string, string>> ResolveCustomerAsync()
        {
            string configured = NormalizeUuid(_config.CustomerUuid);
            if (configured != null)
            {
                CustomerProblem = string.Empty;
                return new KeyValuePair<string, string>(configured, "config");
            }

            try
            {
                Dictionary<string, object> json = await Http.GetJsonAsync(_config.LauncherUrl + "/api/customer").ConfigureAwait(false);
                string uuid = NormalizeUuid(Json.Text(json, "customerUuid"));
                if (uuid != null)
                {
                    CustomerProblem = string.Empty;
                    return new KeyValuePair<string, string>(uuid, "launcher");
                }
                CustomerProblem = "signed_out";
            }
            catch (Exception error)
            {
                CustomerProblem = Http.Root(error) is HttpStatusException ? "signed_out" : "launcher_offline";
                LiveLog.Debug("Launcher customer not available: " + Http.Describe(error));
            }

            RuntimeToken token = Peek() ?? (_environmentToken != null && _environmentToken.IsUsable(_applicationUuid, UseMargin) ? _environmentToken : null);
            return token != null
                ? new KeyValuePair<string, string>(token.Subject, "token")
                : new KeyValuePair<string, string>(null, string.Empty);
        }

        /// <summary>A cached, still valid token, without any request (optional proof on read requests).</summary>
        public RuntimeToken Peek()
        {
            RuntimeToken token = _token;
            return token != null && token.IsUsable(_applicationUuid, UseMargin) ? token : null;
        }

        /// <summary>Token for a write; refreshed ~2 min before it expires. Null when none can be obtained.</summary>
        public async Task<RuntimeToken> GetTokenAsync(bool forceRefresh = false)
        {
            if (!forceRefresh)
            {
                RuntimeToken cached = _token;
                if (cached != null && cached.IsUsable(_applicationUuid, RefreshMargin)) return cached;
            }

            await _tokenLock.WaitAsync().ConfigureAwait(false);
            try
            {
                RuntimeToken current = _token;
                if (!forceRefresh && current != null && current.IsUsable(_applicationUuid, RefreshMargin)) return current;

                try
                {
                    var body = Json.NewObject();
                    body["applicationUuid"] = _applicationUuid;
                    body["audience"] = "eventfabric";
                    Dictionary<string, object> json = await Http.PostJsonAsync(_config.LauncherUrl + "/api/runtime-token", body).ConfigureAwait(false);
                    RuntimeToken token = RuntimeToken.FromJwt(Json.Text(json, "token"), Json.Long(json, "expiresIn"));
                    if (token == null) throw new FormatException("The launcher answered without a runtime token.");
                    if (!token.IsUsable(_applicationUuid, TimeSpan.Zero))
                        throw new InvalidOperationException("The runtime token is expired or for another application (" + token.Application + ").");
                    _token = token;
                    if (TokenProblem.Length > 0) LiveLog.Info("Runtime token available again.");
                    TokenProblem = string.Empty;
                    LiveLog.Debug("Runtime token refreshed; expires " + token.ExpiresUtc.ToString("HH:mm:ss") + " UTC, scope '" + token.Scopes + "'.");
                    return token;
                }
                catch (Exception error)
                {
                    Exception root = Http.Root(error);
                    string problem = root is HttpStatusException http
                        ? (http.Status == 401 ? "no_session" : "token_error")
                        : root is WebException || root is TimeoutException ? "launcher_offline" : "token_error";
                    if (problem != TokenProblem) LiveLog.Warn("Runtime token not available (" + problem + "): " + Http.Describe(error));
                    TokenProblem = problem;
                }

                RuntimeToken fallback = _environmentToken;
                if (fallback != null && fallback.IsUsable(_applicationUuid, UseMargin)) return fallback;
                current = _token;
                return current != null && current.IsUsable(_applicationUuid, UseMargin) ? current : null;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        /// <summary>After a 401 on a write: forget the cached token so the next write asks again.</summary>
        public void Invalidate() => _token = null;

        public static string NormalizeUuid(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Guid.TryParse(value.Trim(), out Guid guid)) return null;
            return guid.ToString("D");
        }
    }
}
