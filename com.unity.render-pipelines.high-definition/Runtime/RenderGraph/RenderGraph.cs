using System;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Profiling;

namespace UnityEngine.Experimental.Rendering
{
    using RTHandle = RTHandleSystem.RTHandle;

    public class RenderGraph
    {
        struct RenderPassDescriptor
        {
            public string                           passName;
            public RenderFunc                       renderFunc;
            public RenderPassData                   passData;
            public CustomSampler                    customSampler;
            public List<RenderGraphResource>        resourceReadList;
            public List<RenderGraphMutableResource> resourceWriteList;
            public bool                             enableAsyncCompute;
        }

        RenderGraphResourceRegistry                     m_Resources = new RenderGraphResourceRegistry();
        RenderGraphTempPool                             m_TemporaryPool = new RenderGraphTempPool();
        List<RenderPassDescriptor>                      m_RenderPasses = new List<RenderPassDescriptor>();
        Dictionary<Type, Queue<RenderPassData>>         m_RenderPassDataPool = new Dictionary<Type, Queue<RenderPassData>>();
        ObjectPool<List<RenderGraphResource>>           m_ResourceReadListPool = new ObjectPool<List<RenderGraphResource>>(null, null);
        ObjectPool<List<RenderGraphMutableResource>>    m_ResourceWriteListPool = new ObjectPool<List<RenderGraphMutableResource>>(null, null);

        #region Public Interface
        public ref struct RenderGraphContext
        {
            public ScriptableRenderContext     renderContext;
            public CommandBuffer               cmd;
            public RenderGraphTempPool         tempPool;
            public RenderGraphResourceRegistry resources;
        }

        public ref struct RenderGraphGlobalParams
        {
            public Rect renderingViewport;
        }

        public class RenderPassData { }
        public delegate void RenderFunc(RenderPassData data, RenderGraphGlobalParams globalParams, RenderGraphContext renderGraphContext);

        public void Cleanup()
        {
            m_Resources.Cleanup();
        }

        // Global resource management (functions to create or import resources outside of render passes)
        public RenderGraphMutableResource ImportTexture(RTHandle rt)
        {
            return m_Resources.ImportTexture(rt);
        }

        public RenderGraphMutableResource CreateTexture(in RenderGraphResourceRegistry.TextureDesc desc)
        {
            return m_Resources.CreateTexture(desc);
        }

        public RenderGraphBuilder AddRenderPass<PassData>(string passName, out PassData passData, CustomSampler customSampler = null) where PassData : RenderPassData, new()
        {
            // WARNING: Currently pooling won't re-init the data inside PassData... this could lead to problems if users don't fill all the members properly (like having stale resource handles for example)
            passData = AllocatePassData<PassData>();

            m_RenderPasses.Add(new RenderPassDescriptor
            {
                passName = passName,
                renderFunc = null,
                passData = passData,
                customSampler = customSampler
            });

            var resourceReadList = m_ResourceReadListPool.Get();
            resourceReadList.Clear();
            var resourceWriteList = m_ResourceWriteListPool.Get();
            resourceWriteList.Clear();
            return new RenderGraphBuilder(this, m_Resources, resourceReadList, resourceWriteList);
        }

        public void Execute(ScriptableRenderContext renderContext, CommandBuffer cmd, RenderGraphGlobalParams parameters)
        {
            // First pass, traversal and pruning
            for (int passIndex = 0; passIndex < m_RenderPasses.Count; ++passIndex)
            {
                var pass = m_RenderPasses[passIndex];
                foreach (var resourceWrite in pass.resourceWriteList)
                {
                    ref var res = ref m_Resources.GetResource(resourceWrite);
                    res.firstWritePassIndex = Math.Min(passIndex, res.firstWritePassIndex);
                }

                foreach (var resourceRead in pass.resourceReadList)
                {
                    ref var res = ref m_Resources.GetResource(resourceRead);
                    res.lastReadPassIndex = Math.Max(passIndex, res.lastReadPassIndex);
                }
            }

            RenderGraphContext rgContext = new RenderGraphContext();
            rgContext.cmd = cmd;
            rgContext.renderContext = renderContext;
            rgContext.tempPool = m_TemporaryPool;
            rgContext.resources = m_Resources;

            // Second pass, execution
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
            }
        }
        #endregion

        #region Internal Interface
        void PreRenderPassExecute(int passIndex, in RenderPassDescriptor pass, RenderGraphContext rgContext, RenderGraphGlobalParams parameters)
        {
            foreach (var resource in pass.resourceWriteList)
            {
                ref var resourceDesc = ref m_Resources.GetResource(resource);
                if (!resourceDesc.imported && resourceDesc.firstWritePassIndex == passIndex)
                {
                    m_Resources.CreateResourceForPass(resource);

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
            m_TemporaryPool.ReleaseAllTempAlloc();

            foreach (var resource in pass.resourceReadList)
            {
                ref var resourceDesc = ref m_Resources.GetResource(resource);
                if (!resourceDesc.imported && resourceDesc.lastReadPassIndex == passIndex)
                {
                    m_Resources.ReleaseResourceForPass(resource);
                }
            }

            // If a resource was created for only a single pass, we don't want users to have to declare explicitly the read operation.
            // So here we test resources written, if they have the initial lastReadPassIndex value of zero, it means that no subsequent pass will read it so we can release it
            foreach (var resource in pass.resourceWriteList)
            {
                ref var resourceDesc = ref m_Resources.GetResource(resource);
                if (!resourceDesc.imported && resourceDesc.lastReadPassIndex == 0)
                {
                    m_Resources.ReleaseResourceForPass(resource);
                }
            }
        }

        internal void FinalizeRenderPassBuild(RenderFunc renderFunc, List<RenderGraphResource> resourceReadList, List<RenderGraphMutableResource> resourceWriteList, bool enableAsyncCompute)
        {
            if (renderFunc == null)
            {
                Debug.LogError("AddRenderPass was not provided with an execute function.");
                m_RenderPasses.RemoveRange(m_RenderPasses.Count - 1, 1); // remove pass being currently added.
                return;
            }

            var desc = m_RenderPasses[m_RenderPasses.Count - 1];
            desc.renderFunc = renderFunc;
            desc.resourceReadList = resourceReadList;
            desc.resourceWriteList = resourceWriteList;
            desc.enableAsyncCompute = enableAsyncCompute;
            m_RenderPasses[m_RenderPasses.Count - 1] = desc;
        }

        void ClearRenderPasses()
        {
            foreach(var pass in m_RenderPasses)
            {
                m_ResourceReadListPool.Release(pass.resourceReadList);
                m_ResourceWriteListPool.Release(pass.resourceWriteList);
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

