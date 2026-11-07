#!/usr/bin/env bash
# 跨平台发布脚本（在 Windows 上请用 build.bat）。
# 本脚本用于在 Linux/macOS 交叉发布 Windows 版 rcl.exe（社区版）。
set -e
cd "$(dirname "$0")"

VERSION="1.3.0.0"
PUBLISH_OUT="dist"
EXE="$PUBLISH_OUT/rcl.exe"

# 1) 发布单文件自包含 win-x64 可执行程序
dotnet publish src/RCL.UI/RCL.UI.csproj -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:PublishTrimmed=false -o "$PUBLISH_OUT"

# 2) 由于在非 Windows 平台无法由 MSBuild 自定义 PE 子系统，
#    这里把子系统从 CONSOLE(3) 改为 GUI(2)，避免启动时弹出黑色控制台窗口。
python3 - "$PWD/$EXE" <<'PY'
import struct, sys
p = sys.argv[1]
b = bytearray(open(p, "rb").read())
lfanew = struct.unpack_from("<I", b, 0x3C)[0]
off = lfanew + 0x5C
if struct.unpack_from("<H", b, off)[0] != 2:
    struct.pack_into("<H", b, off, 2)
    open(p, "wb").write(b)
    print("已将 rcl.exe 子系统改为 GUI")
else:
    print("rcl.exe 已是 GUI 子系统")
PY

# 3) 注入 VS_VERSION_INFO 版本资源（版权、产品名、版本号）。
#    因为交叉编译时 dotnet 无法自定义 apphost 版本资源（NETSDK1074），
#    这里用 pefile 重建 .rsrc 段。
python3 tools/pe_set_version.py "$PWD/$EXE" --filever "$VERSION" --prodver "$VERSION"

# 4) 生成自签名证书（仅演示用），并用 osslsigncode 进行 Authenticode 数字签名。
#    证书只在第一次构建时生成，之后复用，保证签名一致。
CERT_DIR="tools/cert"
CERT_PFX="$CERT_DIR/rcl.pfx"
mkdir -p "$CERT_DIR"
if [ ! -f "$CERT_PFX" ]; then
  echo "生成自签名证书 Rainclouds ..."
  openssl req -x509 -newkey rsa:2048 -nodes \
    -keyout "$CERT_DIR/rcl.key" -out "$CERT_DIR/rcl.crt" -days 3650 \
    -subj "/CN=Rainclouds/O=Rainclouds/C=CN" 2>/dev/null
  openssl pkcs12 -export -out "$CERT_PFX" \
    -inkey "$CERT_DIR/rcl.key" -in "$CERT_DIR/rcl.crt" \
    -passout pass:rainclouds 2>/dev/null
fi
if command -v osslsigncode >/dev/null 2>&1; then
  TMP_SIGNED="$PUBLISH_OUT/rcl-signed.exe"
  osslsigncode sign -pkcs12 "$CERT_PFX" -pass rainclouds \
    -n "Rainclouds Minecraft Launcher" \
    -i "https://rainclouds.example" \
    -in "$EXE" -out "$TMP_SIGNED"
  mv -f "$TMP_SIGNED" "$EXE"
  echo "已使用自签名证书进行 Authenticode 数字签名"
else
  echo "警告: 未找到 osslsigncode，跳过数字签名（属性数字签名将不可用）"
fi

echo "完成: $EXE"
