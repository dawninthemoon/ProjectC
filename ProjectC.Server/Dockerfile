
# Build Stage
FROM ubuntu:24.04 AS Build

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        build-essential \
        cmake \
        ninja-build \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /ProjectC

COPY CMakeLists.txt ./
COPY Include/ Include/
COPY Source/ Source/
COPY Tests/ Tests/

RUN cmake -S . -B Build \
        -G Ninja \
        -DCMAKE_BUILD_TYPE=Release \
    && cmake --build Build \
    && ctest --test-dir Build --output-on-failure

# Runtime Stage
FROM ubuntu:24.04 AS Runtime

WORKDIR /ProjectC

COPY --from=Build \
    /ProjectC/Build/ProjectCServer \
    /ProjectC/ProjectCServer

RUN useradd --create-home --uid 10001 ProjectCUser

USER ProjectCUser

CMD ["./ProjectCServer"]
