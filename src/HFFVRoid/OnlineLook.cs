using System;
using System.Collections.Generic;
using HarmonyLib;
using HumanAPI;
using UnityEngine;
using Button = UnityEngine.UI.Button;

namespace HFFVRoid
{
    /// <summary>
    /// "Online look": a normal (non-VRM) preset that other players see while you wear a VRM avatar.
    ///
    /// The game only sends your saved skin preset (part model names, colours and painted textures) to other
    /// players. A VRM preset is just such a preset with a marker in its description, so whatever body parts it
    /// carries are what unmodded players see. This class copies the tagged preset's parts, colours and paint into
    /// every VRM preset (and the skin you are wearing). Locally those parts stay hidden behind the VRM.
    /// </summary>
    internal static class OnlineLook
    {
        private static readonly WorkshopItemType[] Parts =
        {
            WorkshopItemType.ModelFull, WorkshopItemType.ModelHead, WorkshopItemType.ModelUpperBody, WorkshopItemType.ModelLowerBody
        };

        public static string TaggedFolder
        {
            get { return Plugin.CfgOnlineLook.Value ?? ""; }
            set { Plugin.CfgOnlineLook.Value = value ?? ""; }
        }

        public static bool IsTagged(RagdollPresetMetadata p)
        {
            return p != null && !string.IsNullOrEmpty(p.folder) && p.folder == TaggedFolder;
        }

        /// <summary>The tagged preset, or null for the plain default Human.</summary>
        public static RagdollPresetMetadata FindTagged()
        {
            string f = TaggedFolder;
            if (string.IsNullOrEmpty(f) || WorkshopRepository.instance == null) return null;
            foreach (var p in WorkshopRepository.instance.presetRepo.BySource(WorkshopItemSource.LocalWorkshop))
                if (p.folder == f && VrmLibrary.GetHash(p) == null) return p;
            return null;
        }

        public static bool IsDefaultBody(RagdollPresetMetadata p)
        {
            return p != null && p.main != null && p.main.modelPath == VrmLibrary.BaseBody &&
                   RagdollPresetPartMetadata.IsEmpty(p.head) && RagdollPresetPartMetadata.IsEmpty(p.upperBody) &&
                   RagdollPresetPartMetadata.IsEmpty(p.lowerBody) &&
                   string.IsNullOrEmpty(p.main.color1) && string.IsNullOrEmpty(p.main.color2) && string.IsNullOrEmpty(p.main.color3);
        }

        /// <summary>Gives a VRM preset the online look's parts, colours and paint. Returns true if anything changed.</summary>
        public static bool ApplyTo(RagdollPresetMetadata vrm, RagdollPresetMetadata source)
        {
            if (vrm == null || VrmLibrary.GetHash(vrm) == null || string.IsNullOrEmpty(vrm.folder)) return false;
            bool changed = false;
            foreach (var part in Parts)
            {
                RagdollPresetPartMetadata want;
                if (source != null) want = RagdollPresetPartMetadata.Clone(source.GetPart(part));
                else want = part == WorkshopItemType.ModelFull ? new RagdollPresetPartMetadata { modelPath = VrmLibrary.BaseBody } : null;
                if (!SamePart(vrm.GetPart(part), want))
                {
                    vrm.SetPart(part, want);
                    changed = true;
                }
                // Painted texture for this part
                string target = FileTools.Combine(vrm.folder, part.ToString() + ".png");
                byte[] wantBytes = null;
                if (source != null && want != null && !string.IsNullOrEmpty(source.folder))
                    wantBytes = FileTools.ReadAllBytes(FileTools.Combine(source.folder, part.ToString() + ".png"));
                byte[] haveBytes = FileTools.ReadAllBytes(target);
                if (!SameBytes(haveBytes, wantBytes))
                {
                    if (wantBytes != null) FileTools.WriteAllBytes(target, wantBytes);
                    else FileTools.DeleteFile(target);
                    changed = true;
                }
            }
            if (changed)
            {
                vrm.Save(vrm.folder);
                // The game caches the serialized skin and its checksum (sent to other players); drop the stale copies.
                try
                {
                    AccessTools.Field(typeof(RagdollPresetMetadata), "serialized").SetValue(vrm, null);
                    AccessTools.Field(typeof(RagdollPresetMetadata), "md5").SetValue(vrm, null);
                }
                catch (Exception e) { Plugin.Log.LogWarning("Could not reset skin cache: " + e.Message); }
            }
            return changed;
        }

        public static bool ApplyTo(RagdollPresetMetadata vrm)
        {
            return ApplyTo(vrm, FindTagged());
        }

        /// <summary>Brings every VRM preset and the worn skins in line with the current online look.</summary>
        public static void SyncAll()
        {
            var repo = WorkshopRepository.instance;
            if (repo == null) return;
            repo.ReloadLocalPresets();
            var source = FindTagged();
            if (source == null && !string.IsNullOrEmpty(TaggedFolder))
            {
                Plugin.Log.LogInfo("Online look preset " + TaggedFolder + " no longer exists; using the default Human.");
                TaggedFolder = "";
            }
            int n = 0;
            foreach (var p in repo.presetRepo.BySource(WorkshopItemSource.LocalWorkshop))
                if (ApplyTo(p, source)) n++;
            for (int i = 0; i < 2; i++)
                if (ApplyTo(repo.presetRepo.GetPlayerSkin(i), source)) n++;
            if (n > 0) Plugin.Log.LogInfo("Online look '" + (source != null ? source.title : "default Human") + "' applied to " + n + " VRM preset(s)/skin(s).");
        }

        private static bool SamePart(RagdollPresetPartMetadata a, RagdollPresetPartMetadata b)
        {
            if (RagdollPresetPartMetadata.IsEmpty(a) && RagdollPresetPartMetadata.IsEmpty(b)) return true;
            if (a == null || b == null) return false;
            return a.modelPath == b.modelPath && (a.color1 ?? "") == (b.color1 ?? "") &&
                   (a.color2 ?? "") == (b.color2 ?? "") && (a.color3 ?? "") == (b.color3 ?? "");
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || a.Length == 0) return b == null || b.Length == 0;
            if (b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ------------------------------------------------------------------ "Load My Preset" button

        public const string ButtonName = "HFFVRoid_OnlineLookButton";
        private static GameObject button;

        public static void Inject(CustomizationPresetMenu menu)
        {
            if (menu == null || menu.deleteButton == null) return;
            if (button == null)
            {
                foreach (Transform t in menu.deleteButton.transform.parent)
                    if (t.name == ButtonName) button = t.gameObject;
            }
            if (button == null)
            {
                button = MenuInjector.CloneButton(menu.deleteButton, ButtonName, "SET AS ONLINE LOOK", () => OnClick(menu));
                var group = menu.deleteButton.GetComponentInParent<ButtonGroup>();
                if (group != null) group.RebuildLinks();
                Plugin.Log.LogInfo("Added SET AS ONLINE LOOK button to Load My Preset.");
            }
            Refresh(menu);
        }

        private static RagdollPresetMetadata Selected(CustomizationPresetMenu menu)
        {
            return Traverse.Create(menu).Field("selectedItem").GetValue<RagdollPresetMetadata>();
        }

        /// <summary>Shown in "Load My Preset" for normal presets; hidden for VRM presets and in other modes.</summary>
        public static void Refresh(CustomizationPresetMenu menu)
        {
            if (button == null || menu == null) return;
            var sel = Selected(menu);
            bool show = CustomizationPresetMenu.mode == CustomizationPresetMenuMode.Load && !menu.showSubscribed &&
                        sel != null && VrmLibrary.GetHash(sel) == null && !string.IsNullOrEmpty(sel.folder);
            if (button.activeSelf != show) button.SetActive(show);
            if (show) MenuInjector.SetLabel(button, IsTagged(sel) ? "CLEAR ONLINE LOOK" : "SET AS ONLINE LOOK");
        }

        private static void OnClick(CustomizationPresetMenu menu)
        {
            if (!MenuSystem.CanInvoke) return;
            var sel = Selected(menu);
            if (sel == null || VrmLibrary.GetHash(sel) != null) return;
            try
            {
                if (IsTagged(sel))
                {
                    TaggedFolder = "";
                    Plugin.Toast("Online look cleared. Other players will see a plain default Human while you wear a VRM.", false);
                }
                else
                {
                    TaggedFolder = sel.folder;
                    Plugin.Toast("Other players will now see \"" + sel.title + "\" while you wear a VRM avatar.", false);
                }
                SyncAll();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Setting the online look failed: " + e);
                Plugin.Toast("Could not set the online look: " + e.Message, true);
            }
            Refresh(menu);
            // Re-label the carousel so the "(online)" tag moves.
            foreach (var item in menu.GetComponentsInChildren<CustomizationPresetMenuItem>(true))
                Relabel(item, item.data as RagdollPresetMetadata);
        }

        /// <summary>Adds " (online)" to the tagged preset's name in the carousel.</summary>
        public static void DecorateItem(CustomizationPresetMenuItem item, object data)
        {
            var p = data as RagdollPresetMetadata;
            if (p != null && IsTagged(p)) Relabel(item, p);
        }

        private static void Relabel(CustomizationPresetMenuItem item, RagdollPresetMetadata p)
        {
            if (item == null || p == null) return;
            var t = Traverse.Create(item);
            var label = t.Field("labelComponent").GetValue<Component>();
            if (label == null) return;
            var tmp = label as TMPro.TextMeshProUGUI;
            var txt = label as UnityEngine.UI.Text;
            string text = IsTagged(p) ? p.title + " (online)" : p.title;
            if (tmp != null) tmp.text = text;
            else if (txt != null) txt.text = text;
        }
    }
}
