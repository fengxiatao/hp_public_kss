/*
 * V4L2 + MPP Camera Test Program
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <signal.h>
#include <unistd.h>
#include <time.h>

#include "v4l2_mpp_camera.h"

static volatile int g_running = 1;
static int g_frame_count = 0;
static struct timespec g_start_time;

static void signal_handler(int sig)
{
    (void)sig;
    g_running = 0;
}

static void frame_callback(void* user_data, uint8_t* bgra_data, int width, int height, int stride)
{
    (void)user_data;
    (void)bgra_data;
    (void)stride;
    
    g_frame_count++;
    
    /* 每100帧打印一次统计信息 */
    if (g_frame_count % 100 == 0) {
        struct timespec now;
        clock_gettime(CLOCK_MONOTONIC, &now);
        
        double elapsed = (now.tv_sec - g_start_time.tv_sec) + 
                        (now.tv_nsec - g_start_time.tv_nsec) / 1e9;
        double fps = g_frame_count / elapsed;
        
        printf("Frames: %d, FPS: %.2f, Resolution: %dx%d\n", 
               g_frame_count, fps, width, height);
    }
}

int main(int argc, char* argv[])
{
    const char* device = "/dev/video12";
    int width = 640;
    int height = 480;
    int fps = 30;
    int duration = 10;  /* 测试持续时间（秒） */
    
    /* 解析命令行参数 */
    for (int i = 1; i < argc; i++) {
        if (strcmp(argv[i], "-d") == 0 && i + 1 < argc) {
            device = argv[++i];
        } else if (strcmp(argv[i], "-w") == 0 && i + 1 < argc) {
            width = atoi(argv[++i]);
        } else if (strcmp(argv[i], "-h") == 0 && i + 1 < argc) {
            height = atoi(argv[++i]);
        } else if (strcmp(argv[i], "-f") == 0 && i + 1 < argc) {
            fps = atoi(argv[++i]);
        } else if (strcmp(argv[i], "-t") == 0 && i + 1 < argc) {
            duration = atoi(argv[++i]);
        } else if (strcmp(argv[i], "--help") == 0) {
            printf("Usage: %s [options]\n", argv[0]);
            printf("Options:\n");
            printf("  -d <device>    Video device (default: /dev/video12)\n");
            printf("  -w <width>     Video width (default: 640)\n");
            printf("  -h <height>    Video height (default: 480)\n");
            printf("  -f <fps>       Frame rate (default: 30)\n");
            printf("  -t <seconds>   Test duration (default: 10)\n");
            return 0;
        }
    }
    
    /* 设置信号处理 */
    signal(SIGINT, signal_handler);
    signal(SIGTERM, signal_handler);
    
    printf("=== V4L2 + MPP Camera Test ===\n");
    printf("Device: %s\n", device);
    printf("Resolution: %dx%d @ %dfps\n", width, height, fps);
    printf("Duration: %d seconds\n", duration);
    printf("============================\n\n");
    
    /* 初始化相机 */
    camera_handle_t camera = camera_init(device, width, height, fps);
    if (!camera) {
        fprintf(stderr, "Failed to initialize camera\n");
        return 1;
    }
    
    /* 启动采集 */
    clock_gettime(CLOCK_MONOTONIC, &g_start_time);
    
    camera_error_t err = camera_start(camera, frame_callback, NULL);
    if (err != CAMERA_OK) {
        fprintf(stderr, "Failed to start camera: %s\n", camera_get_error_string(err));
        camera_deinit(camera);
        return 1;
    }
    
    printf("Camera started. Press Ctrl+C to stop.\n\n");
    
    /* 运行指定时间 */
    int elapsed = 0;
    while (g_running && elapsed < duration) {
        sleep(1);
        elapsed++;
    }
    
    /* 停止采集 */
    camera_stop(camera);
    
    /* 打印最终统计 */
    struct timespec end_time;
    clock_gettime(CLOCK_MONOTONIC, &end_time);
    double total_time = (end_time.tv_sec - g_start_time.tv_sec) + 
                       (end_time.tv_nsec - g_start_time.tv_nsec) / 1e9;
    
    printf("\n=== Test Complete ===\n");
    printf("Total frames: %d\n", g_frame_count);
    printf("Total time: %.2f seconds\n", total_time);
    printf("Average FPS: %.2f\n", g_frame_count / total_time);
    printf("====================\n");
    
    /* 释放资源 */
    camera_deinit(camera);
    
    return 0;
}
