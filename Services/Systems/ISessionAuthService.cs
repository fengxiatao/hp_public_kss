using FaceLocker.Models;
using System;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 会话认证服务接口
    /// 管理用户登录状态、密码验证和会话超时
    /// </summary>
    public interface ISessionAuthService
    {

        /// <summary>
        /// 获取当前认证的用户
        /// </summary>
        User CurrentUser { get; }

        /// <summary>
        /// 当前是否已认证
        /// </summary>
        bool IsAuthenticated { get; }

        /// <summary>
        /// 认证时间
        /// </summary>
        DateTime? AuthenticatedAt { get; }

        /// <summary>
        /// 会话超时时间（分钟）
        /// </summary>
        int SessionTimeoutMinutes { get; set; }

        /// <summary>
        /// 认证状态变更事件
        /// </summary>
        event EventHandler<AuthenticationStatusChangedEventArgs> AuthenticationStatusChanged;

        /// <summary>
        /// 执行认证
        /// </summary>
        /// <param name="mode">认证模式</param>
        /// <param name="operation">操作名称</param>
        /// <returns>是否认证成功</returns>
        Task<bool> AuthenticateAsync(AuthMode mode = AuthMode.Session, string operation = "");

        /// <summary>
        /// 验证密码
        /// </summary>
        /// <param name="username">用户名</param>
        /// <param name="password">密码</param>
        /// <returns>是否验证成功</returns>
        Task<bool> VerifyPasswordAsync(string username, string password);

        /// <summary>
        /// 设置认证状态
        /// </summary>
        /// <param name="authenticated">是否认证</param>
        void SetAuthenticationStatus(bool authenticated);

        /// <summary>
        /// 清除认证状态
        /// </summary>
        void ClearAuthentication();

        /// <summary>
        /// 检查会话是否过期
        /// </summary>
        /// <returns>是否过期</returns>
        bool IsSessionExpired();

        /// <summary>
        /// 刷新会话时间
        /// </summary>
        void RefreshSession();

        /// <summary>
        /// 获取剩余会话时间（分钟）
        /// </summary>
        /// <returns>剩余分钟数</returns>
        int GetRemainingSessionMinutes();
    }

    /// <summary>
    /// 认证状态变更事件参数
    /// </summary>
    public class AuthenticationStatusChangedEventArgs : EventArgs
    {
        public bool OldStatus { get; set; }
        public bool NewStatus { get; set; }
        public DateTime ChangedAt { get; set; }
        public string Reason { get; set; }
    }
}
