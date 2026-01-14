namespace FaceLocker.Models
{
    /// <summary>
    /// 认证模式枚举
    /// </summary>
    public enum AuthMode
    {
        /// <summary>
        /// 会话认证（成功后建立会话）
        /// </summary>
        Session = 1,

        /// <summary>
        /// 强制认证（每次都需要验证）
        /// </summary>
        Force = 2
    }
}
