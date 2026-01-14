/*
 * V4L2 + MPP Camera Library
 * 使用 V4L2 采集 MJPEG 数据，使用 Rockchip MPP 硬件解码
 */

#ifndef V4L2_MPP_CAMERA_H
#define V4L2_MPP_CAMERA_H

#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/* 帧回调函数类型 */
typedef void (*frame_callback_t)(void* user_data, uint8_t* bgra_data, int width, int height, int stride);

/* 相机上下文句柄 */
typedef void* camera_handle_t;

/* 错误码 */
typedef enum {
    CAMERA_OK = 0,
    CAMERA_ERROR_DEVICE_NOT_FOUND = -1,
    CAMERA_ERROR_DEVICE_BUSY = -2,
    CAMERA_ERROR_NOT_SUPPORTED = -3,
    CAMERA_ERROR_INVALID_PARAM = -4,
    CAMERA_ERROR_MPP_INIT_FAILED = -5,
    CAMERA_ERROR_V4L2_INIT_FAILED = -6,
    CAMERA_ERROR_OUT_OF_MEMORY = -7,
    CAMERA_ERROR_DECODE_FAILED = -8,
    CAMERA_ERROR_NOT_RUNNING = -9,
} camera_error_t;

/**
 * 初始化相机
 * @param device 设备路径 (如 /dev/video12)
 * @param width 分辨率宽度
 * @param height 分辨率高度
 * @param fps 帧率
 * @return 相机句柄，失败返回 NULL
 */
camera_handle_t camera_init(const char* device, int width, int height, int fps);

/**
 * 释放相机资源
 * @param handle 相机句柄
 */
void camera_deinit(camera_handle_t handle);

/**
 * 启动相机采集
 * @param handle 相机句柄
 * @param callback 帧回调函数
 * @param user_data 用户数据，传递给回调函数
 * @return 错误码
 */
camera_error_t camera_start(camera_handle_t handle, frame_callback_t callback, void* user_data);

/**
 * 停止相机采集
 * @param handle 相机句柄
 * @return 错误码
 */
camera_error_t camera_stop(camera_handle_t handle);

/**
 * 检查相机是否正在运行
 * @param handle 相机句柄
 * @return true 正在运行，false 未运行
 */
bool camera_is_running(camera_handle_t handle);

/**
 * 获取最新的一帧 (同步接口)
 * @param handle 相机句柄
 * @param bgra_data 输出缓冲区 (BGRA格式)
 * @param buffer_size 缓冲区大小
 * @param width 输出宽度
 * @param height 输出高度
 * @param timeout_ms 超时时间 (毫秒)
 * @return 错误码
 */
camera_error_t camera_capture_frame(camera_handle_t handle, uint8_t* bgra_data, 
                                     int buffer_size, int* width, int* height, int timeout_ms);

/**
 * 获取最后一次错误的描述
 * @param error 错误码
 * @return 错误描述字符串
 */
const char* camera_get_error_string(camera_error_t error);

#ifdef __cplusplus
}
#endif

#endif /* V4L2_MPP_CAMERA_H */
