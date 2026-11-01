#!/usr/bin/env python3
"""
把 Rainclouds Minecraft Launcher 社区版上传到 GitHub。

前置条件：环境变量 GH_TOKEN 为有 repo 权限的 Personal Access Token。
用法：
    GH_TOKEN=ghp_xxx python3 tools/gh_upload.py

流程：
  1. 若仓库不存在则创建（默认公开）
  2. 打本地 tag（v1.0.2 / v1.2.0）并推送 main 与 tags
  3. 为每个 tag 创建 Release，并上传 rcl.exe / rcl-portable.zip / README.txt
"""
import os
import subprocess
import sys

import requests

TOKEN = os.environ.get("GH_TOKEN", "").strip()
if not TOKEN:
    print("错误：请先设置环境变量 GH_TOKEN（GitHub Personal Access Token，需 repo 权限）")
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
- **自研 10yv 核心**：内置自研加载器（`io.rainclouds.core.loader.RaincloudsLoader`），
  自带 3 个演示 Mod（欢迎 / FPS 监视 / 出生点信息），Mod 与插件可共存
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

> 说明：签名使用自签名证书，Windows 可能提示“未知发布者”，属正常现象。
"""

def run(args, **kw):
    print("  $", " ".join(args[:6]))
    return subprocess.run(args, cwd=ROOT, check=False, capture_output=True, text=True, **kw)

def api(method, url, **kw):
    return requests.request(method, url, headers=HEADERS, timeout=120, **kw)

def main():
    # ---- 1. 创建仓库 ----
    print(f"[1/4] 创建/检查仓库 {OWNER}/{REPO} ...")
    r = api("POST", "https://api.github.com/user/repos",
            json={"name": REPO, "description": "Rainclouds Minecraft Launcher 社区版 - Windows Minecraft 启动器（自研 10yv 核心 / 联机 / 内网穿透 / 一键开服）",
                  "private": PRIVATE, "auto_init": False, "has_issues": True})
    if r.status_code == 201:
        print("  仓库已创建")
    elif r.status_code == 422:
        print("  仓库已存在，继续")
    else:
        print(f"  创建失败 HTTP {r.status_code}: {r.text[:300]}")
        sys.exit(1)

    # ---- 2. 分支与 tag ----
    print("[2/4] 整理分支与 tag ...")
    run(["git", "branch", "-M", "main"])
    for t in TAGS:
        run(["git", "tag", "-d", t])
        res = run(["git", "tag", "-a", t, "-m", f"Release {t}"])
        if res.returncode != 0:
            print("  打 tag 失败:", res.stderr[:200])
    print("  分支: main, tags:", ", ".join(TAGS))

    # ---- 3. 推送 ----
    print("[3/4] 推送到 GitHub ...")
    remote = f"https://{OWNER}:{TOKEN}@github.com/{OWNER}/{REPO}.git"
    run(["git", "remote", "remove", "origin"])
    run(["git", "remote", "add", "origin", remote])
    res = run(["git", "push", "-u", "origin", "main", "--force"])
    if res.returncode != 0:
        print("  推送 main 失败:", res.stderr[:400])
        sys.exit(1)
    print("  main 已推送")
    res = run(["git", "push", "origin", "--tags", "--force"])
    print("  tags 推送:", "成功" if res.returncode == 0 else res.stderr[:300])

    # ---- 4. 创建 Release 并上传附件 ----
    print("[4/4] 创建 Release 并上传附件 ...")
    for tag in TAGS:
        r = api("POST", f"https://api.github.com/repos/{OWNER}/{REPO}/releases",
                json={"tag_name": tag, "name": f"Rainclouds Minecraft Launcher 社区版 {tag}",
                      "body": RELEASE_BODY.format(tag=tag), "draft": False, "prerelease": False})
        if r.status_code == 422:
            # 已存在则取现有 release
            g = api("GET", f"https://api.github.com/repos/{OWNER}/{REPO}/releases/tags/{tag}")
            if g.status_code != 200:
                print(f"  {tag}: 创建失败且无法获取 ({r.status_code})")
                continue
            rel = g.json()
            print(f"  {tag}: 已存在，复用")
        elif r.status_code in (200, 201):
            rel = r.json()
            print(f"  {tag}: 已创建")
        else:
            print(f"  {tag}: 创建失败 HTTP {r.status_code} {r.text[:200]}")
            continue

        upload_url = rel["upload_url"].split("{")[0]
        for asset in ASSETS:
            path = os.path.join(ROOT, asset)
            if not os.path.exists(path):
                print(f"    跳过(缺失): {asset}")
                continue
            with open(path, "rb") as f:
                data = f.read()
            ur = requests.post(upload_url, headers={
                "Authorization": f"token {TOKEN}",
                "Accept": "application/vnd.github+json",
                "Content-Type": "application/octet-stream",
            }, params={"name": os.path.basename(path)}, data=data, timeout=900)
            if ur.status_code in (200, 201):
                print(f"    已上传 {asset} ({len(data)/1048576:.1f} MB)")
            else:
                print(f"    上传失败 {asset}: HTTP {ur.status_code} {ur.text[:150]}")

    print(f"\n完成：https://github.com/{OWNER}/{REPO}")
    for t in TAGS:
        print(f"  Release {t}: https://github.com/{OWNER}/{REPO}/releases/tag/{t}")

if __name__ == "__main__":
    main()
