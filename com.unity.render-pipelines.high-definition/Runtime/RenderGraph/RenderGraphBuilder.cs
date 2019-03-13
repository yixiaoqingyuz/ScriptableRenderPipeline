using System;
using System.Collections.Generic;

namespace UnityEngine.Experimental.Rendering
{
    public struct RenderGraphBuilder : IDisposable
    {

        RenderGraph                         m_RenderGraph;
        RenderGraphResourceRegistry         m_RenderGraphResources;
        RenderGraph.RenderFunc              m_RenderFunc;
        List<RenderGraphResource>           m_ResourceReadList;
        List<RenderGraphMutableResource>    m_ResourceWriteList;
        bool                                m_Disposed;

        internal RenderGraphBuilder(RenderGraph renderGraph, RenderGraphResourceRegistry resources, List<RenderGraphResource> resourceReadList, List<RenderGraphMutableResource> resourceWriteList)
        {
            m_RenderFunc = null;
            m_Disposed = false;
            m_RenderGraph = renderGraph;
            m_RenderGraphResources = resources;
            m_ResourceReadList = resourceReadList;
            m_ResourceWriteList = resourceWriteList;
        }

        public RenderGraphMutableResource WriteTexture(RenderGraphMutableResource input)
        {
            // TODO: Manage resource "version" for debugging purpose
            m_ResourceWriteList.Add(input);
            return input;
        }

        public RenderGraphResource ReadTexture(RenderGraphResource input)
        {
            m_ResourceReadList.Add(input);
            return input;
        }

        public void SetRenderFunc(RenderGraph.RenderFunc renderFunc)
        {
            m_RenderFunc = renderFunc;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        // Protected implementation of Dispose pattern.
        void Dispose(bool disposing)
        {
            if (m_Disposed)
                return;

            if (disposing)
            {
                m_RenderGraph.FinalizeRenderPass(m_RenderFunc, m_ResourceReadList, m_ResourceWriteList);
            }

            m_Disposed = true;
        }
    }
}
