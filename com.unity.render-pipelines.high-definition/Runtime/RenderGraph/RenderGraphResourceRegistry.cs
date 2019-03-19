using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering
{
    using RTHandle = RTHandleSystem.RTHandle;

    public class RenderGraphResourceRegistry
    {
        public enum TextureSizeMode
        {
            Explicit,
            Scale,
            Functor
        }

        public struct TextureDesc
        {
            public TextureSizeMode sizeMode;
            public int width;
            public int height;
            public int slices;
            public Vector2 scale;
            public ScaleFunc func;
            public DepthBits depthBufferBits;
            public GraphicsFormat colorFormat;
            public FilterMode filterMode;
            public TextureWrapMode wrapMode;
            public TextureDimension dimension;
            public bool enableRandomWrite;
            public bool useMipMap;
            public bool autoGenerateMips;
            public bool isShadowMap;
            public int anisoLevel;
            public float mipMapBias;
            public bool enableMSAA; // Only supported for Scale and Functor size mode
            public MSAASamples msaaSamples; // Only supported for Explicit size mode
            public bool bindTextureMS;
            public bool useDynamicScale;
            public bool xrInstancing;
            public RenderTextureMemoryless memoryless;
            public string name;

            public TextureDesc(int width, int height)
                : this()
            {
                // Size related init
                sizeMode = TextureSizeMode.Explicit;
                this.width = width;
                this.height = height;
                // Important default values not handled by zero construction in this()
                slices = 1;
                msaaSamples = MSAASamples.None;
                dimension = TextureDimension.Tex2D;
            }

            public TextureDesc(Vector2 scale)
                : this()
            {
                // Size related init
                sizeMode = TextureSizeMode.Scale;
                this.scale = scale;
                // Important default values not handled by zero construction in this()
                slices = 1;
                msaaSamples = MSAASamples.None;
                dimension = TextureDimension.Tex2D;
            }

            public TextureDesc(ScaleFunc func)
                : this()
            {
                // Size related init
                sizeMode = TextureSizeMode.Functor;
                this.func = func;
                // Important default values not handled by zero construction in this()
                slices = 1;
                msaaSamples = MSAASamples.None;
                dimension = TextureDimension.Tex2D;
            }

            public override int GetHashCode()
            {
                int hashCode = 17;

                unchecked
                {
                    switch (sizeMode)
                    {
                        case TextureSizeMode.Explicit:
                            hashCode = hashCode * 23 + width;
                            hashCode = hashCode * 23 + height;
                            hashCode = hashCode * 23 + (int)msaaSamples;
                            break;
                        case TextureSizeMode.Functor:
                            if (func != null)
                                hashCode = hashCode * 23 + func.GetHashCode();
                            hashCode = hashCode * 23 + (enableMSAA ? 1 : 0);
                            break;
                        case TextureSizeMode.Scale:
                            hashCode = hashCode * 23 + scale.x.GetHashCode();
                            hashCode = hashCode * 23 + scale.y.GetHashCode();
                            hashCode = hashCode * 23 + (enableMSAA ? 1 : 0);
                            break;
                    }

                    hashCode = hashCode * 23 + mipMapBias.GetHashCode();
                    hashCode = hashCode * 23 + slices;
                    hashCode = hashCode * 23 + (int)depthBufferBits;
                    hashCode = hashCode * 23 + (int)colorFormat;
                    hashCode = hashCode * 23 + (int)filterMode;
                    hashCode = hashCode * 23 + (int)wrapMode;
                    hashCode = hashCode * 23 + (int)dimension;
                    hashCode = hashCode * 23 + (int)memoryless;
                    hashCode = hashCode * 23 + anisoLevel;
                    hashCode = hashCode * 23 + (enableRandomWrite ? 1 : 0);
                    hashCode = hashCode * 23 + (useMipMap ? 1 : 0);
                    hashCode = hashCode * 23 + (autoGenerateMips ? 1 : 0);
                    hashCode = hashCode * 23 + (isShadowMap ? 1 : 0);
                    hashCode = hashCode * 23 + (bindTextureMS ? 1 : 0);
                    hashCode = hashCode * 23 + (useDynamicScale ? 1 : 0);
                    hashCode = hashCode * 23 + (xrInstancing ? 1 : 0);
                }

                return hashCode;
            }
        }

        struct TextureResource
        {
            public TextureDesc  desc;
            public bool         imported;
            public RTHandle     rt;
            public int          cachedHash;
            public int          firstWritePassIndex;
            public int          lastReadPassIndex;

            public TextureResource(RTHandle rt)
                : this()
            {
                Reset();

                this.rt = rt;
                imported = true;
            }

            public TextureResource(in TextureDesc desc)
                : this()
            {
                Reset();

                this.desc = desc;
            }

            void Reset()
            {
                imported = false;
                rt = null;
                cachedHash = -1;
                firstWritePassIndex = int.MaxValue;
                lastReadPassIndex = 0;
            }
        }

        // No List<> here because we want to be able to access and update elements by ref
        // And we want to avoid allocation so TextureResource stays a struct
        TextureResource[]                   m_TextureResources = new TextureResource[32];
        int                                 m_TextureResourcesCount = 0;
        Dictionary<int, Stack<RTHandle>>    m_TexturePool = new Dictionary<int, Stack<RTHandle>>();

        // Diagnostic only
        List<(int, RTHandle)>               m_AllocatedTextures = new List<(int, RTHandle)>();

        #region Public Interface
        internal void Cleanup()
        {
            foreach (var value in m_TexturePool)
            {
                foreach (var rt in value.Value)
                {
                    RTHandles.Release(rt);
                }
            }
        }

        public RTHandle GetTexture(in RenderGraphResource handle)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (handle.type == RenderGraphResourceType.Invalid || m_TextureResources[handle.handle].rt == null)
                throw new InvalidOperationException("Trying to access a RenderGraphResource that has not been created or imported or that has been released.");
#endif
            return m_TextureResources[handle.handle].rt;
        }
        #endregion

        #region Internal Interface

        int AddTextureResource(TextureResource tex)
        {
            int index = m_TextureResourcesCount;

            // Grow array if needed;
            if(index >= m_TextureResources.Length)
            {
                var newArray = new TextureResource[m_TextureResources.Length * 2];
                Array.Copy(m_TextureResources, newArray, m_TextureResources.Length);
                m_TextureResources = newArray;
            }

            m_TextureResources[index] = tex;
            m_TextureResourcesCount++;
            return index;
        }

        // Texture Creation/Import APIs are internal because creation should only go through RenderGraph (for globals, outside of render passes) and RenderGraphBuilder (for render passes)
        internal RenderGraphMutableResource ImportTexture(RTHandle rt)
        {
            int newHandle = AddTextureResource(new TextureResource(rt));
            return new RenderGraphMutableResource(newHandle, RenderGraphResourceType.Texture);
        }

        internal RenderGraphMutableResource CreateTexture(in TextureDesc desc)
        {
            ValidateTextureDesc(desc);

            int newHandle = AddTextureResource(new TextureResource(desc));
            return new RenderGraphMutableResource(newHandle, RenderGraphResourceType.Texture);
        }

        internal void UpdateResourceFirstWrite(RenderGraphMutableResource res, int passIndex)
        {
            m_TextureResources[res.handle].firstWritePassIndex = Math.Min(passIndex, m_TextureResources[res.handle].firstWritePassIndex);
        }

        internal void UpdateResourceLastRead(RenderGraphResource res, int passIndex)
        {
            m_TextureResources[res.handle].lastReadPassIndex = Math.Max(passIndex, m_TextureResources[res.handle].lastReadPassIndex);
        }

        internal void CreateResourcesForPass(int passIndex, List<RenderGraphMutableResource> resourceWriteList)
        {
            foreach (var resource in resourceWriteList)
            {
                ref var resourceDesc = ref m_TextureResources[resource.handle];
                if (!resourceDesc.imported && resourceDesc.firstWritePassIndex == passIndex)
                {
                    resourceDesc.cachedHash = CreateTextureResource(resourceDesc.desc, out resourceDesc.rt);
                }
            }
        }

        internal void ReleaseResourcesForPass(int passIndex, List<RenderGraphResource> resourceReadList, List<RenderGraphMutableResource> resourceWriteList)
        {
            foreach (var resource in resourceReadList)
            {
                ref var resourceDesc = ref m_TextureResources[resource.handle];
                if (!resourceDesc.imported && resourceDesc.lastReadPassIndex == passIndex)
                {
                    ReleaseTextureResource(resourceDesc.cachedHash, resourceDesc.rt);
                    resourceDesc.rt = null;
                    resourceDesc.cachedHash = -1;
                }
            }

            // If a resource was created for only a single pass, we don't want users to have to declare explicitly the read operation.
            // So here we test resources written, if they have the initial lastReadPassIndex value of zero, it means that no subsequent pass will read it so we can release it
            foreach (var resource in resourceWriteList)
            {
                ref var resourceDesc = ref m_TextureResources[resource.handle];
                if (!resourceDesc.imported && resourceDesc.lastReadPassIndex == 0)
                {
                    ReleaseTextureResource(resourceDesc.cachedHash, resourceDesc.rt);
                    resourceDesc.rt = null;
                    resourceDesc.cachedHash = -1;
                }
            }
        }

        void ValidateTextureDesc(in TextureDesc desc)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (desc.colorFormat == GraphicsFormat.None)
            {
                throw new ArgumentException("Texture was created with an invalid color format.");
            }

            if (desc.dimension == TextureDimension.None)
            {
                throw new ArgumentException("Texture was created with an invalid texture dimension.");
            }

            if (desc.slices == 0)
            {
                throw new ArgumentException("Texture was created with a slices parameter value of zero.");
            }

            if (desc.sizeMode == TextureSizeMode.Explicit)
            {
                if (desc.width == 0 || desc.height == 0)
                    throw new ArgumentException("Texture using Explicit size mode was create with either width or height at zero.");
                if (desc.enableMSAA)
                    throw new ArgumentException("enableMSAA TextureDesc parameter is not supported for textures using Explicit size mode.");
            }

            if (desc.sizeMode == TextureSizeMode.Scale || desc.sizeMode == TextureSizeMode.Functor)
            {
                if (desc.msaaSamples != MSAASamples.None)
                    throw new ArgumentException("msaaSamples TextureDesc parameter is not supported for textures using Scale or Functor size mode.");
            }
#endif
        }

        int CreateTextureResource(in TextureDesc desc, out RTHandle rt)
        {
            int hashCode = desc.GetHashCode();

            rt = null;
            if (!TryGetRenderTarget(hashCode, out rt))
            {
                // Note: Name used here will be the one visible in the memory profiler so it means that whatever is the first pass that actually allocate the texture will set the name.
                // TODO: Find a way to display name by pass.
                switch(desc.sizeMode)
                {
                    case TextureSizeMode.Explicit:
                        rt = RTHandles.Alloc(desc.width, desc.height, desc.slices, desc.depthBufferBits, desc.colorFormat, desc.filterMode, desc.wrapMode, desc.dimension, desc.enableRandomWrite,
                        desc.useMipMap, desc.autoGenerateMips, desc.isShadowMap, desc.anisoLevel, desc.mipMapBias, desc.msaaSamples, desc.bindTextureMS, desc.useDynamicScale, desc.xrInstancing, desc.memoryless, desc.name);
                        break;
                    case TextureSizeMode.Scale:
                        rt = RTHandles.Alloc(desc.scale, desc.slices, desc.depthBufferBits, desc.colorFormat, desc.filterMode, desc.wrapMode, desc.dimension, desc.enableRandomWrite,
                        desc.useMipMap, desc.autoGenerateMips, desc.isShadowMap, desc.anisoLevel, desc.mipMapBias, desc.enableMSAA, desc.bindTextureMS, desc.useDynamicScale, desc.xrInstancing, desc.memoryless, desc.name);
                        break;
                    case TextureSizeMode.Functor:
                        rt = RTHandles.Alloc(desc.func, desc.slices, desc.depthBufferBits, desc.colorFormat, desc.filterMode, desc.wrapMode, desc.dimension, desc.enableRandomWrite,
                        desc.useMipMap, desc.autoGenerateMips, desc.isShadowMap, desc.anisoLevel, desc.mipMapBias, desc.enableMSAA, desc.bindTextureMS, desc.useDynamicScale, desc.xrInstancing, desc.memoryless, desc.name);
                        break;
                }
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (hashCode != -1)
            {
                m_AllocatedTextures.Add((hashCode, rt));
            }
#endif

            return hashCode;
        }

        void ReleaseTextureResource(int hash, RTHandle rt)
        {
            if (!m_TexturePool.TryGetValue(hash, out var stack))
            {
                stack = new Stack<RTHandle>();
                m_TexturePool.Add(hash, stack);
            }

            stack.Push(rt);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            m_AllocatedTextures.Remove((hash, rt));
#endif
        }

        bool TryGetRenderTarget(int hashCode, out RTHandle rt)
        {
            if (m_TexturePool.TryGetValue(hashCode, out var stack) && stack.Count > 0)
            {
                rt = stack.Pop();
                return true;
            }

            rt = null;
            return false;
        }

        internal void Clear()
        {
            m_TextureResourcesCount = 0;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (m_AllocatedTextures.Count != 0)
            {
                Debug.LogWarning("RenderGraph: Not all textures were released.");
                foreach (var value in m_AllocatedTextures)
                {
                    ReleaseTextureResource(value.Item1, value.Item2);
                }
            }
#endif
        }
#endregion
    }
}
