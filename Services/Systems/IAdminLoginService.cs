using System.Threading.Tasks;

namespace FaceLocker.Services
{
    public interface IAdminLoginService
    {
        /// <summary>
        /// 初始化管理员用户服务
        /// 检查是否存在默认管理员用户，不存在则创建
        /// </summary>
        Task<bool> InitializeAsync();

        /// <summary>
        /// 验证管理员登录
        /// 使用账号和密码验证，检查用户是否具备MANAGE_USER权限
        /// </summary>
        /// <param name="username">用户名</param>
        /// <param name="password">密码</param>
        /// <returns>是否验证成功</returns>
        Task<bool> VerifyAdminLoginAsync(string username, string password);

        /// <summary>
        /// 检查是否需要初始化默认管理员用户
        /// </summary>
        /// <returns>是否需要初始化</returns>
        Task<bool> NeedsInitializationAsync();
    }
}