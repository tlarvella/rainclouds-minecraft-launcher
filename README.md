# Rainclouds Minecraft Launcher 社区版 (rcl)

一个面向 **Windows 10 及以上** 的第三方 Minecraft 启动器，单文件可执行 `rcl.exe`。
基于 **.NET 6 + Avalonia UI** 构建，发布为 Windows 自包含（self-contained）单文件，无需用户安装运行时。当前版本 **v1.3 社区版（开源核心）**。

---

## 📖 开源 / 闭源说明（Open-Core）

本项目采用 **开放核心（Open-Core）** 模式：

- **开源（本仓库）**：启动器核心、自研 10yv 核心、微软登录、版本管理、Mod 加载、Java 管理等全部基础能力，**完整源码公开**，可自由查看与构建。
- **闭源（进阶模块）**：联机 / 内网穿透 / 一键开服这三大进阶模块的 **源码不公开**，以独立的闭源程序集 `RCL.Paid.dll` 形式交付。开源端仅保留其契约接口（`RCL.Core.Modules.IPaidModule`）与动态加载器（`PaidModuleLoader`）——运行时在验证授权后才会加载该 DLL。
- **授权**：三大模块打包 ¥20 解锁（对应 `RCM-` 激活码），与核心功能（¥1，`RCL-` 激活码）相互独立。无 `RCL.Paid.dll` 时，社区版仅含开源功能，进阶模块按钮会优雅提示「未安装」。

> 闭源源码位于本地 `src/RCL.Paid/`（已在 `.gitignore` 中排除，**不进入本公开仓库**）。如需自行构建闭源 DLL 用于交付，见下方「构建」章节。

---

## ✨ 功能特性

- **微软账号登录**：采用设备码（Device Code）OAuth2 流程，经 Xbox Live → XSTS → Minecraft Services 完成认证，支持令牌自动续期与离线游玩。
- **完整版本管理**：拉取官方版本清单（正式版 / 快照 / 旧版），自动下载版本 JSON、客户端 jar、依赖库、资产索引与对象文件，并对原生库（natives）按平台规则筛选与解压。
- **Mod 加载器**：内置 **Fabric / Forge / Quilt** 安装器，自动下载 installer 并静默安装，自动解析 `inheritsFrom`。
- **自研 10yv 核心**：内置一套完全自研的 Rainclouds 10yv 核心引擎（loader 版本号 `10yv`），封装开源 Fabric 加载器并提供自有 Mod API 与事件总线；自带 3 个演示 Mod（hello / fps / spawninfo）。
- **Mod 与插件共存**：每个档案独立游戏目录，自动识别 Fabric/Forge Mod 与 Bukkit/Spigot/Paper 插件，可分别启用/停用；自研 Mod 使用 `rainclouds.mod.json` 清单，与 Fabric/Forge 互不冲突。
- **Java 管理**：自动扫描本机已安装 JDK/JRE；若缺失所需版本，自动下载 **Adoptium Temurin** 并解压到运行时目录。
- **多档案（Installation）**：可创建多个游戏配置，分别设置版本、加载器、内存、分辨率、Java、游戏目录与附加参数。
- **完善的启动逻辑**：严格按官方协议拼接 JVM / 游戏参数，支持 `${classpath}`、`${natives_directory}`、`${auth_player_name}`、`${resolution_width/height}` 等全部变量替换，兼容新版（`arguments`）与旧版（`minecraftArguments`）版本格式。
- **进度与日志**：下载/安装/启动全过程进度条 + 控制台日志，便于排错。
- **启动页面（Splash）**：每次启动先显示 Rainclouds 启动页，再进入主窗口。
- **数字签名**：`rcl.exe` 已使用 **Rainclouds 自签名证书** 进行 Authenticode 签名，属性版权栏显示 `Copyright © Rainclouds`。

---

## 🚀 快速使用

1. 直接运行 `dist/rcl.exe`（或本项目根目录的 `rcl.exe`）。
2. 首次启动会在 `%APPDATA%/RaincloudsMC` 创建数据目录。
3. 点击右上角 **登录**，用微软账号完成设备码授权。
4. 在左侧 **新建档案**，选择游戏版本与加载器，点击 **保存**。
5. 点击 **▶ 开始游戏**，启动器会自动准备 Java、下载资源并启动游戏。

> 数据目录结构：`versions/`（版本）、`libraries/`（依赖）、`assets/`（资产）、`runtime/java/`（自带 JDK）、`rcl.json`（档案）、`accounts.json`（账号）、`settings.json`（设置）。

---

## 🔑 关于微软登录的 ClientId（重要）

微软账号登录使用 **设备码流**，需要一个 Azure AD 应用（公开客户端）的 **ClientId**。
启动器内置了一个公共 ClientId 作为默认值，但不同环境可能受限，**推荐你免费注册自己的应用**以确保稳定：

1. 打开 <https://entra.microsoft.com> → **应用注册（App registrations）** → 新注册。
2. 名称随意；**支持的账户类型**选「个人 Microsoft 账户」（或「任何组织目录中的账户和个人 Microsoft 账户」）。
3. 重定向 URI 无需填写（设备码流不需要）。
4. 注册完成后，复制 **应用程序(客户端) ID**。
5. 在启动器底部「全局设置」的 **Azure ClientId** 中粘贴保存，重新登录即可。
6. 所需作用域已在代码内固定为 `XboxLive.signin offline_access`，无需额外配置 API 权限（设备码流使用委托权限，由用户登录时授权）。

---

## 🧩 自研 10yv 核心

启动器内置一套完全自研的 **Rainclouds 10yv 核心** 引擎（loader 版本号 `10yv`），对标 Fabric：它在运行时注入 `rainclouds-core.jar`，提供自有的 Mod API、事件总线（客户端 tick、进服、聊天等钩子）与 HUD 能力，并兼容扫描 `mods/` 目录下的自研 Mod（`rainclouds.mod.json` 清单）。

- 通过左侧 **「+ 核心」** 生成的实例默认使用「Rainclouds 核心」加载器，开箱即玩。
- 自带 3 个演示 Mod：`hello`（欢迎信息）、`fps`（FPS 显示）、`spawninfo`（出生点信息）。
- 自研 Mod 与 Fabric/Forge jar 共存于同一 `mods/` 目录，按清单格式自动区分。

## 🔓 进阶模块（联机 / 内网穿透 / 一键开服，¥20 打包解锁）

启动器提供三大进阶模块，**打包售价 20 元，一次解锁全部**，在「工具箱」标签页中可用：

- **联机模块**：服务器收藏、局域网扫描、一键复制连接信息。
- **内网穿透模块**：内置 `frpc` 客户端，自备 frp 服务器地址与令牌即可把本地 `25565` 端口映射为公网地址（需自备 frp 服务端）。
- **一键开服模块**：自动下载并启动 原版 / Paper / Fabric 服务端，可设版本、端口、内存、MOTD、正版验证开关，一键开服。

> **授权与交付**：这三大模块的**源码闭源**（见上方「开源 / 闭源说明」）。购买并激活 `RCM-` 激活码后，会获得闭源组件 `RCL.Paid.dll` 与本机解锁文件；将其与 `rcl.exe` 放在同一目录即可启用。开源社区版（GitHub Release）仅包含基础启动器，不含该闭源组件。

未解锁时模块卡片会提示需要解锁；在「激活」窗口的 **「进阶模块 ¥20」** 中粘贴 `RCM-` 开头的激活码即可解锁。激活状态保存在本机，离线永久有效。核心功能（完整版）仍需一元解锁，对应 `RCL-` 激活码。

---

## 🛠 自行构建（Windows）

前置：安装 [.NET 6 SDK](https://dotnet.microsoft.com/download)。

```bat
build.bat
```

产物位于 `dist/rcl.exe`（社区版，仅含开源功能）。在 Windows 上由 MSBuild 发布时，会正确生成 GUI 子系统并嵌入清单（DPI / Windows 10 兼容），无需额外处理。

### 在 Linux / macOS 交叉发布

```bash
chmod +x build.sh
./build.sh
```

交叉发布时无法由 MSBuild 自定义 PE 子系统，脚本会自动把 `rcl.exe` 的子系统从 CONSOLE 改为 GUI，避免启动弹出控制台窗口；随后用 `pefile` 注入 `VS_VERSION_INFO` 版本资源（版权 `Copyright © Rainclouds`、产品名、版本号），并用 `osslsigncode` 完成 Authenticode 自签名。

### 构建闭源进阶模块（仅交付用，不公开）

进阶模块的源码位于本地 `src/RCL.Paid/`（默认不在本仓库）。构建可交付的闭源组件：

```bash
chmod +x build-closed.sh
./build-closed.sh
```

产物位于 `dist-paid/RCL.Paid.dll`（已签名）。将该 DLL 与 `rcl.exe` 置于同一目录，并在本机激活 `RCM-` 激活码后，进阶模块即可启用。**请勿将 `RCL.Paid.dll` 或 `src/RCL.Paid/` 提交到公开仓库。**

---

## 📂 项目结构

```
rcl/
├── build.sh / build.bat          # 社区版（开源）构建脚本
├── build-closed.sh               # 闭源进阶模块构建脚本（仅本地交付用）
├── rcl.exe                       # 已构建好的可执行文件（Windows 10+）
├── src/
│   ├── RCL.Core/                 # 核心库（与平台无关，开源）
│   │   ├── Auth/                 # 微软认证（设备码 / Xbox / XSTS / Minecraft）
│   │   ├── Versions/             # 版本清单、版本 JSON 解析、inheritsFrom 合并
│   │   ├── Download/             # 并行下载器（带进度、重试、SHA1 校验）
│   │   ├── Libraries/            # 依赖库筛选、classpath、natives 解压
│   │   ├── Assets/               # 资产索引与对象下载、legacy 虚拟资产
│   │   ├── Java/                 # 本机 Java 检测、Temurin 下载
│   │   ├── ModLoaders/           # Fabric / Forge / Quilt 安装器
│   │   ├── Launch/               # 参数组装、进程启动、准备编排
│   │   ├── Modules/              # 闭源模块契约 IPaidModule 与加载器（开源接口层）
│   │   ├── Licensing/            # 授权校验（RCL- / RCM- 离线 HMAC 校验）
│   │   ├── Profiles/             # 设置 / 账号 / 档案持久化
│   │   └── Utils/                # 路径、哈希、JSON 等工具
│   ├── RCL.UI/                   # Avalonia 界面（主窗口 / 登录对话框 / 视图模型，开源）
│   └── RCL.Paid/                 # 闭源进阶模块（联机/内网穿透/一键开服，源码不公开，gitignored）
└── tools/Test/                   # 核心逻辑离线验证小程序（可选）
```

---

## ⚠️ 说明与免责

- 本项目为第三方启动器，遵循 Minecraft EULA 与 Microsoft/Xbox 服务条款使用；账号数据仅保存在本机 `%APPDATA%/RaincloudsMC`。
- 启动器不修改任何游戏文件，所有下载均来自官方 / 官方认可的镜像源（Mojang、FabricMC、MinecraftForge、QuiltMC、Adoptium）。
- 如登录遇到 ClientId 限制，请按上文注册自己的 Azure 应用。
