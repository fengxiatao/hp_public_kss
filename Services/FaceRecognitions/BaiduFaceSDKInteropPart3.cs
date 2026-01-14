using OpenCvSharp;
using System;
using System.Runtime.InteropServices;
using static FaceLocker.Services.BaiduFaceSDKInterop;

namespace FaceLocker.Services
{
    public static partial class BaiduFaceSDKInterop
    {
        #region 人脸检测功能

        /// <summary>
        /// 人脸检测
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="imageType">图像类型</param>
        /// <returns>人脸检测结果</returns>
        /// <summary>
        /// 人脸检测 - 修复版本
        /// </summary>
        public static FaceDetectionResult DetectFaces(Mat image, ImageType imageType = ImageType.RGB)
        {
            var result = new FaceDetectionResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            if (image == null || image.Empty())
            {
                result.Success = false;
                result.Message = "输入图像为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始人脸检测，图像尺寸: {image.Width}x{image.Height}, 类型: {imageType}");

                IntPtr boxesPtr = IntPtr.Zero;
                int faceCount = 0;

                // 使用图像数据指针
                IntPtr matPtr = image.Data;

                int detectResult = baidu_face_detect(ref boxesPtr, ref faceCount, matPtr, (int)imageType);

                if (detectResult >= 0)
                {
                    if (faceCount > 0)
                    {
                        result.FaceBoxes = ConvertFaceBoxes(boxesPtr, faceCount);
                        result.FaceCount = faceCount;
                        result.Success = true;
                        result.Message = $"成功检测到 {faceCount} 个人脸";
                        LogInfo(result.Message);
                    }
                    else
                    {
                        result.Success = true;
                        result.FaceCount = 0;
                        result.Message = "未检测到人脸";
                        LogInfo(result.Message);
                    }
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = detectResult;
                    result.Message = $"人脸检测失败，错误码: {detectResult} - {GetErrorMessage(detectResult)}";
                    LogError(result.Message);
                }

                // 释放内存
                if (boxesPtr != IntPtr.Zero)
                {
                    baidu_face_free_detect_result(boxesPtr);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"人脸检测时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 最优人脸检测
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <returns>最优人脸检测结果</returns>
        public static BestFaceResult DetectBestFace(Mat image)
        {
            var result = new BestFaceResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            if (image == null || image.Empty())
            {
                result.Success = false;
                result.Message = "输入图像为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始最优人脸检测，图像尺寸: {image.Width}x{image.Height}");

                IntPtr bestsPtr = IntPtr.Zero;
                int faceCount = 0;

                // 获取图像数据指针
                IntPtr matPtr = image.Data;

                int detectResult = baidu_face_best(ref bestsPtr, ref faceCount, matPtr);

                if (detectResult >= 0)
                {
                    if (faceCount > 0)
                    {
                        // 转换检测结果
                        result.BestScores = ConvertBestScores(bestsPtr, faceCount);
                        result.FaceCount = faceCount;
                        result.Success = true;
                        result.Message = $"成功检测到 {faceCount} 个最优人脸";
                        LogInfo(result.Message);
                    }
                    else
                    {
                        result.Success = true;
                        result.FaceCount = 0;
                        result.Message = "未检测到最优人脸";
                        LogInfo(result.Message);
                    }
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = detectResult;
                    result.Message = $"最优人脸检测失败，错误码: {detectResult} - {GetErrorMessage(detectResult)}";
                    LogError(result.Message);
                }

                // 释放内存
                if (bestsPtr != IntPtr.Zero)
                {
                    baidu_face_free_best_result(bestsPtr);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"最优人脸检测时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        #endregion

        #region 特征提取功能

        /// <summary>
        /// 提取人脸特征值
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="featureType">特征值类型</param>
        /// <returns>特征提取结果</returns>
        public static FeatureExtractionResult ExtractFeatures(Mat image, FeatureType featureType = FeatureType.VISIBLE_LIVING)
        {
            var result = new FeatureExtractionResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            if (image == null || image.Empty())
            {
                result.Success = false;
                result.Message = "输入图像为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始提取人脸特征值，图像尺寸: {image.Width}x{image.Height}, 类型: {featureType}");

                IntPtr featuresPtr = IntPtr.Zero;
                IntPtr boxesPtr = IntPtr.Zero;
                int featureCount = 0;

                // 使用图像数据指针
                IntPtr matPtr = image.Data;

                int extractResult = baidu_face_feature(ref featuresPtr, ref boxesPtr, ref featureCount, matPtr, (int)featureType);

                if (extractResult >= 0)
                {
                    if (featureCount > 0)
                    {
                        // 使用修复后的转换方法
                        result.Features = ConvertFeaturesSafe(featuresPtr, featureCount);
                        result.FaceBoxes = ConvertFaceBoxes(boxesPtr, featureCount);
                        result.FeatureCount = featureCount;
                        result.Success = true;
                        result.Message = $"成功提取 {featureCount} 个特征值";
                        LogInfo(result.Message);
                    }
                    else
                    {
                        result.Success = true;
                        result.FeatureCount = 0;
                        result.Message = "未提取到特征值";
                        LogInfo(result.Message);
                    }
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = extractResult;
                    result.Message = $"特征值提取失败，错误码: {extractResult} - {GetErrorMessage(extractResult)}";
                    LogError(result.Message);
                }

                // 释放内存
                if (featuresPtr != IntPtr.Zero && boxesPtr != IntPtr.Zero)
                {
                    baidu_face_free_feature_result(featuresPtr, boxesPtr);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"特征值提取时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        private static float[][] ConvertFeaturesSafe(IntPtr featuresPtr, int count)
        {
            if (featuresPtr == IntPtr.Zero || count <= 0)
                return Array.Empty<float[]>();

            try
            {
                var features = new float[count][];
                int size = Marshal.SizeOf<WrapperFeature>();

                for (int i = 0; i < count; i++)
                {
                    IntPtr current = IntPtr.Add(featuresPtr, i * size);
                    WrapperFeature feature = Marshal.PtrToStructure<WrapperFeature>(current);

                    // 安全复制特征值数据
                    if (feature.data != null && feature.data.Length == FEATURE_DIMENSION)
                    {
                        features[i] = new float[FEATURE_DIMENSION];
                        Array.Copy(feature.data, features[i], FEATURE_DIMENSION);
                    }
                    else
                    {
                        features[i] = new float[FEATURE_DIMENSION]; // 返回空特征值
                        LogError($"第 {i} 个特征值数据无效");
                    }
                }

                return features;
            }
            catch (Exception ex)
            {
                LogError($"转换特征值数组时发生异常: {ex.Message}");
                return Array.Empty<float[]>();
            }
        }

        /// <summary>
        /// 特征值比对
        /// </summary>
        /// <param name="feature1">第一个特征值</param>
        /// <param name="feature2">第二个特征值</param>
        /// <param name="compareType">比对类型</param>
        /// <returns>比对结果</returns>
        public static FeatureCompareResult CompareFeatures(float[] feature1, float[] feature2, CompareType compareType = CompareType.DEFAULT)
        {
            var result = new FeatureCompareResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            if (feature1 == null || feature1.Length != FEATURE_DIMENSION ||
                feature2 == null || feature2.Length != FEATURE_DIMENSION)
            {
                result.Success = false;
                result.Message = "特征值无效，必须为128维";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo("开始特征值比对");

                // 使用修复后的方法创建特征值指针
                IntPtr featurePtr1 = CreateFeaturePtr(feature1);
                IntPtr featurePtr2 = CreateFeaturePtr(feature2);

                if (featurePtr1 == IntPtr.Zero || featurePtr2 == IntPtr.Zero)
                {
                    result.Success = false;
                    result.Message = "创建特征值指针失败";
                    LogError(result.Message);
                    return result;
                }

                float score = baidu_face_compare_feature(featurePtr1, featurePtr2, (int)compareType);

                if (score >= 0)
                {
                    result.Score = score;
                    result.Success = true;
                    result.Message = $"特征值比对完成，得分: {score:F4}";
                    LogInfo(result.Message);
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = (int)score;
                    result.Message = $"特征值比对失败，错误码: {score} - {GetErrorMessage((int)score)}";
                    LogError(result.Message);
                }

                // 使用修复后的方法释放内存
                ReleaseFeaturePtr(featurePtr1);
                ReleaseFeaturePtr(featurePtr2);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"特征值比对时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 人脸1:1比对（通过图片）
        /// </summary>
        /// <param name="image1">第一张图片</param>
        /// <param name="image2">第二张图片</param>
        /// <param name="compareType">比对类型</param>
        /// <returns>比对结果</returns>
        public static FaceCompareResult CompareFaces(Mat image1, Mat image2, CompareType compareType = CompareType.DEFAULT)
        {
            var result = new FaceCompareResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            if (image1 == null || image1.Empty() || image2 == null || image2.Empty())
            {
                result.Success = false;
                result.Message = "输入图像为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始人脸1:1比对，图像1尺寸: {image1.Width}x{image1.Height}, 图像2尺寸: {image2.Width}x{image2.Height}");

                // 获取图像数据指针
                IntPtr matPtr1 = image1.Data;
                IntPtr matPtr2 = image2.Data;

                float score = baidu_face_match(matPtr1, matPtr2, (int)compareType);

                if (score >= 0)
                {
                    result.Score = score;
                    result.Success = true;
                    result.Message = $"人脸比对完成，得分: {score:F4}";
                    LogInfo(result.Message);
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = (int)score;
                    result.Message = $"人脸比对失败，错误码: {score} - {GetErrorMessage((int)score)}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"人脸比对时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        #endregion

        #region 转换人脸框数组

        /// <summary>
        /// 转换人脸框数组
        /// </summary>
        private static WrapperFaceBox[] ConvertFaceBoxes(IntPtr boxesPtr, int count)
        {
            if (boxesPtr == IntPtr.Zero || count <= 0)
                return Array.Empty<WrapperFaceBox>();

            try
            {
                var boxes = new WrapperFaceBox[count];
                int size = Marshal.SizeOf<WrapperFaceBox>();

                for (int i = 0; i < count; i++)
                {
                    IntPtr current = IntPtr.Add(boxesPtr, i * size);
                    boxes[i] = Marshal.PtrToStructure<WrapperFaceBox>(current);
                }

                return boxes;
            }
            catch (Exception ex)
            {
                LogError($"转换人脸框数组时发生异常: {ex.Message}");
                return Array.Empty<WrapperFaceBox>();
            }
        }
        #endregion

        #region 转换最优人脸分数数组
        /// <summary>
        /// 转换最优人脸分数数组
        /// </summary>
        private static float[] ConvertBestScores(IntPtr bestsPtr, int count)
        {
            if (bestsPtr == IntPtr.Zero || count <= 0)
                return Array.Empty<float>();

            try
            {
                var scores = new float[count];
                int size = Marshal.SizeOf<WrapperBest>();

                for (int i = 0; i < count; i++)
                {
                    IntPtr current = IntPtr.Add(bestsPtr, i * size);
                    WrapperBest best = Marshal.PtrToStructure<WrapperBest>(current);
                    scores[i] = best.score;
                }

                return scores;
            }
            catch (Exception ex)
            {
                LogError($"转换最优人脸分数数组时发生异常: {ex.Message}");
                return Array.Empty<float>();
            }
        }
        #endregion

        #region 创建特征值指针
        /// <summary>
        /// 创建特征值指针
        /// </summary>
        private static IntPtr CreateFeaturePtr(float[] featureData)
        {
            if (featureData == null || featureData.Length != FEATURE_DIMENSION)
            {
                LogError("特征值数据无效，必须为128维");
                return IntPtr.Zero;
            }

            try
            {
                // 直接分配整个WrapperFeature结构体的内存
                int structSize = Marshal.SizeOf<WrapperFeature>();
                IntPtr featurePtr = Marshal.AllocHGlobal(structSize);

                // 创建WrapperFeature结构体实例
                WrapperFeature feature = new WrapperFeature
                {
                    size = FEATURE_DIMENSION,
                    data = new float[FEATURE_DIMENSION] // 初始化数组
                };

                // 复制特征值数据到数组
                Array.Copy(featureData, feature.data, FEATURE_DIMENSION);

                // 将结构体复制到非托管内存
                Marshal.StructureToPtr(feature, featurePtr, false);

                return featurePtr;
            }
            catch (Exception ex)
            {
                LogError($"创建特征值指针失败: {ex.Message}");
                return IntPtr.Zero;
            }
        }
        #endregion

        #region 从特征值指针提取特征值数据
        /// <summary>
        /// 从特征值指针提取特征值数据
        /// </summary>
        private static float[] ExtractFeatureFromPtr(IntPtr featurePtr)
        {
            if (featurePtr == IntPtr.Zero)
                return Array.Empty<float>();

            try
            {
                // 从指针读取WrapperFeature结构体
                WrapperFeature feature = Marshal.PtrToStructure<WrapperFeature>(featurePtr);

                if (feature.size == FEATURE_DIMENSION && feature.data != null)
                {
                    float[] result = new float[FEATURE_DIMENSION];
                    Array.Copy(feature.data, result, FEATURE_DIMENSION);
                    return result;
                }

                return Array.Empty<float>();
            }
            catch (Exception ex)
            {
                LogError($"提取特征值数据失败: {ex.Message}");
                return Array.Empty<float>();
            }
        }
        #endregion

        #region 释放特征值指针
        /// <summary>
        /// 释放特征值指针
        /// </summary>
        private static void ReleaseFeaturePtr(IntPtr featurePtr)
        {
            if (featurePtr == IntPtr.Zero)
                return;

            try
            {
                Marshal.FreeHGlobal(featurePtr);
            }
            catch (Exception ex)
            {
                LogError($"释放特征值指针失败: {ex.Message}");
            }
        }
        #endregion
    }

    #region 结果类定义

    /// <summary>
    /// 人脸检测结果
    /// </summary>
    public class FaceDetectionResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public int FaceCount { get; set; }
        public WrapperFaceBox[] FaceBoxes { get; set; } = Array.Empty<WrapperFaceBox>();
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 最优人脸检测结果
    /// </summary>
    public class BestFaceResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public int FaceCount { get; set; }
        public float[] BestScores { get; set; } = Array.Empty<float>();
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 特征提取结果
    /// </summary>
    public class FeatureExtractionResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public int FeatureCount { get; set; }
        public float[][] Features { get; set; } = Array.Empty<float[]>();
        public WrapperFaceBox[] FaceBoxes { get; set; } = Array.Empty<WrapperFaceBox>();
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 特征值比对结果
    /// </summary>
    public class FeatureCompareResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public float Score { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 人脸比对结果
    /// </summary>
    public class FaceCompareResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public float Score { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    #endregion
}