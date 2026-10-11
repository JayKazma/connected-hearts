#!/bin/sh
# Cross-build the mod binaries (connected_hearts.dll, the game-folder loader winmm.dll, the relay, the runtime) with clang.
# chctl (the developer test tool) is no longer built or shipped here.
# usage: tools/inject/build.sh <kh2-multiplayer source dir> [build dir]
set -e
SRC=${1:?source dir}; BUILD=${2:-$SRC/build-clang}
HERE=$(cd "$(dirname "$0")" && pwd)
clang -target x86_64-w64-mingw32 -O2 -c "$HERE/ucrtshim.c" -o "$HERE/ucrtshim.o"
cmake -S "$SRC" -B "$BUILD" -G Ninja -DCMAKE_BUILD_TYPE=Release \
      -DCMAKE_TOOLCHAIN_FILE="$HERE/clang-mingw-toolchain.cmake" -DKH2COOP_PORTABLE_PACKAGE=ON
cmake --build "$BUILD" --target kh2coop_inject ch_loader kh2coop_server kh2coop_runtime_scaffold
rm -rf "$BUILD/out"; mkdir -p "$BUILD/out"
for f in inject/staging/connected_hearts.dll loader/winmm.dll connected_hearts_runtime.exe connected_hearts_relay.exe; do
    [ -f "$BUILD/$f" ] && cp "$BUILD/$f" "$BUILD/out/"
done
x86_64-w64-mingw32-strip "$BUILD"/out/*.exe "$BUILD"/out/*.dll
ls -la "$BUILD/out"
