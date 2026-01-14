namespace FaceLocker.Models.Settings
{
    /// <summary>
    /// 摄像头设置
    /// </summary>
    public class CameraSettings
    {
        /// <summary>
        /// 默认摄像头索引
        /// </summary>
        public int DefaultCameraIndex { get; set; } = 0;

        /// <summary>
        /// 摄像头设备路径（用于GStreamer）
        /// </summary>
        public string DevicePath { get; set; } = "/dev/video0";

        /// <summary>
        /// 帧率
        /// </summary>
        public int FrameRate { get; set; } = 30;

        /// <summary>
        /// 分辨率
        /// </summary>
        public Resolution Resolution { get; set; } = new Resolution();

        /// <summary>
        /// 是否自动检测摄像头
        /// </summary>
        public bool AutoDetectCameras { get; set; } = true;


        public CameraSettings()
        {
            Resolution = new Resolution { Width = 640, Height = 480 };
        }
    }

    /// <summary>
    /// 分辨率设置
    /// </summary>
    public class Resolution
    {
        /// <summary>
        /// 宽度
        /// </summary>
        public int Width { get; set; } = 640;

        /// <summary>
        /// 高度
        /// </summary>
        public int Height { get; set; } = 480;
    }
}
