
cmake_minimum_required(VERSION 3.20)

project(ProjectCServer LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_library(ProjectCGameCore STATIC
    Source/GameState.cpp
)

target_include_directories(ProjectCGameCore
    PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/Include
)

add_executable(ProjectCServer
    Source/Main.cpp
)

target_link_libraries(ProjectCServer
    PRIVATE ProjectCGameCore
)

enable_testing()

add_executable(ProjectCTests
    Tests/GameStateTests.cpp
)

target_link_libraries(ProjectCTests
    PRIVATE ProjectCGameCore
)

add_test(
    NAME GameStateTests
    COMMAND ProjectCTests
)
