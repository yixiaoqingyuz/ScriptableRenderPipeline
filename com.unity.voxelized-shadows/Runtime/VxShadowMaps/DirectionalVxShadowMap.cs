
namespace UnityEngine.Experimental.VoxelizedShadows
{
    [ExecuteAlways]
    [AddComponentMenu("Rendering/VxShadowMaps/DirectionalVxShadowMap", 100)]
    public sealed class DirectionalVxShadowMap : VxShadowMap
    {
        public DirectionalVxShadowMapResources resource;

        public float volumeScale = 10.0f;
        public VoxelResolution voxelResolution = VoxelResolution._4096;
        public override int voxelResolutionInt => (int)voxelResolution;
        public override VoxelResolution subtreeResolution =>
            voxelResolutionInt < MaxSubtreeResolutionInt ? voxelResolution : MaxSubtreeResolution;

        public int voxelZBias = 2;
        public int voxelUpBias = 1;
        public ShadowsBlendMode shadowsBlendMode = ShadowsBlendMode.OnlyVxShadowMaps;

#if UNITY_EDITOR
        public float size = 0.0f;
#endif

        const int k_MaxCascades = 4;

        [HideInInspector] public int cascadesCount;
        // todo : allocate here not MainLightShadowCasterPass.cs
        [HideInInspector] public Matrix4x4[] cascadesMatrices = new Matrix4x4[k_MaxCascades + 1];
        [HideInInspector] public Vector4[] cascadeSplitDistances = new Vector4[k_MaxCascades];

        [HideInInspector] public int maxScale;
        [HideInInspector] public Matrix4x4 worldToShadowMatrix = Matrix4x4.identity;
        [HideInInspector] public ComputeBuffer computeBuffer;

        private void OnEnable()
        {
            Debug.Log("Register");
            VxShadowMapsManager.instance.RegisterVxShadowMapComponent(this);
        }
        private void OnDisable()
        {
            Debug.Log("Unregister");
            VxShadowMapsManager.instance.UnregisterVxShadowMapComponent(this);
        }
        private void OnValidate()
        {
            ValidateResources();
        }

        public bool IsValid()
        {
            return enabled && resource != null && computeBuffer != null;
        }

#if UNITY_EDITOR
        public void OnDrawGizmos()
        {
            var light = GetComponent<Light>();

            if (light != null)
            {
                Gizmos.color = new Color(1.0f, 0.0f, 0.0f, 0.3f);
                Gizmos.matrix = light.transform.localToWorldMatrix;
                Gizmos.DrawCube(Vector3.zero, new Vector3(volumeScale, volumeScale, volumeScale));
            }
        }
#endif

        public override void ValidateResources()
        {
            bool needToReload = resource != null && computeBuffer == null;

#if UNITY_EDITOR
            if (resource != null)
            {
                float currSize = (float)(resource.Data.Length * 4);
                currSize /= 1024.0f;
                currSize /= 1024.0f;

                if (size != currSize)
                    needToReload = true;

                size = currSize;
            }
#endif

            if (needToReload)
            {
                transform.position = resource.Position;
                transform.rotation = resource.Rotation;

                volumeScale = resource.VolumeScale;
                voxelResolution = (VoxelResolution)resource.VoxelResolution;
                maxScale = resource.MaxScale;
                worldToShadowMatrix = resource.WorldToShadowMatrix;

                SafeRelease(computeBuffer);

                int count = resource.Data.Length;
                int stride = 4;

                computeBuffer = new ComputeBuffer(count, stride);
                computeBuffer.SetData(resource.Data);
            }
        }
        public override void InvalidateResources()
        {
            SafeRelease(computeBuffer);
            computeBuffer = null;
            resource = null;
        }

        private void SafeRelease(ComputeBuffer buffer)
        {
            if (buffer != null)
                buffer.Release();
        }
    }
}
