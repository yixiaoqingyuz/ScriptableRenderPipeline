using System;
using System.Collections.Generic;

namespace UnityEngine.Experimental.Rendering.RenderGraphModule
{
    public struct RenderGraphBuilder : IDisposable
    {

        RenderGraph                         m_RenderGraph;
        RenderGraphResourceRegistry         m_RenderGraphResources;
        RenderGraph.RenderPassDescriptor    m_RenderPass;
        bool                                m_Disposed;

        #region Public Interface
        public RenderGraphMutableResource CreateTexture( in TextureDesc desc)
        {
            return m_RenderGraphResources.CreateTexture(desc);
        }

        public RenderGraphMutableResource WriteTexture(in RenderGraphMutableResource input)
        {
            if (input.type != RenderGraphResourceType.Texture)
                throw new ArgumentException("Trying to write to a resource that is not a texture.");
            // TODO: Manage resource "version" for debugging purpose
            m_RenderPass.resourceWriteList.Add(input);
            return input;
        }

        public RenderGraphResource ReadTexture(RenderGraphResource input)
        {
            if (input.type != RenderGraphResourceType.Texture)
                throw new ArgumentException("Trying to read a resource that is not a texture.");
            m_RenderPass.resourceReadList.Add(input);
            return input;
        }

        public RenderGraphResource CreateRendererList(in RendererListDesc desc)
        {
            return m_RenderGraphResources.CreateRendererList(desc);
        }

        public RenderGraphResource UseRendererList(in RenderGraphResource resource)
        {
            if (resource.type != RenderGraphResourceType.RendererList)
                throw new ArgumentException("Trying use a resource that is not a renderer list.");
            m_RenderPass.usedRendererListList.Add(resource);
            return resource;
        }

        public void SetRenderFunc(RenderFunc renderFunc)
        {
            m_RenderPass.renderFunc = renderFunc;
        }

        public void EnableAsyncCompute(bool value)
        {
            m_RenderPass.enableAsyncCompute = value;
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion

        #region Internal Interface
        internal RenderGraphBuilder(RenderGraph renderGraph, RenderGraphResourceRegistry resources, RenderGraph.RenderPassDescriptor renderPass)
        {
            m_RenderPass = renderPass;
            m_Disposed = false;
            m_RenderGraph = renderGraph;
            m_RenderGraphResources = resources;
        }

        void Dispose(bool disposing)
        {
            if (m_Disposed)
                return;

            if (disposing)
            {
                if (m_RenderPass.renderFunc == null)
                {
                    throw new InvalidOperationException("AddRenderPass was not provided with an execute function.");
                }
            }

            m_Disposed = true;
        }
        #endregion
    }
}
