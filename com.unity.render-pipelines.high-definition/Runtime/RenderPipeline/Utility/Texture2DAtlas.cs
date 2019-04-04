using System;
using System.Collections.Generic;
using UnityEngine.Rendering;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    public class AtlasAllocator
    {
        private class AtlasNode
        {
            public AtlasNode m_RightChild = null;
            public AtlasNode m_BottomChild = null;
            public Vector4 m_Rect = new Vector4(0, 0, 0, 0); // x,y is width and height (scale) z,w offset into atlas (offset)

            public AtlasNode Allocate(int width, int height, bool powerOfTwoPadding)
            {
                // not a leaf node, try children
                if (m_RightChild != null)
                {
                    AtlasNode node = m_RightChild.Allocate(width, height, powerOfTwoPadding);
                    if (node == null)
                    {
                        node = m_BottomChild.Allocate(width, height, powerOfTwoPadding);
                    }
                    return node;
                }
                
                int wPadd = 0;
                int hPadd = 0;
                
                if (powerOfTwoPadding)
                {
                    wPadd = (int)m_Rect.x % width;
                    hPadd = (int)m_Rect.y % height;
                }

                //leaf node, check for fit
                if ((width <= m_Rect.x - wPadd) && (height <= m_Rect.y - hPadd))
                {
                    // perform the split
                    m_RightChild = new AtlasNode();
                    m_BottomChild = new AtlasNode();
                    
                    m_Rect.z += wPadd;
                    m_Rect.w += hPadd;
                    m_Rect.x -= wPadd;
                    m_Rect.y -= hPadd;

                    if (width > height) // logic to decide which way to split
                    {
                                                                                //  +--------+------+
                        m_RightChild.m_Rect.z = m_Rect.z + width;               //  |        |      |
                        m_RightChild.m_Rect.w = m_Rect.w;                       //  +--------+------+
                        m_RightChild.m_Rect.x = m_Rect.x - width;               //  |               |
                        m_RightChild.m_Rect.y = height;                         //  |               |
                                                                                //  +---------------+
                        m_BottomChild.m_Rect.z = m_Rect.z;
                        m_BottomChild.m_Rect.w = m_Rect.w + height;
                        m_BottomChild.m_Rect.x = m_Rect.x;
                        m_BottomChild.m_Rect.y = m_Rect.y - height;
                    }
                    else
                    {                                                           //  +---+-----------+
                        m_RightChild.m_Rect.z = m_Rect.z + width;               //  |   |           |
                        m_RightChild.m_Rect.w = m_Rect.w;                       //  |   |           |
                        m_RightChild.m_Rect.x = m_Rect.x - width;               //  +---+           +
                        m_RightChild.m_Rect.y = m_Rect.y;                       //  |   |           |
                                                                                //  +---+-----------+
                        m_BottomChild.m_Rect.z = m_Rect.z;
                        m_BottomChild.m_Rect.w = m_Rect.w + height;
                        m_BottomChild.m_Rect.x = width;
                        m_BottomChild.m_Rect.y = m_Rect.y - height;
                    }
                    m_Rect.x = width;
                    m_Rect.y = height;
                    return this;
                }
                Debug.Log("FAIL");
                return null;
            }

            public void Release()
            {
                if (m_RightChild != null)
                {
                    m_RightChild.Release();
                    m_BottomChild.Release();
                }
                m_RightChild = null;
                m_BottomChild = null;
            }
        }

        private AtlasNode m_Root;
        private int m_Width;
        private int m_Height;
        private bool powerOfTwoPadding;

        public AtlasAllocator(int width, int height, bool potPadding)
        {
            m_Root = new AtlasNode();
            m_Root.m_Rect.Set(width, height, 0, 0);
            m_Width = width;
            m_Height = height;
            powerOfTwoPadding = potPadding;
        }

        public bool Allocate(ref Vector4 result, int width, int height)
        {
            Debug.Log()
            AtlasNode node = m_Root.Allocate(width, height, powerOfTwoPadding);
            if (node != null)
            {
                result = node.m_Rect;
                return true;
            }
            else
            {
                result = Vector4.zero;
                return false;
            }
        }

        public void Release()
        {
            m_Root.Release();
            m_Root = new AtlasNode();
            m_Root.m_Rect.Set(m_Width, m_Height, 0, 0);
        }
    }

    public class Texture2DAtlas
    {
        protected RTHandleSystem.RTHandle m_AtlasTexture = null;
        protected int m_Width;
        protected int m_Height;
        protected GraphicsFormat m_Format;
        protected AtlasAllocator m_AtlasAllocator = null;
        protected Dictionary<IntPtr, Vector4> m_AllocationCache = new Dictionary<IntPtr, Vector4>();
        protected Dictionary<IntPtr, uint> m_CustomRenderTextureUpdateCache = new Dictionary<IntPtr, uint>();

        public RTHandleSystem.RTHandle AtlasTexture
        {
            get
            {
                return m_AtlasTexture;
            }
        }

        public Texture2DAtlas(int width, int height, GraphicsFormat format, FilterMode filterMode = FilterMode.Point, bool powerOfTwoPadding = false, string name = "", bool useMipMap = true)
        {
            m_Width = width;
            m_Height = height;
            m_Format = format;
            m_AtlasTexture = RTHandles.Alloc(
                width: m_Width,
                height: m_Height,
                filterMode: filterMode,
                colorFormat: m_Format,
                wrapMode: TextureWrapMode.Clamp,
                useMipMap: useMipMap,
                name: name
            );

            m_AtlasAllocator = new AtlasAllocator(width, height, powerOfTwoPadding);
        }

        public void Release()
        {
            ResetAllocator();
            RTHandles.Release(m_AtlasTexture);
        }

        public void ResetAllocator()
        {
            m_AtlasAllocator.Release();
            m_AllocationCache.Clear();
        }

        public void ClearTarget(CommandBuffer cmd)
            // clear the atlas by blitting a black texture
        {
            BlitTexture(cmd, new Vector4(1, 1, 0, 0), Texture2D.blackTexture);
        }

        protected int GetTextureMipmapCount(int width, int height)
        {
            // We don't care about the real mipmap count in the texture because they are generated by the atlas
            float maxSize = Mathf.Max(width, height);
            return Mathf.CeilToInt(Mathf.Log(maxSize) / Mathf.Log(2));
        }

        protected bool Is2D(Texture texture)
        {
            CustomRenderTexture crt = texture as CustomRenderTexture;
            return (texture is Texture2D || (crt != null && crt.dimension == TextureDimension.Tex2D));
        }

        protected void Blit2DTexture(CommandBuffer cmd, Vector4 scaleOffset, Texture texture)
        {
            int mipCount = GetTextureMipmapCount(texture.width, texture.height);

            for (int mipLevel = 0; mipLevel < mipCount; mipLevel++)
            {
                cmd.SetRenderTarget(m_AtlasTexture, mipLevel);
                HDUtils.BlitQuad(cmd, texture, new Vector4(1, 1, 0, 0), scaleOffset, mipLevel, true);
            }
        }

        protected virtual void BlitTexture(CommandBuffer cmd, Vector4 scaleOffset, Texture texture)
        {
            // This atlas only support 2D texture so we only blit 2D textures
            if (Is2D(texture))
                Blit2DTexture(cmd, scaleOffset, texture);
        }

        protected virtual bool AllocateTexture(CommandBuffer cmd, ref Vector4 scaleOffset, Texture texture, int width, int height)
        {
            if (width <= 0 && height <= 0)
                return false;

            Debug.Log("Alloc " + width + ", " + height);
            
            if (m_AtlasAllocator.Allocate(ref scaleOffset, width, height))
            {
                scaleOffset.Scale(new Vector4(1.0f / m_Width, 1.0f / m_Height, 1.0f / m_Width, 1.0f / m_Height));
                BlitTexture(cmd, scaleOffset, texture);
                m_AllocationCache.Add(texture.GetNativeTexturePtr(), scaleOffset);
                return true;
            }
            else
            {
                return false;
            }
        }

        protected bool IsCached(CommandBuffer cmd, out Vector4 scaleOffset, Texture texture)
        {
            bool                cached = false;
            IntPtr              key = texture.GetNativeTexturePtr();
            CustomRenderTexture crt = texture as CustomRenderTexture;

            if (m_AllocationCache.TryGetValue(key, out scaleOffset))
                cached = true;

            // Update the custom render texture if needed
            if (crt != null && cached)
            {
                uint updateCount;
                if (m_CustomRenderTextureUpdateCache.TryGetValue(key, out updateCount))
                {
                    if (crt.updateCount != updateCount)
                        BlitTexture(cmd, scaleOffset, crt);
                }
                m_CustomRenderTextureUpdateCache[key] = crt.updateCount;
            }

            // TODO: check if it's needed !
// #if UNITY_EDITOR
//             textureHash += (uint)texture.imageContentsHash.GetHashCode();
// #endif

            return cached;
        }

        public virtual bool AddTexture(CommandBuffer cmd, ref Vector4 scaleOffset, Texture texture)
        {
            if (IsCached(cmd, out scaleOffset, texture))
                return true;
            
            // We only support 2D texture in this class, support for other textures are provided by child classes (ex: PowerOfTwoTextureAtlas)
            if (!Is2D(texture))
                return false;

            return AllocateTexture(cmd, ref scaleOffset, texture, texture.width, texture.height);
        }

        public virtual bool UpdateTexture(CommandBuffer cmd, Texture oldTexture, Texture newTexture, ref Vector4 scaleOffset)
        {
            // In case the old texture is here, we Blit the new one at the scale offset of the old one
            if (IsCached(cmd, out scaleOffset, oldTexture))
            {
                BlitTexture(cmd, scaleOffset, newTexture);
                return true;
            }
            else // else we try to allocate the updated texture
            {
                return AllocateTexture(cmd, ref scaleOffset, newTexture, newTexture.width, newTexture.height);
            }
        }
    }
}
