using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace FaceLocker.Services
{
    /// <summary>
    /// 人脸框渲染器 - 在图像上绘制人脸检测框
    /// </summary>
    public class FaceBoxRenderer
    {
        #region 处理WrapperFaceBox数组的人脸框绘制
        /// <summary>
        /// 处理WrapperFaceBox数组的人脸框绘制
        /// </summary>
        public static WriteableBitmap RenderFaceBoxes(WriteableBitmap originalBitmap, BaiduFaceSDKInterop.WrapperFaceBox[] faceBoxes, int imageWidth, int imageHeight)
        {
            if (originalBitmap == null || faceBoxes == null || faceBoxes.Length == 0)
            {
                return originalBitmap;
            }

            try
            {
                // 创建矩形列表
                var rectangles = new List<System.Drawing.Rectangle>(faceBoxes.Length);

                foreach (var faceBox in faceBoxes)
                {
                    // 计算左上角坐标
                    int x = (int)(faceBox.center_x - faceBox.width / 2);
                    int y = (int)(faceBox.center_y - faceBox.height / 2);
                    int width = (int)faceBox.width;
                    int height = (int)faceBox.height;

                    // 确保坐标在图像范围内
                    x = Math.Max(0, Math.Min(x, imageWidth - 1));
                    y = Math.Max(0, Math.Min(y, imageHeight - 1));
                    width = Math.Max(1, Math.Min(width, imageWidth - x));
                    height = Math.Max(1, Math.Min(height, imageHeight - y));

                    if (width > 0 && height > 0)
                    {
                        rectangles.Add(new System.Drawing.Rectangle(x, y, width, height));
                    }
                }

                if (rectangles.Count == 0)
                {
                    return originalBitmap;
                }

                return RenderFaceBoxes(originalBitmap, rectangles);
            }
            catch
            {
                return originalBitmap;
            }
        }
        #endregion

        #region 在WriteableBitmap上绘制人脸框
        /// <summary>
        /// 在WriteableBitmap上绘制人脸框 - 修复版本，确保正确绘制
        /// </summary>
        public static WriteableBitmap RenderFaceBoxes(WriteableBitmap originalBitmap, IEnumerable<System.Drawing.Rectangle> faceRectangles)
        {
            if (originalBitmap == null || faceRectangles == null)
                return originalBitmap;

            try
            {
                var pixelSize = originalBitmap.PixelSize;

                // 直接在原始 bitmap 上绘制，避免创建新 bitmap
                using (var bitmapLock = originalBitmap.Lock())
                {
                    var imageInfo = new SKImageInfo(pixelSize.Width, pixelSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

                    using (var skBitmap = new SKBitmap(imageInfo))
                    {
                        // 安装像素数据
                        var success = skBitmap.InstallPixels(imageInfo, bitmapLock.Address, bitmapLock.RowBytes);
                        if (!success)
                        {
                            return originalBitmap;
                        }

                        // 创建画布并绘制人脸框
                        using (var canvas = new SKCanvas(skBitmap))
                        {
                            // 绘制人脸框 - 使用亮绿色，线宽3像素
                            using var paint = new SKPaint
                            {
                                Style = SKPaintStyle.Stroke,
                                Color = SKColors.LimeGreen,
                                StrokeWidth = 3,
                                IsAntialias = false  // 关闭抗锯齿提高性能
                            };

                            foreach (var rect in faceRectangles)
                            {
                                if (rect.X >= 0 && rect.Y >= 0 &&
                                    rect.X + rect.Width <= pixelSize.Width &&
                                    rect.Y + rect.Height <= pixelSize.Height &&
                                    rect.Width > 0 && rect.Height > 0)
                                {
                                    var skRect = new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
                                    canvas.DrawRect(skRect, paint);
                                }
                            }
                        }

                        skBitmap.NotifyPixelsChanged();
                    }
                }

                return originalBitmap;
            }
            catch (Exception ex)
            {
                return originalBitmap;
            }
        }
        #endregion
    }
}