
using System;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    /// <summary>
    /// 网络通信服务接口
    /// </summary>
    public interface INetworkService
    {
        #region 连接状态和事件

        /// <summary>
        /// 获取是否已连接到服务器
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 获取最后连接时间
        /// </summary>
        DateTime? LastConnectedTime { get; }

        /// <summary>
        /// 获取心跳失败次数
        /// </summary>
        int HeartbeatFailCount { get; }

        /// <summary>
        /// 连接状态变更事件
        /// </summary>
        event EventHandler<ConnectionStatusChangedEventArgs> ConnectionStatusChanged;

        /// <summary>
        /// 数据接收事件
        /// </summary>
        event EventHandler<DataReceivedEventArgs> DataReceived;

        /// <summary>
        /// 指令接收事件
        /// </summary>
        event EventHandler<CommandReceivedEventArgs> CommandReceived;

        #endregion

        #region 连接管理

        /// <summary>
        /// 初始化网络服务
        /// </summary>
        Task<bool> InitializeAsync();

        /// <summary>
        /// 连接到服务器
        /// </summary>
        Task<bool> ConnectAsync();

        /// <summary>
        /// 断开与服务器的连接
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// 重新连接到服务器
        /// </summary>
        Task<bool> ReconnectAsync();

        #endregion

        #region 数据发送

        /// <summary>
        /// 发送数据到服务器
        /// </summary>
        /// <param name="data">要发送的数据</param>
        Task<bool> SendDataAsync(string data);

        /// <summary>
        /// 发送心跳包
        /// </summary>
        Task<bool> SendHeartbeatAsync();

        /// <summary>
        /// 发送同步完成确认
        /// </summary>
        Task<bool> SendSyncCompleteAsync(string dataType);

        /// <summary>
        /// 发送错误报告
        /// </summary>
        Task<bool> SendErrorReportAsync(string errorCode, string errorMessage);

        /// <summary>
        /// 发送协议消息到服务器
        /// </summary>
        /// <param name="messageType"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        Task<bool> SendProtocolMessageAsync(string messageType, object data);

        #endregion

        #region 数据请求
        /// <summary>
        /// 请求主板数据
        /// </summary>
        Task<bool> RequestBoardsDataAsync();
        #endregion

        #region 状态检查

        /// <summary>
        /// 检查网络连接状态
        /// </summary>
        Task<bool> CheckConnectionAsync();

        /// <summary>
        /// 获取网络统计信息
        /// </summary>
        Task<NetworkStatistics> GetStatisticsAsync();

        #endregion
    }

    #region 事件参数类

    /// <summary>
    /// 连接状态变更事件参数
    /// </summary>
    public class ConnectionStatusChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// 变更时间
        /// </summary>
        public DateTime ChangedAt { get; set; }

        /// <summary>
        /// 连接状态描述
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// 服务器地址
        /// </summary>
        public string ServerAddress { get; set; } = string.Empty;

        /// <summary>
        /// 端口号
        /// </summary>
        public int Port { get; set; }
    }

    /// <summary>
    /// 数据接收事件参数
    /// </summary>
    public class DataReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// 接收到的数据
        /// </summary>
        public string Data { get; set; } = string.Empty;

        /// <summary>
        /// 数据类型
        /// </summary>
        public string DataType { get; set; } = string.Empty;

        /// <summary>
        /// 接收时间
        /// </summary>
        public DateTime ReceivedAt { get; set; }

        /// <summary>
        /// 数据长度
        /// </summary>
        public int DataLength => Data?.Length ?? 0;

        /// <summary>
        /// 消息类型
        /// </summary>
        public string MessageType { get; set; } = string.Empty;
    }

    /// <summary>
    /// 指令接收事件参数
    /// </summary>
    public class CommandReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// 指令名称
        /// </summary>
        public string Command { get; set; } = string.Empty;

        /// <summary>
        /// 指令参数
        /// </summary>
        public string Parameters { get; set; } = string.Empty;

        /// <summary>
        /// 接收时间
        /// </summary>
        public DateTime ReceivedAt { get; set; }

        /// <summary>
        /// 指令来源
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// 原始消息数据
        /// </summary>
        public string OriginalMessage { get; set; } = string.Empty;
    }

    #endregion

    #region 数据类

    /// <summary>
    /// 网络统计信息
    /// </summary>
    public class NetworkStatistics
    {
        /// <summary>
        /// 总连接次数
        /// </summary>
        public int TotalConnections { get; set; }

        /// <summary>
        /// 总断开次数
        /// </summary>
        public int TotalDisconnections { get; set; }

        /// <summary>
        /// 总发送数据量（字节）
        /// </summary>
        public long TotalBytesSent { get; set; }

        /// <summary>
        /// 总接收数据量（字节）
        /// </summary>
        public long TotalBytesReceived { get; set; }

        /// <summary>
        /// 总心跳包数量
        /// </summary>
        public int TotalHeartbeats { get; set; }

        /// <summary>
        /// 心跳失败次数
        /// </summary>
        public int HeartbeatFailures { get; set; }

        /// <summary>
        /// 最后连接时间
        /// </summary>
        public DateTime? LastConnectedTime { get; set; }

        /// <summary>
        /// 最后断开时间
        /// </summary>
        public DateTime? LastDisconnectedTime { get; set; }

        /// <summary>
        /// 平均连接时长
        /// </summary>
        public TimeSpan? AverageConnectionDuration { get; set; }

        /// <summary>
        /// 连接成功率
        /// </summary>
        public double ConnectionSuccessRate => TotalConnections > 0 ?
            ((TotalConnections - TotalDisconnections) * 100.0) / TotalConnections : 0;

        /// <summary>
        /// 心跳成功率
        /// </summary>
        public double HeartbeatSuccessRate => TotalHeartbeats > 0 ?
            ((TotalHeartbeats - HeartbeatFailures) * 100.0) / TotalHeartbeats : 0;
    }

    #endregion
}