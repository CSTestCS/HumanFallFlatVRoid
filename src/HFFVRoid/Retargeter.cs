using System;
using System.Collections.Generic;
using UnityEngine;

namespace HFFVRoid
{
    /// <summary>
    /// Drives a VRM humanoid from the 16 ragdoll segments of a Human: Fall Flat character.
    ///
    /// Both skeletons rest in a T-pose. For each mapped bone we store the offset between the two rest orientations
    /// (plus an optional swing correction so limbs point the same way at rest); each frame:
    ///     vrmBone.rotation = segment.rotation * offset
    /// Unmapped bones (neck, shoulders, fingers, toes...) keep their rest pose relative to their parent.
    /// Pure Unity math, no game types, so it can be unit-tested.
    /// </summary>
    internal sealed class Retargeter
    {
        // Segment order shared by the game side and the VRM side.
        public static readonly string[] VrmNames =
        {
            "hips", "spine", "chest", "head",
            "leftUpperArm", "leftLowerArm", "leftHand",
            "rightUpperArm", "rightLowerArm", "rightHand",
            "leftUpperLeg", "leftLowerLeg", "leftFoot",
            "rightUpperLeg", "rightLowerLeg", "rightFoot",
        };
        public const int Count = 16;
        private const int HIPS = 0, CHEST = 2, HEAD = 3, LARM = 4, RARM = 7, LFOOT = 12, RFOOT = 15;
        // Next segment along each limb (for direction matching), -1 = none.
        private static readonly int[] ChildOf = { -1, 2, 3, -1, 5, 6, -1, 8, 9, -1, 11, 12, -1, 14, 15, -1 };
        // Leaf bones (hands, feet) re-use the correction of their parent so wrists/ankles don't bend.
        private static readonly int[] LeafParent = { -1, -1, -1, -1, -1, -1, 5, -1, -1, 8, -1, -1, 11, -1, -1, 14 };

        private struct BoneMap
        {
            public Transform Hff;
            public Transform Vrm;
            public Quaternion Offset;
        }

        private BoneMap[] maps;
        private Transform root, hffHips, vrmHips;
        private Quaternion hipsRestInv;
        private Vector3 hipsOffset;

        public float Scale { get; private set; }
        public Transform VrmHips { get { return vrmHips; } }

        /// <param name="hRestPos">Rest position of each segment in the ragdoll's root space, in world units.</param>
        /// <param name="hRestRot">Rest rotation of each segment in the ragdoll's root space.</param>
        /// <param name="hSegs">Live segment transforms (null where missing).</param>
        /// <param name="vBones">VRM humanoid bones in the same order (null where missing). The VRM root must be at the
        /// origin with identity rotation and scale 1 when this is called.</param>
        /// <param name="hHeadTop">Top of the game character's head mesh in the ragdoll's rest space (null = unknown).</param>
        /// <param name="vHeadTopLocal">Top of the VRM's head mesh in head-bone local space (null = unknown).</param>
        public static Retargeter Create(Vector3[] hRestPos, Quaternion[] hRestRot, Transform[] hSegs, Transform[] vBones,
            Transform vrmRoot, ScaleMode mode, float multiplier, float verticalOffset, bool matchRestPose,
            Vector3? hHeadTop = null, Vector3? vHeadTopLocal = null)
        {
            foreach (int k in new[] { HIPS, HEAD, LARM, RARM, LFOOT, RFOOT })
                if (vBones[k] == null || hSegs[k] == null) throw new Exception("missing bone " + VrmNames[k]);

            // Facing frames: up = hips->head, right = left arm -> right arm.
            Vector3 hUp = (hRestPos[HEAD] - hRestPos[HIPS]).normalized;
            Vector3 hRight = (hRestPos[RARM] - hRestPos[LARM]).normalized;
            Vector3 vUp = (vBones[HEAD].position - vBones[HIPS].position).normalized;
            Vector3 vRight = (vBones[RARM].position - vBones[LARM].position).normalized;
            Quaternion hFrame = Quaternion.LookRotation(Vector3.Cross(hRight, hUp), hUp);
            Quaternion vFrame = Quaternion.LookRotation(Vector3.Cross(vRight, vUp), vUp);
            Quaternion A = hFrame * Quaternion.Inverse(vFrame); // VRM model space -> ragdoll rest space

            // Swing correction per limb
            var corr = new Quaternion[Count];
            for (int i = 0; i < Count; i++) corr[i] = Quaternion.identity;
            if (matchRestPose)
            {
                for (int i = 0; i < Count; i++)
                {
                    int c = ChildOf[i];
                    if (c == CHEST && (vBones[CHEST] == null || hSegs[CHEST] == null)) c = HEAD;
                    if (c < 0 || vBones[i] == null || vBones[c] == null || hSegs[i] == null || hSegs[c] == null) continue;
                    Vector3 dirH = hRestPos[c] - hRestPos[i];
                    Vector3 dirV = A * (vBones[c].position - vBones[i].position);
                    if (dirH.sqrMagnitude < 1e-10f || dirV.sqrMagnitude < 1e-10f) continue;
                    corr[i] = Quaternion.FromToRotation(dirV, dirH);
                }
                for (int i = 0; i < Count; i++)
                    if (LeafParent[i] >= 0) corr[i] = corr[LeafParent[i]];
            }

            // Pose the VRM in its corrected rest pose (in ragdoll rest space) to measure legs/feet exactly.
            var original = new Quaternion[Count];
            for (int i = 0; i < Count; i++) if (vBones[i] != null) original[i] = vBones[i].rotation;
            for (int i = 0; i < Count; i++) if (vBones[i] != null) vBones[i].rotation = corr[i] * A * original[i];
            Vector3 vHipsC = vBones[HIPS].position, vHeadC = vBones[HEAD].position;
            Vector3 vFeetC = (vBones[LFOOT].position + vBones[RFOOT].position) * 0.5f;
            Vector3 vTopC = vHeadTopLocal.HasValue ? vBones[HEAD].TransformPoint(vHeadTopLocal.Value) : vHeadC;
            for (int i = 0; i < Count; i++) if (vBones[i] != null) vBones[i].rotation = original[i];

            // Scale
            Vector3 hFeet = (hRestPos[LFOOT] + hRestPos[RFOOT]) * 0.5f;
            float hLeg = Vector3.Dot(hRestPos[HIPS] - hFeet, hUp);
            float vLeg = Vector3.Dot(vHipsC - vFeetC, hUp);
            float hHeight = Vector3.Dot(hRestPos[HEAD] - hFeet, hUp);
            float vHeight = Vector3.Dot(vHeadC - vFeetC, hUp);
            float hTop = hHeadTop.HasValue ? Vector3.Dot(hHeadTop.Value - hFeet, hUp) : 0f;
            float vTop = Vector3.Dot(vTopC - vFeetC, hUp);
            float scale;
            // Feet (ankles) are aligned below, so matching ankle-to-head-top distances puts both head tops level.
            if (mode == ScaleMode.HeadTop && hHeadTop.HasValue && vHeadTopLocal.HasValue && hTop > 1e-3f && vTop > 1e-3f)
                scale = hTop / vTop;
            else if ((mode == ScaleMode.Height || mode == ScaleMode.HeadTop) && vHeight > 1e-3f && hHeight > 1e-3f)
                scale = hHeight / vHeight;
            else if (vLeg > 1e-3f && hLeg > 1e-3f) scale = hLeg / vLeg;
            else scale = 1f;
            scale *= Mathf.Max(0.05f, multiplier);

            var r = new Retargeter();
            var list = new List<BoneMap>();
            for (int i = 0; i < Count; i++)
            {
                if (vBones[i] == null || hSegs[i] == null) continue;
                list.Add(new BoneMap
                {
                    Hff = hSegs[i],
                    Vrm = vBones[i],
                    Offset = Quaternion.Inverse(hRestRot[i]) * corr[i] * A * original[i]
                });
            }
            r.maps = list.ToArray();
            r.root = vrmRoot;
            r.hffHips = hSegs[HIPS];
            r.vrmHips = vBones[HIPS];
            r.hipsRestInv = Quaternion.Inverse(hRestRot[HIPS]);
            // Put the VRM's feet where the ragdoll's feet are (in rest pose), then add the vertical offset.
            r.hipsOffset = (hFeet + (vHipsC - vFeetC) * scale) - hRestPos[HIPS] + hUp * verticalOffset;
            r.Scale = scale;
            vrmRoot.localScale = Vector3.one * scale;
            return r;
        }

        /// <summary>Copies the ragdoll's current pose onto the VRM.</summary>
        public void Apply()
        {
            if (hffHips == null) return;
            Quaternion hipsDelta = hffHips.rotation * hipsRestInv;
            Vector3 hipsPos = hffHips.position + hipsDelta * hipsOffset;
            root.position = hipsPos;
            for (int i = 0; i < maps.Length; i++)
            {
                if (maps[i].Hff == null) continue;
                maps[i].Vrm.rotation = maps[i].Hff.rotation * maps[i].Offset;
            }
            vrmHips.position = hipsPos;
        }
    }

    internal enum ScaleMode
    {
        /// <summary>Top of the avatar's head level with the top of the default game character's head.</summary>
        HeadTop,
        /// <summary>Match leg length so the feet touch the ground like the original character.</summary>
        Legs,
        /// <summary>Match total height (feet to head).</summary>
        Height,
    }
}
