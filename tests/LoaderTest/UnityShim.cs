// Minimal stand-in for the parts of the UnityEngine API used by the VRM loader, so the loader can be
// exercised by a plain .NET test program (no Unity install needed). Math follows Unity's conventions.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(0, 0); } }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); }
        public static bool operator ==(Vector2 a, Vector2 b) { return a.x == b.x && a.y == b.y; }
        public static bool operator !=(Vector2 a, Vector2 b) { return !(a == b); }
        public override bool Equals(object o) { return o is Vector2 && this == (Vector2)o; }
        public override int GetHashCode() { return x.GetHashCode() ^ y.GetHashCode(); }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(0, 0, 0); } }
        public static Vector3 one { get { return new Vector3(1, 1, 1); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 down { get { return new Vector3(0, -1, 0); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public static Vector3 right { get { return new Vector3(1, 0, 0); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector3 normalized { get { float m = magnitude; return m > 1e-5f ? this / m : zero; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator -(Vector3 a) { return new Vector3(-a.x, -a.y, -a.z); }
        public static Vector3 operator *(Vector3 a, float d) { return new Vector3(a.x * d, a.y * d, a.z * d); }
        public static Vector3 operator *(float d, Vector3 a) { return a * d; }
        public static Vector3 operator /(Vector3 a, float d) { return new Vector3(a.x / d, a.y / d, a.z / d); }
        public static bool operator ==(Vector3 a, Vector3 b) { return (a - b).sqrMagnitude < 1e-10f; }
        public static bool operator !=(Vector3 a, Vector3 b) { return !(a == b); }
        public override bool Equals(object o) { return o is Vector3 && this == (Vector3)o; }
        public override int GetHashCode() { return x.GetHashCode(); }
        public static float Dot(Vector3 a, Vector3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        public static Vector3 Cross(Vector3 a, Vector3 b) { return new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        public static Vector3 Scale(Vector3 a, Vector3 b) { return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z); }
        public static implicit operator Vector3(Vector4 v) { return new Vector3(v.x, v.y, v.z); }
        public override string ToString() { return string.Format("({0:0.000}, {1:0.000}, {2:0.000})", x, y, z); }
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity { get { return new Quaternion(0, 0, 0, 1); } }
        public static Quaternion operator *(Quaternion l, Quaternion r)
        {
            return new Quaternion(
                l.w * r.x + l.x * r.w + l.y * r.z - l.z * r.y,
                l.w * r.y + l.y * r.w + l.z * r.x - l.x * r.z,
                l.w * r.z + l.z * r.w + l.x * r.y - l.y * r.x,
                l.w * r.w - l.x * r.x - l.y * r.y - l.z * r.z);
        }
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            var u = new Vector3(q.x, q.y, q.z);
            var t = 2f * Vector3.Cross(u, v);
            return v + q.w * t + Vector3.Cross(u, t);
        }
        public static Quaternion Inverse(Quaternion q) { return new Quaternion(-q.x, -q.y, -q.z, q.w); }
        public static Quaternion AngleAxis(float deg, Vector3 axis)
        {
            axis = axis.normalized; float h = deg * (float)Math.PI / 360f; float s = (float)Math.Sin(h);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, (float)Math.Cos(h));
        }
        public static Quaternion FromToRotation(Vector3 a, Vector3 b)
        {
            a = a.normalized; b = b.normalized;
            float d = Vector3.Dot(a, b);
            if (d > 0.999999f) return identity;
            if (d < -0.999999f)
            {
                var axis = Vector3.Cross(Vector3.right, a);
                if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(Vector3.up, a);
                return AngleAxis(180, axis);
            }
            var c = Vector3.Cross(a, b);
            var q = new Quaternion(c.x, c.y, c.z, 1 + d);
            float m = (float)Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            var f = forward.normalized;
            var r = Vector3.Cross(up, f).normalized;
            var u = Vector3.Cross(f, r);
            // rotation matrix columns r,u,f -> quaternion
            float m00 = r.x, m01 = u.x, m02 = f.x, m10 = r.y, m11 = u.y, m12 = f.y, m20 = r.z, m21 = u.z, m22 = f.z;
            float tr = m00 + m11 + m22;
            Quaternion q;
            if (tr > 0) { float s = (float)Math.Sqrt(tr + 1) * 2; q = new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s); }
            else if (m00 > m11 && m00 > m22) { float s = (float)Math.Sqrt(1 + m00 - m11 - m22) * 2; q = new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s); }
            else if (m11 > m22) { float s = (float)Math.Sqrt(1 + m11 - m00 - m22) * 2; q = new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s); }
            else { float s = (float)Math.Sqrt(1 + m22 - m00 - m11) * 2; q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s); }
            return q;
        }
        public static float Angle(Quaternion a, Quaternion b)
        {
            float d = Math.Abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w);
            return (float)(Math.Acos(Math.Min(1, d)) * 2 * 180 / Math.PI);
        }
    }

    public struct Matrix4x4
    {
        public float m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33;
        public float this[int r, int c]
        {
            get { return Get(r * 4 + c); }
            set { Set(r * 4 + c, value); }
        }
        private float Get(int i)
        {
            switch (i)
            {
                case 0: return m00; case 1: return m01; case 2: return m02; case 3: return m03;
                case 4: return m10; case 5: return m11; case 6: return m12; case 7: return m13;
                case 8: return m20; case 9: return m21; case 10: return m22; case 11: return m23;
                case 12: return m30; case 13: return m31; case 14: return m32; default: return m33;
            }
        }
        private void Set(int i, float v)
        {
            switch (i)
            {
                case 0: m00 = v; break; case 1: m01 = v; break; case 2: m02 = v; break; case 3: m03 = v; break;
                case 4: m10 = v; break; case 5: m11 = v; break; case 6: m12 = v; break; case 7: m13 = v; break;
                case 8: m20 = v; break; case 9: m21 = v; break; case 10: m22 = v; break; case 11: m23 = v; break;
                case 12: m30 = v; break; case 13: m31 = v; break; case 14: m32 = v; break; default: m33 = v; break;
            }
        }
        public static Matrix4x4 identity { get { var m = new Matrix4x4(); m.m00 = m.m11 = m.m22 = m.m33 = 1; return m; } }
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b)
        {
            var r = new Matrix4x4();
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++)
                {
                    float s = 0; for (int k = 0; k < 4; k++) s += a[i, k] * b[k, j]; r[i, j] = s;
                }
            return r;
        }
        public Vector4 GetColumn(int c) { return new Vector4(this[0, c], this[1, c], this[2, c], this[3, c]); }
        public Vector3 MultiplyPoint3x4(Vector3 p)
        {
            return new Vector3(m00 * p.x + m01 * p.y + m02 * p.z + m03, m10 * p.x + m11 * p.y + m12 * p.z + m13, m20 * p.x + m21 * p.y + m22 * p.z + m23);
        }
        public static Matrix4x4 TRS(Vector3 t, Quaternion q, Vector3 s)
        {
            var m = identity;
            float x = q.x, y = q.y, z = q.z, w = q.w;
            float[,] r = {
                { 1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w) },
                { 2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w) },
                { 2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y) } };
            for (int i = 0; i < 3; i++) { m[i, 0] = r[i, 0] * s.x; m[i, 1] = r[i, 1] * s.y; m[i, 2] = r[i, 2] * s.z; }
            m.m03 = t.x; m.m13 = t.y; m.m23 = t.z;
            return m;
        }
        public Matrix4x4 inverse
        {
            get
            {
                var a = new double[4, 8];
                for (int i = 0; i < 4; i++) { for (int j = 0; j < 4; j++) a[i, j] = this[i, j]; a[i, 4 + i] = 1; }
                for (int c = 0; c < 4; c++)
                {
                    int p = c; for (int r = c + 1; r < 4; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
                    for (int k = 0; k < 8; k++) { var t = a[c, k]; a[c, k] = a[p, k]; a[p, k] = t; }
                    double d = a[c, c]; for (int k = 0; k < 8; k++) a[c, k] /= d;
                    for (int r = 0; r < 4; r++) if (r != c) { double f = a[r, c]; for (int k = 0; k < 8; k++) a[r, k] -= f * a[c, k]; }
                }
                var m = new Matrix4x4();
                for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) m[i, j] = (float)a[i, 4 + j];
                return m;
            }
        }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return new Color(1, 1, 1, 1); } }
        public static Color black { get { return new Color(0, 0, 0, 1); } }
        public float maxColorComponent { get { return Math.Max(r, Math.Max(g, b)); } }
    }

    public struct BoneWeight
    {
        public int boneIndex0, boneIndex1, boneIndex2, boneIndex3;
        public float weight0, weight1, weight2, weight3;
    }

    public static class Mathf
    {
        public static float Sqrt(float f) { return (float)Math.Sqrt(f); }
        public static float Abs(float f) { return Math.Abs(f); }
        public static float Min(float a, float b) { return Math.Min(a, b); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static int Max(int a, int b) { return Math.Max(a, b); }
        public static float Clamp01(float f) { return Math.Max(0, Math.Min(1, f)); }
        public static int Clamp(int v, int lo, int hi) { return Math.Max(lo, Math.Min(hi, v)); }
    }

    public static class Time
    {
        public static float realtimeSinceStartup { get { return (float)(DateTime.Now - DateTime.Today).TotalSeconds; } }
    }

    public class Object
    {
        public string name;
        public static void Destroy(Object o) { }
        public static void DestroyImmediate(Object o) { }
        public static void DontDestroyOnLoad(Object o) { }
        public static implicit operator bool(Object o) { return !ReferenceEquals(o, null); }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
    }

    public sealed class GameObject : Object
    {
        public readonly Transform transform;
        public readonly List<Component> components = new List<Component>();
        public bool activeSelf = true;
        public int layer;
        public GameObject(string name)
        {
            this.name = name;
            transform = new Transform();
            transform.gameObject = this;
            components.Add(transform);
        }
        public void SetActive(bool v) { activeSelf = v; }
        public T AddComponent<T>() where T : Component, new()
        {
            var c = new T(); c.gameObject = this; components.Add(c); return c;
        }
    }

    public class Transform : Component
    {
        private Transform _parent;
        private readonly List<Transform> children = new List<Transform>();
        public Vector3 localPosition = Vector3.zero;
        public Quaternion localRotation = Quaternion.identity;
        public Vector3 localScale = Vector3.one;
        public new string name { get { return gameObject.name; } set { gameObject.name = value; } }
        public Transform parent { get { return _parent; } }
        public int childCount { get { return children.Count; } }
        public Transform GetChild(int i) { return children[i]; }
        public int GetSiblingIndex() { return _parent == null ? 0 : _parent.children.IndexOf(this); }
        public void SetParent(Transform p, bool worldPositionStays)
        {
            if (_parent != null) _parent.children.Remove(this);
            _parent = p;
            if (p != null) p.children.Add(this);
        }
        public Matrix4x4 localToWorldMatrix
        {
            get
            {
                var m = Matrix4x4.TRS(localPosition, localRotation, localScale);
                return _parent == null ? m : _parent.localToWorldMatrix * m;
            }
        }
        public Matrix4x4 worldToLocalMatrix { get { return localToWorldMatrix.inverse; } }
        public Vector3 position
        {
            get { return localToWorldMatrix.GetColumn(3); }
            set { localPosition = _parent == null ? value : _parent.InverseTransformPoint(value); }
        }
        public Quaternion rotation
        {
            get { return _parent == null ? localRotation : _parent.rotation * localRotation; }
            set { localRotation = _parent == null ? value : Quaternion.Inverse(_parent.rotation) * value; }
        }
        public Vector3 lossyScale { get { var s = localScale; return _parent == null ? s : Vector3.Scale(_parent.lossyScale, s); } }
        public Vector3 TransformPoint(Vector3 p) { return localToWorldMatrix.MultiplyPoint3x4(p); }
        public Vector3 InverseTransformPoint(Vector3 p) { return worldToLocalMatrix.MultiplyPoint3x4(p); }
    }

    public class Renderer : Component
    {
        public bool enabled = true;
        public Material[] sharedMaterials;
    }

    public enum SkinQuality { Auto, Bone1, Bone2, Bone4 }

    public sealed class SkinnedMeshRenderer : Renderer
    {
        public Mesh sharedMesh;
        public Transform[] bones;
        public Transform rootBone;
        public bool updateWhenOffscreen;
        public SkinQuality quality;
    }

    public sealed class MeshRenderer : Renderer { }
    public sealed class MeshFilter : Component { public Mesh sharedMesh; }

    public sealed class Mesh : Object
    {
        public UnityEngine.Rendering.IndexFormat indexFormat;
        public List<Vector3> vertices, normals;
        public List<Vector2> uv;
        public BoneWeight[] boneWeights;
        public Matrix4x4[] bindposes;
        public int subMeshCount;
        public readonly Dictionary<int, int[]> tris = new Dictionary<int, int[]>();
        public void SetVertices(List<Vector3> v) { vertices = new List<Vector3>(v); }
        public void SetNormals(List<Vector3> v) { normals = new List<Vector3>(v); }
        public void SetUVs(int ch, List<Vector2> v) { uv = new List<Vector2>(v); }
        public void SetTriangles(int[] t, int sub, bool calc) { tris[sub] = t; }
        public void RecalculateBounds() { }
        public void RecalculateNormals() { }
    }

    public enum TextureFormat { RGBA32 = 4 }
    public enum TextureWrapMode { Repeat, Clamp, Mirror }
    public enum FilterMode { Point, Bilinear, Trilinear }

    public class Texture : Object { }
    public sealed class Texture2D : Texture
    {
        public int width, height;
        public TextureWrapMode wrapModeU, wrapModeV;
        public FilterMode filterMode;
        public int anisoLevel;
        public Texture2D(int w, int h, TextureFormat f, bool mip) { width = w; height = h; }
        public void Apply(bool a, bool b) { }
    }

    public static class ImageConversion
    {
        public static bool LoadImage(Texture2D t, byte[] data, bool markNonReadable)
        {
            // PNG: width/height at bytes 16..23 (big endian); JPEG: accept.
            if (data.Length > 24 && data[0] == 0x89 && data[1] == (byte)'P')
            {
                t.width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
                t.height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
                return true;
            }
            return data.Length > 2 && data[0] == 0xFF && data[1] == 0xD8;
        }
    }

    public enum MaterialGlobalIlluminationFlags { None }

    public sealed class Shader : Object
    {
        public static Shader Find(string n) { return n == "Standard" ? new Shader { name = n } : null; }
    }

    public sealed class Material : Object
    {
        public Shader shader;
        public Color color;
        public Texture mainTexture;
        public int renderQueue;
        public MaterialGlobalIlluminationFlags globalIlluminationFlags;
        public readonly HashSet<string> keywords = new HashSet<string>();
        public readonly Dictionary<string, object> props = new Dictionary<string, object>();
        public Material(Shader s) { shader = s; }
        public bool HasProperty(string n) { return true; }
        public void SetFloat(string n, float v) { props[n] = v; }
        public void SetInt(string n, int v) { props[n] = v; }
        public void SetColor(string n, Color v) { props[n] = v; }
        public void SetTexture(string n, Texture v) { props[n] = v; }
        public void EnableKeyword(string k) { keywords.Add(k); }
        public void DisableKeyword(string k) { keywords.Remove(k); }
        public void SetOverrideTag(string a, string b) { }
    }
}

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16, UInt32 }
    public enum BlendMode { Zero = 0, One = 1, SrcAlpha = 5, OneMinusSrcAlpha = 10 }
}
