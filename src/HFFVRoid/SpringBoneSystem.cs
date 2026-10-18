using System.Collections.Generic;
using UnityEngine;

namespace HFFVRoid
{
    /// <summary>
    /// Verlet spring bones (hair, skirts, accessories), following the VRM 0.x / 1.0 reference algorithm.
    /// All forces and radii are multiplied by the avatar scale so behaviour does not change with size.
    /// </summary>
    internal sealed class SpringBoneSystem
    {
        private sealed class Joint
        {
            public Transform T;
            public Vector3 LocalTail;
            public Vector3 BoneAxis;
            public Quaternion LocalRotation;
            public float Stiffness, GravityPower, Drag, HitRadius;
            public Vector3 GravityDir;
            public Collider[] Colliders;
            public Vector3 CurrentTail, PrevTail;
        }

        private sealed class Collider
        {
            public Transform T;
            public Vector3 Offset, Tail;
            public float Radius;
            public bool Capsule;
        }

        private readonly List<Joint> joints = new List<Joint>();
        private readonly float scale;

        public SpringBoneSystem(VrmModel model, Transform root, float scale)
        {
            this.scale = scale;
            var colliders = new Collider[model.Colliders.Count];
            for (int i = 0; i < colliders.Length; i++)
            {
                var d = model.Colliders[i];
                var t = VrmModel.Resolve(root, d.Path);
                if (t == null) continue;
                colliders[i] = new Collider { T = t, Offset = d.Offset, Tail = d.Tail, Radius = d.Radius, Capsule = d.IsCapsule };
            }
            foreach (var d in model.Joints)
            {
                var t = VrmModel.Resolve(root, d.Path);
                if (t == null) continue;
                Vector3 localTail;
                if (d.TailPath != null)
                {
                    var tail = VrmModel.Resolve(root, d.TailPath);
                    if (tail == null) continue;
                    localTail = t.InverseTransformPoint(tail.position);
                }
                else localTail = d.VirtualTailLocal;
                if (localTail.sqrMagnitude < 1e-12f) continue;
                var cl = new List<Collider>();
                if (d.ColliderIndices != null)
                    foreach (int ci in d.ColliderIndices)
                        if (ci >= 0 && ci < colliders.Length && colliders[ci] != null) cl.Add(colliders[ci]);
                joints.Add(new Joint
                {
                    T = t,
                    LocalTail = localTail,
                    BoneAxis = localTail.normalized,
                    LocalRotation = t.localRotation,
                    Stiffness = d.Stiffness,
                    GravityPower = d.GravityPower,
                    GravityDir = d.GravityDir.sqrMagnitude > 0 ? d.GravityDir.normalized : Vector3.down,
                    Drag = Mathf.Clamp01(d.Drag),
                    HitRadius = d.HitRadius,
                    Colliders = cl.ToArray(),
                });
            }
            Reset();
        }

        public void Reset()
        {
            foreach (var j in joints)
            {
                if (j.T == null) continue;
                j.T.localRotation = j.LocalRotation;
                j.CurrentTail = j.PrevTail = j.T.TransformPoint(j.LocalTail);
            }
        }

        public void Update(float dt)
        {
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 1f / 20f);
            for (int n = 0; n < joints.Count; n++)
            {
                var j = joints[n];
                if (j.T == null) continue;
                Vector3 pos = j.T.position;
                Quaternion parentRot = j.T.parent != null ? j.T.parent.rotation : Quaternion.identity;
                Quaternion restRot = parentRot * j.LocalRotation;
                Vector3 axis = restRot * j.BoneAxis;
                float length = (j.T.TransformPoint(j.LocalTail) - pos).magnitude;

                Vector3 next = j.CurrentTail
                               + (j.CurrentTail - j.PrevTail) * (1f - j.Drag)
                               + axis * (j.Stiffness * dt * scale)
                               + j.GravityDir * (j.GravityPower * dt * scale);
                next = pos + (next - pos).normalized * length;

                float hit = j.HitRadius * scale;
                for (int c = 0; c < j.Colliders.Length; c++)
                {
                    var col = j.Colliders[c];
                    if (col.T == null) continue;
                    Vector3 center = col.T.TransformPoint(col.Offset);
                    if (col.Capsule)
                    {
                        Vector3 b = col.T.TransformPoint(col.Tail);
                        Vector3 ab = b - center;
                        float tt = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(next - center, ab) / ab.sqrMagnitude) : 0f;
                        center = center + ab * tt;
                    }
                    float r = hit + col.Radius * Mathf.Abs(col.T.lossyScale.x);
                    Vector3 d = next - center;
                    if (d.sqrMagnitude < r * r && d.sqrMagnitude > 1e-12f)
                    {
                        next = center + d.normalized * r;
                        next = pos + (next - pos).normalized * length;
                    }
                }

                j.PrevTail = j.CurrentTail;
                j.CurrentTail = next;
                j.T.rotation = Quaternion.FromToRotation(axis, next - pos) * restRot;
            }
        }
    }
}
