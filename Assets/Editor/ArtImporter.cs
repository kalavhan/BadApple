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
            if (assetPath.StartsWith("Assets/Resources/Art/Walls/"))
            {
                // WallKitImporter owns the preview sprite's pivot, size and alpha settings.
                // Unlike the older art, these are actual imported Sprite assets.
                return;
            }
            if (assetPath.StartsWith("Assets/Resources/Art/Materials/") || assetPath.StartsWith("Assets/Resources/Art/Directions/"))
            {
                ti.textureType = TextureImporterType.Default; ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false; ti.filterMode = FilterMode.Point;
                ti.wrapMode = assetPath.Contains("/Materials/") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.npotScale = TextureImporterNPOTScale.None; ti.maxTextureSize = 2048;
                return;
            }
            if (assetPath.StartsWith("Assets/Resources/Art/Chars/"))
            {
                // animation atlases: big sheets, so compress them; no mipmaps (frames sit side by side)
                ti.textureType = TextureImporterType.Default;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.filterMode = FilterMode.Bilinear;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.maxTextureSize = 2048;
                return;
            }
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
