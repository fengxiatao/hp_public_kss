using FaceLocker.Models;
using FaceLocker.Models.Enums;
using FaceLocker.Models.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 锁控板服务（RS-485；严格按《485锁控板24路通讯协议2021》）
    /// 
    /// 工程化特性：
    /// - 端口池化 + 长期开启：同一串口只打开一次并复用。
    /// - 半双工串行互斥：每个串口一个 SemaphoreSlim，所有收发串行化。
    /// - 自动重连：每次收发前确保端口打开；异常后自动重开。
    /// - Linux 端口名双尝试：同时尝试 "/dev/ttyS9" 与 "ttyS9"。
    /// 
    /// 协议要点（BCC 为异或和，对除 BCC 之外的字节逐字节 XOR）：
    /// - 全开：8A AA 00 11 BCC
    /// - 单开（默认 300ms）：8A AA CC 11 BCC ；指定 1～9 秒：8A AA CC 11 SS BCC
    /// - 读单锁状态： 80 AA CC 33 BCC （例：80 01 01 33 B3）
    /// - 读整板状态： 80 AA 00 33 BCC （例：80 01 00 33 B2；24 路返回 3 个状态字节）
    /// - 批量开多个通道：90 AA S1 S2 S3 BCC（S1=1～8 位图，S2=9～16，S3=17～24）
    /// </summary>
    public class LockControlService : ILockControlService
    {
        private readonly IOptions<AppSettings> _appSettings;
        private readonly ILogger<LockControlService> _logger;

        // 用于通知底层严重错误
        public event EventHandler<string>? ErrorOccurred;

        // ========= 配置载体 =========
        private record BoardCfg(int Address, string SerialPort);
        private LockControllerSettings _config => _appSettings.Value.LockController;
        private List<BoardCfg> _boards = new();
        private int DefaultBoardAddr => _boards.FirstOrDefault()?.Address ?? (int)1;

        // ========= 端口池 & 互斥 =========
        private class PortEntry
        {
            public SerialPort? Port { get; set; }
            public readonly SemaphoreSlim Locker = new(1, 1);
            public string CanonicalName = ""; // 实际成功打开的端口名（可能是短名）
        }

        private readonly ConcurrentDictionary<string, PortEntry> _portPool = new(StringComparer.OrdinalIgnoreCase);

        // 默认重试次数
        private const int DefaultRetries = 1;

        public LockControlService(
            IOptions<AppSettings> appSettings,
            ILogger<LockControlService> logger)
        {
            _appSettings = appSettings;
            _logger = logger;
            LoadConfig();
        }

        #region 配置装载
        private void LoadConfig()
        {
            if (_config == null || _config.Boards.Count == 0)
            {
                _logger.LogError("锁控板配置为空，请检查appsettings.json中的LockController:Boards配置");
                return;
            }

            // 加载板配置
            foreach (var b in _config.Boards)
            {
                var addr = (byte)b.Address;
                var port = b.SerialPort;
                if (addr == 0 || string.IsNullOrWhiteSpace(port))
                    continue;
                _boards.Add(new BoardCfg(addr, port.Trim()));
            }

            if (_boards.Count == 0)
            {
                _logger.LogWarning("LockController:Boards 为空，锁控服务将无法工作");
            }
        }
        #endregion

        #region 测试连接
        /// <summary>
        /// 测试连接
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                _logger.LogInformation("开始测试锁控设备连接");
                // 使用现有的CheckConnectionAsync方法进行测试
                bool connectionOk = await CheckConnectionAsync();
                if (connectionOk)
                {
                    _logger.LogInformation("锁控设备连接正常");
                }
                else
                {
                    _logger.LogWarning("锁控设备连接测试失败");
                }
                return connectionOk;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "锁控设备连接测试时发生异常");
                return false;
            }
        }
        #endregion

        #region 获取配置
        /// <summary>
        /// 获取配置中的所有板地址和串口
        /// </summary>
        public IReadOnlyList<(int Address, string SerialPort)> GetConfiguredBoards() =>
            _boards.Select(b => (b.Address, b.SerialPort)).ToList();
        #endregion

        #region 端口池化 + 长期开启
        /// <summary>
        /// 确保所有涉及的串口都已打开
        /// </summary>
        public async Task<IReadOnlyDictionary<string, bool>> EnsureAllPortsOpenAsync(bool announce = false)
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var distinctPorts = _boards.Select(b => b.SerialPort).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var port in distinctPorts)
            {
                var ok = await EnsurePortOpenAsync(port, announce);
                result[port] = ok;
            }

            return result;
        }
        #endregion

        #region 初始化锁控板
        /// <summary>
        /// 初始化锁控板
        /// </summary>
        public async Task<bool> InitializeLockControlAsync()
        {
            _logger.LogInformation("正在初始化锁控板服务...");
            if (_boards.Count == 0)
            {
                _logger.LogError("未在配置中找到锁控板信息（LockController:Boards）");
                return false;
            }

            var openMap = await EnsureAllPortsOpenAsync(announce: true);
            var anySuccess = openMap.Values.Any(v => v);

            if (!anySuccess)
            {
                _logger.LogError("未能成功打开任何锁控串口，请检查硬件连接与权限（dialout 组）");
                return false;
            }

            // 做一次最基本的链路验证（读默认板的 1 号通道状态）
            var okLink = await CheckConnectionAsync();
            if (!okLink)
            {
                _logger.LogWarning("串口已打开，但未获得状态回包，请检查接线/地址/波特率");
                // 这里保持返回 true，让后续流程不被硬阻断；若想严格，可改为 false。
            }

            _logger.LogInformation("锁控板串口初始化完成");
            return true;
        }
        #endregion

        #region 锁控板连接测试
        /// <summary>
        /// 检查锁控板连接
        /// </summary>
        public async Task<bool> CheckConnectionAsync(CancellationToken ct = default)
        {
            if (_boards.Count == 0)
                return false;

            var boardAddr = DefaultBoardAddr;
            var status = await ReadOneStatusAsync(boardAddr, 1, ct);
            // 只要返回不是null（说明收到回包、能正常解析），即判定串口通讯没问题
            var text = status == true ? "开/导通" : status == false ? "关/断开" : "通讯失败";
            _logger.LogInformation($"锁1状态: {text}");
            return status != null;
        }
        #endregion

        #region 开锁
        /// <summary>
        /// 打开指定通道（默认300ms脉冲）
        /// </summary>
        public Task<bool> OpenLockAsync(int channel) => OpenLockAsync(DefaultBoardAddr, channel);
        #endregion

        #region 全开
        /// <summary>
        /// 一键清柜（全开所有锁）
        /// </summary>
        public async Task<bool> OpenAllLocksAsync(CancellationToken ct = default)
        {
            if (_boards.Count == 0)
            {
                _logger.LogError("未在配置中找到锁控板信息（LockController:Boards）");
                return false;
            }

            var openMap = await EnsureAllPortsOpenAsync(announce: true);
            if (!openMap.Values.Any(v => v))
            {
                _logger.LogError("未能成功打开任何串口，无法清柜");
                return false;
            }

            bool allOk = true;
            // 按板地址排序，确保顺序执行
            var sortedBoards = _boards.OrderBy(b => b.Address).ToList();

            foreach (var b in sortedBoards)
            {
                ct.ThrowIfCancellationRequested();
                _logger.LogInformation($"向板{b.Address}发送全开命令");
                var frame = FrameOpenAll(b.Address);
                var resp = await SendAsync(b.Address, frame, expectMinLen: 5, retry: DefaultRetries, ct);
                // 增加板间延迟，确保前一块板执行完成
                await Task.Delay(300, ct);

                var ok = resp != null;
                if (!ok)
                {
                    _logger.LogWarning($"板{b.Address} 全开指令未收到回读");
                    allOk = false;
                }
                else
                {
                    _logger.LogInformation($"板{b.Address} 全开指令执行完成");
                }
            }

            return allOk;
        }
        #endregion

        #region 打开指定板的指定通道
        /// <summary>
        /// 打开指定板的指定通道（默认300ms脉冲）
        /// 根据配置的反馈模式（OpeningFeedback或ClosingFeedback）决定开锁结果
        /// </summary>
        public async Task<bool> OpenLockAsync(int boardAddr, int channel)
        {
            if (channel < 1 || channel > 99)
            {
                _logger.LogError($"非法通道号：{channel}");
                return false;
            }

            var board = _boards.FirstOrDefault(x => x.Address == boardAddr);
            if (board is null)
            {
                _logger.LogError($"未找到板地址 {boardAddr} 的配置");
                return false;
            }

            if (!await EnsurePortOpenAsync(board.SerialPort))
                return false;

            // 单开默认 300ms：8A AA CC 11 BCC
            var frame = FrameOpenOnePulse((byte)boardAddr, (byte)channel);
            var resp = await SendAsync((byte)boardAddr, frame, expectMinLen: 5, retry: DefaultRetries, default);

            if (resp == null || resp.Length < 5)
            {
                _logger.LogWarning($"板{boardAddr} 通道{channel} 开锁指令已发送，未收到回读");
                return false;
            }

            _logger.LogDebug($"开锁命令响应: {BitConverter.ToString(resp).Replace("-", " ")}");

            // 根据协议文档第五点：开锁反馈数据格式
            // 命令头(1) | 板地址(1) | 锁地址(1) | 开锁状态(1) | 校验(1)
            byte stateByte = resp[3]; // 第4字节为开锁状态
            _logger.LogDebug($"板{boardAddr} 通道{channel} 开锁反馈原始状态字节: 0x{stateByte:X2}");

            bool success = false;
            if (_config.FeedbackMode == FeedbackMode.ClosingFeedback)
            {
                // 关门反馈锁逻辑：状态00表示开锁成功，11表示开锁失败
                if (stateByte == 0x00)
                {
                    success = true; // 开锁成功
                    _logger.LogDebug($"板{boardAddr} 通道{channel} 开锁结果(关门反馈): 成功");
                }
                else if (stateByte == 0x11)
                {
                    success = false; // 开锁失败
                    _logger.LogDebug($"板{boardAddr} 通道{channel} 开锁结果(关门反馈): 失败");
                }
            }
            else // FeedbackMode.OpeningFeedback
            {
                // 开门反馈锁逻辑：状态11表示开锁成功，00表示开锁失败
                if (stateByte == 0x11)
                {
                    success = true; // 开锁成功
                    _logger.LogDebug($"板{boardAddr} 通道{channel} 开锁结果(开门反馈): 成功");
                }
                else if (stateByte == 0x00)
                {
                    success = false; // 开锁失败
                    _logger.LogDebug($"板{boardAddr} 通道{channel} 开锁结果(开门反馈): 失败");
                }
            }

            if (success)
            {
                _logger.LogInformation($"板{boardAddr} 通道{channel} 开锁成功");
            }
            else
            {
                _logger.LogWarning($"板{boardAddr} 通道{channel} 开锁失败，状态字节: 0x{stateByte:X2}");
            }

            return success;
        }
        #endregion

        #region 读取单个锁状态
        /// <summary>
        /// 读取指定板单个锁通道状态（协议80 AA CC 33 BCC）
        /// 根据配置的反馈模式（OpeningFeedback或ClosingFeedback）决定状态逻辑
        /// </summary>
        public async Task<bool?> ReadOneStatusAsync(int boardAddr, int channel, CancellationToken ct = default)
        {
            if (channel < 1 || channel > 99)
                throw new ArgumentOutOfRangeException(nameof(channel), "通道号应在 1～99");

            var frame = FrameReadOne((byte)boardAddr, (byte)channel);
            var resp = await SendAsync((byte)boardAddr, frame, expectMinLen: 5, retry: DefaultRetries, ct);

            if (resp == null || resp.Length < 5)
                return null;

            _logger.LogDebug($"读单锁状态响应: {BitConverter.ToString(resp).Replace("-", " ")}");

            // 根据协议文档第六点：读锁状态命令反馈
            // 命令头(1) | 板地址(1) | 锁地址(1) | 反馈的锁状态(1) | 校验(1)
            byte stateByte = resp[3]; // 第4字节为锁状态
            _logger.LogDebug($"板{boardAddr} 通道{channel} 原始状态字节: 0x{stateByte:X2}");

            bool? status = null;
            if (_config.FeedbackMode == FeedbackMode.ClosingFeedback)
            {
                // 关门反馈：状态00表示打开，11表示关闭
                if (stateByte == 0x00)
                {
                    status = true; // 打开
                    _logger.LogDebug($"板{boardAddr} 通道{channel} 状态(关门反馈): 打开");
                }
                else if (stateByte == 0x11)
                {
                    status = false; // 关闭
                    _logger.LogDebug($"板{boardAddr} 通道{channel} 状态(关门反馈): 关闭");
                }
            }
            else // FeedbackMode.OpeningFeedback
            {
                // 开门反馈：状态11表示打开，00表示关闭
                if (stateByte == 0x11)
                {
                    status = true; // 打开
                    _logger.LogDebug($"板{boardAddr} 通道{channel} 状态(开门反馈): 打开");
                }
                else if (stateByte == 0x00)
                {
                    status = false; // 关闭
                    _logger.LogDebug($"板{boardAddr} 通道{channel} 状态(开门反馈): 关闭");
                }
            }

            if (status == null)
            {
                _logger.LogWarning($"板{boardAddr} 通道{channel} 状态未知: 0x{stateByte:X2}");
                return null;
            }

            _logger.LogInformation($"板{boardAddr} 通道{channel} 最终状态: {(status.Value ? "打开" : "关闭")}");
            return status;
        }
        #endregion

        #region 读取所有锁状态
        /// <summary>
        /// 读取指定板整板所有锁状态（协议80 AA 00 33 BCC）
        /// 返回包含板地址、通道地址和状态三个值的元组列表
        /// 根据配置的反馈模式（OpeningFeedback或ClosingFeedback）决定状态逻辑
        /// </summary>
        /// <param name="boardAddr">板地址</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>包含(板地址, 通道号, 状态)的元组列表，null表示读取失败</returns>
        public async Task<List<(int BoardAddress, int Channel, bool Status)>?> ReadAllStatusAsync(int boardAddr, CancellationToken ct = default)
        {
            _logger.LogDebug($"开始读取板{boardAddr}的所有锁状态");

            // 发读取指令
            var frame = FrameReadAll((byte)boardAddr);

            // 最小期望回包：80 AA S1 S2 S3 33 XX（标准24路）或 80 AA S1 S2 S3 S4 33 XX（扩展）
            var resp = await SendAsync((byte)boardAddr, frame, expectMinLen: 7, retry: DefaultRetries, ct);

            if (resp == null || resp.Length < 7)
            {
                _logger.LogWarning($"读取板{boardAddr}所有锁状态失败，响应为空或长度不足");
                return null;
            }

            _logger.LogDebug($"收到板{boardAddr}的整板状态响应: {BitConverter.ToString(resp).Replace("-", " ")}");
            _logger.LogDebug($"整板状态响应长度: {resp.Length}");

            try
            {
                // 验证响应帧头
                if (resp[0] != 0x80 || resp[1] != (byte)boardAddr || resp[resp.Length - 2] != 0x33)
                {
                    _logger.LogWarning($"板{boardAddr}状态响应帧格式错误");
                    return null;
                }

                // 验证BCC校验
                byte receivedBcc = resp[resp.Length - 1];
                byte calculatedBcc = Bcc(resp.AsSpan(0, resp.Length - 1), resp.Length - 1);

                if (receivedBcc != calculatedBcc)
                {
                    _logger.LogWarning($"板{boardAddr}状态响应BCC校验失败，期望: {calculatedBcc:X2}，实际: {receivedBcc:X2}");
                    return null;
                }

                // 根据实际响应长度确定状态字节数
                // 标准24路：80 AA S3 S2 S1 33 BCC (7字节)
                // 扩展32路：80 AA S4 S3 S2 S1 33 BCC (8字节)
                int stateByteCount = resp.Length - 4; // 减去帧头(2)、功能码(1)、BCC(1)
                int channelCount = stateByteCount * 8;
                _logger.LogDebug($"板{boardAddr}检测到 {stateByteCount} 个状态字节，共 {channelCount} 个通道");

                var result = new List<(int BoardAddress, int Channel, bool Status)>();

                // 动态解析状态字节，适应不同的响应格式
                for (int channelNumber = 1; channelNumber <= channelCount; channelNumber++)
                {
                    // 确定该通道号对应的状态字节和位索引
                    // 根据协议：状态字节从高地址到低地址，通道从高编号到小编号
                    int byteIndex, bitIndex;

                    if (stateByteCount == 3)
                    {
                        // 标准24路：状态3=resp[2](1-8), 状态2=resp[3](9-16), 状态1=resp[4](17-24)
                        if (channelNumber <= 8)
                        {
                            byteIndex = 4; // 状态3
                            bitIndex = channelNumber - 1;
                        }
                        else if (channelNumber <= 16)
                        {
                            byteIndex = 3; // 状态2
                            bitIndex = channelNumber - 9;
                        }
                        else
                        {
                            byteIndex = 2; // 状态1
                            bitIndex = channelNumber - 17;
                        }
                    }
                    else if (stateByteCount == 4)
                    {
                        // 扩展32路或其他变体
                        if (channelNumber <= 8)
                        {
                            byteIndex = 5; // 状态4
                            bitIndex = channelNumber - 1;
                        }
                        else if (channelNumber <= 16)
                        {
                            byteIndex = 4; // 状态3
                            bitIndex = channelNumber - 9;
                        }
                        else if (channelNumber <= 24)
                        {
                            byteIndex = 3; // 状态2
                            bitIndex = channelNumber - 17;
                        }
                        else
                        {
                            byteIndex = 2; // 状态1
                            bitIndex = channelNumber - 25;
                        }
                    }
                    else
                    {
                        // 其他情况，使用通用算法
                        int bytesPerGroup = 8;
                        int groupIndex = (channelNumber - 1) / bytesPerGroup;
                        byteIndex = 2 + (stateByteCount - 1 - groupIndex);
                        bitIndex = (channelNumber - 1) % bytesPerGroup;
                    }

                    if (byteIndex >= resp.Length - 2) // 确保不超出范围（排除功能码和BCC）
                    {
                        _logger.LogWarning($"状态字节索引 {byteIndex} 超出响应数组范围");
                        break;
                    }

                    // 某位为1表示通道对应为开（已导通）
                    bool status = (resp[byteIndex] & (1 << bitIndex)) != 0;

                    // 根据反馈模式调整状态
                    if (_config.FeedbackMode == FeedbackMode.OpeningFeedback)
                    {
                        status = !status; // 反转状态
                    }

                    result.Add((boardAddr, channelNumber, status));
                    _logger.LogTrace($"板{boardAddr} 通道{channelNumber} 状态: {(status ? "开" : "关")} (字节[{byteIndex}]位{bitIndex})");
                }

                _logger.LogInformation($"成功读取板{boardAddr}的 {result.Count} 个通道状态");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"解析板{boardAddr}所有锁状态时发生异常");
                return null;
            }
        }
        #endregion

        #region 销毁
        public void Dispose()
        {
            foreach (var kv in _portPool)
            {
                try
                {
                    kv.Value.Port?.Dispose();
                }
                catch
                {
                    // 忽略异常
                }
            }
        }
        #endregion

        #region 确保指定端口处于打开状态
        /// <summary>
        /// 确保指定端口处于打开状态；兼容 Linux：既尝试 "/dev/ttyS9" 也尝试 "ttyS9"。
        /// </summary>
        private async Task<bool> EnsurePortOpenAsync(string configuredPort, bool announce = false)
        {
            var entry = _portPool.GetOrAdd(configuredPort, _ => new PortEntry());

            if (entry.Port is { IsOpen: true })
            {
                if (announce)
                    _logger.LogInformation($"串口已打开：{entry.CanonicalName}");
                return true;
            }

            try
            {
                entry.Port?.Dispose();
            }
            catch
            {
                // 忽略异常
            }

            var candidates = new List<string> { configuredPort };
            if (configuredPort.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase))
                candidates.Add(configuredPort.Substring("/dev/".Length));

            Exception? lastEx = null;

            foreach (var name in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    // 从配置中获取串口参数
                    var parity = Enum.TryParse(_config.Parity, out Parity p) ? p : Parity.None;
                    var stopBits = Enum.TryParse(_config.StopBits, out StopBits sb) ? sb : StopBits.One;

                    _logger.LogInformation("正在打开串口: {PortName}，波特率: {BaudRate}，校验位: {Parity}，数据位: {DataBits}，停止位: {StopBits}，RTS方向控制: {UseRtsDirection}",
                        name, _config.BaudRate, parity, _config.DataBits, stopBits, _config.UseRtsDirection);

                    var sp = new SerialPort(name, _config.BaudRate, parity, _config.DataBits, stopBits)
                    {
                        ReadTimeout = _config.ReadTimeout,
                        WriteTimeout = _config.WriteTimeout,
                        DtrEnable = false,
                        RtsEnable = _config.UseRtsDirection,
                        Handshake = Handshake.None,
                        Encoding = Encoding.ASCII
                    };

                    sp.Open();
                    await Task.Delay(50);
                    entry.Port = sp;
                    entry.CanonicalName = name;
                    _logger.LogInformation("串口打开成功：{PortName}（{BaudRate},{Parity},{DataBits},{StopBits}，RTS方向控制: {UseRtsDirection}）",
                        name, _config.BaudRate, parity, _config.DataBits, stopBits, _config.UseRtsDirection);
                    return true;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    _logger.LogError(ex, "Open serial port {PortName} failed", name);
                    var hint = ex.Message.Contains("Permission", StringComparison.OrdinalIgnoreCase) ?
                        "（权限不足：确认用户在 dialout 组并重新登录）" :
                        ex.Message.Contains("busy", StringComparison.OrdinalIgnoreCase) ?
                        "（设备占用：确认没有其它程序占用该串口）" :
                        "";
                    _logger.LogError(
                        "锁控串口 {PortName} 打开失败: {ErrorMessage}{Hint}",
                        name, ex.Message, hint);
                }
            }

            ErrorOccurred?.Invoke(this, $"串口 {configuredPort} 打开失败：{lastEx?.Message}");
            return false;
        }
        #endregion

        #region 底层发送与收包方法
        /// <summary>
        /// 底层发送与收包方法（严格半双工串行化）：
        /// - 根据板地址查找端口；
        /// - 使用该端口对应的 SemaphoreSlim 进行互斥；
        /// - 发送帧 → 等待 Tx->Rx 换向 → 读取直到达到期望最小长度或超时；
        /// - 异常时自动重开端口并按 retry 重试。
        /// </summary>
        private async Task<byte[]?> SendAsync(int boardAddr, byte[] frame, int expectMinLen, int retry, CancellationToken ct)
        {
            // 1) 根据板地址映射串口
            var b = _boards.FirstOrDefault(x => x.Address == (byte)boardAddr);
            if (b is null)
            {
                _logger.LogError($"未找到板地址 {boardAddr} 的端口映射");
                return null;
            }

            // 2) 确保端口打开
            if (!_portPool.TryGetValue(b.SerialPort, out var entry) || entry.Port is not { IsOpen: true })
            {
                if (!await EnsurePortOpenAsync(b.SerialPort, announce: false))
                    return null;
                entry = _portPool[b.SerialPort];
            }

            var sp = entry.Port!;
            var locker = entry.Locker;

            // 3) 重试发送
            for (int attempt = 0; attempt <= retry; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                await locker.WaitAsync(ct);

                try
                {
                    // 发送前Hex打印
                    var hexout = BitConverter.ToString(frame).Replace("-", " ");
                    _logger.LogDebug($"[发送] 地址:{boardAddr} 串口:{b.SerialPort} 帧HEX:[{hexout}]");

                    // 清空缓存，避免历史残留影响
                    try
                    {
                        sp.DiscardInBuffer();
                        sp.DiscardOutBuffer();
                    }
                    catch
                    {
                        /* ignore */
                    }

                    // 发送
                    sp.Write(frame, 0, frame.Length);
                    sp.BaseStream.Flush();
                    _logger.LogDebug($"[发送] 实际写入字节数:{frame.Length}");

                    // RS-485 半双工换向延迟
                    await Task.Delay(_config.TxRxTurnaroundMs, ct);

                    // 仅发送，不等待（expectMinLen <= 0）
                    if (expectMinLen <= 0)
                        return Array.Empty<byte>();

                    // 简单同步读取：直到凑够最小长度或超时
                    var start = Environment.TickCount;
                    var deadline = _config.ReadTimeout;
                    var buf = new List<byte>(64);
                    var tmp = new byte[64];

                    while ((Environment.TickCount - start) < deadline)
                    {
                        while (sp.BytesToRead > 0)
                        {
                            var read = sp.Read(tmp, 0, Math.Min(tmp.Length, sp.BytesToRead));
                            if (read > 0)
                            {
                                buf.AddRange(tmp.AsSpan(0, read).ToArray());
                                var hexin = BitConverter.ToString(buf.ToArray()).Replace("-", " ");
                                _logger.LogDebug($"[收包] 当前已收:{buf.Count}字节，HEX:[{hexin}]");

                                if (buf.Count >= expectMinLen)
                                {
                                    _logger.LogInformation($"[收包] 收到{buf.Count}字节满足长度，HEX:[{hexin}]");
                                    return buf.ToArray();
                                }
                            }
                        }
                        await Task.Delay(10, ct);
                    }

                    _logger.LogWarning($"板{boardAddr} 等待回包超时（期望≥{expectMinLen}字节，已收:{buf.Count}）");
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning($"板{boardAddr} 通讯超时");
                }
                catch (Exception ex)
                {
                    // 读写异常：记录并尝试重开端口
                    _logger.LogError(ex, "SendAsync failed for board {Board}", boardAddr);
                    try
                    {
                        sp.Close();
                    }
                    catch
                    {
                        // 忽略异常
                    }
                    await EnsurePortOpenAsync(b.SerialPort, announce: false);

                    // 重新抓取端口引用
                    if (_portPool.TryGetValue(b.SerialPort, out var reEntry) && reEntry.Port is { IsOpen: true })
                    {
                        sp = reEntry.Port;
                        locker = reEntry.Locker;
                    }
                }
                finally
                {
                    try
                    {
                        await Task.Delay(_config.InterCommandDelayMs, ct);
                    }
                    catch
                    {
                        // 忽略异常
                    }
                    locker.Release();
                }
            }

            return null;
        }
        #endregion

        #region 协议帧（全部按文档）
        /// <summary>
        /// 全开：8A AA 00 11 BCC
        /// 例：板1 -> 8A 01 00 11 9A（0x8A^0x01^0x00^0x11=0x9A）
        /// </summary>
        private static byte[] FrameOpenAll(int boardAddr)
        {
            var raw = new byte[] { 0x8A, (byte)boardAddr, 0x00, 0x11, 0x00 };
            raw[^1] = Bcc(raw, raw.Length - 1);
            return raw;
        }

        /// <summary>
        /// 单开（默认300ms）：8A AA CC 11 BCC
        /// 若需 1..9 秒：8A AA CC 11 SS BCC
        /// </summary>
        private static byte[] FrameOpenOnePulse(byte boardAddr, byte channel)
        {
            var raw = new byte[] { 0x8A, boardAddr, channel, 0x11, 0x00 };
            raw[^1] = Bcc(raw, raw.Length - 1);
            return raw;
        }

        /// <summary>
        /// 读单锁状态：80 AA CC 33 BCC（例：80 01 01 33 B3）
        /// </summary>
        private static byte[] FrameReadOne(byte boardAddr, byte channel)
        {
            var raw = new byte[] { 0x80, boardAddr, channel, 0x33, 0x00 };
            raw[^1] = Bcc(raw, raw.Length - 1);
            return raw;
        }

        /// <summary>
        /// 读整板状态：80 AA 00 33 BCC（例：80 01 00 33 B2；24 路返回 3 个状态字节）
        /// </summary>
        private static byte[] FrameReadAll(byte boardAddr)
        {
            var raw = new byte[] { 0x80, boardAddr, 0x00, 0x33, 0x00 };
            raw[^1] = Bcc(raw, raw.Length - 1);
            return raw;
        }

        /// <summary>
        /// 异或校验（BCC）：对前 count 个字节逐字节 XOR。
        /// </summary>
        private static byte Bcc(ReadOnlySpan<byte> data, int count)
        {
            byte x = 0;
            for (int i = 0; i < count; i++)
                x ^= data[i];
            return x;
        }
        #endregion
    }
}