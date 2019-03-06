using System;
using System.Collections.Generic;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    /// <summary>Utility to build frame passes.</summary>
    public class FramePassBuilder : IDisposable
    {
        // Owned
        private List<FramePassData> m_FramePassData;

        /// <summary>Add a frame pass.</summary>
        /// <param name="settings">Settings to use for this frame pass.</param>
        /// <param name="bufferAllocator">An allocator for each buffer.</param>
        /// <param name="includedLightList">If non null, only these lights will be rendered, if none, all lights will be rendered.</param>
        /// <param name="buffers">A list of buffers to use.</param>
        /// <param name="callback">A callback that can use the requested buffers once the rendering has completed.</param>
        /// <returns></returns>
        public FramePassBuilder Add(
            FramePassSettings settings,
            FramePassBufferAllocator bufferAllocator,
            List<GameObject> includedLightList,
            Buffers[] buffers,
            FramePassCallback callback
        )
        {
            (m_FramePassData ?? (m_FramePassData = ListPool<FramePassData>.Get())).Add(
                new FramePassData(settings, bufferAllocator, includedLightList, buffers, callback));
            return this;
        }

        /// <summary>Build the frame passes. Allocated resources will be transferred to the returned value.</summary>
        public FramePassDataCollection Build()
        {
            var result = new FramePassDataCollection(m_FramePassData);
            m_FramePassData = null;
            return result;
        }

        /// <summary>
        /// Dispose the builder.
        ///
        /// This is required when you don't call <see cref="Build"/>.
        /// </summary>
        public void Dispose()
        {
            if (m_FramePassData == null) return;
            ListPool<FramePassData>.Release(m_FramePassData);
            m_FramePassData = null;
        }
    }
}
