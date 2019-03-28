using System;
using System.Collections.Generic;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UnityEditor.Rendering.LookDev
{
    //TODO: add undo support
    internal class Stage : IDisposable
    {
        const int k_PreviewCullingLayerIndex = 31; //Camera.PreviewCullingLayer; //TODO: expose or reflection

        private readonly Scene m_PreviewScene;

        // Everything except camera
        private readonly List<GameObject> m_GameObjects = new List<GameObject>();
        private readonly Camera m_Camera;
        
        public Camera camera => m_Camera;

        public Scene scene => m_PreviewScene;

        public Stage(string sceneName)
        {
            m_PreviewScene = EditorSceneManager.NewPreviewScene();
            m_PreviewScene.name = sceneName;

            // Setup default render settings for this preview scene
            //false if
            //  - scene is not loaded
            //  - application cannot update
            //  - scene do not have a LevelGameManager for RenderSettings
            if (Unsupported.SetOverrideRenderSettings(m_PreviewScene))
            {
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom; //TODO: gather data from SRP
                //RenderSettings.customReflection =                //TODO: gather data from SRP
                RenderSettings.skybox = null;                      //TODO: gather data from SRP         
                RenderSettings.ambientMode = AmbientMode.Trilight; //TODO: gather data from SRP
                Unsupported.useScriptableRenderPipeline = true;
                Unsupported.RestoreOverrideRenderSettings();
            }
            else
                throw new System.Exception("Preview scene was not created correctly");
            
            var camGO = EditorUtility.CreateGameObjectWithHideFlags("Look Dev Camera", HideFlags.HideAndDontSave, typeof(Camera));
            
            SceneManager.MoveGameObjectToScene(camGO, m_PreviewScene);
            camGO.transform.position = Vector3.zero;
            camGO.transform.rotation = Quaternion.identity;
            camGO.hideFlags = HideFlags.HideAndDontSave;
            camGO.layer = k_PreviewCullingLayerIndex;

            m_Camera = camGO.GetComponent<Camera>();
            m_Camera.cameraType = CameraType.Preview;
            m_Camera.enabled = false;
            m_Camera.clearFlags = CameraClearFlags.Depth;
            m_Camera.fieldOfView = 15;
            m_Camera.farClipPlane = 10.0f;
            m_Camera.nearClipPlane = 2.0f;
            m_Camera.cullingMask = k_PreviewCullingLayerIndex;
            m_Camera.transform.position = new Vector3(0, 0, -6);
            
            m_Camera.renderingPath = RenderingPath.DeferredShading;
            m_Camera.useOcclusionCulling = false;
            m_Camera.scene = m_PreviewScene;

            m_Camera.backgroundColor = Color.white; // new Color(49.0f / 255.0f, 49.0f / 255.0f, 49.0f / 255.0f, 1.0f);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                m_Camera.backgroundColor = m_Camera.backgroundColor.linear;

            //TODO: check
            m_Camera.allowHDR = true;
        }

        public void AddGameObject(GameObject go)
            => AddGameObject(go, Vector3.zero, Quaternion.identity);
        public void AddGameObject(GameObject go, Vector3 position, Quaternion rotation)
        {
            if (m_GameObjects.Contains(go))
                return;

            SceneManager.MoveGameObjectToScene(go, m_PreviewScene);
            go.transform.position = position;
            go.transform.rotation = rotation;
            m_GameObjects.Add(go);

            InitAddedObjectsRecursively(go);
        }

        public GameObject InstantiateInStage(GameObject prefabOrSceneObject)
            => InstantiateInStage(prefabOrSceneObject, Vector3.zero, Quaternion.identity);
        public GameObject InstantiateInStage(GameObject prefabOrSceneObject, Vector3 position, Quaternion rotation)
        {
            var handle = GameObject.Instantiate(prefabOrSceneObject);
            AddGameObject(handle, position, rotation);
            return handle;
        }

        public void Dispose()
        {
            EditorSceneManager.ClosePreviewScene(m_PreviewScene);
            Clear();
            UnityEngine.Object.DestroyImmediate(camera);
        }

        /// <summary>
        /// Clear everything but the camera in the scene
        /// </summary>
        public void Clear()
        {
            foreach (var go in m_GameObjects)
                UnityEngine.Object.DestroyImmediate(go);
            m_GameObjects.Clear();
        }

        static void InitAddedObjectsRecursively(GameObject go)
        {
            go.hideFlags = HideFlags.HideAndDontSave;
            go.layer = k_PreviewCullingLayerIndex;
            foreach (Transform child in go.transform)
                InitAddedObjectsRecursively(child.gameObject);
        }

        public void SetGameObjectVisible(bool visible)
        {
            foreach (GameObject go in m_GameObjects)
            {
                if (go == null || go.Equals(null))
                    continue;
                foreach (UnityEngine.Renderer renderer in go.GetComponentsInChildren<UnityEngine.Renderer>())
                    renderer.enabled = visible;
                foreach (Light light in go.GetComponentsInChildren<Light>())
                    light.enabled = visible;
            }
        }
    }
}
