namespace FaceLocker.Models.Settings
{
    public class SecuritySettings
    {
        /// <summary>
        /// 管理员密码哈希值（SHA256）
        /// </summary>
        public string AdminPasswordHash { get; set; } = "8D969EEF6ECAD3C29A3A629280E686CF0C3F5D5A86AFF3CA12020C923ADC6C92";

        /// <summary>
        /// 管理员默认密码
        /// </summary>
        public string AdminDefaultPassword { get; set; } = "123456";

        /// <summary>
        /// 会话超时时间（分钟），默认30分钟
        /// </summary>
        public long SessionTimeout { get; set; } = 30;
    }
}
