/*
 * V4L2 + MPP Camera Library Implementation - 高性能优化版本
 * 使用 V4L2 采集 MJPEG 数据，使用 Rockchip MPP 硬件解码
 * 
 * 优化点：
 * 1. 减少 V4L2 缓冲区数量降低延迟
 * 2. 复用 MPP 缓冲区避免频繁分配
 * 3. 非阻塞解码 + 流水线处理
 * 4. 优化的 YUV 转 BGRA（NEON 加速）
 */

#define MODULE_TAG "v4l2_mpp_camera"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <fcntl.h>
#include <unistd.h>
#include <errno.h>
#include <pthread.h>
#include <sys/ioctl.h>
#include <sys/mman.h>
#include <sys/select.h>
#include <time.h>
#include <linux/videodev2.h>

#include "rockchip/rk_mpi.h"
#include "rockchip/mpp_buffer.h"
#include "rockchip/mpp_frame.h"
#include "rockchip/mpp_packet.h"

#ifdef USE_RGA
#include "rga/im2d.h"
#include "rga/rga.h"
#endif

#include "v4l2_mpp_camera.h"

/* 对齐宏 */
#ifndef ALIGN
#define ALIGN(x, a) (((x) + (a) - 1) & ~((a) - 1))
#endif

/* MPP 错误码 */
#define MPP_ERR_BUFFER_FULL (-1012)

/* V4L2 缓冲区数量 - 4个平衡延迟和吞吐量 */
#define V4L2_BUFFER_COUNT 4

/* MPP 解码缓冲区数量 - 增加以支持流水线 */
#define MPP_BUFFER_COUNT 8

/* 帧缓冲区结构 */
typedef struct {
    void* start;
    size_t length;
} v4l2_buffer_t;

/* MPP 解码缓冲区 */
typedef struct {
    MppBuffer pkt_buf;
    MppBuffer frm_buf;
    size_t pkt_buf_size;
    size_t frm_buf_size;
} mpp_decode_buffer_t;

/* 相机上下文结构 */
typedef struct {
    /* V4L2 相关 */
    int v4l2_fd;
    v4l2_buffer_t v4l2_buffers[4];  /* 最大支持4个缓冲区 */
    int buffer_count;
    int width;
    int height;
    int fps;
    
    /* MPP 解码相关 */
    MppCtx mpp_ctx;
    MppApi* mpp_mpi;
    MppBufferGroup frm_grp;
    mpp_decode_buffer_t decode_bufs[MPP_BUFFER_COUNT];
    int current_buf_idx;
    int mpp_initialized;
    
    /* 输出帧缓冲 - 双缓冲 */
    uint8_t* bgra_buffer[2];
    int bgra_write_idx;
    int bgra_buffer_size;
    pthread_mutex_t frame_mutex;
    
    /* 线程相关 */
    pthread_t capture_thread;
    volatile int running;
    volatile int thread_started;
    
    /* 回调相关 */
    frame_callback_t callback;
    void* user_data;
    
    /* 性能统计 */
    int frame_count;
    int decode_count;
    
} camera_context_t;

/* 内部函数声明 */
static int v4l2_init(camera_context_t* ctx, const char* device);
static void v4l2_deinit(camera_context_t* ctx);
static int v4l2_start_streaming(camera_context_t* ctx);
static void v4l2_stop_streaming(camera_context_t* ctx);
static int mpp_decoder_init(camera_context_t* ctx);
static void mpp_decoder_deinit(camera_context_t* ctx);
static int decode_mjpeg_to_bgra_fast(camera_context_t* ctx, void* mjpeg_data, size_t mjpeg_size);
static void* capture_thread_func(void* arg);

/* NEON 优化的 YUV 转 BGRA */
#if defined(__ARM_NEON) || defined(__aarch64__)
#include <arm_neon.h>
#define USE_NEON 1
#endif

/* V4L2 ioctl 包装函数 */
static int xioctl(int fd, int request, void* arg)
{
    int r;
    do {
        r = ioctl(fd, request, arg);
    } while (r == -1 && (errno == EINTR || errno == EAGAIN));
    return r;
}

/* 初始化相机 */
camera_handle_t camera_init(const char* device, int width, int height, int fps)
{
    camera_context_t* ctx = NULL;
    
    if (!device || width <= 0 || height <= 0 || fps <= 0) {
        fprintf(stderr, "[%s] Invalid parameters\n", MODULE_TAG);
        return NULL;
    }
    
    ctx = (camera_context_t*)calloc(1, sizeof(camera_context_t));
    if (!ctx) {
        fprintf(stderr, "[%s] Failed to allocate context\n", MODULE_TAG);
        return NULL;
    }
    
    ctx->width = width;
    ctx->height = height;
    ctx->fps = fps;
    ctx->v4l2_fd = -1;
    ctx->bgra_write_idx = 0;
    
    pthread_mutex_init(&ctx->frame_mutex, NULL);
    
    /* 初始化 V4L2 */
    if (v4l2_init(ctx, device) != 0) {
        fprintf(stderr, "[%s] V4L2 init failed\n", MODULE_TAG);
        camera_deinit(ctx);
        return NULL;
    }
    
    /* 初始化 MPP 解码器 */
    if (mpp_decoder_init(ctx) != 0) {
        fprintf(stderr, "[%s] MPP decoder init failed\n", MODULE_TAG);
        camera_deinit(ctx);
        return NULL;
    }
    
    /* 分配双 BGRA 输出缓冲区 */
    ctx->bgra_buffer_size = width * height * 4;
    ctx->bgra_buffer[0] = (uint8_t*)malloc(ctx->bgra_buffer_size);
    ctx->bgra_buffer[1] = (uint8_t*)malloc(ctx->bgra_buffer_size);
    if (!ctx->bgra_buffer[0] || !ctx->bgra_buffer[1]) {
        fprintf(stderr, "[%s] Failed to allocate BGRA buffer\n", MODULE_TAG);
        camera_deinit(ctx);
        return NULL;
    }
    
    printf("[%s] Camera initialized: %s %dx%d@%dfps (optimized)\n", MODULE_TAG, device, width, height, fps);
    return (camera_handle_t)ctx;
}

/* 释放相机资源 */
void camera_deinit(camera_handle_t handle)
{
    camera_context_t* ctx = (camera_context_t*)handle;
    
    if (!ctx) return;
    
    /* 确保停止采集 */
    camera_stop(handle);
    
    /* 释放 BGRA 缓冲区 */
    if (ctx->bgra_buffer[0]) {
        free(ctx->bgra_buffer[0]);
        ctx->bgra_buffer[0] = NULL;
    }
    if (ctx->bgra_buffer[1]) {
        free(ctx->bgra_buffer[1]);
        ctx->bgra_buffer[1] = NULL;
    }
    
    /* 释放 MPP 解码器 */
    mpp_decoder_deinit(ctx);
    
    /* 释放 V4L2 资源 */
    v4l2_deinit(ctx);
    
    pthread_mutex_destroy(&ctx->frame_mutex);
    
    free(ctx);
    printf("[%s] Camera deinitialized\n", MODULE_TAG);
}

/* 启动相机采集 */
camera_error_t camera_start(camera_handle_t handle, frame_callback_t callback, void* user_data)
{
    camera_context_t* ctx = (camera_context_t*)handle;
    
    if (!ctx) return CAMERA_ERROR_INVALID_PARAM;
    if (ctx->running) return CAMERA_OK;
    
    ctx->callback = callback;
    ctx->user_data = user_data;
    ctx->running = 1;
    ctx->thread_started = 0;
    ctx->frame_count = 0;
    ctx->decode_count = 0;
    
    /* 启动 V4L2 流 */
    if (v4l2_start_streaming(ctx) != 0) {
        ctx->running = 0;
        return CAMERA_ERROR_V4L2_INIT_FAILED;
    }
    
    /* 启动采集线程 - 高优先级 */
    pthread_attr_t attr;
    pthread_attr_init(&attr);
    
    struct sched_param param;
    param.sched_priority = sched_get_priority_max(SCHED_FIFO);
    pthread_attr_setschedpolicy(&attr, SCHED_FIFO);
    pthread_attr_setschedparam(&attr, &param);
    
    if (pthread_create(&ctx->capture_thread, &attr, capture_thread_func, ctx) != 0) {
        /* 如果高优先级失败，尝试普通优先级 */
        pthread_attr_destroy(&attr);
        pthread_attr_init(&attr);
        if (pthread_create(&ctx->capture_thread, &attr, capture_thread_func, ctx) != 0) {
            fprintf(stderr, "[%s] Failed to create capture thread\n", MODULE_TAG);
            v4l2_stop_streaming(ctx);
            ctx->running = 0;
            pthread_attr_destroy(&attr);
            return CAMERA_ERROR_V4L2_INIT_FAILED;
        }
    }
    pthread_attr_destroy(&attr);
    
    /* 等待线程启动 */
    while (!ctx->thread_started && ctx->running) {
        usleep(100);
    }
    
    printf("[%s] Camera started\n", MODULE_TAG);
    return CAMERA_OK;
}

/* 停止相机采集 */
camera_error_t camera_stop(camera_handle_t handle)
{
    camera_context_t* ctx = (camera_context_t*)handle;
    
    if (!ctx) return CAMERA_ERROR_INVALID_PARAM;
    if (!ctx->running) return CAMERA_OK;
    
    ctx->running = 0;
    
    /* 等待采集线程结束 */
    if (ctx->thread_started) {
        pthread_join(ctx->capture_thread, NULL);
        ctx->thread_started = 0;
    }
    
    /* 停止 V4L2 流 */
    v4l2_stop_streaming(ctx);
    
    printf("[%s] Camera stopped (frames: %d, decoded: %d)\n", 
           MODULE_TAG, ctx->frame_count, ctx->decode_count);
    return CAMERA_OK;
}

/* 检查相机是否正在运行 */
bool camera_is_running(camera_handle_t handle)
{
    camera_context_t* ctx = (camera_context_t*)handle;
    return ctx && ctx->running;
}

/* 获取最新的一帧 */
camera_error_t camera_capture_frame(camera_handle_t handle, uint8_t* bgra_data, 
                                     int buffer_size, int* width, int* height, int timeout_ms)
{
    camera_context_t* ctx = (camera_context_t*)handle;
    
    if (!ctx || !bgra_data) return CAMERA_ERROR_INVALID_PARAM;
    if (!ctx->running) return CAMERA_ERROR_NOT_RUNNING;
    if (buffer_size < ctx->bgra_buffer_size) return CAMERA_ERROR_INVALID_PARAM;
    
    /* 读取最新帧（使用不是当前写入的那个） */
    int read_idx = 1 - ctx->bgra_write_idx;
    
    pthread_mutex_lock(&ctx->frame_mutex);
    memcpy(bgra_data, ctx->bgra_buffer[read_idx], ctx->bgra_buffer_size);
    pthread_mutex_unlock(&ctx->frame_mutex);
    
    if (width) *width = ctx->width;
    if (height) *height = ctx->height;
    
    return CAMERA_OK;
}

/* 获取错误描述 */
const char* camera_get_error_string(camera_error_t error)
{
    switch (error) {
        case CAMERA_OK: return "OK";
        case CAMERA_ERROR_DEVICE_NOT_FOUND: return "Device not found";
        case CAMERA_ERROR_DEVICE_BUSY: return "Device busy";
        case CAMERA_ERROR_NOT_SUPPORTED: return "Not supported";
        case CAMERA_ERROR_INVALID_PARAM: return "Invalid parameter";
        case CAMERA_ERROR_MPP_INIT_FAILED: return "MPP init failed";
        case CAMERA_ERROR_V4L2_INIT_FAILED: return "V4L2 init failed";
        case CAMERA_ERROR_OUT_OF_MEMORY: return "Out of memory";
        case CAMERA_ERROR_DECODE_FAILED: return "Decode failed";
        case CAMERA_ERROR_NOT_RUNNING: return "Not running";
        default: return "Unknown error";
    }
}

/* V4L2 初始化 */
static int v4l2_init(camera_context_t* ctx, const char* device)
{
    struct v4l2_capability cap;
    struct v4l2_format fmt;
    struct v4l2_requestbuffers req;
    struct v4l2_streamparm parm;
    int i;
    
    /* 打开设备 - 使用 O_NONBLOCK 非阻塞模式 */
    ctx->v4l2_fd = open(device, O_RDWR | O_NONBLOCK | O_CLOEXEC);
    if (ctx->v4l2_fd < 0) {
        fprintf(stderr, "[%s] Cannot open device %s: %s\n", MODULE_TAG, device, strerror(errno));
        return -1;
    }
    
    /* 查询设备能力 */
    if (xioctl(ctx->v4l2_fd, VIDIOC_QUERYCAP, &cap) < 0) {
        fprintf(stderr, "[%s] VIDIOC_QUERYCAP failed: %s\n", MODULE_TAG, strerror(errno));
        return -1;
    }
    
    if (!(cap.capabilities & V4L2_CAP_VIDEO_CAPTURE)) {
        fprintf(stderr, "[%s] Device does not support video capture\n", MODULE_TAG);
        return -1;
    }
    
    if (!(cap.capabilities & V4L2_CAP_STREAMING)) {
        fprintf(stderr, "[%s] Device does not support streaming\n", MODULE_TAG);
        return -1;
    }
    
    /* 设置视频格式为 MJPEG */
    memset(&fmt, 0, sizeof(fmt));
    fmt.type = V4L2_BUF_TYPE_VIDEO_CAPTURE;
    fmt.fmt.pix.width = ctx->width;
    fmt.fmt.pix.height = ctx->height;
    fmt.fmt.pix.pixelformat = V4L2_PIX_FMT_MJPEG;
    fmt.fmt.pix.field = V4L2_FIELD_NONE;
    
    if (xioctl(ctx->v4l2_fd, VIDIOC_S_FMT, &fmt) < 0) {
        fprintf(stderr, "[%s] VIDIOC_S_FMT failed: %s\n", MODULE_TAG, strerror(errno));
        return -1;
    }
    
    /* 验证格式 */
    if (fmt.fmt.pix.pixelformat != V4L2_PIX_FMT_MJPEG) {
        fprintf(stderr, "[%s] Device does not support MJPEG format\n", MODULE_TAG);
        return -1;
    }
    
    ctx->width = fmt.fmt.pix.width;
    ctx->height = fmt.fmt.pix.height;
    
    printf("[%s] Video format: %dx%d MJPEG\n", MODULE_TAG, ctx->width, ctx->height);
    
    /* 设置帧率 */
    memset(&parm, 0, sizeof(parm));
    parm.type = V4L2_BUF_TYPE_VIDEO_CAPTURE;
    parm.parm.capture.timeperframe.numerator = 1;
    parm.parm.capture.timeperframe.denominator = ctx->fps;
    
    if (xioctl(ctx->v4l2_fd, VIDIOC_S_PARM, &parm) < 0) {
        fprintf(stderr, "[%s] VIDIOC_S_PARM failed (fps): %s\n", MODULE_TAG, strerror(errno));
    }
    
    /* 请求缓冲区 - 最小化延迟 */
    memset(&req, 0, sizeof(req));
    req.count = V4L2_BUFFER_COUNT;
    req.type = V4L2_BUF_TYPE_VIDEO_CAPTURE;
    req.memory = V4L2_MEMORY_MMAP;
    
    if (xioctl(ctx->v4l2_fd, VIDIOC_REQBUFS, &req) < 0) {
        fprintf(stderr, "[%s] VIDIOC_REQBUFS failed: %s\n", MODULE_TAG, strerror(errno));
        return -1;
    }
    
    if (req.count < 2) {
        fprintf(stderr, "[%s] Insufficient buffer memory\n", MODULE_TAG);
        return -1;
    }
    
    ctx->buffer_count = req.count;
    
    /* 映射缓冲区 */
    for (i = 0; i < ctx->buffer_count; i++) {
        struct v4l2_buffer buf;
        
        memset(&buf, 0, sizeof(buf));
        buf.type = V4L2_BUF_TYPE_VIDEO_CAPTURE;
        buf.memory = V4L2_MEMORY_MMAP;
        buf.index = i;
        
        if (xioctl(ctx->v4l2_fd, VIDIOC_QUERYBUF, &buf) < 0) {
            fprintf(stderr, "[%s] VIDIOC_QUERYBUF failed: %s\n", MODULE_TAG, strerror(errno));
            return -1;
        }
        
        ctx->v4l2_buffers[i].length = buf.length;
        ctx->v4l2_buffers[i].start = mmap(NULL, buf.length,
                                           PROT_READ | PROT_WRITE,
                                           MAP_SHARED,
                                           ctx->v4l2_fd, buf.m.offset);
        
        if (ctx->v4l2_buffers[i].start == MAP_FAILED) {
            fprintf(stderr, "[%s] mmap failed: %s\n", MODULE_TAG, strerror(errno));
            return -1;
        }
    }
    
    printf("[%s] V4L2 initialized with %d buffers (low latency)\n", MODULE_TAG, ctx->buffer_count);
    return 0;
}

/* V4L2 释放 */
static void v4l2_deinit(camera_context_t* ctx)
{
    int i;
    
    for (i = 0; i < ctx->buffer_count; i++) {
        if (ctx->v4l2_buffers[i].start && ctx->v4l2_buffers[i].start != MAP_FAILED) {
            munmap(ctx->v4l2_buffers[i].start, ctx->v4l2_buffers[i].length);
            ctx->v4l2_buffers[i].start = NULL;
        }
    }
    
    if (ctx->v4l2_fd >= 0) {
        close(ctx->v4l2_fd);
        ctx->v4l2_fd = -1;
    }
}

/* 启动 V4L2 流 */
static int v4l2_start_streaming(camera_context_t* ctx)
{
    enum v4l2_buf_type type = V4L2_BUF_TYPE_VIDEO_CAPTURE;
    int i;
    
    /* 入队所有缓冲区 */
    for (i = 0; i < ctx->buffer_count; i++) {
        struct v4l2_buffer buf;
        
        memset(&buf, 0, sizeof(buf));
        buf.type = V4L2_BUF_TYPE_VIDEO_CAPTURE;
        buf.memory = V4L2_MEMORY_MMAP;
        buf.index = i;
        
        if (xioctl(ctx->v4l2_fd, VIDIOC_QBUF, &buf) < 0) {
            fprintf(stderr, "[%s] VIDIOC_QBUF failed: %s\n", MODULE_TAG, strerror(errno));
            return -1;
        }
    }
    
    /* 开始流传输 */
    if (xioctl(ctx->v4l2_fd, VIDIOC_STREAMON, &type) < 0) {
        fprintf(stderr, "[%s] VIDIOC_STREAMON failed: %s\n", MODULE_TAG, strerror(errno));
        return -1;
    }
    
    return 0;
}

/* 停止 V4L2 流 */
static void v4l2_stop_streaming(camera_context_t* ctx)
{
    enum v4l2_buf_type type = V4L2_BUF_TYPE_VIDEO_CAPTURE;
    xioctl(ctx->v4l2_fd, VIDIOC_STREAMOFF, &type);
}

/* MPP 解码器初始化 - 预分配缓冲区 */
static int mpp_decoder_init(camera_context_t* ctx)
{
    MPP_RET ret;
    MppDecCfg cfg = NULL;
    int i;
    
    /* 创建 MPP 上下文 */
    ret = mpp_create(&ctx->mpp_ctx, &ctx->mpp_mpi);
    if (ret != MPP_OK) {
        fprintf(stderr, "[%s] mpp_create failed: %d\n", MODULE_TAG, ret);
        return -1;
    }
    
    /* 初始化为 MJPEG 解码器 */
    ret = mpp_init(ctx->mpp_ctx, MPP_CTX_DEC, MPP_VIDEO_CodingMJPEG);
    if (ret != MPP_OK) {
        fprintf(stderr, "[%s] mpp_init failed: %d\n", MODULE_TAG, ret);
        return -1;
    }
    
    /* 配置解码器 - 低延迟模式 */
    mpp_dec_cfg_init(&cfg);
    ret = ctx->mpp_mpi->control(ctx->mpp_ctx, MPP_DEC_GET_CFG, cfg);
    if (ret == MPP_OK) {
        /* 不需要分割解析，MJPEG 每帧都是完整的 */
        mpp_dec_cfg_set_u32(cfg, "base:split_parse", 0);
        /* 设置快速输出模式 */
        mpp_dec_cfg_set_u32(cfg, "base:fast_out", 1);
        ctx->mpp_mpi->control(ctx->mpp_ctx, MPP_DEC_SET_CFG, cfg);
    }
    mpp_dec_cfg_deinit(cfg);
    
    /* 创建帧缓冲组 */
    ret = mpp_buffer_group_get_internal(&ctx->frm_grp, MPP_BUFFER_TYPE_ION);
    if (ret != MPP_OK) {
        fprintf(stderr, "[%s] mpp_buffer_group_get failed: %d\n", MODULE_TAG, ret);
        return -1;
    }
    
    /* 预分配解码缓冲区 */
    RK_U32 hor_stride = ALIGN(ctx->width, 16);
    RK_U32 ver_stride = ALIGN(ctx->height, 16);
    size_t pkt_size = ctx->width * ctx->height;  /* MJPEG 压缩后通常小于原始大小 */
    size_t frm_size = hor_stride * ver_stride * 4;  /* YUV422 最大 */
    
    for (i = 0; i < MPP_BUFFER_COUNT; i++) {
        ret = mpp_buffer_get(ctx->frm_grp, &ctx->decode_bufs[i].pkt_buf, pkt_size);
        if (ret != MPP_OK) {
            fprintf(stderr, "[%s] mpp_buffer_get pkt failed: %d\n", MODULE_TAG, ret);
            return -1;
        }
        ctx->decode_bufs[i].pkt_buf_size = pkt_size;
        
        ret = mpp_buffer_get(ctx->frm_grp, &ctx->decode_bufs[i].frm_buf, frm_size);
        if (ret != MPP_OK) {
            fprintf(stderr, "[%s] mpp_buffer_get frm failed: %d\n", MODULE_TAG, ret);
            return -1;
        }
        ctx->decode_bufs[i].frm_buf_size = frm_size;
    }
    
    ctx->current_buf_idx = 0;
    ctx->mpp_initialized = 1;
    
    printf("[%s] MPP MJPEG decoder initialized (pre-allocated %d buffers)\n", MODULE_TAG, MPP_BUFFER_COUNT);
    return 0;
}

/* MPP 解码器释放 */
static void mpp_decoder_deinit(camera_context_t* ctx)
{
    int i;
    
    /* 释放预分配的缓冲区 */
    for (i = 0; i < MPP_BUFFER_COUNT; i++) {
        if (ctx->decode_bufs[i].pkt_buf) {
            mpp_buffer_put(ctx->decode_bufs[i].pkt_buf);
            ctx->decode_bufs[i].pkt_buf = NULL;
        }
        if (ctx->decode_bufs[i].frm_buf) {
            mpp_buffer_put(ctx->decode_bufs[i].frm_buf);
            ctx->decode_bufs[i].frm_buf = NULL;
        }
    }
    
    if (ctx->frm_grp) {
        mpp_buffer_group_put(ctx->frm_grp);
        ctx->frm_grp = NULL;
    }
    
    if (ctx->mpp_ctx) {
        if (ctx->mpp_mpi) {
            ctx->mpp_mpi->reset(ctx->mpp_ctx);
        }
        mpp_destroy(ctx->mpp_ctx);
        ctx->mpp_ctx = NULL;
        ctx->mpp_mpi = NULL;
    }
}

/* 优化的 NV12/NV21 转 BGRA - 使用 NEON */
#ifdef USE_NEON
static void nv12_to_bgra_neon(uint8_t* __restrict y_plane, uint8_t* __restrict uv_plane,
                               uint8_t* __restrict bgra_data, int width, int height,
                               int y_stride, int uv_stride)
{
    for (int i = 0; i < height; i++) {
        uint8_t* y_row = y_plane + i * y_stride;
        uint8_t* uv_row = uv_plane + (i / 2) * uv_stride;
        uint8_t* bgra_row = bgra_data + i * width * 4;
        
        int j = 0;
        
        /* NEON 向量化处理 - 每次处理 8 个像素 */
        for (; j + 7 < width; j += 8) {
            /* 加载 8 个 Y 值 */
            uint8x8_t y_vec = vld1_u8(y_row + j);
            
            /* 加载 4 对 UV 值 */
            uint8x8_t uv_vec = vld1_u8(uv_row + (j / 2) * 2);
            
            /* 分离 U 和 V */
            uint8x8x2_t uv_deint = vuzp_u8(uv_vec, uv_vec);
            int16x8_t u_vec = vreinterpretq_s16_u16(vmovl_u8(uv_deint.val[0]));
            int16x8_t v_vec = vreinterpretq_s16_u16(vmovl_u8(uv_deint.val[1]));
            
            /* 扩展 UV 到每个像素 */
            int16x8_t u_exp = vcombine_s16(
                vzip1_s16(vget_low_s16(u_vec), vget_low_s16(u_vec)),
                vzip1_s16(vget_high_s16(u_vec), vget_high_s16(u_vec)));
            int16x8_t v_exp = vcombine_s16(
                vzip1_s16(vget_low_s16(v_vec), vget_low_s16(v_vec)),
                vzip1_s16(vget_high_s16(v_vec), vget_high_s16(v_vec)));
            
            /* 减去 128 */
            u_exp = vsubq_s16(u_exp, vdupq_n_s16(128));
            v_exp = vsubq_s16(v_exp, vdupq_n_s16(128));
            
            /* Y 扩展为 16 位 */
            int16x8_t y16 = vreinterpretq_s16_u16(vmovl_u8(y_vec));
            
            /* 计算 RGB */
            int16x8_t r = vaddq_s16(y16, vshrq_n_s16(vmulq_n_s16(v_exp, 359), 8));
            int16x8_t g = vsubq_s16(y16, vshrq_n_s16(vaddq_s16(vmulq_n_s16(u_exp, 88), vmulq_n_s16(v_exp, 183)), 8));
            int16x8_t b = vaddq_s16(y16, vshrq_n_s16(vmulq_n_s16(u_exp, 454), 8));
            
            /* 限制范围 0-255 */
            uint8x8_t r8 = vqmovun_s16(vmaxq_s16(vdupq_n_s16(0), vminq_s16(vdupq_n_s16(255), r)));
            uint8x8_t g8 = vqmovun_s16(vmaxq_s16(vdupq_n_s16(0), vminq_s16(vdupq_n_s16(255), g)));
            uint8x8_t b8 = vqmovun_s16(vmaxq_s16(vdupq_n_s16(0), vminq_s16(vdupq_n_s16(255), b)));
            uint8x8_t a8 = vdup_n_u8(255);
            
            /* 交织存储为 BGRA */
            uint8x8x4_t bgra;
            bgra.val[0] = b8;
            bgra.val[1] = g8;
            bgra.val[2] = r8;
            bgra.val[3] = a8;
            vst4_u8(bgra_row + j * 4, bgra);
        }
        
        /* 处理剩余像素 */
        for (; j < width; j++) {
            int y = y_row[j];
            int u = uv_row[(j / 2) * 2] - 128;
            int v = uv_row[(j / 2) * 2 + 1] - 128;
            
            int r = y + ((v * 359) >> 8);
            int g = y - ((u * 88 + v * 183) >> 8);
            int b = y + ((u * 454) >> 8);
            
            r = r < 0 ? 0 : (r > 255 ? 255 : r);
            g = g < 0 ? 0 : (g > 255 ? 255 : g);
            b = b < 0 ? 0 : (b > 255 ? 255 : b);
            
            bgra_row[j * 4 + 0] = b;
            bgra_row[j * 4 + 1] = g;
            bgra_row[j * 4 + 2] = r;
            bgra_row[j * 4 + 3] = 255;
        }
    }
}
#endif

/* 标量版本的 NV12 转 BGRA */
static void nv12_to_bgra_scalar(uint8_t* y_plane, uint8_t* uv_plane, uint8_t* bgra_data,
                                 int width, int height, int y_stride, int uv_stride)
{
    for (int i = 0; i < height; i++) {
        uint8_t* y_row = y_plane + i * y_stride;
        uint8_t* uv_row = uv_plane + (i / 2) * uv_stride;
        uint8_t* bgra_row = bgra_data + i * width * 4;
        
        for (int j = 0; j < width; j++) {
            int y = y_row[j];
            int u = uv_row[(j / 2) * 2] - 128;
            int v = uv_row[(j / 2) * 2 + 1] - 128;
            
            int r = y + ((v * 359) >> 8);
            int g = y - ((u * 88 + v * 183) >> 8);
            int b = y + ((u * 454) >> 8);
            
            r = r < 0 ? 0 : (r > 255 ? 255 : r);
            g = g < 0 ? 0 : (g > 255 ? 255 : g);
            b = b < 0 ? 0 : (b > 255 ? 255 : b);
            
            bgra_row[j * 4 + 0] = b;
            bgra_row[j * 4 + 1] = g;
            bgra_row[j * 4 + 2] = r;
            bgra_row[j * 4 + 3] = 255;
        }
    }
}

/* NV16 (YUV422) 转 BGRA */
static void nv16_to_bgra(uint8_t* y_plane, uint8_t* uv_plane, uint8_t* bgra_data,
                          int width, int height, int y_stride, int uv_stride)
{
    for (int i = 0; i < height; i++) {
        uint8_t* y_row = y_plane + i * y_stride;
        uint8_t* uv_row = uv_plane + i * uv_stride;
        uint8_t* bgra_row = bgra_data + i * width * 4;
        
        for (int j = 0; j < width; j++) {
            int y = y_row[j];
            int u = uv_row[(j / 2) * 2] - 128;
            int v = uv_row[(j / 2) * 2 + 1] - 128;
            
            int r = y + ((v * 359) >> 8);
            int g = y - ((u * 88 + v * 183) >> 8);
            int b = y + ((u * 454) >> 8);
            
            r = r < 0 ? 0 : (r > 255 ? 255 : r);
            g = g < 0 ? 0 : (g > 255 ? 255 : g);
            b = b < 0 ? 0 : (b > 255 ? 255 : b);
            
            bgra_row[j * 4 + 0] = b;
            bgra_row[j * 4 + 1] = g;
            bgra_row[j * 4 + 2] = r;
            bgra_row[j * 4 + 3] = 255;
        }
    }
}

#ifdef USE_RGA
/* RGA 硬件加速的 YUV 转 BGRA - 超快！ */
static int yuv_to_bgra_rga(MppBuffer yuv_buf, uint8_t* bgra_data, 
                            int width, int height, int hor_stride, int ver_stride,
                            MppFrameFormat format)
{
    IM_STATUS ret;
    rga_buffer_t src = {0};
    rga_buffer_t dst = {0};
    im_handle_param_t src_param = {0};
    im_handle_param_t dst_param = {0};
    rga_buffer_handle_t src_handle = 0;
    rga_buffer_handle_t dst_handle = 0;
    int rga_format;
    
    /* 确定 RGA 输入格式 */
    switch (format & MPP_FRAME_FMT_MASK) {
        case MPP_FMT_YUV420SP:
            rga_format = RK_FORMAT_YCbCr_420_SP;
            break;
        case MPP_FMT_YUV420SP_VU:
            rga_format = RK_FORMAT_YCrCb_420_SP;
            break;
        case MPP_FMT_YUV422SP:
            rga_format = RK_FORMAT_YCbCr_422_SP;
            break;
        case MPP_FMT_YUV422SP_VU:
            rga_format = RK_FORMAT_YCrCb_422_SP;
            break;
        default:
            rga_format = RK_FORMAT_YCbCr_420_SP;
            break;
    }
    
    /* 导入源缓冲区（MPP DMA-BUF） */
    src_param.width = hor_stride;
    src_param.height = ver_stride;
    src_param.format = rga_format;
    
    int src_fd = mpp_buffer_get_fd(yuv_buf);
    src_handle = importbuffer_fd(src_fd, &src_param);
    if (src_handle == 0) {
        fprintf(stderr, "[%s] RGA importbuffer_fd failed\n", MODULE_TAG);
        return -1;
    }
    
    /* 导入目标缓冲区（虚拟地址） */
    dst_param.width = width;
    dst_param.height = height;
    dst_param.format = RK_FORMAT_BGRA_8888;
    
    dst_handle = importbuffer_virtualaddr(bgra_data, &dst_param);
    if (dst_handle == 0) {
        fprintf(stderr, "[%s] RGA importbuffer_virtualaddr failed\n", MODULE_TAG);
        releasebuffer_handle(src_handle);
        return -1;
    }
    
    /* 配置源图像 */
    src = wrapbuffer_handle(src_handle, width, height, rga_format);
    src.wstride = hor_stride;
    src.hstride = ver_stride;
    
    /* 配置目标图像 */
    dst = wrapbuffer_handle(dst_handle, width, height, RK_FORMAT_BGRA_8888);
    dst.wstride = width;
    dst.hstride = height;
    
    /* 执行 RGA 格式转换 */
    ret = imcvtcolor(src, dst, src.format, dst.format);
    if (ret != IM_STATUS_SUCCESS) {
        fprintf(stderr, "[%s] RGA imcvtcolor failed: %s\n", MODULE_TAG, imStrError(ret));
        releasebuffer_handle(src_handle);
        releasebuffer_handle(dst_handle);
        return -1;
    }
    
    /* 释放句柄 */
    releasebuffer_handle(src_handle);
    releasebuffer_handle(dst_handle);
    
    return 0;
}
#endif

/* 通用 YUV 转 BGRA（CPU 回退版本） */
static void yuv_to_bgra_cpu(uint8_t* yuv_data, uint8_t* bgra_data, int width, int height, 
                             int hor_stride, int ver_stride, MppFrameFormat format)
{
    uint8_t* y_plane = yuv_data;
    uint8_t* uv_plane = yuv_data + hor_stride * ver_stride;
    
    switch (format & MPP_FRAME_FMT_MASK) {
        case MPP_FMT_YUV420SP:      /* NV12 */
        case MPP_FMT_YUV420SP_VU:   /* NV21 */
#ifdef USE_NEON
            nv12_to_bgra_neon(y_plane, uv_plane, bgra_data, width, height, hor_stride, hor_stride);
#else
            nv12_to_bgra_scalar(y_plane, uv_plane, bgra_data, width, height, hor_stride, hor_stride);
#endif
            break;
        case MPP_FMT_YUV422SP:      /* NV16 */
        case MPP_FMT_YUV422SP_VU:   /* NV61 */
            nv16_to_bgra(y_plane, uv_plane, bgra_data, width, height, hor_stride, hor_stride);
            break;
        default:
            /* 默认当作 NV12 处理 */
#ifdef USE_NEON
            nv12_to_bgra_neon(y_plane, uv_plane, bgra_data, width, height, hor_stride, hor_stride);
#else
            nv12_to_bgra_scalar(y_plane, uv_plane, bgra_data, width, height, hor_stride, hor_stride);
#endif
            break;
    }
}


/* 时间统计变量 */
static long g_poll_input_us = 0;
static long g_poll_output_us = 0;
static long g_yuv_convert_us = 0;
static int g_timing_count = 0;

/* 快速 MJPEG 解码到 BGRA - 使用 MppTask 接口（MJPEG 必须） */
static int decode_mjpeg_to_bgra_fast(camera_context_t* ctx, void* mjpeg_data, size_t mjpeg_size)
{
    MPP_RET ret;
    MppTask task = NULL;
    MppPacket packet = NULL;
    MppFrame frame = NULL;
    int got_frame = 0;
    struct timespec ts1, ts2, ts3, ts4;
    
    /* 获取当前缓冲区索引 */
    int buf_idx = ctx->current_buf_idx;
    ctx->current_buf_idx = (ctx->current_buf_idx + 1) % MPP_BUFFER_COUNT;
    
    mpp_decode_buffer_t* dec_buf = &ctx->decode_bufs[buf_idx];
    
    /* 检查缓冲区大小是否足够 */
    if (mjpeg_size > dec_buf->pkt_buf_size) {
        return -1;
    }
    
    /* 复制 MJPEG 数据到预分配缓冲区 */
    memcpy(mpp_buffer_get_ptr(dec_buf->pkt_buf), mjpeg_data, mjpeg_size);
    
    /* 创建输入包 */
    ret = mpp_packet_init_with_buffer(&packet, dec_buf->pkt_buf);
    if (ret != MPP_OK) {
        return -1;
    }
    mpp_packet_set_length(packet, mjpeg_size);
    
    /* 创建输出帧 */
    ret = mpp_frame_init(&frame);
    if (ret != MPP_OK) {
        mpp_packet_deinit(&packet);
        return -1;
    }
    mpp_frame_set_buffer(frame, dec_buf->frm_buf);
    
    clock_gettime(CLOCK_MONOTONIC, &ts1);
    
    /* 从输入端口获取任务 - 非阻塞 */
    ret = ctx->mpp_mpi->poll(ctx->mpp_ctx, MPP_PORT_INPUT, MPP_POLL_NON_BLOCK);
    if (ret != MPP_OK) {
        mpp_frame_deinit(&frame);
        mpp_packet_deinit(&packet);
        return -1;
    }
    
    ret = ctx->mpp_mpi->dequeue(ctx->mpp_ctx, MPP_PORT_INPUT, &task);
    if (ret != MPP_OK || !task) {
        mpp_frame_deinit(&frame);
        mpp_packet_deinit(&packet);
        return -1;
    }
    
    /* 设置输入包和输出帧到任务 */
    mpp_task_meta_set_packet(task, KEY_INPUT_PACKET, packet);
    mpp_task_meta_set_frame(task, KEY_OUTPUT_FRAME, frame);
    
    /* 入队任务到输入端口 */
    ret = ctx->mpp_mpi->enqueue(ctx->mpp_ctx, MPP_PORT_INPUT, task);
    if (ret != MPP_OK) {
        mpp_frame_deinit(&frame);
        mpp_packet_deinit(&packet);
        return -1;
    }
    
    clock_gettime(CLOCK_MONOTONIC, &ts2);
    
    /* 从输出端口获取解码结果 - 阻塞等待 */
    ret = ctx->mpp_mpi->poll(ctx->mpp_ctx, MPP_PORT_OUTPUT, MPP_POLL_BLOCK);
    if (ret != MPP_OK) {
        mpp_frame_deinit(&frame);
        mpp_packet_deinit(&packet);
        return -1;
    }
    
    clock_gettime(CLOCK_MONOTONIC, &ts3);
    
    ret = ctx->mpp_mpi->dequeue(ctx->mpp_ctx, MPP_PORT_OUTPUT, &task);
    if (ret != MPP_OK || !task) {
        mpp_frame_deinit(&frame);
        mpp_packet_deinit(&packet);
        return -1;
    }
    
    /* 获取解码后的帧 */
    MppFrame out_frame = NULL;
    mpp_task_meta_get_frame(task, KEY_OUTPUT_FRAME, &out_frame);
    
    if (out_frame) {
        MppBuffer out_buf = mpp_frame_get_buffer(out_frame);
        RK_U32 err_info = mpp_frame_get_errinfo(out_frame);
        
        if (out_buf && !err_info) {
            int width = mpp_frame_get_width(out_frame);
            int height = mpp_frame_get_height(out_frame);
            int hor_stride = mpp_frame_get_hor_stride(out_frame);
            int ver_stride = mpp_frame_get_ver_stride(out_frame);
            MppFrameFormat fmt = mpp_frame_get_fmt(out_frame);
            
            /* 调试：打印格式信息（仅首帧） */
            static int first_frame = 1;
            if (first_frame) {
                printf("[%s] Frame info: %dx%d, stride %dx%d, fmt=0x%x\n", 
                       MODULE_TAG, width, height, hor_stride, ver_stride, fmt);
#ifdef USE_RGA
                printf("[%s] Using RGA hardware acceleration for color conversion\n", MODULE_TAG);
#else
                printf("[%s] Using CPU for color conversion\n", MODULE_TAG);
#endif
                first_frame = 0;
            }
            
            /* 获取写入缓冲区 */
            int write_idx = ctx->bgra_write_idx;
            uint8_t* out_buf_ptr = ctx->bgra_buffer[write_idx];
            
#ifdef USE_RGA
            /* 使用 RGA 硬件加速转换 YUV 到 BGRA */
            int rga_ret = yuv_to_bgra_rga(out_buf, out_buf_ptr, width, height, 
                                          hor_stride, ver_stride, fmt);
            if (rga_ret != 0) {
                /* RGA 失败，回退到 CPU */
                uint8_t* yuv_data = (uint8_t*)mpp_buffer_get_ptr(out_buf);
                yuv_to_bgra_cpu(yuv_data, out_buf_ptr, width, height, hor_stride, ver_stride, fmt);
            }
#else
            /* 使用 CPU 转换 */
            uint8_t* yuv_data = (uint8_t*)mpp_buffer_get_ptr(out_buf);
            yuv_to_bgra_cpu(yuv_data, out_buf_ptr, width, height, hor_stride, ver_stride, fmt);
#endif
            
            clock_gettime(CLOCK_MONOTONIC, &ts4);
            
            /* 切换写入缓冲区（双缓冲） */
            pthread_mutex_lock(&ctx->frame_mutex);
            ctx->bgra_write_idx = 1 - write_idx;
            pthread_mutex_unlock(&ctx->frame_mutex);
            
            /* 累计时间 */
            g_poll_input_us += (ts2.tv_sec - ts1.tv_sec) * 1000000 + (ts2.tv_nsec - ts1.tv_nsec) / 1000;
            g_poll_output_us += (ts3.tv_sec - ts2.tv_sec) * 1000000 + (ts3.tv_nsec - ts2.tv_nsec) / 1000;
            g_yuv_convert_us += (ts4.tv_sec - ts3.tv_sec) * 1000000 + (ts4.tv_nsec - ts3.tv_nsec) / 1000;
            g_timing_count++;
            
            got_frame = 1;
            ctx->decode_count++;
        }
    }
    
    /* 将任务送回输出端口 */
    ctx->mpp_mpi->enqueue(ctx->mpp_ctx, MPP_PORT_OUTPUT, task);
    
    mpp_frame_deinit(&frame);
    mpp_packet_deinit(&packet);
    
    return got_frame ? 0 : -1;
}

/* 打印解码时间统计 */
static void print_decode_timing(void)
{
    if (g_timing_count > 0) {
        printf("[%s] Decode timing (avg over %d frames):\n", MODULE_TAG, g_timing_count);
        printf("[%s]   Poll input:  %.2f ms\n", MODULE_TAG, g_poll_input_us / 1000.0 / g_timing_count);
        printf("[%s]   Poll output: %.2f ms (HW decode wait)\n", MODULE_TAG, g_poll_output_us / 1000.0 / g_timing_count);
        printf("[%s]   YUV convert: %.2f ms\n", MODULE_TAG, g_yuv_convert_us / 1000.0 / g_timing_count);
    }
    /* 重置统计 */
    g_poll_input_us = 0;
    g_poll_output_us = 0;
    g_yuv_convert_us = 0;
    g_timing_count = 0;
}

/* 采集线程函数 - 高性能版本（带时间测量） */
static void* capture_thread_func(void* arg)
{
    camera_context_t* ctx = (camera_context_t*)arg;
    struct timeval tv;
    fd_set fds;
    struct timespec ts_start, ts_capture, ts_decode, ts_callback;
    long total_capture_us = 0, total_decode_us = 0, total_callback_us = 0;
    int measure_count = 0;
    
    ctx->thread_started = 1;
    printf("[%s] Capture thread started (high performance)\n", MODULE_TAG);
    
    while (ctx->running) {
        struct v4l2_buffer buf;
        
        clock_gettime(CLOCK_MONOTONIC, &ts_start);
        
        /* 等待数据就绪 - 短超时以保持响应 */
        FD_ZERO(&fds);
        FD_SET(ctx->v4l2_fd, &fds);
        tv.tv_sec = 0;
        tv.tv_usec = 33333;  /* 约 30fps 的周期 */
        
        int r = select(ctx->v4l2_fd + 1, &fds, NULL, NULL, &tv);
        if (r == -1) {
            if (errno == EINTR) continue;
            fprintf(stderr, "[%s] select error: %s\n", MODULE_TAG, strerror(errno));
            break;
        }
        if (r == 0) continue;  /* 超时 */
        
        /* 出队缓冲区 */
        memset(&buf, 0, sizeof(buf));
        buf.type = V4L2_BUF_TYPE_VIDEO_CAPTURE;
        buf.memory = V4L2_MEMORY_MMAP;
        
        if (xioctl(ctx->v4l2_fd, VIDIOC_DQBUF, &buf) < 0) {
            if (errno == EAGAIN) continue;
            fprintf(stderr, "[%s] VIDIOC_DQBUF failed: %s\n", MODULE_TAG, strerror(errno));
            break;
        }
        
        clock_gettime(CLOCK_MONOTONIC, &ts_capture);
        
        ctx->frame_count++;
        
        /* 解码 MJPEG 帧 */
        if (buf.bytesused > 0) {
            int decode_ret = decode_mjpeg_to_bgra_fast(ctx, ctx->v4l2_buffers[buf.index].start, buf.bytesused);
            
            clock_gettime(CLOCK_MONOTONIC, &ts_decode);
            
            if (decode_ret == 0) {
                /* 调用回调函数 - 使用最新完成的缓冲区 */
                if (ctx->callback) {
                    int read_idx = 1 - ctx->bgra_write_idx;
                    ctx->callback(ctx->user_data, ctx->bgra_buffer[read_idx], 
                                  ctx->width, ctx->height, ctx->width * 4);
                }
                
                clock_gettime(CLOCK_MONOTONIC, &ts_callback);
                
                /* 累计时间 */
                total_capture_us += (ts_capture.tv_sec - ts_start.tv_sec) * 1000000 + 
                                   (ts_capture.tv_nsec - ts_start.tv_nsec) / 1000;
                total_decode_us += (ts_decode.tv_sec - ts_capture.tv_sec) * 1000000 + 
                                  (ts_decode.tv_nsec - ts_capture.tv_nsec) / 1000;
                total_callback_us += (ts_callback.tv_sec - ts_decode.tv_sec) * 1000000 + 
                                    (ts_callback.tv_nsec - ts_decode.tv_nsec) / 1000;
                measure_count++;
            }
        }
        
        /* 立即重新入队缓冲区以减少延迟 */
        if (xioctl(ctx->v4l2_fd, VIDIOC_QBUF, &buf) < 0) {
            fprintf(stderr, "[%s] VIDIOC_QBUF failed: %s\n", MODULE_TAG, strerror(errno));
            break;
        }
    }
    
    /* 打印时间统计 */
    if (measure_count > 0) {
        printf("[%s] Timing stats (avg over %d frames):\n", MODULE_TAG, measure_count);
        printf("[%s]   Capture: %.2f ms\n", MODULE_TAG, total_capture_us / 1000.0 / measure_count);
        printf("[%s]   Decode:  %.2f ms\n", MODULE_TAG, total_decode_us / 1000.0 / measure_count);
        printf("[%s]   Callback:%.2f ms\n", MODULE_TAG, total_callback_us / 1000.0 / measure_count);
        printf("[%s]   Total:   %.2f ms (max %.1f FPS)\n", MODULE_TAG, 
               (total_capture_us + total_decode_us + total_callback_us) / 1000.0 / measure_count,
               1000000.0 * measure_count / (total_capture_us + total_decode_us + total_callback_us));
    }
    
    /* 打印解码详细时间 */
    print_decode_timing();
    
    printf("[%s] Capture thread exiting\n", MODULE_TAG);
    return NULL;
}
