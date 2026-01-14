using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace FaceLocker.Views.Controls
{
    /// <summary>
    /// 人脸框覆盖层控件
    /// 用于在视频画面上显示人脸检测框
    /// 支持原生视频渲染模式（GStreamer）
    /// </summary>
    public class FaceBoxOverlay : Control
    {
        #region 依赖属性

        /// <summary>
        /// 人脸框列表属性
        /// </summary>
        public static readonly StyledProperty<IList<FaceBoxInfo>?> FaceBoxesProperty =
            AvaloniaProperty.Register<FaceBoxOverlay, IList<FaceBoxInfo>?>(nameof(FaceBoxes));

        /// <summary>
        /// 人脸框列表
        /// </summary>
        public IList<FaceBoxInfo>? FaceBoxes
        {
            get => GetValue(FaceBoxesProperty);
            set => SetValue(FaceBoxesProperty, value);
        }

        /// <summary>
        /// 边框颜色属性
        /// </summary>
        public static readonly StyledProperty<IBrush> BoxBrushProperty =
            AvaloniaProperty.Register<FaceBoxOverlay, IBrush>(nameof(BoxBrush), 
                new SolidColorBrush(Color.FromRgb(0, 255, 0))); // 亮绿色

        /// <summary>
        /// 边框颜色
        /// </summary>
        public IBrush BoxBrush
        {
            get => GetValue(BoxBrushProperty);
            set => SetValue(BoxBrushProperty, value);
        }

        /// <summary>
        /// 边框粗细属性
        /// </summary>
        public static readonly StyledProperty<double> BoxThicknessProperty =
            AvaloniaProperty.Register<FaceBoxOverlay, double>(nameof(BoxThickness), 3.0);

        /// <summary>
        /// 边框粗细
        /// </summary>
        public double BoxThickness
        {
            get => GetValue(BoxThicknessProperty);
            set => SetValue(BoxThicknessProperty, value);
        }

        /// <summary>
        /// 源视频宽度（摄像头分辨率宽度）
        /// </summary>
        public static readonly StyledProperty<int> SourceWidthProperty =
            AvaloniaProperty.Register<FaceBoxOverlay, int>(nameof(SourceWidth), 640);

        public int SourceWidth
        {
            get => GetValue(SourceWidthProperty);
            set => SetValue(SourceWidthProperty, value);
        }

        /// <summary>
        /// 源视频高度（摄像头分辨率高度）
        /// </summary>
        public static readonly StyledProperty<int> SourceHeightProperty =
            AvaloniaProperty.Register<FaceBoxOverlay, int>(nameof(SourceHeight), 360);

        public int SourceHeight
        {
            get => GetValue(SourceHeightProperty);
            set => SetValue(SourceHeightProperty, value);
        }

        #endregion

        static FaceBoxOverlay()
        {
            // 当属性变化时触发重绘
            AffectsRender<FaceBoxOverlay>(FaceBoxesProperty, BoxBrushProperty, BoxThicknessProperty,
                SourceWidthProperty, SourceHeightProperty);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var boxes = FaceBoxes;
            var bounds = Bounds;
            
            if (boxes == null || boxes.Count == 0)
            {
                return;
            }

            var pen = new Pen(BoxBrush, BoxThickness);

            // 计算缩放比例（从源图像尺寸到控件尺寸）
            double scaleX = bounds.Width / Math.Max(1, SourceWidth);
            double scaleY = bounds.Height / Math.Max(1, SourceHeight);

            foreach (var box in boxes)
            {
                // 百度SDK返回的是像素坐标（中心点+宽高）
                // 直接使用像素值，然后缩放到控件大小
                double centerX = box.CenterX;
                double centerY = box.CenterY;
                double width = box.Width;
                double height = box.Height;

                // 计算左上角坐标（在源图像坐标系中）
                double srcLeft = centerX - width / 2;
                double srcTop = centerY - height / 2;

                // 缩放到控件坐标系
                double left = srcLeft * scaleX;
                double top = srcTop * scaleY;
                double rectWidth = width * scaleX;
                double rectHeight = height * scaleY;

                // 边界检查
                left = Math.Max(0, Math.Min(bounds.Width - 10, left));
                top = Math.Max(0, Math.Min(bounds.Height - 10, top));
                rectWidth = Math.Max(10, Math.Min(bounds.Width - left, rectWidth));
                rectHeight = Math.Max(10, Math.Min(bounds.Height - top, rectHeight));

                // 绘制矩形框
                var rect = new Rect(left, top, rectWidth, rectHeight);
                context.DrawRectangle(null, pen, rect, 4, 4); // 带圆角

                // 如果有置信度，可以显示
                if (box.Score > 0)
                {
                    var formattedText = new FormattedText(
                        $"{box.Score:P0}",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Microsoft YaHei", FontStyle.Normal, FontWeight.Bold),
                        14,
                        BoxBrush);

                    // 在框的上方显示置信度
                    context.DrawText(formattedText, new Point(left, Math.Max(0, top - 20)));
                }
            }
        }
    }

    /// <summary>
    /// 人脸框信息
    /// </summary>
    public class FaceBoxInfo
    {
        /// <summary>
        /// 中心点X坐标（像素坐标）
        /// </summary>
        public double CenterX { get; set; }

        /// <summary>
        /// 中心点Y坐标（像素坐标）
        /// </summary>
        public double CenterY { get; set; }

        /// <summary>
        /// 宽度（像素）
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// 高度（像素）
        /// </summary>
        public double Height { get; set; }

        /// <summary>
        /// 置信度（0-1）
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// 从 WrapperFaceBox 创建（百度SDK返回的是像素坐标）
        /// </summary>
        public static FaceBoxInfo FromWrapperFaceBox(float cx, float cy, float w, float h, float score)
        {
            return new FaceBoxInfo
            {
                CenterX = cx,
                CenterY = cy,
                Width = w,
                Height = h,
                Score = score
            };
        }
    }
}
