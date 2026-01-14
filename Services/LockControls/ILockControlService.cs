using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 锁控板服务接口
    /// </summary>
    public interface ILockControlService : IDisposable
    {
        /// <summary>底层严重错误冒泡通知。</summary>
        event EventHandler<string>? ErrorOccurred;

        /// <summary>
        /// 获取配置中的所有板地址和串口。
        /// </summary>
        IReadOnlyList<(int Address, string SerialPort)> GetConfiguredBoards();


        /// <summary>
        /// 测试连接
        /// </summary>
        Task<bool> TestConnectionAsync();

        /// <summary>
        /// 尝试打开所有涉及的串口。
        /// </summary>
        Task<IReadOnlyDictionary<string, bool>> EnsureAllPortsOpenAsync(bool announce = false);

        /// <summary>
        /// 初始化自检（保证连接和基本通讯）。
        /// </summary>
        Task<bool> InitializeLockControlAsync();

        /// <summary>
        /// 健康检查（对默认板的1号锁检测回包）。
        /// </summary>
        Task<bool> CheckConnectionAsync(CancellationToken ct = default);

        /// <summary>
        /// 打开指定板的指定通道（默认300ms脉冲）。
        /// </summary>
        Task<bool> OpenLockAsync(int boardAddr, int channel);

        /// <summary>
        /// 一键清柜（全开）。
        /// </summary>
        Task<bool> OpenAllLocksAsync(CancellationToken ct = default);

        /// <summary>
        /// 读取指定板单个锁通道状态（协议80 AA CC 33 BCC）。
        /// </summary>
        Task<bool?> ReadOneStatusAsync(int boardAddr, int channel, CancellationToken ct = default);

        /// <summary>
        /// 读取指定板整板所有锁状态（协议80 AA 00 33 BCC）
        /// 返回包含板地址、通道地址和状态三个值的元组列表
        /// </summary>
        /// <param name="boardAddr">板地址</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>包含(板地址, 通道号, 状态)的元组列表，null表示读取失败</returns>
        Task<List<(int BoardAddress, int Channel, bool Status)>?> ReadAllStatusAsync(int boardAddr, CancellationToken ct = default);
    }


    #region 柜子开锁结果
    /// <summary>
    /// 柜子开锁结果
    /// </summary>
    public class LockerOpenResult
    {
        public long LockerId { get; set; } = 0;
        public string LockerName { get; set; } = string.Empty;
        public int BoardAddress { get; set; }
        public int ChannelNumber { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? OpenedAt { get; set; }
    }
    #endregion
}