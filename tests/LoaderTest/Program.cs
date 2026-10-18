using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HFFVRoid
{
    // Stand-ins for the BepInEx plugin statics the loader uses.
    internal sealed class Entry<T> { public T Value; public Entry(T v) { Value = v; } }
    internal sealed class Logger
    {
        public void LogInfo(object o) { Console.WriteLine("  [info] " + o); }
        public void LogWarning(object o) { Console.WriteLine("  [warn] " + o); }
        public void LogError(object o) { Console.WriteLine("  [error] " + o); }
    }
    internal static class Plugin
    {
        public static Logger Log = new Logger();
        public static Entry<string> CfgShaderNames = new Entry<string>("Standard");
        public static Entry<float> CfgShadeLift = new Entry<float>(0.35f);
    }

    internal static class Program
    {
        static int failures;
        static void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("  FAIL: " + what); } }

        static float AngleBetween(Vector3 a, Vector3 b)
        {
            return (float)(Math.Acos(Math.Max(-1, Math.Min(1, Vector3.Dot(a.normalized, b.normalized)))) * 180 / Math.PI);
        }

        /// <summary>
        /// Uses one VRM's skeleton as a stand-in for the game's ragdoll (with random local axes on every segment and a
        /// rotated root, like the customization turntable) and checks that another VRM follows its poses.
        /// </summary>
        static void RetargetTest(string hffFile, string vrmFile)
        {
            Console.WriteLine("== retarget: " + Path.GetFileName(vrmFile) + " driven by " + Path.GetFileName(hffFile) + " skeleton");
            var hffModel = VrmLoader.Load(File.ReadAllBytes(hffFile), "hff");
            var ragdollRoot = new GameObject("ragdoll").transform;
            ragdollRoot.localPosition = new Vector3(3, 0.2f, -1);
            ragdollRoot.localRotation = Quaternion.AngleAxis(30, Vector3.up);
            hffModel.Template.transform.SetParent(ragdollRoot, false);
            var rnd = new Random(7);
            var segs = new Transform[Retargeter.Count];
            var src = new Transform[Retargeter.Count];
            var restPos = new Vector3[Retargeter.Count];
            var restRot = new Quaternion[Retargeter.Count];
            for (int i = 0; i < Retargeter.Count; i++)
            {
                var bone = VrmModel.Resolve(hffModel.Template.transform, hffModel.HumanBones[Retargeter.VrmNames[i]]);
                src[i] = bone;
                // segment with arbitrary local axes rigidly attached to the bone
                var seg = new GameObject("seg" + i).transform;
                seg.SetParent(bone, false);
                seg.localRotation = Quaternion.AngleAxis((float)rnd.NextDouble() * 360, new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f));
                segs[i] = seg;
                restPos[i] = ragdollRoot.InverseTransformPoint(seg.position);
                restRot[i] = Quaternion.Inverse(ragdollRoot.rotation) * seg.rotation;
            }

            var vrm = VrmLoader.Load(File.ReadAllBytes(vrmFile), "vrm");
            var root = vrm.Template.transform;
            var vb = new Transform[Retargeter.Count];
            for (int i = 0; i < Retargeter.Count; i++)
            {
                int[] path;
                if (vrm.HumanBones.TryGetValue(Retargeter.VrmNames[i], out path)) vb[i] = VrmModel.Resolve(root, path);
            }
            // Head top of the stand-in "game character", in the ragdoll's rest space.
            var hffHead = VrmModel.Resolve(hffModel.Template.transform, hffModel.HumanBones["head"]);
            var hTopWorld = hffHead.TransformPoint(hffModel.HeadTopLocal);
            Vector3 hTopRest = ragdollRoot.InverseTransformPoint(hTopWorld);
            var rt = Retargeter.Create(restPos, restRot, segs, vb, root, ScaleMode.HeadTop, 1f, 0f, true,
                hTopRest, vrm.HeadTopLocal);
            var vHead = vb[3];
            rt.Apply();
            var vTopWorld = vHead.TransformPoint(vrm.HeadTopLocal);
            var upW = ragdollRoot.rotation * Vector3.up;
            float topGap = Vector3.Dot(vTopWorld - hTopWorld, upW);
            Console.WriteLine("  HeadTop scale " + rt.Scale.ToString("0.000") + ": head-top height difference " + topGap.ToString("0.0000") + " m");
            if (hffModel.HasHeadTop && vrm.HasHeadTop) Check(Math.Abs(topGap) < 0.01f, "HeadTop: head tops level");
            Func<Transform[], int, int, Vector3> Dir = (arr, a, b) => arr[b].position - arr[a].position;
            Action<string> Compare = label =>
            {
                rt.Apply();
                float worst = 0; string worstName = "";
                int[,] limbs = { { 0, 3 }, { 4, 5 }, { 5, 6 }, { 7, 8 }, { 8, 9 }, { 10, 11 }, { 11, 12 }, { 13, 14 }, { 14, 15 } };
                for (int k = 0; k < limbs.GetLength(0); k++)
                {
                    float ang = AngleBetween(Dir(src, limbs[k, 0], limbs[k, 1]), Dir(vb, limbs[k, 0], limbs[k, 1]));
                    if (ang > worst) { worst = ang; worstName = Retargeter.VrmNames[limbs[k, 0]] + "->" + Retargeter.VrmNames[limbs[k, 1]]; }
                }
                Vector3 hf = (src[12].position + src[15].position) * 0.5f, vf = (vb[12].position + vb[15].position) * 0.5f;
                Console.WriteLine("  " + label + ": worst limb direction error " + worst.ToString("0.0") + " deg (" + worstName + "), feet gap " + (hf - vf).magnitude.ToString("0.000") + " m, scale " + rt.Scale.ToString("0.000"));
                Check(worst < 3f, label + ": limbs follow the ragdoll");
                Check((hf - vf).magnitude < 0.05f, label + ": feet stay on the ragdoll's feet");
                // same facing: character's left hand on the same side
                var hRight = src[7].position - src[4].position; var vRight = vb[7].position - vb[4].position;
                Check(Vector3.Dot(hRight, vRight) > 0, label + ": left/right not mirrored");
            };
            Compare("rest pose");
            // Pose 1: arms down and forward, one knee bent
            src[4].rotation = Quaternion.AngleAxis(70, ragdollRoot.rotation * Vector3.forward) * src[4].rotation;
            src[7].rotation = Quaternion.AngleAxis(-40, ragdollRoot.rotation * Vector3.up) * src[7].rotation;
            src[8].rotation = Quaternion.AngleAxis(60, ragdollRoot.rotation * Vector3.up) * src[8].rotation;
            src[11].rotation = Quaternion.AngleAxis(-80, ragdollRoot.rotation * Vector3.right) * src[11].rotation;
            Compare("posed");
            // Pose 2: whole body falls flat on its face
            src[0].rotation = Quaternion.AngleAxis(90, ragdollRoot.rotation * Vector3.right) * src[0].rotation;
            src[0].position = src[0].position + new Vector3(0, -0.5f, 0.3f);
            Compare("fallen");
        }

        static IEnumerable<Transform> All(Transform t)
        {
            yield return t;
            for (int i = 0; i < t.childCount; i++) foreach (var c in All(t.GetChild(i))) yield return c;
        }

        static int Main(string[] args)
        {
            foreach (var file in args)
            {
                Console.WriteLine("== " + Path.GetFileName(file));
                var bytes = File.ReadAllBytes(file);
                var info = VrmLoader.ReadInfo(bytes);
                Console.WriteLine("  title='" + info.Title + "' author='" + info.Author + "' spec=" + info.SpecMajor);
                var model = VrmLoader.Load(bytes, "fallback");
                var root = model.Template.transform;

                // Torso and legs must have bind pose == rest pose. Some VRM 1.0 files bind arms/fingers in a different pose
                // than their rest pose (glTF allows it; verified against the raw file), so those are not checked.
                var core = new HashSet<Transform>();
                foreach (var n in new[] { "hips", "spine", "chest", "head", "leftUpperLeg", "leftLowerLeg", "rightUpperLeg", "rightLowerLeg" })
                    if (model.HumanBones.ContainsKey(n)) core.Add(VrmModel.Resolve(root, model.HumanBones[n]));
                int smrCount = 0, meshCount = 0; double maxErr = 0; int verts = 0;
                foreach (var t in All(root))
                    foreach (var c in t.gameObject.components)
                    {
                        if (c is MeshFilter) meshCount++;
                        var smr = c as SkinnedMeshRenderer;
                        if (smr == null) continue;
                        smrCount++;
                        var mesh = smr.sharedMesh;
                        verts += mesh.vertices.Count;
                        Check(mesh.bindposes.Length == smr.bones.Length, "bindpose count");
                        for (int b = 0; b < smr.bones.Length; b++)
                        {
                            if (!core.Contains(smr.bones[b])) continue;
                            var m = smr.bones[b].localToWorldMatrix * mesh.bindposes[b] * smr.transform.worldToLocalMatrix;
                            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++)
                                    maxErr = Math.Max(maxErr, Math.Abs(m[i, j] - (i == j ? 1 : 0)));
                        }
                        foreach (var kv in mesh.tris)
                            foreach (var idx in kv.Value) if (idx < 0 || idx >= mesh.vertices.Count) { Check(false, "triangle index out of range"); break; }
                        foreach (var w in mesh.boneWeights)
                        {
                            if (w.boneIndex0 >= smr.bones.Length || w.boneIndex1 >= smr.bones.Length || w.boneIndex2 >= smr.bones.Length || w.boneIndex3 >= smr.bones.Length) { Check(false, "bone index out of range"); break; }
                            float s = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                            if (Math.Abs(s - 1) > 1e-3) { Check(false, "weights don't sum to 1"); break; }
                        }
                        Check(mesh.subMeshCount == smr.sharedMaterials.Length, "submesh/material count");
                    }
                Console.WriteLine("  skinned renderers=" + smrCount + " static meshes=" + meshCount + " vertices=" + verts + " max bindpose error=" + maxErr.ToString("0.00000"));
                Check(smrCount + meshCount > 0, "has meshes");
                Check(maxErr < 0.02, "bind poses match rest skeleton"); // VRM1 twist sample has 0.0118 in the raw file

                Func<string, Vector3> P = n => VrmModel.Resolve(root, model.HumanBones[n]).position;
                Vector3 hips = P("hips"), head = P("head"), lh = P("leftHand"), rh = P("rightHand"), lf = P("leftFoot"), rf = P("rightFoot");
                Console.WriteLine("  hips=" + hips + " head=" + head + " leftHand=" + lh + " rightHand=" + rh + " leftFoot=" + lf);
                Check(head.y > hips.y && hips.y > lf.y, "upright");
                // (A model without any head-driven mesh has no head top; the mod then falls back to Height scaling.)
                Check(model.HasHeadTop || smrCount == 0, "head top found");
                if (model.HasHeadTop) {
                var headT = VrmModel.Resolve(root, model.HumanBones["head"]);
                var topW = headT.TransformPoint(model.HeadTopLocal);
                Console.WriteLine("  head top=" + topW + " (" + (topW.y - head.y).ToString("0.000") + " m above head joint)");
                Check(topW.y > head.y + 0.05f && topW.y < head.y + 0.6f, "head top is plausible");
                }
                Check(Math.Abs(lh.y - P("leftUpperArm").y) < 0.1f, "T-pose (left arm horizontal)");
                // Character's right is +X in Unity when facing +Z. Facing direction = Cross(right, up).
                var right = (P("rightUpperArm") - P("leftUpperArm")).normalized;
                var fwd = Vector3.Cross(right, (head - hips).normalized);
                Console.WriteLine("  facing=" + fwd + " humanBones=" + model.HumanBones.Count + " springJoints=" + model.Joints.Count + " colliders=" + model.Colliders.Count);

                // Spring bones: simulate a second and make sure nothing explodes.
                var springs = new SpringBoneSystem(model, root, 0.6f);
                for (int i = 0; i < 120; i++)
                {
                    root.localPosition = new Vector3((float)Math.Sin(i * 0.2) * 0.3f, 0, 0);
                    springs.Update(1f / 60f);
                }
                bool finite = true;
                foreach (var t in All(root))
                {
                    var q = t.localRotation;
                    if (float.IsNaN(q.x) || float.IsNaN(q.w) || float.IsInfinity(q.x)) finite = false;
                }
                Check(finite, "spring bones stay finite");
            }
            if (args.Length >= 2) RetargetTest(args[1], args[0]);
            Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : failures + " CHECK(S) FAILED");
            return failures == 0 ? 0 : 1;
        }
    }
}
