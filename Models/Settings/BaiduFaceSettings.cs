namespace FaceLocker.Models.Settings
{
    public class BaiduFaceSettings
    {
        /// <summary>
        /// 识别分数阈值（0-100）
        /// </summary>
        public int IdentifyScore { get; set; } = 80;

        /// <summary>
        /// 模型路径（Linux路径）
        /// </summary>
        public string ModelPath { get; set; } = "/opt/face_offline_sdk";

        /// <summary>
        /// 授权文件路径
        /// </summary>
        public string LicenseKey { get; set; } = "/home/orangepi/soft/face_offline_sdk/license/license.ini";

        /// <summary>
        /// 最大检测人脸数
        /// </summary>
        public int MaxDetectNum { get; set; } = 5;

        /// <summary>
        /// 最小人脸尺寸（0表示自动）
        /// </summary>
        public int MinFaceSize { get; set; } = 0;

        /// <summary>
        /// 相似度阈值（0.0 - 1.0）
        /// </summary>
        public double SimilarityThreshold { get; set; } = 0.8;
    }
}
