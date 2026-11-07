#include "ExplorerCommand.h"
#include <shlobj.h>
#include <algorithm>
#include <cwctype>
#include <fstream>
#include <sstream>
#include <map>

namespace
{
    struct MenuCacheConfig
    {
        bool enabled = true;
        std::map<std::wstring, std::vector<std::wstring>> favoritesByCategory;
    };

    std::wstring ToLower(std::wstring input)
    {
        std::transform(input.begin(), input.end(), input.begin(),
            [](wchar_t c) { return static_cast<wchar_t>(std::towlower(c)); });
        return input;
    }

    std::wstring ToUpper(std::wstring input)
    {
        std::transform(input.begin(), input.end(), input.begin(),
            [](wchar_t c) { return static_cast<wchar_t>(std::towupper(c)); });
        return input;
    }

    std::wstring Utf8ToWide(const std::string& str)
    {
        if (str.empty()) return L"";
        int count = MultiByteToWideChar(CP_UTF8, 0, str.data(), static_cast<int>(str.size()), nullptr, 0);
        if (count <= 0) return L"";
        std::wstring result(static_cast<size_t>(count), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, str.data(), static_cast<int>(str.size()), result.data(), count);
        return result;
    }

    std::string WideToUtf8(const std::wstring& wstr)
    {
        if (wstr.empty()) return "";
        int count = WideCharToMultiByte(CP_UTF8, 0, wstr.data(), static_cast<int>(wstr.size()), nullptr, 0, nullptr, nullptr);
        if (count <= 0) return "";
        std::string result(static_cast<size_t>(count), '\0');
        WideCharToMultiByte(CP_UTF8, 0, wstr.data(), static_cast<int>(wstr.size()), result.data(), count, nullptr, nullptr);
        return result;
    }

    std::wstring GetFileShiftLocalAppDataDir()
    {
        wchar_t buf[32768] = {};
        DWORD len = GetEnvironmentVariableW(L"LOCALAPPDATA", buf, 32768);
        if (len > 0 && len < 32768)
        {
            std::wstring dir(buf, len);
            if (!dir.empty() && dir.back() != L'\\') dir.push_back(L'\\');
            dir += L"FileShift";
            return dir;
        }
        return L"";
    }

    void AppendShellErrorLog(const std::wstring& message)
    {
        std::wstring dir = GetFileShiftLocalAppDataDir();
        if (dir.empty()) return;
        CreateDirectoryW(dir.c_str(), nullptr);
        std::wstring logPath = dir + L"\\shell-errors.log";

        HANDLE hFile = CreateFileW(
            logPath.c_str(),
            FILE_APPEND_DATA,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr,
            OPEN_ALWAYS,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);

        if (hFile != INVALID_HANDLE_VALUE)
        {
            std::string utf8Line = WideToUtf8(message + L"\r\n");
            DWORD written = 0;
            WriteFile(hFile, utf8Line.data(), static_cast<DWORD>(utf8Line.size()), &written, nullptr);
            CloseHandle(hFile);
        }
    }

    std::vector<std::wstring> SplitCommaList(const std::wstring& csv)
    {
        std::vector<std::wstring> items;
        std::wstringstream ss(csv);
        std::wstring token;
        while (std::getline(ss, token, L','))
        {
            size_t start = token.find_first_not_of(L" \t\r\n\"");
            size_t end = token.find_last_not_of(L" \t\r\n\"");
            if (start != std::wstring::npos && end != std::wstring::npos)
            {
                items.push_back(ToLower(token.substr(start, end - start + 1)));
            }
        }
        return items;
    }

    MenuCacheConfig LoadMenuCacheConfig()
    {
        MenuCacheConfig cfg;
        cfg.favoritesByCategory[L"image"] = { L"webp", L"jpeg", L"png", L"avif", L"pdf" };
        cfg.favoritesByCategory[L"svg"] = { L"png", L"webp", L"jpeg", L"pdf" };
        cfg.favoritesByCategory[L"video"] = { L"mp4", L"mov", L"webm", L"gif", L"mp3" };
        cfg.favoritesByCategory[L"audio"] = { L"mp3", L"wav", L"flac", L"m4a", L"opus" };
        cfg.favoritesByCategory[L"pdf"] = { L"png", L"jpeg" };
        cfg.favoritesByCategory[L"document"] = { L"pdf" };

        std::wstring dir = GetFileShiftLocalAppDataDir();
        if (dir.empty()) return cfg;

        std::wstring cachePath = dir + L"\\menu-cache.json";
        HANDLE hFile = CreateFileW(
            cachePath.c_str(),
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            nullptr);

        if (hFile == INVALID_HANDLE_VALUE) return cfg;

        char buffer[8192] = {};
        DWORD bytesRead = 0;
        BOOL ok = ReadFile(hFile, buffer, sizeof(buffer) - 1, &bytesRead, nullptr);
        CloseHandle(hFile);
        if (!ok || bytesRead == 0) return cfg;

        std::wstring content = Utf8ToWide(std::string(buffer, bytesRead));
        std::wstringstream ss(content);
        std::wstring line;
        while (std::getline(ss, line))
        {
            if (!line.empty() && line.back() == L'\r') line.pop_back();
            if (line == L"ENABLED=0" || line.find(L"\"enabled\": false") != std::wstring::npos)
            {
                cfg.enabled = false;
            }
            else if (line.rfind(L"FAV_", 0) == 0)
            {
                size_t eqPos = line.find(L'=');
                if (eqPos != std::wstring::npos)
                {
                    std::wstring cat = ToLower(line.substr(4, eqPos - 4));
                    auto parsed = SplitCommaList(line.substr(eqPos + 1));
                    if (!parsed.empty())
                    {
                        cfg.favoritesByCategory[cat] = parsed;
                    }
                }
            }
        }

        return cfg;
    }

    // Properly quotes and escapes a Win32 argument for CommandLineToArgvW without shell injection
    std::wstring QuoteWin32Arg(const std::wstring& arg)
    {
        if (!arg.empty() && arg.find_first_of(L" \t\n\v\"") == std::wstring::npos)
        {
            return arg;
        }

        std::wstring quoted = L"\"";
        for (auto it = arg.begin(); ; ++it)
        {
            unsigned numBackslashes = 0;
            while (it != arg.end() && *it == L'\\')
            {
                ++it;
                ++numBackslashes;
            }

            if (it == arg.end())
            {
                quoted.append(numBackslashes * 2, L'\\');
                break;
            }
            else if (*it == L'"')
            {
                quoted.append(numBackslashes * 2 + 1, L'\\');
                quoted.push_back(*it);
            }
            else
            {
                quoted.append(numBackslashes, L'\\');
                quoted.push_back(*it);
            }
        }
        quoted.push_back(L'"');
        return quoted;
    }

    std::wstring ClassifyExtensionCategory(const std::wstring& ext)
    {
        if (ext == L".png" || ext == L".jpg" || ext == L".jpeg" || ext == L".webp" ||
            ext == L".avif" || ext == L".bmp" || ext == L".gif" || ext == L".tiff" ||
            ext == L".ico" || ext == L".heic" || ext == L".tga" || ext == L".ppm" ||
            ext == L".qoi" || ext == L".exr")
        {
            return L"image";
        }
        if (ext == L".svg") return L"svg";
        if (ext == L".mp4" || ext == L".mkv" || ext == L".mov" || ext == L".webm" || ext == L".avi")
        {
            return L"video";
        }
        if (ext == L".mp3" || ext == L".wav" || ext == L".flac" || ext == L".aac" ||
            ext == L".m4a" || ext == L".ogg" || ext == L".opus")
        {
            return L"audio";
        }
        if (ext == L".pdf") return L"pdf";
        if (ext == L".docx" || ext == L".odt" || ext == L".xlsx" || ext == L".pptx")
        {
            return L"document";
        }
        return L"";
    }

    std::vector<std::wstring> GetAllSupportedTargetsForCategory(const std::wstring& category)
    {
        if (category == L"image")
        {
            return { L"webp", L"jpeg", L"png", L"avif", L"gif", L"tiff", L"bmp", L"ico", L"tga", L"ppm", L"qoi", L"exr", L"pdf" };
        }
        if (category == L"svg")
        {
            return { L"png", L"webp", L"jpeg", L"pdf" };
        }
        if (category == L"video")
        {
            return { L"mp4", L"mov", L"webm", L"mkv", L"avi", L"gif", L"mp3", L"wav", L"flac", L"aac", L"m4a", L"ogg", L"opus" };
        }
        if (category == L"audio")
        {
            return { L"mp3", L"wav", L"flac", L"aac", L"m4a", L"ogg", L"opus" };
        }
        if (category == L"pdf")
        {
            return { L"png", L"jpeg" };
        }
        if (category == L"document")
        {
            return { L"pdf" };
        }
        return {};
    }

    std::wstring FormatDisplayTitle(const std::wstring& category, const std::wstring& fmt)
    {
        if (category == L"video" &&
            (fmt == L"mp3" || fmt == L"wav" || fmt == L"flac" || fmt == L"aac" || fmt == L"m4a" || fmt == L"ogg" || fmt == L"opus"))
        {
            return L"Extraer audio " + ToUpper(fmt);
        }
        return L"Convertir a " + ToUpper(fmt);
    }

    bool InspectSelectionCategory(IShellItemArray* psiItemArray, std::wstring& outCategory, std::wstring& outSingleExt)
    {
        outCategory.clear();
        outSingleExt.clear();
        if (!psiItemArray) return false;

        DWORD count = 0;
        if (FAILED(psiItemArray->GetCount(&count)) || count == 0)
            return false;

        for (DWORD i = 0; i < count; ++i)
        {
            IShellItem* psi = nullptr;
            if (FAILED(psiItemArray->GetItemAt(i, &psi)) || !psi)
                return false;

            PWSTR pszName = nullptr;
            HRESULT hr = psi->GetDisplayName(SIGDN_FILESYSPATH, &pszName);
            psi->Release();

            if (FAILED(hr) || !pszName)
                return false;

            LPCWSTR extPtr = PathFindExtensionW(pszName);
            std::wstring ext = extPtr ? ToLower(extPtr) : L"";
            CoTaskMemFree(pszName);

            std::wstring category = ClassifyExtensionCategory(ext);
            if (category.empty())
                return false;

            if (i == 0)
            {
                outCategory = category;
                outSingleExt = ext;
            }
            else
            {
                if (category != outCategory)
                    return false;
                if (ext != outSingleExt)
                    outSingleExt.clear();
            }
        }

        return !outCategory.empty();
    }

    std::vector<TargetMenuEntry> BuildSubMenuForCategory(
        const std::wstring& category,
        const std::wstring& singleExt,
        const MenuCacheConfig& cfg)
    {
        std::vector<TargetMenuEntry> entries;
        auto allSupported = GetAllSupportedTargetsForCategory(category);
        if (allSupported.empty()) return entries;

        std::wstring lookupCat = (category == L"pdf") ? L"document" : category;
        std::vector<std::wstring> favorites;
        auto it = cfg.favoritesByCategory.find(lookupCat);
        if (it != cfg.favoritesByCategory.end())
        {
            favorites = it->second;
        }

        auto isSameAsSource = [&](const std::wstring& fmt)
        {
            std::wstring dotFmt = L"." + fmt;
            return (dotFmt == singleExt) || (fmt == L"jpeg" && singleExt == L".jpg");
        };

        std::vector<std::wstring> addedFormats;

        // 1. Add user-configured favorite formats first (if valid for this category)
        for (const auto& fav : favorites)
        {
            if (isSameAsSource(fav)) continue;
            if (std::find(allSupported.begin(), allSupported.end(), fav) != allSupported.end() &&
                std::find(addedFormats.begin(), addedFormats.end(), fav) == addedFormats.end())
            {
                entries.push_back({ fav, FormatDisplayTitle(category, fav), category, false, {} });
                addedFormats.push_back(fav);
            }
        }

        // 2. Group remaining supported formats into cascading submenu "Más formatos…"
        std::vector<TargetMenuEntry> remainingEntries;
        for (const auto& fmt : allSupported)
        {
            if (isSameAsSource(fmt)) continue;
            if (std::find(addedFormats.begin(), addedFormats.end(), fmt) == addedFormats.end())
            {
                remainingEntries.push_back({ fmt, FormatDisplayTitle(category, fmt), category, false, {} });
            }
        }

        // Always allow opening the full FileShift desktop app with the selected files
        remainingEntries.push_back({ L"", L"Abrir en FileShift…", category, true, {} });

        if (entries.empty())
        {
            // If all favorites matched source extension, promote remaining entries to top level
            entries = std::move(remainingEntries);
        }
        else
        {
            entries.push_back({ L"", L"Más formatos…", category, true, std::move(remainingEntries) });
        }

        return entries;
    }

    std::wstring ResolveExecutableAdjacentToDll(const wchar_t* exeName)
    {
        std::vector<wchar_t> dllBuffer(32768, L'\0');
        DWORD len = GetModuleFileNameW(g_hModule, dllBuffer.data(), static_cast<DWORD>(dllBuffer.size() - 1));
        if (len == 0 || len >= dllBuffer.size() - 1)
        {
            return exeName;
        }

        std::wstring fullPath(dllBuffer.data(), len);
        size_t lastSlash = fullPath.find_last_of(L"\\/");
        if (lastSlash != std::wstring::npos)
        {
            return fullPath.substr(0, lastSlash + 1) + exeName;
        }
        return exeName;
    }
}

// ============================================================================
// SubExplorerCommand
// ============================================================================

SubExplorerCommand::SubExplorerCommand(TargetMenuEntry entry)
    : m_entry(std::move(entry))
{
    ++g_dllRefCount;
}

IFACEMETHODIMP SubExplorerCommand::QueryInterface(REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    if (riid == IID_IUnknown || riid == IID_IExplorerCommand)
    {
        *ppv = static_cast<IExplorerCommand*>(this);
        AddRef();
        return S_OK;
    }
    *ppv = nullptr;
    return E_NOINTERFACE;
}

IFACEMETHODIMP_(ULONG) SubExplorerCommand::AddRef()
{
    return ++m_refCount;
}

IFACEMETHODIMP_(ULONG) SubExplorerCommand::Release()
{
    ULONG count = --m_refCount;
    if (count == 0)
    {
        delete this;
        --g_dllRefCount;
    }
    return count;
}

IFACEMETHODIMP SubExplorerCommand::GetTitle(IShellItemArray*, LPWSTR* ppszName)
{
    return SHStrDupW(m_entry.title.c_str(), ppszName);
}

IFACEMETHODIMP SubExplorerCommand::GetIcon(IShellItemArray*, LPWSTR* ppszIcon)
{
    *ppszIcon = nullptr;
    return E_NOTIMPL;
}

IFACEMETHODIMP SubExplorerCommand::GetToolTip(IShellItemArray*, LPWSTR* ppszInfotip)
{
    *ppszInfotip = nullptr;
    return E_NOTIMPL;
}

IFACEMETHODIMP SubExplorerCommand::GetCanonicalName(GUID* pguidCommandName)
{
    *pguidCommandName = GUID_NULL;
    return E_NOTIMPL;
}

IFACEMETHODIMP SubExplorerCommand::GetState(IShellItemArray* psiItemArray, BOOL, EXPCMDSTATE* pCmdState)
{
    if (!pCmdState) return E_POINTER;

    if (psiItemArray && !m_entry.requiredCategory.empty())
    {
        std::wstring cat;
        std::wstring singleExt;
        if (!InspectSelectionCategory(psiItemArray, cat, singleExt) || cat != m_entry.requiredCategory)
        {
            *pCmdState = ECS_HIDDEN;
            return S_OK;
        }
    }

    *pCmdState = ECS_ENABLED;
    return S_OK;
}

IFACEMETHODIMP SubExplorerCommand::GetFlags(EXPCMDFLAGS* pFlags)
{
    if (!pFlags) return E_POINTER;
    *pFlags = m_entry.children.empty() ? ECF_DEFAULT : ECF_HASSUBCOMMANDS;
    return S_OK;
}

IFACEMETHODIMP SubExplorerCommand::EnumSubCommands(IEnumExplorerCommand** ppEnum)
{
    if (!ppEnum) return E_POINTER;
    if (m_entry.children.empty())
    {
        *ppEnum = nullptr;
        return E_NOTIMPL;
    }
    *ppEnum = new EnumExplorerCommand(m_entry.children);
    return S_OK;
}

IFACEMETHODIMP SubExplorerCommand::Invoke(IShellItemArray* psiItemArray, IBindCtx*)
{
    if (!psiItemArray) return E_INVALIDARG;

    DWORD count = 0;
    if (FAILED(psiItemArray->GetCount(&count)) || count == 0)
        return S_OK;

    std::vector<std::wstring> selectedPaths;
    selectedPaths.reserve(count);

    for (DWORD i = 0; i < count; ++i)
    {
        IShellItem* psi = nullptr;
        if (SUCCEEDED(psiItemArray->GetItemAt(i, &psi)) && psi)
        {
            PWSTR pszFilePath = nullptr;
            if (SUCCEEDED(psi->GetDisplayName(SIGDN_FILESYSPATH, &pszFilePath)) && pszFilePath)
            {
                selectedPaths.emplace_back(pszFilePath);
                CoTaskMemFree(pszFilePath);
            }
            psi->Release();
        }
    }

    if (selectedPaths.empty()) return S_OK;

    std::wstring exePath = ResolveExecutableAdjacentToDll(
        m_entry.isOpenMoreDialog ? L"FileShift.App.exe" : L"fileshift.exe");

    std::wstring basePrefix = QuoteWin32Arg(exePath);
    if (!m_entry.isOpenMoreDialog)
    {
        basePrefix += L" convert --to " + QuoteWin32Arg(m_entry.formatId) + L" --";
    }

    // Chunk command lines safely below Win32 CreateProcessW 32,767 wchar_t limit
    constexpr size_t MaxCmdLineChars = 24000;
    size_t idx = 0;

    while (idx < selectedPaths.size())
    {
        std::wstring cmdLine = basePrefix;
        size_t batchCount = 0;

        while (idx < selectedPaths.size())
        {
            std::wstring quotedPath = L" " + QuoteWin32Arg(selectedPaths[idx]);
            if (batchCount > 0 && (cmdLine.size() + quotedPath.size() > MaxCmdLineChars))
            {
                break;
            }
            cmdLine += quotedPath;
            ++idx;
            ++batchCount;
        }

        STARTUPINFOW si{};
        si.cb = sizeof(si);
        PROCESS_INFORMATION pi{};

        std::vector<wchar_t> mutableCmd(cmdLine.begin(), cmdLine.end());
        mutableCmd.push_back(L'\0');

        DWORD creationFlags = m_entry.isOpenMoreDialog ? 0 : CREATE_NO_WINDOW;
        if (CreateProcessW(
                exePath.c_str(),
                mutableCmd.data(),
                nullptr,
                nullptr,
                FALSE,
                creationFlags,
                nullptr,
                nullptr,
                &si,
                &pi))
        {
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
        }
        else
        {
            DWORD err = GetLastError();
            AppendShellErrorLog(L"[FileShift.Shell] Error CreateProcessW (" + std::to_wstring(err) + L") ejecutando: " + exePath);
            return HRESULT_FROM_WIN32(err);
        }
    }

    return S_OK;
}

// ============================================================================
// EnumExplorerCommand
// ============================================================================

EnumExplorerCommand::EnumExplorerCommand(const std::vector<TargetMenuEntry>& entries)
    : m_entries(entries)
{
    ++g_dllRefCount;
}

IFACEMETHODIMP EnumExplorerCommand::QueryInterface(REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    if (riid == IID_IUnknown || riid == IID_IEnumExplorerCommand)
    {
        *ppv = static_cast<IEnumExplorerCommand*>(this);
        AddRef();
        return S_OK;
    }
    *ppv = nullptr;
    return E_NOINTERFACE;
}

IFACEMETHODIMP_(ULONG) EnumExplorerCommand::AddRef()
{
    return ++m_refCount;
}

IFACEMETHODIMP_(ULONG) EnumExplorerCommand::Release()
{
    ULONG count = --m_refCount;
    if (count == 0)
    {
        delete this;
        --g_dllRefCount;
    }
    return count;
}

IFACEMETHODIMP EnumExplorerCommand::Next(ULONG celt, IExplorerCommand** pUICommand, ULONG* pceltFetched)
{
    if (!pUICommand) return E_POINTER;

    ULONG fetched = 0;
    while (fetched < celt && m_currentIndex < m_entries.size())
    {
        pUICommand[fetched] = new SubExplorerCommand(m_entries[m_currentIndex]);
        ++m_currentIndex;
        ++fetched;
    }

    if (pceltFetched)
    {
        *pceltFetched = fetched;
    }

    return (fetched == celt) ? S_OK : S_FALSE;
}

IFACEMETHODIMP EnumExplorerCommand::Skip(ULONG celt)
{
    m_currentIndex = std::min(m_entries.size(), m_currentIndex + static_cast<size_t>(celt));
    return (m_currentIndex < m_entries.size()) ? S_OK : S_FALSE;
}

IFACEMETHODIMP EnumExplorerCommand::Reset()
{
    m_currentIndex = 0;
    return S_OK;
}

IFACEMETHODIMP EnumExplorerCommand::Clone(IEnumExplorerCommand** ppenum)
{
    if (!ppenum) return E_POINTER;
    auto* cloned = new EnumExplorerCommand(m_entries);
    cloned->m_currentIndex = m_currentIndex;
    *ppenum = cloned;
    return S_OK;
}

// ============================================================================
// RootExplorerCommand
// ============================================================================

RootExplorerCommand::RootExplorerCommand()
{
    ++g_dllRefCount;
}

IFACEMETHODIMP RootExplorerCommand::QueryInterface(REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    if (riid == IID_IUnknown || riid == IID_IExplorerCommand)
    {
        *ppv = static_cast<IExplorerCommand*>(this);
        AddRef();
        return S_OK;
    }
    *ppv = nullptr;
    return E_NOINTERFACE;
}

IFACEMETHODIMP_(ULONG) RootExplorerCommand::AddRef()
{
    return ++m_refCount;
}

IFACEMETHODIMP_(ULONG) RootExplorerCommand::Release()
{
    ULONG count = --m_refCount;
    if (count == 0)
    {
        delete this;
        --g_dllRefCount;
    }
    return count;
}

IFACEMETHODIMP RootExplorerCommand::GetTitle(IShellItemArray*, LPWSTR* ppszName)
{
    return SHStrDupW(L"Convertir a…", ppszName);
}

IFACEMETHODIMP RootExplorerCommand::GetIcon(IShellItemArray*, LPWSTR* ppszIcon)
{
    std::wstring exePath = ResolveExecutableAdjacentToDll(L"FileShift.App.exe");
    exePath += L",0";
    return SHStrDupW(exePath.c_str(), ppszIcon);
}

IFACEMETHODIMP RootExplorerCommand::GetToolTip(IShellItemArray*, LPWSTR* ppszInfotip)
{
    return SHStrDupW(L"Convertir archivos seleccionados con FileShift (local y sin restricciones)", ppszInfotip);
}

IFACEMETHODIMP RootExplorerCommand::GetCanonicalName(GUID* pguidCommandName)
{
    if (!pguidCommandName) return E_POINTER;
    *pguidCommandName = CLSID_FileShiftExplorerCommand;
    return S_OK;
}

IFACEMETHODIMP RootExplorerCommand::GetState(IShellItemArray* psiItemArray, BOOL, EXPCMDSTATE* pCmdState)
{
    if (!pCmdState) return E_POINTER;
    *pCmdState = ECS_HIDDEN;

    std::lock_guard<std::mutex> lock(m_mutex);
    m_cachedSubCommands.clear();

    MenuCacheConfig cfg = LoadMenuCacheConfig();
    if (!cfg.enabled)
    {
        return S_OK;
    }

    std::wstring commonCategory;
    std::wstring singleExt;
    if (!InspectSelectionCategory(psiItemArray, commonCategory, singleExt))
    {
        return S_OK;
    }

    m_cachedSubCommands = BuildSubMenuForCategory(commonCategory, singleExt, cfg);
    if (!m_cachedSubCommands.empty())
    {
        *pCmdState = ECS_ENABLED;
    }

    return S_OK;
}

IFACEMETHODIMP RootExplorerCommand::Invoke(IShellItemArray*, IBindCtx*)
{
    return S_OK;
}

IFACEMETHODIMP RootExplorerCommand::GetFlags(EXPCMDFLAGS* pFlags)
{
    if (!pFlags) return E_POINTER;
    *pFlags = ECF_HASSUBCOMMANDS;
    return S_OK;
}

IFACEMETHODIMP RootExplorerCommand::EnumSubCommands(IEnumExplorerCommand** ppEnum)
{
    if (!ppEnum) return E_POINTER;
    std::lock_guard<std::mutex> lock(m_mutex);

    if (m_cachedSubCommands.empty())
    {
        // Fallback if Explorer calls EnumSubCommands before GetState
        MenuCacheConfig cfg = LoadMenuCacheConfig();
        if (cfg.enabled)
        {
            for (const wchar_t* cat : { L"image", L"video", L"audio", L"pdf", L"document" })
            {
                auto catEntries = BuildSubMenuForCategory(cat, L"", cfg);
                m_cachedSubCommands.insert(m_cachedSubCommands.end(), catEntries.begin(), catEntries.end());
            }
        }
    }

    *ppEnum = new EnumExplorerCommand(m_cachedSubCommands);
    return S_OK;
}
