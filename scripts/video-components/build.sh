#!/usr/bin/env bash
# 仅构建 BGRA 管道输入、颜色转换和指定格式的视频编码；源码与配方随组件发布。
set -euo pipefail
export LC_ALL=C SOURCE_DATE_EPOCH=1790726400
jobs=${JOBS:-4}
mkdir -p /work/sources /work/src /work/out
fetch() {
    local name=$1 url=$2 hash=$3
    test -f "/work/sources/$name.tar.gz" || curl -fL --retry 3 "$url" -o "/work/sources/$name.tar.gz"
    echo "$hash  /work/sources/$name.tar.gz" | sha256sum -c -
    mkdir -p "/work/src/$name"
    if ! test -f "/work/src/$name/.source-$hash"; then
        tar -xzf "/work/sources/$name.tar.gz" --strip-components=1 -C "/work/src/$name"
        touch "/work/src/$name/.source-$hash"
    fi
}
fetch ffmpeg https://codeload.github.com/FFmpeg/FFmpeg/tar.gz/29e619e767cde9045a75c29bc9a8278ae7b3a98b 8e4699bcb6a311772e8f513cc72ce27328891b82a598b82e7cff3dfe580875a0
fetch x264 https://codeload.github.com/mirror/x264/tar.gz/31e19f92f00c7003fa115047ce50978bc98c3a0d d053c9d86988d6bc78237ca5205865c5ddf99c98ef4cd9927eec8f6d388f6dd9
fetch libvpx https://codeload.github.com/webmproject/libvpx/tar.gz/v1.15.2 26fcd3db88045dee380e581862a6ef106f49b74b6396ee95c2993a260b4636aa
export CC=x86_64-w64-mingw32-gcc-posix CXX=x86_64-w64-mingw32-g++-posix
export AR=x86_64-w64-mingw32-ar RANLIB=x86_64-w64-mingw32-ranlib STRIP=x86_64-w64-mingw32-strip
prefix=/work/prefix
mkdir -p "$prefix"
if ! test -f "$prefix/lib/libx264.a"; then
    cd /work/src/x264
    ./configure --host=x86_64-w64-mingw32 --cross-prefix=x86_64-w64-mingw32- --prefix="$prefix" \
        --enable-static --disable-cli --disable-opencl --disable-avs --disable-lavf --disable-swscale \
        --bit-depth=8 --chroma-format=420
    make -j"$jobs" && make install
fi
if ! test -f "$prefix/lib/libvpx.a"; then
    cd /work/src/libvpx
    CROSS=x86_64-w64-mingw32- ./configure --target=x86_64-win64-gcc --prefix="$prefix" \
        --disable-examples --disable-tools --disable-docs --disable-unit-tests --disable-vp8 \
        --enable-vp9 --disable-vp9-decoder --disable-vp9-highbitdepth --disable-shared --enable-static
    make -j"$jobs" && make install
fi
for format in ${FORMATS:-mp4 webm}; do
    mkdir -p "/work/build-$format" "/work/package-$format/bin" "/work/package-$format/licenses"
    cd "/work/build-$format"
    extra=(--enable-libx264 --enable-encoder=libx264 --enable-muxer=mp4)
    if test "$format" = webm; then extra=(--enable-libvpx --enable-encoder=libvpx_vp9 --enable-muxer=webm --enable-bsf=vp9_superframe); fi
    PKG_CONFIG_LIBDIR="$prefix/lib/pkgconfig" /work/src/ffmpeg/configure \
        --prefix="$prefix" --target-os=mingw32 --arch=x86_64 --cross-prefix=x86_64-w64-mingw32- \
        --cc="$CC" --cxx="$CXX" --pkg-config=pkg-config --pkg-config-flags=--static \
        --disable-autodetect --disable-everything --disable-network --disable-doc --disable-debug \
        --disable-ffplay --disable-ffprobe --disable-avdevice --disable-shared --enable-static \
        --enable-ffmpeg --enable-gpl --enable-version3 --enable-small \
        --enable-protocol=file,pipe --enable-demuxer=rawvideo --enable-decoder=rawvideo \
        --enable-filter=scale,format --enable-swscale --extra-ldflags=-static "${extra[@]}"
    make -j"$jobs" ffmpeg.exe
    "$STRIP" ffmpeg.exe
    cp ffmpeg.exe "/work/package-$format/bin/ffmpeg.exe"
    cp /work/src/ffmpeg/COPYING.GPLv3 "/work/package-$format/LICENSE.txt"
    cp /work/src/ffmpeg/LICENSE.md "/work/package-$format/licenses/FFmpeg.txt"
    cp config.h "/work/package-$format/licenses/ffmpeg-config.h"
    cp config_components.h "/work/package-$format/licenses/ffmpeg-components.h"
    if test "$format" = mp4; then cp /work/src/x264/COPYING "/work/package-$format/licenses/x264.txt";
    else cp /work/src/libvpx/LICENSE /work/src/libvpx/PATENTS "/work/package-$format/licenses/"; fi
    printf 'FFmpeg 29e619e767cde9045a75c29bc9a8278ae7b3a98b\nGPL-3.0-or-later\nCorresponding source and build recipe: FrameCapture-video-sources-1.tar.gz alongside this archive.\nhttps://github.com/NekoWs/FrameCapture/releases/download/v0.9.33/FrameCapture-video-sources-1.tar.gz\n' > "/work/package-$format/SOURCE.txt"
done
cd /work
for format in mp4 webm; do
    cp /usr/share/doc/mingw-w64-common/copyright "package-$format/licenses/MinGW-w64.txt"
    cp /usr/share/doc/gcc-mingw-w64-x86-64-posix-runtime/copyright "package-$format/licenses/GCC-runtime.txt"
done
tar --sort=name --mtime="@$SOURCE_DATE_EPOCH" --owner=0 --group=0 --numeric-owner -czf out/FrameCapture-video-sources-1.tar.gz \
    -C /work sources -C /recipe build.sh Dockerfile
python3 - <<'PY'
import pathlib,zipfile,hashlib,json
out=pathlib.Path('/work/out')
for kind in ['mp4','webm']:
    root=pathlib.Path('/work/package-'+kind)
    name='FrameCapture-video-'+kind+'-1-win-x64'
    archive=out/(name+'.zip')
    with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for file in sorted(root.rglob('*')):
            if not file.is_file(): continue
            entry=zipfile.ZipInfo(name+'/'+file.relative_to(root).as_posix(),(2026,9,30,0,0,0))
            entry.compress_type=zipfile.ZIP_DEFLATED
            entry.external_attr=0o100644<<16
            z.writestr(entry,file.read_bytes(),compresslevel=9)
    print(json.dumps({'format':kind,'file':archive.name,'bytes':archive.stat().st_size,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest()}))
PY
