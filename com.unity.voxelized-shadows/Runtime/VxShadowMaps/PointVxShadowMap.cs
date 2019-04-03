
namespace UnityEngine.Experimental.VoxelizedShadows
{
    [ExecuteAlways]
    [AddComponentMenu("Rendering/VxShadows", 110)]
    public sealed class PointVxShadowMap : VxShadowMap
    {
        // TODO :
        public override int voxelResolutionInt => (int)VoxelResolution._4096;
        public override VoxelResolution subtreeResolution => VoxelResolution._4096;

        public override void ValidateResources()
        {
            // todo :
        }
        public override void InvalidateResources()
        {
            // todo :
        }
    }
}
