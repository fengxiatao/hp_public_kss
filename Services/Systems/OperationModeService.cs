using FaceLocker.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 运行模式服务实现
    /// </summary>
    public class OperationModeService : IOperationModeService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<OperationModeService> _logger;

        private OperationMode _currentMode = OperationMode.Hybrid;
        private DateTime _lastModeChange = DateTime.Now;

        /// <summary>
        /// 获取当前运行模式
        /// </summary>
        public OperationMode CurrentMode => _currentMode;

        /// <summary>
        /// 获取是否为离线模式
        /// </summary>
        public bool IsOfflineMode => _currentMode == OperationMode.Offline;

        /// <summary>
        /// 运行模式变更事件
        /// </summary>
        public event EventHandler<OperationModeChangedEventArgs> OperationModeChanged;

        #region 构造函数
        /// <summary>
        /// 初始化运行模式服务
        /// </summary>
        /// <param name="configuration">配置服务</param>
        /// <param name="logger">日志记录器</param>
        public OperationModeService(
            IConfiguration configuration,
            ILogger<OperationModeService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }
        #endregion

        #region 公共方法
        /// <summary>
        /// 初始化运行模式服务
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            try
            {
                _logger.LogInformation("正在初始化运行模式服务");

                // 从配置读取默认模式
                var defaultMode = _configuration.GetValue<string>("Operation:DefaultMode", "Hybrid");
                if (Enum.TryParse<OperationMode>(defaultMode, true, out var mode))
                {
                    _currentMode = mode;
                }

                // 自动检测并切换模式
                await AutoDetectModeAsync();

                _logger.LogInformation("运行模式服务初始化完成，当前模式: {Mode}", _currentMode);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "运行模式服务初始化失败");
                return false;
            }
        }

        /// <summary>
        /// 切换到指定模式
        /// </summary>
        /// <param name="mode">目标模式</param>
        /// <param name="reason">切换原因</param>
        public async Task<bool> SwitchToModeAsync(OperationMode mode, string reason = "")
        {
            try
            {
                if (_currentMode == mode)
                {
                    _logger.LogDebug("运行模式未变化，无需切换");
                    return await Task.FromResult(true);
                }

                var oldMode = _currentMode;
                _currentMode = mode;
                _lastModeChange = DateTime.Now;

                _logger.LogInformation("运行模式已切换: {OldMode} → {NewMode}, 原因: {Reason}",
                    GetModeDisplayName(oldMode), GetModeDisplayName(mode), reason);

                // 触发模式变更事件
                OperationModeChanged?.Invoke(this, new OperationModeChangedEventArgs
                {
                    OldMode = oldMode,
                    NewMode = mode,
                    ChangedAt = DateTime.Now,
                    Reason = reason
                });

                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切换运行模式失败: {Mode}", mode);
                return false;
            }
        }

        /// <summary>
        /// 自动检测运行模式
        /// </summary>
        public async Task<OperationMode> AutoDetectModeAsync()
        {
            try
            {
                _logger.LogInformation("正在自动检测运行模式");

                // 检查服务器连接
                var canConnect = await CheckServerConnectionAsync();

                if (canConnect)
                {
                    // 服务器可连接，使用配置的默认模式或联网模式
                    var preferredMode = _configuration.GetValue<string>("Operation:PreferredOnlineMode", "Online");
                    if (Enum.TryParse<OperationMode>(preferredMode, true, out var mode))
                    {
                        await SwitchToModeAsync(mode, "服务器连接正常");
                        return mode;
                    }
                    else
                    {
                        await SwitchToModeAsync(OperationMode.Online, "服务器连接正常");
                        return OperationMode.Online;
                    }
                }
                else
                {
                    // 服务器不可连接，切换到离线模式
                    await SwitchToModeAsync(OperationMode.Offline, "服务器连接失败");
                    return OperationMode.Offline;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "自动检测运行模式失败");

                // 失败时保持当前模式
                return _currentMode;
            }
        }

        /// <summary>
        /// 检查服务器连接状态
        /// </summary>
        public async Task<bool> CheckServerConnectionAsync()
        {
            try
            {
                _logger.LogDebug("检查服务器连接状态");

                // 使用服务器通信服务检查连接
                // return await _serverCommunicationService.TestConnectionAsync();

                // 暂时返回 false，待实现服务器通信服务
                _logger.LogWarning("服务器连接检查功能暂未实现");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查服务器连接失败");
                return false;
            }
        }

        /// <summary>
        /// 获取模式状态文本
        /// </summary>
        public string GetModeStatusText()
        {
            return _currentMode switch
            {
                OperationMode.Offline => "单机模式",
                OperationMode.Online => "联网模式",
                OperationMode.Hybrid => "混合模式",
                _ => "未知模式"
            };
        }

        /// <summary>
        /// 获取模式状态颜色
        /// </summary>
        public string GetModeStatusColor()
        {
            return _currentMode switch
            {
                OperationMode.Offline => "#FF9800", // 橙色
                OperationMode.Online => "#4CAF50",  // 绿色
                OperationMode.Hybrid => "#2196F3",  // 蓝色
                _ => "#9E9E9E"                      // 灰色
            };
        }
        #endregion

        #region 私有方法
        /// <summary>
        /// 获取模式显示名称
        /// </summary>
        private string GetModeDisplayName(OperationMode mode)
        {
            return mode switch
            {
                OperationMode.Offline => "单机模式",
                OperationMode.Online => "联网模式",
                OperationMode.Hybrid => "混合模式",
                _ => "未知模式"
            };
        }
        #endregion
    }
}