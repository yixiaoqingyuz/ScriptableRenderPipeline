using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering.HDPipeline;

namespace UnityEngine.Experimental.Rendering
{
    public class PowerOfTwoTextureAtlas : Texture2DAtlas
    {
        public int mipPadding;

        public PowerOfTwoTextureAtlas(int size, int mipPadding, GraphicsFormat format, FilterMode filterMode = FilterMode.Point, string name = "", bool useMipMap = true)
            : base(size, size, format, filterMode, true, name, useMipMap)
        {
            this.mipPadding = mipPadding;

            // Check if size is a power of two
            if ((size & (size - 1)) != 0)
                Debug.Assert(false, "Power of two atlas was constructed with non power of two size: " + size);
        }

        int GetTexturePadding()
        {
            return (int)Mathf.Pow(2, mipPadding) * 2;
        }
        
        void Blit2DTexturePadding(CommandBuffer cmd, Vector4 scaleOffset, Texture texture, Vector4 sourceScaleOffset)
        {
            int mipCount = GetTextureMipmapCount(texture.width, texture.height);
            int pixelPadding = GetTexturePadding();
            Vector2 textureSize = GetPowerOfTwoTextureSize(texture);
            bool bilinear = texture.filterMode != FilterMode.Point;

            using (new ProfilingSample(cmd, "Blit texture with padding"))
            {
                for (int mipLevel = 0; mipLevel < mipCount; mipLevel++)
                {
                    cmd.SetRenderTarget(m_AtlasTexture, mipLevel);
                    HDUtils.BlitPaddedQuad(cmd, texture, textureSize, sourceScaleOffset, scaleOffset, mipLevel, bilinear, pixelPadding);
                }
            }
        }

        public override void BlitTexture(CommandBuffer cmd, Vector4 scaleOffset, Texture texture, Vector4 sourceScaleOffset)
        {
            // We handle ourself the 2D blit because cookies needs mipPadding for trilinear filtering
            if (Is2D(texture))
                Blit2DTexturePadding(cmd, scaleOffset, texture, sourceScaleOffset);
        }

        void TextureSizeToPowerOfTwo(Texture texture, ref int width, ref int height)
        {
            // Change the width and height of the texture to be power of two
            width = Mathf.NextPowerOfTwo(width);
            height = Mathf.NextPowerOfTwo(height);
        }

        Vector2 GetPowerOfTwoTextureSize(Texture texture)
        {
            int width = texture.width, height = texture.height;
            
            TextureSizeToPowerOfTwo(texture, ref width, ref height);
            return new Vector2(width, height);
        }

        // Override the behavior when we add a texture so all non-pot textures are blitted to a pot target zone
        public override bool AllocateTexture(CommandBuffer cmd, ref Vector4 scaleOffset, Texture texture, int width, int height)
        {
            // This atlas only supports square textures
            if (height != width)
                return false;

            TextureSizeToPowerOfTwo(texture, ref height, ref width);

            return base.AllocateTexture(cmd, ref scaleOffset, texture, width, height);
        }
    }
}
