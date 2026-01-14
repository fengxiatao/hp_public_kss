using System.Text.Json.Serialization;

namespace FaceLocker.Models.Settings
{
    /// <summary>
    /// 服务器设置配置
    /// </summary>
    public class ServerSettings
    {
        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName { get; set; } = "FaceLocker";

        /// <summary>
        /// 服务器地址 (IP地址或域名)
        /// </summary>
        public string ServerAddress { get; set; } = "localhost";

        /// <summary>
        /// 服务器端口
        /// </summary>
        public int ServerPort { get; set; } = 8080;

        /// <summary>
        /// 检测间隔（单位：秒）
        /// </summary>
        public int CheckInterval { get; set; } = 30;
    }
}
