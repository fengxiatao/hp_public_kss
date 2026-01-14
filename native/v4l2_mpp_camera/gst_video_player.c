/*
 * GStreamer 原生视频播放器 - cairooverlay 人脸框版本
 * 
 * 架构：
 * - 显示分支：cairooverlay → glimagesink
 * - 检测分支：appsink → 人脸检测
 * - 人脸框有延迟但能显示
 */

#include "gst_video_player.h"
#include <gst/gst.h>
#include <gst/video/videooverlay.h>
#include <gst/app/gstappsink.h>
#include <cairo/cairo.h>
#include <pthread.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/time.h>

#define MODULE_TAG "gst_video_player"
#define MAX_FACE_BOXES 10

static int64_t get_time_us(void)
{
    struct timeval tv;
    gettimeofday(&tv, NULL);
    return (int64_t)tv.tv_sec * 1000000 + tv.tv_usec;
}

typedef struct {
    GstElement* pipeline;
    GstElement* video_sink;
    GstElement* app_sink;
    GstElement* overlay;
    GstBus* bus;
    
    char device[64];
    int width;
    int height;
    int fps;
    gst_player_format_t format;
    bool use_hardware_decode;
    
    unsigned long x11_window_id;
    
    gst_frame_callback_t frame_callback;
    void* callback_user_data;
    
    /* 人脸框 */
    gst_face_box_t face_boxes[MAX_FACE_BOXES];
    int face_box_count;
    int face_source_width;
    int face_source_height;
    int video_width;
    int video_height;
    pthread_mutex_t face_box_mutex;
    
    volatile bool running;
    volatile bool playing;
    
    int face_detect_width;
    int face_detect_height;
    
    int frame_count;
    int64_t start_time;
} gst_player_context_t;

static bool g_gst_initialized = false;

gst_player_error_t gst_player_global_init(void)
{
    if (g_gst_initialized) return GST_PLAYER_OK;
    
    GError* error = NULL;
    if (!gst_init_check(NULL, NULL, &error)) {
        printf("[%s] GStreamer 初始化失败\n", MODULE_TAG);
        if (error) g_error_free(error);
        return GST_PLAYER_ERROR_INIT_FAILED;
    }
    
    g_gst_initialized = true;
    printf("[%s] GStreamer 初始化成功\n", MODULE_TAG);
    return GST_PLAYER_OK;
}

/* cairooverlay 绘制回调 */
static void on_cairo_draw(GstElement* overlay, cairo_t* cr,
                          guint64 timestamp, guint64 duration,
                          gpointer user_data)
{
    gst_player_context_t* ctx = (gst_player_context_t*)user_data;
    if (!ctx) return;
    
    pthread_mutex_lock(&ctx->face_box_mutex);
    
    if (ctx->face_box_count <= 0 || ctx->video_width <= 0 || ctx->face_source_width <= 0) {
        pthread_mutex_unlock(&ctx->face_box_mutex);
        return;
    }
    
    double scale_x = (double)ctx->video_width / ctx->face_source_width;
    double scale_y = (double)ctx->video_height / ctx->face_source_height;
    
    /* 绘制所有人脸框 */
    for (int i = 0; i < ctx->face_box_count && i < MAX_FACE_BOXES; i++) {
        gst_face_box_t* box = &ctx->face_boxes[i];
        
        double cx = box->center_x * scale_x;
        double cy = box->center_y * scale_y;
        double w = box->width * scale_x;
        double h = box->height * scale_y;
        
        double x = cx - w / 2;
        double y = cy - h / 2;
        
        /* 绿色框 */
        cairo_set_source_rgb(cr, 0.0, 1.0, 0.0);
        cairo_set_line_width(cr, 3.0);
        cairo_rectangle(cr, x, y, w, h);
        cairo_stroke(cr);
        
        /* 置信度文本 */
        if (box->score > 0) {
            char text[32];
            snprintf(text, sizeof(text), "%.0f%%", box->score * 100);
            cairo_set_font_size(cr, 16);
            cairo_move_to(cr, x, y > 20 ? y - 5 : y + h + 15);
            cairo_show_text(cr, text);
        }
    }
    
    pthread_mutex_unlock(&ctx->face_box_mutex);
}

/* caps 变化回调 */
static void on_caps_changed(GstElement* overlay, GstCaps* caps, gpointer user_data)
{
    gst_player_context_t* ctx = (gst_player_context_t*)user_data;
    if (!ctx || !caps) return;
    
    GstStructure* s = gst_caps_get_structure(caps, 0);
    if (s) {
        gst_structure_get_int(s, "width", &ctx->video_width);
        gst_structure_get_int(s, "height", &ctx->video_height);
        printf("[%s] 视频尺寸: %dx%d\n", MODULE_TAG, ctx->video_width, ctx->video_height);
    }
}

/* appsink 回调 */
static GstFlowReturn on_new_sample(GstAppSink* sink, gpointer user_data)
{
    gst_player_context_t* ctx = (gst_player_context_t*)user_data;
    if (!ctx || !ctx->running) return GST_FLOW_OK;
    
    GstSample* sample = gst_app_sink_pull_sample(sink);
    if (!sample) return GST_FLOW_OK;
    
    GstBuffer* buffer = gst_sample_get_buffer(sample);
    if (!buffer) {
        gst_sample_unref(sample);
        return GST_FLOW_OK;
    }
    
    GstMapInfo map;
    if (gst_buffer_map(buffer, &map, GST_MAP_READ)) {
        if (ctx->frame_callback) {
            ctx->frame_callback(ctx->callback_user_data,
                               map.data,
                               ctx->face_detect_width,
                               ctx->face_detect_height,
                               ctx->face_detect_width * 4);
        }
        gst_buffer_unmap(buffer, &map);
    }
    
    gst_sample_unref(sample);
    return GST_FLOW_OK;
}

static char* build_pipeline_string(const gst_player_config_t* config)
{
    char* pipeline = (char*)malloc(1024);
    if (!pipeline) return NULL;
    
    char* p = pipeline;
    int remaining = 1024;
    int written;
    
    written = snprintf(p, remaining, "v4l2src device=%s ! ", config->device);
    p += written; remaining -= written;
    
    switch (config->format) {
        case GST_PLAYER_FORMAT_MJPEG:
            written = snprintf(p, remaining,
                "image/jpeg,width=%d,height=%d,framerate=%d/1 ! ",
                config->width, config->height, config->fps);
            break;
        case GST_PLAYER_FORMAT_YUY2:
            written = snprintf(p, remaining,
                "video/x-raw,format=YUY2,width=%d,height=%d,framerate=%d/1 ! ",
                config->width, config->height, config->fps);
            break;
        default:
            free(pipeline);
            return NULL;
    }
    p += written; remaining -= written;
    
    if (config->format == GST_PLAYER_FORMAT_MJPEG) {
        /* 使用软件解码以兼容 cairooverlay */
        written = snprintf(p, remaining, "jpegdec ! ");
        p += written; remaining -= written;
    }
    
    written = snprintf(p, remaining, "tee name=t ! ");
    p += written; remaining -= written;
    
    /* 显示分支 - 使用 cairooverlay 绘制人脸框 */
    written = snprintf(p, remaining,
        "queue max-size-buffers=2 leaky=downstream ! "
        "videoconvert ! "
        "cairooverlay name=overlay ! "
        "videoconvert ! "
        "xvimagesink name=videosink sync=false force-aspect-ratio=false ");
    p += written; remaining -= written;
    
    /* 检测分支 */
    int face_w = config->face_detect_width > 0 ? config->face_detect_width : config->width;
    int face_h = config->face_detect_height > 0 ? config->face_detect_height : config->height;
    int face_fps = config->face_detect_fps > 0 ? config->face_detect_fps : 10;
    
    written = snprintf(p, remaining,
        "t. ! queue max-size-buffers=1 leaky=downstream ! "
        "videorate ! video/x-raw,framerate=%d/1 ! "
        "videoscale ! video/x-raw,width=%d,height=%d ! "
        "videoconvert ! video/x-raw,format=BGRA ! "
        "appsink name=facesink emit-signals=true max-buffers=1 drop=true sync=false",
        face_fps, face_w, face_h);
    
    return pipeline;
}

gst_player_handle_t gst_player_create(const gst_player_config_t* config)
{
    if (!config || !config->device) return NULL;
    if (gst_player_global_init() != GST_PLAYER_OK) return NULL;
    
    gst_player_context_t* ctx = (gst_player_context_t*)calloc(1, sizeof(gst_player_context_t));
    if (!ctx) return NULL;
    
    strncpy(ctx->device, config->device, sizeof(ctx->device) - 1);
    ctx->width = config->width;
    ctx->height = config->height;
    ctx->fps = config->fps;
    ctx->format = config->format;
    ctx->use_hardware_decode = config->use_hardware_decode;
    ctx->face_detect_width = config->face_detect_width > 0 ? config->face_detect_width : config->width;
    ctx->face_detect_height = config->face_detect_height > 0 ? config->face_detect_height : config->height;
    ctx->face_source_width = ctx->face_detect_width;
    ctx->face_source_height = ctx->face_detect_height;
    
    pthread_mutex_init(&ctx->face_box_mutex, NULL);
    
    char* pipeline_str = build_pipeline_string(config);
    if (!pipeline_str) {
        free(ctx);
        return NULL;
    }
    
    printf("[%s] 管道: %s\n", MODULE_TAG, pipeline_str);
    
    GError* error = NULL;
    ctx->pipeline = gst_parse_launch(pipeline_str, &error);
    free(pipeline_str);
    
    if (!ctx->pipeline || error) {
        printf("[%s] 创建管道失败: %s\n", MODULE_TAG, error ? error->message : "");
        if (error) g_error_free(error);
        free(ctx);
        return NULL;
    }
    
    ctx->video_sink = gst_bin_get_by_name(GST_BIN(ctx->pipeline), "videosink");
    ctx->app_sink = gst_bin_get_by_name(GST_BIN(ctx->pipeline), "facesink");
    ctx->overlay = gst_bin_get_by_name(GST_BIN(ctx->pipeline), "overlay");
    
    if (ctx->overlay) {
        g_signal_connect(ctx->overlay, "draw", G_CALLBACK(on_cairo_draw), ctx);
        g_signal_connect(ctx->overlay, "caps-changed", G_CALLBACK(on_caps_changed), ctx);
        printf("[%s] cairooverlay 已连接\n", MODULE_TAG);
    } else {
        printf("[%s] 警告: 未找到 cairooverlay\n", MODULE_TAG);
    }
    
    if (ctx->app_sink) {
        GstAppSinkCallbacks callbacks = { NULL, NULL, on_new_sample };
        gst_app_sink_set_callbacks(GST_APP_SINK(ctx->app_sink), &callbacks, ctx, NULL);
    }
    
    ctx->bus = gst_element_get_bus(ctx->pipeline);
    
    printf("[%s] 播放器创建成功 %dx%d\n", MODULE_TAG, ctx->width, ctx->height);
    return ctx;
}

void gst_player_destroy(gst_player_handle_t handle)
{
    gst_player_context_t* ctx = (gst_player_context_t*)handle;
    if (!ctx) return;
    
    gst_player_stop(handle);
    
    if (ctx->overlay) gst_object_unref(ctx->overlay);
    if (ctx->app_sink) gst_object_unref(ctx->app_sink);
    if (ctx->video_sink) gst_object_unref(ctx->video_sink);
    if (ctx->bus) gst_object_unref(ctx->bus);
    if (ctx->pipeline) {
        gst_element_set_state(ctx->pipeline, GST_STATE_NULL);
        gst_object_unref(ctx->pipeline);
    }
    
    pthread_mutex_destroy(&ctx->face_box_mutex);
    free(ctx);
    printf("[%s] 播放器已销毁\n", MODULE_TAG);
}

gst_player_error_t gst_player_set_window(gst_player_handle_t handle, unsigned long x11_window_id)
{
    gst_player_context_t* ctx = (gst_player_context_t*)handle;
    if (!ctx || !ctx->video_sink) return GST_PLAYER_ERROR_INVALID_PARAM;
    
    ctx->x11_window_id = x11_window_id;
    gst_video_overlay_set_window_handle(GST_VIDEO_OVERLAY(ctx->video_sink), x11_window_id);
    
    printf("[%s] 已设置窗口 0x%lx\n", MODULE_TAG, x11_window_id);
    return GST_PLAYER_OK;
}

gst_player_error_t gst_player_set_frame_callback(gst_player_handle_t handle,
                                                   gst_frame_callback_t callback,
                                                   void* user_data)
{
    gst_player_context_t* ctx = (gst_player_context_t*)handle;
    if (!ctx) return GST_PLAYER_ERROR_INVALID_PARAM;
    
    ctx->frame_callback = callback;
    ctx->callback_user_data = user_data;
    return GST_PLAYER_OK;
}

gst_player_error_t gst_player_start(gst_player_handle_t handle)
{
    gst_player_context_t* ctx = (gst_player_context_t*)handle;
    if (!ctx || !ctx->pipeline) return GST_PLAYER_ERROR_INVALID_PARAM;
    if (ctx->playing) return GST_PLAYER_OK;
    
    ctx->running = true;
    ctx->playing = true;
    ctx->start_time = get_time_us();
    ctx->frame_count = 0;
    
    GstStateChangeReturn ret = gst_element_set_state(ctx->pipeline, GST_STATE_PLAYING);
    if (ret == GST_STATE_CHANGE_FAILURE) {
        ctx->running = false;
        ctx->playing = false;
        return GST_PLAYER_ERROR_PIPELINE_FAILED;
    }
    
    printf("[%s] 播放启动（cairooverlay 人脸框）\n", MODULE_TAG);
    return GST_PLAYER_OK;
}

gst_player_error_t gst_player_stop(gst_player_handle_t handle)
{
    gst_player_context_t* ctx = (gst_player_context_t*)handle;
    if (!ctx) return GST_PLAYER_ERROR_INVALID_PARAM;
    if (!ctx->playing) return GST_PLAYER_OK;
    
    ctx->running = false;
    ctx->playing = false;
    
    if (ctx->pipeline)
        gst_element_set_state(ctx->pipeline, GST_STATE_NULL);
    
    printf("[%s] 播放停止\n", MODULE_TAG);
    return GST_PLAYER_OK;
}

bool gst_player_is_playing(gst_player_handle_t handle)
{
    gst_player_context_t* ctx = (gst_player_context_t*)handle;
    return ctx && ctx->playing;
}

const char* gst_player_get_error_string(gst_player_error_t error)
{
    switch (error) {
        case GST_PLAYER_OK: return "成功";
        case GST_PLAYER_ERROR_INIT_FAILED: return "初始化失败";
        case GST_PLAYER_ERROR_INVALID_PARAM: return "无效参数";
        case GST_PLAYER_ERROR_DEVICE_NOT_FOUND: return "设备未找到";
        case GST_PLAYER_ERROR_PIPELINE_FAILED: return "管道失败";
        case GST_PLAYER_ERROR_NO_DISPLAY: return "无显示";
        case GST_PLAYER_ERROR_WINDOW_INVALID: return "窗口无效";
        default: return "未知错误";
    }
}

void gst_player_get_stats(gst_player_handle_t handle, float* fps, int* dropped_frames)
{
    gst_player_context_t* ctx = (gst_player_context_t*)handle;
    if (!ctx) {
        if (fps) *fps = 0;
        if (dropped_frames) *dropped_frames = 0;
        return;
    }
    int64_t elapsed = get_time_us() - ctx->start_time;
    if (fps && elapsed > 0)
        *fps = (float)ctx->frame_count * 1000000.0f / elapsed;
    if (dropped_frames) *dropped_frames = 0;
}

gst_player_error_t gst_player_set_face_boxes(gst_player_handle_t handle,
                                              const gst_face_box_t* boxes,
                                              int count,
                                              int source_width,
                                              int source_height)
{
    gst_player_context_t* ctx = (gst_player_context_t*)handle;
    if (!ctx) return GST_PLAYER_ERROR_INVALID_PARAM;
    
    pthread_mutex_lock(&ctx->face_box_mutex);
    
    if (boxes && count > 0) {
        int n = count < MAX_FACE_BOXES ? count : MAX_FACE_BOXES;
        memcpy(ctx->face_boxes, boxes, n * sizeof(gst_face_box_t));
        ctx->face_box_count = n;
        ctx->face_source_width = source_width > 0 ? source_width : ctx->face_detect_width;
        ctx->face_source_height = source_height > 0 ? source_height : ctx->face_detect_height;
    } else {
        ctx->face_box_count = 0;
    }
    
    pthread_mutex_unlock(&ctx->face_box_mutex);
    return GST_PLAYER_OK;
}

void gst_player_clear_face_boxes(gst_player_handle_t handle)
{
    gst_player_context_t* ctx = (gst_player_context_t*)handle;
    if (!ctx) return;
    
    pthread_mutex_lock(&ctx->face_box_mutex);
    ctx->face_box_count = 0;
    pthread_mutex_unlock(&ctx->face_box_mutex);
}
