FROM ubuntu:24.04 AS build
RUN apt-get update \
    && apt-get install -y --no-install-recommends build-essential cmake ninja-build ca-certificates python3 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /ProjectC/ProjectC.Server
COPY ProjectC.Server/CMakeLists.txt ProjectC.Server/Network.cmake ./
COPY ProjectC.Server/Include/ Include/
COPY ProjectC.Server/Source/ Source/
COPY ProjectC.Server/Network/ Network/
COPY ProjectC.Server/Data/ Data/
COPY ProjectC.Server/Tests/ Tests/
# CMake tests refer to files one level above the server source directory.
COPY Tools/ /ProjectC/Tools/
COPY ProjectC.Client/Assets/Resources/ /ProjectC/ProjectC.Client/Assets/Resources/
RUN cmake -S . -B Build -G Ninja -DCMAKE_BUILD_TYPE=Release \
    && cmake --build Build --parallel 2 \
    && ctest --test-dir Build --output-on-failure
FROM ubuntu:24.04 AS runtime
WORKDIR /ProjectC
COPY --from=build /ProjectC/ProjectC.Server/Build/ProjectCNetworkServer /ProjectC/ProjectCNetworkServer
COPY --from=build /ProjectC/ProjectC.Server/Data /ProjectC/Data
RUN useradd --create-home --uid 10001 ProjectCUser
USER ProjectCUser
EXPOSE 7777
STOPSIGNAL SIGTERM
ENTRYPOINT ["./ProjectCNetworkServer"]
CMD ["--port", "7777"]
