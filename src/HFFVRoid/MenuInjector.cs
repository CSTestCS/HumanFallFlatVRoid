using System;
using System.Collections.Generic;
using HarmonyLib;
using HumanAPI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Button = UnityEngine.UI.Button;

namespace HFFVRoid
{
    /// <summary>Adds an "IMPORT VRM" button under "LOAD PRESET" in the Customize Character menu.</summary>
    internal static class MenuInjector
    {
        public const string ButtonName = "HFFVRoid_ImportVrmButton";
        public const string Label = "IMPORT VRM";

        private static CustomizationRootMenu activeMenu;

        public static void Inject(CustomizationRootMenu menu)
        {
            if (menu == null) return;
            activeMenu = menu;
            Transform container = menu.menuButtons != null ? menu.menuButtons.transform : menu.transform;
            foreach (var t in container.GetComponentsInChildren<Transform>(true))
                if (t.name == ButtonName) return; // already there

            Button load = FindButton(container, "LoadClick", "LOAD");
            if (load == null)
            {
                Plugin.Log.LogWarning("Could not find the LOAD PRESET button; IMPORT VRM button not added.");
                return;
            }

            var parent = load.transform.parent;
            var siblings = new List<RectTransform>();
            for (int i = 0; i < parent.childCount; i++)
            {
                var rt = parent.GetChild(i) as RectTransform;
                if (rt != null && rt.GetComponent<Selectable>() != null) siblings.Add(rt);
            }

            var go = UnityEngine.Object.Instantiate(load.gameObject, parent, false);
            go.name = ButtonName;
            go.transform.SetSiblingIndex(load.transform.GetSiblingIndex() + 1);

            // Remove localisation helpers so the game doesn't rename our button back to "LOAD PRESET".
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string tn = c.GetType().Name;
                if (tn.IndexOf("Locali", StringComparison.OrdinalIgnoreCase) >= 0)
                    UnityEngine.Object.DestroyImmediate(c);
            }

            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(OnImportClicked);

            foreach (var tmp in go.GetComponentsInChildren<TextMeshProUGUI>(true)) tmp.text = Label;
            foreach (var txt in go.GetComponentsInChildren<Text>(true)) txt.text = Label;

            // Manual layout: if the buttons are not in a layout group, shift the ones below down by one slot.
            if (parent.GetComponent<LayoutGroup>() == null)
            {
                var loadRt = (RectTransform)load.transform;
                int idx = siblings.IndexOf(loadRt);
                Vector2 step = Vector2.zero;
                if (idx >= 0 && idx + 1 < siblings.Count) step = siblings[idx + 1].anchoredPosition - loadRt.anchoredPosition;
                else if (idx > 0) step = loadRt.anchoredPosition - siblings[idx - 1].anchoredPosition;
                if (step != Vector2.zero)
                {
                    ((RectTransform)go.transform).anchoredPosition = loadRt.anchoredPosition + step;
                    for (int i = idx + 1; i < siblings.Count; i++) siblings[i].anchoredPosition += step;
                }
            }

            if (menu.menuButtons != null) menu.menuButtons.RebuildLinks();
            Plugin.Log.LogInfo("Added IMPORT VRM button to the Customize Character menu.");
        }

        private static Button FindButton(Transform container, string method, string labelStart)
        {
            var buttons = container.GetComponentsInChildren<Button>(true);
            foreach (var b in buttons)
            {
                var ev = b.onClick;
                for (int i = 0; i < ev.GetPersistentEventCount(); i++)
                    if (ev.GetPersistentMethodName(i) == method) return b;
            }
            foreach (var b in buttons)
            {
                var tmp = b.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null && tmp.text != null && tmp.text.Trim().ToUpperInvariant().StartsWith(labelStart)) return b;
                var txt = b.GetComponentInChildren<Text>(true);
                if (txt != null && txt.text != null && txt.text.Trim().ToUpperInvariant().StartsWith(labelStart)) return b;
            }
            return null;
        }

        private static void OnImportClicked()
        {
            if (!MenuSystem.CanInvoke) return;
            var cc = CustomizationController.instance;
            if (cc == null || cc.activeCustomization == null) return;
            Plugin.Instance.OpenImportDialog();
        }

        /// <summary>Called after a successful import: preview it and open "Load My Preset" with it selected.</summary>
        public static void ShowImported(RagdollPresetMetadata preset)
        {
            var cc = CustomizationController.instance;
            if (preset == null || cc == null || cc.activeCustomization == null) return;
            try
            {
                var set = AccessTools.Method(typeof(PresetRepository), "SetSkinPresetReference");
                if (set != null) set.Invoke(WorkshopRepository.instance.presetRepo, new object[] { cc.activePlayer, preset.folder });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not pre-select the new preset: " + e.Message);
            }
            cc.LoadPreset(preset);
            if (activeMenu != null && MenuSystem.instance != null && MenuSystem.instance.activeMenu == activeMenu)
                activeMenu.LoadClick();
        }
    }
}
