using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HumanAPI;
using UnityEngine;

namespace HFFVRoid
{
    /// <summary>
    /// Keeps imported VRM files and links them to the game's own preset system.
    ///
    /// Each import creates a normal "My Preset" entry (pr:Presets/N, stored by the game) whose description carries
    /// a marker "[vrm:&lt;sha1&gt;]". The .vrm itself is too big for the game's Steam Cloud preset storage, so it is
    /// kept in BepInEx/VRoid/Models/&lt;sha1&gt;.vrm. Because the game copies a preset's description into the player's
    /// active skin when the preset is loaded, the marker follows the player into every level automatically.
    /// </summary>
    internal static class VrmLibrary
    {
        public const string MarkerStart = "[vrm:";
        public const string BaseBody = "builtin:HumanDefaultBody";

        private static readonly Dictionary<string, VrmModel> cache = new Dictionary<string, VrmModel>();
        private static readonly HashSet<string> failed = new HashSet<string>();

        public static string RootDir { get { return Path.Combine(BepInEx.Paths.BepInExRootPath, "VRoid"); } }
        public static string ModelsDir { get { return Path.Combine(RootDir, "Models"); } }
        public static string ImportDir { get { return Path.Combine(RootDir, "Import"); } }

        public static void EnsureDirs()
        {
            Directory.CreateDirectory(ModelsDir);
            Directory.CreateDirectory(ImportDir);
            string readme = Path.Combine(ImportDir, "README.txt");
            if (!File.Exists(readme))
            {
                File.WriteAllText(readme,
                    "Drop .vrm files in this folder, then open Customize Character in Human: Fall Flat.\r\n" +
                    "They are imported automatically and show up under LOAD PRESET.\r\n" +
                    "(You can also use the IMPORT VRM button in the Customize Character menu.)\r\n");
            }
        }

        public static string GetHash(RagdollPresetMetadata preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.description)) return null;
            int i = preset.description.IndexOf(MarkerStart, StringComparison.Ordinal);
            if (i < 0) return null;
            int start = i + MarkerStart.Length;
            int end = preset.description.IndexOf(']', start);
            if (end <= start) return null;
            string h = preset.description.Substring(start, end - start).Trim().ToLowerInvariant();
            foreach (char c in h) if (!Uri.IsHexDigit(c)) return null;
            return h;
        }

        public static string ModelPath(string hash)
        {
            return Path.Combine(ModelsDir, hash + ".vrm");
        }

        /// <summary>Returns the loaded model for a hash, loading it from disk the first time. Null if unavailable.</summary>
        public static VrmModel GetModel(string hash)
        {
            if (hash == null) return null;
            VrmModel m;
            if (cache.TryGetValue(hash, out m) && m.Template != null) return m;
            if (failed.Contains(hash)) return null;
            string path = ModelPath(hash);
            if (!File.Exists(path))
            {
                failed.Add(hash);
                Plugin.Log.LogInfo("VRM " + hash + " is not in the library (" + path + "); using the normal body.");
                return null;
            }
            try
            {
                var t0 = Time.realtimeSinceStartup;
                m = VrmLoader.Load(File.ReadAllBytes(path), hash.Substring(0, 8));
                cache[hash] = m;
                Plugin.Log.LogInfo(string.Format("Loaded VRM '{0}' in {1:0.00}s ({2} spring joints).",
                    m.Info.Title, Time.realtimeSinceStartup - t0, m.Joints.Count));
                return m;
            }
            catch (Exception e)
            {
                failed.Add(hash);
                Plugin.Log.LogError("Failed to load VRM " + path + ": " + e);
                Plugin.Toast("Could not load VRM: " + e.Message, true);
                return null;
            }
        }

        public static string Sha1(byte[] data)
        {
            using (var sha = SHA1.Create())
            {
                var h = sha.ComputeHash(data);
                var sb = new StringBuilder(h.Length * 2);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Imports a .vrm file: validates it, copies it into the library and adds a preset to "Load My Preset".
        /// Returns the new (or existing) preset. Throws with a user-readable message on failure.
        /// </summary>
        public static RagdollPresetMetadata Import(string sourcePath)
        {
            if (!File.Exists(sourcePath)) throw new Exception("File not found: " + sourcePath);
            byte[] data = File.ReadAllBytes(sourcePath);
            VrmInfo info = VrmLoader.ReadInfo(data); // throws if not a humanoid VRM
            string hash = Sha1(data);
            EnsureDirs();
            string dest = ModelPath(hash);
            if (!File.Exists(dest)) File.WriteAllBytes(dest, data);
            failed.Remove(hash);

            // Make sure the model really builds before we add a preset for it.
            if (GetModel(hash) == null) throw new Exception("The VRM could not be loaded (see BepInEx/LogOutput.log).");

            var repo = WorkshopRepository.instance;
            repo.ReloadLocalPresets();
            var presets = repo.presetRepo;
            foreach (var existing in presets.BySource(WorkshopItemSource.LocalWorkshop))
                if (GetHash(existing) == hash) return existing; // already imported

            string title = info.Title;
            if (string.IsNullOrEmpty(title)) title = Path.GetFileNameWithoutExtension(sourcePath);
            if (title.Length > 40) title = title.Substring(0, 40);

            var preset = new RagdollPresetMetadata();
            preset.itemType = WorkshopItemType.RagdollPreset;
            preset.title = title;
            preset.description = "VRM avatar" + (string.IsNullOrEmpty(info.Author) ? "" : " by " + info.Author) +
                                 " (HFF VRoid mod) " + MarkerStart + hash + "]";
            preset.main = new RagdollPresetPartMetadata { modelPath = BaseBody };
            preset.workshopId = 0uL;
            preset.folder = presets.GenerateNewPresetPath();
            preset.Save(preset.folder);
            presets.AddItem(WorkshopItemSource.LocalWorkshop, preset);
            Plugin.Log.LogInfo("Created preset " + preset.folder + " for VRM '" + title + "' (" + hash + ").");

            // Let the game render a thumbnail of the preview character wearing the VRM.
            try
            {
                if (CustomizationController.instance != null)
                    CustomizationController.instance.EnsureThumbnails(new List<RagdollPresetMetadata> { preset });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Thumbnail capture failed: " + e.Message);
            }
            return preset;
        }

        /// <summary>Imports every .vrm dropped into BepInEx/VRoid/Import. Returns the last created preset.</summary>
        public static RagdollPresetMetadata ImportDropFolder()
        {
            RagdollPresetMetadata last = null;
            string[] files;
            try
            {
                EnsureDirs();
                files = Directory.GetFiles(ImportDir, "*.vrm");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cannot read import folder: " + e.Message);
                return null;
            }
            foreach (var f in files)
            {
                try
                {
                    last = Import(f);
                    string done = Path.Combine(ImportDir, "imported");
                    Directory.CreateDirectory(done);
                    string target = Path.Combine(done, Path.GetFileName(f));
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(f, target);
                    Plugin.Toast("Imported \"" + last.title + "\" - find it under LOAD PRESET.", false);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Import of " + f + " failed: " + e);
                    Plugin.Toast("Could not import " + Path.GetFileName(f) + ": " + e.Message, true);
                    try
                    {
                        string bad = Path.Combine(ImportDir, "failed");
                        Directory.CreateDirectory(bad);
                        string target = Path.Combine(bad, Path.GetFileName(f));
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(f, target);
                    }
                    catch { }
                }
            }
            return last;
        }

        /// <summary>
        /// Re-renders the thumbnails of existing VRM presets once after an update that changes how avatars look
        /// (e.g. new sizing). Tracked with a small marker file in BepInEx/VRoid.
        /// </summary>
        public static void RefreshThumbnailsOnce()
        {
            const string version = "thumbs-v2-headtop";
            string marker = Path.Combine(RootDir, "thumbnails.version");
            try
            {
                if (File.Exists(marker) && File.ReadAllText(marker).Trim() == version) return;
                var cc = CustomizationController.instance;
                if (cc == null) return;
                var repo = WorkshopRepository.instance;
                repo.ReloadLocalPresets();
                var list = new List<RagdollPresetMetadata>();
                foreach (var p in repo.presetRepo.BySource(WorkshopItemSource.LocalWorkshop))
                {
                    if (GetHash(p) == null || !File.Exists(ModelPath(GetHash(p)))) continue;
                    FileTools.DeleteFile(p.thumbPath);
                    p.cachedThumbnail = null;
                    p.cachedThumbnailBytes = null;
                    list.Add(p);
                }
                if (list.Count > 0)
                {
                    cc.EnsureThumbnails(list);
                    Plugin.Log.LogInfo("Re-rendered " + list.Count + " VRM preset thumbnail(s).");
                }
                EnsureDirs();
                File.WriteAllText(marker, version);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Thumbnail refresh failed: " + e.Message);
            }
        }

        /// <summary>After a preset is deleted, remove its .vrm if no other preset or player skin still uses it.</summary>
        public static void OnPresetDeleted(RagdollPresetMetadata deleted)
        {
            string hash = GetHash(deleted);
            if (hash == null || !Plugin.CfgDeleteUnusedFiles.Value) return;
            try
            {
                var repo = WorkshopRepository.instance.presetRepo;
                foreach (var p in repo.BySource(WorkshopItemSource.LocalWorkshop))
                    if (p != deleted && GetHash(p) == hash) return;
                for (int i = 0; i < 2; i++)
                    if (GetHash(repo.GetPlayerSkin(i)) == hash) return;
                string path = ModelPath(hash);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Plugin.Log.LogInfo("Deleted unused VRM file " + path);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cleanup after preset delete failed: " + e.Message);
            }
        }
    }
}
