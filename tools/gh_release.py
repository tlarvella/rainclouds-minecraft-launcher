#!/usr/bin/env python3
"""
为 Rainclouds Minecraft Launcher 社区版创建单个 GitHub Release 并上传附件。

用法：
    GH_TOKEN=ghp_xxx python3 tools/gh_release.py [tag] [version_in_body]
默认 tag=v1.4.0。
"""
import os
import sys
import time

import requests

TOKEN = os.environ.get("GH_TOKEN", "").strip()
if not TOKEN:
    print("错误：请先设置环境变量 GH_TOKEN（GitHub PAT，需 repo 权限）")
    sys.exit(1)

OWNER = os.environ.get("GH_OWNER", "tlarvella")
REPO = os.environ.get("GH_REPO", "rainclouds-minecraft-launcher")
TAG = sys.argv[1] if len(sys.argv) > 1 else "v1.4.0"
VER = sys.argv[2] if len(sys.argv) > 2 else TAG.lstrip("v")
ASSETS = ["rcl.exe", "rcl-portable.zip", "README.txt"]
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HEADERS = {"Authorization": f"token {TOKEN}", "Accept": "application/vnd.github+json"}

BODY = f"""## Rainclouds Minecraft Launcher 社区版 {TAG}

适用于 Windows 10 及以上的 Minecraft 启动器，单文件 `rcl.exe`，无需安装 .NET 运行时。

### 开源 / 闭源（Open-Core）
- **开源（本仓库）**：启动器核心、自研 10yv 核心、微软登录、版本管理、Mod 加载、Java 管理等基础能力，**完整源码公开**。
- **闭源（进阶模块）**：联机 / 内网穿透 / 一键开服三大进阶模块的**源码不公开**，以独立闭源程序集 `RCL.Paid.dll` 交付，运行时授权校验通过后动态加载。
- 本社区版 Release 仅含开源基础启动器；进阶模块需购买 ¥20 激活码（RCM-）后另行获取 `RCL.Paid.dll` 并置于 `rcl.exe` 同目录启用。

### v{VER} 新增：离线资料馆与彩蛋
启动器内嵌了一批**真正有用的离线内容**，随 `rcl.exe` 一起发布，断网可用、不含任何第三方素材：
- **6 张像素风壁纸**（1600×900 PNG）：晨曦草原 / 雨云之夜 / 地底矿洞 / 下界要塞 / 末地之岛 / 繁花森林
- **6 个无损音效**（44.1kHz / 16bit / 立体声 WAV）：启动音 / 升级提示 / 发现钻石 / 雨声 / 洞穴氛围 / 隐藏旋律
- **6 份离线资料**：合成表速查（20 条配方）、方块图鉴（25 种）、离线生存手册、版本编年史、启动名言（28 条）、鸣谢与许可
- **打开方式**：连点顶栏云朵 logo 7 次，或按 ↑↑↓↓←→←→ B A
- **能做什么**：壁纸预览 / 导出 / 设为启动页背景，音效直接试听，断网查合成表与方块属性，一个隐藏挖矿小游戏；启动页每次随机显示一条名言

原始资源约 **21.8 MB**，由 `tools/gen_extras.py` 程序化生成（可复现）。因 PNG/WAV 属已编码的高熵媒体，单文件发布时几乎不被二次压缩，**exe 从 v1.3 的 41.3 MiB 增至 63.2 MiB（+21.9 MiB）**——不是无意义填充，而是一份开箱即用的离线内容包。

### 核心特性
- **自研 10yv 核心**：内置自研加载器，自带 3 个演示 Mod（欢迎 / FPS 监视 / 出生点信息），Mod 与插件可共存
- **多账号登录**：微软设备码 OAuth + 离线账号，可切换管理
- **版本与加载器**：原版 / Fabric / Forge / Quilt，自动下载并静默安装
- **启动页**：展示品牌启动画面 + 随机名言，支持自定义背景
- **版权与签名**：文件属性含 `Copyright © Rainclouds`，附带 Authenticode 数字签名

### 进阶模块（RCM- 授权，¥20，源码闭源）
- 联机模块：服务器收藏 + 局域网扫描
- 内网穿透模块：内置 frpc，需自备 frp 服务端
- 一键开服模块：Vanilla / Paper / Fabric 服务端一键下载启动

### 下载
- `rcl.exe` — 单文件可执行（约 66 MB，含离线资料馆）
- `rcl-portable.zip` — 便携版，解压即用
- `README.txt` — 使用说明

> 签名使用自签名证书，Windows 可能提示“未知发布者”，属正常现象。
"""


def log(*a):
    print("[release]", *a, flush=True)


def req(method, url, tries=5, base_timeout=30, **kw):
    last = None
    for i in range(tries):
        try:
            return requests.request(method, url, headers=HEADERS, timeout=base_timeout, **kw)
        except Exception as e:
            last = e
            log(f"  网络错误({i + 1}/{tries}) {type(e).__name__}，重试...")
            time.sleep(2)
    raise last


def main():
    log(f"创建 Release {TAG} for {OWNER}/{REPO}")
    r = req("POST", f"https://api.github.com/repos/{OWNER}/{REPO}/releases",
            json={"tag_name": TAG, "name": f"Rainclouds Minecraft Launcher 社区版 {TAG}",
                  "body": BODY, "draft": False, "prerelease": False})
    if r.status_code == 422:
        g = req("GET", f"https://api.github.com/repos/{OWNER}/{REPO}/releases/tags/{TAG}")
        if g.status_code != 200:
            log(f"  创建失败且无法获取: {r.status_code} {r.text[:300]}")
            sys.exit(1)
        rel = g.json()
        log("  Release 已存在，复用")
    elif r.status_code in (200, 201):
        rel = r.json()
        log("  Release 已创建")
    else:
        log(f"  创建失败 HTTP {r.status_code} {r.text[:300]}")
        sys.exit(1)

    upload_url = rel["upload_url"].split("{")[0]
    for asset in ASSETS:
        path = os.path.join(ROOT, asset)
        if not os.path.exists(path):
            log(f"  跳过(缺失): {asset}")
            continue
        with open(path, "rb") as f:
            data = f.read()
        done = False
        for attempt in range(3):
            try:
                ur = requests.post(upload_url,
                                   headers={"Authorization": f"token {TOKEN}",
                                            "Accept": "application/vnd.github+json",
                                            "Content-Type": "application/octet-stream"},
                                   params={"name": os.path.basename(path)},
                                   data=data, timeout=1200)
                if ur.status_code in (200, 201):
                    log(f"  已上传 {asset} ({len(data) / 1048576:.1f} MB)")
                    done = True
                    break
                log(f"  上传失败 {asset}: HTTP {ur.status_code} {ur.text[:120]}")
            except Exception as e:
                log(f"  上传异常 {asset} ({attempt + 1}/3): {type(e).__name__}")
            time.sleep(3)
        if not done:
            log(f"  ❌ {asset} 最终未上传成功")

    log(f"\n完成：https://github.com/{OWNER}/{REPO}/releases/tag/{TAG}")


if __name__ == "__main__":
    main()
