//
// StreamEmber: every product name and installation path of this runtime lives here.
// Upstream ScriptHookRDR2DotNet hard-coded its names and paths next to the .asi; this fork ships as
//
//   <game>\StreamEmber.Runtime.RDR2.asi                       this assembly (C++/CLI + core)
//   <game>\StreamEmber\Runtime\StreamEmber.Scripting.RDR2.dll  scripting API (the former ScriptHookRDRNetAPI.dll)
//   <game>\StreamEmber\Scripts\                                scripts
//   <game>\StreamEmber\Config\Runtime.ini                      settings
//   <game>\StreamEmber\Logs\Runtime.log                        log
//
// Keep upstream merges simple: other files only reference these members.
//

using System;
using System.IO;
using System.Reflection;

namespace RDR2DN
{
    public static class StreamEmberLayout
    {
        /// <summary>Game code used in every product name.</summary>
        public const string GameCode = "RDR2";

        /// <summary>Assembly name of the runtime (.asi). Must match TargetName in ScriptHookRDRDotNet.vcxproj.</summary>
        public const string RuntimeAssemblyName = "StreamEmber.Runtime." + GameCode;

        /// <summary>Assembly name of the scripting API. Must match AssemblyName in the API project.</summary>
        public const string ScriptingAssemblyName = "StreamEmber.Scripting." + GameCode;

        public const string ScriptingFileName = ScriptingAssemblyName + ".dll";

        /// <summary>Major version of the scripting API assembly (the API level, not the product version). Upstream API 2.x.</summary>
        public const int ScriptingApiMajorVersion = 2;

        private static string s_gameDirectory;

        /// <summary>Folder of the game executable (where the .asi lives).</summary>
        public static string GameDirectory
        {
            get
            {
                if (s_gameDirectory == null)
                {
                    string location = typeof(StreamEmberLayout).Assembly.Location;
                    s_gameDirectory = string.IsNullOrEmpty(location)
                        ? AppDomain.CurrentDomain.BaseDirectory
                        : Path.GetDirectoryName(location);
                }
                return s_gameDirectory;
            }
        }

        public static string RootDirectory => Path.Combine(GameDirectory, "StreamEmber");
        public static string RuntimeDirectory => Path.Combine(RootDirectory, "Runtime");
        public static string ScriptsDirectory => Path.Combine(RootDirectory, "Scripts");
        public static string ConfigDirectory => Path.Combine(RootDirectory, "Config");
        public static string LogsDirectory => Path.Combine(RootDirectory, "Logs");

        public static string ConfigFile => Path.Combine(ConfigDirectory, "Runtime.ini");
        public static string LogFile => Path.Combine(LogsDirectory, "Runtime.log");
        public static string ScriptingFile => Path.Combine(RuntimeDirectory, ScriptingFileName);

        /// <summary>Product version (StreamEmber SemVer), from the informational version attribute.</summary>
        public static string ProductVersion
        {
            get
            {
                var attribute = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    typeof(StreamEmberLayout).Assembly, typeof(AssemblyInformationalVersionAttribute));
                return attribute?.InformationalVersion ?? typeof(StreamEmberLayout).Assembly.GetName().Version.ToString(3);
            }
        }

        /// <summary>Resolves a path from Runtime.ini: absolute as is, relative to the game folder otherwise.</summary>
        public static string ResolveGamePath(string path)
        {
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(GameDirectory, path));
        }

        /// <summary>Creates the StreamEmber folders that the runtime writes to. Never throws.</summary>
        public static void EnsureWritableDirectories()
        {
            try
            {
                Directory.CreateDirectory(LogsDirectory);
                Directory.CreateDirectory(ScriptsDirectory);
            }
            catch
            {
                // Read-only installation: logging falls back to nothing, scripts folder simply stays missing
            }
        }

        /// <summary>True for a reference to our scripting API.</summary>
        public static bool IsScriptingAssemblyName(string name)
            => string.Equals(name, ScriptingAssemblyName, StringComparison.OrdinalIgnoreCase);

        /// <summary>True for a reference to this runtime assembly.</summary>
        public static bool IsRuntimeAssemblyName(string name)
            => string.Equals(name, RuntimeAssemblyName, StringComparison.OrdinalIgnoreCase);
    }
}
