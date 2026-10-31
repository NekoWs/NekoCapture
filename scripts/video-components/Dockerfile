FROM debian:bookworm-slim@sha256:99c0b25c46d0214d5a2091092e7908907dcaecd6a962cde0ac700acee222b6f3
RUN apt-get update && apt-get install -y --no-install-recommends \
    gcc-mingw-w64-x86-64-posix g++-mingw-w64-x86-64-posix make nasm pkg-config python3 ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*
RUN apt-get update && apt-get install -y --no-install-recommends gcc libc6-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /work
ENTRYPOINT ["bash", "/recipe/build.sh"]
