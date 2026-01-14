cmake_minimum_required(VERSION 3.10)
project(v4l2_mpp_camera C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} -Wall -fPIC -O3")

# 查找 MPP 库
find_library(MPP_LIBRARY rockchip_mpp HINTS /usr/lib /usr/local/lib)
find_path(MPP_INCLUDE_DIR rockchip/rk_mpi.h HINTS /usr/include /usr/local/include)

# 查找 RGA 库
find_library(RGA_LIBRARY rga HINTS /usr/lib /usr/local/lib /lib/aarch64-linux-gnu)
find_path(RGA_INCLUDE_DIR rga/im2d.h HINTS /usr/include /usr/local/include)

# 查找 GStreamer 库
find_package(PkgConfig REQUIRED)
pkg_check_modules(GST REQUIRED gstreamer-1.0 gstreamer-video-1.0 gstreamer-app-1.0)

# 查找 Cairo 库（用于人脸框绘制）
pkg_check_modules(CAIRO REQUIRED cairo)

# 查找 X11 扩展库
pkg_check_modules(XEXT xext)

if(NOT MPP_LIBRARY)
    message(FATAL_ERROR "MPP library not found!")
endif()

if(NOT MPP_INCLUDE_DIR)
    message(FATAL_ERROR "MPP headers not found!")
endif()

message(STATUS "MPP library: ${MPP_LIBRARY}")
message(STATUS "MPP include: ${MPP_INCLUDE_DIR}")

if(RGA_LIBRARY AND RGA_INCLUDE_DIR)
    message(STATUS "RGA library: ${RGA_LIBRARY}")
    message(STATUS "RGA include: ${RGA_INCLUDE_DIR}")
    add_definitions(-DUSE_RGA=1)
else()
    message(WARNING "RGA not found, using CPU for color conversion")
endif()

message(STATUS "GStreamer libraries: ${GST_LIBRARIES}")
message(STATUS "GStreamer include: ${GST_INCLUDE_DIRS}")

# ============================================
# V4L2 + MPP 摄像头库（原有）
# ============================================
set(V4L2_MPP_SOURCES
    v4l2_mpp_camera.c
)

add_library(v4l2_mpp_camera SHARED ${V4L2_MPP_SOURCES})

target_include_directories(v4l2_mpp_camera PRIVATE
    ${MPP_INCLUDE_DIR}
    ${RGA_INCLUDE_DIR}
)

target_link_libraries(v4l2_mpp_camera
    ${MPP_LIBRARY}
    ${RGA_LIBRARY}
    pthread
)

# ============================================
# GStreamer 原生视频播放器库（新增）
# 支持 X11 窗口嵌入，零拷贝渲染
# ============================================
set(GST_PLAYER_SOURCES
    gst_video_player.c
)

add_library(gst_video_player SHARED ${GST_PLAYER_SOURCES})

target_include_directories(gst_video_player PRIVATE
    ${GST_INCLUDE_DIRS}
    ${CAIRO_INCLUDE_DIRS}
)

target_link_libraries(gst_video_player
    ${GST_LIBRARIES}
    ${CAIRO_LIBRARIES}
    X11
    Xext
    pthread
)

# ============================================
# 安装目标
# ============================================
install(TARGETS v4l2_mpp_camera gst_video_player
    LIBRARY DESTINATION lib
)

install(FILES v4l2_mpp_camera.h gst_video_player.h
    DESTINATION include
)

# ============================================
# 测试程序
# ============================================
add_executable(v4l2_mpp_test test_camera.c)
target_include_directories(v4l2_mpp_test PRIVATE ${MPP_INCLUDE_DIR} ${RGA_INCLUDE_DIR})
target_link_libraries(v4l2_mpp_test v4l2_mpp_camera ${MPP_LIBRARY} ${RGA_LIBRARY} pthread)

# GStreamer 视频播放器测试程序
add_executable(gst_player_test test_gst_player.c)
target_include_directories(gst_player_test PRIVATE ${GST_INCLUDE_DIRS})
target_link_libraries(gst_player_test gst_video_player ${GST_LIBRARIES} X11 pthread)
