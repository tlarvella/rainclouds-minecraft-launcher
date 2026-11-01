#!/usr/bin/env python3
"""
Rainclouds Launcher 激活码生成器（作者自用，请勿随程序分发）

用法：
    python keygen.py                      # 主程序（RCL-）生成 1 个
    python keygen.py 10                   # 主程序批量生成 10 个
    python keygen.py --tier modules       # 进阶模块（RCM-）生成 1 个
    python keygen.py --tier modules 10    # 进阶模块批量 10 个
    python keygen.py --check RCL-XXXXX-XXXXX-XXXXX-XXXXX          # 校验主程序码
    python keygen.py --check --tier modules RCM-XXXXX-XXXXX-XXXXX-XXXXX  # 校验模块码

密钥必须与 src/RCL.Core/Licensing/*.cs 中的 Secret 一致。
"""
import hashlib
import hmac
import random
import sys

ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"
PAYLOAD_LEN = 5
SIG_LEN = 15

# 两个层级的独立密钥
TIERS = {
    "core": {
        "prefix": "RCL",
        "secret": b"rainclouds-launcher-2026-9f3c1a7e5b2d4c8f",
        "tag": b"RCL1:",
    },
    "modules": {
        "prefix": "RCM",
        "secret": b"rainclouds-modules-2026-7b3e9c2a1f5d4e8c",
        "tag": b"RCM1:",
    },
}


def to_base32(data: bytes, chars: int) -> str:
    out = []
    buf = 0
    bits = 0
    for byte in data:
        buf = (buf << 8) | byte
        bits += 8
        while bits >= 5:
            out.append(ALPHABET[(buf >> (bits - 5)) & 31])
            bits -= 5
            if len(out) == chars:
                return "".join(out)
    while len(out) < chars:
        out.append(ALPHABET[0])
    return "".join(out)


def signature(payload: str, tier: str) -> str:
    cfg = TIERS[tier]
    digest = hmac.new(cfg["secret"], cfg["tag"] + payload.encode(), hashlib.sha256).digest()
    return to_base32(digest, SIG_LEN)


def format_key(raw: str, tier: str) -> str:
    prefix = TIERS[tier]["prefix"]
    return prefix + "-" + "-".join(raw[i:i + 5] for i in range(0, len(raw), 5))


def generate(count: int = 1, tier: str = "core") -> None:
    for _ in range(count):
        payload = "".join(random.choice(ALPHABET) for _ in range(PAYLOAD_LEN))
        print(format_key(payload + signature(payload, tier), tier))


def normalize(text: str, tier: str) -> str:
    prefix = TIERS[tier]["prefix"]
    s = "".join(c for c in text.upper() if c.isalnum())
    if s.startswith(prefix):
        s = s[len(prefix):]
    return s


def verify(key: str, tier: str = "core") -> bool:
    k = normalize(key, tier)
    if len(k) != PAYLOAD_LEN + SIG_LEN:
        return False
    payload, sig = k[:PAYLOAD_LEN], k[PAYLOAD_LEN:]
    return hmac.compare_digest(sig, signature(payload, tier))


if __name__ == "__main__":
    raw = sys.argv[1:]
    tier = "core"
    check = False
    positional = []
    i = 0
    while i < len(raw):
        a = raw[i]
        if a == "--tier":
            tier = raw[i + 1]
            i += 2
        elif a == "--check":
            check = True
            i += 1
        else:
            positional.append(a)
            i += 1

    if check:
        target = " ".join(positional)
        print("有效" if verify(target, tier) else "无效")
    else:
        n = int(positional[0]) if positional else 1
        generate(n, tier)
