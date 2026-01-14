/*
 * GStreamer 原生视频播放器 - X11 窗口嵌入版本
 * 
 * 核心优势：
 * 1. 视频渲染完全绕过 Avalonia UI 线程
 * 2. GStreamer 直接渲染到 X11 窗口（零拷贝）
 * 3. 人脸识别通过 appsink 分流，不影响显示
 * 4. CPU 占用极低，适合 RK3568 (2G RAM)
 * 
 * 架构：
 * Camera (V4L2)
 *    ↓
 * GStreamer (mppjpegdec 硬解)
 *    ↓
 *   tee
 *    ├── glimagesink (渲染到 X11 窗口)
 *    └── appsink (人脸识别)
 */

#ifndef GST_VIDEO_PLAYER_H
#define GST_VIDEO_PLAYER_H

#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/* 播放器句柄 */
typedef void* gst_player_handle_t;

/* 帧回调函数类型 - 用于人脸识别
 * @param user_data 用户数据
 * @param data 帧数据（BGRA格式）
 * @param width 宽度
 * @param height 高度
 * @param stride 行字节数
 */
typedef void (*gst_frame_callback_t)(void* user_data, uint8_t* data, int width, int height, int stride);

/* 错误码 */
typedef enum {
    GST_PLAYER_OK = 0,
    GST_PLAYER_ERROR_INIT_FAILED = -1,
    GST_PLAYER_ERROR_INVALID_PARAM = -2,
    GST_PLAYER_ERROR_DEVICE_NOT_FOUND = -3,
    GST_PLAYER_ERROR_PIPELINE_FAILED = -4,
    GST_PLAYER_ERROR_NO_DISPLAY = -5,
    GST_PLAYER_ERROR_WINDOW_INVALID = -6,
} gst_player_error_t;

/* 视频格式 */
typedef enum {
    GST_PLAYER_FORMAT_MJPEG = 0,  /* MJPEG 格式（需要 MPP 解码） */
    GST_PLAYER_FORMAT_YUY2 = 1,   /* YUY2/YUYV 格式（直接 RGA 转换） */
    GST_PLAYER_FORMAT_NV12 = 2,   /* NV12 格式 */
} gst_player_format_t;

/* 播放器配置 */
typedef struct {
    const char* device;           /* 设备路径，如 /dev/video12 */
    int width;                    /* 分辨率宽度 */
    int height;                   /* 分辨率高度 */
    int fps;                      /* 帧率 */
    gst_player_format_t format;   /* 视频格式 */
    bool use_hardware_decode;     /* 是否使用硬件解码（MPP） */
    bool use_rga;                 /* 是否使用 RGA 硬件加速 */
    int face_detect_fps;          /* 人脸检测帧率（0 表示不需要 appsink） */
    int face_detect_width;        /* 人脸检测缩放宽度（0 表示使用原始尺寸） */
    int face_detect_height;       /* 人脸检测缩放高度 */
} gst_player_config_t;

/**
 * 初始化 GStreamer（全局，只需调用一次）
 * @return 错误码
 */
gst_player_error_t gst_player_global_init(void);

/**
 * 创建播放器实例
 * @param config 配置
 * @return 播放器句柄，失败返回 NULL
 */
gst_player_handle_t gst_player_create(const gst_player_config_t* config);

/**
 * 销毁播放器实例
 * @param handle 播放器句柄
 */
void gst_player_destroy(gst_player_handle_t handle);

/**
 * 设置 X11 窗口句柄（必须在 start 之前调用）
 * @param handle 播放器句柄
 * @param x11_window_id X11 窗口 ID
 * @return 错误码
 */
gst_player_error_t gst_player_set_window(gst_player_handle_t handle, unsigned long x11_window_id);

/**
 * 设置帧回调（用于人脸识别，可选）
 * @param handle 播放器句柄
 * @param callback 回调函数
 * @param user_data 用户数据
 * @return 错误码
 */
gst_player_error_t gst_player_set_frame_callback(gst_player_handle_t handle, 
                                                   gst_frame_callback_t callback,
                                                   void* user_data);

/**
 * 启动播放
 * @param handle 播放器句柄
 * @return 错误码
 */
gst_player_error_t gst_player_start(gst_player_handle_t handle);

/**
 * 停止播放
 * @param handle 播放器句柄
 * @return 错误码
 */
gst_player_error_t gst_player_stop(gst_player_handle_t handle);

/**
 * 检查是否正在播放
 * @param handle 播放器句柄
 * @return true 正在播放，false 未播放
 */
bool gst_player_is_playing(gst_player_handle_t handle);

/**
 * 获取错误描述
 * @param error 错误码
 * @return 错误描述字符串
 */
const char* gst_player_get_error_string(gst_player_error_t error);

/**
 * 获取性能统计信息
 * @param handle 播放器句柄
 * @param fps 输出当前帧率
 * @param dropped_frames 输出丢帧数
 */
void gst_player_get_stats(gst_player_handle_t handle, float* fps, int* dropped_frames);

/* 人脸框信息 */
typedef struct {
    float center_x;     /* 中心点 X 坐标（像素） */
    float center_y;     /* 中心点 Y 坐标（像素） */
    float width;        /* 宽度（像素） */
    float height;       /* 高度（像素） */
    float score;        /* 置信度 (0-1) */
} gst_face_box_t;

/**
 * 设置人脸框（用于视频叠加绘制）
 * @param handle 播放器句柄
 * @param boxes 人脸框数组
 * @param count 人脸框数量
 * @param source_width 源图像宽度（用于坐标转换）
 * @param source_height 源图像高度
 * @return 错误码
 */
gst_player_error_t gst_player_set_face_boxes(gst_player_handle_t handle,
                                              const gst_face_box_t* boxes,
                                              int count,
                                              int source_width,
                                              int source_height);

/**
 * 清除人脸框
 * @param handle 播放器句柄
 */
void gst_player_clear_face_boxes(gst_player_handle_t handle);

#ifdef __cplusplus
}
#endif

#endif /* GST_VIDEO_PLAYER_H */
