using FaceLocker.Models.Enums;
using System.Collections.Generic;

namespace FaceLocker.Models.Settings
{
    public class LockControllerSettings
    {
        /// <summary>
        /// 锁控组名称
        /// </summary>
        public string GroupName { get; set; }

        /// <summary>
        /// IP地址
        /// </summary>
        public string IPAddress { get; set; }

        /// <summary>
        /// 锁柜排序方向
        /// </summary>
        public int Direction { get; set; }

        /// <summary>
        /// RS485锁控板列表
        /// </summary>
        public List<LockBoardSettings> Boards { get; set; } = new();

        /// <summary>
        /// 波特率
        /// </summary>
        public int BaudRate { get; set; } = 9600;

        /// <summary>
        /// 校验位（"None", "Odd", "Even" 等）
        /// </summary>
        public string Parity { get; set; } = "None";

        /// <summary>
        /// 数据位
        /// </summary>
        public int DataBits { get; set; } = 8;

        /// <summary>
        /// 停止位（"One", "Two", "OnePointFive"）
        /// </summary>
        public string StopBits { get; set; } = "One";

        /// <summary>
        /// 读取超时（毫秒）
        /// </summary>
        public int ReadTimeout { get; set; } = 1200;

        /// <summary>
        /// 写入超时（毫秒）
        /// </summary>
        public int WriteTimeout { get; set; } = 800;

        /// <summary>
        /// 是否使用RTS方向控制（用于半双工RS485）
        /// </summary>
        public bool UseRtsDirection { get; set; } = false;

        /// <summary>
        /// 收发切换延迟（毫秒）
        /// </summary>
        public int TxRxTurnaroundMs { get; set; } = 200;

        /// <summary>
        /// 命令间延迟（毫秒）
        /// </summary>
        public int InterCommandDelayMs { get; set; } = 200;

        /// <summary>
        /// 反馈模式
        /// </summary>
        public FeedbackMode FeedbackMode { get; set; } = FeedbackMode.None;
    }

    public class LockBoardSettings
    {
        /// <summary>
        /// 锁控板地址（1-255）
        /// </summary>
        public int Address { get; set; } = 1;

        /// <summary>
        /// 串口设备路径（Linux）
        /// </summary>
        public string SerialPort { get; set; } = "/dev/ttyS9";
    }
}