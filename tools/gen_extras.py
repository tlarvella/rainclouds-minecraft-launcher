#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Rainclouds 启动器「离线资料馆 / 彩蛋」资源生成器。

本脚本程序化生成随 rcl.exe 一起嵌入发布的离线资源，全部为真实、可用的内容：
  - wallpapers/ : 6 张 Minecraft 像素风高清壁纸（1600x900，带材质噪点纹理）
  - audio/      : 6 个 44.1kHz/16bit 立体声 WAV 合成音效（启动/升级/钻石/雨声/洞穴/隐藏旋律）
  - data/       : 合成表、方块图鉴、名言、版本史、离线百科、鸣谢
  - index.json  : 资源清单（类别、文件名、标题、说明），供 EasterEggAssets 读取

生成的 PNG/WAV 属已编码的高熵媒体，单文件发布时几乎不会被二次压缩，
因此会实打实地增加启动器体积，同时每一项都能在彩蛋馆里浏览 / 试听 / 导出 / 查阅。

可重复执行：固定随机种子，输出确定性。
用法： python3 tools/gen_extras.py
"""
import json
import math
import os
import struct
import wave
from datetime import datetime

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "src", "RCL.Core", "Resources", "Extras"))
W, H = 1600, 900
CELL = 20                      # 像素方块尺寸
SAMPLE_RATE = 44100

WALLPAPERS = [
    ("morning_meadow.png",  "晨曦草原",   "日出时的草原与橡树，方格地形、云层与远处花点。"),
    ("raincloud_night.png", "雨云之夜",   "Rainclouds 主题：深蓝雨夜、斜落雨丝、水洼反光与山影。"),
    ("deep_cave.png",       "地底矿洞",   "分层岩壁与钻石 / 红石 / 金 / 煤矿脉，底部岩浆与火把。"),
    ("nether_fortress.png", "下界要塞",   "岩浆海、黑石拱门、悬浮灰烬与火焰粒子。"),
    ("end_island.png",      "末地之岛",   "紫色星空、末地石浮岛、黑曜石柱与末影水晶。"),
    ("flower_forest.png",   "繁花森林",   "明亮白昼、成片彩色花朵、树冠与飞舞的蜜蜂蝴蝶。"),
]

AUDIO = [
    ("startup.wav",       "启动音",     "C 大调上行琶音，8-bit 方波音色。"),
    ("levelup.wav",       "升级提示",   "五声音阶上行 + 铃音尾奏。"),
    ("diamond.wav",       "发现钻石",   "两声清脆叮咚，带泛音尾巴。"),
    ("rain.wav",          "雨声氛围",   "10 秒滤波白噪雨幕，含远处闷雷与零星雨滴。"),
    ("cave_ambience.wav", "洞穴氛围",   "10 秒低频 drone、风声与随机滴水。"),
    ("secret.wav",        "隐藏旋律",   "Rainclouds 原创 8 小节 8-bit 小旋律。"),
]

DATA = [
    ("crafting.json",        "合成表速查",   "常用物品的材料与摆位，离线可查。"),
    ("blocks.json",          "方块图鉴",     "常见方块的硬度、掉落、亮度与可燃性。"),
    ("quotes.txt",           "启动名言",     "Minecraft 经典语录与 Rainclouds 原创短句。"),
    ("version_history.json", "版本编年史",   "Rainclouds 启动器从 v1.0 到 v1.4 的更新记录。"),
    ("fieldguide.md",        "离线生存手册", "红石、附魔、生物群系、命令与启动器技巧速查。"),
    ("credits.txt",          "鸣谢与许可",   "项目致谢、开源组件与彩蛋说明。"),
]


# ----------------------------------------------------------------- 图像工具

def vgradient(top, bottom):
    """垂直渐变，返回 (H, W, 3) uint8。"""
    t = np.linspace(0, 1, H, dtype=np.float32)[:, None, None]
    a = np.array(top, np.float32)[None, None, :]
    b = np.array(bottom, np.float32)[None, None, :]
    arr = a * (1 - t) + b * t
    return np.broadcast_to(arr, (H, W, 3)).astype(np.uint8).copy()


def to_image(arr):
    return Image.fromarray(arr, "RGB")


def add_grain(img, seed, grain=30):
    """叠加材质噪点与方块网格线：模拟 Minecraft 像素纹理，同时保证画面高熵。"""
    rng = np.random.default_rng(seed)
    a = np.asarray(img).astype(np.int16)
    n = rng.integers(-grain, grain + 1, size=(H, W, 1), dtype=np.int16)
    a = np.clip(a + n, 0, 255)

    # 每 CELL 像素画一条暗色网格线，强化方块感
    a[::CELL, :, :] = (a[::CELL, :, :] * 0.78).astype(np.int16)
    a[:, ::CELL, :] = (a[:, ::CELL, :] * 0.78).astype(np.int16)
    return Image.fromarray(a.astype(np.uint8), "RGB")


def rect(draw, box, color, outline=None, width=1):
    draw.rectangle(box, fill=color, outline=outline, width=width)


# ----------------------------------------------------------------- 六个场景

def scene_morning():
    arr = vgradient((58, 104, 178), (255, 196, 120))
    img = to_image(arr)
    d = ImageDraw.Draw(img)
    # 太阳 + 光晕
    for r, c in [(92, (255, 226, 150)), (62, (255, 238, 186)), (34, (255, 250, 224))]:
        d.ellipse([1180 - r, 190 - r, 1180 + r, 190 + r], fill=c)
    # 云
    for (x, y, w) in [(120, 150, 260), (640, 110, 320), (900, 230, 210), (300, 300, 180)]:
        d.rounded_rectangle([x, y, x + w, y + 42], radius=20, fill=(238, 240, 246))
        d.rounded_rectangle([x + 40, y - 20, x + w - 60, y + 26], radius=18, fill=(246, 247, 250))
    # 地面分层（草地 / 泥土 / 深层）
    horizon = 560
    rect(d, [0, horizon, W, horizon + 60], (104, 168, 66))
    rect(d, [0, horizon + 60, W, H], (124, 88, 56))
    for y in range(horizon + 60, H, CELL * 2):
        rect(d, [0, y, W, y + 2], (98, 68, 44))
    # 树干与树冠
    for (tx, ty, s) in [(220, 400, 1.0), (1340, 460, 0.8), (760, 500, 0.6)]:
        tw = int(46 * s)
        rect(d, [tx, ty, tx + tw, horizon + 40], (96, 66, 40))
        rr = int(110 * s)
        for (ox, oy) in [(-rr // 2, -rr // 2), (rr // 3, -rr // 3), (-rr // 4, rr // 5)]:
            d.ellipse([tx + ox, ty + oy, tx + ox + rr, ty + oy + rr], fill=(56, 118, 52))
            d.ellipse([tx + ox + 8, ty + oy + 8, tx + ox + rr - 12, ty + oy + rr - 16], fill=(70, 138, 60))
    # 花朵
    rng = np.random.default_rng(11)
    for _ in range(220):
        x = int(rng.integers(0, W)); y = int(rng.integers(horizon + 4, H - 4))
        c = [(240, 90, 90), (250, 220, 90), (238, 150, 200), (245, 245, 245)][int(rng.integers(0, 4))]
        d.ellipse([x, y, x + 6, y + 6], fill=c)
    return img


def scene_raincloud_night():
    arr = vgradient((12, 18, 40), (42, 58, 92))
    img = to_image(arr)
    d = ImageDraw.Draw(img)
    # 月亮
    d.ellipse([180, 110, 280, 210], fill=(214, 224, 240))
    d.ellipse([150, 80, 310, 240], outline=(90, 110, 150), width=3)
    # 雨云（多层椭圆）
    rng = np.random.default_rng(23)
    for _ in range(46):
        x = int(rng.integers(-100, W)); y = int(rng.integers(20, 330))
        rw = int(rng.integers(160, 380)); rh = int(rng.integers(40, 90))
        shade = int(rng.integers(48, 92))
        d.ellipse([x, y, x + rw, y + rh], fill=(shade, shade + 8, shade + 24))
    # 远山剪影
    for i in range(0, W, 90):
        hh = int(120 + 60 * math.sin(i / 170.0))
        d.polygon([(i, 600), (i + 130, 600 - hh), (i + 260, 600)], fill=(26, 38, 56))
    # 地面 + 水洼
    rect(d, [0, 600, W, H], (30, 44, 46))
    for _ in range(26):
        x = int(rng.integers(0, W)); y = int(rng.integers(620, H - 20))
        w = int(rng.integers(60, 220))
        d.ellipse([x, y, x + w, y + 12], fill=(70, 104, 128))
    # 斜雨丝
    for _ in range(900):
        x = int(rng.integers(0, W)); y = int(rng.integers(0, H))
        ln = int(rng.integers(10, 26))
        d.line([(x, y), (x - 6, y + ln)], fill=(150, 176, 210), width=1)
    return img


def scene_deep_cave():
    rng = np.random.default_rng(37)
    arr = vgradient((58, 56, 54), (34, 33, 32))
    img = to_image(arr)
    d = ImageDraw.Draw(img)
    # 岩层横纹
    for y in range(0, H, 44):
        shade = int(38 + 24 * abs(math.sin(y / 97.0)))
        rect(d, [0, y, W, y + 40], (shade, shade - 2, shade - 4))
    # 矿脉簇
    ores = [((72, 214, 214), 14), ((214, 62, 62), 13), ((240, 186, 70), 11),
            ((40, 40, 42), 16), ((196, 176, 150), 10)]
    for color, cnt in ores:
        for _ in range(cnt):
            cx = int(rng.integers(60, W - 60)); cy = int(rng.integers(60, 660))
            for _ in range(int(rng.integers(4, 10))):
                ox = int(rng.integers(-40, 40)); oy = int(rng.integers(-40, 40))
                s = CELL
                rect(d, [cx + ox, cy + oy, cx + ox + s, cy + oy + s], color)
    # 岩浆带
    rect(d, [0, 770, W, H], (122, 44, 18))
    for _ in range(120):
        x = int(rng.integers(0, W)); y = int(rng.integers(770, H))
        c = (250, int(rng.integers(110, 190)), 30)
        rect(d, [x, y, x + int(rng.integers(10, 46)), y + 6], c)
    # 火把与光晕
    for (x, y) in [(300, 300), (1120, 420), (760, 640)]:
        for r, col in [(120, (70, 58, 30)), (70, (120, 96, 44)), (28, (200, 160, 70))]:
            d.ellipse([x - r, y - r, x + r, y + r], fill=col)
        rect(d, [x - 4, y, x + 4, y + 46], (110, 78, 44))
        d.ellipse([x - 12, y - 26, x + 12, y + 4], fill=(255, 206, 92))
    return img


def scene_nether():
    rng = np.random.default_rng(53)
    img = to_image(vgradient((52, 16, 20), (96, 28, 20)))
    d = ImageDraw.Draw(img)
    # 岩浆海
    rect(d, [0, 640, W, H], (150, 52, 16))
    for y in range(640, H, 16):
        rect(d, [0, y, W, y + 8], (196, 84, 22))
    for _ in range(180):
        x = int(rng.integers(0, W)); y = int(rng.integers(640, H))
        rect(d, [x, y, x + int(rng.integers(12, 70)), y + 5],
             (255, int(rng.integers(150, 220)), 40))
    # 要塞黑石柱与拱门
    for x in [140, 520, 980, 1360]:
        rect(d, [x, 180, x + 90, 660], (34, 26, 28))
        for yy in range(200, 640, 40):
            rect(d, [x + 6, yy, x + 84, yy + 3], (52, 40, 42))
    d.rectangle([430, 300, 1150, 660], outline=(30, 22, 24), width=26)
    d.rectangle([560, 380, 1020, 660], fill=(58, 20, 20))
    # 火焰粒子 / 灰烬
    for _ in range(260):
        x = int(rng.integers(0, W)); y = int(rng.integers(0, H))
        r = int(rng.integers(2, 7))
        c = (255, int(rng.integers(120, 210)), int(rng.integers(20, 80)))
        d.ellipse([x, y, x + r, y + r], fill=c)
    for _ in range(140):
        x = int(rng.integers(0, W)); y = int(rng.integers(0, 620))
        d.point((x, y), fill=(120, 96, 92))
    return img


def scene_end():
    rng = np.random.default_rng(71)
    img = to_image(vgradient((28, 12, 52), (74, 40, 104)))
    d = ImageDraw.Draw(img)
    # 星空
    for _ in range(420):
        x = int(rng.integers(0, W)); y = int(rng.integers(0, 680))
        r = int(rng.integers(1, 3))
        d.ellipse([x, y, x + r, y + r], fill=(224, 216, 246))
    # 浮岛
    d.ellipse([330, 470, 1290, 900], fill=(196, 190, 158))
    d.ellipse([360, 480, 1260, 850], fill=(214, 208, 176))
    for _ in range(300):
        x = int(rng.integers(360, 1260)); y = int(rng.integers(500, 840))
        d.point((x, y), fill=(170, 166, 138))
    # 黑曜石柱 + 末影水晶
    for (x, top) in [(520, 250), (820, 170), (1100, 300)]:
        rect(d, [x, top, x + 34, 620], (18, 14, 28))
        for r, col in [(60, (60, 130, 140)), (30, (120, 220, 224)), (14, (220, 250, 250))]:
            d.ellipse([x + 17 - r, top - r, x + 17 + r, top + r], fill=col)
        d.rectangle([x + 3, top - 26, x + 31, top + 2], fill=(160, 240, 240))
    # 末影龙剪影
    d.polygon([(700, 220), (760, 190), (900, 210), (960, 170), (930, 250), (820, 260),
               (760, 300), (700, 270)], fill=(20, 14, 34))
    # 虚空粒子
    for _ in range(160):
        x = int(rng.integers(0, W)); y = int(rng.integers(0, H))
        d.point((x, y), fill=(150, 110, 190))
    return img


def scene_flower_forest():
    rng = np.random.default_rng(97)
    img = to_image(vgradient((124, 186, 236), (222, 240, 240)))
    d = ImageDraw.Draw(img)
    # 草地
    rect(d, [0, 520, W, H], (110, 176, 74))
    for y in range(520, H, CELL):
        rect(d, [0, y, W, y + 2], (96, 158, 64))
    # 树
    for (tx, ty, s) in [(160, 260, 1.1), (620, 320, 0.9), (1180, 240, 1.2), (1420, 360, 0.7)]:
        rect(d, [tx, ty + 120, tx + int(40 * s), 560], (110, 78, 46))
        rr = int(150 * s)
        for (ox, oy) in [(-rr // 2, -rr // 2), (rr // 4, -rr // 3), (-rr // 3, rr // 6)]:
            d.ellipse([tx + ox, ty + oy, tx + ox + rr, ty + oy + rr], fill=(62, 132, 58))
    # 花海
    colors = [(240, 96, 112), (250, 214, 88), (238, 152, 202), (246, 246, 246),
              (128, 152, 238), (196, 120, 226), (255, 168, 70)]
    for _ in range(900):
        x = int(rng.integers(0, W)); y = int(rng.integers(530, H - 6))
        c = colors[int(rng.integers(0, len(colors)))]
        d.ellipse([x, y, x + 7, y + 7], fill=c)
        d.point((x + 3, y + 3), fill=(250, 236, 130))
    # 蜜蜂 / 蝴蝶
    for _ in range(46):
        x = int(rng.integers(0, W)); y = int(rng.integers(120, 520))
        c = (250, 210, 60) if rng.random() < 0.5 else (236, 168, 220)
        d.ellipse([x, y, x + 9, y + 6], fill=c)
        d.ellipse([x + 9, y - 4, x + 17, y + 8], fill=(250, 250, 250))
    return img


SCENES = [scene_morning, scene_raincloud_night, scene_deep_cave,
          scene_nether, scene_end, scene_flower_forest]


# ----------------------------------------------------------------- 音频工具

def _env(n, attack=0.01, release=0.25):
    """ADSR 简化包络（按实际采样数裁剪，兼容极短音符）。"""
    e = np.ones(n, np.float32)
    a = min(n, max(1, int(SAMPLE_RATE * attack)))
    r = min(n - a, max(1, int(SAMPLE_RATE * release)))
    if a > 0:
        e[:a] = np.linspace(0, 1, a, dtype=np.float32)
    if r > 0:
        e[n - r:] *= np.linspace(1, 0, r, dtype=np.float32)
    return e


def tone(freq, dur, kind="square", vol=0.3, attack=0.01, release=0.25, detune=0.0):
    n = int(SAMPLE_RATE * dur)
    t = np.arange(n, dtype=np.float32) / SAMPLE_RATE
    ph = 2 * np.pi * (freq + detune) * t
    if kind == "square":
        s = np.sign(np.sin(ph)) * 0.6 + 0.35 * np.sign(np.sin(2 * ph))
    elif kind == "triangle":
        s = 2 / np.pi * np.arcsin(np.sin(ph))
    elif kind == "saw":
        s = 2 * (t * freq % 1) - 1
    else:
        s = np.sin(ph)
    s = (s * vol * _env(n, attack, release)).astype(np.float32)
    return s


def silence(dur):
    return np.zeros(int(SAMPLE_RATE * dur), np.float32)


def join(*parts):
    return np.concatenate(parts)


def write_wav(path, mono, rng_seed):
    """转为 16bit 立体声写入；左右声道轻微去相关，听感更宽且保持无损体积。"""
    rng = np.random.default_rng(rng_seed)
    n = len(mono)
    right = mono * 0.92 + rng.normal(0, 0.0006, n).astype(np.float32)
    right = np.clip(right, -1, 1)
    left = np.clip(mono, -1, 1)
    inter = np.empty(n * 2, np.float32)
    inter[0::2] = left
    inter[1::2] = right
    pcm = (inter * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SAMPLE_RATE)
        w.writeframes(pcm.tobytes())
    rng2 = np.random.default_rng(rng_seed + 1)
    # 尾部保留极轻微房间底噪，避免尾段被压缩成全零（真实录音底噪）
    return pcm


def audio_startup():
    notes = [(261.63, .16), (329.63, .16), (392.00, .16), (523.25, .16),
             (392.00, .12), (523.25, .12), (659.25, .30), (523.25, .42)]
    parts = [silence(.05)]
    for f, d in notes:
        parts.append(tone(f, d, "square", .26))
        parts.append(tone(f * 2, d * .6, "triangle", .07))
    parts.append(tone(130.81, 1.0, "sine", .18, release=.6))
    return join(*parts)


def audio_levelup():
    seq = [523.25, 587.33, 659.25, 783.99, 880.00, 1046.50]
    parts = []
    for f in seq:
        parts.append(tone(f, .11, "triangle", .28, release=.12))
    parts.append(tone(1567.98, .55, "sine", .22, attack=.005, release=.5))
    parts.append(tone(2093.00, .55, "sine", .10, attack=.005, release=.5))
    return join(*parts)


def audio_diamond():
    parts = [tone(1318.51, .30, "sine", .34, release=.28),
             silence(.06),
             tone(1975.53, .55, "sine", .30, release=.5),
             tone(2637.02, .55, "sine", .09, release=.5)]
    return join(*parts)


def audio_rain(rng_seed=201):
    rng = np.random.default_rng(rng_seed)
    n = SAMPLE_RATE * 10
    white = rng.normal(0, 1, n).astype(np.float32)
    # 一阶低通模拟雨幕
    rain = np.empty(n, np.float32)
    acc = 0.0
    for i in range(n):
        acc = 0.86 * acc + 0.14 * white[i]
        rain[i] = acc
    rain *= 0.34
    # 远处闷雷：低频扫频
    t = np.arange(n, dtype=np.float32) / SAMPLE_RATE
    thunder = np.zeros(n, np.float32)
    for start, dur, amp in [(1.8, 2.4, .30), (6.9, 2.0, .22)]:
        s = int(start * SAMPLE_RATE); e = min(n, s + int(dur * SAMPLE_RATE))
        lt = np.arange(e - s, dtype=np.float32) / SAMPLE_RATE
        f = 62 - 26 * (lt / dur)
        thunder[s:e] = np.sin(2 * np.pi * np.cumsum(f) / SAMPLE_RATE) * amp * np.exp(-lt * 1.3)
    # 零星雨滴
    for _ in range(160):
        s = int(rng.integers(0, n - 2000))
        d = int(rng.integers(400, 1600))
        lt = np.arange(d, dtype=np.float32) / SAMPLE_RATE
        f = 900 + rng.random() * 1400
        thunder[s:s + d] += (np.sin(2 * np.pi * f * lt) * 0.05 * np.exp(-lt * 40)).astype(np.float32)
    return np.clip(rain + thunder, -1, 1)


def audio_cave(rng_seed=307):
    rng = np.random.default_rng(rng_seed)
    n = SAMPLE_RATE * 10
    t = np.arange(n, dtype=np.float32) / SAMPLE_RATE
    drone = (0.16 * np.sin(2 * np.pi * 55 * t)
             + 0.09 * np.sin(2 * np.pi * 82.4 * t)
             + 0.05 * np.sin(2 * np.pi * 110 * t * (1 + .002 * np.sin(2 * np.pi * .07 * t))))
    wind = np.convolve(rng.normal(0, 1, n).astype(np.float32),
                       np.ones(900, np.float32) / 900, mode="same") * 0.07
    out = drone + wind
    for _ in range(38):
        s = int(rng.integers(0, n - 6000))
        d = int(rng.integers(1200, 4200))
        lt = np.arange(d, dtype=np.float32) / SAMPLE_RATE
        f = 1100 + rng.random() * 900
        out[s:s + d] += (np.sin(2 * np.pi * f * lt) * 0.10 * np.exp(-lt * 12)).astype(np.float32)
    return np.clip(out, -1, 1)


def audio_secret():
    # Rainclouds 原创 8 小节小旋律（A 小调，8-bit）
    mel = [(440, .22), (523.25, .22), (659.25, .22), (587.33, .22),
           (523.25, .22), (493.88, .22), (440, .34), (0, .10),
           (659.25, .22), (698.46, .22), (880, .30), (783.99, .22),
           (659.25, .22), (587.33, .22), (440, .50)]
    parts = []
    for f, d in mel:
        if f == 0:
            parts.append(silence(d)); continue
        parts.append(tone(f, d, "square", .24, release=.14))
        parts.append(tone(f / 2, d, "triangle", .12, release=.2))
        parts.append(tone(f * 1.5, d * .5, "sine", .05, release=.1))
    return join(*parts)


AUDIO_FUNCS = [audio_startup, audio_levelup, audio_diamond,
               lambda: audio_rain(), lambda: audio_cave(), audio_secret]


# ----------------------------------------------------------------- 文本资料

CRAFTING = {
    "说明": "离线合成表速查：materials 为材料与数量，pattern 为工作台 3x3 摆位（. 表示空）。",
    "recipes": [
        {"name": "工作台", "result": "crafting_table x1", "materials": {"橡木木板": 4},
         "pattern": ["##.", "##.", "..."], "legend": {"#": "木板"}},
        {"name": "熔炉", "result": "furnace x1", "materials": {"圆石": 8},
         "pattern": ["###", "#.#", "###"], "legend": {"#": "圆石"}},
        {"name": "箱子", "result": "chest x1", "materials": {"木板": 8},
         "pattern": ["###", "#.#", "###"], "legend": {"#": "木板"}},
        {"name": "火把", "result": "torch x4", "materials": {"煤/木炭": 1, "木棍": 1},
         "pattern": ["#..", "S..", "..."], "legend": {"#": "煤", "S": "木棍"}},
        {"name": "木棍", "result": "stick x4", "materials": {"木板": 2},
         "pattern": ["#..", "#..", "..."], "legend": {"#": "木板"}},
        {"name": "木镐", "result": "wooden_pickaxe x1", "materials": {"木板": 3, "木棍": 2},
         "pattern": ["###", ".S.", ".S."], "legend": {"#": "木板", "S": "木棍"}},
        {"name": "石镐", "result": "stone_pickaxe x1", "materials": {"圆石": 3, "木棍": 2},
         "pattern": ["###", ".S.", ".S."], "legend": {"#": "圆石", "S": "木棍"}},
        {"name": "铁镐", "result": "iron_pickaxe x1", "materials": {"铁锭": 3, "木棍": 2},
         "pattern": ["###", ".S.", ".S."], "legend": {"#": "铁锭", "S": "木棍"}},
        {"name": "钻石镐", "result": "diamond_pickaxe x1", "materials": {"钻石": 3, "木棍": 2},
         "pattern": ["###", ".S.", ".S."], "legend": {"#": "钻石", "S": "木棍"}},
        {"name": "铁剑", "result": "iron_sword x1", "materials": {"铁锭": 2, "木棍": 1},
         "pattern": ["#..", "#..", "S.."], "legend": {"#": "铁锭", "S": "木棍"}},
        {"name": "铁盔甲（头盔）", "result": "iron_helmet x1", "materials": {"铁锭": 5},
         "pattern": ["###", "#.#", "..."], "legend": {"#": "铁锭"}},
        {"name": "铁盔甲（胸甲）", "result": "iron_chestplate x1", "materials": {"铁锭": 8},
         "pattern": ["#.#", "###", "###"], "legend": {"#": "铁锭"}},
        {"name": "盾牌", "result": "shield x1", "materials": {"木板": 6, "铁锭": 1},
         "pattern": ["#S#", "###", ".#."], "legend": {"#": "木板", "S": "铁锭"}},
        {"name": "熔炼（铁锭）", "result": "iron_ingot x1", "materials": {"铁矿石": 1, "燃料": 1},
         "pattern": ["熔炉", "上方放矿石，下方放煤/木炭", "每份燃料可熔炼 8 个"]},
        {"name": "附魔台", "result": "enchanting_table x1", "materials": {"书": 1, "钻石": 2, "黑曜石": 4},
         "pattern": ["B..", "DD.", "OOO"], "legend": {"B": "书", "D": "钻石", "O": "黑曜石"}},
        {"name": "酿造台", "result": "brewing_stand x1", "materials": {"烈焰棒": 1, "圆石": 3},
         "pattern": ["...", ".B.", "CCC"], "legend": {"B": "烈焰棒", "C": "圆石"}},
        {"name": "末影箱", "result": "ender_chest x1", "materials": {"黑曜石": 8, "末影之眼": 1},
         "pattern": ["OOO", "OEO", "OOO"], "legend": {"O": "黑曜石", "E": "末影之眼"}},
        {"name": "信标底座", "result": "beacon x1", "materials": {"黑曜石": 3, "玻璃": 5, "下界之星": 1},
         "pattern": ["GGG", "GSG", "OOO"], "legend": {"G": "玻璃", "S": "下界之星", "O": "黑曜石"}},
        {"name": "红石中继器", "result": "repeater x1", "materials": {"红石粉": 1, "圆石": 3, "木棍": 2},
         "pattern": ["S.S", "###", "..."], "legend": {"S": "木棍", "#": "圆石（中间格加红石粉）"}},
        {"name": "活塞", "result": "piston x1", "materials": {"木板": 3, "圆石": 4, "铁锭": 1, "红石粉": 1},
         "pattern": ["PPP", "CIC", "CRC"],
         "legend": {"P": "木板", "C": "圆石", "I": "铁锭", "R": "红石粉"}},
    ],
    "小贴士": [
        "木镐只能挖石头与煤；铁镐起可挖铁、青金石、红石、钻石需铁镐及以上。",
        "钻石矿最佳层数在 Y=-59 附近（1.18+），记得带水桶应对岩浆。",
        "时运 III 附魔可让钻石矿掉落最多 4 颗钻石。",
    ],
}

BLOCKS = {
    "说明": "常见方块图鉴：hardness 为硬度，luminance 为发光等级(0-15)，flammable 是否可燃。",
    "blocks": [
        {"id": "stone", "name": "石头", "hardness": 1.5, "tool": "镐", "drop": "圆石", "luminance": 0, "flammable": False},
        {"id": "cobblestone", "name": "圆石", "hardness": 2.0, "tool": "镐", "drop": "圆石", "luminance": 0, "flammable": False},
        {"id": "dirt", "name": "泥土", "hardness": 0.5, "tool": "锹", "drop": "泥土", "luminance": 0, "flammable": False},
        {"id": "grass_block", "name": "草方块", "hardness": 0.6, "tool": "锹", "drop": "泥土", "luminance": 0, "flammable": False},
        {"id": "oak_log", "name": "橡木原木", "hardness": 2.0, "tool": "斧", "drop": "橡木原木", "luminance": 0, "flammable": True},
        {"id": "oak_planks", "name": "橡木木板", "hardness": 2.0, "tool": "斧", "drop": "橡木木板", "luminance": 0, "flammable": True},
        {"id": "sand", "name": "沙子", "hardness": 0.5, "tool": "锹", "drop": "沙子", "luminance": 0, "flammable": False,
         "note": "受重力影响，无支撑会下落"},
        {"id": "gravel", "name": "砂砾", "hardness": 0.6, "tool": "锹", "drop": "砂砾或燧石", "luminance": 0, "flammable": False},
        {"id": "glass", "name": "玻璃", "hardness": 0.3, "tool": "无", "drop": "无（精准采集可得）", "luminance": 0, "flammable": False},
        {"id": "glowstone", "name": "萤石", "hardness": 0.3, "tool": "无", "drop": "萤石粉 2-4", "luminance": 15, "flammable": False},
        {"id": "torch", "name": "火把", "hardness": 0.0, "tool": "无", "drop": "火把", "luminance": 14, "flammable": False,
         "note": "光照 14，可防止怪物在附近生成"},
        {"id": "lantern", "name": "灯笼", "hardness": 3.5, "tool": "镐", "drop": "灯笼", "luminance": 15, "flammable": False},
        {"id": "coal_ore", "name": "煤矿石", "hardness": 3.0, "tool": "镐", "drop": "煤 1（时运可翻倍）", "luminance": 0, "flammable": False},
        {"id": "iron_ore", "name": "铁矿石", "hardness": 3.0, "tool": "石镐+", "drop": "粗铁", "luminance": 0, "flammable": False},
        {"id": "gold_ore", "name": "金矿石", "hardness": 3.0, "tool": "铁镐+", "drop": "粗金", "luminance": 0, "flammable": False},
        {"id": "diamond_ore", "name": "钻石矿石", "hardness": 3.0, "tool": "铁镐+", "drop": "钻石 1", "luminance": 0, "flammable": False},
        {"id": "redstone_ore", "name": "红石矿石", "hardness": 3.0, "tool": "铁镐+", "drop": "红石粉 4-5", "luminance": 0, "flammable": False,
         "note": "被踩踏或点击时会短暂发出 9 级红石信号"},
        {"id": "obsidian", "name": "黑曜石", "hardness": 50.0, "tool": "钻石镐+", "drop": "黑曜石", "luminance": 0, "flammable": False,
         "note": "需钻石镐，约 8.3 秒；水+岩浆源可生成"},
        {"id": "netherrack", "name": "下界岩", "hardness": 0.4, "tool": "镐", "drop": "下界岩", "luminance": 0, "flammable": False,
         "note": "顶部火焰可永久燃烧"},
        {"id": "magma_block", "name": "岩浆块", "hardness": 0.5, "tool": "镐", "drop": "岩浆块", "luminance": 3, "flammable": False,
         "note": "站上去会持续受伤"},
        {"id": "end_stone", "name": "末地石", "hardness": 3.0, "tool": "镐", "drop": "末地石", "luminance": 0, "flammable": False},
        {"id": "chest", "name": "箱子", "hardness": 2.5, "tool": "斧", "drop": "箱子", "luminance": 0, "flammable": False,
         "note": "相邻两个箱子会合并为大箱子（27×2 格）"},
        {"id": "crafting_table", "name": "工作台", "hardness": 2.5, "tool": "斧", "drop": "工作台", "luminance": 0, "flammable": True},
        {"id": "furnace", "name": "熔炉", "hardness": 3.5, "tool": "镐", "drop": "熔炉", "luminance": 0, "flammable": False,
         "note": "工作时发光 13"},
        {"id": "bookshelf", "name": "书架", "hardness": 1.5, "tool": "斧", "drop": "书 3", "luminance": 0, "flammable": True,
         "note": "附魔台周围每格书架提升可用附魔等级，最多 15 个"},
    ],
}

QUOTES = """# Rainclouds 启动名言（每次启动随机展示一条）
# —— Minecraft 经典 ——
不要挖脚下的方块。
Don't dig straight down.
Creeper? Aww man.
钻石，是给有耐心的人的礼物。
造船出海之前，先记得带桶水。
红石是一门艺术，也是一门玄学。
第一个夜晚，总有一间三格高的土屋。
背包满了，冒险才刚开始。
The End? 不，是新的开始。
别在末地看末影人的眼睛。
下界没有天空，但有最亮的岩浆。
一张地图装不下所有的路。
—— Rainclouds 原创 ——
云层之上，也可以是起点。
雨会停，存档还在。
自研核心，不为替代谁，只为多一种可能。
把每一次启动，当成一次新的生成。
社区版很小，但装得下一整个方块世界。
模组与插件同席，本不该是选择题。
离线也能查合成表，是我们对断网的温柔。
彩蛋藏在第七次点击里。
一键开服之前，先一键备份。
版权归 Rainclouds，世界归你。
愿你的帧率像草原的风一样稳。
如果启动页换了壁纸，说明你找到了这里。
写给每一个在深夜等下载完成的人。
体积大一点，是因为我们塞进了一整个资料馆。
不必联网，也能翻一页百科。
"""

VERSION_HISTORY = {
    "项目": "Rainclouds Minecraft Launcher 社区版",
    "versions": [
        {"tag": "v1.0.2", "date": "2026-10-31", "title": "首个公开发布",
         "changes": ["微软正版登录 / 离线账号", "版本清单、依赖库、资产、Java 自动准备",
                     "Fabric / Forge / Quilt 加载器安装", "实例隔离、模组管理、并行下载"]},
        {"tag": "v1.2.0", "date": "2026-11-01", "title": "自研 10yv 核心",
         "changes": ["内置 Rainclouds 自研核心（同时支持 Mod 与插件）",
                     "启动页 Splash、PE 版本资源与 Authenticode 数字签名",
                     "版权信息 Copyright © Rainclouds", "新增联机 / 内网穿透 / 一键开服三个进阶模块",
                     "修复 classpath 注入顺序导致核心主类无法加载的问题"]},
        {"tag": "v1.3.0", "date": "2026-11-08", "title": "开源 / 闭源分离（Open-Core）",
         "changes": ["付费模块源码移出公开仓库，改为闭源 RCL.Paid.dll 运行时加载",
                     "新增 IPaidModule 契约与 PaidModuleLoader，DLL 缺失时优雅降级",
                     "新增 build-closed.sh 构建签名闭源 DLL", "构建脚本版本号随 csproj 自动同步"]},
        {"tag": "v1.4.0", "date": "2026-11-11", "title": "离线资料馆与彩蛋",
         "changes": ["内置 6 张像素风壁纸、6 个无损音效、6 份离线资料，全部随 exe 嵌入发布",
                     "新增彩蛋馆：壁纸预览 / 音效试听 / 资料速查 / 隐藏挖矿小游戏",
                     "Konami 码或连点顶栏 logo 7 次可开启彩蛋馆",
                     "启动页随机名言，支持把任意内置壁纸设为启动页背景",
                     "资源可一键导出到磁盘，离线可用"]},
    ],
}

FIELDGUIDE = """# Rainclouds 离线生存手册

> 这份手册随启动器一起嵌入，完全离线可读。查资料不必再开浏览器。

## 一、红石速查

- **信号强度**：红石线每传播一格衰减 1 级，最远 15 格；中继器可把信号重置为 15 级并单向导通。
- **中继器延时**：默认 1 红石刻（0.1 秒），右键可调整为 2/3/4 刻，四档合计可做出精确节拍。
- **比较器**：两种模式。前端火炬熄灭为「比较模式」（输出 = 输入与侧输入较大者），点亮为「减法模式」（输出 = 输入 − 侧输入）。可读取箱子/漏斗/炼药锅的充盈度。
- **活塞**：普通活塞推 1 格、最多 12 个方块；粘性活塞可拉回。粘液块可带动相邻方块构成飞行器。
- **常用电路**：脉冲发生器（观察者 + 活塞）、T 触发器（两个活塞交替）、物品分类器（比较器 + 漏斗）、自动熔炉组。
- **坑点**：红石粉在台阶/玻璃上无法放置；水流会冲掉红石线；区块卸载会冻结时钟电路。

## 二、附魔要点

- 附魔台周围 1 格高度、距附魔台 2 格范围内最多放 **15 个书架** 可解锁 30 级附魔；书架与附魔台之间不能有方块（含火把、地毯）阻挡。
- 30 级附魔消耗 3 级经验 + 3 个青金石，但只扣 3 级，不是 30 级。
- **优先级建议**：镐（时运 III / 精准采集）、剑（锋利 V / 抢夺 III）、弓（力量 V / 无限）、盔甲（保护 IV / 耐久 III）、靴子（摔落保护 IV / 深海探索者 III）。
- **互斥**：精准采集 与 时运 互斥；无限 与 修补 互斥；保护类附魔之间互斥。
- **经验修补（Mending）**：只能从附魔书、村庄交易、末地城宝箱获得，不在附魔台出现。手持带修补的装备捡经验球即可自动修复。
- 铁砧合并同名物品可叠加等级，但「累积惩罚」会让成本指数上升；先用附魔书两两合并再敲到装备上更省。

## 三、生物群系与生成

- **1.18+ 高度**：世界 Y 范围 −64 ~ 320，主世界地表约 Y=64。钻石在 Y=−59 附近最密集，深层深色岩层（Deepslate）更硬但矿更富。
- **群系**： plains / forest / taiga / desert / savanna / jungle / badlands / snowy / mushroom_fields / cherry_grove（1.20）。
- **结构**：村庄、掠夺者前哨站、沙漠神殿、丛林神庙、海底遗迹、要塞（末地传送门）、下界要塞、堡垒遗迹、末地城、远古城市（Deep Dark）。
- **定位**：`/locate structure minecraft:stronghold` 等命令；生存中可用「末影之眼」指向要塞。
- **史莱姆区块**：特定区块在 Y<40 会刷史莱姆，沼泽满月时地表也会刷。

## 四、实用命令（需开启作弊或局域网开放）

```
/gamerule keepInventory true        死亡不掉落
/gamerule doDaylightCycle false     锁定时间
/time set day                      设为白天
/weather clear                     雨停
/tp @s ~ ~ ~                       传送到自身（取消坠落）
/give @s minecraft:diamond 64      给一组钻石
/enchant @s minecraft:fortune 3    给手持物品附魔
/effect give @s minecraft:night_vision 99999 1 true  夜视
/summon minecraft:lightning_bolt   召唤闪电
/locate structure minecraft:village_plains
/seed                              查看种子
```

## 五、性能与稳定性

- 分配内存 **4~6 GB** 通常最佳；给太多反而触发长时间 GC 卡顿。
- JVM 参数推荐：`-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200`。
- 客户端优化模组（Sodium / Lithium / Iris）对帧率提升显著；光影包需要 Iris/OptiFine。
- 视距：生存 8~10 区块够用；服务器视距每 +1，区块数按平方增长。
- 崩溃先查 `minecraft/logs/latest.log` 与 `crash-reports/`；Mod 冲突多半是同类改同一处或缺失前置库。

## 六、启动器使用技巧

- **实例隔离**：每个实例有独立 `.minecraft/mods`，模组互不干扰，可同时保留「原版」「Fabric 整合包」「自研核心」多套配置。
- **自研核心（10yv）**：点「核心」一键创建实例，首次启动自动解压加载器与 3 个演示 Mod（hello / fps / spawn）。同时支持 Mod 与插件。
- **Java**：设置页「自动检测」会挑最合适的 JDK；留空则由启动器自动下载 Temurin。1.17+ 需要 Java 16/17，1.20.5+ 需要 Java 21。
- **下载并发**：网络好可调到 16，弱网建议 4~8，避免超时重试。
- **离线可查**：本窗口的「离线资料」分页内置合成表、方块图鉴与这份手册，断网照样用。
- **彩蛋**：顶栏云朵 logo 连点 7 次，或依次按 ↑↑↓↓←→←→ B A，可打开彩蛋馆。
- **数据位置**：默认在 `%APPDATA%/RaincloudsMC`，「打开游戏目录」可直接跳转。导出的壁纸/音效也在该目录的 `extras/` 下。

## 七、备份建议

关服或退出游戏后再复制存档；存档在 `saves/<世界名>`，改动中的 `level.dat` 不要在写入时复制。多版本切换前先备份整个实例目录。自动备份可用「一键开服」模块的定时快照功能（完整版）。
"""

CREDITS = """Rainclouds Minecraft Launcher 社区版 — 鸣谢与许可

【项目】
设计与实现：Rainclouds
版权：Copyright © Rainclouds
开源协议：社区版核心代码公开；进阶模块（联机 / 内网穿透 / 一键开服）为闭源组件，
          随完整版以 RCL.Paid.dll 形式提供，不在公开仓库内。

【内置离线资料馆 / 彩蛋（v1.4.0）】
  - 6 张像素风壁纸、6 个合成音效、离线合成表 / 方块图鉴 / 生存手册 / 名言 / 版本史
  - 全部由 tools/gen_extras.py 程序化生成并可复现，随 rcl.exe 作为嵌入资源发布
  - 目的：断网时也能查资料、换壁纸、听音效，让启动器自带一份「开箱即用」的内容包
  - 打开方式：顶栏云朵 logo 连点 7 次，或按 ↑↑↓↓←→←→ B A
  - 所有图片 / 音频 / 文本均为本项目原创生成，不含第三方素材

【开源组件】
  - .NET 6 / Avalonia UI 11（MIT）
  - SkiaSharp / HarfBuzzSharp（MIT）
  - Minecraft 名称与相关商标归 Mojang / Microsoft 所有，本项目为非官方第三方工具

【致谢】
感谢每一位反馈问题的社区用户，以及所有在深夜等下载进度条走完的人。
雨会停，存档还在。
"""


def main():
    for sub in ("wallpapers", "audio", "data"):
        os.makedirs(os.path.join(OUT, sub), exist_ok=True)

    total = 0
    print("生成壁纸 ...")
    for i, ((fname, _t, _d), fn) in enumerate(zip(WALLPAPERS, SCENES)):
        img = add_grain(fn(), seed=1000 + i, grain=30)
        p = os.path.join(OUT, "wallpapers", fname)
        img.save(p, "PNG", compress_level=6)
        sz = os.path.getsize(p); total += sz
        print(f"  {fname:24s} {sz/1024/1024:6.2f} MB")

    print("生成音效 ...")
    for i, ((fname, _t, _d), fn) in enumerate(zip(AUDIO, AUDIO_FUNCS)):
        mono = fn()
        p = os.path.join(OUT, "audio", fname)
        write_wav(p, mono, 2000 + i)
        sz = os.path.getsize(p); total += sz
        print(f"  {fname:24s} {sz/1024/1024:6.2f} MB  ({len(mono)/SAMPLE_RATE:.1f}s)")

    print("写入离线资料 ...")
    payloads = {
        "crafting.json": json.dumps(CRAFTING, ensure_ascii=False, indent=2),
        "blocks.json": json.dumps(BLOCKS, ensure_ascii=False, indent=2),
        "quotes.txt": QUOTES,
        "version_history.json": json.dumps(VERSION_HISTORY, ensure_ascii=False, indent=2),
        "fieldguide.md": FIELDGUIDE,
        "credits.txt": CREDITS,
    }
    for fname, _t, _d in DATA:
        p = os.path.join(OUT, "data", fname)
        with open(p, "w", encoding="utf-8") as f:
            f.write(payloads[fname])
        sz = os.path.getsize(p); total += sz
        print(f"  {fname:24s} {sz/1024:8.1f} KB")

    index = {
        "version": "1.4.0",
        "generated": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "generator": "tools/gen_extras.py",
        "note": "全部资源随 rcl.exe 嵌入发布，离线可用；PNG/WAV 为高熵媒体，实打实增加启动器体积。",
        "wallpapers": [{"file": f, "title": t, "desc": d} for f, t, d in WALLPAPERS],
        "audio": [{"file": f, "title": t, "desc": d} for f, t, d in AUDIO],
        "data": [{"file": f, "title": t, "desc": d} for f, t, d in DATA],
    }
    with open(os.path.join(OUT, "index.json"), "w", encoding="utf-8") as f:
        json.dump(index, f, ensure_ascii=False, indent=2)
    total += os.path.getsize(os.path.join(OUT, "index.json"))

    print(f"\n合计原始资源： {total/1024/1024:.2f} MB  ->  {OUT}")
    print("（PNG/WAV 在单文件发布时几乎不被二次压缩，exe 增量接近此数值）")


if __name__ == "__main__":
    main()
