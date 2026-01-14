using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 应用状态服务接口
    /// 负责管理应用窗口状态和导航
    /// </summary>
    public interface IAppStateService
    {
        #region 属性
        /// <summary>
        /// 桌面应用生命周期管理
        /// </summary>
        IClassicDesktopStyleApplicationLifetime DesktopLifetime { get; set; }

        /// <summary>
        /// 主窗口引用
        /// </summary>
        Window MainWindow { get; }
        #endregion

        #region 窗口管理方法
        /// <summary>
        /// 返回主窗口
        /// 关闭所有其他窗口并显示主窗口
        /// </summary>
        void ReturnToMainWindow();

        /// <summary>
        /// 显示管理员主窗口
        /// </summary>
        void ShowAdminMainWindow();

        /// <summary>
        /// 显示管理员登录窗口
        /// 用于管理员身份验证
        /// </summary>
        void ShowAdminLoginWindow();

        /// <summary>
        /// 显示存放物品窗口
        /// </summary>
        void ShowStoreWindowAsync();

        /// <summary>
        /// 显示取物窗口
        /// </summary>
        void ShowRetrieveWindowAsync();

        /// <summary>
        /// 关闭指定类型的窗口
        /// </summary>
        /// <typeparam name="TWindow">要关闭的窗口类型</typeparam>
        void CloseWindow<TWindow>() where TWindow : Window;

        /// <summary>
        /// 关闭所有非主窗口
        /// </summary>
        void CloseAllNonMainWindows();

        /// <summary>
        /// 显示并激活主窗口
        /// </summary>
        void ShowAndActivateMainWindow();
        #endregion

        #region 窗口状态检查
        /// <summary>
        /// 检查指定类型的窗口是否已打开
        /// </summary>
        /// <typeparam name="TWindow">要检查的窗口类型</typeparam>
        /// <returns>如果窗口存在且可见返回true，否则返回false</returns>
        bool IsWindowOpen<TWindow>() where TWindow : Window;

        /// <summary>
        /// 获取指定类型的窗口实例
        /// </summary>
        /// <typeparam name="TWindow">窗口类型</typeparam>
        /// <returns>窗口实例，如果未找到返回null</returns>
        TWindow GetWindow<TWindow>() where TWindow : Window;

        /// <summary>
        /// 检查主窗口是否处于全屏状态
        /// </summary>
        /// <returns>如果主窗口全屏返回true，否则返回false</returns>
        bool IsMainWindowFullScreen();

        /// <summary>
        /// 获取当前打开的窗口数量（不包括主窗口）
        /// </summary>
        /// <returns>打开的窗口数量</returns>
        int GetOpenWindowCount();
        #endregion

        #region 应用状态管理
        /// <summary>
        /// 检查应用是否处于就绪状态
        /// </summary>
        /// <returns>如果所有必要服务已初始化返回true</returns>
        bool IsApplicationReady();

        /// <summary>
        /// 获取应用运行状态摘要
        /// </summary>
        /// <returns>包含窗口状态和运行信息的字符串</returns>
        string GetApplicationStatus();

        /// <summary>
        /// 安全关闭应用
        /// 清理资源并关闭所有窗口
        /// </summary>
        void ShutdownApplication();
        #endregion
    }
}
