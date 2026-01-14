using FaceLocker.Models;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 数据同步服务接口
    /// </summary>
    public interface IDataSyncService
    {
        #region 设置网络服务

        /// <summary>
        /// 设置网络服务
        /// </summary>
        /// <param name="networkService">网络服务实例</param>
        void SetNetworkService(INetworkService networkService);
        #endregion

        #region 初始化数据同步服务
        /// <summary>
        /// 初始化数据同步服务
        /// </summary>
        Task<bool> InitializeAsync();

        #endregion

        #region 数据下载处理 - 客户端主动请求
        /// <summary>
        /// 处理锁控板数据
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        Task<bool> ProcessBoardsDataAsync(string data);
        /// <summary>
        /// 处理角色数据
        /// </summary>
        /// <param name="data">角色数据JSON</param>
        /// <returns>处理是否成功</returns>
        Task<bool> ProcessRolesDataAsync(string data);

        /// <summary>
        /// 处理用户数据
        /// </summary>
        /// <param name="data">用户数据JSON</param>
        /// <returns>处理是否成功</returns>
        Task<bool> ProcessUsersDataAsync(string data);

        /// <summary>
        /// 处理锁柜数据
        /// </summary>
        /// <param name="data">锁柜数据JSON</param>
        /// <returns>处理是否成功</returns>
        Task<bool> ProcessLockersDataAsync(string data);

        /// <summary>
        /// 处理用户锁柜关联数据
        /// </summary>
        /// <param name="data">用户锁柜关联数据JSON</param>
        /// <returns>处理是否成功</returns>
        Task<bool> ProcessUserLockersDataAsync(string data);

        #endregion

        #region 服务器发起的全量同步
        /// <summary>
        /// 全量同步角色列表
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        Task<bool> SyncRolesAsync(string data);
        /// <summary>
        /// 全量同步用户列表
        /// </summary>
        /// <param name="data">用户数据JSON</param>
        /// <returns>同步是否成功</returns>
        Task<bool> SyncUsersAsync(string data);

        /// <summary>
        /// 全量同步锁柜列表
        /// </summary>
        /// <param name="data">锁柜数据JSON</param>
        /// <returns>同步是否成功</returns>
        Task<bool> SyncLockersAsync(string data);

        /// <summary>
        /// 全量同步用户锁柜关联列表
        /// </summary>
        /// <param name="data">用户锁柜关联数据JSON</param>
        /// <returns>同步是否成功</returns>
        Task<bool> SyncUserLockersAsync(string data);
        #endregion

        #region 服务器指令创建或更新单个用户
        /// <summary>
        /// 创建或更新用户
        /// </summary>
        /// <param name="data">用户数据JSON</param>
        /// <returns>返回操作结果：(是否成功, 是否新用户, 用户ID)</returns>
        Task<(bool success, bool isNewUser, long userId)> CreateOrUpdateUserAsync(string data);
        #endregion

        #region 用户柜格分配管理
        /// <summary>
        /// 处理用户柜格分配指令（不发送响应）
        /// </summary>
        /// <param name="data">分配数据JSON</param>
        /// <param name="sendResponse">是否发送响应</param>
        /// <returns>处理是否成功</returns>
        Task<bool> ProcessUserLockerAssignmentCommandAsync(string data, bool sendResponse = true);
        #endregion

        #region 更新管理员密码
        /// <summary>
        /// 更新管理员密码
        /// </summary>
        /// <param name="newPassword"></param>
        /// <returns></returns>
        Task<bool> UpdateAdminPasswordAsync(string newPassword);
        #endregion

        #region 同步访问日志到服务器
        /// <summary>
        /// 同步访问日志到服务器
        /// </summary>
        Task<bool> SyncAccessLogsAsync();

        #endregion

        #region 获取用户锁柜关联
        /// <summary>
        /// 获取用户锁柜关联
        /// </summary>
        /// <param name="lockerId"></param>
        /// <returns></returns>
        Task<Locker> GetUserLocker(long lockerId);
        #endregion
    }
}