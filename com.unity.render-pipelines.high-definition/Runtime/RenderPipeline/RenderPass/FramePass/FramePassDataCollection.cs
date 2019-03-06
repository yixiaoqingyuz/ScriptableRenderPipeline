using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    /// <summary>A collection of frame passes. To build one, <see cref="FramePassBuilder"/></summary>
    public class FramePassDataCollection : IEnumerable<FramePassData>, IDisposable
    {
        // Owned
        private List<FramePassData> m_FramePassData;

        internal FramePassDataCollection(List<FramePassData> framePassData)
            // Transfer ownership of the list
            => m_FramePassData = framePassData;

        /// <summary>Enumerate the frame passes.</summary>
        public IEnumerator<FramePassData> GetEnumerator() =>
            (m_FramePassData ?? Enumerable.Empty<FramePassData>()).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            if (m_FramePassData == null) return;

            ListPool<FramePassData>.Release(m_FramePassData);
            m_FramePassData = null;
        }
    }
}
