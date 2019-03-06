using System;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    public enum TonemappingMode
    {
        None,
        Neutral, // Neutral tonemapper
        ACES,    // ACES Filmic reference tonemapper (custom approximation)
        Custom,  // Tweakable artist-friendly curve
        External // External LUT
    }

    [Serializable, VolumeComponentMenu("Post-processing/Tonemapping")]
    public sealed class Tonemapping : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Select a tonemapping algorithm to use for the color grading process.")]
        public TonemappingModeParameter mode = new TonemappingModeParameter(TonemappingMode.None);

        [Tooltip("Affects the transition between the toe and the mid section of the curve. A value of 0 means no toe, a value of 1 means a very hard transition.")]
        public ClampedFloatParameter toeStrength = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip("Affects how much of the dynamic range is in the toe. With a small value, the toe will be very short and quickly transition into the linear section, and with a longer value having a longer toe.")]
        public ClampedFloatParameter toeLength = new ClampedFloatParameter(0.5f, 0f, 1f);

        [Tooltip("Affects the transition between the mid section and the shoulder of the curve. A value of 0 means no shoulder, a value of 1 means a very hard transition.")]
        public ClampedFloatParameter shoulderStrength = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip("Affects how many F-stops (EV) to add to the dynamic range of the curve.")]
        public MinFloatParameter shoulderLength = new MinFloatParameter(0.5f, 0f);

        [Tooltip("Affects how much overshoot to add to the shoulder.")]
        public ClampedFloatParameter shoulderAngle = new ClampedFloatParameter(0f, 0f, 1f);

        [Tooltip("Applies a gamma correction to the whole curve.")]
        public MinFloatParameter gamma = new MinFloatParameter(1f, 0.001f);

        [Tooltip("A custom 3D texture lookup table to apply.")]
        public TextureParameter lutTexture = new TextureParameter(null);

        [Tooltip("How much of the lookup texture will contribute to the color grading effect.")]
        public ClampedFloatParameter lutContribution = new ClampedFloatParameter(1f, 0f, 1f);

        public bool IsActive()
        {
            return mode.value != TonemappingMode.None
                && (mode.value == TonemappingMode.External && ValidateLUT() && lutContribution.value > 0f);
        }

        public bool ValidateLUT()
        {
            var hdAsset = GraphicsSettings.renderPipelineAsset as HDRenderPipelineAsset;
            if (hdAsset == null || lutTexture.value == null)
                return false;

            if (lutTexture.value.width != hdAsset.currentPlatformRenderPipelineSettings.postProcessSettings.lutSize)
                return false;

            bool valid = false;

            switch (lutTexture.value)
            {
                case Texture3D t:
                    valid |= t.width == t.height
                        && t.height == t.depth;
                    break;
                case RenderTexture rt:
                    valid |= rt.dimension == TextureDimension.Tex3D
                        && rt.width == rt.height
                        && rt.height == rt.volumeDepth;
                    break;
            }

            return valid;
        }
    }

    [Serializable]
    public sealed class TonemappingModeParameter : VolumeParameter<TonemappingMode> { public TonemappingModeParameter(TonemappingMode value, bool overrideState = false) : base(value, overrideState) { } }
}
