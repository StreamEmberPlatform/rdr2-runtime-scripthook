//
// StreamEmber Live: shared between gtav-runtime-scripthook and rdr2-runtime-scripthook (keep identical).
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using StreamEmber.Live.Internal;

namespace StreamEmber.Live
{
    /// <summary>
    /// Embedded resources of a live mod's assembly (LogicalName = file name):
    /// <c>streamember-module.json</c> (applicationUuid = Falcon game UUID, code, name) and
    /// <c>game-schema.json</c> (actions, setting and argument defaults, action labels).
    /// </summary>
    public sealed class LiveManifest
    {
        public const string ManifestResource = "streamember-module.json";
        public const string SchemaResource = "game-schema.json";

        private static readonly Dictionary<Assembly, LiveManifest> Cache = new Dictionary<Assembly, LiveManifest>();

        private readonly Dictionary<string, object> _settingDefaults;
        private readonly Dictionary<string, Dictionary<string, object>> _argumentDefaults;
        private readonly Dictionary<string, Dictionary<string, object>> _actionLabels;

        private LiveManifest(string applicationUuid, string code, string name, List<string> actionIds,
                             Dictionary<string, object> settingDefaults,
                             Dictionary<string, Dictionary<string, object>> argumentDefaults,
                             Dictionary<string, Dictionary<string, object>> actionLabels)
        {
            ApplicationUuid = applicationUuid;
            Code = code;
            Name = name;
            ActionIds = actionIds;
            _settingDefaults = settingDefaults;
            _argumentDefaults = argumentDefaults;
            _actionLabels = actionLabels;
        }

        /// <summary>Falcon game UUID (applicationUuid in EventFabric).</summary>
        public string ApplicationUuid { get; }

        public string Code { get; }
        public string Name { get; }

        /// <summary>Action ids of the schema, in schema order.</summary>
        public IReadOnlyList<string> ActionIds { get; }

        /// <summary>Setting defaults from the schema (settings.fields[].default).</summary>
        public LiveValues SettingDefaults => new LiveValues(Json.CloneObject(_settingDefaults));

        internal Dictionary<string, object> SettingDefaultsRaw() => Json.CloneObject(_settingDefaults);

        internal Dictionary<string, object> ArgumentDefaults(string actionId) =>
            _argumentDefaults.TryGetValue(actionId ?? string.Empty, out Dictionary<string, object> value) ? Json.CloneObject(value) : Json.NewObject();

        /// <summary>The action's label in the schema; language, then en, then the id.</summary>
        public string ActionLabel(string actionId, string language)
        {
            if (!_actionLabels.TryGetValue(actionId ?? string.Empty, out Dictionary<string, object> label)) return actionId;
            string text = Json.Text(label, language ?? "en");
            if (text.Length == 0) text = Json.Text(label, "en");
            return text.Length > 0 ? text : actionId;
        }

        /// <summary>Manifest of a mod assembly (cached).</summary>
        /// <exception cref="InvalidOperationException">A resource is missing or invalid.</exception>
        public static LiveManifest For(Assembly assembly)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(assembly, out LiveManifest cached)) return cached;
                LiveManifest manifest = Load(assembly);
                Cache[assembly] = manifest;
                return manifest;
            }
        }

        private static LiveManifest Load(Assembly assembly)
        {
            Dictionary<string, object> manifest = Read(assembly, ManifestResource);
            string uuid = Json.Text(manifest, "applicationUuid").Trim().ToLowerInvariant();
            if (!Guid.TryParse(uuid, out Guid parsed) || parsed.ToString("D") != uuid)
                throw new InvalidOperationException(ManifestResource + ": applicationUuid is not a valid UUID: '" + uuid + "'");
            string code = Json.Text(manifest, "code").Trim();
            if (code.Length == 0) throw new InvalidOperationException(ManifestResource + ": code is missing");
            string name = Json.Text(manifest, "name").Trim();

            Dictionary<string, object> schema = Read(assembly, SchemaResource);
            var actionIds = new List<string>();
            var argumentDefaults = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            var labels = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            foreach (object item in Json.Arr(schema, "actions") ?? new List<object>())
            {
                if (!(item is Dictionary<string, object> action)) continue;
                string id = Json.Text(action, "id");
                if (id.Length == 0) continue;
                actionIds.Add(id);
                argumentDefaults[id] = DefaultsOf(Json.Arr(action, "arguments"));
                Dictionary<string, object> label = Json.Obj(action, "label");
                if (label != null) labels[id] = label;
            }
            Dictionary<string, object> settingDefaults = DefaultsOf(Json.Arr(Json.Obj(schema, "settings"), "fields"));
            return new LiveManifest(uuid, code, name.Length == 0 ? code : name, actionIds, settingDefaults, argumentDefaults, labels);
        }

        /// <summary>fields[].path (or id) → default; dotted paths become nested objects.</summary>
        private static Dictionary<string, object> DefaultsOf(List<object> fields)
        {
            Dictionary<string, object> result = Json.NewObject();
            if (fields == null) return result;
            foreach (object item in fields)
            {
                if (!(item is Dictionary<string, object> field)) continue;
                string path = Json.Text(field, "path");
                if (path.Length == 0) path = Json.Text(field, "id");
                if (path.Length == 0 || !field.TryGetValue("default", out object value) || value == null) continue;
                Dictionary<string, object> parent = result;
                string[] parts = path.Split('.');
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (!(parent.TryGetValue(parts[i], out object existing) && existing is Dictionary<string, object> child))
                    {
                        child = Json.NewObject();
                        parent[parts[i]] = child;
                    }
                    parent = child;
                }
                parent[parts[parts.Length - 1]] = Json.Clone(value);
            }
            return result;
        }

        private static Dictionary<string, object> Read(Assembly assembly, string resource)
        {
            using (Stream stream = assembly.GetManifestResourceStream(resource))
            {
                if (stream == null)
                    throw new InvalidOperationException(assembly.GetName().Name + ": embedded resource '" + resource
                        + "' is missing (<EmbeddedResource Include=\"…\\" + resource + "\" LogicalName=\"" + resource + "\" />).");
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return Json.ParseObject(reader.ReadToEnd());
                }
            }
        }
    }
}
