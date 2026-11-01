#pragma once
#include <windows.h>
#include <shlobj.h>
#include <mutex>
#include <string>
#include <cstdio>
#include <cwchar>
#include <memory>
#include <new>

namespace diagnostics {
inline std::mutex logMutex;
inline std::wstring logPath, markerPath, closedPath;
inline bool testMode = false;
inline bool active = false;

inline void write(const std::string& message) noexcept {
    try {
        std::lock_guard lock(logMutex);
        if (!active) return;
        HANDLE file = CreateFileW(logPath.c_str(), GENERIC_WRITE | GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
        LARGE_INTEGER size{};
        if (GetFileSizeEx(file, &size) && size.QuadPart > 256 * 1024) {
            // Keep recent breadcrumbs after long sessions instead of ceasing
            // logging when the file reaches its size cap.
            constexpr DWORD tailSize = 128 * 1024;
            std::unique_ptr<char[]> tail(new (std::nothrow) char[tailSize]);
            if (!tail) { CloseHandle(file); return; }
            LARGE_INTEGER offset{}; offset.QuadPart = -static_cast<LONGLONG>(tailSize);
            SetFilePointerEx(file, offset, nullptr, FILE_END);
            DWORD got{}; ReadFile(file, tail.get(), tailSize, &got, nullptr);
            DWORD start = 0; while (start < got && tail[start] != '\n') ++start; if (start < got) ++start;
            offset.QuadPart = 0; SetFilePointerEx(file, offset, nullptr, FILE_BEGIN);
            DWORD kept{}; WriteFile(file, tail.get() + start, got - start, &kept, nullptr); SetEndOfFile(file);
        }
        LARGE_INTEGER end{}; SetFilePointerEx(file, end, nullptr, FILE_END);
        SYSTEMTIME utc{}; GetSystemTime(&utc);
        char prefix[64]{};
        snprintf(prefix, sizeof(prefix), "%04u-%02u-%02uT%02u:%02u:%02u.%03uZ ", utc.wYear, utc.wMonth, utc.wDay, utc.wHour, utc.wMinute, utc.wSecond, utc.wMilliseconds);
        // Callers pass fixed event descriptions only, never invites or keys.
        std::string line = prefix + message + "\r\n";
        DWORD written{}; WriteFile(file, line.data(), static_cast<DWORD>(line.size()), &written, nullptr);
        FlushFileBuffers(file); CloseHandle(file);
    } catch (...) { /* Diagnostics must never bring down the game. */ }
}

inline void start() noexcept {
    try {
        wchar_t base[MAX_PATH + 1]{};
        DWORD testLength = testMode ? GetEnvironmentVariableW(L"NEONLINK_TEST_LOG_ROOT", base, MAX_PATH) : 0;
        if (testLength == 0 || testLength >= MAX_PATH) {
            if (FAILED(SHGetFolderPathW(nullptr, CSIDL_LOCAL_APPDATA | CSIDL_FLAG_CREATE, nullptr, 0, base))) return;
            std::wstring app = std::wstring(base) + L"\\NeonLink"; CreateDirectoryW(app.c_str(), nullptr);
            std::wstring root = app + L"\\runtime-logs"; CreateDirectoryW(root.c_str(), nullptr);
            wcscpy_s(base, MAX_PATH + 1, root.c_str());
        } else CreateDirectoryW(base, nullptr);
        FILETIME created{}, exited{}, kernel{}, user{};
        if (!GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user)) return;
        ULARGE_INTEGER time{}; time.LowPart = created.dwLowDateTime; time.HighPart = created.dwHighDateTime;
        wchar_t stem[128]{}; swprintf(stem, 128, L"\\runtime-%lu-%016llx", GetCurrentProcessId(), static_cast<unsigned long long>(time.QuadPart));
        std::wstring path = std::wstring(base) + stem;
        logPath = path + L".log"; markerPath = path + L".active"; closedPath = path + L".closed";
        DeleteFileW(closedPath.c_str());
        HANDLE marker = CreateFileW(markerPath.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (marker != INVALID_HANDLE_VALUE) {
            std::string content = std::to_string(GetCurrentProcessId()) + "\n" + std::to_string(time.QuadPart) + "\n0.7.0\n";
            DWORD written{}; WriteFile(marker, content.data(), static_cast<DWORD>(content.size()), &written, nullptr); FlushFileBuffers(marker); CloseHandle(marker);
        }
        active = true;
        write("NeonLink 0.7.0 native runtime starting. Gameplay replication unavailable.");
    } catch (...) { }
}
inline void close() noexcept {
    write("Native runtime unloaded cleanly.");
    try {
        std::lock_guard lock(logMutex);
        if (!active) return;
        HANDLE closed = CreateFileW(closedPath.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (closed != INVALID_HANDLE_VALUE) { FlushFileBuffers(closed); CloseHandle(closed); DeleteFileW(markerPath.c_str()); }
        active = false;
    } catch (...) { }
}
} // namespace diagnostics
