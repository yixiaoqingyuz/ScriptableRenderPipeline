using System;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

namespace UnityEngine.Experimental.Rendering.RenderGraphModule
{
    using RTHandle = RTHandleSystem.RTHandle;

    public ref struct RenderGraphContext
    {
        public ScriptableRenderContext renderContext;
        public CommandBuffer cmd;
        public RenderGraphObjectPool renderGraphPool;
        public RenderGraphResourceRegistry resources;
    }

    public ref struct RenderGraphGlobalParams
    {
        public Rect renderingViewport;
    }

    public class RenderPassData { }
    public delegate void RenderFunc(RenderPassData data, RenderGraphGlobalParams globalParams, RenderGraphContext renderGraphContext);

    public class RenderGraph
    {
        internal class RenderPassDescriptor
        {
            public string                           passName;
            public RenderFunc                       renderFunc;
            public RenderPassData                   passData;
            public CustomSampler                    customSampler;
            public List<RenderGraphResource>        resourceReadList = new List<RenderGraphResource>();
            public List<RenderGraphMutableResource> resourceWriteList = new List<RenderGraphMutableResource>();
            public List<RenderGraphResource>        usedRendererListList = new List<RenderGraphResource>();
            public bool                             enableAsyncCompute;

            internal void Clear()
            {
                passName = "";
                passData = null;
                renderFunc = null;
                resourceReadList.Clear();
                resourceWriteList.Clear();
                usedRendererListList.Clear();
                enableAsyncCompute = false;
            }
        }

        RenderGraphResourceRegistry             m_Resources = new RenderGraphResourceRegistry();
        RenderGraphObjectPool                   m_RenderGraphPool = new RenderGraphObjectPool();
        List<RenderPassDescriptor>              m_RenderPasses = new List<RenderPassDescriptor>();
        List<RenderGraphResource>               m_RendererLists = new List<RenderGraphResource>();
        Dictionary<Type, Queue<RenderPassData>> m_RenderPassDataPool = new Dictionary<Type, Queue<RenderPassData>>();

        #region Public Interface

        public void Cleanup()
        {
            m_Resources.Cleanup();
        }

        // Global resource management (functions to create or import resources outside of render passes)
        public RenderGraphMutableResource ImportTexture(RTHandle rt)
        {
            return m_Resources.ImportTexture(rt);
        }

        public RenderGraphMutableResource CreateTexture(in TextureDesc desc)
        {
            return m_Resources.CreateTexture(desc);
        }

        public RenderGraphBuilder AddRenderPass<PassData>(string passName, out PassData passData, CustomSampler customSampler = null) where PassData : RenderPassData, new()
        {
            // WARNING: Currently pooling won't re-init the data inside PassData... this could lead to problems if users don't fill all the members properly (like having stale resource handles for example)
            passData = AllocatePassData<PassData>();

            var renderPass = m_RenderGraphPool.Get<RenderPassDescriptor>();
            renderPass.Clear();
            renderPass.passName = passName;
            renderPass.renderFunc = null;
            renderPass.passData = passData;
            renderPass.customSampler = customSampler;

            m_RenderPasses.Add(renderPass);

            return new RenderGraphBuilder(this, m_Resources, renderPass);
        }

        public void Execute(ScriptableRenderContext renderContext, CommandBuffer cmd, RenderGraphGlobalParams parameters)
        {
            // First pass, traversal and pruning
            for (int passIndex = 0; passIndex < m_RenderPasses.Count; ++passIndex)
            {
                var pass = m_RenderPasses[passIndex];
                foreach (var resourceWrite in pass.resourceWriteList)
                {
                    ref var res = ref m_Resources.GetTexturetResource(resourceWrite);
                    res.firstWritePassIndex = Math.Min(passIndex, res.firstWritePassIndex);
                }

                foreach (var resourceRead in pass.resourceReadList)
                {
                    ref var res = ref m_Resources.GetTexturetResource(resourceRead);
                    res.lastReadPassIndex = Math.Max(passIndex, res.lastReadPassIndex);
                }

                // Gather all renderer lists
                m_RendererLists.AddRange(pass.usedRendererListList);
            }

            // Creates all renderer lists
            m_Resources.CreateRendererLists(m_RendererLists);

            // Second pass, execution
            RenderGraphContext rgContext = new RenderGraphContext();
            rgContext.cmd = cmd;
            rgContext.renderContext = renderContext;
            rgContext.renderGraphPool = m_RenderGraphPool;
            rgContext.resources = m_Resources;

            try
            {
                for (int passIndex = 0; passIndex < m_RenderPasses.Count; ++passIndex)
                {
                    var pass = m_RenderPasses[passIndex];
                    using (new ProfilingSample(cmd, pass.passName, pass.customSampler))
                    {
                        PreRenderPassExecute(passIndex, pass, rgContext, parameters);
                        pass.renderFunc(pass.passData, parameters, rgContext);
                        PostRenderPassExecute(passIndex, pass);
                    }
                }
            }
            catch(Exception e)
            {
                Debug.LogError("Render Graph Execution error");
                Debug.LogException(e);
            }
            finally
            {
                ClearRenderPasses();
                m_Resources.Clear();
                m_RendererLists.Clear();
            }
        }
        #endregion

        #region Internal Interface
        void PreRenderPassExecute(int passIndex, in RenderPassDescriptor pass, RenderGraphContext rgContext, RenderGraphGlobalParams parameters)
        {
            foreach (var resource in pass.resourceWriteList)
            {
                ref var resourceDesc = ref m_Resources.GetTexturetResource(resource);
                if (!resourceDesc.imported && resourceDesc.firstWritePassIndex == passIndex)
                {
                    m_Resources.CreateTextureForPass(resource);

                    if (resourceDesc.desc.clearBuffer)
                    {
                        //using (new ProfilingSample(rgContext.cmd, string.Format("RenderGraph: Clear Buffer {0}", resourceDesc.desc.name)))
                        using (new ProfilingSample(rgContext.cmd, "RenderGraph: Clear Buffer"))
                        {
                            var clearFlag = resourceDesc.desc.depthBufferBits != DepthBits.None ? ClearFlag.Depth : ClearFlag.Color;
                            HDPipeline.HDUtils.SetRenderTarget(rgContext.cmd, parameters.renderingViewport, m_Resources.GetTexture(resource), clearFlag, resourceDesc.desc.clearColor);
                        }
                    }
                }
            }
        }

        void PostRenderPassExecute(int passIndex, in RenderPassDescriptor pass)
        {
            ReleasePassData(pass.passData);
            m_RenderGraphPool.ReleaseAllTempAlloc();

            foreach (var resource in pass.resourceReadList)
            {
                ref var resourceDesc = ref m_Resources.GetTexturetResource(resource);
                if (!resourceDesc.imported && resourceDesc.lastReadPassIndex == passIndex)
                {
                    m_Resources.ReleaseTextureForPass(resource);
                }
            }

            // If a resource was created for only a single pass, we don't want users to have to declare explicitly the read operation.
            // So here we test resources written, if they have the initial lastReadPassIndex value of zero, it means that no subsequent pass will read it so we can release it
            foreach (var resource in pass.resourceWriteList)
            {
                ref var resourceDesc = ref m_Resources.GetTexturetResource(resource);
                if (!resourceDesc.imported && resourceDesc.lastReadPassIndex == 0)
                {
                    m_Resources.ReleaseTextureForPass(resource);
                }
            }
        }

        void ClearRenderPasses()
        {
            foreach(var pass in m_RenderPasses)
            {
                m_RenderGraphPool.Release(pass);
            }
            m_RenderPasses.Clear();
        }

        PassData AllocatePassData<PassData>() where PassData : RenderPassData, new()
        {
            Type t = typeof(PassData);
            Queue<RenderPassData> pool = null;
            if (!m_RenderPassDataPool.TryGetValue(t, out pool))
            {
                return new PassData();
            }
            else
            {
                return (pool.Count != 0) ? (PassData)pool.Dequeue() : new PassData();
            }
        }

        void ReleasePassData(RenderPassData data)
        {
            Type t = data.GetType();
            if (!m_RenderPassDataPool.TryGetValue(t, out Queue<RenderPassData> pool))
            {
                pool = new Queue<RenderPassData>();
                m_RenderPassDataPool.Add(t, pool);
            }

            pool.Enqueue(data);
        }


        #endregion
    }
}

