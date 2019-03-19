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

        public class RenderPassData { }
        public delegate void RenderFunc(RenderPassData data, RenderGraphResourceRegistry resources, RenderGraphTempPool tempPool, CommandBuffer cmd, ScriptableRenderContext renderContext);

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

        public void Execute(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            // First pass, traversal and pruning
            for (int passIndex = 0; passIndex < m_RenderPasses.Count; ++passIndex)
            {
                var pass = m_RenderPasses[passIndex];
                foreach (var resourceWrite in pass.resourceWriteList)
                {
                    m_Resources.UpdateResourceFirstWrite(resourceWrite, passIndex);
                }

                foreach (var resourceRead in pass.resourceReadList)
                {
                    m_Resources.UpdateResourceLastRead(resourceRead, passIndex);
                }
            }

            // Second pass, execution
            try
            {
                for (int passIndex = 0; passIndex < m_RenderPasses.Count; ++passIndex)
                {
                    var pass = m_RenderPasses[passIndex];
                    using (new ProfilingSample(cmd, pass.passName, pass.customSampler))
                    {
                        PreRenderPassExecute(passIndex, pass);
                        pass.renderFunc(pass.passData, m_Resources, m_TemporaryPool, cmd, renderContext);
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
        void PreRenderPassExecute(int passIndex, in RenderPassDescriptor pass)
        {
            m_Resources.CreateResourcesForPass(passIndex, pass.resourceWriteList);
        }

        void PostRenderPassExecute(int passIndex, in RenderPassDescriptor pass)
        {
            ReleasePassData(pass.passData);
            m_TemporaryPool.ReleaseAllTempAlloc();
            m_Resources.ReleaseResourcesForPass(passIndex, pass.resourceReadList, pass.resourceWriteList);
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

