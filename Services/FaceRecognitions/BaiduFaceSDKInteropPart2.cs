using System;
using System.Text;

namespace FaceLocker.Services
{
    public static partial class BaiduFaceSDKInterop
    {
        #region 初始化百度人脸SDK

        /// <summary>
        /// SDK初始化状态
        /// </summary>
        private static bool _isSdkInitialized = false;
        private static IntPtr _apiHandle = IntPtr.Zero;

        /// <summary>
        /// 初始化百度人脸SDK
        /// </summary>
        /// <param name="modelPath">模型文件路径，如果为null则使用默认路径</param>
        /// <returns>初始化结果</returns>
        public static SDKInitializeResult InitializeSDK(string modelPath = null)
        {
            var result = new SDKInitializeResult();

            try
            {
                LogInfo("开始初始化百度人脸识别SDK");

                if (_isSdkInitialized && _apiHandle != IntPtr.Zero)
                {
                    LogInfo("SDK已经初始化，先清理现有实例");
                    ReleaseSDK();
                }

                // 调用SDK初始化函数
                string actualModelPath = string.IsNullOrEmpty(modelPath) ? "/opt/face_offline_sdk" : modelPath;

                LogInfo($"使用模型路径: {actualModelPath}");

                int initResult = baidu_face_sdk_init(actualModelPath);

                if (initResult == 0)
                {
                    _isSdkInitialized = true;
                    result.Success = true;
                    result.Message = "SDK初始化成功";
                    LogInfo("百度人脸SDK初始化成功");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = initResult;
                    result.Message = $"SDK初始化失败，错误码: {initResult} - {GetErrorMessage(initResult)}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorCode = -9999;
                result.Message = $"SDK初始化异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }
        #endregion

        #region 释放百度人脸SDK
        /// <summary>
        /// 释放SDK资源
        /// </summary>
        public static void ReleaseSDK()
        {
            try
            {
                LogInfo("开始释放SDK资源");

                // 注意：C++包装库使用全局静态实例，不需要手动释放
                // 这里主要是重置状态
                _isSdkInitialized = false;
                _apiHandle = IntPtr.Zero;

                LogInfo("SDK资源释放完成");
            }
            catch (Exception ex)
            {
                LogError($"释放SDK资源时发生异常: {ex.Message}");
            }
        }
        #endregion

        #region 检查SDK是否已初始化
        /// <summary>
        /// 检查SDK是否已初始化
        /// </summary>
        public static bool IsInitialized()
        {
            return _isSdkInitialized;
        }
        #endregion

        #region 检查SDK授权状态
        /// <summary>
        /// 检查SDK授权状态
        /// </summary>
        public static AuthCheckResult CheckAuthStatus()
        {
            var result = new AuthCheckResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                return result;
            }

            try
            {
                LogInfo("检查SDK授权状态");

                int authStatus = baidu_face_is_auth();

                if (authStatus == 1)
                {
                    result.Success = true;
                    result.IsAuthorized = true;
                    result.Message = "SDK已授权";
                    LogInfo("SDK授权状态: 已授权");
                }
                else if (authStatus == 0)
                {
                    result.Success = true;
                    result.IsAuthorized = false;
                    result.Message = "SDK未授权";
                    LogInfo("SDK授权状态: 未授权");
                }
                else
                {
                    result.Success = false;
                    result.IsAuthorized = false;
                    result.Message = $"授权检查失败，错误码: {authStatus}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"检查授权状态时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }
        #endregion

        #region 获取设备ID
        /// <summary>
        /// 获取设备ID
        /// </summary>
        public static DeviceInfoResult GetDeviceId()
        {
            var result = new DeviceInfoResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                return result;
            }

            try
            {
                LogInfo("获取设备ID");

                StringBuilder deviceId = new StringBuilder(256);
                int getResult = baidu_face_get_device_id(deviceId, deviceId.Capacity);

                if (getResult == 0)
                {
                    result.Success = true;
                    result.DeviceId = deviceId.ToString();
                    result.Message = "获取设备ID成功";
                    LogInfo($"设备ID: {result.DeviceId}");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = getResult;
                    result.Message = $"获取设备ID失败，错误码: {getResult}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"获取设备ID时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }
        #endregion

        #region 获取SDK版本信息
        /// <summary>
        /// 获取SDK版本信息
        /// </summary>
        public static VersionInfoResult GetVersionInfo()
        {
            var result = new VersionInfoResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                return result;
            }

            try
            {
                LogInfo("获取SDK版本信息");

                StringBuilder version = new StringBuilder(256);
                int getResult = baidu_face_sdk_version(version, version.Capacity);

                if (getResult == 0)
                {
                    result.Success = true;
                    result.Version = version.ToString();
                    result.Message = "获取版本信息成功";
                    LogInfo($"SDK版本: {result.Version}");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = getResult;
                    result.Message = $"获取版本信息失败，错误码: {getResult}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"获取版本信息时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }
        #endregion

        #region 获取完整的SDK信息
        /// <summary>
        /// 获取完整的SDK信息
        /// </summary>
        public static SDKInfo GetSDKInfo()
        {
            var info = new SDKInfo();

            if (!_isSdkInitialized)
            {
                info.IsAvailable = false;
                info.ErrorMessage = "SDK未初始化";
                return info;
            }

            try
            {
                // 获取设备ID
                var deviceResult = GetDeviceId();
                if (deviceResult.Success)
                {
                    info.DeviceId = deviceResult.DeviceId;
                }

                // 获取版本信息
                var versionResult = GetVersionInfo();
                if (versionResult.Success)
                {
                    info.Version = versionResult.Version;
                }

                // 检查授权状态
                var authResult = CheckAuthStatus();
                if (authResult.Success)
                {
                    info.IsAuthorized = authResult.IsAuthorized;
                }

                info.IsAvailable = true;
                info.Message = "SDK信息获取完成";
            }
            catch (Exception ex)
            {
                info.IsAvailable = false;
                info.ErrorMessage = $"获取SDK信息时发生异常: {ex.Message}";
                LogError(info.ErrorMessage);
            }

            return info;
        }
        #endregion

        #region 错误码处理

        /// <summary>
        /// 获取错误码对应的描述信息
        /// </summary>
        public static string GetErrorMessage(int errorCode)
        {
            return errorCode switch
            {
                0 => "操作成功",
                -1 => "非法参数",
                -2 => "内存分配失败",
                -3 => "API实例为空",
                -4 => "模型内容为空",
                -5 => "不支持的能力类型",
                -6 => "不支持的预测库类型",
                -7 => "预测库对象创建失败",
                -8 => "预测库对象初始化失败",
                -9 => "图像数据为空",
                -10 => "人脸能力初始化失败",
                -11 => "人脸能力未加载",
                -12 => "人脸能力已加载",
                -13 => "未授权",
                -14 => "人脸能力运行异常",
                -15 => "不支持的图像类型",
                -16 => "图像转换失败",
                -1001 => "系统错误",
                -1002 => "参数错误",
                -1003 => "数据库操作失败",
                -1004 => "没有数据",
                -1005 => "记录不存在",
                -1006 => "记录已存在",
                -1007 => "文件不存在",
                -1008 => "提取特征值失败",
                -1009 => "文件太大",
                -1010 => "人脸资源文件不存在",
                -1011 => "特征值长度错误",
                -1012 => "未检测到人脸",
                -1013 => "摄像头错误或不存在",
                -1014 => "人脸引擎初始化错误",
                -1015 => "授权文件不存在",
                -1016 => "授权序列号为空",
                -1017 => "授权序列号无效",
                -1018 => "授权序列号过期",
                -1019 => "授权序列号已被使用",
                -1020 => "设备指纹为空",
                -1021 => "网络超时",
                -1022 => "网络错误",
                -1023 => "配置文件face.ini不存在",
                _ => $"未知错误 (错误码: {errorCode})"
            };
        }

        #endregion
    }

    #region 结果类定义

    /// <summary>
    /// SDK初始化结果
    /// </summary>
    public class SDKInitializeResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 授权检查结果
    /// </summary>
    public class AuthCheckResult
    {
        public bool Success { get; set; }
        public bool IsAuthorized { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 设备信息结果
    /// </summary>
    public class DeviceInfoResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 版本信息结果
    /// </summary>
    public class VersionInfoResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public string Version { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// SDK信息类
    /// </summary>
    public class SDKInfo
    {
        public bool IsAvailable { get; set; }
        public bool IsAuthorized { get; set; }
        public string Version { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    #endregion
}