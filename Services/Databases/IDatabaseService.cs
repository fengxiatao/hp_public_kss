using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 数据库服务接口
    /// 定义数据库初始化、迁移、备份恢复等操作
    /// </summary>
    public interface IDatabaseService
    {
        #region 数据库初始化

        /// <summary>
        /// 初始化数据库（创建数据库、应用迁移、加载种子数据）
        /// </summary>
        /// <returns>初始化是否成功</returns>
        Task<bool> InitializeDatabaseAsync();

        /// <summary>
        /// 检查数据库连接状态
        /// </summary>
        /// <returns>连接是否成功</returns>
        Task<bool> CheckDatabaseConnectionAsync();

        /// <summary>
        /// 记录数据库连接详细信息
        /// </summary>
        void LogDatabaseConnectionInfo();

        #endregion
    }
}