using OpenCvSharp;
using System;
using System.Text;

namespace FaceLocker.Services
{
    public static partial class BaiduFaceSDKInterop
    {
        #region 人脸库管理功能

        /// <summary>
        /// 用户注册（通过图片）
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="userId">用户ID</param>
        /// <param name="groupId">组ID</param>
        /// <param name="userInfo">用户信息（可选）</param>
        /// <returns>注册结果</returns>
        public static UserRegistrationResult RegisterUserByImage(Mat image, string userId, string groupId, string userInfo = null)
        {
            var result = new UserRegistrationResult();

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

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(groupId))
            {
                result.Success = false;
                result.Message = "用户ID或组ID为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始用户注册 - 用户ID: {userId}, 组ID: {groupId}, 图像尺寸: {image.Width}x{image.Height}");

                StringBuilder resultJson = new StringBuilder(MAX_BUFFER_SIZE);
                IntPtr matPtr = image.Data;

                int registerResult = baidu_face_user_add_by_mat(resultJson, resultJson.Capacity, matPtr,
                    userId, groupId, userInfo ?? string.Empty);

                if (registerResult == 0)
                {
                    result.Success = true;
                    result.JsonResult = resultJson.ToString();
                    result.Message = "用户注册成功";
                    LogInfo($"用户注册成功 - 用户ID: {userId}, 组ID: {groupId}");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = registerResult;
                    result.Message = $"用户注册失败，错误码: {registerResult} - {GetErrorMessage(registerResult)}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"用户注册时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 用户注册（通过特征值）
        /// </summary>
        /// <param name="feature">特征值</param>
        /// <param name="userId">用户ID</param>
        /// <param name="groupId">组ID</param>
        /// <param name="userInfo">用户信息（可选）</param>
        /// <returns>注册结果</returns>
        public static UserRegistrationResult RegisterUserByFeature(float[] feature, string userId, string groupId, string userInfo = null)
        {
            var result = new UserRegistrationResult();

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

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(groupId))
            {
                result.Success = false;
                result.Message = "用户ID或组ID为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始用户注册（特征值） - 用户ID: {userId}, 组ID: {groupId}");

                StringBuilder resultJson = new StringBuilder(MAX_BUFFER_SIZE);
                IntPtr featurePtr = CreateFeaturePtr(feature);

                int registerResult = baidu_face_user_add_by_feature(resultJson, resultJson.Capacity, featurePtr,
                    userId, groupId, userInfo ?? string.Empty);

                if (registerResult == 0)
                {
                    result.Success = true;
                    result.JsonResult = resultJson.ToString();
                    result.Message = "用户注册成功";
                    LogInfo($"用户注册成功（特征值） - 用户ID: {userId}, 组ID: {groupId}");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = registerResult;
                    result.Message = $"用户注册失败，错误码: {registerResult} - {GetErrorMessage(registerResult)}";
                    LogError(result.Message);
                }

                // 释放内存
                ReleaseFeaturePtr(featurePtr);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"用户注册时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 更新用户信息
        /// </summary>
        /// <param name="image">输入图像</param>
        /// <param name="userId">用户ID</param>
        /// <param name="groupId">组ID</param>
        /// <param name="userInfo">用户信息（可选）</param>
        /// <returns>更新结果</returns>
        public static UserUpdateResult UpdateUser(Mat image, string userId, string groupId, string userInfo = null)
        {
            var result = new UserUpdateResult();

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

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(groupId))
            {
                result.Success = false;
                result.Message = "用户ID或组ID为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始更新用户信息 - 用户ID: {userId}, 组ID: {groupId}");

                StringBuilder resultJson = new StringBuilder(MAX_BUFFER_SIZE);
                IntPtr matPtr = image.Data;

                int updateResult = baidu_face_user_update(resultJson, resultJson.Capacity, matPtr,
                    userId, groupId, userInfo ?? string.Empty);

                if (updateResult == 0)
                {
                    result.Success = true;
                    result.JsonResult = resultJson.ToString();
                    result.Message = "用户信息更新成功";
                    LogInfo($"用户信息更新成功 - 用户ID: {userId}, 组ID: {groupId}");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = updateResult;
                    result.Message = $"用户信息更新失败，错误码: {updateResult} - {GetErrorMessage(updateResult)}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"更新用户信息时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 删除用户
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="groupId">组ID（可选）</param>
        /// <returns>删除结果</returns>
        public static UserDeletionResult DeleteUser(string userId, string groupId = null)
        {
            var result = new UserDeletionResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            if (string.IsNullOrEmpty(userId))
            {
                result.Success = false;
                result.Message = "用户ID为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始删除用户 - 用户ID: {userId}, 组ID: {groupId ?? "空（删除所有组中的用户）"}");

                StringBuilder resultJson = new StringBuilder(MAX_BUFFER_SIZE);
                string actualGroupId = groupId ?? string.Empty;

                int deleteResult = baidu_face_user_delete(resultJson, resultJson.Capacity, userId, actualGroupId);

                if (deleteResult == 0)
                {
                    result.Success = true;
                    result.JsonResult = resultJson.ToString();
                    result.Message = "用户删除成功";
                    LogInfo($"用户删除成功 - 用户ID: {userId}");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = deleteResult;
                    result.Message = $"用户删除失败，错误码: {deleteResult} - {GetErrorMessage(deleteResult)}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"删除用户时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 添加组
        /// </summary>
        /// <param name="groupId">组ID</param>
        /// <returns>添加结果</returns>
        public static GroupOperationResult AddGroup(string groupId)
        {
            var result = new GroupOperationResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            if (string.IsNullOrEmpty(groupId))
            {
                result.Success = false;
                result.Message = "组ID为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始添加组 - 组ID: {groupId}");

                StringBuilder resultJson = new StringBuilder(MAX_BUFFER_SIZE);

                int addResult = baidu_face_group_add(resultJson, resultJson.Capacity, groupId);

                if (addResult == 0)
                {
                    result.Success = true;
                    result.JsonResult = resultJson.ToString();
                    result.Message = "组添加成功";
                    LogInfo($"组添加成功 - 组ID: {groupId}");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = addResult;
                    result.Message = $"组添加失败，错误码: {addResult} - {GetErrorMessage(addResult)}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"添加组时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 删除组
        /// </summary>
        /// <param name="groupId">组ID</param>
        /// <returns>删除结果</returns>
        public static GroupOperationResult DeleteGroup(string groupId)
        {
            var result = new GroupOperationResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            if (string.IsNullOrEmpty(groupId))
            {
                result.Success = false;
                result.Message = "组ID为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始删除组 - 组ID: {groupId}");

                StringBuilder resultJson = new StringBuilder(MAX_BUFFER_SIZE);

                int deleteResult = baidu_face_group_delete(resultJson, resultJson.Capacity, groupId);

                if (deleteResult == 0)
                {
                    result.Success = true;
                    result.JsonResult = resultJson.ToString();
                    result.Message = "组删除成功";
                    LogInfo($"组删除成功 - 组ID: {groupId}");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = deleteResult;
                    result.Message = $"组删除失败，错误码: {deleteResult} - {GetErrorMessage(deleteResult)}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"删除组时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 获取用户信息
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="groupId">组ID（可选）</param>
        /// <returns>用户信息结果</returns>
        public static UserInfoResult GetUserInfo(string userId, string groupId = null)
        {
            var result = new UserInfoResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            if (string.IsNullOrEmpty(userId))
            {
                result.Success = false;
                result.Message = "用户ID为空";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始获取用户信息 - 用户ID: {userId}, 组ID: {groupId ?? "空"}");

                StringBuilder resultJson = new StringBuilder(MAX_BUFFER_SIZE);
                string actualGroupId = groupId ?? string.Empty;

                int infoResult = baidu_face_get_user_info(resultJson, resultJson.Capacity, userId, actualGroupId);

                if (infoResult == 0)
                {
                    result.Success = true;
                    result.JsonResult = resultJson.ToString();
                    result.Message = "获取用户信息成功";
                    LogInfo($"获取用户信息成功 - 用户ID: {userId}");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = infoResult;
                    result.Message = $"获取用户信息失败，错误码: {infoResult} - {GetErrorMessage(infoResult)}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"获取用户信息时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 获取用户列表
        /// </summary>
        /// <param name="groupId">组ID（可选）</param>
        /// <param name="start">起始位置</param>
        /// <param name="length">获取数量</param>
        /// <returns>用户列表结果</returns>
        public static UserListResult GetUserList(string groupId = null, int start = 0, int length = 100)
        {
            var result = new UserListResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始获取用户列表 - 组ID: {groupId ?? "空"}, 起始位置: {start}, 数量: {length}");

                StringBuilder resultJson = new StringBuilder(MAX_BUFFER_SIZE);
                string actualGroupId = groupId ?? string.Empty;

                int listResult = baidu_face_get_user_list(resultJson, resultJson.Capacity, actualGroupId, start, length);

                if (listResult == 0)
                {
                    result.Success = true;
                    result.JsonResult = resultJson.ToString();
                    result.Message = "获取用户列表成功";
                    LogInfo($"获取用户列表成功 - 数量: {length}");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = listResult;
                    result.Message = $"获取用户列表失败，错误码: {listResult} - {GetErrorMessage(listResult)}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"获取用户列表时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 获取组列表
        /// </summary>
        /// <param name="start">起始位置</param>
        /// <param name="length">获取数量</param>
        /// <returns>组列表结果</returns>
        public static GroupListResult GetGroupList(int start = 0, int length = 100)
        {
            var result = new GroupListResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始获取组列表 - 起始位置: {start}, 数量: {length}");

                StringBuilder resultJson = new StringBuilder(MAX_BUFFER_SIZE);

                int listResult = baidu_face_get_group_list(resultJson, resultJson.Capacity, start, length);

                if (listResult == 0)
                {
                    result.Success = true;
                    result.JsonResult = resultJson.ToString();
                    result.Message = "获取组列表成功";
                    LogInfo($"获取组列表成功 - 数量: {length}");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = listResult;
                    result.Message = $"获取组列表失败，错误码: {listResult} - {GetErrorMessage(listResult)}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"获取组列表时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 获取人脸数量
        /// </summary>
        /// <param name="groupId">组ID（可选）</param>
        /// <returns>人脸数量结果</returns>
        public static FaceCountResult GetFaceCount(string groupId = null)
        {
            var result = new FaceCountResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo($"开始查询人脸数量 - 组ID: {groupId ?? "空（全库查询）"}");

                string actualGroupId = groupId ?? string.Empty;
                int count = baidu_face_db_face_count(actualGroupId);

                if (count >= 0)
                {
                    result.Success = true;
                    result.FaceCount = count;
                    result.Message = $"查询人脸数量成功";
                    LogInfo($"人脸数量查询完成: {count} {(string.IsNullOrEmpty(groupId) ? "(全库)" : $"(组: {groupId})")}");
                }
                else
                {
                    result.Success = false;
                    result.ErrorCode = count;
                    result.Message = $"查询人脸数量失败，错误码: {count} - {GetErrorMessage(count)}";
                    LogError(result.Message);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"查询人脸数量时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        /// <summary>
        /// 加载人脸库到内存
        /// </summary>
        /// <returns>加载结果</returns>
        public static LoadDatabaseResult LoadDatabase()
        {
            var result = new LoadDatabaseResult();

            if (!_isSdkInitialized)
            {
                result.Success = false;
                result.Message = "SDK未初始化";
                LogError(result.Message);
                return result;
            }

            try
            {
                LogInfo("开始加载人脸库到内存");

                int loadResult = baidu_face_load_db_face();

                if (loadResult == 1)
                {
                    result.Success = true;
                    result.IsLoaded = true;
                    result.Message = "人脸库加载成功";
                    LogInfo("人脸库加载成功");
                }
                else
                {
                    result.Success = false;
                    result.IsLoaded = false;
                    result.Message = "人脸库加载失败";
                    LogError("人脸库加载失败");
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"加载人脸库时发生异常: {ex.Message}";
                LogError(result.Message);
            }

            return result;
        }

        #endregion 
    }

    #region 人脸库管理结果类定义

    /// <summary>
    /// 用户注册结果
    /// </summary>
    public class UserRegistrationResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public string JsonResult { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 用户更新结果
    /// </summary>
    public class UserUpdateResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public string JsonResult { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 用户删除结果
    /// </summary>
    public class UserDeletionResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public string JsonResult { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 组操作结果
    /// </summary>
    public class GroupOperationResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public string JsonResult { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 用户信息结果
    /// </summary>
    public class UserInfoResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public string JsonResult { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 用户列表结果
    /// </summary>
    public class UserListResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public string JsonResult { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 组列表结果
    /// </summary>
    public class GroupListResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public string JsonResult { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 人脸数量结果
    /// </summary>
    public class FaceCountResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public int FaceCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// 数据库加载结果
    /// </summary>
    public class LoadDatabaseResult
    {
        public bool Success { get; set; }
        public bool IsLoaded { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    #endregion
}