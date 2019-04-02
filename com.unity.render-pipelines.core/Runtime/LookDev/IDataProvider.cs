namespace UnityEngine.Rendering.LookDev
{
    public interface IDataProvider
    {
        CustomRenderSettings GetEnvironmentSetup();
        void SetupCamera(Camera camera);
    }

    public struct CustomRenderSettings
    {
        public DefaultReflectionMode defaultReflectionMode;
        public Cubemap customReflection;
        public Material skybox;
        public AmbientMode ambientMode;
    }
}
