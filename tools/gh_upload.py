#!/usr/bin/env python3
"""
把 Rainclouds Minecraft Launcher 社区版上传到 GitHub（带重试，应对线路抖动）。

用法：
    GH_TOKEN=ghp_xxx python3 tools/gh_upload.py
"""
import os
import subprocess
import sys
import time

import requests

TOKEN = os.environ.get("GH_TOKEN", "").strip()
if not TOKEN:
    print("错误：请先设置环境变量 GH_TOKEN（GitHub PAT，需 repo 权限）")
    sys.exit(1)

OWNER = os.environ.get("GH_OWNER", "tlarvella")
REPO = os.environ.get("GH_REPO", "rainclouds-minecraft-launcher")
PRIVATE = os.environ.get("GH_PRIVATE", "0") == "1"
TAGS = ["v1.0.2", "v1.2.0"]
ASSETS = ["rcl.exe", "rcl-portable.zip", "README.txt"]
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

HEADERS = {
    "Authorization": f"token {TOKEN}",
    "Accept": "application/vnd.github+json",
}

RELEASE_BODY = """## Rainclouds Minecraft Launcher 社区版 {tag}

适用于 Windows 10 及以上的 Minecraft 启动器，单文件 `rcl.exe`，无需安装 .NET 运行时。

### 核心特性
- **自研 10yv 核心**：内置自研加载器（`io.rainclouds.core.loader.RaincloudsLoader`），自带 3 个演示 Mod（欢迎 / FPS 监视 / 出生点信息），Mod 与插件可共存
- **多账号登录**：微软设备码 OAuth + 离线账号，可切换管理
- **版本与加载器**：原版 / Fabric / Forge / Quilt，自动下载并静默安装
- **启动页**：启动时展示品牌启动画面
- **版权与签名**：文件属性含 `Copyright © Rainclouds`，附带 Authenticode 数字签名

### 进阶模块（RCM- 授权，¥20）
- 联机模块：服务器收藏 + 局域网扫描
- 内网穿透模块：内置 frpc，需自备 frp 服务端
- 一键开服模块：Vanilla / Paper / Fabric 服务端一键下载启动

### 下载
- `rcl.exe` — 单文件可执行（约 43 MB）
- `rcl-portable.zip` — 便携版，解压即用

> 签名使用自签名证书，Windows 可能提示“未知发布者”，属正常现象。
"""

def log(*a):
    print("[upload]", *a, flush=True)

def req(method, url, tries=5, base_timeout=30, **kw):
    last = None
    for i in range(tries):
        try:
            return requests.request(method, url, headers=HEADERS, timeout=base_timeout, **kw)
        except Exception as e:
            last = e
            log(f"  网络错误({i+1}/{tries}) {type(e).__name__}，重试...")
            time.sleep(2)
    raise last

def run(args):
    log("  $", " ".join([a if "ghp_" not in a else "<token>" for a in args[:6]]))
    return subprocess.run(args, cwd=ROOT, check=False, capture_output=True, text=True)

def main():
    log("[1/4] 创建/检查仓库", f"{OWNER}/{REPO}")
    r = req("POST", "https://api.github.com/user/repos",
            json={"name": REPO,
                  "description": "Rainclouds Minecraft Launcher 社区版 - Windows Minecraft 启动器（自研 10yv 核心 / 联机 / 内网穿透 / 一键开服）",
                  "private": PRIVATE, "auto_init": False, "has_issues": True})
    if r.status_code == 201:
        log("  仓库已创建")
    elif r.status_code == 422:
        log("  仓库已存在，继续")
    else:
        log(f"  创建失败 HTTP {r.status_code}: {r.text[:300]}")
        sys.exit(1)

    log("[2/4] 整理分支与 tag")
    run(["git", "branch", "-M", "main"])
    for t in TAGS:
        run(["git", "tag", "-d", t])
        res = run(["git", "tag", "-a", t, "-m", f"Release {t}"])
        if res.returncode != 0:
            log("  打 tag 失败:", res.stderr[:200])
    log("  分支 main, tags:", ", ".join(TAGS))

    log("[3/4] 推送到 GitHub")
    remote = f"https://{OWNER}:{TOKEN}@github.com/{OWNER}/{REPO}.git"
    run(["git", "remote", "remove", "origin"])
    run(["git", "remote", "add", "origin", remote])
    ok = False
    for attempt in range(4):
        res = run(["git", "push", "-u", "origin", "main", "--force"])
        if res.returncode == 0:
            ok = True
            break
        log(f"  推送失败(第{attempt+1}次):", (res.stderr or "")[:300])
        time.sleep(3)
    if not ok:
        log("❌ 推送 main 失败")
        sys.exit(1)
    log("  main 已推送")
    for attempt in range(4):
        res = run(["git", "push", "origin", "--tags", "--force"])
        if res.returncode == 0:
            log("  tags 已推送")
            break
        log(f"  tags 推送失败(第{attempt+1}次)，重试...")
        time.sleep(3)

    log("[4/4] 创建 Release 并上传附件")
    for tag in TAGS:
        r = req("POST", f"https://api.github.com/repos/{OWNER}/{REPO}/releases",
                json={"tag_name": tag,
                      "name": f"Rainclouds Minecraft Launcher 社区版 {tag}",
                      "body": RELEASE_BODY.format(tag=tag), "draft": False, "prerelease": False})
        if r.status_code == 422:
            g = req("GET", f"https://api.github.com/repos/{OWNER}/{REPO}/releases/tags/{tag}")
            if g.status_code != 200:
                log(f"  {tag}: 创建失败且无法获取 ({r.status_code})")
                continue
            rel = g.json()
            log(f"  {tag}: 已存在，复用")
        elif r.status_code in (200, 201):
            rel = r.json()
            log(f"  {tag}: 已创建")
        else:
            log(f"  {tag}: 创建失败 HTTP {r.status_code} {r.text[:200]}")
            continue

        upload_url = rel["upload_url"].split("{")[0]
        for asset in ASSETS:
            path = os.path.join(ROOT, asset)
            if not os.path.exists(path):
                log(f"    跳过(缺失): {asset}")
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
                        log(f"    已上传 {asset} ({len(data)/1048576:.1f} MB)")
                        done = True
                        break
                    log(f"    上传失败 {asset}: HTTP {ur.status_code} {ur.text[:120]}")
                except Exception as e:
                    log(f"    上传异常 {asset} ({attempt+1}/3): {type(e).__name__}")
                time.sleep(3)
            if not done:
                log(f"    ❌ {asset} 最终未上传成功")

    log(f"\n完成：https://github.com/{OWNER}/{REPO}")
    for t in TAGS:
        log(f"  Release {t}: https://github.com/{OWNER}/{REPO}/releases/tag/{t}")

if __name__ == "__main__":
    main()
