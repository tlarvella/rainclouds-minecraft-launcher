using System.Threading.Tasks;

namespace RCL.Core.Modules;

/// <summary>
/// 付费进阶模块契约（联机 / 内网穿透 / 一键开服）。
/// 具体实现位于闭源程序集 RCL.Paid.dll，由启动器在验证 RCM 授权后
/// 通过 <see cref="PaidModuleLoader"/> 动态加载，开源仓库不包含其源码。
/// 本接口刻意不依赖 Avalonia，使 RCL.Core 保持纯净。
/// </summary>
public interface IPaidModule
{
    /// <summary>模块唯一标识：frpc / server / network。</summary>
    string Id { get; }

    /// <summary>显示名称（用于日志与提示）。</summary>
    string DisplayName { get; }

    /// <summary>
    /// 绑定上下文。宿主传入主窗口与主机视图模型（以 object 传递以避免 RCL.Core
    /// 依赖 Avalonia/UI；实现方按自身引用的强类型转型即可）。
    /// </summary>
    void AttachContext(object mainWindow, object mainViewModel);

    /// <summary>打开模块窗口（通常为 ShowDialog）。</summary>
    Task OpenAsync();
}
