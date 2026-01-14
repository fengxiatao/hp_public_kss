using Avalonia.Media.Imaging;
using System;
using System.Threading.Tasks;

namespace FaceLocker.Services
{
    public interface ICameraService
    {
        /// <summary>
        /// 摄像头是否可用
        /// </summary>
        bool IsCameraAvailable { get; }

        /// <summary>
        /// 启动摄像头
        /// </summary>
        Task<bool> StartCameraAsync();

        /// <summary>
        /// 停止摄像头
        /// </summary>
        Task StopCameraAsync();

        /// <summary>
        /// 获取当前显示帧
        /// </summary>
        Task<WriteableBitmap> CaptureDisplayFrameAsync();

        /// <summary>
        /// 摄像头显示帧事件
        /// </summary>
        event EventHandler<WriteableBitmap> FrameDisplayCaptured;

        /// <summary>
        /// 检查摄像头健康
        /// </summary>
        /// <returns></returns>
        Task<bool> CheckCameraHealthAsync();
    }
}
