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
        internal class RenderPass
        {
            public string                           passName;
            public RenderFunc                       renderFunc;
            public RenderPassData                   passData;
            public CustomSampler                    customSampler;
            public List<RenderGraphResource>        resourceReadList = new List<RenderGraphResource>();
            public List<RenderGraphMutableResource> resourceWriteList = new List<RenderGraphMutableResource>();
            public List<RenderGraphResource>        usedRendererListList = new List<RenderGraphResource>();
            public bool                             enableAsyncCompute;
            public RenderGraphMutableResource       depthBuffer { get { return m_DepthBuffer; } }
            public RenderGraphMutableResource[]     colorBuffers { get { return m_ColorBuffers; } }
            public int                              colorBufferMaxIndex { get { return m_MaxColorBufferIndex; } }

            RenderGraphMutableResource[]            m_ColorBuffers = new RenderGraphMutableResource[RenderGraphUtils.kMaxMRTCount];
            RenderGraphMutableResource              m_DepthBuffer;
            int                                     m_MaxColorBufferIndex = -1;

            internal void Clear()
            {
                passName = "";
                renderFunc = null;
                passData = null;
                customSampler = null;
                resourceReadList.Clear();
                resourceWriteList.Clear();
                usedRendererListList.Clear();
                enableAsyncCompute = false;

                // Invalidate everything
                m_MaxColorBufferIndex = -1;
                m_DepthBuffer = new RenderGraphMutableResource();
                for (int i = 0; i < RenderGraphUtils.kMaxMRTCount; ++i)
                {
                    m_ColorBuffers[i] = new RenderGraphMutableResource();
                }
            }

            internal void SetColorBuffer(in RenderGraphMutableResource resource, int index)
            {
                Debug.Assert(index < RenderGraphUtils.kMaxMRTCount && index >= 0);
                m_MaxColorBufferIndex = Math.Max(m_MaxColorBufferIndex, index);
                m_ColorBuffers[index] = resource;
                resourceWriteList.Add(resource);
            }

            internal void SetDepthBuffer(in RenderGraphMutableResource resource)
            {
                m_DepthBuffer = resource;
                resourceWriteList.Add(resource);
            }
        }

        RenderGraphResourceRegistry             m_Resources = new RenderGraphResourceRegistry();
        RenderGraphObjectPool                   m_RenderGraphPool = new RenderGraphObjectPool();
        List<RenderPass>                        m_RenderPasses = new List<RenderPass>();
        List<RenderGraphResource>               m_RendererLists = new List<RenderGraphResource>();
        Dictionary<Type, Queue<RenderPassData>> m_RenderPassDataPool = new Dictionary<Type, Queue<RenderPassData>>();

        #region Public Interface

        public void Cleanup()
        {
            m_Resources.Cleanup();
        }

        // Global resource management (functions to create or import resources outside of render passes)
        public RenderGraphMutableResource ImportTexture(RTHandle rt, int shaderProperty = 0)
        {
            return m_Resources.ImportTexture(rt, shaderProperty);
        }

        public RenderGraphMutableResource CreateTexture(in TextureDesc desc, int shaderProperty = 0)
        {
            return m_Resources.CreateTexture(desc, shaderProperty);
        }

        public RenderGraphBuilder AddRenderPass<PassData>(string passName, out PassData passData, CustomSampler customSampler = null) where PassData : RenderPassData, new()
        {
            // WARNING: Currently pooling won't re-init the data inside PassData... this could lead to problems if users don't fill all the members properly (like having stale resource handles for example)
            passData = AllocatePassData<PassData>();

            var renderPass = m_RenderGraphPool.Get<RenderPass>();
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

                    // We increment lastRead index here so that a resource used only for a single pass can be released at the end of said pass.
                    // This will also keep the resource alive as long as it is written to.
                    // Typical example is a depth buffer that may never be explicitly read from but is necessary all along
                    res.lastReadPassIndex = Math.Max(passIndex, res.lastReadPassIndex);
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

        void PreRenderPassCreateAndClearRenderTargets(int passIndex, in RenderPass pass, RenderGraphContext rgContext, RenderGraphGlobalParams parameters)
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

        void PreRenderPassSetRenderTargets(in RenderPass pass, RenderGraphContext rgContext, RenderGraphGlobalParams parameters)
        {
            if (pass.depthBuffer.IsValid() || pass.colorBufferMaxIndex != -1)
            {
                var mrtArray = RenderGraphUtils.GetMRTArray(pass.colorBufferMaxIndex + 1);
                var colorBuffers = pass.colorBuffers;

                if (pass.colorBufferMaxIndex > 0)
                {
                    for (int i = 0; i <= pass.colorBufferMaxIndex; ++i)
                    {
                        if (!colorBuffers[i].IsValid())
                            throw new InvalidOperationException("MRT setup is invalid. Some indices are not used.");
                        mrtArray[i] = m_Resources.GetTexture(colorBuffers[i]);
                    }

                    if (pass.depthBuffer.IsValid())
                    {
                        HDPipeline.HDUtils.SetRenderTarget(rgContext.cmd, parameters.renderingViewport, mrtArray, m_Resources.GetTexture(pass.depthBuffer));
                    }
                    else
                    {
                        throw new InvalidOperationException("Setting MRTs without a depth buffer is not supported.");
                    }
                }
                else
                {
                    if (pass.depthBuffer.IsValid())
                    {
                        HDPipeline.HDUtils.SetRenderTarget(rgContext.cmd, parameters.renderingViewport, m_Resources.GetTexture(pass.colorBuffers[0]), m_Resources.GetTexture(pass.depthBuffer));
                    }
                    else
                    {
                        HDPipeline.HDUtils.SetRenderTarget(rgContext.cmd, parameters.renderingViewport, m_Resources.GetTexture(pass.colorBuffers[0]));
                    }

                }
            }
        }

        void PreRenderPassSetGlobalTextures(in RenderPass pass, RenderGraphContext rgContext)
        {
            foreach (var resource in pass.resourceReadList)
            {
                var resourceDesc = m_Resources.GetTexturetResource(resource);
                if (resourceDesc.shaderProperty != 0)
                {
                    rgContext.cmd.SetGlobalTexture(resourceDesc.shaderProperty, resourceDesc.rt);
                }
            }
        }

        #region Internal Interface
        void PreRenderPassExecute(int passIndex, in RenderPass pass, RenderGraphContext rgContext, RenderGraphGlobalParams parameters)
        {
            // TODO merge clear and setup here if possible
            PreRenderPassCreateAndClearRenderTargets(passIndex, pass, rgContext, parameters);
            PreRenderPassSetRenderTargets(pass, rgContext, parameters);
            PreRenderPassSetGlobalTextures(pass, rgContext);
        }

        void PostRenderPassExecute(int passIndex, in RenderPass pass)
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
            // So to do that, we also update lastReadIndex on resource writes.
            // This means that we need to check written resources for destruction too
            foreach (var resource in pass.resourceWriteList)
            {
                ref var resourceDesc = ref m_Resources.GetTexturetResource(resource);
                if (!resourceDesc.imported && resourceDesc.lastReadPassIndex == passIndex)
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

