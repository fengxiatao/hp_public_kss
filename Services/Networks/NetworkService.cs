using Avalonia.Threading;
using FaceLocker.Models.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 网络通信服务实现，负责与服务器建立连接、发送心跳、接收指令和数据
    /// </summary>
    public class NetworkService : INetworkService, IDisposable
    {
        #region 私有字段
        private readonly ILogger<NetworkService> _logger;
        private readonly AppSettings _appSettings;
        private readonly IDataSyncService _dataSyncService;
        private readonly ILockControlService _lockControlService;

        private Socket? _socket;
        private readonly string _serverIp;
        private readonly int _port;
        private readonly string _deviceName;

        /// <summary>
        /// 心跳失败次数
        /// </summary>
        private int _heartbeatFailCount = 0;

        /// <summary>
        /// 心跳定时器
        /// </summary>
        private Timer? _heartbeatTimer;

        /// <summary>
        /// 重连定时器
        /// </summary>
        private Timer? _reconnectTimer;

        /// <summary>
        /// 连接状态标志
        /// </summary>
        private bool _isConnected = false;

        /// <summary>
        /// 最后连接时间
        /// </summary>
        private DateTime? _lastConnectedTime = null;

        /// <summary>
        /// 最大心跳失败次数
        /// </summary>
        private readonly int _maxHeartbeatFailures = 5;

        /// <summary>
        /// 重连间隔秒数
        /// </summary>
        private readonly int _reconnectIntervalSeconds = 10;

        /// <summary>
        /// 心跳间隔秒数
        /// </summary>
        private readonly int _heartbeatIntervalSeconds = 30;

        /// <summary>
        /// 接收数据缓冲区大小
        /// </summary>
        private const int BufferSize = 409600;

        /// <summary>
        /// 接收数据缓冲区
        /// </summary>
        private readonly byte[] _receiveBuffer = new byte[BufferSize];

        /// <summary>
        /// 统计信息
        /// </summary>
        private readonly NetworkStatistics _statistics = new NetworkStatistics();

        /// <summary>
        /// 重连尝试次数
        /// </summary>
        private int _reconnectAttemptCount = 0;

        /// <summary>
        /// 最大重连尝试次数（0表示无限重试）
        /// </summary>
        private const int MaxReconnectAttempts = 0;

        /// <summary>
        /// 是否正在尝试重连
        /// </summary>
        private bool _isReconnecting = false;

        /// <summary>
        /// 重连锁对象，防止重复重连
        /// </summary>
        private readonly object _reconnectLock = new object();

        #endregion

        #region 公共属性

        /// <summary>
        /// 获取是否已连接到服务器
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// 获取最后连接时间
        /// </summary>
        public DateTime? LastConnectedTime => _lastConnectedTime;

        /// <summary>
        /// 获取心跳失败次数
        /// </summary>
        public int HeartbeatFailCount => _heartbeatFailCount;

        /// <summary>
        /// 获取重连尝试次数
        /// </summary>
        public int ReconnectAttemptCount => _reconnectAttemptCount;

        /// <summary>
        /// 获取是否正在重连
        /// </summary>
        public bool IsReconnecting => _isReconnecting;

        #endregion

        #region 事件

        /// <summary>
        /// 连接状态变更事件
        /// </summary>
        public event EventHandler<ConnectionStatusChangedEventArgs>? ConnectionStatusChanged;

        /// <summary>
        /// 数据接收事件
        /// </summary>
        public event EventHandler<DataReceivedEventArgs>? DataReceived;

        /// <summary>
        /// 指令接收事件
        /// </summary>
        public event EventHandler<CommandReceivedEventArgs>? CommandReceived;

        /// <summary>
        /// 重连状态变更事件
        /// </summary>
        public event EventHandler<ReconnectStatusChangedEventArgs>? ReconnectStatusChanged;

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化网络服务
        /// </summary>
        public NetworkService(
            IOptions<AppSettings> appSettings,
            ILogger<NetworkService> logger,
            IDataSyncService dataSyncService,
            ILockControlService lockControlService)
        {
            _appSettings = appSettings.Value;
            _logger = logger;
            _dataSyncService = dataSyncService;

            var serverSettings = _appSettings.Server;
            _serverIp = serverSettings.ServerAddress;
            _port = serverSettings.ServerPort;
            _deviceName = serverSettings.DeviceName;

            // 检查服务器配置
            if (string.IsNullOrWhiteSpace(_serverIp) || _port <= 0)
            {
                _logger.LogError("网络服务配置无效，服务器地址：{ServerIp}，端口：{Port}", _serverIp, _port);
                throw new ArgumentException("网络服务配置无效，请检查服务器地址和端口配置");
            }

            _logger.LogInformation("网络服务初始化完成，服务器地址：{ServerIp}，端口：{Port}，设备名称：{DeviceName}", _serverIp, _port, _deviceName);
            _lockControlService = lockControlService;
        }

        #endregion

        #region 连接管理

        /// <summary>
        /// 初始化网络服务
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            try
            {
                _logger.LogInformation("开始初始化网络服务");

                // 再次验证网络配置
                if (string.IsNullOrWhiteSpace(_serverIp) || _port <= 0)
                {
                    _logger.LogError("网络配置无效，服务器地址：{ServerIp}，端口：{Port}", _serverIp, _port);
                    return await Task.FromResult(false);
                }

                // 设置数据同步服务的网络服务引用
                _dataSyncService.SetNetworkService(this);

                _logger.LogInformation("网络服务初始化完成");
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "网络服务初始化失败");
                return await Task.FromResult(false);
            }
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            if (_isConnected)
            {
                _logger.LogWarning("已经连接到服务器，无需重复连接");
                return true;
            }

            try
            {
                _logger.LogInformation("正在连接到服务器：{ServerIp}:{Port}", _serverIp, _port);

                _isConnected = false;
                _heartbeatFailCount = 0;

                // 创建新的Socket
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                // 设置连接超时
                var connectTask = _socket.ConnectAsync(_serverIp, _port);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(3));
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _logger.LogWarning("连接服务器超时：{ServerIp}:{Port}", _serverIp, _port);
                    await DisconnectAsync();
                    return false;
                }

                await connectTask; // 确保连接任务完成

                // 验证连接是否真正建立
                if (!_socket.Connected)
                {
                    _logger.LogError("Socket连接状态异常，连接未成功建立");
                    await DisconnectAsync();
                    return false;
                }

                _isConnected = true;
                _lastConnectedTime = DateTime.Now;
                _statistics.TotalConnections++;
                _statistics.LastConnectedTime = _lastConnectedTime;
                _reconnectAttemptCount = 0; // 重置重连计数

                _logger.LogInformation("成功连接到服务器：{ServerIp}:{Port}", _serverIp, _port);

                // 清空接收缓冲区
                _receiveDataBuffer.Clear();

                // 启动心跳机制
                StartHeartbeat();

                // 启动接收数据循环
                _ = Task.Run(ReceiveDataLoop);

                // 触发连接状态变更事件
                OnConnectionStatusChanged(true, "连接成功");

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "连接服务器失败：{ServerIp}:{Port}", _serverIp, _port);
                await DisconnectAsync();
                return false;
            }
        }

        /// <summary>
        /// 断开与服务器的连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                _logger.LogInformation("正在断开与服务器的连接");

                // 停止心跳
                StopHeartbeat();

                // 停止重连
                StopReconnectTimer();

                // 重置重连状态
                _isReconnecting = false;

                // 关闭Socket
                if (_socket != null)
                {
                    if (_socket.Connected)
                    {
                        _socket.Shutdown(SocketShutdown.Both);
                    }
                    _socket.Close();
                    _socket.Dispose();
                    _socket = null;
                }

                _isConnected = false;
                _statistics.TotalDisconnections++;
                _statistics.LastDisconnectedTime = DateTime.Now;

                _logger.LogInformation("已断开与服务器的连接");

                // 触发连接状态变更事件
                OnConnectionStatusChanged(false, "连接断开");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "断开连接时发生异常");
            }
        }

        /// <summary>
        /// 重新连接到服务器
        /// </summary>
        public async Task<bool> ReconnectAsync()
        {
            _logger.LogInformation("尝试重新连接到服务器");

            await DisconnectAsync();
            await Task.Delay(TimeSpan.FromSeconds(2)); // 等待2秒后重连

            return await ConnectAsync();
        }

        #endregion

        #region 断网重试机制
        /// <summary>
        /// 启动断网重试机制
        /// </summary>
        private void StartReconnectMechanism()
        {
            lock (_reconnectLock)
            {
                if (_isReconnecting)
                {
                    _logger.LogDebug("重连机制已经在运行中，跳过重复启动");
                    return;
                }

                _isReconnecting = true;
                _reconnectAttemptCount++;

                _logger.LogInformation("启动断网重试机制，第 {AttemptCount} 次重连尝试，重连间隔：{Interval} 秒",
                    _reconnectAttemptCount, _reconnectIntervalSeconds);

                // 触发重连状态变更事件
                OnReconnectStatusChanged(true, _reconnectAttemptCount, "开始重连尝试");

                // 启动重连定时器
                StartReconnectTimer();
            }
        }

        /// <summary>
        /// 停止断网重试机制
        /// </summary>
        private void StopReconnectMechanism()
        {
            lock (_reconnectLock)
            {
                if (!_isReconnecting)
                {
                    return;
                }

                _isReconnecting = false;
                _logger.LogInformation("停止断网重试机制，总共进行了 {TotalAttempts} 次重连尝试", _reconnectAttemptCount);

                // 触发重连状态变更事件
                OnReconnectStatusChanged(false, _reconnectAttemptCount, "重连机制已停止");

                // 停止重连定时器
                StopReconnectTimer();
            }
        }

        /// <summary>
        /// 检查是否应该继续重连
        /// </summary>
        private bool ShouldContinueReconnecting()
        {
            if (MaxReconnectAttempts > 0 && _reconnectAttemptCount >= MaxReconnectAttempts)
            {
                _logger.LogWarning("已达到最大重连尝试次数 {MaxAttempts}，停止重连", MaxReconnectAttempts);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 执行重连操作
        /// </summary>
        private async Task PerformReconnectAsync()
        {
            try
            {
                _logger.LogInformation("执行第 {AttemptCount} 次重连操作，目标服务器：{ServerIp}:{Port}",
                    _reconnectAttemptCount, _serverIp, _port);

                bool success = await ConnectAsync();

                if (success)
                {
                    _logger.LogInformation("第 {AttemptCount} 次重连成功", _reconnectAttemptCount);
                    StopReconnectMechanism();
                    OnReconnectStatusChanged(false, _reconnectAttemptCount, "重连成功");
                }
                else
                {
                    _logger.LogWarning("第 {AttemptCount} 次重连失败，{Interval} 秒后将再次尝试",
                        _reconnectAttemptCount, _reconnectIntervalSeconds);

                    OnReconnectStatusChanged(true, _reconnectAttemptCount, $"第{_reconnectAttemptCount}次重连失败");

                    // 检查是否应该继续重连
                    if (!ShouldContinueReconnecting())
                    {
                        StopReconnectMechanism();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行重连操作时发生异常，第 {AttemptCount} 次尝试", _reconnectAttemptCount);

                OnReconnectStatusChanged(true, _reconnectAttemptCount, $"重连异常：{ex.Message}");

                // 检查是否应该继续重连
                if (!ShouldContinueReconnecting())
                {
                    StopReconnectMechanism();
                }
            }
        }

        #endregion

        #region 发送数据到服务器
        /// <summary>
        /// 发送数据到服务器
        /// </summary>
        public async Task<bool> SendDataAsync(string data)
        {
            if (!_isConnected || _socket == null)
            {
                _logger.LogWarning("未连接到服务器，无法发送数据");
                return false;
            }

            try
            {
                // 在数据末尾添加换行符，满足Java服务器要求
                string dataWithNewLine = data + "\n";
                byte[] buffer = Encoding.UTF8.GetBytes(dataWithNewLine);

                _logger.LogDebug("发送数据，长度：{Length} 字节", buffer.Length);
                int bytesSent = await _socket.SendAsync(new ArraySegment<byte>(buffer), SocketFlags.None);

                if (bytesSent == buffer.Length)
                {
                    _statistics.TotalBytesSent += bytesSent;
                    _logger.LogDebug("发送数据成功，长度：{Length} 字节", bytesSent);
                    return true;
                }
                else
                {
                    _logger.LogWarning("发送数据不完整，预期：{Expected} 字节，实际：{Actual} 字节", buffer.Length, bytesSent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送数据失败");
                await HandleConnectionFailure();
                return false;
            }
        }
        #endregion

        #region 发送协议消息到服务器
        /// <summary>
        /// 发送协议消息到服务器
        /// </summary>
        public async Task<bool> SendProtocolMessageAsync(string messageType, object data)
        {
            try
            {
                var message = new
                {
                    version = "1.0",
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    deviceName = _deviceName,
                    messageType = messageType,
                    data = data
                };

                string jsonData = JsonSerializer.Serialize(message, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                _logger.LogDebug("发送协议消息，类型：{MessageType}", messageType);
                return await SendDataAsync(jsonData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送协议消息失败，消息类型：{MessageType}", messageType);
                return false;
            }
        }
        #endregion

        #region 发送心跳包
        /// <summary>
        /// 发送心跳包
        /// </summary>
        public async Task<bool> SendHeartbeatAsync()
        {
            try
            {
                _statistics.TotalHeartbeats++;

                var heartbeatData = new { status = "HELLO" };
                return await SendProtocolMessageAsync("heartbeat", heartbeatData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送心跳包失败");
                _statistics.HeartbeatFailures++;
                return false;
            }
        }
        #endregion

        #region 发送同步完成确认
        /// <summary>
        /// 发送同步完成确认
        /// </summary>
        public async Task<bool> SendSyncCompleteAsync(string dataType)
        {
            try
            {
                string feedbackMessageType = GetSyncFeedbackMessageType(dataType);
                if (string.IsNullOrEmpty(feedbackMessageType))
                {
                    _logger.LogWarning("未知的数据类型：{DataType}，无法发送同步反馈", dataType);
                    return false;
                }

                _logger.LogInformation("发送 {DataType} 同步完成确认", dataType);

                // 构建完整的同步响应数据
                var feedbackData = new
                {
                    status = "success",
                    message = GetSyncSuccessMessage(dataType),
                    syncTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    added = 0,
                    updated = 0,
                    deleted = 0
                };

                return await SendProtocolMessageAsync(feedbackMessageType, feedbackData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送同步完成确认失败");
                return false;
            }
        }
        #endregion

        #region 发送错误报告
        /// <summary>
        /// 发送错误报告
        /// </summary>
        public async Task<bool> SendErrorReportAsync(string errorCode, string errorMessage)
        {
            try
            {
                _logger.LogWarning("发送错误报告，错误码：{ErrorCode}，错误信息：{ErrorMessage}", errorCode, errorMessage);

                var errorData = new
                {
                    errorCode = errorCode,
                    errorMessage = errorMessage,
                    originalMessageType = "",
                    details = ""
                };

                return await SendProtocolMessageAsync("error", errorData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送错误报告失败");
                return false;
            }
        }
        #endregion

        #region 请求主板数据
        /// <summary>
        /// 请求主板数据
        /// </summary>
        public async Task<bool> RequestBoardsDataAsync()
        {
            try
            {
                _logger.LogInformation("发送主板数据请求");

                var requestData = new
                {
                    // 可以添加请求参数，比如设备信息等
                    deviceInfo = _deviceName,
                    requestTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };

                return await SendProtocolMessageAsync("request_boards", requestData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "发送主板数据请求失败");
                return false;
            }
        }
        #endregion

        #region 状态检查

        /// <summary>
        /// 检查网络连接状态
        /// </summary>
        public async Task<bool> CheckConnectionAsync()
        {
            if (!_isConnected || _socket == null)
            {
                return false;
            }

            try
            {
                // 简单的连接检查：尝试发送一个测试消息
                return await SendHeartbeatAsync();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取网络统计信息
        /// </summary>
        public Task<NetworkStatistics> GetStatisticsAsync()
        {
            return Task.FromResult(_statistics);
        }

        #endregion

        #region 心跳机制

        /// <summary>
        /// 启动心跳机制
        /// </summary>
        private void StartHeartbeat()
        {
            _logger.LogInformation("启动心跳机制，间隔：{Interval} 秒", _heartbeatIntervalSeconds);

            _heartbeatTimer = new Timer(async _ => await SendHeartbeatInternalAsync(),
                null, TimeSpan.Zero, TimeSpan.FromSeconds(_heartbeatIntervalSeconds));
        }

        /// <summary>
        /// 停止心跳机制
        /// </summary>
        private void StopHeartbeat()
        {
            _logger.LogInformation("停止心跳机制");

            _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
        }

        /// <summary>
        /// 内部心跳发送方法
        /// </summary>
        private async Task SendHeartbeatInternalAsync()
        {
            if (!_isConnected)
            {
                _logger.LogDebug("心跳跳过 - 未连接");
                return;
            }

            try
            {
                bool success = await SendHeartbeatAsync();

                if (success)
                {
                    _heartbeatFailCount = 0;
                    _logger.LogDebug("心跳发送成功");
                }
                else
                {
                    _heartbeatFailCount++;
                    _logger.LogWarning("发送心跳失败，连续失败次数：{FailCount} (最大允许：{MaxFailures})",
                        _heartbeatFailCount, _maxHeartbeatFailures);

                    // 连续失败达到阈值则断开连接
                    if (_heartbeatFailCount >= _maxHeartbeatFailures)
                    {
                        _logger.LogWarning("发送心跳连续失败达到阈值，断开连接");
                        await HandleConnectionFailure();
                    }
                }
            }
            catch (Exception ex)
            {
                _heartbeatFailCount++;
                _logger.LogError(ex, "发送心跳异常，连续失败次数：{FailCount}", _heartbeatFailCount);

                if (_heartbeatFailCount >= _maxHeartbeatFailures)
                {
                    await HandleConnectionFailure();
                }
            }
        }

        #endregion

        #region 重连定时器管理

        /// <summary>
        /// 启动重连定时器
        /// </summary>
        private void StartReconnectTimer()
        {
            _logger.LogInformation("启动重连定时器，间隔：{Interval} 秒", _reconnectIntervalSeconds);

            _reconnectTimer = new Timer(async _ => await PerformReconnectAsync(),
                null, TimeSpan.FromSeconds(_reconnectIntervalSeconds), TimeSpan.FromSeconds(_reconnectIntervalSeconds));
        }

        /// <summary>
        /// 停止重连定时器
        /// </summary>
        private void StopReconnectTimer()
        {
            _logger.LogInformation("停止重连定时器");

            _reconnectTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
        }

        #endregion

        #region 连接失败处理

        /// <summary>
        /// 处理连接失败
        /// </summary>
        private async Task HandleConnectionFailure()
        {
            _logger.LogWarning("处理连接失败");

            await DisconnectAsync();

            // 检查是否应该重连
            if (ShouldContinueReconnecting())
            {
                // 启动断网重试机制
                StartReconnectMechanism();
                _logger.LogInformation("已启动断网重试机制，将在 {Interval} 秒后尝试重连", _reconnectIntervalSeconds);
            }
            else
            {
                _logger.LogWarning("已达到最大重连次数，停止重连");
            }
        }
        #endregion

        #region 数据接收处理

        /// <summary>
        /// 接收数据缓冲区
        /// </summary>
        private readonly StringBuilder _receiveDataBuffer = new StringBuilder();

        /// <summary>
        /// 接收数据循环
        /// </summary>
        private async Task ReceiveDataLoop()
        {
            while (_isConnected && _socket != null)
            {
                try
                {
                    int receivedBytes = await _socket.ReceiveAsync(new ArraySegment<byte>(_receiveBuffer), SocketFlags.None);
                    if (receivedBytes == 0)
                    {
                        _logger.LogInformation("服务器主动断开连接");
                        await HandleConnectionFailure();
                        break;
                    }

                    string receivedData = Encoding.UTF8.GetString(_receiveBuffer, 0, receivedBytes);
                    _statistics.TotalBytesReceived += receivedBytes;

                    _logger.LogDebug("收到数据，长度：{Length} 字节", receivedBytes);

                    // 将接收到的数据添加到缓冲区
                    _receiveDataBuffer.Append(receivedData);

                    // 处理缓冲区中的完整消息
                    await ProcessBufferData();
                }
                catch (SocketException sex) when (sex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    _logger.LogWarning("连接被服务器重置");
                    await HandleConnectionFailure();
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "接收数据时发生异常");
                    await HandleConnectionFailure();
                    break;
                }
            }
        }

        /// <summary>
        /// 处理缓冲区中的数据，按换行符分隔消息
        /// </summary>
        private async Task ProcessBufferData()
        {
            string bufferContent = _receiveDataBuffer.ToString();

            // 查找完整的消息（以换行符分隔）
            int newLineIndex;
            while ((newLineIndex = bufferContent.IndexOf('\n')) >= 0)
            {
                // 提取一条完整消息（不包括换行符）
                string completeMessage = bufferContent.Substring(0, newLineIndex).Trim();

                if (!string.IsNullOrEmpty(completeMessage))
                {
                    try
                    {
                        // 检查是否为有效JSON消息
                        if (IsValidJsonMessage(completeMessage))
                        {
                            // 添加消息去重检查
                            if (!IsDuplicateMessage(completeMessage))
                            {
                                // 处理单条消息
                                await ProcessSingleMessageAsync(completeMessage);
                            }
                            else
                            {
                                _logger.LogDebug("忽略重复消息");
                            }
                        }
                        else
                        {
                            _logger.LogWarning("接收到非JSON格式消息，内容：{Message}", completeMessage);

                            // 如果是HTTP请求等非协议数据，忽略并记录
                            if (completeMessage.StartsWith("HTTP/") || completeMessage.StartsWith("GET ") ||
                                completeMessage.StartsWith("POST ") || completeMessage.StartsWith("HEAD "))
                            {
                                _logger.LogInformation("接收到HTTP协议数据，已忽略");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "处理单条消息时发生异常，消息：{Message}", completeMessage);
                    }
                }

                // 从缓冲区中移除已处理的消息（包括换行符）
                bufferContent = bufferContent.Substring(newLineIndex + 1);
                _receiveDataBuffer.Clear();
                _receiveDataBuffer.Append(bufferContent);
            }

            // 如果缓冲区数据过长，可能是异常情况，进行清理
            if (_receiveDataBuffer.Length > BufferSize * 10)
            {
                _logger.LogWarning("接收缓冲区数据过长，进行清理。当前长度：{Length}", _receiveDataBuffer.Length);
                _receiveDataBuffer.Clear();
            }
        }

        // 添加消息去重方法
        private readonly HashSet<string> _processedMessages = new HashSet<string>();
        private readonly object _messageLock = new object();

        private bool IsDuplicateMessage(string message)
        {
            try
            {
                // 使用消息内容的哈希值进行去重
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(message));
                var hashString = Convert.ToBase64String(hashBytes);

                lock (_messageLock)
                {
                    if (_processedMessages.Contains(hashString))
                    {
                        return true;
                    }

                    // 保持最近1000条消息的记录，避免内存无限增长
                    if (_processedMessages.Count > 1000)
                    {
                        _processedMessages.Clear();
                    }

                    _processedMessages.Add(hashString);
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 消息处理

        /// <summary>
        /// 检查是否为有效的JSON消息
        /// </summary>
        private bool IsValidJsonMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            // 快速检查：必须以 { 开头，以 } 结尾
            if (!message.Trim().StartsWith("{") || !message.Trim().EndsWith("}"))
                return false;

            try
            {
                // 尝试解析JSON验证格式
                using JsonDocument doc = JsonDocument.Parse(message);
                return doc.RootElement.ValueKind == JsonValueKind.Object;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 处理单条消息
        /// </summary>
        private async Task ProcessSingleMessageAsync(string message)
        {
            try
            {
                _logger.LogDebug("收到消息，内容：{Data}", message);

                // 触发数据接收事件
                OnDataReceived(message, "raw", "unknown");

                // 尝试解析为JSON消息
                using JsonDocument doc = JsonDocument.Parse(message);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("messageType", out JsonElement messageTypeElement))
                {
                    string messageType = messageTypeElement.GetString() ?? "";
                    _logger.LogInformation("收到协议消息，类型：{MessageType}", messageType);

                    // 根据消息类型处理
                    await HandleProtocolMessageAsync(messageType, message, root);
                }
                else
                {
                    _logger.LogWarning("收到未知格式的消息，缺少messageType字段");
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "JSON解析失败，消息内容：{Message}", message);

                // 尝试检查是否是消息粘包导致的问题
                if (message.Contains("}{"))
                {
                    _logger.LogWarning("检测到消息粘包，尝试手动分割处理");
                    await HandleStickyPacketsAsync(message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理单条消息时发生异常");
            }
        }

        /// <summary>
        /// 处理消息粘包情况
        /// </summary>
        private async Task HandleStickyPacketsAsync(string stickyMessage)
        {
            try
            {
                _logger.LogInformation("开始处理消息粘包，原始消息长度：{Length}", stickyMessage.Length);

                // 查找所有独立的JSON对象
                int startIndex = -1;
                int braceCount = 0;

                for (int i = 0; i < stickyMessage.Length; i++)
                {
                    char c = stickyMessage[i];

                    if (c == '{')
                    {
                        if (braceCount == 0)
                        {
                            startIndex = i; // 开始新的JSON对象
                        }
                        braceCount++;
                    }
                    else if (c == '}')
                    {
                        braceCount--;
                        if (braceCount == 0 && startIndex >= 0)
                        {
                            // 找到一个完整的JSON对象
                            int length = i - startIndex + 1;
                            string singleMessage = stickyMessage.Substring(startIndex, length);

                            _logger.LogDebug("从粘包中提取单条消息，长度：{Length}", length);

                            // 递归处理单条消息
                            await ProcessSingleMessageAsync(singleMessage);

                            startIndex = -1;
                        }
                    }
                }

                _logger.LogInformation("消息粘包处理完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理消息粘包时发生异常");
            }
        }

        /// <summary>
        /// 处理协议消息
        /// </summary>
        private async Task HandleProtocolMessageAsync(string messageType, string rawMessage, JsonElement root)
        {
            try
            {
                // 触发数据接收事件
                OnDataReceived(rawMessage, "protocol", messageType);

                // 根据消息类型分发处理
                switch (messageType)
                {
                    case "heartbeat_response":
                        await HandleHeartbeatResponseAsync(root);
                        break;
                    case "access_logs_upload_response":
                        await HandleAccessLogsUploadResponseAsync(root);
                        break;

                    case "request_boards_response":
                        await HandleBoardsDataResponseAsync(root);
                        break;

                    case "change_admin_password":
                    case "open_single_locker":
                    case "open_all_lockers":
                    case "sync_roles":
                    case "sync_users":
                    case "sync_lockers":
                    case "sync_user_lockers":
                    case "createAndUpdate_user":
                    case "user_locker_assignment":
                        await HandleServerCommandAsync(messageType, root, rawMessage);
                        break;

                    case "error":
                        await HandleErrorMessageAsync(root);
                        break;

                    // 处理同步响应消息
                    case "sync_roles_response":
                    case "sync_users_response":
                    case "sync_lockers_response":
                    case "sync_user_lockers_response":
                    case "createAndUpdate_user_response":
                    case "user_locker_assignment_response":
                        await HandleSyncResponseAsync(messageType, root);
                        break;

                    default:
                        _logger.LogWarning("未处理的消息类型：{MessageType}", messageType);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理协议消息时发生异常，消息类型：{MessageType}", messageType);
            }
        }

        #endregion

        #region 具体消息类型处理

        /// <summary>
        /// 处理心跳响应
        /// </summary>
        private async Task HandleHeartbeatResponseAsync(JsonElement root)
        {
            try
            {
                // 减少心跳响应的日志输出，避免日志过多
                if (_heartbeatFailCount > 0)
                {
                    _logger.LogDebug("收到服务器心跳响应，重置失败计数");
                }
                _heartbeatFailCount = 0; // 重置心跳失败计数

                // 解析心跳响应数据
                if (root.TryGetProperty("data", out JsonElement dataElement) &&
                    dataElement.TryGetProperty("status", out JsonElement statusElement))
                {
                    string status = statusElement.GetString() ?? "";
                    _logger.LogDebug("心跳响应状态：{Status}", status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理心跳响应时发生异常");
            }
        }

        /// <summary>
        /// 处理访问日志上传响应
        /// </summary>
        private async Task HandleAccessLogsUploadResponseAsync(JsonElement root)
        {
            try
            {
                _logger.LogInformation("收到访问日志上传响应");

                if (root.TryGetProperty("data", out JsonElement dataElement))
                {
                    string status = dataElement.GetProperty("status").GetString() ?? "";
                    string? errorMessage = null;

                    if (dataElement.TryGetProperty("errorMessage", out JsonElement errorElement))
                    {
                        errorMessage = errorElement.GetString();
                    }

                    bool success = status == "success";

                    _logger.LogInformation("访问日志上传响应状态: {Status}, 错误信息: {ErrorMessage}",
                        status, errorMessage ?? "无");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理访问日志上传响应时发生异常");
            }
        }

        #region 处理锁控板数据请求响应
        /// <summary>
        /// 处理锁控板数据请求响应
        /// </summary>
        private async Task HandleBoardsDataResponseAsync(JsonElement root)
        {
            try
            {
                _logger.LogInformation("收到锁控板数据请求响应");

                if (root.TryGetProperty("data", out JsonElement dataElement))
                {
                    string boardsData = dataElement.ToString();

                    // 使用现有的主板数据处理方法
                    bool success = await _dataSyncService.ProcessBoardsDataAsync(boardsData);

                    if (success)
                    {
                        _logger.LogInformation("锁控板数据处理成功");
                    }
                    else
                    {
                        _logger.LogWarning("锁控板数据处理失败");
                    }
                }
                else
                {
                    _logger.LogWarning("锁控板数据响应中缺少data字段");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理主板数据请求响应时发生异常");
            }
        }
        #endregion
        /// <summary>
        /// 处理服务器指令
        /// </summary>
        private async Task HandleServerCommandAsync(string command, JsonElement root, string rawMessage)
        {
            try
            {
                // 提取指令数据
                if (root.TryGetProperty("data", out JsonElement dataElement))
                {
                    string parameters = dataElement.ToString();

                    _logger.LogInformation("处理服务器指令，类型：{Command}，参数长度：{Length}",
                        command, parameters.Length);

                    // 触发指令接收事件
                    OnCommandReceived(command, parameters, root.ToString());

                    // 在UI线程中处理指令
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        try
                        {
                            await HandleCommandAsync(command, parameters, dataElement, rawMessage);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "处理指令 {Command} 时发生异常", command);
                            await SendErrorReportAsync("COMMAND_PROCESS_ERROR", $"处理指令{command}时发生异常：{ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理服务器指令时发生异常，指令：{Command}", command);
                await SendErrorReportAsync("COMMAND_PROCESS_ERROR", $"处理指令{command}时发生异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 处理错误消息
        /// </summary>
        private async Task HandleErrorMessageAsync(JsonElement root)
        {
            try
            {
                if (root.TryGetProperty("data", out JsonElement dataElement))
                {
                    string errorCode = dataElement.GetProperty("errorCode").GetString() ?? "";
                    string errorMessage = dataElement.GetProperty("errorMessage").GetString() ?? "";
                    string originalMessageType = dataElement.GetProperty("originalMessageType").GetString() ?? "";

                    _logger.LogError("收到错误消息，错误码：{ErrorCode}，错误信息：{ErrorMessage}，原始消息类型：{OriginalMessageType}",
                        errorCode, errorMessage, originalMessageType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理错误消息时发生异常");
            }
        }

        /// <summary>
        /// 处理同步响应消息
        /// </summary>
        private async Task HandleSyncResponseAsync(string messageType, JsonElement root)
        {
            try
            {
                _logger.LogDebug("收到同步响应消息，类型：{MessageType}", messageType);

                // 检查是否包含完整的同步结果（包含syncTime字段）
                if (root.TryGetProperty("data", out JsonElement dataElement) &&
                    dataElement.TryGetProperty("syncTime", out JsonElement syncTimeElement))
                {
                    string syncTime = syncTimeElement.GetString() ?? "";
                    _logger.LogInformation("处理完整同步响应，类型：{MessageType}，同步时间：{SyncTime}", messageType, syncTime);

                    // 这里可以添加对完整同步响应的进一步处理逻辑
                    // 例如更新同步状态、记录同步统计信息等

                    // 记录详细的同步结果
                    if (dataElement.TryGetProperty("status", out JsonElement statusElement))
                    {
                        string status = statusElement.GetString() ?? "";
                        string message = dataElement.TryGetProperty("message", out JsonElement messageElement)
                            ? messageElement.GetString() ?? ""
                            : "";

                        int added = dataElement.TryGetProperty("added", out JsonElement addedElement)
                            ? addedElement.GetInt32()
                            : 0;
                        int updated = dataElement.TryGetProperty("updated", out JsonElement updatedElement)
                            ? updatedElement.GetInt32()
                            : 0;
                        int deleted = dataElement.TryGetProperty("deleted", out JsonElement deletedElement)
                            ? deletedElement.GetInt32()
                            : 0;

                        _logger.LogInformation("同步响应详细信息 - 状态: {Status}, 消息: {Message}, 新增: {Added}, 更新: {Updated}, 删除: {Deleted}",
                            status, message, added, updated, deleted);
                    }
                }
                else
                {
                    // 忽略简单的状态响应（不包含syncTime字段）
                    _logger.LogDebug("忽略简单状态同步响应，类型：{MessageType}，缺少完整同步信息", messageType);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理同步响应消息时发生异常，类型：{MessageType}", messageType);
            }
        }

        #endregion

        #region 处理指令

        /// <summary>
        /// 处理指令
        /// </summary>
        private async Task HandleCommandAsync(string command, string parameters, JsonElement dataElement, string rawMessage)
        {
            try
            {
                _logger.LogInformation("执行服务器指令：{Command}", command);

                switch (command)
                {
                    case "change_admin_password":
                        await HandleChangeAdminPasswordCommand(dataElement);
                        break;

                    case "open_single_locker":
                        await HandleOpenSingleLockerCommand(dataElement);
                        break;

                    case "open_all_lockers":
                        await HandleOpenAllLockersCommand(dataElement);
                        break;
                    case "sync_roles":
                        await HandleSyncRolesCommand(rawMessage);
                        break;
                    case "sync_users":
                        await HandleSyncUsersCommand(rawMessage);
                        break;

                    case "sync_lockers":
                        await HandleSyncLockersCommand(rawMessage);
                        break;

                    case "sync_user_lockers":
                        await HandleSyncUserLockersCommand(rawMessage);
                        break;

                    case "createAndUpdate_user":
                        await HandleCreateOrUpdateUserCommand(rawMessage);
                        break;

                    case "user_locker_assignment":
                        await HandleUserLockerAssignmentCommand(rawMessage);
                        break;

                    default:
                        _logger.LogWarning("未知指令：{Command}", command);
                        await SendErrorReportAsync("UNKNOWN_COMMAND", $"未知指令：{command}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理指令时发生异常，指令：{Command}", command);
                await SendErrorReportAsync("COMMAND_EXECUTION_ERROR", $"执行指令{command}时发生异常：{ex.Message}");
            }
        }
        #endregion

        #region 处理修改管理员密码指令
        /// <summary>
        /// 处理修改管理员密码指令
        /// </summary>
        private async Task HandleChangeAdminPasswordCommand(JsonElement dataElement)
        {
            try
            {
                _logger.LogInformation("执行修改管理员密码指令");

                if (dataElement.TryGetProperty("password", out JsonElement passwordElement))
                {
                    string newPassword = passwordElement.GetString() ?? "";

                    _logger.LogInformation("收到修改管理员密码请求，新密码哈希：{Password}", newPassword);

                    var result = await _dataSyncService.UpdateAdminPasswordAsync(newPassword);
                    if (!result)
                    {
                        _logger.LogError("密码修改失败");
                        await SendProtocolMessageAsync("change_password_response", new
                        {
                            status = "error",
                            message = "密码修改失败"
                        });
                        return;
                    }

                    // 发送响应
                    await SendProtocolMessageAsync("change_password_response", new
                    {
                        status = "success",
                        message = "密码修改成功"
                    });
                }
                else
                {
                    _logger.LogWarning("修改管理员密码指令缺少password参数");
                    await SendProtocolMessageAsync("change_password_response", new
                    {
                        status = "error",
                        message = "缺少密码参数"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理修改管理员密码指令时发生异常");
                await SendProtocolMessageAsync("change_password_response", new
                {
                    status = "error",
                    message = "密码修改失败"
                });
            }
        }
        #endregion

        #region 处理打开单个锁柜指令
        /// <summary>
        /// 处理打开单个锁柜指令
        /// </summary>
        private async Task HandleOpenSingleLockerCommand(JsonElement dataElement)
        {
            try
            {
                _logger.LogInformation("执行打开单个锁柜指令");

                if (dataElement.TryGetProperty("lockerId", out JsonElement lockerIdElement))
                {
                    long lockerId = lockerIdElement.GetInt64();
                    string adminName = dataElement.GetProperty("adminName").GetString() ?? "";
                    string reason = dataElement.GetProperty("reason").GetString() ?? "";

                    _logger.LogInformation("收到打开单个锁柜指令，锁柜ID：{LockerId}，管理员：{AdminName}，原因：{Reason}",
                        lockerId, adminName, reason);

                    // 从UserLockers表读取Id=lockerId的数据
                    var locker = await _dataSyncService.GetUserLocker(lockerId);
                    if (locker != null)
                    {
                        var openResult = await _lockControlService.OpenLockAsync(locker.BoardAddress, locker.ChannelNumber);
                        if (openResult)
                        {
                            // 发送响应
                            await SendProtocolMessageAsync("open_locker_response", new
                            {
                                lockerId = lockerId,
                                status = "success",
                                message = $"{locker.LockerName} 开锁成功"
                            });
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("打开单个锁柜指令缺少lockerId参数");
                    await SendProtocolMessageAsync("open_locker_response", new
                    {
                        status = "error",
                        message = "缺少锁柜ID参数"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理打开单个锁柜指令时发生异常");
                await SendProtocolMessageAsync("open_locker_response", new
                {
                    status = "error",
                    message = "开锁失败"
                });
            }
        }
        #endregion

        #region 处理打开所有锁柜指令
        /// <summary>
        /// 处理打开所有锁柜指令
        /// </summary>
        private async Task HandleOpenAllLockersCommand(JsonElement dataElement)
        {
            try
            {
                _logger.LogInformation("执行打开所有锁柜指令");

                string adminName = dataElement.GetProperty("adminName").GetString() ?? "";
                string reason = dataElement.GetProperty("reason").GetString() ?? "";

                _logger.LogInformation("收到打开所有锁柜指令，管理员：{AdminName}，原因：{Reason}", adminName, reason);

                // 确保锁控服务已初始化
                if (!await _lockControlService.InitializeLockControlAsync())
                {
                    _logger.LogError("锁控服务未初始化，无法执行清柜操作");

                    await SendProtocolMessageAsync("open_all_response", new
                    {
                        status = "error",
                        message = "开锁失败",
                        openedCount = 0,
                        failedCount = 0
                    });
                    return;
                }

                var result = await _lockControlService.OpenAllLocksAsync();

                if (result)
                {
                    // 发送响应
                    await SendProtocolMessageAsync("open_all_response", new
                    {
                        status = "success",
                        message = "所有锁柜已打开",
                        openedCount = 0, // TODO: 实际开锁数量
                        failedCount = 0  // TODO: 实际失败数量
                    });
                }
                else
                {
                    await SendProtocolMessageAsync("open_all_response", new
                    {
                        status = "error",
                        message = "开锁失败",
                        openedCount = 0,
                        failedCount = 0
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理打开所有锁柜指令时发生异常");
                await SendProtocolMessageAsync("open_all_response", new
                {
                    status = "error",
                    message = "开锁失败",
                    openedCount = 0,
                    failedCount = 0
                });
            }
        }
        #endregion

        #region 处理同步角色列表指令
        /// <summary>
        /// 处理同步角色列表指令
        /// </summary>
        /// <param name="dataElement"></param>
        /// <returns></returns>
        private async Task HandleSyncRolesCommand(string rawMessage)
        {

            _logger.LogInformation("执行同步角色列表指令");

            bool success = await _dataSyncService.SyncRolesAsync(rawMessage);

            _logger.LogInformation("角色列表同步处理完成，结果：{Success}", success ? "成功" : "失败");

        }
        #endregion

        #region 处理同步用户列表指令
        /// <summary>
        /// 处理同步用户列表指令
        /// </summary>
        private async Task HandleSyncUsersCommand(string rawMessage)
        {

            _logger.LogInformation("执行同步用户列表指令");

            bool success = await _dataSyncService.SyncUsersAsync(rawMessage);

            _logger.LogInformation("用户列表同步处理完成，结果：{Success}", success ? "成功" : "失败");

        }
        #endregion

        #region 处理同步锁柜列表指令
        /// <summary>
        /// 处理同步锁柜列表指令
        /// </summary>
        private async Task HandleSyncLockersCommand(string rawMessage)
        {

            _logger.LogInformation("执行同步锁柜列表指令");

            bool success = await _dataSyncService.SyncLockersAsync(rawMessage);

            _logger.LogInformation("锁柜列表同步处理完成，结果：{Success}", success ? "成功" : "失败");

        }
        #endregion

        #region 处理同步用户锁柜分配指令
        /// <summary>
        /// 处理同步用户锁柜分配指令
        /// </summary>
        private async Task HandleSyncUserLockersCommand(string rawMessage)
        {

            _logger.LogInformation("执行同步用户锁柜分配指令");

            bool success = await _dataSyncService.SyncUserLockersAsync(rawMessage);

            _logger.LogInformation("用户锁柜分配同步处理完成，结果：{Success}", success ? "成功" : "失败");

        }
        #endregion

        #region 处理创建或更新用户指令
        /// <summary>
        /// 处理创建或更新用户指令
        /// </summary>
        private async Task HandleCreateOrUpdateUserCommand(string rawMessage)
        {
            try
            {
                _logger.LogInformation("执行创建或更新用户指令");

                // 调用数据同步服务，获取操作结果
                var result = await _dataSyncService.CreateOrUpdateUserAsync(rawMessage);
                bool success = result.success;
                bool isNewUser = result.isNewUser;
                var userId = result.userId;

                if (success)
                {
                    // 发送响应
                    var responseData = new
                    {
                        status = "success",
                        message = isNewUser ? "用户创建完成" : "用户更新完成",
                        added = isNewUser ? 1 : 0,
                        updated = isNewUser ? 0 : 1,
                        deleted = 0,
                        syncTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    };

                    await SendProtocolMessageAsync("createAndUpdate_user_response", responseData);
                    _logger.LogInformation("创建或更新用户指令执行完成，结果：{Operation}", isNewUser ? "新增" : "更新");
                }
                else
                {
                    // 发送错误响应
                    var errorResponse = new
                    {
                        status = "error",
                        message = "用户信息同步失败",
                        added = 0,
                        updated = 0,
                        deleted = 0,
                        syncTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    };

                    await SendProtocolMessageAsync("createAndUpdate_user_response", errorResponse);
                    _logger.LogWarning("创建或更新用户指令执行失败，用户ID：{UserId}", userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理创建或更新用户指令时发生异常");
                await SendErrorReportAsync("CREATE_UPDATE_USER_ERROR", $"处理创建或更新用户指令时发生异常：{ex.Message}");
            }
        }
        #endregion

        #region 处理用户柜格分配指令
        /// <summary>
        /// 处理用户柜格分配指令
        /// </summary>
        private async Task HandleUserLockerAssignmentCommand(string rawMessage)
        {
            try
            {
                _logger.LogInformation("执行用户柜格分配指令");

                // 首先解析原始消息获取type值
                int requestType = 0;
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(rawMessage);
                    if (doc.RootElement.TryGetProperty("data", out JsonElement dataElement) &&
                        dataElement.TryGetProperty("type", out JsonElement typeElement))
                    {
                        requestType = typeElement.GetInt32();
                    }
                }
                catch (Exception parseEx)
                {
                    _logger.LogWarning(parseEx, "无法从原始消息中解析type值");
                }

                bool success = await _dataSyncService.ProcessUserLockerAssignmentCommandAsync(rawMessage, false);

                // 发送响应
                var responseData = new
                {
                    status = success ? "success" : "error",
                    type = requestType,
                    message = success ? "用户与柜格分配完成" : "用户与柜格分配失败",
                    added = success ? 1 : 0,
                    updated = 0,
                    deleted = 0,
                    syncTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };

                await SendProtocolMessageAsync("user_locker_assignment_response", responseData);
                _logger.LogInformation("用户柜格分配指令执行完成，结果：{Success}", success ? "成功" : "失败");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理用户柜格分配指令时发生异常");

                // 发送错误响应
                var errorResponse = new
                {
                    status = "error",
                    type = 0,
                    message = $"处理用户柜格分配指令时发生异常：{ex.Message}",
                    added = 0,
                    updated = 0,
                    deleted = 0,
                    syncTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff")
                };

                await SendProtocolMessageAsync("user_locker_assignment_response", errorResponse);
            }
        }
        #endregion

        #region 根据数据类型获取同步反馈消息类型
        /// <summary>
        /// 根据数据类型获取同步反馈消息类型
        /// </summary>
        private string GetSyncFeedbackMessageType(string dataType)
        {
            return dataType.ToLower() switch
            {
                "roles" => "sync_roles_response",
                "users" => "sync_users_response",
                "lockers" => "sync_lockers_response",
                "userlockers" => "sync_user_lockers_response",
                _ => $"sync_{dataType}_response"
            };
        }
        #endregion

        #region 根据数据类型获取同步成功消息
        /// <summary>
        /// 根据数据类型获取同步成功消息
        /// </summary>
        private string GetSyncSuccessMessage(string dataType)
        {
            return dataType.ToLower() switch
            {
                "roles" => "角色列表同步完成",
                "users" => "用户列表同步完成",
                "lockers" => "锁柜列表同步完成",
                "userlockers" => "用户锁柜分配同步完成",
                "boards" => "锁控板数据同步完成",
                _ => $"{dataType}数据同步完成"
            };
        }
        #endregion

        #region 事件触发方法

        /// <summary>
        /// 触发连接状态变更事件
        /// </summary>
        private void OnConnectionStatusChanged(bool isConnected, string status)
        {
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs
            {
                IsConnected = isConnected,
                ChangedAt = DateTime.Now,
                Status = status,
                ServerAddress = _serverIp,
                Port = _port
            });
        }

        /// <summary>
        /// 触发数据接收事件
        /// </summary>
        private void OnDataReceived(string data, string dataType, string messageType)
        {
            DataReceived?.Invoke(this, new DataReceivedEventArgs
            {
                Data = data,
                DataType = dataType,
                ReceivedAt = DateTime.Now,
                MessageType = messageType
            });
        }

        /// <summary>
        /// 触发指令接收事件
        /// </summary>
        private void OnCommandReceived(string command, string parameters, string originalMessage)
        {
            CommandReceived?.Invoke(this, new CommandReceivedEventArgs
            {
                Command = command,
                Parameters = parameters,
                ReceivedAt = DateTime.Now,
                Source = _serverIp,
                OriginalMessage = originalMessage
            });
        }

        /// <summary>
        /// 触发重连状态变更事件
        /// </summary>
        private void OnReconnectStatusChanged(bool isReconnecting, int attemptCount, string status)
        {
            ReconnectStatusChanged?.Invoke(this, new ReconnectStatusChangedEventArgs
            {
                IsReconnecting = isReconnecting,
                AttemptCount = attemptCount,
                Status = status,
                ChangedAt = DateTime.Now,
                ServerAddress = _serverIp,
                Port = _port
            });
        }

        #endregion

        #region 清理资源

        /// <summary>
        /// 清理网络服务资源
        /// </summary>
        public void Dispose()
        {
            _logger.LogInformation("清理网络服务资源");

            StopHeartbeat();
            StopReconnectTimer();
            StopReconnectMechanism();

            try
            {
                if (_socket != null)
                {
                    if (_socket.Connected)
                    {
                        _socket.Shutdown(SocketShutdown.Both);
                    }
                    _socket.Close();
                    _socket.Dispose();
                    _socket = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理Socket资源时发生异常");
            }

            _logger.LogInformation("网络服务资源清理完成");
        }

        #endregion
    }
}