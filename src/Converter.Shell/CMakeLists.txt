cmake_minimum_required(VERSION 3.22)
project(FileShiftShell LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(FileShift.Shell SHARED
    ExplorerCommand.h
    ExplorerCommand.cpp
    dllmain.cpp
    FileShift.Shell.def
)

target_compile_definitions(FileShift.Shell PRIVATE
    UNICODE
    _UNICODE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
)

target_link_libraries(FileShift.Shell PRIVATE
    shlwapi
    shell32
    ole32
    uuid
    advapi32
)

if(MSVC)
    set_property(TARGET FileShift.Shell PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    target_compile_options(FileShift.Shell PRIVATE /utf-8)
endif()
