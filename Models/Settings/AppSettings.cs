namespace FaceLocker.Models.Settings
{
    public class AppSettings
    {
        public BaiduFaceSettings BaiduFace { get; set; } = new();

        public CameraSettings Camera { get; set; } = new();

        public DatabaseSettings Database { get; set; } = new();

        public LockControllerSettings LockController { get; set; } = new();

        public SecuritySettings Security { get; set; } = new();

        public ServerSettings Server { get; set; } = new();
    }
}
