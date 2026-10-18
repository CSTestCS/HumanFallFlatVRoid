using System;
using System.Collections.Generic;
using HumanAPI;
using UnityEngine;

namespace HFFVRoid
{
    /// <summary>
    /// Lives on a ragdoll that wears a VRM preset. Hides the game's own body meshes, spawns a copy of the VRM and
    /// drives its humanoid bones from the ragdoll's physics segments every frame (see <see cref="Retargeter"/>).
    /// </summary>
    internal sealed class VrmAvatar : MonoBehaviour
    {
        private RagdollCustomization customization;
        private string hash;
        private VrmModel model;
        private GameObject instance;
        private Retargeter retargeter;
        private readonly List<Renderer> hidden = new List<Renderer>();
        private SpringBoneSystem springs;
        private Vector3 lastHipsPos;
        private int frame;

        // ------------------------------------------------------------------ entry point from the ApplyPreset patch

        public static void OnPresetApplied(RagdollCustomization c, RagdollPresetMetadata preset)
        {
            if (c == null) return;
            string h = VrmLibrary.GetHash(preset);
            var avatar = c.GetComponent<VrmAvatar>();
            VrmModel m = h != null ? VrmLibrary.GetModel(h) : null;
            if (m == null)
            {
                // Not a VRM preset, or the model file is not on this PC (e.g. another player's VRM): normal body.
                if (avatar != null) { avatar.Teardown(); Destroy(avatar); }
                return;
            }
            if (avatar == null) avatar = c.gameObject.AddComponent<VrmAvatar>();
            avatar.Setup(c, m, h);
        }

        // ------------------------------------------------------------------ setup

        private void Setup(RagdollCustomization c, VrmModel m, string h)
        {
            customization = c;
            if (instance == null || hash != h)
            {
                Teardown();
                hash = h;
                model = m;
                try
                {
                    Build();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Could not attach VRM to character: " + e);
                    Teardown();
                    return;
                }
            }
            HideGameBody();
            retargeter.Apply();
            if (springs != null) springs.Reset();
            lastHipsPos = retargeter.VrmHips.position;
        }

        private static HumanSegment[] Segments(Ragdoll r)
        {
            // Same order as Retargeter.VrmNames
            return new[]
            {
                r.partHips, r.partWaist, r.partChest, r.partHead,
                r.partLeftArm, r.partLeftForearm, r.partLeftHand,
                r.partRightArm, r.partRightForearm, r.partRightHand,
                r.partLeftThigh, r.partLeftLeg, r.partLeftFoot,
                r.partRightThigh, r.partRightLeg, r.partRightFoot,
            };
        }

        private void Build()
        {
            var ragdoll = customization.ragdoll != null ? customization.ragdoll : GetComponent<Ragdoll>();
            if (ragdoll == null || ragdoll.partHips == null) throw new Exception("character has no ragdoll segments");

            instance = (GameObject)Instantiate(model.Template);
            instance.name = model.Template.name;
            var root = instance.transform;
            root.SetParent(null, false);
            // Its lifetime follows this component (OnDestroy), not the scene: player characters can survive level loads.
            DontDestroyOnLoad(instance);
            root.position = Vector3.zero;
            root.rotation = Quaternion.identity;
            root.localScale = Vector3.one;

            var segs = Segments(ragdoll);
            if (Plugin.CfgMirror.Value)
            {
                // Swap left/right segments in case the game's naming is mirrored.
                for (int i = 4; i <= 6; i++) { var t = segs[i]; segs[i] = segs[i + 3]; segs[i + 3] = t; }
                for (int i = 10; i <= 12; i++) { var t = segs[i]; segs[i] = segs[i + 3]; segs[i + 3] = t; }
            }
            float rootScale = Mathf.Abs(ragdoll.transform.lossyScale.y);
            if (rootScale < 1e-4f) rootScale = 1f;

            var restPos = new Vector3[Retargeter.Count];
            var restRot = new Quaternion[Retargeter.Count];
            var segT = new Transform[Retargeter.Count];
            var vBones = new Transform[Retargeter.Count];
            for (int i = 0; i < Retargeter.Count; i++)
            {
                restRot[i] = Quaternion.identity;
                if (segs[i] != null && segs[i].transform != null)
                {
                    // bindPose = segment.worldToLocal * ragdollRoot.localToWorld, captured in the T-pose on Awake.
                    Matrix4x4 inv = segs[i].bindPose.inverse;
                    restPos[i] = (Vector3)inv.GetColumn(3) * rootScale;
                    restRot[i] = Quaternion.LookRotation(inv.GetColumn(2), inv.GetColumn(1));
                    segT[i] = segs[i].transform;
                }
                int[] path;
                if (model.HumanBones.TryGetValue(Retargeter.VrmNames[i], out path)) vBones[i] = VrmModel.Resolve(root, path);
            }

            retargeter = Retargeter.Create(restPos, restRot, segT, vBones, root, Plugin.CfgScaleMode.Value,
                Plugin.CfgScaleMultiplier.Value, Plugin.CfgVerticalOffset.Value, Plugin.CfgMatchRestPose.Value);

            // Match the layer the game uses for this character so its cameras (incl. the thumbnail camera) see it.
            int layer = gameObject.layer;
            var r0 = FindGameRenderers();
            if (r0.Count > 0) layer = r0[0].gameObject.layer;
            foreach (var t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

            instance.SetActive(enabled && gameObject.activeInHierarchy);
            retargeter.Apply();
            if (Plugin.CfgSpringBones.Value && model.Joints.Count > 0)
                springs = new SpringBoneSystem(model, root, retargeter.Scale);
            Plugin.Log.LogInfo("Attached VRM '" + model.Info.Title + "' to " + name + " (scale " + retargeter.Scale.ToString("0.000") + ").");
        }

        private List<Renderer> FindGameRenderers()
        {
            var result = new List<Renderer>();
            if (customization == null) return result;
            foreach (var m in new RagdollModel[] { customization.main, customization.head, customization.upper, customization.lower })
            {
                if (m == null) continue;
                result.AddRange(m.GetComponentsInChildren<Renderer>(true));
            }
            return result;
        }

        private void HideGameBody()
        {
            RestoreGameBody();
            RefreshHidden();
        }

        // Adds any game body renderer we are not hiding yet (safety net in case the game rebuilds meshes).
        private void RefreshHidden()
        {
            foreach (var r in FindGameRenderers())
            {
                if (r == null || hidden.Contains(r)) continue;
                hidden.Add(r);
                r.enabled = false;
            }
        }

        private void RestoreGameBody()
        {
            foreach (var r in hidden) if (r != null) r.enabled = true;
            hidden.Clear();
        }

        public void Teardown()
        {
            RestoreGameBody();
            if (instance != null) Destroy(instance);
            instance = null;
            springs = null;
            retargeter = null;
            hash = null;
            model = null;
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (instance == null && model != null && customization != null && Time.frameCount % 30 == 0)
            {
                // The avatar object was destroyed behind our back (e.g. by a scene change): rebuild it.
                var m = model; var h = hash;
                Teardown();
                Setup(customization, m, h);
            }
            if (instance == null || retargeter == null) return;
            if (++frame % 60 == 0) { hidden.RemoveAll(r => r == null); RefreshHidden(); }
            for (int i = 0; i < hidden.Count; i++)
                if (hidden[i] != null && hidden[i].enabled) hidden[i].enabled = false;
            retargeter.Apply();
            Vector3 hips = retargeter.VrmHips.position;
            if (springs != null)
            {
                if ((hips - lastHipsPos).magnitude > 2f) springs.Reset(); // respawn / teleport
                else springs.Update(Time.deltaTime);
            }
            lastHipsPos = hips;
        }

        private void OnEnable()
        {
            if (instance != null) instance.SetActive(true);
        }

        private void OnDisable()
        {
            if (instance != null) instance.SetActive(false);
        }

        private void OnDestroy()
        {
            Teardown();
        }
    }
}
