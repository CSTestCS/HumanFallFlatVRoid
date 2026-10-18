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

            Vector3? hHeadTop = null;
            if (Plugin.CfgScaleMode.Value == ScaleMode.HeadTop && segs[3] != null && segs[3].transform != null)
                hHeadTop = GameHeadTop(ragdoll, rootScale, restPos, restRot, segs[3]);
            retargeter = Retargeter.Create(restPos, restRot, segT, vBones, root, Plugin.CfgScaleMode.Value,
                Plugin.CfgScaleMultiplier.Value, Plugin.CfgVerticalOffset.Value, Plugin.CfgMatchRestPose.Value,
                hHeadTop, model.HasHeadTop ? (Vector3?)model.HeadTopLocal : null);

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

        // ------------------------------------------------------------------ head-top of the default game character

        private const int HEAD = 3, HIPS = 0;
        private static Vector3? cachedHeadTop; // in the head segment's rest frame (world units)
        private static bool cacheLoaded;
        private static string CacheFile { get { return System.IO.Path.Combine(VrmLibrary.RootDir, "hff_head_top.txt"); } }

        /// <summary>
        /// Top of the default character's head in the ragdoll's rest space. Measured from the game's own body mesh
        /// whenever the ragdoll stands in its rest pose (always true in Customize Character), remembered relative to
        /// the head segment for use in levels, with an estimate from the head collider as a last resort.
        /// </summary>
        private Vector3? GameHeadTop(Ragdoll ragdoll, float rootScale, Vector3[] restPos, Quaternion[] restRot, HumanSegment head)
        {
            Vector3 hUp = (restPos[HEAD] - restPos[HIPS]).normalized;
            Quaternion invHead = Quaternion.Inverse(restRot[HEAD]);
            LoadCache();

            // 1. Measure the mesh if the ragdoll is at rest right now.
            try
            {
                Vector3 headNow = ragdoll.transform.InverseTransformPoint(head.transform.position) * rootScale;
                if ((headNow - restPos[HEAD]).magnitude < 0.01f)
                {
                    float best = float.NegativeInfinity;
                    Vector3 bestP = Vector3.zero;
                    foreach (var r in FindGameRenderers())
                    {
                        Vector3[] verts = null;
                        Matrix4x4 m = r.transform.localToWorldMatrix;
                        var smr = r as SkinnedMeshRenderer;
                        if (smr != null && smr.sharedMesh != null)
                        {
                            var baked = new Mesh();
                            smr.BakeMesh(baked); // same convention as the game's own RagdollModelSkinnedMesh
                            verts = baked.vertices;
                            Destroy(baked);
                        }
                        else
                        {
                            var mf = r.GetComponent<MeshFilter>();
                            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable) verts = mf.sharedMesh.vertices;
                        }
                        if (verts == null) continue;
                        foreach (var v in verts)
                        {
                            Vector3 p = ragdoll.transform.InverseTransformPoint(m.MultiplyPoint3x4(v)) * rootScale;
                            float d = Vector3.Dot(p, hUp);
                            if (d > best) { best = d; bestP = p; }
                        }
                    }
                    float aboveHead = best - Vector3.Dot(restPos[HEAD], hUp);
                    if (!float.IsNegativeInfinity(best) && aboveHead > 0f && aboveHead < 1f)
                    {
                        Vector3 local = invHead * (bestP - restPos[HEAD]);
                        if (!cachedHeadTop.HasValue || (cachedHeadTop.Value - local).magnitude > 0.001f)
                        {
                            cachedHeadTop = local;
                            SaveCache();
                            Plugin.Log.LogInfo("Measured default character head top: " + aboveHead.ToString("0.000") + " m above the head joint.");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not measure the character's head: " + e.Message);
            }
            if (cachedHeadTop.HasValue) return restPos[HEAD] + restRot[HEAD] * cachedHeadTop.Value;

            // 2. Estimate from the head collider (top of its local bounding box).
            var col = head.collider;
            if (col == null) return null;
            var pts = new List<Vector3>();
            Vector3 c = Vector3.zero, ext = Vector3.zero;
            var sphere = col as SphereCollider;
            var capsule = col as CapsuleCollider;
            var box = col as BoxCollider;
            if (sphere != null) { c = sphere.center; ext = Vector3.one * sphere.radius; }
            else if (capsule != null)
            {
                c = capsule.center;
                ext = Vector3.one * capsule.radius;
                ext[capsule.direction] = Mathf.Max(capsule.radius, capsule.height * 0.5f);
            }
            else if (box != null) { c = box.center; ext = box.size * 0.5f; }
            else return null;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        pts.Add(c + new Vector3(sx * ext.x, sy * ext.y, sz * ext.z));
            Vector3 hs = head.transform.lossyScale;
            float bestC = float.NegativeInfinity;
            Vector3 top = restPos[HEAD];
            foreach (var lp in pts)
            {
                // Collider space == segment local space; place it in the rest pose.
                Vector3 p = restPos[HEAD] + restRot[HEAD] * Vector3.Scale(lp, hs);
                float d = Vector3.Dot(p, hUp);
                if (d > bestC) { bestC = d; top = p; }
            }
            return top;
        }

        private static void LoadCache()
        {
            if (cacheLoaded) return;
            cacheLoaded = true;
            try
            {
                if (!System.IO.File.Exists(CacheFile)) return;
                var parts = System.IO.File.ReadAllText(CacheFile).Trim().Split(' ');
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                cachedHeadTop = new Vector3(float.Parse(parts[0], inv), float.Parse(parts[1], inv), float.Parse(parts[2], inv));
            }
            catch { cachedHeadTop = null; }
        }

        private static void SaveCache()
        {
            try
            {
                var v = cachedHeadTop.Value;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                System.IO.File.WriteAllText(CacheFile, v.x.ToString("R", inv) + " " + v.y.ToString("R", inv) + " " + v.z.ToString("R", inv));
            }
            catch { }
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
