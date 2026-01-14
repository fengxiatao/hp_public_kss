using System;
using System.Runtime.InteropServices;

namespace FaceLocker.Services
{
    /// <summary>
    /// GStreamer 互操作类 - Ubuntu ARM64 GStreamer 1.18.5 兼容版本
    /// </summary>
    public static class GStreamerInterop
    {
        #region GStreamer 常量
        public const int GST_PADDING = 4;
        #endregion

        #region GStreamer 枚举
        /// <summary>
        /// GStreamer 状态
        /// </summary>
        public enum GstState
        {
            GST_STATE_VOID_PENDING = 0,
            GST_STATE_NULL = 1,
            GST_STATE_READY = 2,
            GST_STATE_PAUSED = 3,
            GST_STATE_PLAYING = 4
        }

        /// <summary>
        /// 状态改变返回
        /// </summary>
        public enum GstStateChangeReturn
        {
            GST_STATE_CHANGE_FAILURE = 0,
            GST_STATE_CHANGE_SUCCESS = 1,
            GST_STATE_CHANGE_ASYNC = 2,
            GST_STATE_CHANGE_NO_PREROLL = 3
        }

        /// <summary>
        /// 流返回
        /// </summary>
        public enum GstFlowReturn
        {
            GST_FLOW_CUSTOM_SUCCESS_1 = 100,
            GST_FLOW_CUSTOM_SUCCESS = 101,
            GST_FLOW_RESEND = 102,
            GST_FLOW_OK = 0,
            GST_FLOW_NOT_LINKED = -1,
            GST_FLOW_FLUSHING = -2,
            GST_FLOW_EOS = -3,
            GST_FLOW_NOT_NEGOTIATED = -4,
            GST_FLOW_ERROR = -5,
            GST_FLOW_NOT_SUPPORTED = -6,
            GST_FLOW_CUSTOM_ERROR = -100,
            GST_FLOW_CUSTOM_ERROR_1 = -101,
            GST_FLOW_CUSTOM_ERROR_2 = -102
        }

        /// <summary>
        /// 消息类型
        /// </summary>
        [Flags]
        public enum GstMessageType
        {
            GST_MESSAGE_UNKNOWN = 0,
            GST_MESSAGE_EOS = 1 << 0,
            GST_MESSAGE_ERROR = 1 << 1,
            GST_MESSAGE_WARNING = 1 << 2,
            GST_MESSAGE_INFO = 1 << 3,
            GST_MESSAGE_TAG = 1 << 4,
            GST_MESSAGE_BUFFERING = 1 << 5,
            GST_MESSAGE_STATE_CHANGED = 1 << 6,
            GST_MESSAGE_STATE_DIRTY = 1 << 7,
            GST_MESSAGE_STEP_DONE = 1 << 8,
            GST_MESSAGE_CLOCK_PROVIDE = 1 << 9,
            GST_MESSAGE_CLOCK_LOST = 1 << 10,
            GST_MESSAGE_NEW_CLOCK = 1 << 11,
            GST_MESSAGE_STRUCTURE_CHANGE = 1 << 12,
            GST_MESSAGE_STREAM_STATUS = 1 << 13,
            GST_MESSAGE_APPLICATION = 1 << 14,
            GST_MESSAGE_ELEMENT = 1 << 15,
            GST_MESSAGE_SEGMENT_START = 1 << 16,
            GST_MESSAGE_SEGMENT_DONE = 1 << 17,
            GST_MESSAGE_DURATION_CHANGED = 1 << 18,
            GST_MESSAGE_LATENCY = 1 << 19,
            GST_MESSAGE_ASYNC_START = 1 << 20,
            GST_MESSAGE_ASYNC_DONE = 1 << 21,
            GST_MESSAGE_REQUEST_STATE = 1 << 22,
            GST_MESSAGE_STEP_START = 1 << 23,
            GST_MESSAGE_QOS = 1 << 24,
            GST_MESSAGE_PROGRESS = 1 << 25,
            GST_MESSAGE_TOC = 1 << 26,
            GST_MESSAGE_RESET_TIME = 1 << 27,
            GST_MESSAGE_STREAM_START = 1 << 28,
            GST_MESSAGE_NEED_CONTEXT = 1 << 29,
            GST_MESSAGE_HAVE_CONTEXT = 1 << 30,
            GST_MESSAGE_EXTENDED = 1 << 31,
            GST_MESSAGE_DEVICE_ADDED = 1 << 32,
            GST_MESSAGE_DEVICE_REMOVED = 1 << 33,
            GST_MESSAGE_ANY = ~0
        }

        /// <summary>
        /// 映射标志
        /// </summary>
        [Flags]
        public enum GstMapFlags
        {
            GST_MAP_READ = 1 << 0,
            GST_MAP_WRITE = 1 << 1,
            GST_MAP_FLAG_LAST = 1 << 16
        }
        #endregion

        #region GStreamer 结构体
        /// <summary>
        /// GstMapInfo 结构体
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct GstMapInfo
        {
            public IntPtr memory;
            public GstMapFlags flags;
            public IntPtr data;
            public UIntPtr size;
            public UIntPtr maxsize;
            public IntPtr user_data;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public IntPtr[] _gst_reserved;
        }

        /// <summary>
        /// GstAppSinkCallbacks 结构体
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct GstAppSinkCallbacks
        {
            public delegate void eos_delegate(IntPtr appsink, IntPtr user_data);
            public delegate GstFlowReturn new_preroll_delegate(IntPtr appsink, IntPtr user_data);
            public delegate GstFlowReturn new_sample_delegate(IntPtr appsink, IntPtr user_data);

            public eos_delegate eos;
            public new_preroll_delegate new_preroll;
            public new_sample_delegate new_sample;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = GST_PADDING)]
            public IntPtr[] _gst_reserved;
        }
        #endregion

        #region GStreamer 核心函数 - 使用正确的函数名称
        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool gst_init_check(IntPtr argc, IntPtr argv, out bool initSuccess);

        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr gst_parse_launch(string pipeline_description, out IntPtr error);

        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern GstStateChangeReturn gst_element_set_state(IntPtr element, GstState state);

        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr gst_element_get_bus(IntPtr element);

        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern void gst_object_unref(IntPtr obj);

        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr gst_bin_get_by_name(IntPtr bin, string name);

        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr gst_bus_timed_pop_filtered(IntPtr bus, ulong timeout, GstMessageType types);

        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern void gst_message_unref(IntPtr message);

        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern GstMessageType gst_message_get_type(IntPtr message);

        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern void gst_message_parse_error(IntPtr message, out IntPtr gerror, out IntPtr debug);

        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern void gst_message_parse_warning(IntPtr message, out IntPtr gerror, out IntPtr debug);
        #endregion

        #region GStreamer Appsink 函数
        [DllImport("libgstapp-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern void gst_app_sink_set_emit_signals(IntPtr appsink, bool emit);

        [DllImport("libgstapp-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern void gst_app_sink_set_max_buffers(IntPtr appsink, uint max);

        [DllImport("libgstapp-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern void gst_app_sink_set_drop(IntPtr appsink, bool drop);

        [DllImport("libgstapp-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern void gst_app_sink_set_callbacks(IntPtr appsink, ref GstAppSinkCallbacks callbacks, IntPtr user_data, IntPtr notify);

        [DllImport("libgstapp-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr gst_app_sink_pull_sample(IntPtr appsink);
        #endregion

        #region GStreamer Sample 函数
        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr gst_sample_get_buffer(IntPtr sample);

        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern void gst_sample_unref(IntPtr sample);
        #endregion

        #region GStreamer Buffer 函数
        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool gst_buffer_map(IntPtr buffer, ref GstMapInfo info, GstMapFlags flags);

        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        public static extern void gst_buffer_unmap(IntPtr buffer, ref GstMapInfo info);
        #endregion
    }
}