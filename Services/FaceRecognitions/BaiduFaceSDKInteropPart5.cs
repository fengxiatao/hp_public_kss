using OpenCvSharp;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FaceLocker.Services
{
    public static partial class BaiduFaceSDKInterop
    {
        #region 人脸识别功能

        /// <summary>
        /// 人脸识别（通过图片）
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="groupIdList">组ID列表（多个组用逗号分隔）</param>
        /// <param name="userId">用户ID（可选）</param>
        /// <param name="featureType">特征值类型</param>
        /// <returns>识别结果</returns>
        public static FaceRecognitionResult IdentifyByImage(Mat image, string groupIdList, string userId = null, FeatureType featureType = FeatureType.VISIBLE_LIVING)
        {
            var result = new FaceRecognitionResult();

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

            if (string.IsNullOrEmpty(groupIdList))
            {
                result.Success = false;
                result.Message = "组ID列表为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始人脸识别（图片） - 组列表: {groupIdList}, 用户ID: {userId ?? "空"}, 图像尺寸: {image.Width}x{image.Height}");

                StringBuilder resultJson = new StringBuilder(MAX_BUFFER_SIZE);
                IntPtr matPtr = image.Data;
                string actualUserId = userId ?? string.Empty;

                int identifyResult = baidu_face_identify_by_mat(resultJson, resultJson.Capacity, matPtr,
                    groupIdList, actualUserId, (int)featureType);

                if (identifyResult == 0)
                {
                    result.Success = true;
                    result.JsonResult = resultJson.ToString();
                    result.Message = "人脸识别成功";
                    LogInfo($"人脸识别成功（图片） - 组列表: {groupIdList}");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = identifyResult;
                    result.Message = $"人脸识别失败，错误码: {identifyResult} - {GetErrorMessage(identifyResult)}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"人脸识别时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 人脸识别（通过特征值）
        /// </summary>
        /// <param name="feature">特征值</param>
        /// <param name="groupIdList">组ID列表（多个组用逗号分隔）</param>
        /// <param name="userId">用户ID（可选）</param>
        /// <param name="featureType">特征值类型</param>
        /// <returns>识别结果</returns>
        public static FaceRecognitionResult IdentifyByFeature(float[] feature, string groupIdList, string userId = null, FeatureType featureType = FeatureType.VISIBLE_LIVING)
        {
            var result = new FaceRecognitionResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            if (feature == null || feature.Length != FEATURE_DIMENSION)
            {
                result.Success = false;
                result.Message = "特征值无效，必须为128维";
                LogError(result.Message);
                return result;
            }

            if (string.IsNullOrEmpty(groupIdList))
            {
                result.Success = false;
                result.Message = "组ID列表为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始人脸识别（特征值） - 组列表: {groupIdList}, 用户ID: {userId ?? "空"}");

                StringBuilder resultJson = new StringBuilder(MAX_BUFFER_SIZE);
                IntPtr featurePtr = CreateFeaturePtr(feature);
                string actualUserId = userId ?? string.Empty;

                int identifyResult = baidu_face_identify_by_feature(resultJson, resultJson.Capacity, featurePtr,
                    groupIdList, actualUserId, (int)featureType);

                if (identifyResult == 0)
                {
                    result.Success = true;
                    result.JsonResult = resultJson.ToString();
                    result.Message = "人脸识别成功";
                    LogInfo($"人脸识别成功（特征值） - 组列表: {groupIdList}");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = identifyResult;
                    result.Message = $"人脸识别失败，错误码: {identifyResult} - {GetErrorMessage(identifyResult)}";
                    LogError(result.Message);
                }

                // 释放内存
                ReleaseFeaturePtr(featurePtr);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"人脸识别时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 人脸识别（通过图片，全库比较）
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="featureType">特征值类型</param>
        /// <returns>识别结果</returns>
        public static FaceRecognitionResult IdentifyWithAllByImage(Mat image, FeatureType featureType = FeatureType.VISIBLE_LIVING)
        {
            var result = new FaceRecognitionResult();

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
                LogInfo($"开始全库人脸识别（图片） - 图像尺寸: {image.Width}x{image.Height}");

                StringBuilder resultJson = new StringBuilder(MAX_BUFFER_SIZE);
                IntPtr matPtr = image.Data;

                int identifyResult = baidu_face_identify_with_all_by_mat(resultJson, resultJson.Capacity,
                    matPtr, (int)featureType);

                if (identifyResult == 0)
                {
                    result.Success = true;
                    result.JsonResult = resultJson.ToString();
                    result.Message = "全库人脸识别成功";
                    LogInfo("全库人脸识别成功（图片）");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = identifyResult;
                    result.Message = $"全库人脸识别失败，错误码: {identifyResult} - {GetErrorMessage(identifyResult)}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"全库人脸识别时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 人脸识别（通过特征值，全库比较）
        /// </summary>
        /// <param name="feature">特征值</param>
        /// <param name="featureType">特征值类型</param>
        /// <returns>识别结果</returns>
        public static FaceRecognitionResult IdentifyWithAllByFeature(float[] feature, FeatureType featureType = FeatureType.VISIBLE_LIVING)
        {
            var result = new FaceRecognitionResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            if (feature == null || feature.Length != FEATURE_DIMENSION)
            {
                result.Success = false;
                result.Message = "特征值无效，必须为128维";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo("开始全库人脸识别（特征值）");

                StringBuilder resultJson = new StringBuilder(MAX_BUFFER_SIZE);
                IntPtr featurePtr = CreateFeaturePtr(feature);

                int identifyResult = baidu_face_identify_with_all_by_feature(resultJson, resultJson.Capacity,
                    featurePtr, (int)featureType);

                if (identifyResult == 0)
                {
                    result.Success = true;
                    result.JsonResult = resultJson.ToString();
                    result.Message = "全库人脸识别成功";
                    LogInfo("全库人脸识别成功（特征值）");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = identifyResult;
                    result.Message = $"全库人脸识别失败，错误码: {identifyResult} - {GetErrorMessage(identifyResult)}";
                    LogError(result.Message);
                }

                // 释放内存
                ReleaseFeaturePtr(featurePtr);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"全库人脸识别时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        #endregion

        #region 高级功能封装

        /// <summary>
        /// 批量人脸检测和特征提取
        /// </summary>
        /// <param name="images">图像数组</param>
        /// <param name="featureType">特征值类型</param>
        /// <returns>批量处理结果</returns>
        public static BatchProcessResult BatchDetectAndExtract(Mat[] images, FeatureType featureType = FeatureType.VISIBLE_LIVING)
        {
            var result = new BatchProcessResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            if (images == null || images.Length == 0)
            {
                result.Success = false;
                result.Message = "图像数组为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始批量处理，图像数量: {images.Length}");

                var detectionResults = new FaceDetectionResult[images.Length];
                var featureResults = new FeatureExtractionResult[images.Length];
                int successCount = 0;

                for (int i = 0; i < images.Length; i++)
                {
                    LogInfo($"处理第 {i + 1} 张图像，尺寸: {images[i].Width}x{images[i].Height}");

                    // 人脸检测
                    detectionResults[i] = DetectFaces(images[i], ImageType.RGB);

                    if (detectionResults[i].Success && detectionResults[i].FaceCount > 0)
                    {
                        // 特征提取
                        featureResults[i] = ExtractFeatures(images[i], featureType);

                        if (featureResults[i].Success)
                        {
                            successCount++;
                        }
                    }
                }

                result.Success = true;
                result.ProcessedCount = images.Length;
                result.SuccessCount = successCount;
                result.DetectionResults = detectionResults;
                result.FeatureResults = featureResults;
                result.Message = $"批量处理完成，成功: {successCount}/{images.Length}";
                LogInfo(result.Message);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"批量处理时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 验证用户身份（1:1比对）
        /// </summary>
        /// <param name="inputImage">输入图像</param>
        /// <param name="registeredUserId">已注册用户ID</param>
        /// <param name="groupId">组ID</param>
        /// <param name="threshold">比对阈值</param>
        /// <returns>验证结果</returns>
        public static IdentityVerificationResult VerifyIdentity(Mat inputImage, string registeredUserId, string groupId, float threshold = 80.0f)
        {
            var result = new IdentityVerificationResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            if (inputImage == null || inputImage.Empty())
            {
                result.Success = false;
                result.Message = "输入图像为空";
                LogError(result.Message);
                return result;
            }

            if (string.IsNullOrEmpty(registeredUserId) || string.IsNullOrEmpty(groupId))
            {
                result.Success = false;
                result.Message = "用户ID或组ID为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始身份验证 - 用户ID: {registeredUserId}, 组ID: {groupId}, 阈值: {threshold}");

                // 第一步：提取输入图像的特征值
                var featureResult = ExtractFeatures(inputImage, FeatureType.VISIBLE_LIVING);

                if (!featureResult.Success || featureResult.FeatureCount == 0)
                {
                    result.Success = false;
                    result.Message = "输入图像特征提取失败";
                    LogError(result.Message);
                    return result;
                }

                // 第二步：获取用户信息（包含特征值）
                var userInfoResult = GetUserInfo(registeredUserId, groupId);

                if (!userInfoResult.Success)
                {
                    result.Success = false;
                    result.Message = "获取用户信息失败";
                    LogError(result.Message);
                    return result;
                }

                // 第三步：从用户信息中解析特征值（这里需要根据实际JSON结构解析）
                // 注意：实际实现需要根据百度SDK返回的JSON格式来解析特征值
                // 这里简化实现，实际使用时需要完善
                float[] registeredFeature = ParseFeatureFromUserInfo(userInfoResult.JsonResult);

                if (registeredFeature == null || registeredFeature.Length != FEATURE_DIMENSION)
                {
                    result.Success = false;
                    result.Message = "解析注册特征值失败";
                    LogError(result.Message);
                    return result;
                }

                // 第四步：特征值比对
                var compareResult = CompareFeatures(featureResult.Features[0], registeredFeature);

                if (compareResult.Success)
                {
                    result.Success = true;
                    result.Score = compareResult.Score;
                    result.IsVerified = compareResult.Score >= threshold;
                    result.Message = $"身份验证完成，得分: {compareResult.Score:F2}, 阈值: {threshold}, 结果: {(result.IsVerified ? "通过" : "不通过")}";
                    LogInfo(result.Message);
                }
                else
                {
                    result.Success = false;
                    result.Message = "特征值比对失败";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"身份验证时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 搜索相似人脸（1:N搜索）
        /// </summary>
        /// <param name="queryImage">查询图像</param>
        /// <param name="groupIdList">搜索的组列表</param>
        /// <param name="topK">返回最相似的前K个结果</param>
        /// <param name="threshold">相似度阈值</param>
        /// <returns>搜索结果</returns>
        public static FaceSearchResult SearchSimilarFaces(Mat queryImage, string groupIdList, int topK = 10, float threshold = 70.0f)
        {
            var result = new FaceSearchResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            if (queryImage == null || queryImage.Empty())
            {
                result.Success = false;
                result.Message = "查询图像为空";
                LogError(result.Message);
                return result;
            }

            if (string.IsNullOrEmpty(groupIdList))
            {
                result.Success = false;
                result.Message = "组ID列表为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始相似人脸搜索 - 组列表: {groupIdList}, TopK: {topK}, 阈值: {threshold}");

                // 使用识别功能进行搜索
                var identifyResult = IdentifyByImage(queryImage, groupIdList, null, FeatureType.VISIBLE_LIVING);

                if (identifyResult.Success)
                {
                    result.Success = true;
                    result.JsonResult = identifyResult.JsonResult;
                    result.Message = "相似人脸搜索完成";

                    // 解析搜索结果
                    var searchResults = ParseSearchResults(identifyResult.JsonResult, threshold, topK);
                    result.SearchResults = searchResults;
                    result.MatchCount = searchResults.Length;

                    LogInfo($"搜索完成，找到 {result.MatchCount} 个相似人脸");
                }
                else
                {
                    result.Success = false;
                    result.Message = identifyResult.Message;
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"相似人脸搜索时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 从用户信息JSON中解析特征值
        /// </summary>
        private static float[] ParseFeatureFromUserInfo(string userInfoJson)
        {
            // 注意：这里需要根据百度SDK返回的实际JSON格式来解析特征值
            // 这里提供简化实现，实际使用时需要根据具体格式完善

            try
            {
                // 示例JSON格式（需要根据实际调整）：
                // {
                //   "result": {
                //     "user_list": [{
                //       "user_id": "123",
                //       "user_info": "info",
                //       "feature": [0.1, 0.2, ...] // 128维特征值
                //     }]
                //   }
                // }

                // 简化实现：返回一个空的128维数组
                // 实际使用时需要解析JSON并提取特征值
                return new float[FEATURE_DIMENSION];
            }
            catch (Exception ex)
            {
                LogError($"解析用户特征值时发生异常: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 解析搜索结果
        /// </summary>
        private static FaceSearchResult.SearchItem[] ParseSearchResults(string searchResultJson, float threshold, int topK)
        {
            // 注意：这里需要根据百度SDK返回的实际JSON格式来解析搜索结果
            // 这里提供简化实现，实际使用时需要根据具体格式完善

            try
            {
                // 示例JSON格式（需要根据实际调整）：
                // {
                //   "result": {
                //     "face_token": "xxx",
                //     "user_list": [{
                //       "group_id": "group1",
                //       "user_id": "user1",
                //       "user_info": "info",
                //       "score": 85.5
                //     }, ...]
                //   }
                // }

                // 简化实现：返回空数组
                // 实际使用时需要解析JSON并提取搜索结果
                return Array.Empty<FaceSearchResult.SearchItem>();
            }
            catch (Exception ex)
            {
                LogError($"解析搜索结果时发生异常: {ex.Message}");
                return Array.Empty<FaceSearchResult.SearchItem>();
            }
        }

        #endregion
    }

    #region 高级功能结果类定义

    /// <summary>
    /// 人脸识别结果
    /// </summary>
    public class FaceRecognitionResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public string JsonResult { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 批量处理结果
    /// </summary>
    public class BatchProcessResult
    {
        public bool Success { get; set; }
        public int ProcessedCount { get; set; }
        public int SuccessCount { get; set; }
        public FaceDetectionResult[] DetectionResults { get; set; } = Array.Empty<FaceDetectionResult>();
        public FeatureExtractionResult[] FeatureResults { get; set; } = Array.Empty<FeatureExtractionResult>();
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 身份验证结果
    /// </summary>
    public class IdentityVerificationResult
    {
        public bool Success { get; set; }
        public bool IsVerified { get; set; }
        public float Score { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 人脸搜索结果
    /// </summary>
    public class FaceSearchResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public string JsonResult { get; set; } = string.Empty;
        public int MatchCount { get; set; }
        public SearchItem[] SearchResults { get; set; } = Array.Empty<SearchItem>();
        public string Message { get; set; } = string.Empty;

        public class SearchItem
        {
            public string UserId { get; set; } = string.Empty;
            public string GroupId { get; set; } = string.Empty;
            public string UserInfo { get; set; } = string.Empty;
            public float Score { get; set; }
        }
    }

    #endregion
}