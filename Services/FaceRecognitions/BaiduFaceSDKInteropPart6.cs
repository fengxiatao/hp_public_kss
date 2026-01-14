using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using static FaceLocker.Services.BaiduFaceSDKInterop;

namespace FaceLocker.Services
{
    public static partial class BaiduFaceSDKInterop
    {
        #region 完整错误码定义

        /// <summary>
        /// 百度人脸SDK错误码定义
        /// </summary>
        public static class BaiduFaceErrorCodes
        {
            // 成功状态
            public const int SUCCESS = 0;

            // 通用错误
            public const int ILLEGAL_PARAMS = -1;
            public const int MEMORY_ALLOCATION_FAILED = -2;
            public const int INSTANCE_IS_EMPTY = -3;
            public const int MODEL_IS_EMPTY = -4;
            public const int UNSUPPORT_ABILITY_TYPE = -5;
            public const int UNSUPPORT_INFER_TYPE = -6;
            public const int NN_CREATE_FAILED = -7;
            public const int NN_INIT_FAILED = -8;
            public const int IMAGE_IS_EMPTY = -9;
            public const int ABILITY_INIT_FAILED = -10;
            public const int ABILITY_UNLOAD = -11;
            public const int ABILITY_ALREADY_LOADED = -12;
            public const int NOT_AUTHORIZED = -13;
            public const int ABILITY_RUN_EXCEPTION = -14;
            public const int UNSUPPORT_IMAGE_TYPE = -15;
            public const int IMAGE_TRANSFORM_FAILED = -16;

            // 系统级错误
            public const int SYSTEM_ERROR = -1001;
            public const int PARAM_ERROR = -1002;
            public const int DB_OP_FAILED = -1003;
            public const int NO_DATA = -1004;
            public const int RECORD_UNEXIST = -1005;
            public const int RECORD_ALREADY_EXIST = -1006;
            public const int FILE_NOT_EXIST = -1007;
            public const int GET_FEATURE_FAIL = -1008;
            public const int FILE_TOO_BIG = -1009;
            public const int FACE_RESOURCE_NOT_EXIST = -1010;
            public const int FEATURE_LEN_ERROR = -1011;
            public const int DETECT_NO_FACE = -1012;
            public const int CAMERA_ERROR = -1013;
            public const int FACE_INSTANCE_ERROR = -1014;

            // 授权相关错误
            public const int LICENSE_FILE_NOT_EXIST = -1015;
            public const int LICENSE_KEY_EMPTY = -1016;
            public const int LICENSE_KEY_INVALID = -1017;
            public const int LICENSE_KEY_EXPIRE = -1018;
            public const int LICENSE_ALREADY_USED = -1019;
            public const int DEVICE_ID_EMPTY = -1020;

            // 网络相关错误
            public const int NETWORK_TIMEOUT = -1021;
            public const int NETWORK_ERROR = -1022;

            // 配置相关错误
            public const int CONF_INI_UNEXIST = -1023;

            /// <summary>
            /// 获取错误码对应的描述信息
            /// </summary>
            public static string GetErrorMessage(int errorCode)
            {
                return errorCode switch
                {
                    SUCCESS => "操作成功",
                    ILLEGAL_PARAMS => "非法参数",
                    MEMORY_ALLOCATION_FAILED => "内存分配失败",
                    INSTANCE_IS_EMPTY => "API实例为空",
                    MODEL_IS_EMPTY => "模型内容为空",
                    UNSUPPORT_ABILITY_TYPE => "不支持的能力类型",
                    UNSUPPORT_INFER_TYPE => "不支持的预测库类型",
                    NN_CREATE_FAILED => "预测库对象创建失败",
                    NN_INIT_FAILED => "预测库对象初始化失败",
                    IMAGE_IS_EMPTY => "图像数据为空",
                    ABILITY_INIT_FAILED => "人脸能力初始化失败",
                    ABILITY_UNLOAD => "人脸能力未加载",
                    ABILITY_ALREADY_LOADED => "人脸能力已加载",
                    NOT_AUTHORIZED => "未授权",
                    ABILITY_RUN_EXCEPTION => "人脸能力运行异常",
                    UNSUPPORT_IMAGE_TYPE => "不支持的图像类型",
                    IMAGE_TRANSFORM_FAILED => "图像转换失败",
                    SYSTEM_ERROR => "系统错误",
                    PARAM_ERROR => "参数错误",
                    DB_OP_FAILED => "数据库操作失败",
                    NO_DATA => "没有数据",
                    RECORD_UNEXIST => "记录不存在",
                    RECORD_ALREADY_EXIST => "记录已存在",
                    FILE_NOT_EXIST => "文件不存在",
                    GET_FEATURE_FAIL => "提取特征值失败",
                    FILE_TOO_BIG => "文件太大",
                    FACE_RESOURCE_NOT_EXIST => "人脸资源文件不存在",
                    FEATURE_LEN_ERROR => "特征值长度错误",
                    DETECT_NO_FACE => "未检测到人脸",
                    CAMERA_ERROR => "摄像头错误或不存在",
                    FACE_INSTANCE_ERROR => "人脸引擎初始化错误",
                    LICENSE_FILE_NOT_EXIST => "授权文件不存在",
                    LICENSE_KEY_EMPTY => "授权序列号为空",
                    LICENSE_KEY_INVALID => "授权序列号无效",
                    LICENSE_KEY_EXPIRE => "授权序列号过期",
                    LICENSE_ALREADY_USED => "授权序列号已被使用",
                    DEVICE_ID_EMPTY => "设备指纹为空",
                    NETWORK_TIMEOUT => "网络超时",
                    NETWORK_ERROR => "网络错误",
                    CONF_INI_UNEXIST => "配置文件face.ini不存在",
                    _ => $"未知错误 (错误码: {errorCode})"
                };
            }
        }

        #endregion

        #region JSON响应数据结构

        /// <summary>
        /// 基本响应结构
        /// </summary>
        public class BasicResponse
        {
            public int ErrorCode { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;
            public long LogId { get; set; }
            public bool IsSuccess => ErrorCode == 0;
        }

        /// <summary>
        /// 用户注册响应
        /// </summary>
        public class UserRegistrationResponse : BasicResponse
        {
            public string FaceToken { get; set; } = string.Empty;
            public FaceLocation Location { get; set; } = new FaceLocation();
        }

        /// <summary>
        /// 人脸位置信息
        /// </summary>
        public class FaceLocation
        {
            public float Left { get; set; }
            public float Top { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }
            public int Rotation { get; set; }
        }

        /// <summary>
        /// 人脸识别响应
        /// </summary>
        public class FaceRecognitionResponse : BasicResponse
        {
            public string FaceToken { get; set; } = string.Empty;
            public List<RecognizedUser> UserList { get; set; } = new List<RecognizedUser>();
            public int ResultNum { get; set; }
        }

        /// <summary>
        /// 识别到的用户信息
        /// </summary>
        public class RecognizedUser
        {
            public string GroupId { get; set; } = string.Empty;
            public long UserId { get; set; } = 0;
            public string UserInfo { get; set; } = string.Empty;
            public float Score { get; set; }
        }

        /// <summary>
        /// 用户列表响应
        /// </summary>
        public class UserListResponse : BasicResponse
        {
            public string[] UserIdList { get; set; } = Array.Empty<string>();
        }

        #endregion

        #region 图像处理工具方法

        /// <summary>
        /// 图像处理工具类
        /// </summary>
        public static class ImageUtils
        {
            /// <summary>
            /// 加载图像文件
            /// </summary>
            public static Mat LoadImage(string filePath)
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        LogError($"图像文件不存在: {filePath}");
                        return null;
                    }

                    Mat image = Cv2.ImRead(filePath, ImreadModes.Color);
                    if (image.Empty())
                    {
                        LogError($"加载图像失败: {filePath}");
                        return null;
                    }

                    LogInfo($"成功加载图像: {filePath}, 尺寸: {image.Width}x{image.Height}");
                    return image;
                }
                catch (Exception ex)
                {
                    LogError($"加载图像时发生异常: {ex.Message}");
                    return null;
                }
            }

            /// <summary>
            /// 调整图像尺寸
            /// </summary>
            public static Mat ResizeImage(Mat image, int maxWidth = 1920, int maxHeight = 1080)
            {
                if (image == null || image.Empty())
                    return null;

                if (image.Width <= maxWidth && image.Height <= maxHeight)
                    return image;

                double scale = Math.Min((double)maxWidth / image.Width, (double)maxHeight / image.Height);
                int newWidth = (int)(image.Width * scale);
                int newHeight = (int)(image.Height * scale);

                Mat resized = new Mat();
                Cv2.Resize(image, resized, new Size(newWidth, newHeight));

                LogInfo($"调整图像尺寸: {image.Width}x{image.Height} -> {newWidth}x{newHeight}");
                return resized;
            }

            /// <summary>
            /// 转换图像格式为BGR
            /// </summary>
            public static Mat ConvertToBGR(Mat image)
            {
                if (image == null || image.Empty())
                    return null;

                Mat result = new Mat();
                if (image.Channels() == 1)
                {
                    Cv2.CvtColor(image, result, ColorConversionCodes.GRAY2BGR);
                }
                else if (image.Channels() == 4)
                {
                    Cv2.CvtColor(image, result, ColorConversionCodes.BGRA2BGR);
                }
                else
                {
                    image.CopyTo(result);
                }

                return result;
            }

            /// <summary>
            /// 图像预处理（调整尺寸和转换格式）
            /// </summary>
            public static Mat PreprocessImage(Mat image, int maxWidth = 1920, int maxHeight = 1080)
            {
                if (image == null || image.Empty())
                    return null;

                // 转换格式
                Mat converted = ConvertToBGR(image);
                if (converted == null)
                    return null;

                // 调整尺寸
                Mat resized = ResizeImage(converted, maxWidth, maxHeight);
                if (resized == null)
                {
                    converted.Dispose();
                    return null;
                }

                converted.Dispose();
                return resized;
            }

            /// <summary>
            /// 保存图像到文件
            /// </summary>
            public static bool SaveImage(Mat image, string filePath)
            {
                try
                {
                    if (image == null || image.Empty())
                    {
                        LogError("无法保存空图像");
                        return false;
                    }

                    Cv2.ImWrite(filePath, image);
                    LogInfo($"图像已保存: {filePath}");
                    return true;
                }
                catch (Exception ex)
                {
                    LogError($"保存图像失败: {ex.Message}");
                    return false;
                }
            }
        }

        #endregion
    }
    #region JSON结果解析工具类

    /// <summary>
    /// JSON结果解析工具类
    /// </summary>
    public static class JsonResultParser
    {
        /// <summary>
        /// 解析基本响应结果
        /// </summary>
        public static BasicResponse ParseBasicResponse(string jsonResult)
        {
            try
            {
                if (string.IsNullOrEmpty(jsonResult))
                    return new BasicResponse { ErrorCode = -1, ErrorMessage = "JSON结果为空" };

                var jObject = JObject.Parse(jsonResult);
                return new BasicResponse
                {
                    ErrorCode = jObject["error_code"]?.Value<int>() ?? 0,
                    ErrorMessage = jObject["error_msg"]?.Value<string>() ?? string.Empty,
                    LogId = jObject["log_id"]?.Value<long>() ?? 0
                };
            }
            catch (Exception ex)
            {
                return new BasicResponse
                {
                    ErrorCode = -1,
                    ErrorMessage = $"解析JSON结果失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 解析用户注册结果
        /// </summary>
        public static UserRegistrationResponse ParseUserRegistrationResponse(string jsonResult)
        {
            var response = new UserRegistrationResponse();
            var basic = ParseBasicResponse(jsonResult);

            if (basic.ErrorCode != 0)
            {
                response.ErrorCode = basic.ErrorCode;
                response.ErrorMessage = basic.ErrorMessage;
                return response;
            }

            try
            {
                var jObject = JObject.Parse(jsonResult);

                var dataToken = jObject["data"];
                if (dataToken != null)
                {
                    var resultToken = dataToken["result"];
                    if (resultToken != null)
                    {
                        response.FaceToken = resultToken["face_token"]?.Value<string>();

                        var locationToken = resultToken["location"];
                        if (locationToken != null)
                        {
                            response.Location = new FaceLocation
                            {
                                Left = locationToken["left"]?.Value<float>() ?? 0,
                                Top = locationToken["top"]?.Value<float>() ?? 0,
                                Width = locationToken["width"]?.Value<float>() ?? 0,
                                Height = locationToken["height"]?.Value<float>() ?? 0,
                                Rotation = locationToken["rotation"]?.Value<int>() ?? 0
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                response.ErrorCode = -1;
                response.ErrorMessage = $"解析用户注册结果失败: {ex.Message}";
            }

            return response;
        }

        /// <summary>
        /// 解析人脸识别结果
        /// </summary>
        public static FaceRecognitionResponse ParseFaceRecognitionResponse(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json))
                {
                    return new FaceRecognitionResponse { ErrorCode = -1, ErrorMessage = "JSON字符串为空" };
                }

                // 使用 Newtonsoft.Json 解析
                var jObject = JObject.Parse(json);

                var response = new FaceRecognitionResponse
                {
                    ErrorCode = jObject["errno"]?.Value<int>() ?? -1,
                    ErrorMessage = jObject["msg"]?.Value<string>() ?? "未知错误"
                };

                // 检查是否有错误
                if (response.ErrorCode != 0)
                {
                    return response;
                }

                // 解析 data 部分
                var data = jObject["data"];
                if (data != null)
                {
                    response.FaceToken = data["face_token"]?.Value<string>() ?? "";
                    response.LogId = data["log_id"]?.Value<long>() ?? 0;
                    response.ResultNum = data["result_num"]?.Value<int>() ?? 0;

                    // 解析用户列表
                    var resultArray = data["result"] as JArray;
                    if (resultArray != null && resultArray.Count > 0)
                    {
                        response.UserList = new List<RecognizedUser>();

                        foreach (var item in resultArray)
                        {
                            var user = new RecognizedUser
                            {
                                GroupId = item["group_id"]?.Value<string>() ?? "",
                                UserId = item["user_id"]?.Value<long>() ?? 0,
                                Score = item["score"]?.Value<float>() ?? 0f,
                            };

                            response.UserList.Add(user);
                        }
                    }
                }

                return response;
            }
            catch (JsonException ex)
            {
                // JSON 解析异常
                return new FaceRecognitionResponse
                {
                    ErrorCode = -1,
                    ErrorMessage = $"JSON解析错误: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                // 其他异常
                return new FaceRecognitionResponse
                {
                    ErrorCode = -1,
                    ErrorMessage = $"解析异常: {ex.Message}"
                };
            }
        }


        /// <summary>
        /// 解析用户列表结果
        /// </summary>
        public static UserListResponse ParseUserListResponse(string jsonResult)
        {
            var response = new UserListResponse();
            var basic = ParseBasicResponse(jsonResult);

            if (basic.ErrorCode != 0)
            {
                response.ErrorCode = basic.ErrorCode;
                response.ErrorMessage = basic.ErrorMessage;
                return response;
            }

            try
            {
                var jObject = JObject.Parse(jsonResult);
                var resultToken = jObject["data"];

                if (resultToken != null)
                {
                    response.UserIdList = resultToken["user_id_list"]?.ToObject<string[]>() ?? Array.Empty<string>();
                }
            }
            catch (Exception ex)
            {
                response.ErrorCode = -1;
                response.ErrorMessage = $"解析用户列表失败: {ex.Message}";
            }

            return response;
        }
    }

    #endregion

    #region SDK使用示例和工具类

    /// <summary>
    /// SDK使用示例类
    /// </summary>
    public static class UsageExamples
    {
        /// <summary>
        /// 完整的人脸注册流程示例
        /// </summary>
        public static void Example_UserRegistration()
        {
            BaiduFaceSDKInterop.LogInfo("=== 人脸注册流程示例 ===");

            // 1. 初始化SDK
            var initResult = InitializeSDK("/opt/face_offline_sdk/models");
            if (!initResult.Success)
            {
                LogError($"SDK初始化失败: {initResult.Message}");
                return;
            }

            // 2. 检查授权状态
            var authResult = CheckAuthStatus();
            if (!authResult.Success || !authResult.IsAuthorized)
            {
                LogError($"SDK授权检查失败: {authResult.Message}");
                return;
            }

            // 3. 加载图像
            Mat image = ImageUtils.LoadImage("/path/to/user/image.jpg");
            if (image == null)
                return;

            // 4. 预处理图像
            Mat processedImage = ImageUtils.PreprocessImage(image);
            if (processedImage == null)
            {
                image.Dispose();
                return;
            }

            // 5. 人脸检测
            var detectResult = DetectFaces(processedImage, ImageType.RGB);
            if (!detectResult.Success || detectResult.FaceCount == 0)
            {
                LogError("未检测到人脸，无法注册");
                image.Dispose();
                processedImage.Dispose();
                return;
            }

            // 6. 用户注册
            var registerResult = RegisterUserByImage(processedImage, "user123", "group1", "用户信息");
            if (registerResult.Success)
            {
                // 解析注册结果
                var parsedResult = JsonResultParser.ParseUserRegistrationResponse(registerResult.JsonResult);
                if (parsedResult.IsSuccess)
                {
                    LogInfo($"用户注册成功，FaceToken: {parsedResult.FaceToken}");
                }
                else
                {
                    LogError($"用户注册结果解析失败: {parsedResult.ErrorMessage}");
                }
            }
            else
            {
                LogError($"用户注册失败: {registerResult.Message}");
            }

            // 7. 清理资源
            image.Dispose();
            processedImage.Dispose();
            ReleaseSDK();

            LogInfo("=== 人脸注册流程完成 ===");
        }

        /// <summary>
        /// 人脸识别流程示例
        /// </summary>
        public static void Example_FaceRecognition()
        {
            LogInfo("=== 人脸识别流程示例 ===");

            // 1. 初始化SDK
            var initResult = InitializeSDK();
            if (!initResult.Success)
            {
                LogError($"SDK初始化失败: {initResult.Message}");
                return;
            }

            // 2. 加载查询图像
            Mat queryImage = ImageUtils.LoadImage("/path/to/query/image.jpg");
            if (queryImage == null)
                return;

            // 3. 预处理图像
            Mat processedImage = ImageUtils.PreprocessImage(queryImage);
            if (processedImage == null)
            {
                queryImage.Dispose();
                return;
            }

            // 4. 人脸识别
            var identifyResult = IdentifyByImage(processedImage, "group1,group2", null, FeatureType.VISIBLE_LIVING);
            if (identifyResult.Success)
            {
                // 解析识别结果
                var parsedResult = JsonResultParser.ParseFaceRecognitionResponse(identifyResult.JsonResult);
                if (parsedResult.IsSuccess && parsedResult.UserList.Count > 0)
                {
                    var bestMatch = parsedResult.UserList[0];
                    LogInfo($"识别成功，最佳匹配: 用户ID={bestMatch.UserId}, 得分={bestMatch.Score:F2}");
                }
                else
                {
                    LogInfo("未识别到匹配的用户");
                }
            }
            else
            {
                LogError($"人脸识别失败: {identifyResult.Message}");
            }

            // 5. 清理资源
            queryImage.Dispose();
            processedImage.Dispose();
            ReleaseSDK();

            LogInfo("=== 人脸识别流程完成 ===");
        }

        /// <summary>
        /// 批量处理示例
        /// </summary>
        public static void Example_BatchProcessing()
        {
            LogInfo("=== 批量处理示例 ===");

            // 1. 初始化SDK
            var initResult = InitializeSDK();
            if (!initResult.Success)
                return;

            // 2. 获取图像文件列表
            string[] imageFiles = Directory.GetFiles("/path/to/images/", "*.jpg");
            if (imageFiles.Length == 0)
            {
                LogError("未找到图像文件");
                return;
            }

            // 3. 批量处理
            var images = new List<Mat>();
            foreach (string file in imageFiles)
            {
                Mat image = ImageUtils.LoadImage(file);
                if (image != null)
                {
                    images.Add(ImageUtils.PreprocessImage(image));
                    image.Dispose();
                }
            }

            if (images.Count > 0)
            {
                var batchResult = BatchDetectAndExtract(images.ToArray(), FeatureType.VISIBLE_LIVING);
                if (batchResult.Success)
                {
                    LogInfo($"批量处理完成: {batchResult.SuccessCount}/{batchResult.ProcessedCount} 成功");
                }
            }

            // 4. 清理资源
            foreach (var image in images)
            {
                image?.Dispose();
            }
            ReleaseSDK();

            LogInfo("=== 批量处理完成 ===");
        }
    }

    #endregion
}