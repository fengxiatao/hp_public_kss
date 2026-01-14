namespace FaceLocker.Models
{
    /// <summary>
    /// 运行模式枚举
    /// </summary>
    public enum OperationMode
    {
        /// <summary>
        /// 单机模式
        /// </summary>
        Offline = 1,

        /// <summary>
        /// 联网模式
        /// </summary>
        Online = 2,

        /// <summary>
        /// 混合模式（优先使用本地数据，尝试连接服务器）
        /// </summary>
        Hybrid = 3
    }
}
