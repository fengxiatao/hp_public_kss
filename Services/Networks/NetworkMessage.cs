using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaceLocker.Models
{
    /// <summary>
    /// 网络消息类型枚举
    /// </summary>
    public enum MessageType
    {
        /// <summary>
        /// 心跳包
        /// </summary>
        Heartbeat = 1,

        /// <summary>
        /// 下载请求
        /// </summary>
        DownloadRequest = 2,

        /// <summary>
        /// 同步完成确认
        /// </summary>
        SyncComplete = 3,

        /// <summary>
        /// 错误报告
        /// </summary>
        ErrorReport = 4,

        /// <summary>
        /// 连接测试
        /// </summary>
        ConnectionTest = 5,

        /// <summary>
        /// 数据更新
        /// </summary>
        DataUpdate = 6,

        /// <summary>
        /// 指令请求
        /// </summary>
        CommandRequest = 7,

        /// <summary>
        /// 指令响应
        /// </summary>
        CommandResponse = 8,
        /// <summary>
        /// 创建或更新用户
        /// </summary>
        CreateOrUpdateUser = 9,
        /// <summary>
        /// 用户柜格分配
        /// </summary>
        UserLockerAssignment = 10
    }

    /// <summary>
    /// 网络消息结构
    /// </summary>
    public class NetworkMessage
    {
        /// <summary>
        /// 协议版本
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// 消息时间戳
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 设备名称
        /// </summary>
        [JsonPropertyName("deviceName")]
        public string DeviceName { get; set; } = Environment.MachineName;

        /// <summary>
        /// 消息类型
        /// </summary>
        [JsonPropertyName("messageType")]
        public MessageType MessageType { get; set; }

        /// <summary>
        /// 消息数据
        /// </summary>
        [JsonPropertyName("data")]
        public object Data { get; set; } = new();

        /// <summary>
        /// 创建心跳消息
        /// </summary>
        public static NetworkMessage CreateHeartbeat()
        {
            return new NetworkMessage
            {
                MessageType = MessageType.Heartbeat,
                Data = new { Status = "HELLO" }
            };
        }

        /// <summary>
        /// 创建同步完成消息
        /// </summary>
        public static NetworkMessage CreateSyncComplete(string dataType)
        {
            return new NetworkMessage
            {
                MessageType = MessageType.SyncComplete,
                Data = new { DataType = dataType, Status = "Success" }
            };
        }

        /// <summary>
        /// 创建错误报告消息
        /// </summary>
        public static NetworkMessage CreateErrorReport(string errorCode, string errorMessage)
        {
            return new NetworkMessage
            {
                MessageType = MessageType.ErrorReport,
                Data = new { ErrorCode = errorCode, ErrorMessage = errorMessage }
            };
        }

        /// <summary>
        /// 创建连接测试消息
        /// </summary>
        public static NetworkMessage CreateConnectionTest()
        {
            return new NetworkMessage
            {
                MessageType = MessageType.ConnectionTest,
                Data = new { Test = "Connection" }
            };
        }

        /// <summary>
        /// 创建数据更新消息
        /// </summary>
        public static NetworkMessage CreateDataUpdate(object data)
        {
            return new NetworkMessage
            {
                MessageType = MessageType.DataUpdate,
                Data = data
            };
        }

        /// <summary>
        /// 创建指令请求消息
        /// </summary>
        public static NetworkMessage CreateCommandRequest(string command, object parameters)
        {
            return new NetworkMessage
            {
                MessageType = MessageType.CommandRequest,
                Data = new { Command = command, Parameters = parameters }
            };
        }

        /// <summary>
        /// 创建指令响应消息
        /// </summary>
        public static NetworkMessage CreateCommandResponse(string command, object result)
        {
            return new NetworkMessage
            {
                MessageType = MessageType.CommandResponse,
                Data = new { Command = command, Result = result }
            };
        }

        /// <summary>
        /// 序列化为JSON字符串
        /// </summary>
        public string ToJson()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
        }

        /// <summary>
        /// 从JSON字符串反序列化
        /// </summary>
        public static NetworkMessage? FromJson(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<NetworkMessage>(json, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 验证消息格式
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(Version) &&
                   !string.IsNullOrWhiteSpace(DeviceName) &&
                   Timestamp > DateTime.MinValue &&
                   Data != null;
        }

        /// <summary>
        /// 获取消息类型名称
        /// </summary>
        public string GetMessageTypeName()
        {
            return MessageType switch
            {
                MessageType.Heartbeat => "心跳包",
                MessageType.DownloadRequest => "下载请求",
                MessageType.SyncComplete => "同步完成",
                MessageType.ErrorReport => "错误报告",
                MessageType.ConnectionTest => "连接测试",
                MessageType.DataUpdate => "数据更新",
                MessageType.CommandRequest => "指令请求",
                MessageType.CommandResponse => "指令响应",
                _ => "未知消息"
            };
        }
    }
}