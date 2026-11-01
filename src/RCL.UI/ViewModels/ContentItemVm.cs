using System;
using RCL.Core.Content;

namespace RCL.UI.ViewModels;

/// <summary>模组/插件列表中的一项。勾选即启用，取消勾选即禁用（改扩展名，不删文件）。</summary>
public class ContentItemVm : ViewModelBase
{
    private readonly Action<ContentItemVm, bool> _onToggle;
    private bool _enabled;

    public ContentItemVm(ContentItem item, Action<ContentItemVm, bool> onToggle)
    {
        Item = item;
        _enabled = item.Enabled;
        _onToggle = onToggle;
    }

    public ContentItem Item { get; private set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Item.DisplayName)
        ? Item.FileName
        : Item.DisplayName;

    public string SubText => Item.SubText;

    public string Kind => Item.Kind;

    public string Loader => string.IsNullOrWhiteSpace(Item.Loader) ? Item.Kind : Item.Loader;

    public string SizeText => Item.SizeText;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            OnPropertyChanged();
            _onToggle(this, value);
        }
    }

    internal void ReplaceItem(ContentItem item)
    {
        Item = item;
        _enabled = item.Enabled;
        OnPropertyChanged(nameof(Enabled));
    }
}
