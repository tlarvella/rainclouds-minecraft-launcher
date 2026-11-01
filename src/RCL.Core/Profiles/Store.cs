using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RCL.Core.Models;
using RCL.Core.Utils;

namespace RCL.Core.Profiles;

/// <summary>全局设置 / 账号 / 实例的本地持久化（JSON 文件）。</summary>
public static class Store
{
    public static Settings LoadSettings()
    {
        if (File.Exists(Paths.SettingsFile))
        {
            try { return Json.Deserialize<Settings>(File.ReadAllText(Paths.SettingsFile)) ?? new Settings(); }
            catch { }
        }
        return new Settings();
    }

    public static void SaveSettings(Settings s) => File.WriteAllText(Paths.SettingsFile, Json.Serialize(s));

    public static List<Account> LoadAccounts()
    {
        if (File.Exists(Paths.AccountsFile))
        {
            try { return Json.Deserialize<List<Account>>(File.ReadAllText(Paths.AccountsFile)) ?? new(); }
            catch { }
        }
        return new List<Account>();
    }

    public static void SaveAccounts(List<Account> accounts) => File.WriteAllText(Paths.AccountsFile, Json.Serialize(accounts));

    public static List<Profile> LoadProfiles()
    {
        var list = new List<Profile>();
        if (File.Exists(Paths.ProfilesFile))
        {
            try { list = Json.Deserialize<List<Profile>>(File.ReadAllText(Paths.ProfilesFile)) ?? new(); }
            catch { }
        }

        // 旧版档案（共享根目录）迁移：保留原有存档位置，避免玩家找不到世界
        foreach (var p in list.Where(p => p.Schema < 2))
        {
            p.GameDirectory ??= Paths.Root;
            p.Schema = 2;
        }

        if (list.Count == 0)
            list.Add(Content.RaincloudsPack.CreateProfile());

        return list;
    }

    public static void SaveProfiles(List<Profile> profiles) => File.WriteAllText(Paths.ProfilesFile, Json.Serialize(profiles));
}
