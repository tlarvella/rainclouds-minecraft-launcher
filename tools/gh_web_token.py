#!/usr/bin/env python3
"""
用「账号 + 密码 + TOTP 种子」通过 requests 完成 GitHub 网页登录，
并在登录成功后创建一枚 classic PAT（repo 权限），打印 TOKEN=ghp_xxx。

背景：用户环境无法访问 github.com；服务端已将 github.com 固定到可用 IP，
但线路约 1/3 概率超时，故所有请求带重试。

用法：
  GH_USER=xxx GH_PASS=xxx GH_TOTP_SEED=XXX python3 tools/gh_web_token.py [--probe]
"""
import hashlib
import hmac
import os
import re
import struct
import sys
import time

import requests
from bs4 import BeautifulSoup

USER = os.environ.get("GH_USER", "")
PASS = os.environ.get("GH_PASS", "")
SEED = os.environ.get("GH_TOTP_SEED", "")
PROBE = "--probe" in sys.argv

UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36")

def log(*a):
    print("[gh_web]", *a, flush=True)

def req(s, method, url, tries=6, timeout=20, **kw):
    last = None
    for i in range(tries):
        try:
            return s.request(method, url, timeout=timeout, **kw)
        except Exception as e:
            last = e
            log(f"  请求失败({i+1}/{tries}) {type(e).__name__}，重试...")
            time.sleep(0.8)
    raise last

def totp(secret: str) -> str:
    alph = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
    s = secret.upper().replace("=", "")
    bits = "".join(format(alph.index(c), "05b") for c in s)
    data = bytearray(int(bits[i:i+8], 2) for i in range(0, len(bits) - 7, 8))
    counter = int(time.time() // 30)
    msg = struct.pack(">Q", counter)
    h = hmac.new(bytes(data), msg, hashlib.sha1).digest()
    off = h[-1] & 0xF
    return "%06d" % ((struct.unpack(">I", h[off:off+4])[0] & 0x7FFFFFFF) % 1000000)

def get_token_from_form(soup):
    for form in soup.find_all("form"):
        inp = form.find("input", {"name": "authenticity_token"})
        if inp and inp.get("value"):
            return form, inp["value"]
    inp = soup.find("input", {"name": "authenticity_token"})
    return (None, inp["value"]) if inp and inp.get("value") else (None, None)

def main():
    s = requests.Session()
    s.headers.update({
        "User-Agent": UA,
        "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
        "Accept-Language": "en-US,en;q=0.9",
    })

    log("GET https://github.com/login")
    r = req(s, "GET", "https://github.com/login")
    log("status", r.status_code, "url", r.url)
    soup = BeautifulSoup(r.text, "html.parser")
    _, token = get_token_from_form(soup)
    if not token:
        log("❌ 未找到登录 CSRF token"); return
    log("已获取登录 authenticity_token:", token[:12] + "...")

    data = {
        "authenticity_token": token,
        "login": USER,
        "password": PASS,
        "commit": "Sign in",
        "webauthn-conditional": "undefined",
        "javascript-support": "true",
        "webauthn-support": "supported",
        "webauthn-iuvpaa-support": "unsupported",
        "return_to": "https://github.com/",
        "allow_signup": "",
        "client_id": "",
        "integration": "",
    }
    log("POST https://github.com/session")
    r2 = req(s, "POST", "https://github.com/session", data=data, allow_redirects=True)
    log("登录后 status", r2.status_code, "url", r2.url)
    html2 = r2.text
    fl = BeautifulSoup(html2, "html.parser").select_one(".flash-error, .flash, #js-flash-container")
    if fl:
        log("页面提示:", fl.get_text(" ", strip=True)[:200])

    if "two-factor" in r2.url or 'name="otp"' in html2:
        log("检测到 2FA 页面...")
        soup3 = BeautifulSoup(html2, "html.parser")
        _, t3 = get_token_from_form(soup3)
        code = totp(SEED)
        log("使用 OTP:", code)
        r3 = req(s, "POST", "https://github.com/sessions/two-factor",
                 data={"authenticity_token": t3, "otp": code}, allow_redirects=True)
        log("2FA 后 status", r3.status_code, "url", r3.url)
        html2 = r3.text

    rv = req(s, "GET", "https://github.com/settings/profile", allow_redirects=True)
    log("settings/profile status", rv.status_code, "url", rv.url)
    if "/login" in rv.url:
        log("❌ 仍未登录成功")
        ff = BeautifulSoup(rv.text, "html.parser").select_one(".flash-error, .flash")
        if ff:
            log("提示:", ff.get_text(" ", strip=True)[:200])
        return
    log("✅ 登录成功")

    rt = req(s, "GET", "https://github.com/settings/tokens/new")
    log("tokens/new status", rt.status_code, "url", rt.url)
    soup = BeautifulSoup(rt.text, "html.parser")
    form = soup.find("form", action=re.compile(r"/settings/tokens"))
    if not form:
        log("❌ 未找到创建 token 表单")
        return

    if PROBE:
        log("表单 action:", form.get("action"), "method:", form.get("method"))
        for inp in form.find_all(["input", "select"]):
            if inp.name == "input":
                log("  input:", inp.get("type"), "name=", inp.get("name"), "value=", inp.get("value"), "id=", inp.get("id"))
            else:
                log("  select: name=", inp.get("name"), "options=", [o.get("value") for o in inp.find_all("option")][:8])
        return

    action = form.get("action")
    base = "https://github.com" if action.startswith("/") else ""
    token_val = None
    for inp in form.find_all("input"):
        if inp.get("name") == "authenticity_token":
            token_val = inp.get("value"); break
    note = "rcl-upload-" + str(int(time.time()))
    payload = {"authenticity_token": token_val}
    for inp in form.find_all("input"):
        nm = inp.get("name") or ""
        if "description" in nm:
            payload[nm] = note
        elif nm == "oauth_access[default_expires_at]" and inp.get("value"):
            payload[nm] = inp.get("value")
    repo_cb = next((i for i in form.find_all("input", {"type": "checkbox"})
                    if (i.get("value") or "").lower() == "repo"), None)
    if repo_cb is not None:
        payload[repo_cb.get("name")] = "repo"
        log("勾选作用域:", repo_cb.get("name"))
    else:
        log("⚠️ 未找到 repo 复选框")

    log("POST 创建 token ...")
    rc = req(s, "POST", base + action, data=payload, allow_redirects=True)
    log("创建后 status", rc.status_code, "url", rc.url)
    m = re.search(r"gh[pousr]_[A-Za-z0-9]{36,}", rc.text)
    if m:
        log("✅ 已生成 PAT")
        print("TOKEN=" + m.group(0), flush=True)
        return
    soup2 = BeautifulSoup(rc.text, "html.parser")
    el = soup2.select_one("#new-oauth-token, code")
    if el and re.match(r"gh[pousr]_", el.get_text(strip=True)):
        print("TOKEN=" + el.get_text(strip=True), flush=True)
        return
    log("❌ 未取到 token；页面片段:", rc.text[:400].replace("\n", " "))

if __name__ == "__main__":
    if not (USER and PASS and SEED):
        print("缺少 GH_USER / GH_PASS / GH_TOTP_SEED"); sys.exit(1)
    main()
