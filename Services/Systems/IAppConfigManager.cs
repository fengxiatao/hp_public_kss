using FaceLocker.Models.Settings;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    public interface IAppConfigManager
    {
        /// <summary>
        /// 获取指定路径的配置值
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="key">配置键，支持嵌套格式，例如："Section:SubSection:Key"</param>
        /// <returns>配置值</returns>
        T GetValue<T>(string key);

        /// <summary>
        /// 更新指定路径的配置值
        /// </summary>
        /// <param name="key">配置键</param>
        /// <param name="value">新值</param>
        void UpdateValue(string key, object value);

        /// <summary>
        /// 添加新的配置节点（若已存在则更新）
        /// </summary>
        /// <param name="key">配置键</param>
        /// <param name="value">值</param>
        void AddNode(string key, object value);

        /// <summary>
        /// 将当前配置保存到文件
        /// </summary>
        void Save();

        #region 配置类异步操作方法

        /// <summary>
        /// 保存百度人脸识别设置
        /// </summary>
        /// <param name="settings">百度人脸识别设置对象</param>
        /// <returns>保存是否成功</returns>
        Task<bool> SaveBaiduFaceAsync(BaiduFaceSettings settings);

        /// <summary>
        /// 保存摄像头设置
        /// </summary>
        /// <param name="settings">摄像头设置对象</param>
        /// <returns>保存是否成功</returns>
        Task<bool> SaveCameraAsync(CameraSettings settings);

        /// <summary>
        /// 保存数据库设置
        /// </summary>
        /// <param name="settings">数据库设置对象</param>
        /// <returns>保存是否成功</returns>
        Task<bool> SaveDatabaseAsync(DatabaseSettings settings);

        /// <summary>
        /// 保存锁控制器设置
        /// </summary>
        /// <param name="settings">锁控制器设置对象</param>
        /// <returns>保存是否成功</returns>
        Task<bool> SaveLockControllerAsync(LockControllerSettings settings);

        /// <summary>
        /// 保存服务器设置
        /// </summary>
        /// <param name="settings">服务器设置对象</param>
        /// <returns>保存是否成功</returns>
        Task<bool> SaveServerAsync(ServerSettings settings);

        /// <summary>
        /// 保存安全设置
        /// </summary>
        /// <param name="settings">安全设置对象</param>
        /// <returns>保存是否成功</returns>
        Task<bool> SaveSecurityAsync(SecuritySettings settings);

        /// <summary>
        /// 保存完整的应用程序设置
        /// </summary>
        /// <param name="appSettings">应用程序设置对象</param>
        /// <returns>保存是否成功</returns>
        Task<bool> SaveAppSettingsAsync(AppSettings appSettings);

        /// <summary>
        /// 获取百度人脸识别设置
        /// </summary>
        /// <returns>百度人脸识别设置对象</returns>
        Task<BaiduFaceSettings> GetBaiduFaceAsync();

        /// <summary>
        /// 获取摄像头设置
        /// </summary>
        /// <returns>摄像头设置对象</returns>
        Task<CameraSettings> GetCameraAsync();

        /// <summary>
        /// 获取数据库设置
        /// </summary>
        /// <returns>数据库设置对象</returns>
        Task<DatabaseSettings> GetDatabaseAsync();

        /// <summary>
        /// 获取锁控制器设置
        /// </summary>
        /// <returns>锁控制器设置对象</returns>
        Task<LockControllerSettings> GetLockControllerAsync();

        /// <summary>
        /// 获取服务器设置
        /// </summary>
        /// <returns>服务器设置对象</returns>
        Task<ServerSettings> GetServerAsync();

        /// <summary>
        /// 获取安全设置
        /// </summary>
        /// <returns>安全设置对象</returns>
        Task<SecuritySettings> GetSecurityAsync();

        /// <summary>
        /// 获取完整的应用程序设置
        /// </summary>
        /// <returns>应用程序设置对象</returns>
        Task<AppSettings> GetAppSettingsAsync();

        #endregion
    }
}