using UnityEngine;

public class SGLWGraphicsTestSettings : LWGraphicsTestSettings
{
    public GameObject sgRoot;
    public GameObject lwRoot;

    public SGLWGraphicsTestSettings() : base()
    {
        ImageComparisonSettings.TargetWidth = 640;
        ImageComparisonSettings.TargetHeight = 360;
    }
}