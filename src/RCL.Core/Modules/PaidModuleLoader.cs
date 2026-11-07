using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

namespace RCL.Core.Modules;

/// <summary>
/// 闭源付费模块加载器。
/// 从应用程序目录加载 RCL.Paid.dll（若存在），发现其中实现 <see cref="IPaidModule"/>
/// 的类型并缓存；DLL 缺失时所有查询优雅返回 null，使社区版（不含付费逻辑）仍可运行。
///
/// 关键：使用 <see cref="AssemblyLoadContext.Default"/> 加载，以复用宿主已加载的
/// 同一个 Avalonia / RCL.Core / RCL.UI 实例，保证类型同一性（自建 ALC 会导致
/// 第二份 Avalonia 实例、基类 Type 不一致而失败）。
/// </summary>
public static class PaidModuleLoader
{
    private static readonly object Lock = new();
    private static readonly Dictionary<string, IPaidModule> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static bool _scanned;

    private static string DllPath => Path.Combine(AppContext.BaseDirectory, "RCL.Paid.dll");

    /// <summary>闭源模块程序集是否存在于应用目录。</summary>
    public static bool Available => File.Exists(DllPath);

    /// <summary>
    /// 按标识取模块；未加载、DLL 缺失或加载失败时返回 null。
    /// </summary>
    public static IPaidModule? Get(string id)
    {
        EnsureScanned();
        lock (Lock)
            return Cache.TryGetValue(id, out var m) ? m : null;
    }

    /// <summary>是否已注册某标识的模块。</summary>
    public static bool Contains(string id) => Get(id) != null;

    private static void EnsureScanned()
    {
        lock (Lock)
        {
            if (_scanned) return;
            _scanned = true;

            if (!File.Exists(DllPath))
                return; // 社区版：无闭源组件，优雅降级

            try
            {
                var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(DllPath);
                foreach (var t in asm.GetTypes())
                {
                    if (t.IsAbstract || t.IsInterface) continue;
                    if (!typeof(IPaidModule).IsAssignableFrom(t)) continue;
                    if (Activator.CreateInstance(t) is IPaidModule m && !string.IsNullOrEmpty(m.Id))
                        Cache[m.Id] = m;
                }
            }
            catch (Exception ex)
            {
                // 加载失败不应拖垮启动器；记录到日志目录便于排查。
                try
                {
                    var logDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "RaincloudsMC", "logs");
                    Directory.CreateDirectory(logDir);
                    File.AppendAllText(Path.Combine(logDir, "paid-loader.log"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 加载 RCL.Paid.dll 失败: {ex}{Environment.NewLine}");
                }
                catch { }
            }
        }
    }
}
