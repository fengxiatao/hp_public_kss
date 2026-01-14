using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FaceLocker.Services
{
    /// <summary>
    /// 百度人脸识别SDK的P/Invoke封装类
    /// 通过C++包装库libbaidu_face_wrapper.so桥接C++ BaiduFaceApi和.NET
    /// </summary>
    public static partial class BaiduFaceSDKInterop
    {
        #region 常量定义
        // P/Invoke库名称
        private const string LibraryName = "/opt/face_offline_sdk/lib/armv8/libbaidu_face_wrapper.so";
        public const string SDK_BASE_PATH = "/opt/face_offline_sdk";

        // 默认缓冲区大小
        public const int MAX_BUFFER_SIZE = 4096;
        public const int FEATURE_DIMENSION = 128;
        public const int MAX_FACE_COUNT = 3;
        #endregion

        #region 枚举定义 - 与C++包装器对齐

        /// <summary>
        /// 图像类型枚举
        /// </summary>
        public enum ImageType
        {
            /// <summary>
            /// RGB可见光图像
            /// </summary>
            RGB = 0,
            /// <summary>
            /// 近红外图像
            /// </summary>
            NIR = 1
        }

        /// <summary>
        /// 特征值类型枚举
        /// </summary>
        public enum FeatureType
        {
            /// <summary>
            /// 可见光生活照特征值
            /// </summary>
            VISIBLE_LIVING = 0,
            /// <summary>
            /// 可见光证件照特征值
            /// </summary>
            VISIBLE_ID = 1,
            /// <summary>
            /// 近红外特征值
            /// </summary>
            NIR = 2
        }

        /// <summary>
        /// 比对类型枚举
        /// </summary>
        public enum CompareType
        {
            /// <summary>
            /// 默认比对类型
            /// </summary>
            DEFAULT = 0,
            /// <summary>
            /// 活体比对
            /// </summary>
            LIVING = 1
        }

        #endregion

        #region 数据结构定义 - 与C++包装器完全对齐

        /// <summary>
        /// 人脸框信息结构体 - 对应C++的WrapperFaceBox
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct WrapperFaceBox
        {
            public int index;          // 人脸索引
            public float center_x;      // 中心点X坐标
            public float center_y;      // 中心点Y坐标
            public float width;         // 人脸宽度
            public float height;        // 人脸高度
            public float score;         // 人脸置信度
        }

        /// <summary>
        /// 特征值结构体 - 对应C++的WrapperFeature
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct WrapperFeature
        {
            public int size;           // 特征值维度
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = FEATURE_DIMENSION)]
            public float[] data;       // 特征值数据
        }

        /// <summary>
        /// 最优人脸结构体 - 对应C++的WrapperBest
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct WrapperBest
        {
            public float score;        // 最优人脸分数
        }

        #endregion

        #region P/Invoke函数声明 - 与libbaidu_face_wrapper.so完全对齐

        #region SDK基础功能

        /// <summary>
        /// 获取设备ID
        /// </summary>
        /// <param name="device_id">输出参数，设备ID字符串</param>
        /// <param name="max_length">设备ID字符串的最大长度</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_get_device_id(StringBuilder device_id, int max_length);

        /// <summary>
        /// 获取SDK版本号
        /// </summary>
        /// <param name="version">输出参数，版本号字符串</param>
        /// <param name="max_length">版本号字符串的最大长度</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_sdk_version(StringBuilder version, int max_length);

        /// <summary>
        /// 初始化SDK
        /// </summary>
        /// <param name="model_path">模型文件路径，如果为nullptr则使用默认路径</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_sdk_init(string model_path);

        /// <summary>
        /// 检查SDK是否已授权
        /// </summary>
        /// <returns>1表示已授权，0表示未授权</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int baidu_face_is_auth();

        #endregion

        #region 人脸检测功能

        /// <summary>
        /// 人脸检测
        /// </summary>
        /// <param name="out_boxes">输出参数，检测到的人脸框数组</param>
        /// <param name="out_count">输出参数，检测到的人脸数量</param>
        /// <param name="mat">输入图像指针</param>
        /// <param name="type">检测类型：0表示RGB检测，1表示NIR检测</param>
        /// <returns>检测到的人脸数量，负数表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int baidu_face_detect(ref IntPtr out_boxes, ref int out_count, IntPtr mat, int type);

        /// <summary>
        /// 释放人脸检测结果内存
        /// </summary>
        /// <param name="boxes">要释放的人脸框数组</param>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void baidu_face_free_detect_result(IntPtr boxes);

        #endregion

        #region 最优人脸检测

        /// <summary>
        /// 最优人脸检测
        /// </summary>
        /// <param name="out_bests">输出参数，最优人脸结果数组</param>
        /// <param name="out_count">输出参数，结果数量</param>
        /// <param name="mat">输入图像指针</param>
        /// <returns>检测到的人脸数量，负数表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int baidu_face_best(ref IntPtr out_bests, ref int out_count, IntPtr mat);

        /// <summary>
        /// 释放最优人脸检测结果内存
        /// </summary>
        /// <param name="bests">要释放的最优人脸结果数组</param>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void baidu_face_free_best_result(IntPtr bests);

        #endregion

        #region 特征值相关功能

        /// <summary>
        /// 提取人脸特征值
        /// </summary>
        /// <param name="out_features">输出参数，特征值数组</param>
        /// <param name="out_boxes">输出参数，对应的人脸框数组</param>
        /// <param name="out_count">输出参数，特征值数量</param>
        /// <param name="mat">输入图像指针</param>
        /// <param name="type">特征值类型：0表示RGB特征值，1表示NIR特征值</param>
        /// <returns>提取到的特征值数量，负数表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int baidu_face_feature(ref IntPtr out_features, ref IntPtr out_boxes,
            ref int out_count, IntPtr mat, int type);

        /// <summary>
        /// 释放特征值提取结果内存
        /// </summary>
        /// <param name="features">要释放的特征值数组</param>
        /// <param name="boxes">要释放的人脸框数组</param>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void baidu_face_free_feature_result(IntPtr features, IntPtr boxes);

        /// <summary>
        /// 特征值比对
        /// </summary>
        /// <param name="f1">第一个特征值</param>
        /// <param name="f2">第二个特征值</param>
        /// <param name="type">比对类型</param>
        /// <returns>比对分数，负数表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern float baidu_face_compare_feature(IntPtr f1, IntPtr f2, int type);

        #endregion

        #region 人脸库管理功能

        /// <summary>
        /// 人脸注册（通过图片）
        /// </summary>
        /// <param name="res">输出参数，注册结果JSON字符串</param>
        /// <param name="res_max_length">结果字符串的最大长度</param>
        /// <param name="mat">输入图像指针</param>
        /// <param name="user_id">用户ID</param>
        /// <param name="group_id">组ID</param>
        /// <param name="user_info">用户信息（可选）</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_user_add_by_mat(StringBuilder res, int res_max_length,
            IntPtr mat, string user_id, string group_id, string user_info);

        /// <summary>
        /// 人脸注册（通过特征值）
        /// </summary>
        /// <param name="res">输出参数，注册结果JSON字符串</param>
        /// <param name="res_max_length">结果字符串的最大长度</param>
        /// <param name="feature">特征值指针</param>
        /// <param name="user_id">用户ID</param>
        /// <param name="group_id">组ID</param>
        /// <param name="user_info">用户信息（可选）</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_user_add_by_feature(StringBuilder res, int res_max_length,
            IntPtr feature, string user_id, string group_id, string user_info);

        /// <summary>
        /// 人脸更新
        /// </summary>
        /// <param name="res">输出参数，更新结果JSON字符串</param>
        /// <param name="res_max_length">结果字符串的最大长度</param>
        /// <param name="mat">输入图像指针</param>
        /// <param name="user_id">用户ID</param>
        /// <param name="group_id">组ID</param>
        /// <param name="user_info">用户信息（可选）</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_user_update(StringBuilder res, int res_max_length,
            IntPtr mat, string user_id, string group_id, string user_info);

        /// <summary>
        /// 删除用户
        /// </summary>
        /// <param name="res">输出参数，删除结果JSON字符串</param>
        /// <param name="res_max_length">结果字符串的最大长度</param>
        /// <param name="user_id">用户ID</param>
        /// <param name="group_id">组ID</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_user_delete(StringBuilder res, int res_max_length,
            string user_id, string group_id);

        /// <summary>
        /// 添加组
        /// </summary>
        /// <param name="res">输出参数，添加结果JSON字符串</param>
        /// <param name="res_max_length">结果字符串的最大长度</param>
        /// <param name="group_id">组ID</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_group_add(StringBuilder res, int res_max_length, string group_id);

        /// <summary>
        /// 删除组
        /// </summary>
        /// <param name="res">输出参数，删除结果JSON字符串</param>
        /// <param name="res_max_length">结果字符串的最大长度</param>
        /// <param name="group_id">组ID</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_group_delete(StringBuilder res, int res_max_length, string group_id);

        /// <summary>
        /// 获取用户图片
        /// </summary>
        /// <param name="out_mat">输出参数，用户图片指针</param>
        /// <param name="user_id">用户ID</param>
        /// <param name="group_id">组ID</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int baidu_face_get_user_image(IntPtr out_mat, string user_id, string group_id);

        /// <summary>
        /// 获取用户信息
        /// </summary>
        /// <param name="res">输出参数，用户信息JSON字符串</param>
        /// <param name="res_max_length">结果字符串的最大长度</param>
        /// <param name="user_id">用户ID</param>
        /// <param name="group_id">组ID</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_get_user_info(StringBuilder res, int res_max_length,
            string user_id, string group_id);

        /// <summary>
        /// 获取用户列表
        /// </summary>
        /// <param name="res">输出参数，用户列表JSON字符串</param>
        /// <param name="res_max_length">结果字符串的最大长度</param>
        /// <param name="group_id">组ID</param>
        /// <param name="start">起始位置</param>
        /// <param name="length">获取数量</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_get_user_list(StringBuilder res, int res_max_length, string group_id,
            int start, int length);

        /// <summary>
        /// 获取组列表
        /// </summary>
        /// <param name="res">输出参数，组列表JSON字符串</param>
        /// <param name="res_max_length">结果字符串的最大长度</param>
        /// <param name="start">起始位置</param>
        /// <param name="length">获取数量</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_get_group_list(StringBuilder res, int res_max_length,
            int start, int length);

        /// <summary>
        /// 获取人脸数量
        /// </summary>
        /// <param name="group_id">组ID，如果为nullptr则查询整个库</param>
        /// <returns>人脸数量，负数表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_db_face_count(string group_id);

        #endregion

        #region 人脸比对识别功能

        /// <summary>
        /// 人脸1:1比对（通过图片）
        /// </summary>
        /// <param name="img1">第一张图片指针</param>
        /// <param name="img2">第二张图片指针</param>
        /// <param name="type">比对类型</param>
        /// <returns>比对分数，负数表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern float baidu_face_match(IntPtr img1, IntPtr img2, int type);

        /// <summary>
        /// 人脸识别（通过图片）
        /// </summary>
        /// <param name="res">输出参数，识别结果JSON字符串</param>
        /// <param name="res_max_length">结果字符串的最大长度</param>
        /// <param name="mat">输入图像指针</param>
        /// <param name="group_id_list">组ID列表（多个组用逗号分隔）</param>
        /// <param name="user_id">用户ID（可选）</param>
        /// <param name="type">识别类型</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_identify_by_mat(StringBuilder res, int res_max_length, IntPtr mat,
            string group_id_list, string user_id, int type);

        /// <summary>
        /// 人脸识别（通过特征值）
        /// </summary>
        /// <param name="res">输出参数，识别结果JSON字符串</param>
        /// <param name="res_max_length">结果字符串的最大长度</param>
        /// <param name="feature">特征值指针</param>
        /// <param name="group_id_list">组ID列表（多个组用逗号分隔）</param>
        /// <param name="user_id">用户ID（可选）</param>
        /// <param name="type">识别类型</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_identify_by_feature(StringBuilder res, int res_max_length, IntPtr feature,
            string group_id_list, string user_id, int type);

        /// <summary>
        /// 人脸识别（通过图片，和整个库比较）
        /// </summary>
        /// <param name="res">输出参数，识别结果JSON字符串</param>
        /// <param name="res_max_length">结果字符串的最大长度</param>
        /// <param name="mat">输入图像指针</param>
        /// <param name="type">识别类型</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_identify_with_all_by_mat(StringBuilder res, int res_max_length,
            IntPtr mat, int type);

        /// <summary>
        /// 人脸识别（通过特征值，和整个库比较）
        /// </summary>
        /// <param name="res">输出参数，识别结果JSON字符串</param>
        /// <param name="res_max_length">结果字符串的最大长度</param>
        /// <param name="feature">特征值指针</param>
        /// <param name="type">识别类型</param>
        /// <returns>0表示成功，其他表示错误码</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int baidu_face_identify_with_all_by_feature(StringBuilder res, int res_max_length,
            IntPtr feature, int type);

        /// <summary>
        /// 加载人脸库到内存
        /// </summary>
        /// <returns>1表示成功，0表示失败</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int baidu_face_load_db_face();

        #endregion

        #endregion

        #region 工具方法

        /// <summary>
        /// 记录信息日志
        /// </summary>
        public static void LogInfo(string message)
        {
            Console.WriteLine($"[INFO] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        /// <summary>
        /// 记录错误日志
        /// </summary>
        public static void LogError(string message)
        {
            Console.WriteLine($"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        /// <summary>
        /// 记录调试日志
        /// </summary>
        public static void LogDebug(string message)
        {
            Console.WriteLine($"[DEBUG] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        /// <summary>
        /// 检查SDK是否已初始化
        /// </summary>
        public static bool IsSDKInitialized()
        {
            // 这里需要实现检查SDK是否已初始化的逻辑
            // 由于C++包装库使用全局变量，我们需要跟踪初始化状态
            return true; // 简化实现，实际需要维护状态
        }

        #endregion
    }
}