using System;
using System.IO;
using System.IO.Compression;
using HFFVRoidInstaller;

static class T
{
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) fails++; }

    static string FakeGame(string bepZip, string arch)
    {
        string dir = Path.Combine(Path.GetTempPath(), "hff-test-" + arch + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Human_Data", "Managed"));
        // Use BepInEx's winhttp.dll of the wanted bitness as a stand-in PE file for Human.exe
        using (var z = ZipFile.OpenRead(bepZip)) z.GetEntry("winhttp.dll").ExtractToFile(Path.Combine(dir, "Human.exe"));
        return dir;
    }

    static int Main(string[] args)
    {
        string payload = args[0];
        foreach (var arch in new[] { "x86", "x64" })
        {
            Console.WriteLine("== " + arch + " game");
            string dir = FakeGame(Path.Combine(payload, "BepInEx_" + arch + ".zip"), arch);
            Check(GameInstall.IsGameFolder(dir), "fake folder recognised");
            Check(GameInstall.Is64Bit(Path.Combine(dir, "Human.exe")) == (arch == "x64"), "bitness detected");
            Console.Write(GameInstall.Install(dir));
            Check(File.Exists(Path.Combine(dir, "winhttp.dll")), "winhttp.dll proxy");
            Check(GameInstall.Is64Bit(Path.Combine(dir, "winhttp.dll")) == (arch == "x64"), "proxy matches game bitness");
            Check(File.Exists(Path.Combine(dir, "BepInEx", "core", "BepInEx.Preloader.dll")), "BepInEx core");
            Check(File.Exists(Path.Combine(dir, "BepInEx", "plugins", "HFFVRoid", "HFFVRoid.dll")), "plugin");
            Check(Directory.Exists(Path.Combine(dir, "BepInEx", "VRoid", "Import")), "import folder");
            string cfg = Path.Combine(dir, "BepInEx", "config", "BepInEx.cfg");
            Check(File.ReadAllText(cfg).Contains("HideManagerGameObject = true"), "HideManagerGameObject enabled (new cfg)");
            // simulate BepInEx having written its full config with the default false
            File.WriteAllText(cfg, "[Caching]\nEnableAssemblyCache = true\n\n[Chainloader]\n\nHideManagerGameObject = false\n\n[Logging]\nUnityLogListening = true\n");
            Console.Write(GameInstall.Install(dir));
            string t = File.ReadAllText(cfg);
            Check(t.Contains("HideManagerGameObject = true") && !t.Contains("= false") && t.Contains("UnityLogListening"), "existing cfg patched, rest kept");
            // Another mod present -> uninstall must keep BepInEx
            File.WriteAllText(Path.Combine(dir, "BepInEx", "plugins", "Other.dll"), "x");
            Console.Write(GameInstall.Uninstall(dir, true));
            Check(!GameInstall.IsInstalled(dir) && File.Exists(Path.Combine(dir, "winhttp.dll")), "uninstall keeps BepInEx when other mods exist");
            File.Delete(Path.Combine(dir, "BepInEx", "plugins", "Other.dll"));
            Console.Write(GameInstall.Install(dir));
            Console.Write(GameInstall.Uninstall(dir, true));
            Check(!File.Exists(Path.Combine(dir, "winhttp.dll")) && !Directory.Exists(Path.Combine(dir, "BepInEx", "core")), "uninstall removes BepInEx it installed");
            Check(Directory.Exists(Path.Combine(dir, "BepInEx", "VRoid")), "VRM library kept");
            Directory.Delete(dir, true);
        }
        Console.WriteLine(fails == 0 ? "ALL CHECKS PASSED" : fails + " CHECK(S) FAILED");
        return fails == 0 ? 0 : 1;
    }
}
