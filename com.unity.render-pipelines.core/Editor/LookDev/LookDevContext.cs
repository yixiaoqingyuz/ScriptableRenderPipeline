using UnityEngine;

namespace UnityEditor.Rendering.LookDev
{
    [System.Serializable]
    public class LookDevContext : ScriptableObject
    {
        [field: SerializeField]
        public LayoutContext layout { get; private set; } = new LayoutContext();
        [field: SerializeField]
        public ViewContext viewA { get; private set; } = new ViewContext();
        [field: SerializeField]
        public ViewContext viewB { get; private set; } = new ViewContext();
        [field: SerializeField]
        public LookDevCameraState cameraA { get; private set; } = new LookDevCameraState();
        [field: SerializeField]
        public LookDevCameraState cameraB { get; private set; } = new LookDevCameraState();
    }
    
    [System.Serializable]
    public class LayoutContext
    {
        // /!\ WARNING: these value name are used as uss file too.
        // if your rename here, rename in the uss too.
        public enum Layout { FullA, FullB, HorizontalSplit, VerticalSplit, CustomSplit, CustomCircular }

        public Layout viewLayout;
        public bool showEnvironmentPanel;

        [SerializeField]
        internal LookDevGizmoState gizmoState = new LookDevGizmoState();

        public bool isSimpleView => viewLayout == Layout.FullA || viewLayout == Layout.FullB;
        public bool isMultiView => viewLayout == Layout.HorizontalSplit || viewLayout == Layout.VerticalSplit;
        public bool isCombinedView => viewLayout == Layout.CustomSplit || viewLayout == Layout.CustomCircular;
    }

    [System.Serializable]
    public class ViewContext
    {
        //[TODO: add object]
        //[TODO: add object position]
        //[TODO: add camera frustum]
        //[TODO: add HDRI]
        //[TODO: manage shadow and lights]
    }
    
}
