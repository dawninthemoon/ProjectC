FROM ubuntu:24.04 AS build
RUN apt-get update \
    && apt-get install -y --no-install-recommends build-essential cmake ninja-build ca-certificates python3 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /ProjectC
COPY CMakeLists.txt Network.cmake ./
COPY Include/ Include/
COPY Source/ Source/
COPY Network/ Network/
COPY Data/ Data/
COPY Tests/ Tests/
RUN cmake -S . -B Build -G Ninja -DCMAKE_BUILD_TYPE=Release \
    && cmake --build Build --parallel 2 \
    && ctest --test-dir Build --output-on-failure
FROM ubuntu:24.04 AS runtime
WORKDIR /ProjectC
COPY --from=build /ProjectC/Build/ProjectCNetworkServer /ProjectC/ProjectCNetworkServer
COPY --from=build /ProjectC/Data /ProjectC/Data
RUN useradd --create-home --uid 10001 ProjectCUser
USER ProjectCUser
EXPOSE 7777
STOPSIGNAL SIGTERM
ENTRYPOINT ["./ProjectCNetworkServer"]
CMD ["--port", "7777"]
