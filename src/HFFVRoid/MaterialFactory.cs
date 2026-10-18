using UnityEngine;

namespace HFFVRoid
{
    /// <summary>
    /// Builds Unity materials for VRM meshes. VRM avatars normally use the MToon toon shader, which the game
    /// does not ship, so we approximate it with the built-in Standard shader (no gloss, optional "shade lift"
    /// through emission so faces don't look muddy). Shader names are configurable in the BepInEx config.
    /// </summary>
    internal static class MaterialFactory
    {
        private static Shader cachedShader;
        private static bool searched;

        private static Shader FindShader()
        {
            if (searched) return cachedShader;
            searched = true;
            foreach (var raw in Plugin.CfgShaderNames.Value.Split(','))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                var s = Shader.Find(name);
                if (s != null)
                {
                    cachedShader = s;
                    Plugin.Log.LogInfo("Using shader '" + name + "' for VRM materials.");
                    return s;
                }
            }
            Plugin.Log.LogWarning("None of the configured shaders were found; VRM materials may look wrong.");
            return null;
        }

        public static Material Create(string name, Color color, Texture2D tex, bool cutout, bool blend, float cutoff,
            Color emissive, Texture2D emissiveTex, int queueOverride)
        {
            var shader = FindShader();
            var m = shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
            m.name = name;
            m.color = color;
            if (tex != null) m.mainTexture = tex;

            bool isStandard = m.HasProperty("_Glossiness") && m.HasProperty("_Mode");
            if (isStandard)
            {
                m.SetFloat("_Glossiness", 0f);
                m.SetFloat("_GlossMapScale", 0f);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_SpecularHighlights", 0f);
                m.SetFloat("_GlossyReflections", 0f);
                m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                m.EnableKeyword("_GLOSSYREFLECTIONS_OFF");

                if (blend)
                {
                    m.SetFloat("_Mode", 2f); // Fade
                    m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    m.SetInt("_ZWrite", 0);
                    m.DisableKeyword("_ALPHATEST_ON");
                    m.EnableKeyword("_ALPHABLEND_ON");
                    m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    m.SetOverrideTag("RenderType", "Transparent");
                    m.renderQueue = queueOverride > 0 ? queueOverride : 3000;
                }
                else if (cutout)
                {
                    m.SetFloat("_Mode", 1f);
                    m.SetFloat("_Cutoff", cutoff);
                    m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    m.SetInt("_ZWrite", 1);
                    m.EnableKeyword("_ALPHATEST_ON");
                    m.DisableKeyword("_ALPHABLEND_ON");
                    m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    m.SetOverrideTag("RenderType", "TransparentCutout");
                    m.renderQueue = 2450;
                }
                else
                {
                    m.SetFloat("_Mode", 0f);
                    m.SetOverrideTag("RenderType", "");
                    m.renderQueue = -1;
                }

                float lift = Mathf.Clamp01(Plugin.CfgShadeLift.Value);
                Color em = emissive;
                Texture emTex = emissiveTex;
                if (lift > 0f && tex != null && emissiveTex == null)
                {
                    // Re-use the base texture as a weak emission map: brightens shadowed areas, closer to toon shading.
                    em = new Color(color.r * lift + emissive.r, color.g * lift + emissive.g, color.b * lift + emissive.b);
                    emTex = tex;
                }
                if (em.maxColorComponent > 0.001f)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", em);
                    if (emTex != null) m.SetTexture("_EmissionMap", emTex);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
            }
            else
            {
                if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", cutoff);
                if (blend) m.renderQueue = queueOverride > 0 ? queueOverride : 3000;
                else if (cutout) m.renderQueue = 2450;
            }
            return m;
        }
    }
}
