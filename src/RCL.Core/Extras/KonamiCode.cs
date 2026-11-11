using System;

namespace RCL.Core.Extras;

/// <summary>
/// Konami 码状态机（↑↑↓↓←→←→ B A）。纯逻辑、零 UI 依赖，便于单测。
/// UI 层把按键名字符串依次喂给 Feed，命中一次后自动复位，可重复触发。
/// </summary>
public sealed class KonamiCode
{
    // 使用 Avalonia Key.ToString() 的名称；同时兼容常见别名
    private static readonly string[] Sequence =
    {
        "Up", "Up", "Down", "Down", "Left", "Right", "Left", "Right", "B", "A"
    };

    private int _index;

    /// <summary>当前已连续匹配的步数（用于 UI 提示，可不用）。</summary>
    public int Progress => _index;

    /// <summary>喂入一个按键名，返回是否完整命中。</summary>
    public bool Feed(string key)
    {
        if (string.IsNullOrEmpty(key)) { Reset(); return false; }

        // 允许 "B" / "A" 大小写与 "KeyB" 之类变体
        var k = Normalize(key);
        if (k == Normalize(Sequence[_index]))
        {
            _index++;
            if (_index == Sequence.Length)
            {
                Reset();
                return true;
            }
        }
        else
        {
            // 失配时，若当前键恰好是序列首键，则从 1 开始重新计数
            _index = k == Normalize(Sequence[0]) ? 1 : 0;
        }
        return false;
    }

    public void Reset() => _index = 0;

    private static string Normalize(string key)
    {
        // 统一成大写字母/单词，便于比较
        var s = key.Trim();
        if (s.StartsWith("Key", StringComparison.OrdinalIgnoreCase) && s.Length == 4)
            s = s.Substring(3);
        if (s.Equals("Space", StringComparison.OrdinalIgnoreCase)) s = "Space";
        if (s.Length == 1) s = s.ToUpperInvariant();
        return s;
    }
}
