Rainclouds Minecraft Launcher 社区版（rcl.exe）v1.2

系统要求
- Windows 10 或更高版本（64 位）
- 无需安装 .NET 运行时，程序已自包含

快速开始
1. 双击 rcl.exe 启动，会先显示启动页面（Splash）。
2. 在「账号」里添加微软账号（设备码登录）或离线用户名。
3. 左侧点「+ 核心」一键生成内置 Rainclouds 10yv 核心，开箱即玩；
   或者点「+ 新建」自建档案，选择游戏版本与 Mod 加载器。
4. 选中档案后点右下角「开始游戏」即可启动。

自研 10yv 核心（对标 Fabric）
- 启动器内置一套完全自研的 Rainclouds 10yv 核心引擎（loader 版本号 10yv），
  封装开源 Fabric 加载器，并提供自有的 Mod API 与事件总线。
- 通过「+ 核心」生成的实例默认使用「Rainclouds 核心」加载器，
  启动时会注入 rainclouds-core.jar 并扫描 mods 目录下的自研 Mod。
- 自带 3 个演示 Mod：hello（欢迎信息）、fps（FPS 显示）、spawninfo（出生点信息）。

Mod 与插件共存（按实例隔离）
- 每个档案拥有独立的游戏目录（instances/<id>/minecraft），
  mods、plugins、config、saves、resourcepacks、shaderpacks 互不干扰。
- 「模组」标签页扫描 mods 目录中的 .jar，自动识别是 Mod（Fabric/Forge/Quilt）
  还是服务端插件（Bukkit/Spigot/Paper），可单独启用/停用（改名 .disabled）。
- 「插件」标签页查看并管理 plugins 目录中的插件 .jar。
- 自研 Mod 使用 rainclouds.mod.json 清单格式，与 Fabric/Forge jar 互不冲突。

进阶模块（联机 / 内网穿透 / 一键开服，¥20 打包解锁）
- 启动器内置三大进阶模块，打包售价 20 元，一次解锁全部：
  - 联机模块：服务器收藏、局域网扫描、一键复制连接信息。
  - 内网穿透模块：内置 frpc 客户端，自备 frp 服务器地址与令牌即可把本地
    25565 端口映射为公网地址，方便好友联机（需自备 frp 服务端）。
  - 一键开服模块：自动下载并启动原版 / Paper / Fabric 服务端，可设版本、
    端口、内存、MOTD、正版验证开关，开服一键完成。
- 未解锁时，「工具箱」中的三个模块卡片会提示需要解锁；点「激活」窗口的
  「进阶模块 ¥20」粘贴 RCM- 开头的激活码即可解锁。
- 激活状态保存在本机，激活码由作者通过配套工具生成，离线永久有效。

完整版一元解锁（核心功能）
- rcl 完整版功能需一元解锁。启动器内「激活」窗口展示收款二维码。
- 扫码支付一元后，将支付截图发给作者，作者会返回一组激活码（格式 RCL-XXXXX-XXXXX-XXXXX-XXXXX）。
- 在激活窗口粘贴激活码并点「立即激活」即可离线永久解锁（无需联网）。

数字签名与版权
- rcl.exe 已使用 Rainclouds 自签名证书进行 Authenticode 数字签名。
- 右键 rcl.exe → 属性 → 详细信息，版权栏显示「Copyright © Rainclouds」。
- 自签名证书非受信任 CA 签发，Windows 可能提示“未知发布者”，属正常现象。

如何获取微软登录 ClientId（微软要求，建议自建）
1. 打开 https://entra.microsoft.com 进入「应用注册」，新注册一个应用。
2. 账户类型选择「个人 Microsoft 账户」。
3. 在「身份验证」中启用「允许公共客户端流」并保存。
4. 复制「应用程序(客户端) ID」，填入启动器「设置 → Azure ClientId」。

便携版
- rcl-portable.zip 为完整文件夹版本，解压后双击其中的 rcl.exe 即可，
  适合无法运行单文件版的环境。若下载下来是 .bin 结尾，重命名为 .zip 再解压。

常见问题
- 双击没反应：检查杀毒软件 / Windows Defender 是否拦截、隔离；
  右键 exe → 属性 → 勾选「解除锁定」。首次启动需自解压，请等待几秒。
- 仍无法启动：打开下方目录中的 crash.log 查看具体错误原因。

游戏与缓存目录
%APPDATA%\RaincloudsMC
