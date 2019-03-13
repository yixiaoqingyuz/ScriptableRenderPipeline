using System.Collections.Generic;

namespace UnityEngine.Experimental.Rendering
{
    using RTHandle = RTHandleSystem.RTHandle;

    public class RenderGraphResourceRegistry
    {
        struct TextureResource
        {
            public bool     imported;
            public RTHandle rt;
        }

        List<TextureResource> m_TextureResources = new List<TextureResource>();

        // =============================
        //      Public Interface
        // =============================
        public RTHandle GetTexture(RenderGraphResource handle)
        {
            return m_TextureResources[handle.handle].rt;
        }

        // ====================================
        //      Private/internal Interface
        // ====================================
        internal RenderGraphMutableResource ImportTexture(RTHandle rt)
        {
            int newHandle = m_TextureResources.Count;
            m_TextureResources.Add(new TextureResource { imported = true, rt = rt });

            return new RenderGraphMutableResource(newHandle, RenderGraphResourceType.Texture);
        }

        internal void Clear()
        {
            m_TextureResources.Clear();
        }
    }
}
