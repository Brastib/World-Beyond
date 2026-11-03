using System;
using System.Collections.Generic;
using UnityEngine;
using WorldBeyond.Simulation.World;

namespace WorldBeyond.Presentation.WorldView
{
    // Bodentexturen fürs Gelände: lädt T_Ground_<Schicht>_D/_N aus Resources/WorldBeyondTerrain (Art/Terrain), packt sie
    // in Texture2DArrays (Graphics.CopyTexture, keine CPU-Kopie) und schreibt je Kachel die Schicht in drei Steuerkarten.
    // Shader: WorldBeyond/TerrainSplat (gleicher Ordner). TryCreate liefert null, wenn Shader, Texturen oder
    // CopyTexture fehlen; WorldRenderer bleibt dann beim bisherigen Material (Farbkarte).
    public sealed class TerrainSplat : IDisposable
    {
        const string Folder = "WorldBeyondTerrain/";

        readonly IWorldQuery _world;
        readonly byte[][] _ctrl;
        readonly Texture2D[] _ctrlTex = new Texture2D[3];
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        bool _dirty;

        public Material Material { get; private set; }

        TerrainSplat(IWorldQuery world)
        {
            _world = world;
            int n = world.Width * world.Height * 4;
            _ctrl = new[] { new byte[n], new byte[n], new byte[n] };
        }

        public static TerrainSplat TryCreate(IWorldQuery world, Texture colorMap)
        {
            var shader = Resources.Load<Shader>(Folder + "TerrainSplat");
            if (shader == null) shader = Shader.Find("WorldBeyond/TerrainSplat");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("TerrainSplat: Shader WorldBeyond/TerrainSplat fehlt oder wird nicht unterstützt, Gelände bleibt einfarbig.");
                return null;
            }
            if (SystemInfo.copyTextureSupport == UnityEngine.Rendering.CopyTextureSupport.None)
            {
                Debug.LogWarning("TerrainSplat: Graphics.CopyTexture nicht unterstützt, Gelände bleibt einfarbig.");
                return null;
            }
            var s = new TerrainSplat(world);
            Texture2DArray albedo = s.BuildArray("_D", false);
            if (albedo == null)
            {
                s.Dispose();
                return null;
            }
            Texture2DArray normal = s.BuildArray("_N", true);

            var m = new Material(shader) { name = "TerrainSplat", enableInstancing = false };
            s._owned.Add(m);
            m.SetTexture("_ColorMap", colorMap);
            m.SetTexture("_Albedo", albedo);
            if (normal != null) m.SetTexture("_Normal", normal);
            m.SetFloat("_HasNormal", normal != null ? 1f : 0f);
            var macro = Resources.Load<Texture2D>(Folder + "T_Ground_Macro");
            if (macro != null) m.SetTexture("_Macro", macro);
            m.SetFloatArray("_Repeat", TerrainLayers.RepeatMeters);
            m.SetVector("_WorldSizeXZ", new Vector4(world.Width * WorldConstants.TileSize, world.Height * WorldConstants.TileSize, 0f, 0f));

            for (int i = 0; i < 3; i++)
            {
                var t = new Texture2D(world.Width, world.Height, TextureFormat.RGBA32, false, true)
                {
                    name = "TerrainSplatControl" + i,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                s._ctrlTex[i] = t;
                s._owned.Add(t);
                m.SetTexture("_Splat" + i, t);
            }
            for (int y = 0; y < world.Height; y++)
            {
                for (int x = 0; x < world.Width; x++)
                    TerrainLayers.Write(s._ctrl, world.Width, x, y, TerrainLayers.LayerFor(world.Get(x, y)));
            }
            s._dirty = true;
            s.ApplyIfDirty();
            s.Material = m;
            Debug.Log($"TerrainSplat: {TerrainLayers.Count} Bodenschichten geladen" + (normal != null ? " (mit Normalen)." : " (ohne Normalen)."));
            return s;
        }

        // Alle Schichten müssen gleich groß sein und dasselbe Format haben (README: 1024², gleiche Importeinstellungen).
        Texture2DArray BuildArray(string suffix, bool linear)
        {
            var src = new Texture2D[TerrainLayers.Count];
            for (int i = 0; i < src.Length; i++)
            {
                src[i] = Resources.Load<Texture2D>(Folder + "T_Ground_" + TerrainLayers.Names[i] + suffix);
                if (src[i] == null)
                {
                    Debug.LogWarning($"TerrainSplat: T_Ground_{TerrainLayers.Names[i]}{suffix} fehlt.");
                    return null;
                }
                if (src[i].width != src[0].width || src[i].height != src[0].height || src[i].format != src[0].format || src[i].mipmapCount != src[0].mipmapCount)
                {
                    Debug.LogWarning($"TerrainSplat: T_Ground_{TerrainLayers.Names[i]}{suffix} hat andere Größe/Format/Mipmaps als {TerrainLayers.Names[0]} ({src[i].width}×{src[i].height} {src[i].format} vs. {src[0].width}×{src[0].height} {src[0].format}). Importeinstellungen angleichen.");
                    return null;
                }
            }
            var arr = new Texture2DArray(src[0].width, src[0].height, src.Length, src[0].format, src[0].mipmapCount > 1, linear)
            {
                name = "TerrainLayers" + suffix,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };
            for (int i = 0; i < src.Length; i++)
                Graphics.CopyTexture(src[i], 0, arr, i);   // alle Mip-Stufen eines Elements
            _owned.Add(arr);
            return arr;
        }

        public void UpdateTile(int x, int y)
        {
            if (!_world.InBounds(x, y))
                return;
            TerrainLayers.Write(_ctrl, _world.Width, x, y, TerrainLayers.LayerFor(_world.Get(x, y)));
            _dirty = true;
        }

        public void ApplyIfDirty()
        {
            if (!_dirty)
                return;
            _dirty = false;
            for (int i = 0; i < 3; i++)
            {
                _ctrlTex[i].SetPixelData(_ctrl[i], 0);
                _ctrlTex[i].Apply(false, false);
            }
        }

        public void Dispose()
        {
            foreach (var o in _owned)
                UnityObjects.Destroy(o);
            _owned.Clear();
            Material = null;
        }
    }
}
