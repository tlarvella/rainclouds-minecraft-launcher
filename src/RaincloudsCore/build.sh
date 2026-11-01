#!/usr/bin/env bash
# 编译 Rainclouds 自研核心（10yv 核心）：
#   - rainclouds-core.jar  : API + 加载器（主类 io.rainclouds.core.loader.RaincloudsLoader）
#   - mods/*.jar            : 各演示 Mod（含 rainclouds.mod.json）
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="$ROOT/dist"
CLASSES="$OUT/classes"
rm -rf "$OUT" "$CLASSES"
mkdir -p "$CLASSES" "$OUT/mods"

echo "==> 编译 API + 加载器"
find "$ROOT/api" "$ROOT/loader" -name "*.java" > "$OUT/sources.txt"
javac --release 17 -d "$CLASSES" @"$OUT/sources.txt"

echo "==> 打包 rainclouds-core.jar"
jar cfe "$OUT/rainclouds-core.jar" io.rainclouds.core.loader.RaincloudsLoader -C "$CLASSES" .

echo "==> 编译演示 Mod"
# 每个演示 Mod 的固定 jar 文件名（与启动器内嵌资源名保持一致）
declare -A MODJAR=(
  ["rainclouds-hello"]="rainclouds-hello.jar"
  ["rainclouds-fpsmonitor"]="rainclouds-fps.jar"
  ["rainclouds-spawninfo"]="rainclouds-spawn.jar"
)
for d in "$ROOT"/mods/*/; do
  [ -d "$d" ] || continue
  folder="$(basename "$d")"
  name="${MODJAR[$folder]:-$folder.jar}"
  mclasses="$OUT/modclasses/$folder"
  mkdir -p "$mclasses"
  find "$d/src" -name "*.java" > "$OUT/modsrc.txt"
  javac --release 17 -cp "$CLASSES" -d "$mclasses" @"$OUT/modsrc.txt"
  cp "$d/rainclouds.mod.json" "$mclasses/rainclouds.mod.json"
  ( cd "$mclasses" && jar cf "$OUT/mods/$name" . )
  echo "    -> mods/$name"
done

echo "==> 完成，产物位于 $OUT"
ls -la "$OUT" "$OUT/mods"
