using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.Rendering.LookDev
{
    public interface IDisplayer
    {
        Rect GetRect(ViewIndex index);
        void SetTexture(ViewIndex index, Texture texture);

        event Action<Layout> OnLayoutChanged;

        event Action OnRenderDocAcquisitionTriggered;
    }

    /// <summary>
    /// Displayer and User Interaction 
    /// </summary>
    internal class DisplayWindow : EditorWindow, IDisplayer
    {
        static class Style
        {
            internal const string k_IconFolder = @"Packages/com.unity.render-pipelines.core/Editor/LookDev/Icons/";
            internal const string k_uss = @"Packages/com.unity.render-pipelines.core/Editor/LookDev/DisplayWindow.uss";

            public static readonly GUIContent WindowTitleAndIcon = EditorGUIUtility.TrTextContentWithIcon("Look Dev", CoreEditorUtils.LoadIcon(k_IconFolder, "LookDevMainIcon"));
        }
        
        // /!\ WARNING:
        //The following const are used in the uss.
        //If you change them, update the uss file too.
        const string k_MainContainerName = "mainContainer";
        const string k_EnvironmentContainerName = "environmentContainer";
        const string k_ViewContainerName = "viewContainer";
        const string k_FirstViewName = "firstView";
        const string k_SecondViewName = "secondView";
        const string k_ToolbarName = "toolbar";
        const string k_ToolbarRadioName = "toolbarRadio";
        const string k_ToolbarEnvironmentName = "toolbarEnvironment";
        const string k_SharedContainerClass = "container";
        const string k_FirstViewClass = "firstView";
        const string k_SecondViewsClass = "secondView";
        const string k_ShowEnvironmentPanelClass = "showEnvironmentPanel";

        VisualElement m_MainContainer;
        VisualElement m_ViewContainer;
        VisualElement m_EnvironmentContainer;

        Image[] m_Views = new Image[2];
        

        Layout layout
        {
            get => LookDev.currentContext.layout.viewLayout;
            set
            {
                if (LookDev.currentContext.layout.viewLayout != value)
                {
                    if (value == Layout.HorizontalSplit || value == Layout.VerticalSplit)
                    {
                        if (!m_ViewContainer.ClassListContains(k_FirstViewClass))
                            m_ViewContainer.AddToClassList(k_FirstViewClass);
                        if (!m_ViewContainer.ClassListContains(k_SecondViewsClass))
                            m_ViewContainer.AddToClassList(k_SecondViewsClass);
                    }
                    else if (value == Layout.FullA)
                    {
                        if (!m_ViewContainer.ClassListContains(k_FirstViewClass))
                            m_ViewContainer.AddToClassList(k_FirstViewClass);
                        if (m_ViewContainer.ClassListContains(k_SecondViewsClass))
                            m_ViewContainer.RemoveFromClassList(k_SecondViewsClass);
                    }
                    else if (value == Layout.FullB)
                    {
                        if (m_ViewContainer.ClassListContains(k_FirstViewClass))
                            m_ViewContainer.RemoveFromClassList(k_FirstViewClass);
                        if (!m_ViewContainer.ClassListContains(k_SecondViewsClass))
                            m_ViewContainer.AddToClassList(k_SecondViewsClass);
                    }
                    else
                    {
                        if (m_ViewContainer.ClassListContains(k_FirstViewClass))
                            m_ViewContainer.RemoveFromClassList(k_FirstViewClass);
                        if (m_ViewContainer.ClassListContains(k_SecondViewsClass))
                            m_ViewContainer.RemoveFromClassList(k_SecondViewsClass);
                    }

                    //Handle flex direction here
                    if (m_ViewContainer.ClassListContains(LookDev.currentContext.layout.viewLayout.ToString()))
                        m_ViewContainer.RemoveFromClassList(LookDev.currentContext.layout.viewLayout.ToString());
                    m_ViewContainer.AddToClassList(value.ToString());

                    LookDev.currentContext.layout.viewLayout = value;

                    OnLayoutChangedInternal?.Invoke(value);
                }
            }
        }
        
        bool showEnvironmentPanel
        {
            get => LookDev.currentContext.layout.showEnvironmentPanel;
            set
            {
                if (LookDev.currentContext.layout.showEnvironmentPanel != value)
                {
                    if (value)
                    {
                        if (!m_MainContainer.ClassListContains(k_ShowEnvironmentPanelClass))
                            m_MainContainer.AddToClassList(k_ShowEnvironmentPanelClass);
                    }
                    else
                    {
                        if (m_MainContainer.ClassListContains(k_ShowEnvironmentPanelClass))
                            m_MainContainer.RemoveFromClassList(k_ShowEnvironmentPanelClass);
                    }

                    LookDev.currentContext.layout.showEnvironmentPanel = value;
                }
            }
        }
        
        event Action<Layout> OnLayoutChangedInternal;
        event Action<Layout> IDisplayer.OnLayoutChanged
        {
            add => OnLayoutChangedInternal += value;
            remove => OnLayoutChangedInternal -= value;
        }

        event Action OnRenderDocAcquisitionTriggeredInternal;
        event Action IDisplayer.OnRenderDocAcquisitionTriggered
        {
            add => OnRenderDocAcquisitionTriggeredInternal += value;
            remove => OnRenderDocAcquisitionTriggeredInternal -= value;
        }

        public event Action OnWindowClosed;

        void OnEnable()
        {
            titleContent = Style.WindowTitleAndIcon;

            rootVisualElement.styleSheets.Add(
                AssetDatabase.LoadAssetAtPath<StyleSheet>(Style.k_uss));
            
            CreateToolbar();
            
            m_MainContainer = new VisualElement() { name = k_MainContainerName };
            m_MainContainer.AddToClassList(k_SharedContainerClass);
            rootVisualElement.Add(m_MainContainer);

            CreateViews();
            CreateEnvironment();
        }

        void OnDisable() => OnWindowClosed?.Invoke();

        void CreateToolbar()
        {
            // Layout swapper part
            var toolbarRadio = new ToolbarRadio() { name = k_ToolbarRadioName };
            toolbarRadio.AddRadios(new[] {
                CoreEditorUtils.LoadIcon(Style.k_IconFolder, "LookDevSingle1"),
                CoreEditorUtils.LoadIcon(Style.k_IconFolder, "LookDevSingle2"),
                CoreEditorUtils.LoadIcon(Style.k_IconFolder, "LookDevSideBySideVertical"),
                CoreEditorUtils.LoadIcon(Style.k_IconFolder, "LookDevSideBySideHorizontal"),
                CoreEditorUtils.LoadIcon(Style.k_IconFolder, "LookDevSplit"),
                CoreEditorUtils.LoadIcon(Style.k_IconFolder, "LookDevZone"),
                });
            toolbarRadio.RegisterCallback((ChangeEvent<int> evt)
                => layout = (Layout)evt.newValue);
            toolbarRadio.SetValueWithoutNotify((int)layout);

            // Environment part
            var toolbarEnvironment = new Toolbar() { name = k_ToolbarEnvironmentName };
            var showEnvironmentToggle = new ToolbarToggle() { text = "Show Environment" };
            showEnvironmentToggle.RegisterCallback((ChangeEvent<bool> evt)
                => showEnvironmentPanel = evt.newValue);
            showEnvironmentToggle.SetValueWithoutNotify(showEnvironmentPanel);
            toolbarEnvironment.Add(showEnvironmentToggle);

            //other parts to be completed

            // Aggregate parts
            var toolbar = new Toolbar() { name = k_ToolbarName };
            toolbar.Add(new Label() { text = "Layout:" });
            toolbar.Add(toolbarRadio);
            toolbar.Add(new ToolbarSpacer());
            //to complete


            toolbar.Add(new ToolbarSpacer() { flex = true });

            //TODO: better RenderDoc integration
            toolbar.Add(new ToolbarButton(() => OnRenderDocAcquisitionTriggeredInternal?.Invoke())
            {
                text = "RenderDoc Content"
            });
            
            toolbar.Add(toolbarEnvironment);
            rootVisualElement.Add(toolbar);
        }

        void CreateViews()
        {
            if (m_MainContainer == null || m_MainContainer.Equals(null))
                throw new System.MemberAccessException("m_MainContainer should be assigned prior CreateViews()");

            m_ViewContainer = new VisualElement() { name = k_ViewContainerName };
            m_ViewContainer.AddToClassList(LookDev.currentContext.layout.isMultiView ? k_SecondViewsClass : k_FirstViewClass);
            m_ViewContainer.AddToClassList(k_SharedContainerClass);
            m_MainContainer.Add(m_ViewContainer);

            m_Views[(int)ViewIndex.First] = new Image() { name = k_FirstViewName, image = Texture2D.blackTexture };
            m_ViewContainer.Add(m_Views[(int)ViewIndex.First]);
            m_Views[(int)ViewIndex.Second] = new Image() { name = k_SecondViewName, image = Texture2D.blackTexture };
            m_ViewContainer.Add(m_Views[(int)ViewIndex.Second]);
        }

        void CreateEnvironment()
        {
            if (m_MainContainer == null || m_MainContainer.Equals(null))
                throw new System.MemberAccessException("m_MainContainer should be assigned prior CreateEnvironment()");

            m_EnvironmentContainer = new VisualElement() { name = k_EnvironmentContainerName };
            m_MainContainer.Add(m_EnvironmentContainer);
            if (showEnvironmentPanel)
                m_MainContainer.AddToClassList(k_ShowEnvironmentPanelClass);

            //to complete
        }

        Rect IDisplayer.GetRect(ViewIndex index)
            => m_Views[(int)index].contentRect;

        void IDisplayer.SetTexture(ViewIndex index, Texture texture)
            => m_Views[(int)index].image = texture;
    }
    
}
