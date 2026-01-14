using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FaceLocker.Models;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace FaceLocker.Converters
{
    #region 布尔值到可见性转换器
    /// <summary>
    /// 布尔值到可见性转换器
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public static BoolToVisibilityConverter Instance { get; } = new BoolToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                // 如果parameter为"False"，则反转逻辑
                if (parameter?.ToString()?.Equals("False", StringComparison.OrdinalIgnoreCase) == true)
                {
                    boolValue = !boolValue;
                }

                return boolValue ? true : false; // 返回bool值，在XAML中通过IsVisible绑定
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    #endregion

    #region 小柜子状态到启用状态转换器
    /// <summary>
    /// 小柜子状态到启用状态转换器
    /// </summary>
    public class SmallLockerStatusToEnabledConverter : IValueConverter
    {
        public static SmallLockerStatusToEnabledConverter Default { get; } = new SmallLockerStatusToEnabledConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is LockerStatus status)
            {
                // 只有Available状态的格子可以点击
                return status == LockerStatus.Available;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    #endregion

    #region 布尔值到画刷转换器
    /// <summary>
    /// 布尔值到画刷转换器
    /// </summary>
    public class BoolToBrushConverter : IValueConverter
    {
        public static BoolToBrushConverter Instance { get; } = new BoolToBrushConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isRunning)
            {
                var parameters = parameter?.ToString()?.Split(';');
                if (parameters?.Length == 2)
                {
                    var colorString = isRunning ? parameters[0] : parameters[1];
                    return Brush.Parse(colorString);
                }

                // 如果只有一个参数，true时使用该颜色，false时使用透明
                if (parameters?.Length == 1)
                {
                    return isRunning ? Brush.Parse(parameters[0]) : Brushes.Transparent;
                }

                return isRunning ? Brush.Parse("#C62828") : Brush.Parse("#2E7D32");
            }
            return Brush.Parse("#2E7D32");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
    #endregion

    #region 选项卡激活状态转换器
    /// <summary>
    /// 选项卡激活状态转换器 - 返回CSS类名
    /// </summary>
    public class TabActiveConverter : IMultiValueConverter
    {
        public static TabActiveConverter Instance { get; } = new TabActiveConverter();

        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Count >= 2 && values[0] is string currentTab && values[1] is string targetTab)
            {
                return currentTab == targetTab ? "tab-button active" : "tab-button";
            }
            return "tab-button";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
    #endregion

    #region 布尔值到颜色转换器（用于选项卡选中状态）
    /// <summary>
    /// 布尔值到颜色转换器 - 专门用于选项卡选中状态
    /// </summary>
    public class BoolToTabColorConverter : IValueConverter
    {
        public static BoolToTabColorConverter Instance { get; } = new BoolToTabColorConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSelected)
            {
                return isSelected ? Brush.Parse("#3498db") : Brushes.Transparent;
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    #endregion

    #region 布尔值到颜色转换器（绿色/红色）
    /// <summary>
    /// 布尔值到颜色转换器 - 绿色表示真，红色表示假
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        public static readonly BoolToColorConverter Default = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string param)
            {
                var colors = param.Split(':');
                if (colors.Length >= 2)
                {
                    var colorString = boolValue ? colors[1] : colors[0];
                    return Color.Parse(colorString);
                }
            }

            return Colors.Gray;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
    #endregion

    #region 布尔值到字符串转换器
    /// <summary>
    /// 布尔值到字符串转换器
    /// 参数格式："FalseText:TrueText"
    /// </summary>
    public class BoolToStringConverter : IValueConverter
    {
        public static readonly BoolToStringConverter Default = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string param)
            {
                var texts = param.Split(':');
                if (texts.Length >= 2)
                {
                    return boolValue ? texts[1] : texts[0];
                }
            }

            return string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
    #endregion

    #region 布尔值到登记状态文本转换器
    /// <summary>
    /// 布尔值到登记状态文本转换器 - 用于显示人脸特征状态
    /// </summary>
    public class BoolToRegisteredTextConverter : IValueConverter
    {
        public static BoolToRegisteredTextConverter Instance { get; } = new BoolToRegisteredTextConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? "已登记" : "未登记";
            }
            return "未登记";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    #endregion

    #region 对象非空转换器
    /// <summary>
    /// 对象非空转换器 - 检查对象是否为null
    /// </summary>
    public class ObjectToBoolConverter : IValueConverter
    {
        public static readonly ObjectToBoolConverter IsNotNull = new ObjectToBoolConverter();
        public static readonly ObjectToBoolConverter IsNull = new ObjectToBoolConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool result = value != null;

            // 如果参数为"IsNull"，则反转逻辑
            if (parameter?.ToString() == "IsNull")
            {
                result = !result;
            }

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
    #endregion

    #region 图像安全转换器
    /// <summary>
    /// 图像安全转换器 - 检查WriteableBitmap是否有效
    /// </summary>
    public class ImageSafeConverter : IValueConverter
    {
        public static readonly ImageSafeConverter Instance = new ImageSafeConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is WriteableBitmap bitmap)
            {
                try
                {
                    // 检查bitmap是否有效
                    if (bitmap.Size.Width <= 0 || bitmap.Size.Height <= 0)
                    {
                        return null;
                    }
                    return bitmap;
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
    #endregion

    #region 字符串非空检查转换器
    /// <summary>
    /// 字符串非空检查转换器
    /// </summary>
    public class StringIsNotNullOrEmptyConverter : IValueConverter
    {
        public static readonly StringIsNotNullOrEmptyConverter Default = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return !string.IsNullOrEmpty(value as string);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
    #endregion

    #region SKBitmap 到 WriteableBitmap 转换器
    /// <summary>
    /// SKBitmap 到 WriteableBitmap 转换器
    /// </summary>
    public class SKBitmapToWriteableBitmapConverter : IValueConverter
    {
        public static readonly SKBitmapToWriteableBitmapConverter Instance = new SKBitmapToWriteableBitmapConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SKBitmap skBitmap && skBitmap.Width > 0 && skBitmap.Height > 0)
            {
                try
                {
                    // 使用 SKPixmap 来读取像素数据
                    using (var pixmap = skBitmap.PeekPixels())
                    {
                        var imageInfo = pixmap.Info;
                        var writeableBitmap = new WriteableBitmap(
                            new Avalonia.PixelSize(imageInfo.Width, imageInfo.Height),
                            new Avalonia.Vector(96, 96),
                            Avalonia.Platform.PixelFormat.Bgra8888,
                            Avalonia.Platform.AlphaFormat.Premul);

                        using (var lockedBitmap = writeableBitmap.Lock())
                        {
                            // 正确的 ReadPixels 调用方式
                            var success = pixmap.ReadPixels(
                                imageInfo,  // 目标图像信息
                                lockedBitmap.Address,
                                lockedBitmap.RowBytes);

                            if (!success)
                            {
                                return null;
                            }
                        }

                        return writeableBitmap;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SKBitmap转换失败: {ex.Message}");
                    return null;
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
    #endregion

    #region 字节数组到 Bitmap 转换器
    /// <summary>
    /// 字节数组到 Bitmap 转换器
    /// </summary>
    public class ByteArrayToBitmapConverter : IValueConverter
    {
        public static readonly ByteArrayToBitmapConverter Instance = new ByteArrayToBitmapConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is byte[] byteArray && byteArray.Length > 0)
            {
                try
                {
                    using (var stream = new System.IO.MemoryStream(byteArray))
                    {
                        return new Bitmap(stream);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"字节数组转换Bitmap失败: {ex.Message}");
                    return null;
                }
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
    #endregion

    public class StringToBoolConverter : IValueConverter
    {
        public static readonly StringToBoolConverter Default = new StringToBoolConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                bool result = !string.IsNullOrEmpty(str);

                // 如果参数是"inverse"，则反转结果
                if (parameter is string param && param == "inverse")
                {
                    return !result;
                }

                return result;
            }

            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 布尔值到可见性转换器
    /// </summary>
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public static BooleanToVisibilityConverter Instance { get; } = new BooleanToVisibilityConverter();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                // 如果没有参数，true 表示可见，false 表示隐藏
                if (parameter == null)
                {
                    return boolValue ? true : false;
                }

                // 如果有参数 "inverse"，则反转逻辑
                if (parameter is string param && param.ToLower() == "inverse")
                {
                    return !boolValue ? true : false;
                }
            }

            return false; // 默认隐藏
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// 多值转换器
    /// </summary>
    public class AndToVisibilityConverter : IMultiValueConverter
    {
        public static AndToVisibilityConverter Instance { get; } = new AndToVisibilityConverter();

        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count >= 2 &&
                values[0] is bool isLoading &&
                values[1] is bool hasData)
            {
                return !isLoading && !hasData;
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}