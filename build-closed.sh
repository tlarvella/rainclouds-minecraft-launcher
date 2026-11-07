#!/usr/bin/env bash
# 闭源进阶模块构建脚本（仅本地交付用，产物绝不提交到公开仓库）。
# 产出 dist-paid/RCL.Paid.dll（已 Authenticode 签名），与 dist/rcl.exe 同目录即可启用。
set -e
cd "$(dirname "$0")"

OUT="dist-paid"
mkdir -p "$OUT"

# 1) 构建 RCL.Paid（Release，net6.0 托管程序集）
dotnet build src/RCL.Paid/RCL.Paid.csproj -c Release -f net6.0 -o "$OUT"

DLL="$OUT/RCL.Paid.dll"
# 只保留托管程序集；其运行依赖（Avalonia / RCL.Core / RCL.UI）已由自包含 rcl.exe 捆绑提供。
find "$OUT" -maxdepth 1 -type f ! -name 'RCL.Paid.dll' -delete 2>/dev/null || true

# 2) 生成自签名证书（与 build.sh 共用，首次生成后复用）
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

# 3) 对 RCL.Paid.dll 进行 Authenticode 数字签名
if command -v osslsigncode >/dev/null 2>&1; then
  TMP="$OUT/RCL.Paid.signed.dll"
  osslsigncode sign -pkcs12 "$CERT_PFX" -pass rainclouds \
    -n "Rainclouds Paid Modules" \
    -i "https://rainclouds.example" \
    -in "$DLL" -out "$TMP"
  mv -f "$TMP" "$DLL"
  echo "已对 RCL.Paid.dll 进行 Authenticode 数字签名"
else
  echo "警告: 未找到 osslsigncode，跳过数字签名"
fi

echo "完成: $DLL"
echo "将该 DLL 与 rcl.exe 放在同一目录，并在本机激活 RCM- 激活码后启用进阶模块。"
