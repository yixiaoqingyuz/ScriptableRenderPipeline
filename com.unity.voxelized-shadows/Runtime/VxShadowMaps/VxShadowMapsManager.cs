using System.Collections.Generic;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.VoxelizedShadows
{
    public enum RenderPipelineType
    {
        Lightweight,
        HighDefinition,
        Unknown,
    }

    public class VxShadowMapsManager
    {
        private RenderPipelineType _renderPipelineType = RenderPipelineType.Unknown;

        private ComputeBuffer _nullVxShadowMapsBuffer = null;

        private List<DirectionalVxShadowMap> _dirVxShadowMapList = new List<DirectionalVxShadowMap>();
        private List<PointVxShadowMap> _pointVxShadowMapList = new List<PointVxShadowMap>();
        private List<SpotVxShadowMap> _spotVxShadowMapList = new List<SpotVxShadowMap>();

        private static VxShadowMapsManager _instance = null;
        public static VxShadowMapsManager instance
        {
            get
            {
                if (_instance == null)
                    _instance = new VxShadowMapsManager();

                return _instance;
            }
        }

        public VxShadowMapsManager()
        {
            if (GraphicsSettings.renderPipelineAsset.name == "LightweightRenderPipelineAsset")
                _renderPipelineType = RenderPipelineType.Lightweight;
            else if (GraphicsSettings.renderPipelineAsset.name == "HDRenderPipelineAsset")
                _renderPipelineType = RenderPipelineType.HighDefinition;
            else
                _renderPipelineType = RenderPipelineType.Unknown;
        }

        private void InstantiateNullVxShadowMapsBuffer()
        {
            uint[] nullData = new uint[]
            {
                // resolution, maxScale
                0, 0,
                // matrix
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
                // data
                0,
            };

            _nullVxShadowMapsBuffer = new ComputeBuffer(19, 4);
            _nullVxShadowMapsBuffer.SetData(nullData);
        }

        public void RegisterVxShadowMapComponent(DirectionalVxShadowMap dirVxsm)
        {
#if UNITY_EDITOR
            if (_renderPipelineType == RenderPipelineType.Unknown)
            {
                Debug.LogWarning("Try to register VxShadowMap on 'unknown RenderPipeline', it may not work.");
            }
#endif
            _dirVxShadowMapList.Add(dirVxsm);
            dirVxsm.ValidateResources();
        }
        public void RegisterVxShadowMapComponent(PointVxShadowMap pointVxsm)
        {
#if UNITY_EDITOR
            if (_renderPipelineType == RenderPipelineType.Unknown)
            {
                Debug.LogWarning("Try to register VxShadowMap on 'unknown RenderPipeline', it may not work.");
            }
            else if (_renderPipelineType == RenderPipelineType.Lightweight)
            {
                Debug.LogWarning("VxShadowMap of PointLight is not supported on 'Lightweight RenderPipeline'.");
                return;
            }
#endif

            _pointVxShadowMapList.Add(pointVxsm);
            pointVxsm.ValidateResources();
        }
        public void RegisterVxShadowMapComponent(SpotVxShadowMap spotVxsm)
        {
#if UNITY_EDITOR
            if (_renderPipelineType == RenderPipelineType.Unknown)
            {
                Debug.LogWarning("Try to register VxShadowMap on 'unknown RenderPipeline', it may not work.");
            }
            else if (_renderPipelineType == RenderPipelineType.Lightweight)
            {
                Debug.LogWarning("VxShadowMap of SpotLight is not supported on 'Lightweight RenderPipeline'.");
                return;
            }
#endif

            _spotVxShadowMapList.Add(spotVxsm);
            spotVxsm.ValidateResources();
        }
        public void UnregisterVxShadowMapComponent(DirectionalVxShadowMap dirVxsm)
        {
            if (_dirVxShadowMapList.Contains(dirVxsm))
            {
                _dirVxShadowMapList.Remove(dirVxsm);
                dirVxsm.InvalidateResources();
            }
        }
        public void UnregisterVxShadowMapComponent(PointVxShadowMap pointVxsm)
        {
            if (_pointVxShadowMapList.Contains(pointVxsm))
            {
                _pointVxShadowMapList.Remove(pointVxsm);
                pointVxsm.InvalidateResources();
            }
        }
        public void UnregisterVxShadowMapComponent(SpotVxShadowMap spotVxsm)
        {
            if (_spotVxShadowMapList.Contains(spotVxsm))
            {
                _spotVxShadowMapList.Remove(spotVxsm);
                spotVxsm.InvalidateResources();
            }
        }

        public void Build()
        {
            var dirVxShadowMaps   = Object.FindObjectsOfType<DirectionalVxShadowMap>();
            var pointVxShadowMaps = Object.FindObjectsOfType<PointVxShadowMap>();
            var spotVxShadowMaps  = Object.FindObjectsOfType<SpotVxShadowMap>();

            foreach (var vxsm in dirVxShadowMaps)
            {
                if (vxsm.enabled)
                    _dirVxShadowMapList.Add(vxsm);
            }
            foreach (var vxsm in pointVxShadowMaps)
            {
                if (vxsm.enabled)
                    _pointVxShadowMapList.Add(vxsm);
            }
            foreach (var vxsm in spotVxShadowMaps)
            {
                if (vxsm.enabled)
                    _spotVxShadowMapList.Add(vxsm);
            }
        }
        public void Cleanup()
        {
            if (_nullVxShadowMapsBuffer != null)
            {
                _nullVxShadowMapsBuffer.Release();
                _nullVxShadowMapsBuffer = null;
            }

            foreach (var vxsm in _dirVxShadowMapList)
                vxsm.InvalidateResources();
            foreach (var vxsm in _pointVxShadowMapList)
                vxsm.InvalidateResources();
            foreach (var vxsm in _spotVxShadowMapList)
                vxsm.InvalidateResources();

            _dirVxShadowMapList.Clear();
            _pointVxShadowMapList.Clear();
            _spotVxShadowMapList.Clear();
        }

        public ComputeBuffer NullVxShadowMapsBuffer
        {
            get
            {
                if (_nullVxShadowMapsBuffer == null)
                    InstantiateNullVxShadowMapsBuffer();

                return _nullVxShadowMapsBuffer;
            }
        }

        public ComputeBuffer VxShadowMapsBuffer
        {
            get
            {
                ComputeBuffer computeBuffer = null;

                // todo : merge all VxShadowMaps into one compute buffer later
                if (_dirVxShadowMapList.Count > 0)
                    computeBuffer = _dirVxShadowMapList[0].computeBuffer;

                if (computeBuffer == null)
                    computeBuffer = NullVxShadowMapsBuffer;

                return computeBuffer;
            }
        }
    }
}
