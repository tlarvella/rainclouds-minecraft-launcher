using Avalonia;
using RCL.Core.Utils;
using System;
using System.IO;
using System.Text;

namespace RCL.UI;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // GUI 子系统下异常不会弹窗，统一写入日志，便于排查"点了没反应"这类问题
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog(e.ExceptionObject as Exception, "AppDomain.UnhandledException");

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex, "Main");
            throw;
        }
    }

    private static void WriteCrashLog(Exception? ex, string source)
    {
        try
        {
            var dir = Paths.Root;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crash.log");

            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 来源: {source}");
            sb.AppendLine($"运行时: {Environment.Version} / {Environment.OSVersion}");
            sb.AppendLine(ex?.ToString() ?? "未知异常（异常对象为空）");
            sb.AppendLine(new string('-', 70));
            File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // 日志写入失败时不得掩盖原始异常
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
