using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FaceLocker.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static FaceLocker.Services.BaiduFaceSDKInterop;

namespace FaceLocker.Services
{
    /// <summary>
    /// 百度人脸识别服务
    /// 提供人脸识别相关的业务功能，包括初始化、用户注册、人脸识别等
    /// </summary>
    public class BaiduFaceService : IDisposable
    {

        private static bool _isInitialized = false;
        private static bool _staticIsAuthorized = false;
        private static IntPtr _apiInstance = IntPtr.Zero;
        private readonly object _lockObject = new object();
        private static int _instanceCount = 0;

        private readonly ILogger<BaiduFaceService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private bool _disposed = false;

        #region 获取SDK是否已初始化
        /// <summary>
        /// 获取SDK是否已初始化
        /// </summary>
        public bool IsInitialized
        {
            get
            {
                lock (_lockObject)
                {
                    return _isInitialized && BaiduFaceSDKInterop.IsInitialized();
                }
            }
        }
        public bool IsAuthorized => _staticIsAuthorized;

        private static readonly SemaphoreSlim _sdkSemaphore = new SemaphoreSlim(1, 1);

        #endregion

        #region 构造函数
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="userService">用户服务</param>
        public BaiduFaceService(ILogger<BaiduFaceService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;

            /// 初始化百度人脸SDK
            InitializeSDK();
        }
        #endregion

        #region 初始化人脸识别服务
        /// <summary>
        /// 初始化人脸识别服务
        /// </summary>
        /// <returns>初始化是否成功</returns>
        public void InitializeSDK()
        {
            lock (_lockObject)
            {
                try
                {
                    if (_isInitialized)
                    {
                        _logger.LogInformation("百度人脸SDK已初始化，跳过初始化");
                        return;
                    }
                    // 使用null作为模型路径，让SDK使用默认路径
                    var result = BaiduFaceSDKInterop.InitializeSDK(string.Empty);
                    if (result.Success)
                    {
                        _isInitialized = true;
                        _logger.LogInformation("百度人脸SDK初始化成功");

                        // 检查授权状态
                        var authResult = BaiduFaceSDKInterop.CheckAuthStatus();
                        if (!authResult.Success || !authResult.IsAuthorized)
                        {
                            _logger.LogError("SDK未授权");
                        }

                        // 加载人脸库到内存
                        BaiduFaceSDKInterop.LoadDatabase();
                    }
                    else
                    {
                        _isInitialized = false;
                        _logger.LogError($"百度人脸SDK初始化失败: {result.Message}");
                    }
                }
                catch (Exception ex)
                {
                    _isInitialized = false;
                    _logger.LogError($"百度人脸SDK初始化异常: {ex.Message}");
                }
            }
        }
        #endregion

        #region 检测图像中的人脸
        /// <summary>
        /// 检测图像中的人脸
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <returns>检测到的人脸框数组</returns>
        public async Task<WrapperFaceBox[]> DetectFacesAsync(Mat image)
        {
            if (image == null || image.Empty())
            {
                _logger.LogError("DetectFacesAsync: 输入图像无效");
                return [];
            }

            // 使用信号量确保线程安全
            await _sdkSemaphore.WaitAsync();

            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        // 确保图像是BGR格式（3通道）
                        Mat processedImage = image;
                        bool needsDispose = false;

                        if (image.Channels() == 4)
                        {
                            _logger.LogDebug("DetectFacesAsync: 转换4通道图像到3通道BGR");
                            processedImage = new Mat();
                            Cv2.CvtColor(image, processedImage, ColorConversionCodes.BGRA2BGR);
                            needsDispose = true;
                        }
                        else if (image.Channels() == 1)
                        {
                            _logger.LogDebug("DetectFacesAsync: 转换灰度图像到BGR");
                            processedImage = new Mat();
                            Cv2.CvtColor(image, processedImage, ColorConversionCodes.GRAY2BGR);
                            needsDispose = true;
                        }
                        else if (image.Channels() != 3)
                        {
                            _logger.LogError($"DetectFacesAsync: 不支持的图像通道数: {image.Channels()}，需要3通道(BGR)或4通道(BGRA)");
                            return [];
                        }

                        // 检查图像指针
                        if (processedImage.CvPtr == IntPtr.Zero)
                        {
                            _logger.LogError("DetectFacesAsync: 图像指针为空");
                            if (needsDispose) processedImage.Dispose();
                            return [];
                        }

                        IntPtr boxesPtr = IntPtr.Zero;
                        int faceCount = 0;

                        // 使用RGB检测类型（0）
                        int result = BaiduFaceSDKInterop.baidu_face_detect(ref boxesPtr, ref faceCount, processedImage.CvPtr, 0);

                        if (result != BaiduFaceErrorCodes.SUCCESS && faceCount <= 0)
                        {
                            string errorMsg = BaiduFaceSDKInterop.GetErrorMessage(result);
                            _logger.LogWarning($"DetectFacesAsync: 人脸检测失败，错误码: {result} - {errorMsg}");
                            if (needsDispose) processedImage.Dispose();
                            return [];
                        }

                        if (faceCount == 0)
                        {
                            if (needsDispose) processedImage.Dispose();
                            return [];
                        }

                        // 将非托管内存转换为托管数组
                        WrapperFaceBox[] faces = new WrapperFaceBox[faceCount];
                        int structSize = Marshal.SizeOf(typeof(WrapperFaceBox));

                        for (int i = 0; i < faceCount; i++)
                        {
                            IntPtr currentBoxPtr = boxesPtr + i * structSize;
                            faces[i] = Marshal.PtrToStructure<WrapperFaceBox>(currentBoxPtr);
                        }

                        // 释放非托管内存
                        BaiduFaceSDKInterop.baidu_face_free_detect_result(boxesPtr);

                        // 释放临时图像
                        if (needsDispose) processedImage.Dispose();

                        return faces;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "DetectFacesAsync: 人脸检测过程中发生异常");
                        return [];
                    }
                });
            }
            finally
            {
                _sdkSemaphore.Release();
            }
        }
        #endregion

        #region 提取人脸特征码
        /// <summary>
        /// 提取人脸特征值（异步版本）
        /// 返回分数最高的特征值
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="featureType">特征值类型</param>
        /// <returns>特征值提取结果</returns>
        public async Task<FeatureExtractionResult> ExtractFeatureAsync(Mat image, FeatureType featureType = FeatureType.VISIBLE_LIVING)
        {
            _logger.LogInformation("开始异步提取人脸特征值");

            if (!IsInitialized)
            {
                _logger.LogError("SDK未初始化");
                return new FeatureExtractionResult
                {
                    Success = false,
                    Message = "SDK未初始化"
                };
            }

            if (image == null || image.Empty())
            {
                _logger.LogError("输入图像无效");
                return new FeatureExtractionResult
                {
                    Success = false,
                    Message = "输入图像无效"
                };
            }

            // 克隆图像以确保线程安全
            Mat clonedImage = null;
            try
            {
                clonedImage = image.Clone();
                _logger.LogDebug($"克隆图像成功，尺寸: {clonedImage.Width}x{clonedImage.Height}, 通道数: {clonedImage.Channels()}");

                // 使用信号量确保线程安全
                await _sdkSemaphore.WaitAsync();

                return await Task.Run(() =>
                {
                    try
                    {
                        _logger.LogDebug($"提取人脸特征值，图像尺寸: {clonedImage.Width}x{clonedImage.Height}, 类型: {featureType}");

                        // 检查图像指针有效性
                        if (clonedImage.CvPtr == IntPtr.Zero)
                        {
                            _logger.LogError("图像指针为空，图像数据无效");
                            return new FeatureExtractionResult
                            {
                                Success = false,
                                Message = "图像数据无效"
                            };
                        }

                        // 确保图像数据有效
                        if (clonedImage.Data == IntPtr.Zero)
                        {
                            _logger.LogError("图像数据指针为空");
                            return new FeatureExtractionResult
                            {
                                Success = false,
                                Message = "图像数据指针为空"
                            };
                        }

                        IntPtr featuresPtr = IntPtr.Zero;
                        IntPtr boxesPtr = IntPtr.Zero;
                        int featureCount = 0;

                        // 调用SDK特征提取函数
                        int result = BaiduFaceSDKInterop.baidu_face_feature(ref featuresPtr, ref boxesPtr, ref featureCount, clonedImage.CvPtr, (int)featureType);

                        if (result < 0)
                        {
                            string errorMsg = BaiduFaceSDKInterop.GetErrorMessage(result);
                            _logger.LogError($"特征值提取失败，错误码: {result} - {errorMsg}");
                            return new FeatureExtractionResult
                            {
                                Success = false,
                                ErrorCode = result,
                                Message = errorMsg
                            };
                        }

                        if (featureCount == 0)
                        {
                            _logger.LogDebug("未提取到特征值");
                            return new FeatureExtractionResult
                            {
                                Success = true,
                                FeatureCount = 0,
                                Features = Array.Empty<float[]>(),
                                FaceBoxes = Array.Empty<WrapperFaceBox>()
                            };
                        }

                        _logger.LogDebug($"成功提取 {featureCount} 个特征值，开始转换结果");

                        // 将非托管内存转换为托管数组
                        float[][] features = new float[featureCount][];
                        WrapperFaceBox[] faceBoxes = new WrapperFaceBox[featureCount];

                        int featureStructSize = Marshal.SizeOf(typeof(WrapperFeature));
                        int boxStructSize = Marshal.SizeOf(typeof(WrapperFaceBox));

                        for (int i = 0; i < featureCount; i++)
                        {
                            // 提取特征值
                            IntPtr currentFeaturePtr = IntPtr.Add(featuresPtr, i * featureStructSize);
                            WrapperFeature wrapperFeature = Marshal.PtrToStructure<WrapperFeature>(currentFeaturePtr);

                            // 确保特征值数据有效
                            if (wrapperFeature.data != null && wrapperFeature.size == 128)
                            {
                                features[i] = new float[128];
                                Array.Copy(wrapperFeature.data, features[i], 128);
                            }
                            else
                            {
                                _logger.LogWarning($"第 {i} 个特征值数据无效，使用空数组");
                                features[i] = new float[128];
                            }

                            // 提取人脸框
                            IntPtr currentBoxPtr = IntPtr.Add(boxesPtr, i * boxStructSize);
                            faceBoxes[i] = Marshal.PtrToStructure<WrapperFaceBox>(currentBoxPtr);
                        }

                        // 释放非托管内存
                        BaiduFaceSDKInterop.baidu_face_free_feature_result(featuresPtr, boxesPtr);

                        // 找到分数最高的特征值
                        int bestIndex = FindBestFaceIndex(faceBoxes);
                        float[][] bestFeatures = bestIndex >= 0 ? new float[][] { features[bestIndex] } : Array.Empty<float[]>();
                        WrapperFaceBox[] bestFaceBoxes = bestIndex >= 0 ? new WrapperFaceBox[] { faceBoxes[bestIndex] } : Array.Empty<WrapperFaceBox>();

                        _logger.LogInformation($"特征值提取成功，找到 {featureCount} 个人脸，选择第 {bestIndex + 1} 个作为最佳特征值（分数: {faceBoxes[bestIndex].score:F4}）");

                        return new FeatureExtractionResult
                        {
                            Success = true,
                            FeatureCount = bestFeatures.Length,
                            Features = bestFeatures,
                            FaceBoxes = bestFaceBoxes,
                            Message = $"成功提取 {bestFeatures.Length} 个最佳特征值"
                        };
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "特征值提取过程中发生异常");
                        return new FeatureExtractionResult
                        {
                            Success = false,
                            ErrorCode = -9999,
                            Message = $"特征值提取异常: {ex.Message}"
                        };
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "异步特征提取任务发生异常");
                return new FeatureExtractionResult
                {
                    Success = false,
                    Message = $"异步特征提取失败: {ex.Message}"
                };
            }
            finally
            {
                _sdkSemaphore.Release();
                clonedImage?.Dispose();
                _logger.LogDebug("克隆的图像资源已释放");
            }
        }

        /// <summary>
        /// 找到分数最高的人脸索引
        /// </summary>
        private int FindBestFaceIndex(WrapperFaceBox[] faceBoxes)
        {
            if (faceBoxes == null || faceBoxes.Length == 0)
                return -1;

            int bestIndex = 0;
            float bestScore = faceBoxes[0].score;

            for (int i = 1; i < faceBoxes.Length; i++)
            {
                if (faceBoxes[i].score > bestScore)
                {
                    bestScore = faceBoxes[i].score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }
        #endregion

        #region 从用户Avatar字段生成人脸特征码
        /// <summary>
        /// 从用户Avatar字段（base64编码）生成人脸特征码
        /// 成功提取特征码后更新用户相关字段
        /// </summary>
        /// <param name="user">用户对象</param>
        /// <returns>操作是否成功</returns>
        public async Task<bool> GenerateFaceFeatureFromAvatarAsync(User user)
        {            // 添加用户对象空值检查 - 防止空引用异常
            if (user == null)
            {
                _logger.LogError("用户对象为空，无法生成人脸特征码");
                return false;
            }

            _logger.LogInformation("开始从用户Avatar生成人脸特征码 - 用户: {UserName} (ID: {UserId})", user.Name, user.Id);

            if (!_isInitialized)
            {
                _logger.LogError("百度人脸识别服务未初始化，无法生成特征码");
                return false;
            }

            if (string.IsNullOrEmpty(user.Avatar))
            {
                _logger.LogWarning("用户 {UserName} 的Avatar字段为空，无法生成特征码", user.Name);
                return false;
            }

            Mat avatarImage = null;
            try
            {
                #region 1. Base64解码
                _logger.LogDebug("步骤1: 开始解码Base64 Avatar数据");
                byte[] imageBytes;
                try
                {
                    // 移除可能的Base64前缀（如"data:image/jpeg;base64,"）
                    string base64Data = user.Avatar;
                    if (base64Data.Contains(","))
                    {
                        base64Data = base64Data.Split(',')[1];
                        _logger.LogDebug("检测到Base64前缀，已移除前缀");
                    }

                    imageBytes = Convert.FromBase64String(base64Data);
                    _logger.LogDebug("Base64解码成功，数据长度: {DataLength} 字节", imageBytes.Length);
                }
                catch (FormatException ex)
                {
                    _logger.LogError(ex, "Base64解码失败: Avatar数据格式不正确");
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Base64解码过程中发生异常");
                    return false;
                }
                #endregion

                #region 2. 图像解码
                _logger.LogDebug("步骤2: 开始图像解码");
                try
                {
                    avatarImage = Cv2.ImDecode(imageBytes, ImreadModes.Color);
                    if (avatarImage == null || avatarImage.Empty())
                    {
                        _logger.LogError("图像解码失败: 无法从字节数据创建Mat对象");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "图像解码过程中发生异常");
                    return false;
                }
                #endregion

                #region 3. 特征提取
                _logger.LogDebug("步骤3: 开始特征提取");

                var featureResult = await ExtractFeatureAsync(avatarImage);

                if (!featureResult.Success || featureResult.FeatureCount == 0)
                {
                    _logger.LogError($"特征提取失败: {featureResult.Message}");
                    return false;
                }

                // 获取最佳特征值
                float[] bestFeature = featureResult.Features[0];
                WrapperFaceBox bestFaceBox = featureResult.FaceBoxes[0];

                _logger.LogDebug("特征提取成功，置信度: {Confidence:F4}", bestFaceBox.score);
                #endregion

                #region 4. 注册到百度人脸库
                _logger.LogDebug("步骤4: 开始注册到百度人脸库");
                try
                {
                    // 准备注册参数
                    string userId = user.Id.ToString(); // 确保转换为字符串
                    string groupId = user.RoleId.ToString(); // 确保转换为字符串
                    string userInfo = user.Name;

                    _logger.LogInformation("注册用户 {UserName} 到百度人脸库 - 用户ID: {UserId}, 组ID: {GroupId}, 用户信息: {UserInfo}",
                        user.Name, userId, groupId, userInfo);

                    // 调用SDK注册到百度人脸库
                    var registrationResult = await Task.Run(() => BaiduFaceSDKInterop.RegisterUserByFeature(bestFeature, userId, groupId, userInfo));

                    if (!registrationResult.Success)
                    {
                        _logger.LogError("百度人脸库注册失败: {ErrorMessage}", registrationResult.Message);
                        return false;
                    }

                    // 解析注册结果
                    var regResult = JsonResultParser.ParseUserRegistrationResponse(registrationResult.JsonResult);
                    if (!regResult.IsSuccess)
                    {
                        _logger.LogError("百度人脸库注册失败，错误码: {ErrorCode}, 错误信息: {ErrorMessage}",
                            regResult.ErrorCode, regResult.ErrorMessage);
                        return false;
                    }

                    _logger.LogInformation("用户成功注册到百度人脸库 - 人脸令牌: {FaceToken}", regResult.FaceToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "注册到百度人脸库过程中发生异常");
                    return false;
                }
                #endregion

                #region 5. 更新用户数据
                _logger.LogDebug("步骤5: 开始更新用户数据");
                try
                {
                    byte[] featureBytes = ConvertFloatsToBytes(bestFeature);

                    // 使用新的上下文更新用户数据
                    bool updateSuccess = await UpdateUserInNewContextAsync(user, featureBytes, bestFaceBox.score);

                    if (!updateSuccess)
                    {
                        _logger.LogError("更新用户数据失败");
                        return false;
                    }

                    _logger.LogInformation("用户数据更新成功 - 特征码长度: {FeatureBytesLength} 字节, 置信度: {Confidence:P2}", featureBytes.Length, bestFaceBox.score);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "更新用户数据到数据库时发生异常");
                    return false;
                }
                #endregion

                _logger.LogInformation("用户 {UserName} 的人脸特征码生成和注册成功完成", user.Name);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从用户Avatar生成人脸特征码过程中发生异常");
                return false;
            }
            finally
            {
                avatarImage?.Dispose();
                _logger.LogDebug("Avatar图像资源已清理");
            }
        }
        #endregion

        #region 创建新的DbContext实例
        /// <summary>
        /// 创建新的DbContext实例以避免实体跟踪冲突
        /// </summary>
        private async Task<bool> UpdateUserInNewContextAsync(User user, byte[] featureBytes, float confidence)
        {
            try
            {
                _logger.LogDebug("开始在新上下文中更新用户数据");

                // 创建新的作用域
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<FaceLockerDbContext>();

                // 从数据库重新加载用户
                var dbUser = await dbContext.Users.FindAsync(user.Id);
                if (dbUser == null)
                {
                    _logger.LogError("在数据库中找不到用户 ID: {UserId}", user.Id);
                    return false;
                }

                // 更新用户数据
                dbUser.UpdateFaceFeature(featureBytes, confidence);
                dbUser.FaceFeatureVersion++;
                dbUser.LastFaceUpdate = DateTime.Now;
                dbUser.UpdatedAt = DateTime.Now;

                // 保存更改
                int changes = await dbContext.SaveChangesAsync();

                _logger.LogInformation("在新上下文中成功更新用户数据，影响行数: {Changes}", changes);
                return changes > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "在新上下文中更新用户数据时发生异常");
                return false;
            }
        }
        #endregion

        #region 仅进行人脸检测，不进行识别
        /// <summary>
        /// 仅进行人脸检测，不进行识别
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <returns>人脸检测结果</returns>
        public async Task<FaceDetectionResult> DetectFacesOnlyAsync(Mat image)
        {
            if (!_isInitialized)
            {
                _logger.LogError("百度人脸识别服务未初始化");
                return new FaceDetectionResult
                {
                    Success = false,
                    Message = "人脸识别服务未初始化",
                    FaceCount = 0,
                    FaceBoxes = []
                };
            }

            if (image == null || image.Empty())
            {
                _logger.LogWarning("输入图像为空");
                return new FaceDetectionResult
                {
                    Success = false,
                    Message = "输入图像为空",
                    FaceCount = 0,
                    FaceBoxes = []
                };
            }

            try
            {
                // 调用百度SDK进行人脸检测
                var faces = await Task.Run(() => DetectFacesAsync(image));
                if (faces.Length == 0)
                {
                    return new FaceDetectionResult
                    {
                        Success = true,
                        FaceCount = 0,
                        FaceBoxes = []
                    };
                }

                // 如果检测到多个人脸，使用分数最高的那个
                var bestFace = faces.MaxBy(o => o.score);


                return new FaceDetectionResult
                {
                    Success = true,
                    FaceCount = faces.Length,
                    FaceBoxes = faces
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "人脸检测失败");
                return new FaceDetectionResult
                {
                    Success = false,
                    Message = $"人脸检测异常: {ex.Message}",
                    FaceCount = 0,
                    FaceBoxes = []
                };
            }
        }
        #endregion

        #region 按角色进行人脸识别（1:N）
        /// <summary>
        /// 按角色进行人脸识别（1:N）
        /// 只与指定角色的用户进行比对
        /// </summary>
        /// <param name="mat">输入图像</param>
        /// <param name="roleId">角色ID</param>
        /// <returns>人脸识别结果</returns>
        public async Task<FaceRecognitionResult> RecognizeFaceWithRoleAsync(Mat mat, long roleId)
        {
            var recognitionResult = new FaceRecognitionResult();

            if (!_isInitialized)
            {
                recognitionResult.ErrorCode = -1;
                recognitionResult.Success = false;
                recognitionResult.Message = "SDK未初始化";
                _logger.LogError("人脸识别失败: {ErrorMessage}", recognitionResult.Message);
                return recognitionResult;
            }

            if (mat == null || mat.Empty())
            {
                recognitionResult.ErrorCode = -1;
                recognitionResult.Success = false;
                recognitionResult.Message = "图像数据为空";
                _logger.LogWarning("人脸识别失败: {ErrorMessage}", recognitionResult.Message);
                return recognitionResult;
            }

            await _sdkSemaphore.WaitAsync();

            return await Task.Run(() =>
            {
                lock (_lockObject)
                {
                    try
                    {
                        _logger.LogDebug($"开始角色人脸识别，角色ID: {roleId}, 图像尺寸: {mat.Width}x{mat.Height}");

                        if (mat.CvPtr == IntPtr.Zero)
                        {
                            recognitionResult.Success = false;
                            recognitionResult.Message = "图像指针为空，图像数据无效";
                            _logger.LogError("人脸识别失败: {ErrorMessage}", recognitionResult.Message);
                            return recognitionResult;
                        }

                        StringBuilder resultJson = new StringBuilder(BaiduFaceSDKInterop.MAX_BUFFER_SIZE);

                        int result = BaiduFaceSDKInterop.baidu_face_identify_by_mat(resultJson, resultJson.Capacity, mat.CvPtr, roleId.ToString(), null, 0);

                        if (result != 0)
                        {
                            string errorMsg = BaiduFaceSDKInterop.GetErrorMessage(result);
                            BaiduFaceSDKInterop.LogError($"人脸识别失败，错误码: {result} - {errorMsg}");
                            return new FaceRecognitionResult
                            {
                                Success = false,
                                ErrorCode = result,
                                Message = errorMsg,
                                JsonResult = resultJson.ToString()
                            };
                        }
                        recognitionResult.Success = true;
                        recognitionResult.JsonResult = resultJson.ToString();

                        string jsonResult = resultJson.ToString();

                        _logger.LogDebug($"人脸识别完成，结果: {jsonResult}");

                        return recognitionResult;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"摄像头帧识别异常：{ex.Message}");
                        recognitionResult.Message = $"识别异常: {ex.Message}";
                        return recognitionResult;
                    }
                    finally
                    {
                        _sdkSemaphore.Release();
                    }
                }
            });
        }
        #endregion

        #region 将浮点数组转换为字节数组（用于数据库存储）
        /// <summary>
        /// 将浮点数组转换为字节数组（用于数据库存储）
        /// </summary>
        public static byte[] ConvertFloatsToBytes(float[] floats)
        {
            if (floats == null || floats.Length == 0)
            {
                // 避免 Console 噪音：返回空数组即可
                return new byte[0];
            }

            try
            {
                var bytes = new byte[floats.Length * sizeof(float)];
                Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
                // 避免 Console 噪音：不输出转换细节
                return bytes;
            }
            catch (Exception ex)
            {
                // 避免 Console 噪音：由调用方记录异常上下文
                return new byte[0];
            }
        }
        #endregion

        #region 将字节数组转换为浮点数组
        /// <summary>
        /// 将字节数组转换为浮点数组
        /// </summary>
        /// <param name="bytes">字节数组</param>
        /// <returns>浮点数组</returns>
        private float[] ConvertBytesToFloats(byte[] bytes)
        {
            try
            {
                if (bytes == null || bytes.Length == 0)
                {
                    _logger.LogWarning("ConvertBytesToFloats: 字节数组为空");
                    return new float[0];
                }

                // 确保字节数组长度是4的倍数（每个float占4字节）
                if (bytes.Length % 4 != 0)
                {
                    _logger.LogWarning("ConvertBytesToFloats: 字节数组长度 {Length} 不是4的倍数，无法正确转换为float数组", bytes.Length);
                    return new float[0];
                }

                var floats = new float[bytes.Length / 4];
                Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);

                _logger.LogDebug("ConvertBytesToFloats: 成功转换字节数组到浮点数组，输入长度: {InputLength}, 输出长度: {OutputLength}",
                    bytes.Length, floats.Length);

                return floats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConvertBytesToFloats: 转换过程中发生异常");
                return new float[0];
            }
        }
        #endregion

        #region 将WriteableBitmap转换为Mat
        /// <summary>
        /// 将WriteableBitmap转换为Mat
        /// </summary>
        public Mat? ConvertWriteableBitmapToMat(WriteableBitmap bitmap)
        {
            if (bitmap == null)
            {
                _logger.LogWarning("WriteableBitmap为null或空帧");
                return null;
            }

            ILockedFramebuffer? lockedBitmap = null;
            try
            {
                lockedBitmap = bitmap.Lock();
                if (lockedBitmap == null || lockedBitmap.Address == IntPtr.Zero)
                {
                    _logger.LogWarning("锁定WriteableBitmap失败");
                    return null;
                }

                var size = bitmap.PixelSize;
                if (size.Width <= 0 || size.Height <= 0)
                {
                    _logger.LogWarning("WriteableBitmap尺寸无效: {Width}x{Height}", size.Width, size.Height);
                    return null;
                }

                // 直接从锁定的内存创建Mat（BGRA格式）
                var mat = Mat.FromPixelData(
                    size.Height,
                    size.Width,
                    MatType.CV_8UC4,
                    lockedBitmap.Address,
                    lockedBitmap.RowBytes);

                if (mat == null || mat.Empty())
                {
                    _logger.LogWarning("创建Mat失败");
                    return null;
                }

                // 转换为BGR格式（百度SDK需要）
                Mat bgrMat = new Mat();
                Cv2.CvtColor(mat, bgrMat, ColorConversionCodes.BGRA2BGR);
                mat.Dispose();

                if (bgrMat.Empty())
                {
                    _logger.LogWarning("颜色转换失败");
                    bgrMat.Dispose();
                    return null;
                }

                return bgrMat;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "转换WriteableBitmap为Mat时发生异常");
                return null;
            }
            finally
            {
                lockedBitmap?.Dispose();
            }
        }
        #endregion

        #region 将 Mat 图像编码为 JPEG 格式的字节数组
        /// <summary>
        /// 将 Mat 图像编码为 JPEG 格式的字节数组
        /// </summary>
        /// <param name="mat">输入图像（BGR 或 BGRA）</param>
        /// <returns>JPEG 字节数组，失败时返回 null</returns>
        public static byte[]? MatToByteArray(Mat mat)
        {
            if (mat == null || mat.Empty())
            {
                return null;
            }

            try
            {
                // 编码为 JPEG
                var bytes = mat.ToBytes(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, 100));
                return bytes?.Length > 0 ? bytes : null;
            }
            catch (Exception ex)
            {
                LogError($"MatToByteArray 异常: {ex.Message}");
                return null;
            }
        }
        #endregion

        #region 从图像和人脸框中裁剪出置信度最高的人脸区域，并编码为 JPEG 字节数组
        /// <summary>
        /// 从图像和人脸框中裁剪出置信度最高的人脸区域，并编码为 JPEG 字节数组
        /// </summary>
        /// <param name="image">原始图像（BGR格式）</param>
        /// <param name="faceBoxes">人脸框数组（来自百度SDK，归一化中心+宽高）</param>
        /// <param name="targetSize">目标尺寸（可选，默认200x200）</param>
        /// <returns>裁剪后的人脸JPEG字节数组，失败时返回 null</returns>
        public static byte[]? ExtractBestFaceAsByteArray(Mat image, WrapperFaceBox[]? faceBoxes, Size? targetSize = null)
        {
            if (image == null || image.Empty() || faceBoxes == null || faceBoxes.Length == 0)
            {
                LogDebug("图像或人脸框为空，无法提取人脸");
                return null;
            }

            // 找到置信度最高的人脸
            var bestBox = faceBoxes.OrderByDescending(box => box.score).First();
            int imgW = image.Width;
            int imgH = image.Height;

            // 直接使用SDK返回的像素坐标（不再乘以图像尺寸）
            float cx = bestBox.center_x;
            float cy = bestBox.center_y;
            float w = bestBox.width;
            float h = bestBox.height;

            LogInfo($"人脸框坐标: center=({cx:F2},{cy:F2}), width={w:F2}, height={h:F2}, image size=({imgW},{imgH})");

            // 计算左上角
            int x = (int)(cx - w / 2);
            int y = (int)(cy - h / 2);
            // 边界保护
            x = Math.Max(0, x);
            y = Math.Max(0, y);
            w = Math.Min(w, imgW - x);
            h = Math.Min(h, imgH - y);

            // 修正阈值：从10改为15，使用 < 而不是 <=
            if (w < 15 || h < 15)
            {
                LogDebug($"人脸框太小: w={w:F2}, h={h:F2}, 低于阈值15");
                return null;
            }

            try
            {
                using var cropped = new Mat(image, new Rect(x, y, (int)w, (int)h));
                using var finalImage = targetSize.HasValue ? new Mat() : cropped;

                if (targetSize.HasValue)
                {
                    Cv2.Resize(cropped, finalImage, targetSize.Value);
                }

                // 编码为 JPEG，质量90
                return finalImage.ToBytes(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, 90));
            }
            catch (Exception ex)
            {
                LogError($"ExtractBestFaceAsByteArray 异常: {ex.Message}");
                return null;
            }
        }
        #endregion

        #region 获取SDK信息
        /// <summary>
        /// 获取SDK信息
        /// </summary>
        public SDKInfo GetSDKInfo()
        {
            lock (_lockObject)
            {
                return BaiduFaceSDKInterop.GetSDKInfo();
            }
        }
        #endregion

        #region IDisposable
        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                BaiduFaceSDKInterop.ReleaseSDK();
                _logger.LogInformation("百度人脸API实例已销毁");

                _disposed = true;
                _isInitialized = false;
            }
        }

        ~BaiduFaceService()
        {
        }
        #endregion

    }
}