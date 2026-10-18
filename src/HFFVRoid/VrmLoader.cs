using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace HFFVRoid
{
    /// <summary>Basic facts read from a .vrm file without building any Unity objects.</summary>
    internal sealed class VrmInfo
    {
        public string Title;
        public string Author;
        public int SpecMajor; // 0 = VRM 0.x, 1 = VRM 1.0
    }

    /// <summary>Spring bone joint description, stored as index paths so it can be resolved on clones.</summary>
    internal sealed class SpringJointDef
    {
        public int[] Path;
        public int[] TailPath;          // null = virtual tail
        public Vector3 VirtualTailLocal; // used when TailPath == null
        public float Stiffness;
        public float GravityPower;
        public Vector3 GravityDir;
        public float Drag;
        public float HitRadius;
        public int[] ColliderIndices;    // into VrmModel.Colliders
        public int[] CenterPath;         // optional
    }

    internal sealed class SpringColliderDef
    {
        public int[] Path;
        public Vector3 Offset;
        public float Radius;
        public bool IsCapsule;
        public Vector3 Tail;
    }

    /// <summary>A loaded VRM, kept as an inactive template that is cloned for every character using it.</summary>
    internal sealed class VrmModel
    {
        public VrmInfo Info;
        public GameObject Template;
        public readonly Dictionary<string, int[]> HumanBones = new Dictionary<string, int[]>();
        public readonly List<SpringJointDef> Joints = new List<SpringJointDef>();
        public readonly List<SpringColliderDef> Colliders = new List<SpringColliderDef>();
        /// <summary>Highest point of the head mesh (incl. hair / ears) in the rest pose, in head-bone local space.</summary>
        public Vector3 HeadTopLocal;
        public bool HasHeadTop;

        public static int[] PathOf(Transform t, Transform root)
        {
            var list = new List<int>();
            while (t != null && t != root)
            {
                list.Add(t.GetSiblingIndex());
                t = t.parent;
            }
            list.Reverse();
            return list.ToArray();
        }

        public static Transform Resolve(Transform root, int[] path)
        {
            if (path == null) return null;
            Transform t = root;
            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] >= t.childCount) return null;
                t = t.GetChild(path[i]);
            }
            return t;
        }
    }

    /// <summary>
    /// Self-contained glTF 2.0 binary (.glb / .vrm) loader. Supports VRM 0.x and VRM 1.0 humanoids,
    /// skinned meshes, textures, basic MToon/glTF material settings and spring bones.
    /// Blend shapes (facial expressions) are not imported.
    /// </summary>
    internal sealed class VrmLoader
    {
        private Dictionary<string, object> gltf;
        private byte[] bin;
        private bool flipX; // VRM 1.0 / plain glTF: flip X. VRM 0.x (UniVRM convention): flip Z.
        private bool isVrm0;
        private readonly List<Texture2D> createdTextures = new List<Texture2D>();
        private Dictionary<int, Texture2D> textureCache = new Dictionary<int, Texture2D>();
        private Dictionary<int, Material[]> materialCache = new Dictionary<int, Material[]>();
        private Transform[] nodes;
        private readonly HashSet<Transform> meshObjects = new HashSet<Transform>();

        // ------------------------------------------------------------------ parsing

        private static void ParseGlb(byte[] data, out Dictionary<string, object> json, out byte[] binChunk)
        {
            if (data.Length < 20 || data[0] != 'g' || data[1] != 'l' || data[2] != 'T' || data[3] != 'F')
                throw new Exception("This is not a VRM/GLB file.");
            uint version = BitConverter.ToUInt32(data, 4);
            if (version != 2) throw new Exception("Unsupported glTF version " + version + ".");
            int offset = 12;
            json = null;
            binChunk = null;
            while (offset + 8 <= data.Length)
            {
                int len = (int)BitConverter.ToUInt32(data, offset);
                uint type = BitConverter.ToUInt32(data, offset + 4);
                offset += 8;
                if (offset + len > data.Length) throw new Exception("The file is truncated.");
                if (type == 0x4E4F534A) // JSON
                {
                    json = MiniJson.Parse(Encoding.UTF8.GetString(data, offset, len)) as Dictionary<string, object>;
                }
                else if (type == 0x004E4942 && binChunk == null) // BIN
                {
                    binChunk = new byte[len];
                    Buffer.BlockCopy(data, offset, binChunk, 0, len);
                }
                offset += len;
            }
            if (json == null) throw new Exception("The file has no glTF JSON chunk.");
        }

        public static VrmInfo ReadInfo(byte[] data)
        {
            Dictionary<string, object> json;
            byte[] b;
            ParseGlb(data, out json, out b);
            return ReadInfo(json);
        }

        private static VrmInfo ReadInfo(Dictionary<string, object> json)
        {
            var ext = J.Obj(json, "extensions");
            var info = new VrmInfo();
            var v1 = J.Obj(ext, "VRMC_vrm");
            var v0 = J.Obj(ext, "VRM");
            if (v1 != null)
            {
                info.SpecMajor = 1;
                var meta = J.Obj(v1, "meta");
                info.Title = J.Str(meta, "name");
                var authors = J.Arr(meta, "authors");
                if (authors != null && authors.Count > 0) info.Author = authors[0] as string;
                if (J.Obj(v1, "humanoid") == null) throw new Exception("The VRM has no humanoid bone data.");
            }
            else if (v0 != null)
            {
                info.SpecMajor = 0;
                var meta = J.Obj(v0, "meta");
                info.Title = J.Str(meta, "title");
                info.Author = J.Str(meta, "author");
                if (J.Obj(v0, "humanoid") == null) throw new Exception("The VRM has no humanoid bone data.");
            }
            else
            {
                throw new Exception("This glTF file has no VRM data (it is not a VRM avatar).");
            }
            if (string.IsNullOrEmpty(info.Title)) info.Title = null;
            return info;
        }

        // ------------------------------------------------------------------ building

        public static VrmModel Load(byte[] data, string fallbackName)
        {
            var loader = new VrmLoader();
            return loader.Build(data, fallbackName);
        }

        private VrmModel Build(byte[] data, string fallbackName)
        {
            ParseGlb(data, out gltf, out bin);
            var model = new VrmModel();
            model.Info = ReadInfo(gltf);
            if (model.Info.Title == null) model.Info.Title = fallbackName;
            isVrm0 = model.Info.SpecMajor == 0;
            flipX = !isVrm0;

            var root = new GameObject("VRM_" + model.Info.Title);
            root.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(root);
            model.Template = root;

            try
            {
                BuildNodes(root.transform);
                ReadHumanoid(model, root.transform);
                BuildMeshes(root.transform, model);
                if (isVrm0) ReadSprings0(model, root.transform); else ReadSprings1(model, root.transform);
            }
            catch
            {
                UnityEngine.Object.Destroy(root);
                foreach (var t in createdTextures) if (t != null) UnityEngine.Object.Destroy(t);
                throw;
            }
            return model;
        }

        // ---- coordinate conversion (glTF right-handed -> Unity left-handed)

        private Vector3 P(float x, float y, float z)
        {
            return flipX ? new Vector3(-x, y, z) : new Vector3(x, y, -z);
        }

        private Vector3 P(float[] v)
        {
            return v == null || v.Length < 3 ? Vector3.zero : P(v[0], v[1], v[2]);
        }

        private Quaternion Q(float x, float y, float z, float w)
        {
            return flipX ? new Quaternion(x, -y, -z, w) : new Quaternion(-x, -y, z, w);
        }

        private Matrix4x4 M(float[] m) // column-major glTF matrix
        {
            var r = new Matrix4x4();
            for (int c = 0; c < 4; c++)
                for (int row = 0; row < 4; row++)
                    r[row, c] = m[c * 4 + row];
            var s = Matrix4x4.identity;
            if (flipX) s.m00 = -1; else s.m22 = -1;
            return s * r * s;
        }

        // ---- nodes

        private void BuildNodes(Transform root)
        {
            var jn = J.Arr(gltf, "nodes") ?? new List<object>();
            nodes = new Transform[jn.Count];
            for (int i = 0; i < jn.Count; i++)
            {
                string name = J.Str(jn[i], "name");
                if (string.IsNullOrEmpty(name)) name = "node" + i;
                nodes[i] = new GameObject(name).transform;
            }
            // parent
            var hasParent = new bool[jn.Count];
            for (int i = 0; i < jn.Count; i++)
            {
                var children = J.Arr(jn[i], "children");
                if (children == null) continue;
                foreach (var c in children)
                {
                    int ci = (int)(double)c;
                    if (ci < 0 || ci >= nodes.Length) continue;
                    nodes[ci].SetParent(nodes[i], false);
                    hasParent[ci] = true;
                }
            }
            // scene roots
            var scenes = J.Arr(gltf, "scenes");
            int sceneIndex = J.Int(gltf, "scene", 0);
            List<object> roots = null;
            if (scenes != null && scenes.Count > 0)
                roots = J.Arr(scenes[Mathf.Clamp(sceneIndex, 0, scenes.Count - 1)], "nodes");
            if (roots == null)
            {
                roots = new List<object>();
                for (int i = 0; i < jn.Count; i++) if (!hasParent[i]) roots.Add((double)i);
            }
            foreach (var r in roots)
            {
                int ri = (int)(double)r;
                if (ri >= 0 && ri < nodes.Length) nodes[ri].SetParent(root, false);
            }
            for (int i = 0; i < jn.Count; i++)
            {
                if (nodes[i].parent == null) nodes[i].SetParent(root, false); // orphan safety
                var n = jn[i];
                var matrix = J.Floats(n, "matrix");
                if (matrix != null && matrix.Length == 16)
                {
                    var m = M(matrix);
                    nodes[i].localPosition = m.GetColumn(3);
                    Vector3 c0 = m.GetColumn(0), c1 = m.GetColumn(1), c2 = m.GetColumn(2);
                    nodes[i].localScale = new Vector3(c0.magnitude, c1.magnitude, c2.magnitude);
                    if (c2.sqrMagnitude > 0 && c1.sqrMagnitude > 0)
                        nodes[i].localRotation = Quaternion.LookRotation(c2, c1);
                }
                else
                {
                    var t = J.Floats(n, "translation");
                    var q = J.Floats(n, "rotation");
                    var s = J.Floats(n, "scale");
                    if (t != null) nodes[i].localPosition = P(t);
                    if (q != null && q.Length == 4) nodes[i].localRotation = Normalize(Q(q[0], q[1], q[2], q[3]));
                    if (s != null && s.Length == 3) nodes[i].localScale = new Vector3(s[0], s[1], s[2]);
                }
            }
        }

        private static Quaternion Normalize(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (m < 1e-8f) return Quaternion.identity;
            return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }

        // ---- accessors

        private byte[] BufferBytes(int bufferIndex)
        {
            var buffers = J.Arr(gltf, "buffers");
            var b = buffers != null && bufferIndex < buffers.Count ? buffers[bufferIndex] : null;
            string uri = J.Str(b, "uri");
            if (uri == null) return bin;
            if (uri.StartsWith("data:"))
            {
                int comma = uri.IndexOf(',');
                return Convert.FromBase64String(uri.Substring(comma + 1));
            }
            throw new Exception("External buffers are not supported (" + uri + ").");
        }

        private static int ComponentCount(string type)
        {
            switch (type)
            {
                case "SCALAR": return 1;
                case "VEC2": return 2;
                case "VEC3": return 3;
                case "VEC4": return 4;
                case "MAT2": return 4;
                case "MAT3": return 9;
                case "MAT4": return 16;
            }
            throw new Exception("Unknown accessor type " + type);
        }

        private static int ComponentSize(int componentType)
        {
            switch (componentType)
            {
                case 5120: case 5121: return 1;
                case 5122: case 5123: return 2;
                case 5125: case 5126: return 4;
            }
            throw new Exception("Unknown component type " + componentType);
        }

        private static double ReadComponent(byte[] buf, int pos, int componentType, bool normalized)
        {
            switch (componentType)
            {
                case 5120: { sbyte v = (sbyte)buf[pos]; return normalized ? Math.Max(v / 127.0, -1.0) : v; }
                case 5121: { byte v = buf[pos]; return normalized ? v / 255.0 : v; }
                case 5122: { short v = BitConverter.ToInt16(buf, pos); return normalized ? Math.Max(v / 32767.0, -1.0) : v; }
                case 5123: { ushort v = BitConverter.ToUInt16(buf, pos); return normalized ? v / 65535.0 : v; }
                case 5125: return BitConverter.ToUInt32(buf, pos);
                case 5126: return BitConverter.ToSingle(buf, pos);
            }
            return 0;
        }

        /// <summary>Reads an accessor into a flat double array (count * components), including sparse data.</summary>
        private double[] ReadAccessor(int index, out int count, out int comps)
        {
            var acc = J.Arr(gltf, "accessors")[index];
            count = J.Int(acc, "count", 0);
            comps = ComponentCount(J.Str(acc, "type", "SCALAR"));
            int ctype = J.Int(acc, "componentType", 5126);
            bool norm = J.Bool(acc, "normalized");
            var result = new double[count * comps];
            int bvIndex = J.Int(acc, "bufferView", -1);
            if (bvIndex >= 0)
            {
                var bv = J.Arr(gltf, "bufferViews")[bvIndex];
                byte[] buf = BufferBytes(J.Int(bv, "buffer", 0));
                int baseOffset = J.Int(bv, "byteOffset", 0) + J.Int(acc, "byteOffset", 0);
                int csize = ComponentSize(ctype);
                int stride = J.Int(bv, "byteStride", 0);
                if (stride <= 0) stride = csize * comps;
                // matrices of 1/2-byte components have column padding; not used by VRM, ignored.
                for (int e = 0; e < count; e++)
                {
                    int p = baseOffset + e * stride;
                    for (int c = 0; c < comps; c++)
                        result[e * comps + c] = ReadComponent(buf, p + c * csize, ctype, norm);
                }
            }
            var sparse = J.Obj(acc, "sparse");
            if (sparse != null)
            {
                int scount = J.Int(sparse, "count", 0);
                var si = J.Obj(sparse, "indices");
                var sv = J.Obj(sparse, "values");
                var ibv = J.Arr(gltf, "bufferViews")[J.Int(si, "bufferView", 0)];
                var vbv = J.Arr(gltf, "bufferViews")[J.Int(sv, "bufferView", 0)];
                byte[] ib = BufferBytes(J.Int(ibv, "buffer", 0));
                byte[] vb = BufferBytes(J.Int(vbv, "buffer", 0));
                int ictype = J.Int(si, "componentType", 5125);
                int ioff = J.Int(ibv, "byteOffset", 0) + J.Int(si, "byteOffset", 0);
                int voff = J.Int(vbv, "byteOffset", 0) + J.Int(sv, "byteOffset", 0);
                int isz = ComponentSize(ictype), vsz = ComponentSize(ctype);
                for (int k = 0; k < scount; k++)
                {
                    int target = (int)ReadComponent(ib, ioff + k * isz, ictype, false);
                    if (target < 0 || target >= count) continue;
                    for (int c = 0; c < comps; c++)
                        result[target * comps + c] = ReadComponent(vb, voff + (k * comps + c) * vsz, ctype, norm);
                }
            }
            return result;
        }

        // ---- textures & materials

        private Texture2D GetTexture(int textureIndex)
        {
            if (textureIndex < 0) return null;
            Texture2D cached;
            if (textureCache.TryGetValue(textureIndex, out cached)) return cached;
            Texture2D tex = null;
            try
            {
                var textures = J.Arr(gltf, "textures");
                if (textures != null && textureIndex < textures.Count)
                {
                    var tj = textures[textureIndex];
                    int src = J.Int(tj, "source", -1);
                    if (src >= 0)
                    {
                        var img = J.Arr(gltf, "images")[src];
                        byte[] bytes = null;
                        int bvIndex = J.Int(img, "bufferView", -1);
                        if (bvIndex >= 0)
                        {
                            var bv = J.Arr(gltf, "bufferViews")[bvIndex];
                            byte[] buf = BufferBytes(J.Int(bv, "buffer", 0));
                            int off = J.Int(bv, "byteOffset", 0), len = J.Int(bv, "byteLength", 0);
                            bytes = new byte[len];
                            Buffer.BlockCopy(buf, off, bytes, 0, len);
                        }
                        else
                        {
                            string uri = J.Str(img, "uri");
                            if (uri != null && uri.StartsWith("data:"))
                                bytes = Convert.FromBase64String(uri.Substring(uri.IndexOf(',') + 1));
                        }
                        if (bytes != null)
                        {
                            tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                            tex.name = J.Str(img, "name", "vrm_tex" + src);
                            if (!ImageConversion.LoadImage(tex, bytes, false))
                            {
                                UnityEngine.Object.Destroy(tex);
                                tex = null;
                            }
                            else
                            {
                                var sampler = J.Int(tj, "sampler", -1) >= 0 ? J.Arr(gltf, "samplers")[J.Int(tj, "sampler", 0)] : null;
                                int wrapS = J.Int(sampler, "wrapS", 10497);
                                int wrapT = J.Int(sampler, "wrapT", 10497);
                                tex.wrapModeU = wrapS == 33071 ? TextureWrapMode.Clamp : (wrapS == 33648 ? TextureWrapMode.Mirror : TextureWrapMode.Repeat);
                                tex.wrapModeV = wrapT == 33071 ? TextureWrapMode.Clamp : (wrapT == 33648 ? TextureWrapMode.Mirror : TextureWrapMode.Repeat);
                                int mag = J.Int(sampler, "magFilter", 9729);
                                tex.filterMode = mag == 9728 ? FilterMode.Point : FilterMode.Bilinear;
                                tex.anisoLevel = 2;
                                tex.Apply(true, true); // upload + free CPU copy
                                createdTextures.Add(tex);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Texture " + textureIndex + " failed to load: " + e.Message);
                tex = null;
            }
            textureCache[textureIndex] = tex;
            return tex;
        }

        private Dictionary<string, object> Vrm0MaterialProps(int materialIndex, string materialName)
        {
            if (!isVrm0) return null;
            var props = J.Arr(J.Obj(J.Obj(gltf, "extensions"), "VRM"), "materialProperties");
            if (props == null) return null;
            foreach (var p in props)
                if (J.Str(p, "name") == materialName) return p as Dictionary<string, object>;
            if (materialIndex < props.Count) return props[materialIndex] as Dictionary<string, object>;
            return null;
        }

        private enum AlphaMode { Opaque, Mask, Blend }

        private sealed class MatInfo
        {
            public Material Material;
            public bool DoubleSided;
        }

        private readonly Dictionary<int, MatInfo> matInfos = new Dictionary<int, MatInfo>();

        private MatInfo GetMaterial(int index)
        {
            MatInfo mi;
            if (matInfos.TryGetValue(index, out mi)) return mi;
            mi = new MatInfo();
            var mats = J.Arr(gltf, "materials");
            var mj = index >= 0 && mats != null && index < mats.Count ? mats[index] : null;
            string name = J.Str(mj, "name", "vrm_mat" + index);
            var pbr = J.Obj(mj, "pbrMetallicRoughness");
            float[] cf = J.Floats(pbr, "baseColorFactor");
            Color color = cf != null && cf.Length == 4 ? new Color(cf[0], cf[1], cf[2], cf[3]) : Color.white;
            int texIndex = J.Int(J.Obj(pbr, "baseColorTexture"), "index", -1);
            string am = J.Str(mj, "alphaMode", "OPAQUE");
            AlphaMode mode = am == "MASK" ? AlphaMode.Mask : (am == "BLEND" ? AlphaMode.Blend : AlphaMode.Opaque);
            float cutoff = (float)J.Num(mj, "alphaCutoff", 0.5);
            bool doubleSided = J.Bool(mj, "doubleSided");
            float[] ef = J.Floats(mj, "emissiveFactor");
            Color emissive = ef != null && ef.Length >= 3 ? new Color(ef[0], ef[1], ef[2]) : Color.black;
            int emissiveTex = J.Int(J.Obj(mj, "emissiveTexture"), "index", -1);
            int queueOverride = -1;

            var v0 = Vrm0MaterialProps(index, name);
            if (v0 != null)
            {
                string shader = J.Str(v0, "shader", "");
                var fp = J.Obj(v0, "floatProperties");
                var vp = J.Obj(v0, "vectorProperties");
                var tp = J.Obj(v0, "textureProperties");
                var c = J.Arr(vp, "_Color");
                if (c != null && c.Count == 4) color = new Color((float)(double)c[0], (float)(double)c[1], (float)(double)c[2], (float)(double)c[3]);
                if (J.Has(tp, "_MainTex")) texIndex = J.Int(tp, "_MainTex");
                if (J.Has(tp, "_EmissionMap")) emissiveTex = J.Int(tp, "_EmissionMap");
                var ec = J.Arr(vp, "_EmissionColor");
                if (ec != null && ec.Count >= 3) emissive = new Color((float)(double)ec[0], (float)(double)ec[1], (float)(double)ec[2]);
                if (shader == "VRM/MToon")
                {
                    int bm = (int)J.Num(fp, "_BlendMode", 0);
                    mode = bm == 1 ? AlphaMode.Mask : (bm >= 2 ? AlphaMode.Blend : AlphaMode.Opaque);
                    cutoff = (float)J.Num(fp, "_Cutoff", cutoff);
                    if (J.Has(fp, "_CullMode")) doubleSided = (int)J.Num(fp, "_CullMode") == 0;
                }
                else if (shader == "VRM/UnlitCutout") mode = AlphaMode.Mask;
                else if (shader == "VRM/UnlitTransparent" || shader == "VRM/UnlitTransparentZWrite") mode = AlphaMode.Blend;
                else if (shader == "VRM/UnlitTexture") mode = AlphaMode.Opaque;
                int rq = J.Int(v0, "renderQueue", -1);
                if (rq > 0 && mode == AlphaMode.Blend) queueOverride = rq;
            }

            Texture2D tex = GetTexture(texIndex);
            mi.Material = MaterialFactory.Create(name, color, tex, mode == AlphaMode.Mask, mode == AlphaMode.Blend, cutoff,
                emissive, GetTexture(emissiveTex), queueOverride);
            mi.DoubleSided = doubleSided;
            matInfos[index] = mi;
            return mi;
        }

        // ---- meshes

        private void BuildMeshes(Transform root, VrmModel model)
        {
            // Head-top search: vertices driven mostly by the head bone or anything under it (hair, ears, hats).
            Transform head = VrmModel.Resolve(root, model.HumanBones["head"]);
            Transform hips = VrmModel.Resolve(root, model.HumanBones["hips"]);
            Vector3 up = (head.position - hips.position).normalized;
            var headSet = new HashSet<Transform>();
            var stack = new Stack<Transform>();
            stack.Push(head);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                headSet.Add(t);
                for (int c = 0; c < t.childCount; c++) stack.Push(t.GetChild(c));
            }
            float bestUp = float.NegativeInfinity;
            Vector3 bestPoint = Vector3.zero;

            var jn = J.Arr(gltf, "nodes");
            var meshes = J.Arr(gltf, "meshes");
            var skins = J.Arr(gltf, "skins");
            if (jn == null || meshes == null) throw new Exception("The VRM contains no meshes.");
            int built = 0;
            for (int i = 0; i < jn.Count; i++)
            {
                int meshIndex = J.Int(jn[i], "mesh", -1);
                if (meshIndex < 0 || meshIndex >= meshes.Count) continue;
                int skinIndex = J.Int(jn[i], "skin", -1);
                var skin = skinIndex >= 0 && skins != null && skinIndex < skins.Count ? skins[skinIndex] : null;

                Material[] materials;
                List<Vector3> verts;
                List<BoneWeight> vweights;
                Mesh mesh = BuildMesh(meshes[meshIndex], skin != null, out materials, out verts, out vweights);
                if (mesh == null) continue;
                mesh.name = nodes[i].name;

                if (skin != null)
                {
                    var joints = J.Arr(skin, "joints");
                    var bones = new Transform[joints.Count];
                    for (int b = 0; b < bones.Length; b++) bones[b] = nodes[(int)(double)joints[b]];
                    var bindposes = new Matrix4x4[bones.Length];
                    int ibm = J.Int(skin, "inverseBindMatrices", -1);
                    if (ibm >= 0)
                    {
                        int cnt, comps;
                        var m = ReadAccessor(ibm, out cnt, out comps);
                        var tmp = new float[16];
                        for (int b = 0; b < bones.Length && b < cnt; b++)
                        {
                            for (int k = 0; k < 16; k++) tmp[k] = (float)m[b * 16 + k];
                            bindposes[b] = M(tmp);
                        }
                    }
                    else
                    {
                        for (int b = 0; b < bones.Length; b++) bindposes[b] = bones[b].worldToLocalMatrix * root.localToWorldMatrix;
                    }
                    mesh.bindposes = bindposes;

                    // Rest-pose (skinned) position of head vertices: sum_i w_i * bone_i.localToWorld * bindpose_i * v
                    var skinM = new Matrix4x4[bones.Length];
                    var isHead = new bool[bones.Length];
                    for (int b = 0; b < bones.Length; b++)
                    {
                        skinM[b] = bones[b].localToWorldMatrix * bindposes[b];
                        isHead[b] = headSet.Contains(bones[b]);
                    }
                    for (int v = 0; v < verts.Count; v++)
                    {
                        var w = vweights[v];
                        int dom = w.boneIndex0; float dw = w.weight0;
                        if (w.weight1 > dw) { dom = w.boneIndex1; dw = w.weight1; }
                        if (w.weight2 > dw) { dom = w.boneIndex2; dw = w.weight2; }
                        if (w.weight3 > dw) { dom = w.boneIndex3; }
                        if (dom < 0 || dom >= bones.Length || !isHead[dom]) continue;
                        Vector3 p = Vector3.zero;
                        if (w.weight0 > 0 && w.boneIndex0 < bones.Length) p += skinM[w.boneIndex0].MultiplyPoint3x4(verts[v]) * w.weight0;
                        if (w.weight1 > 0 && w.boneIndex1 < bones.Length) p += skinM[w.boneIndex1].MultiplyPoint3x4(verts[v]) * w.weight1;
                        if (w.weight2 > 0 && w.boneIndex2 < bones.Length) p += skinM[w.boneIndex2].MultiplyPoint3x4(verts[v]) * w.weight2;
                        if (w.weight3 > 0 && w.boneIndex3 < bones.Length) p += skinM[w.boneIndex3].MultiplyPoint3x4(verts[v]) * w.weight3;
                        float d = Vector3.Dot(p, up);
                        if (d > bestUp) { bestUp = d; bestPoint = p; }
                    }

                    var go = new GameObject(nodes[i].name + "_skin");
                    go.transform.SetParent(root, false);
                    var smr = go.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = mesh;
                    smr.bones = bones;
                    smr.rootBone = bones.Length > 0 ? bones[0] : null;
                    smr.sharedMaterials = materials;
                    smr.updateWhenOffscreen = true;
                    smr.quality = SkinQuality.Bone4;
                }
                else
                {
                    var go = new GameObject(nodes[i].name + "_mesh");
                    go.transform.SetParent(nodes[i], false);
                    meshObjects.Add(go.transform);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterials = materials;
                    if (headSet.Contains(nodes[i]))
                    {
                        var m = nodes[i].localToWorldMatrix;
                        foreach (var v in verts)
                        {
                            var p = m.MultiplyPoint3x4(v);
                            float d = Vector3.Dot(p, up);
                            if (d > bestUp) { bestUp = d; bestPoint = p; }
                        }
                    }
                }
                built++;
            }
            if (built == 0) throw new Exception("The VRM contains no usable meshes.");
            if (!float.IsNegativeInfinity(bestUp))
            {
                model.HeadTopLocal = head.InverseTransformPoint(bestPoint);
                model.HasHeadTop = true;
            }
        }

        private Mesh BuildMesh(object meshJson, bool skinned, out Material[] materials,
            out List<Vector3> positions, out List<BoneWeight> weights)
        {
            var prims = J.Arr(meshJson, "primitives");
            positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            weights = new List<BoneWeight>();
            var subTris = new List<int[]>();
            var mats = new List<Material>();
            bool anyNormals = true;

            // Primitives often share one vertex buffer (VRoid / UniVRM exports): add each buffer only once.
            var vertexBase = new Dictionary<string, int>();
            foreach (var prim in prims)
            {
                int mode = J.Int(prim, "mode", 4);
                if (mode != 4) continue; // triangles only
                var attrs = J.Obj(prim, "attributes");
                if (!J.Has(attrs, "POSITION")) continue;

                int[] indices = null;
                int indexSourceCount = -1;
                if (J.Has(prim, "indices"))
                {
                    int icount, icomps;
                    var id = ReadAccessor(J.Int(prim, "indices"), out icount, out icomps);
                    indices = new int[icount];
                    for (int k = 0; k < icount; k++) indices[k] = (int)id[k];
                }

                var mi = GetMaterial(J.Int(prim, "material", -1));
                string attrKey = J.Int(attrs, "POSITION") + "/" + J.Int(attrs, "NORMAL") + "/" + J.Int(attrs, "TEXCOORD_0") +
                                 "/" + J.Int(attrs, "JOINTS_0") + "/" + J.Int(attrs, "WEIGHTS_0");
                int passes = mi.DoubleSided ? 2 : 1;
                for (int pass = 0; pass < passes; pass++)
                {
                    bool back = pass == 1;
                    string key = attrKey + (back ? "/back" : "/front");
                    int baseVertex, vcount;
                    if (!vertexBase.TryGetValue(key, out baseVertex))
                    {
                        baseVertex = positions.Count;
                        vertexBase[key] = baseVertex;
                        int comps, tmpCount, uvcomps = 2;
                        var pos = ReadAccessor(J.Int(attrs, "POSITION"), out vcount, out comps);
                        double[] nrm = null, uv = null, jnt = null, wgt = null;
                        if (J.Has(attrs, "NORMAL")) nrm = ReadAccessor(J.Int(attrs, "NORMAL"), out tmpCount, out comps);
                        if (J.Has(attrs, "TEXCOORD_0")) uv = ReadAccessor(J.Int(attrs, "TEXCOORD_0"), out tmpCount, out uvcomps);
                        if (skinned && J.Has(attrs, "JOINTS_0")) jnt = ReadAccessor(J.Int(attrs, "JOINTS_0"), out tmpCount, out comps);
                        if (skinned && J.Has(attrs, "WEIGHTS_0")) wgt = ReadAccessor(J.Int(attrs, "WEIGHTS_0"), out tmpCount, out comps);
                        if (nrm == null) anyNormals = false;
                        for (int v = 0; v < vcount; v++)
                        {
                            positions.Add(P((float)pos[v * 3], (float)pos[v * 3 + 1], (float)pos[v * 3 + 2]));
                            if (nrm != null)
                            {
                                var n = P((float)nrm[v * 3], (float)nrm[v * 3 + 1], (float)nrm[v * 3 + 2]);
                                normals.Add(back ? -n : n);
                            }
                            else normals.Add(Vector3.up);
                            uvs.Add(uv != null ? new Vector2((float)uv[v * uvcomps], 1f - (float)uv[v * uvcomps + 1]) : Vector2.zero);
                            if (skinned)
                            {
                                var bw = new BoneWeight();
                                if (jnt != null && wgt != null)
                                {
                                    int o = v * 4;
                                    float w0 = (float)wgt[o], w1 = (float)wgt[o + 1], w2 = (float)wgt[o + 2], w3 = (float)wgt[o + 3];
                                    float sum = w0 + w1 + w2 + w3;
                                    if (sum > 1e-6f) { w0 /= sum; w1 /= sum; w2 /= sum; w3 /= sum; } else { w0 = 1; }
                                    bw.boneIndex0 = (int)jnt[o]; bw.weight0 = w0;
                                    bw.boneIndex1 = (int)jnt[o + 1]; bw.weight1 = w1;
                                    bw.boneIndex2 = (int)jnt[o + 2]; bw.weight2 = w2;
                                    bw.boneIndex3 = (int)jnt[o + 3]; bw.weight3 = w3;
                                }
                                else { bw.boneIndex0 = 0; bw.weight0 = 1; }
                                weights.Add(bw);
                            }
                        }
                        indexSourceCount = vcount;
                    }
                    if (indices == null)
                    {
                        // Non-indexed primitive: one vertex per corner.
                        int n = indexSourceCount >= 0 ? indexSourceCount : J.Int(J.Arr(gltf, "accessors")[J.Int(attrs, "POSITION")], "count", 0);
                        indices = new int[n];
                        for (int k = 0; k < n; k++) indices[k] = k;
                    }
                    // glTF is CCW front faces, Unity is CW: reverse winding (and again for the back side copy)
                    var tris = new int[indices.Length - indices.Length % 3];
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        int a = indices[t] + baseVertex, b = indices[t + 1] + baseVertex, c = indices[t + 2] + baseVertex;
                        if (!back) { tris[t] = a; tris[t + 1] = c; tris[t + 2] = b; }
                        else { tris[t] = a; tris[t + 1] = b; tris[t + 2] = c; }
                    }
                    subTris.Add(tris);
                    mats.Add(mi.Material);
                }
            }

            materials = mats.ToArray();
            if (positions.Count == 0) return null;

            var mesh = new Mesh();
            if (positions.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            if (skinned) mesh.boneWeights = weights.ToArray();
            mesh.subMeshCount = subTris.Count;
            for (int s = 0; s < subTris.Count; s++) mesh.SetTriangles(subTris[s], s, false);
            mesh.RecalculateBounds();
            if (!anyNormals) mesh.RecalculateNormals();
            return mesh;
        }

        // ---- humanoid

        private static readonly string[] RequiredBones =
        {
            "hips", "spine", "head",
            "leftUpperArm", "leftLowerArm", "leftHand", "rightUpperArm", "rightLowerArm", "rightHand",
            "leftUpperLeg", "leftLowerLeg", "leftFoot", "rightUpperLeg", "rightLowerLeg", "rightFoot"
        };

        private void ReadHumanoid(VrmModel model, Transform root)
        {
            var ext = J.Obj(gltf, "extensions");
            if (isVrm0)
            {
                var list = J.Arr(J.Obj(J.Obj(ext, "VRM"), "humanoid"), "humanBones");
                if (list != null)
                    foreach (var hb in list)
                    {
                        string bone = J.Str(hb, "bone");
                        int node = J.Int(hb, "node", -1);
                        if (bone != null && node >= 0 && node < nodes.Length)
                            model.HumanBones[bone] = VrmModel.PathOf(nodes[node], root);
                    }
            }
            else
            {
                var dict = J.Obj(J.Obj(J.Obj(ext, "VRMC_vrm"), "humanoid"), "humanBones");
                if (dict != null)
                    foreach (var kv in dict)
                    {
                        int node = J.Int(kv.Value, "node", -1);
                        if (node >= 0 && node < nodes.Length)
                            model.HumanBones[kv.Key] = VrmModel.PathOf(nodes[node], root);
                    }
            }
            foreach (var b in RequiredBones)
                if (!model.HumanBones.ContainsKey(b))
                    throw new Exception("The VRM is missing the required humanoid bone '" + b + "'.");
        }

        // ---- spring bones

        private void ReadSprings0(VrmModel model, Transform root)
        {
            var sa = J.Obj(J.Obj(J.Obj(gltf, "extensions"), "VRM"), "secondaryAnimation");
            if (sa == null) return;
            // collider groups (VRM 0.x stores offsets in Unity space; we use the UniVRM Z-flip convention so they map 1:1)
            var groupToColliders = new List<int[]>();
            var cgs = J.Arr(sa, "colliderGroups");
            if (cgs != null)
                foreach (var cg in cgs)
                {
                    int node = J.Int(cg, "node", -1);
                    var ids = new List<int>();
                    var cols = J.Arr(cg, "colliders");
                    if (node >= 0 && node < nodes.Length && cols != null)
                        foreach (var c in cols)
                        {
                            var off = J.Vec(c, "offset");
                            model.Colliders.Add(new SpringColliderDef
                            {
                                Path = VrmModel.PathOf(nodes[node], root),
                                Offset = off != null ? new Vector3(off[0], off[1], off[2]) : Vector3.zero,
                                Radius = (float)J.Num(c, "radius", 0.05),
                            });
                            ids.Add(model.Colliders.Count - 1);
                        }
                    groupToColliders.Add(ids.ToArray());
                }

            var groups = J.Arr(sa, "boneGroups");
            if (groups == null) return;
            foreach (var g in groups)
            {
                var gd = J.Vec(g, "gravityDir");
                var gravityDir = gd != null ? new Vector3(gd[0], gd[1], gd[2]) : Vector3.down;
                float stiffness = (float)J.Num(g, "stiffiness", J.Num(g, "stiffness", 1));
                float gravity = (float)J.Num(g, "gravityPower", 0);
                float drag = (float)J.Num(g, "dragForce", 0.4);
                float hit = (float)J.Num(g, "hitRadius", 0.02);
                int center = J.Int(g, "center", -1);
                var colliders = new List<int>();
                var cgi = J.Arr(g, "colliderGroups");
                if (cgi != null)
                    foreach (var ci in cgi)
                    {
                        int idx = (int)(double)ci;
                        if (idx >= 0 && idx < groupToColliders.Count) colliders.AddRange(groupToColliders[idx]);
                    }
                var bones = J.Arr(g, "bones");
                if (bones == null) continue;
                foreach (var bo in bones)
                {
                    int bi = (int)(double)bo;
                    if (bi < 0 || bi >= nodes.Length) continue;
                    AddChain0(model, root, nodes[bi], stiffness, gravity, gravityDir, drag, hit, colliders.ToArray(),
                        center >= 0 && center < nodes.Length ? VrmModel.PathOf(nodes[center], root) : null);
                }
            }
        }

        private void AddChain0(VrmModel model, Transform root, Transform t, float stiffness, float gravity, Vector3 gdir,
            float drag, float hit, int[] colliders, int[] center)
        {
            var def = new SpringJointDef
            {
                Path = VrmModel.PathOf(t, root),
                Stiffness = stiffness, GravityPower = gravity, GravityDir = gdir, Drag = drag, HitRadius = hit,
                ColliderIndices = colliders, CenterPath = center
            };
            Transform firstChild = null;
            for (int i = 0; i < t.childCount && firstChild == null; i++)
                if (!meshObjects.Contains(t.GetChild(i))) firstChild = t.GetChild(i);
            if (firstChild == null)
            {
                Vector3 delta = t.parent != null ? t.position - t.parent.position : Vector3.up;
                Vector3 childPos = t.position + delta.normalized * 0.07f;
                def.VirtualTailLocal = t.InverseTransformPoint(childPos);
            }
            else
            {
                def.TailPath = VrmModel.PathOf(firstChild, root);
            }
            model.Joints.Add(def);
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (meshObjects.Contains(c)) continue;
                AddChain0(model, root, c, stiffness, gravity, gdir, drag, hit, colliders, center);
            }
        }

        private void ReadSprings1(VrmModel model, Transform root)
        {
            var sb = J.Obj(J.Obj(gltf, "extensions"), "VRMC_springBone");
            if (sb == null) return;
            var cols = J.Arr(sb, "colliders");
            var colliderMap = new List<int>();
            if (cols != null)
                foreach (var c in cols)
                {
                    int node = J.Int(c, "node", -1);
                    var shape = J.Obj(c, "shape");
                    var sphere = J.Obj(shape, "sphere");
                    var capsule = J.Obj(shape, "capsule");
                    if (node < 0 || node >= nodes.Length || (sphere == null && capsule == null)) { colliderMap.Add(-1); continue; }
                    var def = new SpringColliderDef { Path = VrmModel.PathOf(nodes[node], root) };
                    if (sphere != null)
                    {
                        def.Offset = P(J.Floats(sphere, "offset"));
                        def.Radius = (float)J.Num(sphere, "radius", 0);
                    }
                    else
                    {
                        def.IsCapsule = true;
                        def.Offset = P(J.Floats(capsule, "offset"));
                        def.Tail = P(J.Floats(capsule, "tail"));
                        def.Radius = (float)J.Num(capsule, "radius", 0);
                    }
                    model.Colliders.Add(def);
                    colliderMap.Add(model.Colliders.Count - 1);
                }
            var groupCols = new List<int[]>();
            var cgs = J.Arr(sb, "colliderGroups");
            if (cgs != null)
                foreach (var g in cgs)
                {
                    var ids = new List<int>();
                    var cl = J.Arr(g, "colliders");
                    if (cl != null)
                        foreach (var ci in cl)
                        {
                            int idx = (int)(double)ci;
                            if (idx >= 0 && idx < colliderMap.Count && colliderMap[idx] >= 0) ids.Add(colliderMap[idx]);
                        }
                    groupCols.Add(ids.ToArray());
                }
            var springs = J.Arr(sb, "springs");
            if (springs == null) return;
            foreach (var s in springs)
            {
                var joints = J.Arr(s, "joints");
                if (joints == null || joints.Count < 1) continue;
                var colliders = new List<int>();
                var sg = J.Arr(s, "colliderGroups");
                if (sg != null)
                    foreach (var gi in sg)
                    {
                        int idx = (int)(double)gi;
                        if (idx >= 0 && idx < groupCols.Count) colliders.AddRange(groupCols[idx]);
                    }
                int center = J.Int(s, "center", -1);
                int[] centerPath = center >= 0 && center < nodes.Length ? VrmModel.PathOf(nodes[center], root) : null;
                for (int k = 0; k < joints.Count; k++)
                {
                    var jj = joints[k];
                    int node = J.Int(jj, "node", -1);
                    if (node < 0 || node >= nodes.Length) continue;
                    var t = nodes[node];
                    var gd = J.Floats(jj, "gravityDir");
                    var def = new SpringJointDef
                    {
                        Path = VrmModel.PathOf(t, root),
                        Stiffness = (float)J.Num(jj, "stiffness", 1),
                        GravityPower = (float)J.Num(jj, "gravityPower", 0),
                        GravityDir = gd != null ? P(gd) : Vector3.down,
                        Drag = (float)J.Num(jj, "dragForce", 0.5),
                        HitRadius = (float)J.Num(jj, "hitRadius", 0),
                        ColliderIndices = colliders.ToArray(),
                        CenterPath = centerPath
                    };
                    if (k + 1 < joints.Count)
                    {
                        int next = J.Int(joints[k + 1], "node", -1);
                        if (next < 0 || next >= nodes.Length) continue;
                        def.TailPath = VrmModel.PathOf(nodes[next], root);
                    }
                    else
                    {
                        // The last joint is only a tail in VRM 1.0, unless it has no children of its own
                        // (then give it a short virtual tail like VRM 0.x does).
                        if (joints.Count > 1) continue;
                        Vector3 delta = t.parent != null ? t.position - t.parent.position : Vector3.up;
                        def.VirtualTailLocal = t.InverseTransformPoint(t.position + delta.normalized * 0.07f);
                    }
                    model.Joints.Add(def);
                }
            }
        }
    }
}
