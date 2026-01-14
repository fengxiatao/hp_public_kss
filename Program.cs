using Avalonia;
using Avalonia.ReactiveUI;
using System.Runtime.InteropServices;
using System.Threading;

namespace FaceLocker
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var culture = new System.Globalization.CultureInfo("en-US");
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;

            ThreadPool.SetMinThreads(50, 50);

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            var builder = AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .UseReactiveUI()
                .LogToTrace();

            // 仅在 Linux/X11 下强制软件渲染，完全绕开 EGL/GL/GLX
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                builder = builder.With(new X11PlatformOptions
                {
                    // 只给 Software，一刀切禁用 GLX/EGL 路径
                    RenderingMode = new[] { X11RenderingMode.Software },
                    OverlayPopups = false
                });
            }

            return builder;
        }
    }
}