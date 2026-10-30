# Configure downloads by release URL and verify SHA-256 before extraction.
include(FetchContent)
if(POLICY CMP0135)
    cmake_policy(SET CMP0135 NEW)
endif()
FetchContent_Declare(BoostHeaders
    URL https://archives.boost.io/release/1.86.0/source/boost_1_86_0.tar.gz
    URL_HASH SHA256=2575e74ffc3ef1cd0babac2c1ee8bdb5782a0ee672b1912da40e5b4b591ca01f
    SOURCE_SUBDIR ProjectCHeaderOnly)
# SOURCE_SUBDIR intentionally has no CMakeLists: use only the release header tree.
FetchContent_MakeAvailable(BoostHeaders)
FetchContent_Declare(Json
    URL https://github.com/nlohmann/json/releases/download/v3.11.3/json.tar.xz
    URL_HASH SHA256=d6c65aca6b1ed68e7a182f4757257b107ae403032760ed6ef121c9d55e81757d)
FetchContent_MakeAvailable(Json)
find_package(Threads REQUIRED)
add_library(ProjectCNetwork Network/Protocol.cpp Network/CardCatalog.cpp Network/MatchSession.cpp Network/MatchManager.cpp Network/WebSocketServer.cpp)
target_include_directories(ProjectCNetwork PUBLIC Network)
target_compile_definitions(ProjectCNetwork PRIVATE PROJECTC_DATA_DIR="${CMAKE_CURRENT_SOURCE_DIR}/Data")
target_include_directories(ProjectCNetwork SYSTEM PRIVATE ${boostheaders_SOURCE_DIR})
target_compile_definitions(ProjectCNetwork PRIVATE BOOST_ALL_NO_LIB BOOST_ERROR_CODE_HEADER_ONLY)
target_link_libraries(ProjectCNetwork PUBLIC ProjectCGameCore nlohmann_json::nlohmann_json PRIVATE Threads::Threads)
if(WIN32)
    target_link_libraries(ProjectCNetwork PRIVATE ws2_32 mswsock bcrypt)
    target_compile_definitions(ProjectCNetwork PRIVATE _WIN32_WINNT=0x0A00)
endif()
add_executable(ProjectCNetworkServer Network/main.cpp)
target_link_libraries(ProjectCNetworkServer PRIVATE ProjectCNetwork)
add_custom_command(TARGET ProjectCNetworkServer POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_directory ${CMAKE_CURRENT_SOURCE_DIR}/Data $<TARGET_FILE_DIR:ProjectCNetworkServer>/Data)
add_executable(ProjectCNetworkTests Tests/NetworkTests.cpp)
target_link_libraries(ProjectCNetworkTests PRIVATE ProjectCNetwork)
add_test(NAME NetworkTests COMMAND ProjectCNetworkTests)
find_package(Python3 REQUIRED COMPONENTS Interpreter)
add_test(NAME WebSocketIntegration COMMAND ${Python3_EXECUTABLE}
    ${CMAKE_CURRENT_SOURCE_DIR}/Tests/WebSocketIntegration.py $<TARGET_FILE:ProjectCNetworkServer>)
set_tests_properties(NetworkTests WebSocketIntegration PROPERTIES TIMEOUT 45)
