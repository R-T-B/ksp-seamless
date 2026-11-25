using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;

namespace Seamless
{
    /// <summary>
    /// Orbitron (SIL OFL) as TextMeshPro distance-field fonts, built at runtime from the atlases
    /// tools/sdfgen wrote into GameData/Seamless/Fonts (KSP's TMP is the old 1.0-style
    /// API: FaceInfo + TMP_Glyph list + atlas + material). Falls back to the game's UI font when
    /// anything is missing; the game font is also the fallback for characters Orbitron lacks.
    /// </summary>
    internal static class Fonts
    {
        private static TMP_FontAsset bold, medium;
        private static bool failed;

        public static TMP_FontAsset Bold { get { Ensure(); return bold != null ? bold : GameFont(); } }
        public static TMP_FontAsset Medium { get { Ensure(); return medium != null ? medium : GameFont(); } }

        public static TMP_FontAsset GameFont()
        {
            try { return UISkinManager.TMPFont; } catch { return null; }
        }

        private static void Ensure()
        {
            if (failed || (bold != null && medium != null)) return;
            TMP_FontAsset game = GameFont();
            if (game == null || game.material == null) return;   // try again later
            try
            {
                string dir = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/Seamless/Fonts");
                if (bold == null) bold = Load(Path.Combine(dir, "Orbitron-Bold"), game);
                if (medium == null) medium = Load(Path.Combine(dir, "Orbitron-Medium"), game);
            }
            catch (Exception e)
            {
                failed = true;
                Log.Warn("Orbitron fonts not loaded, using the game font: " + e.Message);
            }
        }

        private static TMP_FontAsset Load(string basePath, TMP_FontAsset game)
        {
            byte[] raw = File.ReadAllBytes(basePath + ".atlas");
            int w = BitConverter.ToInt32(raw, 0), h = BitConverter.ToInt32(raw, 4);
            if (w <= 0 || h <= 0 || raw.Length != 8 + w * h) throw new Exception("bad atlas " + basePath);
            var px = new byte[w * h];
            Buffer.BlockCopy(raw, 8, px, 0, px.Length);
            var tex = new Texture2D(w, h, TextureFormat.Alpha8, false, true);
            tex.name = Path.GetFileName(basePath) + " Atlas";
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.LoadRawTextureData(px);
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

            var face = new FaceInfo { Scale = 1f, Baseline = 0f };
            var glyphs = new List<TMP_Glyph>();
            var kern = new KerningTable();
            float gradient = 10f;
            var inv = CultureInfo.InvariantCulture;
            foreach (string line in File.ReadAllLines(basePath + ".metrics"))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split(' ');
                switch (p[0])
                {
                    case "name": face.Name = line.Substring(5); break;
                    case "face":
                        float v = float.Parse(p[2], inv);
                        switch (p[1])
                        {
                            case "PointSize": face.PointSize = v; break;
                            case "Ascender": face.Ascender = v; break;
                            case "Descender": face.Descender = v; break;
                            case "LineHeight": face.LineHeight = v; break;
                            case "CapHeight": face.CapHeight = v; break;
                            case "Underline": face.Underline = v; break;
                            case "UnderlineThickness": face.UnderlineThickness = v; break;
                            case "Padding": face.Padding = v; break;
                            case "GradientScale": gradient = v; break;
                            case "AtlasWidth": face.AtlasWidth = v; break;
                            case "AtlasHeight": face.AtlasHeight = v; break;
                        }
                        break;
                    case "glyph":
                        glyphs.Add(new TMP_Glyph
                        {
                            id = int.Parse(p[1], inv),
                            x = float.Parse(p[2], inv), y = float.Parse(p[3], inv),
                            width = float.Parse(p[4], inv), height = float.Parse(p[5], inv),
                            xOffset = float.Parse(p[6], inv), yOffset = float.Parse(p[7], inv),
                            xAdvance = float.Parse(p[8], inv), scale = 1f,
                        });
                        break;
                    case "kern":
                        kern.AddKerningPair(uint.Parse(p[1], inv), uint.Parse(p[2], inv), float.Parse(p[3], inv));
                        break;
                }
            }
            face.CenterLine = face.CapHeight * 0.5f;
            face.SuperscriptOffset = face.Ascender;
            face.SubscriptOffset = face.Underline;
            face.SubSize = 0.5f;
            face.strikethrough = face.CapHeight * 0.4f;
            face.strikethroughThickness = face.UnderlineThickness;

            var mat = new Material(game.material);
            mat.name = Path.GetFileName(basePath) + " Material";
            foreach (string k in new[] { "OUTLINE_ON", "UNDERLAY_ON", "UNDERLAY_INNER", "GLOW_ON", "BEVEL_ON" })
                mat.DisableKeyword(k);
            mat.SetTexture("_MainTex", tex);
            Set(mat, "_TextureWidth", w);
            Set(mat, "_TextureHeight", h);
            Set(mat, "_GradientScale", gradient);
            Set(mat, "_FaceDilate", 0f);
            Set(mat, "_OutlineWidth", 0f);
            Set(mat, "_OutlineSoftness", 0f);
            Set(mat, "_WeightNormal", 0f);
            Set(mat, "_WeightBold", 0.5f);
            Set(mat, "_Sharpness", 0f);
            mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            try { ShaderUtilities.UpdateShaderRatios(mat); } catch { }

            var fa = ScriptableObject.CreateInstance<TMP_FontAsset>();
            fa.name = Path.GetFileName(basePath);
            fa.hideFlags = HideFlags.DontUnloadUnusedAsset;
            fa.fontAssetType = TMP_FontAsset.FontAssetTypes.SDF;
            fa.atlas = tex;
            fa.material = mat;
            fa.AddFaceInfo(face);
            fa.AddGlyphInfo(glyphs.ToArray());
            fa.AddKerningInfo(kern);
            fa.ReadFontDefinition();
            fa.fallbackFontAssets = new List<TMP_FontAsset> { game };
            Log.Info("font " + fa.name + ": " + glyphs.Count + " glyphs, atlas " + w + "x" + h);
            return fa;
        }

        private static void Set(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }
    }
}
