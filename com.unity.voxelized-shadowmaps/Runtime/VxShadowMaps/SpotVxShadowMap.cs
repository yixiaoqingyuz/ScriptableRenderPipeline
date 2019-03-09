
namespace UnityEngine.Experimental.VoxelizedShadowMaps
{
    public sealed class SpotVxShadowMap : VxShadowMap
    {
        // TODO :
        public override int voxelResolutionInt => (int)VoxelResolution._4096;
        public override VoxelResolution subtreeResolution => VoxelResolution._4096;
    }
}
