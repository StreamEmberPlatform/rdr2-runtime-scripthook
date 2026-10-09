//
// StreamEmber Live: shared between gtav-runtime-scripthook and rdr2-runtime-scripthook (keep identical).
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace StreamEmber.Live.Internal
{
    /// <summary>Non-2xx answer. <see cref="Code"/> is the body's "code" / "error" field when there is one.</summary>
    internal sealed class HttpStatusException : Exception
    {
        public HttpStatusException(int status, string code, string url)
            : base("HTTP " + status + (code.Length > 0 ? " " + code : string.Empty) + " " + StripQuery(url))
        {
            Status = status;
            Code = code;
        }

        public int Status { get; }
        public string Code { get; }

        private static string StripQuery(string url)
        {
            int query = url.IndexOf('?');
            return query < 0 ? url : url.Substring(0, query);
        }
    }

    /// <summary>
    /// HttpWebRequest (System.dll) instead of HttpClient: no extra reference in the scripting API.
    /// Every call runs off the script thread; callers marshal results back through the service queue.
    /// </summary>
    internal static class Http
    {
        private const int TimeoutMilliseconds = 10000;

        static Http()
        {
            // The game process is not a .NET app with a config file; make sure TLS 1.2 is allowed.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;
            if (ServicePointManager.DefaultConnectionLimit < 8) ServicePointManager.DefaultConnectionLimit = 8;
        }

        public static Task<Dictionary<string, object>> GetJsonAsync(string url, string bearer = null) =>
            SendAsync("GET", url, bearer, null);

        public static Task<Dictionary<string, object>> PostJsonAsync(string url, Dictionary<string, object> body, string bearer = null) =>
            SendAsync("POST", url, bearer, Json.Write(body ?? Json.NewObject()));

        private static async Task<Dictionary<string, object>> SendAsync(string method, string url, string bearer, string body)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Accept = "application/json";
            request.UserAgent = "StreamEmber-Runtime/" + GameBridge.Source + " " + GameBridge.ProductVersion;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (!string.IsNullOrEmpty(bearer)) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;

            Task<WebResponse> pending = Send(request, body);
            Task finished = await Task.WhenAny(pending, Task.Delay(TimeoutMilliseconds)).ConfigureAwait(false);
            if (finished != pending)
            {
                request.Abort();
                Observe(pending);
                throw new TimeoutException(method + " " + url.Split('?')[0] + " timed out.");
            }

            HttpWebResponse response;
            try
            {
                response = (HttpWebResponse)await pending.ConfigureAwait(false);
            }
            catch (WebException error) when (error.Response is HttpWebResponse failed)
            {
                using (failed)
                {
                    string text = Read(failed);
                    string code = string.Empty;
                    try
                    {
                        Dictionary<string, object> json = Json.ParseObject(text);
                        code = Json.Text(json, "code");
                        if (code.Length == 0) code = Json.Text(json, "error");
                    }
                    catch (FormatException)
                    {
                        // not JSON
                    }
                    throw new HttpStatusException((int)failed.StatusCode, code, url);
                }
            }

            using (response)
            {
                // Always read the body so the connection goes back to the pool.
                return Json.ParseObject(Read(response));
            }
        }

        private static async Task<WebResponse> Send(HttpWebRequest request, string body)
        {
            if (body != null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                request.ContentType = "application/json; charset=utf-8";
                request.ContentLength = bytes.Length;
                using (Stream stream = await request.GetRequestStreamAsync().ConfigureAwait(false))
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                }
            }
            return await request.GetResponseAsync().ConfigureAwait(false);
        }

        private static string Read(HttpWebResponse response)
        {
            using (Stream stream = response.GetResponseStream())
            {
                if (stream == null) return string.Empty;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static void Observe(Task<WebResponse> task)
        {
            task.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose();
                else _ = t.Exception;
            }, TaskScheduler.Default);
        }

        public static string Query(IEnumerable<KeyValuePair<string, string>> values) =>
            string.Join("&", values.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value ?? string.Empty)));

        public static string Encode(string value) => Uri.EscapeDataString(value ?? string.Empty);

        public static bool IsStatus(Exception error, int status) => Root(error) is HttpStatusException http && http.Status == status;

        public static Exception Root(Exception error)
        {
            Exception root = error;
            while (root is AggregateException aggregate && aggregate.InnerException != null) root = aggregate.InnerException;
            return root;
        }

        public static string Describe(Exception error)
        {
            Exception root = Root(error);
            if (root is WebException web && web.InnerException != null) root = web.InnerException;
            return root.GetType().Name + ": " + root.Message;
        }
    }
}
