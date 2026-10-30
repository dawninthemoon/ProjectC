
cmake_minimum_required(VERSION 3.20)

project(ProjectCServer LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_library(ProjectCGameCore STATIC
    Source/CardInstance.cpp
    Source/PlayerState.cpp
    Source/GameState.cpp
    Source/GameCommand.cpp
    Source/Zone.cpp
    Source/GameInitializer.cpp
    Source/GameFlow.cpp
    Source/GameReplay.cpp
    Source/GameOperations.cpp
    Source/TargetSelection.cpp
    Source/CardEffects.cpp
    Source/CardMutations.cpp
    Source/TriggeredEffects.cpp
)

target_include_directories(ProjectCGameCore
    PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/Include
)

add_executable(ProjectCServer
    Source/main.cpp
)

target_link_libraries(ProjectCServer
    PRIVATE ProjectCGameCore
)

enable_testing()

add_executable(ProjectCTests
    Tests/GameStateTests.cpp
    Tests/ZoneTests.cpp
    Tests/GameFlowTests.cpp
    Tests/GameReplayTests.cpp
    Tests/GameIntegrationTests.cpp
    Tests/CardRulesTests.cpp
    Tests/EffectTests.cpp
)

target_link_libraries(ProjectCTests
    PRIVATE ProjectCGameCore
)

add_test(
    NAME GameStateTests
    COMMAND ProjectCTests
)
include(Network.cmake)
