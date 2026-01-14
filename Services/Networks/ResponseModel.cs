using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaceLocker.Services
{
    #region 服务器用户响应数据模型
    /// <summary>
    /// 服务器用户响应数据模型
    /// </summary>
    public class ServerUserResponse
    {
        public ServerUserData[] Data { get; set; } = Array.Empty<ServerUserData>();
        public string DeviceName { get; set; } = string.Empty;
        public string MessageType { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
    #endregion

    #region 服务器用户数据模型
    /// <summary>
    /// 服务器用户数据模型
    /// </summary>
    public class ServerUserData
    {
        public long Id { get; set; }
        public string UserNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string IdNumber { get; set; } = string.Empty;
        public long RoleId { get; set; }
        public List<long> AssignedLockers { get; set; } = [];
        public string? Avatar { get; set; }
        public float FaceConfidence { get; set; }
        public int FaceFeatureVersion { get; set; }
        public DateTime? LastFaceUpdate { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? Department { get; set; }
        public string? Remarks { get; set; }
    }
    #endregion

    #region 服务器主板响应数据模型
    /// <summary>
    /// 服务器主板响应数据模型
    /// </summary>
    public class ServerBoardResponse
    {
        public ServerBoardData Data { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string MessageType { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
    #endregion

    #region 服务器主板数据模型
    /// <summary>
    /// 服务器主板数据模型
    /// </summary>
    public class ServerBoardData
    {
        public string GroupName { get; set; }
        public string IPAddress { get; set; } = string.Empty;
        public int Direction { get; set; }

        public BoardData[] Boards { get; set; } = Array.Empty<BoardData>();
    }
    #endregion

    #region 主板数据模型
    /// <summary>
    /// 主板数据模型
    /// </summary>
    public class BoardData
    {
        public int Address { get; set; }
        public string SerialPort { get; set; } = string.Empty;
        public int BaudRate { get; set; }
        public int DataBits { get; set; }
        public string StopBits { get; set; } = string.Empty;
        public string Parity { get; set; } = string.Empty;
    }
    #endregion

    #region 服务器角色响应数据模型
    /// <summary>
    /// 服务器角色响应数据模型
    /// </summary>
    public class ServerRoleResponse
    {
        public ServerRoleData[] Data { get; set; } = Array.Empty<ServerRoleData>();
        public string DeviceName { get; set; } = string.Empty;
        public string MessageType { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
    #endregion

    #region 服务器角色数据模型
    /// <summary>
    /// 服务器角色数据模型
    /// </summary>
    public class ServerRoleData
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Permissions { get; set; } = string.Empty; // JSON格式的权限列表
        public bool IsFromServer { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
    #endregion

    #region 服务器锁柜响应数据模型
    /// <summary>
    /// 服务器锁柜响应数据模型
    /// </summary>
    public class ServerLockerResponse
    {
        public ServerLockerData[] Data { get; set; } = Array.Empty<ServerLockerData>();
        public string DeviceName { get; set; } = string.Empty;
        public string MessageType { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
    #endregion

    #region 服务器锁柜数据模型
    /// <summary>
    /// 服务器锁柜数据模型
    /// </summary>
    public class ServerLockerData
    {
        public long LockerId { get; set; }
        public string LockerName { get; set; } = string.Empty;
        public string LockerNumber { get; set; } = string.Empty;
        public int BoardAddress { get; set; }
        public int ChannelNumber { get; set; }
        public int Status { get; set; }
        public bool IsOpened { get; set; }
        public DateTime? LastOpened { get; set; }
        public bool IsAvailable { get; set; }
        public string Location { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
    #endregion

    #region 服务器用户锁柜关联响应数据模型
    /// <summary>
    /// 服务器用户锁柜关联响应数据模型
    /// </summary>
    public class ServerUserLockerResponse
    {
        public ServerUserLockerData[] Data { get; set; } = Array.Empty<ServerUserLockerData>();
        public string DeviceName { get; set; } = string.Empty;
        public string MessageType { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
    #endregion

    #region 服务器用户锁柜关联数据模型
    /// <summary>
    /// 服务器用户锁柜关联数据模型
    /// </summary>
    public class ServerUserLockerData
    {
        public long  Id { get; set; }
        public long UserId { get; set; }
        public long LockerId { get; set; }
        public DateTime AssignedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
    #endregion

    #region 服务器用户柜格分配请求数据模型
    /// <summary>
    /// 服务器用户柜格分配请求数据模型
    /// </summary>
    public class ServerUserLockerAssignmentRequest
    {
        public ServerUserLockerAssignmentData Data { get; set; } = new ServerUserLockerAssignmentData();
        public string DeviceName { get; set; } = string.Empty;
        public string MessageType { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
    #endregion

    #region 服务器用户柜格分配数据模型
    /// <summary>
    /// 服务器用户柜格分配数据模型
    /// </summary>
    public class ServerUserLockerAssignmentData
    {
        /// <summary>
        /// 操作类型：0=绑定，1=解绑
        /// </summary>
        public int Type { get; set; } = 0;

        /// <summary>
        /// 用户ID
        /// </summary>
        public long UserId { get; set; } = 0;

        /// <summary>
        /// 柜格ID
        /// </summary>
        public long LockerId { get; set; } = 0;

        /// <summary>
        /// 分配时间
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? AssignedAt { get; set; }

        /// <summary>
        /// 过期时间
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// 是否激活
        /// </summary>
        public bool IsActive { get; set; } = true;
    }
    #endregion

    #region Id转换器
    /// <summary>
    /// Id转换器，处理整数和字符串类型的Id
    /// </summary>
    public class IdConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                if (reader.TryGetInt32(out int intValue))
                {
                    return intValue.ToString();
                }
                else if (reader.TryGetInt64(out long longValue))
                {
                    return longValue.ToString();
                }
                else
                {
                    throw new JsonException($"无法将数字 {reader.GetInt64()} 转换为字符串");
                }
            }
            else if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString() ?? string.Empty;
            }
            else
            {
                throw new JsonException($"无法将 token 类型 {reader.TokenType} 转换为字符串");
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
    #endregion

    #region 重连状态变更事件参数

    /// <summary>
    /// 重连状态变更事件参数
    /// </summary>
    public class ReconnectStatusChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 是否正在重连
        /// </summary>
        public bool IsReconnecting { get; set; }

        /// <summary>
        /// 重连尝试次数
        /// </summary>
        public int AttemptCount { get; set; }

        /// <summary>
        /// 状态描述
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// 变更时间
        /// </summary>
        public DateTime ChangedAt { get; set; }

        /// <summary>
        /// 服务器地址
        /// </summary>
        public string ServerAddress { get; set; } = string.Empty;

        /// <summary>
        /// 服务器端口
        /// </summary>
        public int Port { get; set; }
    }

    #endregion
}
