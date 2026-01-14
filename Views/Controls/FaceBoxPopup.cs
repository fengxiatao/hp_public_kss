using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;

namespace FaceLocker.Views.Controls
{
    /// <summary>
    /// 人脸框弹出窗口 - 使用独立的透明窗口覆盖在视频上
    /// 解决原生视频窗口无法被 Avalonia 控件覆盖的问题
    /// </summary>
    public class FaceBoxPopup : Window
    {
        private List<FaceBoxInfo>? _faceBoxes;
        private int _sourceWidth = 640;
        private int _sourceHeight = 360;
        private IBrush _boxBrush = Brushes.LimeGreen;
        private double _boxThickness = 3;

        public FaceBoxPopup()
        {
            // 设置窗口属性
            SystemDecorations = SystemDecorations.None;
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            CanResize = false;
            ShowInTaskbar = false;
            Topmost = true;
            
            // 禁用窗口交互
            IsHitTestVisible = false;
        }

        /// <summary>
        /// 更新人脸框数据
        /// </summary>
        public void UpdateFaceBoxes(List<FaceBoxInfo>? boxes, int sourceWidth, int sourceHeight)
        {
            _faceBoxes = boxes;
            _sourceWidth = sourceWidth;
            _sourceHeight = sourceHeight;
            Console.WriteLine($"[FaceBoxPopup] UpdateFaceBoxes: {boxes?.Count ?? 0} boxes, source={sourceWidth}x{sourceHeight}");
            InvalidateVisual();
        }

        /// <summary>
        /// 设置边框颜色
        /// </summary>
        public void SetBoxBrush(IBrush brush)
        {
            _boxBrush = brush;
            InvalidateVisual();
        }

        /// <summary>
        /// 设置边框粗细
        /// </summary>
        public void SetBoxThickness(double thickness)
        {
            _boxThickness = thickness;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            Console.WriteLine($"[FaceBoxPopup] Render called, Bounds={Bounds.Width}x{Bounds.Height}, Boxes={_faceBoxes?.Count ?? 0}");

            var boxes = _faceBoxes;
            if (boxes == null || boxes.Count == 0)
                return;

            var pen = new Pen(_boxBrush, _boxThickness);
            var bounds = Bounds;

            // 计算缩放比例
            double scaleX = bounds.Width / Math.Max(1, _sourceWidth);
            double scaleY = bounds.Height / Math.Max(1, _sourceHeight);

            foreach (var box in boxes)
            {
                // 百度SDK返回的是像素坐标
                double srcLeft = box.CenterX - box.Width / 2;
                double srcTop = box.CenterY - box.Height / 2;

                // 缩放到窗口坐标
                double left = srcLeft * scaleX;
                double top = srcTop * scaleY;
                double rectWidth = box.Width * scaleX;
                double rectHeight = box.Height * scaleY;

                // 边界检查
                left = Math.Max(0, Math.Min(bounds.Width - 10, left));
                top = Math.Max(0, Math.Min(bounds.Height - 10, top));
                rectWidth = Math.Max(10, Math.Min(bounds.Width - left, rectWidth));
                rectHeight = Math.Max(10, Math.Min(bounds.Height - top, rectHeight));

                // 绘制矩形框
                var rect = new Rect(left, top, rectWidth, rectHeight);
                context.DrawRectangle(null, pen, rect, 4, 4);

                // 显示置信度
                if (box.Score > 0)
                {
                    var formattedText = new FormattedText(
                        $"{box.Score:P0}",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Microsoft YaHei", FontStyle.Normal, FontWeight.Bold),
                        14,
                        _boxBrush);

                    context.DrawText(formattedText, new Point(left, Math.Max(0, top - 20)));
                }
            }
        }
    }
}
