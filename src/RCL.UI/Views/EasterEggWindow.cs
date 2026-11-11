using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using RCL.Core.Extras;

namespace RCL.UI.Views;

/// <summary>
/// 「Rainclouds 离线资料馆 · 彩蛋」窗口。
///
/// 全部内容来自随 exe 嵌入的离线资源（见 RCL.Core.Extras.EasterEggAssets），
/// 断网可用。用纯 C# 构造界面，不依赖 XAML 编译，便于在单文件发布下稳定运行。
/// 四个分页：壁纸画廊 / 音效盒 / 离线资料 / 关于与隐藏小游戏。
/// </summary>
public class EasterEggWindow : Window
{
    private const uint SND_FILENAME = 0x00020000;
    private const uint SND_ASYNC = 0x0001;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

    private readonly TextBlock _status;
    private readonly TextBlock _quoteBlock;
    private readonly TextBlock _scoreBlock;
    private readonly StackPanel _mineHost;

    private bool[] _mines = Array.Empty<bool>();
    private bool[] _clicked = Array.Empty<bool>();
    private int _score;

    private static readonly SolidColorBrush White = new(Color.Parse("#f2f5fa"));
    private static readonly SolidColorBrush Muted = new(Color.Parse("#98a3b5"));
    private static readonly SolidColorBrush Accent = new(Color.Parse("#7ea6ff"));
    private static readonly SolidColorBrush Panel = new(Color.Parse("#1b1f28"));
    private static readonly SolidColorBrush Deep = new(Color.Parse("#101319"));

    public EasterEggWindow()
    {
        Title = "Rainclouds 离线资料馆 · 彩蛋";
        Width = 960;
        Height = 660;
        MinWidth = 780;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#14161c"));

        _status = new TextBlock
        {
            Text = "提示：顶栏云朵连点 7 次，或按 ↑↑↓↓←→←→ B A 可再次打开这里。",
            FontSize = 11,
            Foreground = Muted
        };
        _quoteBlock = new TextBlock
        {
            Text = EasterEggAssets.RandomQuote(),
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Foreground = Accent,
            TextWrapping = TextWrapping.Wrap
        };
        _scoreBlock = new TextBlock { Text = "得分 0", FontSize = 13, Foreground = White };
        _mineHost = new StackPanel { Spacing = 4 };

        Content = BuildRoot();
        ResetMineGame();
    }

    // ---------------------------------------------------------------- 骨架

    private Control BuildRoot()
    {
        var root = new DockPanel();

        var header = new Border
        {
            Padding = new Thickness(22, 16, 22, 12),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = "✦ Rainclouds 离线资料馆", FontSize = 21,
                        FontWeight = FontWeight.Bold, Foreground = White
                    },
                    new TextBlock
                    {
                        Text = $"全部资源随 rcl.exe 内嵌发布 · 断网可用 · 资源包 v{EasterEggAssets.Version}"
                               + $" · 共 {EasterEggAssets.Items.Count} 项 / "
                               + $"{EasterEggAssets.TotalEmbeddedBytes / 1024.0 / 1024:F1} MB",
                        FontSize = 11, Foreground = Muted
                    }
                }
            }
        };
        DockPanel.SetDock(header, Dock.Top);

        var footer = new Border
        {
            Padding = new Thickness(22, 10),
            Background = Deep,
            Child = _status
        };
        DockPanel.SetDock(footer, Dock.Bottom);

        var tabs = new TabControl { Margin = new Thickness(8, 4, 8, 8) };
        tabs.Items.Add(new TabItem { Header = "壁纸画廊", Content = BuildWallpapers() });
        tabs.Items.Add(new TabItem { Header = "音效盒", Content = BuildAudio() });
        tabs.Items.Add(new TabItem { Header = "离线资料", Content = BuildData() });
        tabs.Items.Add(new TabItem { Header = "关于 · 小游戏", Content = BuildAbout() });

        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(tabs);
        return root;
    }

    private static Border SideBar(Control child)
        => new()
        {
            Width = 256,
            Background = Deep,
            Child = child
        };

    private static Button NewButton(string text)
        => new()
        {
            Content = text,
            Padding = new Thickness(12, 7),
            Background = Panel,
            Foreground = White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(7)
        };

    private Button MakeButton(string text, Action onClick)
    {
        var b = NewButton(text);
        b.Click += (_, _) =>
        {
            try { onClick(); }
            catch (Exception ex) { SetStatus("操作失败：" + ex.Message); }
        };
        return b;
    }

    private Button MakeAsyncButton(string text, Func<Task> onClick)
    {
        var b = NewButton(text);
        b.Click += async (_, _) =>
        {
            try { await onClick(); }
            catch (Exception ex) { SetStatus("操作失败：" + ex.Message); }
        };
        return b;
    }

    private static StackPanel ButtonRow(params Control[] buttons)
    {
        var p = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(16, 10)
        };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }

    // ---------------------------------------------------------------- 壁纸

    private Control BuildWallpapers()
    {
        var items = EasterEggAssets.Of(EasterEggAssets.Wallpapers).ToList();
        var list = new ListBox { Margin = new Thickness(10), Background = Deep, Foreground = White };
        list.ItemsSource = items.Select(i => i.Title).ToList();

        var preview = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(16) };
        var desc = new TextBlock
        {
            FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 12, 16, 0)
        };
        var title = new TextBlock
        {
            FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = White,
            Margin = new Thickness(16, 12, 16, 0)
        };

        ExtraItem? current = null;

        void Show(int idx)
        {
            if (idx < 0 || idx >= items.Count) return;
            current = items[idx];
            title.Text = current.Title;
            desc.Text = current.Desc + $"\n（1600×900 PNG，{EasterEggAssets.Read(current.File)?.Length / 1024.0 / 1024:F2} MB 内嵌）";
            var data = EasterEggAssets.Read(current.File);
            if (data == null) return;
            using var ms = new MemoryStream(data);
            preview.Source = new Bitmap(ms);
        }

        list.SelectionChanged += (_, _) => Show(list.SelectedIndex);

        var buttons = ButtonRow(
            MakeButton("设为启动页背景", () =>
            {
                if (current == null) return;
                try
                {
                    EasterEggAssets.SetBackground(current.File);
                    SetStatus($"已把「{current.Title}」设为启动页背景，下次启动生效。");
                }
                catch (Exception ex) { SetStatus("设置失败：" + ex.Message); }
            }),
            MakeAsyncButton("导出这张", async () =>
            {
                if (current == null) return;
                var dir = await PickDirAsync();
                var p = EasterEggAssets.Export(current.File, dir);
                SetStatus("已导出：" + p);
                OpenFolder(dir);
            }),
            MakeAsyncButton("导出全部资源", async () =>
            {
                var dir = await PickDirAsync();
                var (n, b) = EasterEggAssets.ExportAll(dir);
                SetStatus($"已导出 {n} 个文件（{b / 1024.0 / 1024:F1} MB）到 {dir}");
                OpenFolder(dir);
            }),
            MakeButton("恢复默认背景", () =>
            {
                EasterEggAssets.ResetBackground();
                SetStatus("已恢复默认启动页。");
            }));

        var bottom = new Border { Child = buttons };
        DockPanel.SetDock(bottom, Dock.Bottom);
        var top = new StackPanel { Children = { title, desc } };
        DockPanel.SetDock(top, Dock.Top);

        var right = new DockPanel { Background = Panel };
        right.Children.Add(bottom);
        right.Children.Add(top);
        right.Children.Add(new Border { Child = preview });

        var split = new DockPanel();
        var side = SideBar(list);
        DockPanel.SetDock(side, Dock.Left);
        split.Children.Add(side);
        split.Children.Add(right);

        list.SelectedIndex = 0;
        return split;
    }

    // ---------------------------------------------------------------- 音效

    private Control BuildAudio()
    {
        var items = EasterEggAssets.Of(EasterEggAssets.Audio).ToList();
        var list = new ListBox { Margin = new Thickness(10), Background = Deep, Foreground = White };
        list.ItemsSource = items.Select(i => i.Title).ToList();

        var title = new TextBlock
        {
            FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = White,
            Margin = new Thickness(20, 24, 20, 0)
        };
        var desc = new TextBlock
        {
            FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20, 8, 20, 0)
        };
        var meta = new TextBlock
        {
            FontSize = 11, Foreground = Accent, Margin = new Thickness(20, 8, 20, 0)
        };
        var icon = new TextBlock
        {
            Text = "♫", FontSize = 74, Foreground = Accent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        ExtraItem? current = null;
        void Show(int idx)
        {
            if (idx < 0 || idx >= items.Count) return;
            current = items[idx];
            title.Text = current.Title;
            desc.Text = current.Desc;
            var len = EasterEggAssets.Read(current.File)?.Length ?? 0;
            meta.Text = $"44.1 kHz · 16 bit · 立体声 WAV · {len / 1024.0 / 1024:F2} MB 内嵌";
        }
        list.SelectionChanged += (_, _) => Show(list.SelectedIndex);

        var buttons = ButtonRow(
            MakeButton("▶ 试听", () =>
            {
                if (current == null) return;
                if (!OperatingSystem.IsWindows())
                {
                    SetStatus("当前平台不支持直接试听，可点「导出」后用系统播放器打开 WAV。");
                    return;
                }
                try
                {
                    var tmp = EasterEggAssets.ToTempFile(current.File);
                    PlaySound(tmp, IntPtr.Zero, SND_FILENAME | SND_ASYNC);
                    SetStatus("正在播放：「" + current.Title + "」");
                }
                catch (Exception ex) { SetStatus("播放失败：" + ex.Message); }
            }),
            MakeAsyncButton("导出这个", async () =>
            {
                if (current == null) return;
                var dir = await PickDirAsync();
                var p = EasterEggAssets.Export(current.File, dir);
                SetStatus("已导出：" + p);
                OpenFolder(dir);
            }),
            MakeAsyncButton("导出全部音效", async () =>
            {
                var dir = await PickDirAsync();
                foreach (var it in items) EasterEggAssets.Export(it.File, dir);
                SetStatus($"已导出 {items.Count} 个音效到 {dir}");
                OpenFolder(dir);
            }));

        var bottom = new Border { Child = buttons };
        DockPanel.SetDock(bottom, Dock.Bottom);
        var top = new StackPanel { Children = { title, desc, meta } };
        DockPanel.SetDock(top, Dock.Top);

        var right = new DockPanel { Background = Panel };
        right.Children.Add(bottom);
        right.Children.Add(top);
        right.Children.Add(new Border { Child = icon });

        var split = new DockPanel();
        var side = SideBar(list);
        DockPanel.SetDock(side, Dock.Left);
        split.Children.Add(side);
        split.Children.Add(right);

        list.SelectedIndex = 0;
        return split;
    }

    // ---------------------------------------------------------------- 离线资料

    private Control BuildData()
    {
        var items = EasterEggAssets.Of(EasterEggAssets.Data).ToList();
        var list = new ListBox { Margin = new Thickness(10), Background = Deep, Foreground = White };
        list.ItemsSource = items.Select(i => i.Title).ToList();

        var box = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Microsoft YaHei, SimSun, monospace"),
            FontSize = 12,
            Background = Panel,
            Foreground = White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16),
            Margin = new Thickness(8)
        };
        var title = new TextBlock
        {
            FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = White,
            Margin = new Thickness(16, 12, 16, 0)
        };

        list.SelectionChanged += (_, _) =>
        {
            var idx = list.SelectedIndex;
            if (idx < 0 || idx >= items.Count) return;
            var it = items[idx];
            title.Text = it.Title;
            box.Text = FormatData(it.File, EasterEggAssets.ReadText(it.File));
        };

        var top = new Border { Child = title };
        DockPanel.SetDock(top, Dock.Top);
        var right = new DockPanel { Background = Panel };
        right.Children.Add(top);
        right.Children.Add(new Border { Child = box });

        var split = new DockPanel();
        var side = SideBar(list);
        DockPanel.SetDock(side, Dock.Left);
        split.Children.Add(side);
        split.Children.Add(right);

        list.SelectedIndex = 0;
        return split;
    }

    private static string FormatData(string file, string raw)
    {
        try
        {
            if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return raw;
            using var doc = JsonDocument.Parse(raw);
            var r = doc.RootElement;
            var sb = new StringBuilder();

            if (file == "crafting.json")
            {
                sb.AppendLine(r.GetProperty("说明").GetString()).AppendLine();
                foreach (var e in r.GetProperty("recipes").EnumerateArray())
                {
                    sb.AppendLine("【" + e.GetProperty("name").GetString() + "】  →  " +
                                  e.GetProperty("result").GetString());
                    var mats = new List<string>();
                    foreach (var m in e.GetProperty("materials").EnumerateObject())
                        mats.Add($"{m.Name} ×{m.Value}");
                    sb.AppendLine("  材料：" + string.Join("，", mats));
                    foreach (var line in e.GetProperty("pattern").EnumerateArray())
                        sb.AppendLine("      " + line.GetString());
                    sb.AppendLine();
                }
                foreach (var t in r.GetProperty("小贴士").EnumerateArray())
                    sb.AppendLine("· " + t.GetString());
                return sb.ToString();
            }

            if (file == "blocks.json")
            {
                sb.AppendLine(r.GetProperty("说明").GetString()).AppendLine();
                sb.AppendLine(string.Format("{0,-14}{1,-20}{2,-8}{3,-8}{4,-6}{5}",
                    "名称", "ID", "硬度", "工具", "亮度", "掉落"));
                sb.AppendLine(new string('─', 82));
                foreach (var e in r.GetProperty("blocks").EnumerateArray())
                {
                    sb.AppendLine(string.Format("{0,-14}{1,-20}{2,-8}{3,-8}{4,-6}{5}",
                        e.GetProperty("name").GetString(),
                        e.GetProperty("id").GetString(),
                        e.GetProperty("hardness").GetRawText(),
                        e.GetProperty("tool").GetString(),
                        e.GetProperty("luminance").GetRawText(),
                        e.GetProperty("drop").GetString()));
                    if (e.TryGetProperty("note", out var note))
                        sb.AppendLine("      ↳ " + note.GetString());
                }
                return sb.ToString();
            }

            if (file == "version_history.json")
            {
                sb.AppendLine(r.GetProperty("项目").GetString()).AppendLine();
                foreach (var v in r.GetProperty("versions").EnumerateArray())
                {
                    sb.AppendLine($"{v.GetProperty("tag").GetString()}  ({v.GetProperty("date").GetString()})  " +
                                  v.GetProperty("title").GetString());
                    foreach (var c in v.GetProperty("changes").EnumerateArray())
                        sb.AppendLine("   · " + c.GetString());
                    sb.AppendLine();
                }
                return sb.ToString();
            }
        }
        catch
        {
            // 资料格式异常时退回原文，不影响使用
        }
        return raw;
    }

    // ---------------------------------------------------------------- 关于 / 小游戏

    private Control BuildAbout()
    {
        var scroll = new ScrollViewer { Background = Panel };
        var panel = new StackPanel { Margin = new Thickness(22), Spacing = 14 };

        panel.Children.Add(new TextBlock
        {
            Text = "启动名言", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = White
        });
        panel.Children.Add(_quoteBlock);
        panel.Children.Add(MakeButton("换一条", () => _quoteBlock.Text = EasterEggAssets.RandomQuote()));

        panel.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#2b3040")) });

        panel.Children.Add(new TextBlock
        {
            Text = "隐藏小游戏 · 挖矿计分", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = White
        });
        panel.Children.Add(new TextBlock
        {
            Text = "4×4 石层里藏着 6 颗钻石，点开石块找出来，每颗 +100 分。",
            FontSize = 12, Foreground = Muted
        });
        var gameBar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            Children = { _scoreBlock, MakeButton("重新开始", ResetMineGame) }
        };
        panel.Children.Add(gameBar);
        panel.Children.Add(_mineHost);

        panel.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#2b3040")) });
        panel.Children.Add(new TextBlock
        {
            Text = "鸣谢与许可", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = White
        });
        panel.Children.Add(new TextBox
        {
            Text = EasterEggAssets.ReadText("credits.txt"),
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Height = 210, FontSize = 11,
            Background = Deep, Foreground = Muted,
            BorderThickness = new Thickness(0), Padding = new Thickness(12)
        });

        scroll.Content = panel;
        return scroll;
    }

    private void ResetMineGame()
    {
        const int rows = 4, cols = 4, mineCount = 6;
        _mines = new bool[rows * cols];
        _clicked = new bool[rows * cols];
        _score = 0;
        var rng = new Random();
        var positions = Enumerable.Range(0, rows * cols).OrderBy(_ => rng.Next()).Take(mineCount);
        foreach (var p in positions) _mines[p] = true;
        _scoreBlock.Text = "得分 0  ·  剩余 6 颗";

        _mineHost.Children.Clear();
        int idx = 0;
        for (int r = 0; r < rows; r++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            for (int c = 0; c < cols; c++)
            {
                int mine = idx++;
                var b = new Button
                {
                    Width = 42, Height = 42, Content = "■", FontSize = 16,
                    Background = new SolidColorBrush(Color.Parse("#2b3040")),
                    Foreground = Muted,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(6)
                };
                b.Click += (_, _) => OnMineClicked(b, mine);
                row.Children.Add(b);
            }
            _mineHost.Children.Add(row);
        }
    }

    private void OnMineClicked(Button b, int idx)
    {
        if (_clicked[idx]) return;
        _clicked[idx] = true;
        if (_mines[idx])
        {
            b.Content = "◆";
            b.Foreground = new SolidColorBrush(Color.Parse("#5fe0e8"));
            _score += 100;
        }
        else
        {
            b.Content = "·";
            b.Foreground = new SolidColorBrush(Color.Parse("#5b6472"));
        }
        int left = 0;
        for (int i = 0; i < _mines.Length; i++)
            if (_mines[i] && !_clicked[i]) left++;
        _scoreBlock.Text = $"得分 {_score}  ·  剩余 {left} 颗";
        if (left == 0)
        {
            _scoreBlock.Text = $"通关！得分 {_score} —— 全部钻石已找到。";
            SetStatus("彩蛋：挖矿小游戏通关，得分 " + _score);
        }
    }

    // ---------------------------------------------------------------- 辅助

    private void SetStatus(string text) => _status.Text = text;

    private async Task<string> PickDirAsync()
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择导出目录",
                AllowMultiple = false
            });
            var p = folders.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(p)) return p;
        }
        catch
        {
            // 文件选择器不可用时退回默认导出目录
        }
        return Path.Combine(EasterEggAssets.ExtrasDir, "export");
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { /* 打开失败不影响导出结果 */ }
    }
}

