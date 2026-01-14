using FaceLocker.Models;
using System;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 运行模式服务接口
    /// 管理系统运行模式（单机/联网）和模式切换
    /// </summary>
    public interface IOperationModeService
    {
        /// <summary>
        /// 当前运行模式
        /// </summary>
        OperationMode CurrentMode { get; }

        /// <summary>
        /// 是否处于离线模式
        /// </summary>
        bool IsOfflineMode { get; }

        /// <summary>
        /// 运行模式变更事件
        /// </summary>
        event EventHandler<OperationModeChangedEventArgs> OperationModeChanged;

        /// <summary>
        /// 初始化服务
        /// </summary>
        Task<bool> InitializeAsync();

        /// <summary>
        /// 切换到指定模式
        /// </summary>
        /// <param name="mode">目标模式</param>
        /// <param name="reason">切换原因</param>
        /// <returns>是否切换成功</returns>
        Task<bool> SwitchToModeAsync(OperationMode mode, string reason = "");

        /// <summary>
        /// 自动检测并切换模式
        /// </summary>
        /// <returns>当前模式</returns>
        Task<OperationMode> AutoDetectModeAsync();

        /// <summary>
        /// 检查服务器连接状态
        /// </summary>
        /// <returns>是否可连接</returns>
        Task<bool> CheckServerConnectionAsync();

        /// <summary>
        /// 获取模式状态文本
        /// </summary>
        /// <returns>状态文本</returns>
        string GetModeStatusText();

        /// <summary>
        /// 获取模式状态颜色
        /// </summary>
        /// <returns>状态颜色</returns>
        string GetModeStatusColor();
    }

    /// <summary>
    /// 运行模式变更事件参数
    /// </summary>
    public class OperationModeChangedEventArgs : EventArgs
    {
        public OperationMode OldMode { get; set; }
        public OperationMode NewMode { get; set; }
        public DateTime ChangedAt { get; set; }
        public string Reason { get; set; }
    }
}
