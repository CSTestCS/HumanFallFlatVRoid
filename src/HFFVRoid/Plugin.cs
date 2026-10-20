using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using HumanAPI;
using UnityEngine;

namespace HFFVRoid
{
    [BepInPlugin(Guid, "HFF VRoid (VRM avatar importer)", Version)]
    [BepInProcess("Human.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.hffvroid.vrmimport";
        public const string Version = "1.2.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        internal static ConfigEntry<ScaleMode> CfgScaleMode;
        internal static ConfigEntry<float> CfgScaleMultiplier;
        internal static ConfigEntry<float> CfgVerticalOffset;
        internal static ConfigEntry<float> CfgShadeLift;
        internal static ConfigEntry<bool> CfgSpringBones;
        internal static ConfigEntry<bool> CfgMatchRestPose;
        internal static ConfigEntry<bool> CfgMirror;
        internal static ConfigEntry<string> CfgShaderNames;
        internal static ConfigEntry<bool> CfgDeleteUnusedFiles;
        internal static ConfigEntry<string> CfgOnlineLook;

        private readonly FileDialog dialog = new FileDialog();
        private CustomizationController lastController;
        private bool pendingDropScan;

        private static string toastText;
        private static bool toastError;
        private static float toastUntil;
        private GUIStyle toastStyle;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // (New key name so installs that already saved the old "ScaleMode = Legs" default get the new HeadTop default.)
            CfgScaleMode = Config.Bind("Avatar", "SizeMode", ScaleMode.HeadTop,
                "How the VRM is sized. HeadTop = top of the avatar's head level with the top of a default Human's head. " +
                "Legs = match leg length. Height = match ankle-to-head-joint height.");
            CfgScaleMultiplier = Config.Bind("Avatar", "ScaleMultiplier", 1f, "Extra size multiplier applied after automatic scaling.");
            CfgVerticalOffset = Config.Bind("Avatar", "VerticalOffset", 0f, "Moves the avatar up (+) or down (-) in metres.");
            CfgMatchRestPose = Config.Bind("Avatar", "MatchRestPose", true,
                "Bend the VRM's limbs to match the game character's rest pose before following it. Turn off to use the VRM's own T-pose.");
            CfgMirror = Config.Bind("Avatar", "SwapLeftRight", false,
                "Swap the avatar's left and right limbs. Only needed if arms/legs move on the wrong side.");
            CfgSpringBones = Config.Bind("Avatar", "SpringBones", true, "Simulate VRM hair / skirt / accessory physics.");
            CfgShadeLift = Config.Bind("Rendering", "ShadeLift", 0.35f,
                "0..1. Brightens shadowed parts of the avatar to mimic VRM toon shading. 0 = plain lighting.");
            CfgShaderNames = Config.Bind("Rendering", "Shaders", "Standard,Legacy Shaders/Diffuse,Diffuse,Unlit/Texture",
                "Comma separated list of shaders to try for VRM materials (first one found is used).");
            CfgDeleteUnusedFiles = Config.Bind("Library", "DeleteUnusedFiles", true,
                "When a VRM preset is deleted in game, also delete its .vrm copy from BepInEx/VRoid/Models if nothing else uses it.");

            try { VrmLibrary.EnsureDirs(); }
            catch (Exception e) { Log.LogWarning("Could not create library folders: " + e.Message); }

            CfgOnlineLook = Config.Bind("Multiplayer", "OnlineLookPreset", "",
                "Preset folder (e.g. pr:Presets/3) that other players see while you wear a VRM. Set it in game with " +
                "LOAD PRESET > select a normal preset > SET AS ONLINE LOOK. Empty = plain default Human.");

            // Patch each hook separately so one missing game method (after a game update) doesn't disable the rest.
            var harmony = new Harmony(Guid);
            foreach (var patch in new[] { typeof(Patch_ApplyPreset), typeof(Patch_RootMenuFocus), typeof(Patch_DeletePreset),
                         typeof(Patch_PresetMenuFocus), typeof(Patch_PresetMenuButtons), typeof(Patch_PresetItemBind),
                         typeof(Patch_SelectSkinPreset) })
            {
                try { harmony.CreateClassProcessor(patch).Patch(); }
                catch (Exception e) { Log.LogError("Could not apply " + patch.Name + " - this game version may not be supported: " + e.Message); }
            }
            Log.LogInfo("HFF VRoid " + Version + " loaded. VRM library: " + VrmLibrary.RootDir);
        }

        internal static void Toast(string text, bool error)
        {
            toastText = text;
            toastError = error;
            toastUntil = Time.realtimeSinceStartup + (error ? 8f : 5f);
            if (error) Log.LogWarning(text); else Log.LogInfo(text);
        }

        internal void OpenImportDialog()
        {
            if (dialog.IsOpen) return;
            string start = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            dialog.Open("Choose a VRM avatar to import into Human: Fall Flat", start);
            toastText = "Choose a .vrm file in the window that opened (press Alt+Tab if you can't see it).";
            toastError = false;
            toastUntil = float.MaxValue;
        }

        private void Update()
        {
            string path, err;
            if (dialog.TryGetResult(out path, out err))
            {
                toastUntil = 0;
                if (err != null) Toast("The file dialog failed: " + err + ". Put .vrm files in " + VrmLibrary.ImportDir + " instead.", true);
                else if (path != null) ImportFromDialog(path);
            }

            // Pick up files dropped into BepInEx/VRoid/Import each time the customization scene is entered.
            var cc = CustomizationController.instance;
            if (cc != lastController)
            {
                lastController = cc;
                pendingDropScan = cc != null;
            }
            if (pendingDropScan && cc != null && cc.activeCustomization != null && MenuSystem.instance != null &&
                MenuSystem.instance.activeMenu is CustomizationRootMenu && MenuSystem.CanInvoke)
            {
                pendingDropScan = false;
                try { VrmLibrary.RefreshThumbnailsOnce(); }
                catch (Exception e) { Log.LogError("Thumbnail refresh failed: " + e); }
                try { VrmLibrary.ImportDropFolder(); }
                catch (Exception e) { Log.LogError("Import folder scan failed: " + e); }
                try { OnlineLook.SyncAll(); }
                catch (Exception e) { Log.LogError("Online look sync failed: " + e); }
            }
        }

        private void ImportFromDialog(string path)
        {
            try
            {
                RagdollPresetMetadata preset = VrmLibrary.Import(path);
                Toast("Imported \"" + preset.title + "\". It is now in LOAD PRESET.", false);
                MenuInjector.ShowImported(preset);
            }
            catch (Exception e)
            {
                Log.LogError("Import failed: " + e);
                Toast("Could not import VRM: " + e.Message, true);
            }
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(toastText) || Time.realtimeSinceStartup > toastUntil) return;
            if (toastStyle == null)
            {
                toastStyle = new GUIStyle(GUI.skin.box);
                toastStyle.wordWrap = true;
                toastStyle.alignment = TextAnchor.MiddleCenter;
                toastStyle.padding = new RectOffset(16, 16, 10, 10);
            }
            toastStyle.fontSize = Mathf.Max(14, Screen.height / 45);
            float w = Mathf.Min(Screen.width - 40, Screen.width * 0.6f);
            float h = toastStyle.CalcHeight(new GUIContent(toastText), w) + 4;
            var rect = new Rect((Screen.width - w) / 2f, Screen.height * 0.82f - h, w, h);
            var old = GUI.color;
            GUI.color = toastError ? new Color(1f, 0.6f, 0.6f, 0.95f) : new Color(1f, 1f, 1f, 0.95f);
            GUI.Box(rect, toastText, toastStyle);
            GUI.Box(rect, toastText, toastStyle); // draw twice for a more opaque background
            GUI.color = old;
        }
    }

    // ---------------------------------------------------------------------- Harmony patches

    [HarmonyPatch(typeof(RagdollCustomization), "ApplyPreset")]
    internal static class Patch_ApplyPreset
    {
        private static void Postfix(RagdollCustomization __instance)
        {
            try { VrmAvatar.OnPresetApplied(__instance, __instance.preset); }
            catch (Exception e) { Plugin.Log.LogError("VRM apply failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(CustomizationRootMenu), "OnGotFocus")]
    internal static class Patch_RootMenuFocus
    {
        private static void Postfix(CustomizationRootMenu __instance)
        {
            try { MenuInjector.Inject(__instance); }
            catch (Exception e) { Plugin.Log.LogError("Adding IMPORT VRM button failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(PresetRepository), "DeletePreset")]
    internal static class Patch_DeletePreset
    {
        private static void Postfix(RagdollPresetMetadata preset)
        {
            try { VrmLibrary.OnPresetDeleted(preset); }
            catch (Exception e) { Plugin.Log.LogError("VRM cleanup failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(CustomizationPresetMenu), "OnGotFocus")]
    internal static class Patch_PresetMenuFocus
    {
        private static void Postfix(CustomizationPresetMenu __instance)
        {
            try { OnlineLook.Inject(__instance); }
            catch (Exception e) { Plugin.Log.LogError("Adding SET AS ONLINE LOOK button failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(CustomizationPresetMenu), "BindButtons")]
    internal static class Patch_PresetMenuButtons
    {
        private static void Postfix(CustomizationPresetMenu __instance)
        {
            try { OnlineLook.Refresh(__instance); }
            catch (Exception e) { Plugin.Log.LogError("Online look button update failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(CustomizationPresetMenuItem), "Bind")]
    internal static class Patch_PresetItemBind
    {
        private static void Postfix(CustomizationPresetMenuItem __instance, object data)
        {
            try { OnlineLook.DecorateItem(__instance, data); }
            catch (Exception e) { Plugin.Log.LogError("Preset label update failed: " + e); }
        }
    }

    // Wearing a VRM preset: make sure it carries the current online look before the game saves it as your skin.
    [HarmonyPatch(typeof(PresetRepository), "SelectSkinPreset")]
    internal static class Patch_SelectSkinPreset
    {
        private static void Prefix(RagdollPresetMetadata preset)
        {
            try { OnlineLook.ApplyTo(preset); }
            catch (Exception e) { Plugin.Log.LogError("Online look apply failed: " + e); }
        }
    }
}
