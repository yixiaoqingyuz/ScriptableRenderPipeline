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
        bool                                m_EnableAsyncCompute;
        bool                                m_Disposed;

        #region Public Interface
        public RenderGraphMutableResource CreateTexture( in RenderGraphResourceRegistry.TextureDesc desc)
        {
            return m_RenderGraphResources.CreateTexture(desc);
        }

        public RenderGraphMutableResource WriteTexture(in RenderGraphMutableResource input)
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

        public void EnableAsyncCompute(bool value)
        {
            m_EnableAsyncCompute = value;
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion

        #region Internal Interface
        internal RenderGraphBuilder(RenderGraph renderGraph, RenderGraphResourceRegistry resources, List<RenderGraphResource> resourceReadList, List<RenderGraphMutableResource> resourceWriteList)
        {
            m_RenderFunc = null;
            m_Disposed = false;
            m_RenderGraph = renderGraph;
            m_RenderGraphResources = resources;
            m_ResourceReadList = resourceReadList;
            m_ResourceWriteList = resourceWriteList;
            m_EnableAsyncCompute = false;
        }

        void Dispose(bool disposing)
        {
            if (m_Disposed)
                return;

            if (disposing)
            {
                m_RenderGraph.FinalizeRenderPassBuild(m_RenderFunc, m_ResourceReadList, m_ResourceWriteList, m_EnableAsyncCompute);
            }

            m_Disposed = true;
        }
        #endregion
    }
}
