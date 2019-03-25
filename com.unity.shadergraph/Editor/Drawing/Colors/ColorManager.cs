using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEditor.ShaderGraph.Drawing.Colors
{
    class ColorManager
    {
        List<IColorProvider> m_Providers;
        IColorProvider m_ActiveColors;

        public ColorManager(string activeColors)
        {
            m_Providers = new List<IColorProvider>();

            if (string.IsNullOrEmpty(activeColors))
                activeColors = "Category";

            foreach (var colorType in UnityEditor.TypeCache.GetTypesDerivedFrom<IColorProvider>())
            {
                var provider = (IColorProvider) Activator.CreateInstance(colorType);
                if (provider.Title == activeColors)
                {
                    m_ActiveColors = provider;
                }

                m_Providers.Add(provider);
            }
        }

        public Color GetColor(AbstractMaterialNode node)
        {
            return m_ActiveColors.GetColor(node);
        }
    }

    interface IColorProvider
    {
        string Title { get; }

        Color GetColor(AbstractMaterialNode node);
    }

    class CategoryColors : IColorProvider
    {
        public string Title => "Category";

        public Color GetColor(AbstractMaterialNode node)
        {
            var title = node.GetType().GetCustomAttributes(typeof(TitleAttribute), false).FirstOrDefault() as TitleAttribute;
            if(!typeColors.TryGetValue(title.title[0], out var ret))
                ret = Color.magenta;
            return ret;
        }

        public CategoryColors()
        {
            typeColors = new Dictionary<string, Color>();
            typeColors.Add("Artistic", new Color(0.0f, 0.3f, 0.3f));
            typeColors.Add("Channel", new Color(0.237f, 0.3f, 0.12f));
            typeColors.Add("Input", new Color(0.5f, 0.07499999f, 0.07499999f));
            typeColors.Add("Master", new Color(0.2235294f, 0.2235294f, 0.2235294f)); // this should actually just stay the same grey we used to have
            typeColors.Add("Math", new Color(0.1494118f, 0.265621f, 0.4980392f));
            typeColors.Add("Procedural", new Color(0.4f, 0.2f, 0.3662921f));
            typeColors.Add("Utility", new Color(0.1935937f, 0.1575f, 0.35f));
            typeColors.Add("UV", new Color(0.04705882f, 0.2235294f, 0.04705882f));
            
//            typeColors.Add("Artistic", new Color());
//            typeColors.Add("Channel", new Color());
//            typeColors.Add("Input", new Color());
//            typeColors.Add("Master", new Color());
//            typeColors.Add("Math", new Color());
//            typeColors.Add("Procedural", new Color());
//            typeColors.Add("Utility", new Color());
//            typeColors.Add("UV", new Color());
        }
        Dictionary<string, Color> typeColors;
    }
}
