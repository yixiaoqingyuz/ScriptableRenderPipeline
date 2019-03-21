using UnityEditor.AnimatedValues;
using UnityEngine;

namespace UnityEditor.Rendering.LookDev
{
    [System.Serializable]
    public class LookDevCameraState
    {
        private static readonly Quaternion kDefaultRotation = Quaternion.LookRotation(new Vector3(0.0f, 0.0f, 1.0f));
        private const float kDefaultViewSize = 10f;
        private static readonly Vector3 kDefaultPivot = Vector3.zero;
        private const float kDefaultFoV = 90f;
        private static readonly float distanceCoef = 1f / Mathf.Tan(kDefaultFoV * 0.5f * Mathf.Deg2Rad);

        //Note: we need animation to do the same focus as in SceneView
        [SerializeField] private AnimVector3 m_Pivot = new AnimVector3(kDefaultPivot);
        [SerializeField] private AnimQuaternion m_Rotation = new AnimQuaternion(kDefaultRotation);
        [SerializeField] private AnimFloat m_ViewSize = new AnimFloat(kDefaultViewSize);

        public AnimVector3 pivot { get { return m_Pivot; } set { m_Pivot = value; } }
        public AnimQuaternion rotation { get { return m_Rotation; } set { m_Rotation = value; } }
        public AnimFloat viewSize { get { return m_ViewSize; } set { m_ViewSize = value; } }

        public float cameraDistance => m_ViewSize.value * distanceCoef;

        public void UpdateCamera(Camera camera)
        {
            camera.transform.rotation = m_Rotation.value;
            camera.transform.position = m_Pivot.value + camera.transform.rotation * new Vector3(0, 0, -cameraDistance);

            float farClip = Mathf.Max(1000f, 2000f * m_ViewSize.value);
            camera.nearClipPlane = farClip * 0.000005f;
            camera.farClipPlane = farClip;
        }
    }
}
