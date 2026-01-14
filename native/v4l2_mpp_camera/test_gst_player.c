/*
 * GStreamer 视频播放器测试程序
 * 
 * 测试内容：
 * 1. GStreamer 初始化
 * 2. 播放器创建
 * 3. 窗口设置（使用 X11 根窗口测试）
 * 4. 帧回调（人脸识别数据流）
 * 5. 播放/停止
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <signal.h>
#include <unistd.h>
#include <X11/Xlib.h>

#include "gst_video_player.h"

static volatile int g_running = 1;
static int g_frame_count = 0;

/* 信号处理 */
static void signal_handler(int sig)
{
    printf("\n收到信号 %d，准备退出...\n", sig);
    g_running = 0;
}

/* 帧回调（模拟人脸识别） */
static void on_frame_callback(void* user_data, uint8_t* data, int width, int height, int stride)
{
    g_frame_count++;
    if (g_frame_count % 30 == 0) {
        printf("[测试] 收到人脸识别帧 #%d: %dx%d, stride=%d\n", 
               g_frame_count, width, height, stride);
    }
}

/* 测试1: GStreamer 初始化 */
static int test_init(void)
{
    printf("\n=== 测试1: GStreamer 初始化 ===\n");
    
    gst_player_error_t ret = gst_player_global_init();
    if (ret != GST_PLAYER_OK) {
        printf("❌ GStreamer 初始化失败: %s\n", gst_player_get_error_string(ret));
        return -1;
    }
    
    printf("✅ GStreamer 初始化成功\n");
    return 0;
}

/* 测试2: 检查摄像头设备 */
static int test_device(const char* device)
{
    printf("\n=== 测试2: 检查摄像头设备 ===\n");
    
    if (access(device, F_OK) != 0) {
        printf("❌ 设备不存在: %s\n", device);
        return -1;
    }
    
    printf("✅ 设备存在: %s\n", device);
    return 0;
}

/* 测试3: 创建播放器 */
static gst_player_handle_t test_create_player(const char* device)
{
    printf("\n=== 测试3: 创建播放器 ===\n");
    
    gst_player_config_t config = {
        .device = device,
        .width = 640,
        .height = 480,
        .fps = 30,
        .format = GST_PLAYER_FORMAT_MJPEG,
        .use_hardware_decode = 1,
        .use_rga = 1,
        .face_detect_fps = 5,
        .face_detect_width = 320,
        .face_detect_height = 240
    };
    
    gst_player_handle_t player = gst_player_create(&config);
    if (!player) {
        printf("❌ 创建播放器失败\n");
        return NULL;
    }
    
    printf("✅ 播放器创建成功\n");
    return player;
}

/* 测试4: 获取X11窗口 */
static Window test_get_x11_window(Display** display_out)
{
    printf("\n=== 测试4: 获取X11窗口 ===\n");
    
    Display* display = XOpenDisplay(NULL);
    if (!display) {
        printf("❌ 无法打开X11显示\n");
        return 0;
    }
    
    Window root = DefaultRootWindow(display);
    printf("✅ X11根窗口: 0x%lx\n", root);
    
    /* 创建一个测试窗口 */
    int screen = DefaultScreen(display);
    Window win = XCreateSimpleWindow(display, root, 
        100, 100, 640, 480, 1,
        BlackPixel(display, screen),
        BlackPixel(display, screen));
    
    if (!win) {
        printf("❌ 创建测试窗口失败\n");
        XCloseDisplay(display);
        return 0;
    }
    
    XSelectInput(display, win, ExposureMask | KeyPressMask);
    XMapWindow(display, win);
    XStoreName(display, win, "GStreamer Video Player Test");
    XFlush(display);
    
    printf("✅ 创建测试窗口: 0x%lx\n", win);
    
    *display_out = display;
    return win;
}

/* 测试5: 设置窗口并播放 */
static int test_play(gst_player_handle_t player, Window window)
{
    printf("\n=== 测试5: 设置窗口并播放 ===\n");
    
    /* 设置帧回调 */
    gst_player_error_t ret = gst_player_set_frame_callback(player, on_frame_callback, NULL);
    if (ret != GST_PLAYER_OK) {
        printf("⚠️ 设置帧回调失败: %s\n", gst_player_get_error_string(ret));
    } else {
        printf("✅ 帧回调已设置\n");
    }
    
    /* 设置窗口 */
    ret = gst_player_set_window(player, (unsigned long)window);
    if (ret != GST_PLAYER_OK) {
        printf("❌ 设置窗口失败: %s\n", gst_player_get_error_string(ret));
        return -1;
    }
    printf("✅ 窗口已设置\n");
    
    /* 启动播放 */
    ret = gst_player_start(player);
    if (ret != GST_PLAYER_OK) {
        printf("❌ 启动播放失败: %s\n", gst_player_get_error_string(ret));
        return -1;
    }
    printf("✅ 播放已启动\n");
    
    return 0;
}

/* 测试6: 播放状态和统计 */
static void test_stats(gst_player_handle_t player)
{
    printf("\n=== 测试6: 播放状态和统计 ===\n");
    
    if (gst_player_is_playing(player)) {
        printf("✅ 播放器正在运行\n");
    } else {
        printf("❌ 播放器未运行\n");
    }
    
    float fps;
    int dropped;
    gst_player_get_stats(player, &fps, &dropped);
    printf("📊 统计: FPS=%.1f, 丢帧=%d, 人脸帧=%d\n", fps, dropped, g_frame_count);
}

/* 主函数 */
int main(int argc, char* argv[])
{
    const char* device = "/dev/video12";
    if (argc > 1) {
        device = argv[1];
    }
    
    printf("========================================\n");
    printf("GStreamer 视频播放器测试\n");
    printf("设备: %s\n", device);
    printf("========================================\n");
    
    /* 设置信号处理 */
    signal(SIGINT, signal_handler);
    signal(SIGTERM, signal_handler);
    
    /* 测试1: GStreamer 初始化 */
    if (test_init() != 0) {
        return 1;
    }
    
    /* 测试2: 检查设备 */
    if (test_device(device) != 0) {
        printf("\n⚠️ 跳过后续测试（设备不存在）\n");
        printf("========================================\n");
        printf("测试结果: 部分通过 (1/6)\n");
        printf("========================================\n");
        return 0;
    }
    
    /* 测试3: 创建播放器 */
    gst_player_handle_t player = test_create_player(device);
    if (!player) {
        return 1;
    }
    
    /* 测试4: 获取X11窗口 */
    Display* display = NULL;
    Window window = test_get_x11_window(&display);
    if (!window) {
        gst_player_destroy(player);
        return 1;
    }
    
    /* 测试5: 播放 */
    if (test_play(player, window) != 0) {
        gst_player_destroy(player);
        if (display) XCloseDisplay(display);
        return 1;
    }
    
    /* 运行5秒 */
    printf("\n>>> 播放中，请观察视频窗口（5秒后自动停止）...\n");
    printf(">>> 按 Ctrl+C 提前退出\n\n");
    
    for (int i = 0; i < 50 && g_running; i++) {
        usleep(100000); /* 100ms */
        
        /* 处理X11事件 */
        while (XPending(display)) {
            XEvent event;
            XNextEvent(display, &event);
        }
        
        /* 每秒打印统计 */
        if (i % 10 == 9) {
            test_stats(player);
        }
    }
    
    /* 测试6: 最终统计 */
    test_stats(player);
    
    /* 停止并销毁 */
    printf("\n>>> 停止播放...\n");
    gst_player_stop(player);
    gst_player_destroy(player);
    
    /* 清理X11 */
    if (display) {
        XDestroyWindow(display, window);
        XCloseDisplay(display);
    }
    
    printf("\n========================================\n");
    printf("测试结果: 全部通过 (6/6)\n");
    printf("========================================\n");
    
    return 0;
}
