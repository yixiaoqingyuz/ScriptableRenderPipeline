using System;
using System.Collections.Generic;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    /// <summary>Called when the rendering has completed.</summary>
    /// <param name="cmd">A command buffer that can be used.</param>
    /// <param name="buffers">The buffers that has been requested.</param>
    /// <param name="properties">Several properties that were computed for this frame.</param>
    public delegate void FramePassCallback(CommandBuffer cmd, List<RTHandleSystem.RTHandle> buffers, FrameProperties properties);
    public delegate RTHandleSystem.RTHandle FramePassBufferAllocator(Buffers bufferId);

    /// <summary>Describes a frame pass.</summary>
    public struct FramePassData
    {
        /// <summary>Default frame pass settings.</summary>
        public static readonly FramePassData @default = new FramePassData
        {
            m_Settings = FramePassSettings.@default,
            m_RequestedBuffers = new Buffers[] {},
            m_Callback = null
        };

        private FramePassSettings m_Settings;
        private Buffers[] m_RequestedBuffers;
        private FramePassCallback m_Callback;
        private readonly FramePassBufferAllocator m_BufferAllocator;
        private List<GameObject> m_LightFilter;

        /// <summary>Whether this frame pass is valid.</summary>
        public bool isValid => m_RequestedBuffers != null && m_Callback != null;

        /// <summary>Create a new frame pass.</summary>
        /// <param name="settings">Settings to use.</param>
        /// <param name="bufferAllocator">Buffer allocators to use.</param>
        /// <param name="lightFilter">If null, all light will be rendered, if not, only those light will be rendered.</param>
        /// <param name="requestedBuffers">The requested buffers for the callback.</param>
        /// <param name="callback">The callback to execute.</param>
        public FramePassData(
            FramePassSettings settings,
            FramePassBufferAllocator bufferAllocator,
            List<GameObject> lightFilter,
            Buffers[] requestedBuffers,
            FramePassCallback callback
        )
        {
            m_Settings = settings;
            m_BufferAllocator = bufferAllocator;
            m_RequestedBuffers = requestedBuffers;
            m_LightFilter = lightFilter;
            m_Callback = callback;
        }

        /// <summary>Allocate texture if required.</summary>
        /// <param name="textures">A buffer of texture ready to use.</param>
        public void AllocateTargetTexturesIfRequired(ref List<RTHandleSystem.RTHandle> textures)
        {
            if (!isValid || textures == null)
                return;

            Assert.IsNotNull(m_RequestedBuffers);

            textures.Clear();

            foreach (var bufferId in m_RequestedBuffers)
                textures.Add(m_BufferAllocator(bufferId));
        }

        /// <summary>Copy a camera sized texture into the texture buffers.</summary>
        /// <param name="cmd">the command buffer to use for the copy.</param>
        /// <param name="bufferId">The id of the buffer to copy.</param>
        /// <param name="camera">The camera associated with the source texture.</param>
        /// <param name="source">The source texture to copy</param>
        /// <param name="targets">The target texture buffer.</param>
        public void PushCameraTexture(
            CommandBuffer cmd,
            Buffers bufferId,
            HDCamera camera,
            RTHandleSystem.RTHandle source,
            List<RTHandleSystem.RTHandle> targets
        )
        {
            if (!isValid)
                return;

            Assert.IsNotNull(m_RequestedBuffers);
            Assert.IsNotNull(targets);

            var index = Array.IndexOf(m_RequestedBuffers, bufferId);
            if (index == -1)
                return;

            HDUtils.BlitCameraTexture(cmd, camera, source, targets[index]);
        }

        /// <summary>Execute the frame pass callback. It assumes that the textures are properly initialized and filled.</summary>
        /// <param name="cmd">The command buffer to use.</param>
        /// <param name="framePassTextures">The textures to use.</param>
        /// <param name="properties">The properties computed for this frame.</param>
        public void Execute(CommandBuffer cmd, List<RTHandleSystem.RTHandle> framePassTextures, FrameProperties properties)
        {
            if (!isValid)
                return;

            m_Callback(cmd, framePassTextures, properties);
        }

        /// <summary>Setup the display manager if necessary.</summary>
        /// <param name="debugDisplaySettings"></param>
        public void SetupDebugData(ref DebugDisplaySettings debugDisplaySettings)
        {
            if (!isValid)
                return;

            debugDisplaySettings = new DebugDisplaySettings();
            m_Settings.FillDebugData(debugDisplaySettings);
        }

        /// <summary>Whether a light should be rendered.</summary>
        /// <param name="gameObject">The game object of the light to be rendered.</param>
        /// <returns><c>true</c> when the light must be rendered, <c>false</c> when it should be ignored.</returns>
        public bool IsLightEnabled(GameObject gameObject) => m_LightFilter == null || m_LightFilter.Contains(gameObject);
    }
}
