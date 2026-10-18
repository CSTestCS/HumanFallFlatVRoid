using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HFFVRoidInstaller
{
    /// <summary>Finds Human: Fall Flat and installs / removes BepInEx + the HFF VRoid plugin.</summary>
    internal static class GameInstall
    {
        public const string SteamAppId = "477160";
        public const string GameExe = "Human.exe";
        public const string Marker = "BepInEx/.hffvroid-installed-bepinex";

        // ------------------------------------------------------------------ finding the game

        public static string FindGameFolder()
        {
            foreach (var lib in SteamLibraries())
            {
                try
                {
                    string manifest = Path.Combine(lib, "steamapps", "appmanifest_" + SteamAppId + ".acf");
                    string installDir = "Human Fall Flat";
                    if (File.Exists(manifest))
                    {
                        var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
                        if (m.Success) installDir = m.Groups[1].Value;
                    }
                    string dir = Path.Combine(lib, "steamapps", "common", installDir);
                    if (IsGameFolder(dir)) return dir;
                }
                catch { }
            }
            foreach (var guess in new[]
                     {
                         @"C:\Program Files (x86)\Steam\steamapps\common\Human Fall Flat",
                         @"C:\Program Files\Steam\steamapps\common\Human Fall Flat",
                         @"D:\SteamLibrary\steamapps\common\Human Fall Flat",
                     })
                if (IsGameFolder(guess)) return guess;
            return null;
        }

        private static IEnumerable<string> SteamLibraries()
        {
            var roots = new List<string>();
            AddReg(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            AddReg(roots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
            AddReg(roots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
            var libs = new List<string>();
            foreach (var root in roots)
            {
                if (!libs.Contains(root)) libs.Add(root);
                string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                try
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"(?:path|\\d+)\"\\s+\"([^\"]+)\""))
                    {
                        string p = m.Groups[1].Value.Replace(@"\\", @"\");
                        if (Directory.Exists(p) && !libs.Contains(p)) libs.Add(p);
                    }
                }
                catch { }
            }
            return libs;
        }

        private static void AddReg(List<string> list, RegistryKey hive, string key, string value)
        {
            try
            {
                using (var k = hive.OpenSubKey(key))
                {
                    var v = k != null ? k.GetValue(value) as string : null;
                    if (!string.IsNullOrEmpty(v))
                    {
                        v = v.Replace('/', '\\');
                        if (Directory.Exists(v) && !list.Contains(v)) list.Add(v);
                    }
                }
            }
            catch { }
        }

        public static bool IsGameFolder(string dir)
        {
            return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, GameExe)) &&
                   Directory.Exists(Path.Combine(dir, "Human_Data"));
        }

        public static bool IsGameRunning()
        {
            try { return Process.GetProcessesByName("Human").Length > 0; }
            catch { return false; }
        }

        /// <summary>True for a 64-bit game executable, false for 32-bit.</summary>
        public static bool Is64Bit(string exePath)
        {
            using (var fs = File.OpenRead(exePath))
            using (var br = new BinaryReader(fs))
            {
                fs.Position = 0x3C;
                int pe = br.ReadInt32();
                fs.Position = pe + 4;
                ushort machine = br.ReadUInt16();
                return machine == 0x8664 || machine == 0xAA64;
            }
        }

        public static bool IsInstalled(string dir)
        {
            return File.Exists(Path.Combine(dir, "BepInEx", "plugins", "HFFVRoid", "HFFVRoid.dll"));
        }

        // ------------------------------------------------------------------ install

        public static string Install(string dir)
        {
            var log = new StringBuilder();
            string exe = Path.Combine(dir, GameExe);
            bool x64 = Is64Bit(exe);
            log.AppendLine("Game: " + dir + (x64 ? " (64-bit)" : " (32-bit)"));

            string bepCore = Path.Combine(dir, "BepInEx", "core", "BepInEx.dll");
            string proxy = Path.Combine(dir, "winhttp.dll");
            bool needBepInEx = !File.Exists(bepCore) || !File.Exists(proxy) || ProxyArchMismatch(proxy, x64);
            if (needBepInEx)
            {
                bool fresh = !File.Exists(bepCore);
                ExtractZip(x64 ? "BepInEx_x64.zip" : "BepInEx_x86.zip", dir);
                if (fresh) File.WriteAllText(Path.Combine(dir, Marker), "BepInEx was installed by the HFF VRoid installer.\r\n");
                log.AppendLine("Installed BepInEx 5.4.23.2 (" + (x64 ? "x64" : "x86") + ").");
            }
            else log.AppendLine("BepInEx already installed - kept.");

            EnsureBepInExConfig(dir);

            string pluginDir = Path.Combine(dir, "BepInEx", "plugins", "HFFVRoid");
            Directory.CreateDirectory(pluginDir);
            WriteResource("HFFVRoid.dll", Path.Combine(pluginDir, "HFFVRoid.dll"));
            log.AppendLine("Installed plugin to BepInEx\\plugins\\HFFVRoid.");

            string import = Path.Combine(dir, "BepInEx", "VRoid", "Import");
            Directory.CreateDirectory(import);
            Directory.CreateDirectory(Path.Combine(dir, "BepInEx", "VRoid", "Models"));
            log.AppendLine("VRM drop folder: " + import);
            return log.ToString();
        }

        private static bool ProxyArchMismatch(string proxyDll, bool gameIs64)
        {
            try { return Is64Bit(proxyDll) != gameIs64; }
            catch { return true; }
        }

        /// <summary>The game needs BepInEx's manager object hidden, or it gets destroyed on scene load.</summary>
        private static void EnsureBepInExConfig(string dir)
        {
            string cfgDir = Path.Combine(dir, "BepInEx", "config");
            Directory.CreateDirectory(cfgDir);
            string cfg = Path.Combine(cfgDir, "BepInEx.cfg");
            if (!File.Exists(cfg))
            {
                File.WriteAllText(cfg, "[Chainloader]\r\n\r\n## If enabled, hides BepInEx Manager GameObject from Unity.\r\n" +
                                       "# Setting type: Boolean\r\n# Default value: false\r\nHideManagerGameObject = true\r\n");
                return;
            }
            string text = File.ReadAllText(cfg);
            if (Regex.IsMatch(text, @"^\s*HideManagerGameObject\s*=\s*true", RegexOptions.Multiline | RegexOptions.IgnoreCase)) return;
            if (Regex.IsMatch(text, @"^\s*HideManagerGameObject\s*=", RegexOptions.Multiline))
                text = Regex.Replace(text, @"^(\s*HideManagerGameObject\s*=\s*)\w+", "${1}true", RegexOptions.Multiline);
            else if (text.Contains("[Chainloader]"))
                text = text.Replace("[Chainloader]", "[Chainloader]\r\n\r\nHideManagerGameObject = true\r\n");
            else
                text += "\r\n[Chainloader]\r\n\r\nHideManagerGameObject = true\r\n";
            File.WriteAllText(cfg, text);
        }

        private static Stream Resource(string name)
        {
            var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (s == null) throw new Exception("Installer is damaged: missing " + name);
            return s;
        }

        private static void WriteResource(string name, string target)
        {
            using (var s = Resource(name))
            using (var f = File.Create(target)) s.CopyTo(f);
        }

        private static void ExtractZip(string resource, string dir)
        {
            using (var s = Resource(resource))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                string root = Path.GetFullPath(dir);
                foreach (var e in zip.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(root, e.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard
                    if (string.IsNullOrEmpty(e.Name)) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var es = e.Open())
                    using (var f = File.Create(target)) es.CopyTo(f);
                }
            }
        }

        // ------------------------------------------------------------------ uninstall

        public static string Uninstall(string dir, bool removeBepInExIfOurs)
        {
            var log = new StringBuilder();
            string pluginDir = Path.Combine(dir, "BepInEx", "plugins", "HFFVRoid");
            if (Directory.Exists(pluginDir))
            {
                Directory.Delete(pluginDir, true);
                log.AppendLine("Removed the HFF VRoid plugin.");
            }
            else log.AppendLine("The plugin was not installed.");

            string marker = Path.Combine(dir, Marker);
            if (removeBepInExIfOurs && File.Exists(marker) && !OtherPluginsPresent(dir))
            {
                foreach (var f in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt" })
                {
                    string p = Path.Combine(dir, f);
                    if (File.Exists(p)) File.Delete(p);
                }
                string core = Path.Combine(dir, "BepInEx", "core");
                if (Directory.Exists(core)) Directory.Delete(core, true);
                File.Delete(marker);
                log.AppendLine("Removed BepInEx (it was installed by this installer and no other mods use it).");
            }
            log.AppendLine("Your imported VRM files were kept in BepInEx\\VRoid.");
            return log.ToString();
        }

        private static bool OtherPluginsPresent(string dir)
        {
            string plugins = Path.Combine(dir, "BepInEx", "plugins");
            if (!Directory.Exists(plugins)) return false;
            return Directory.GetFiles(plugins, "*.dll", SearchOption.AllDirectories).Length > 0;
        }
    }
}
