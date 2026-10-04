using UnityEditor;
using UnityEngine;

namespace BadAppleHotel.EditorTools
{
    /// <summary>
    /// Textures under Assets/Resources/Art are comic-style art, not pixel art: keep them uncompressed with mipmaps and
    /// smooth filtering so they stay sharp when the camera shrinks them to a few dozen pixels per tile.
    /// Sprites.cs builds the actual Sprite (size, pivot) at runtime, so no per-file import settings are needed.
    /// </summary>
    public class ArtImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/Art/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 512;
        }
    }
}
