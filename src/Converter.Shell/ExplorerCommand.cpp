#include "ExplorerCommand.h"
#include <shlobj.h>
#include <algorithm>
#include <cwctype>
#include <fstream>
#include <sstream>
#include <map>
#include <new>

namespace
{
    struct MenuCacheConfig
    {
        bool enabled = true;
        std::map<std::wstring, std::vector<std::wstring>> favoritesByCategory;
        std::map<std::wstring, std::pair<std::wstring, std::vector<std::wstring>>> formats;

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

    std::wstring ReadLocalCache(const std::wstring& path)
    {
        HANDLE file = CreateFileW(path.c_str(), GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return {};
        char buffer[16384] = {};
        DWORD length = 0;
        BOOL ok = ReadFile(file, buffer, sizeof(buffer), &length, nullptr);
        CloseHandle(file);
        return ok && length < sizeof(buffer) ? Utf8ToWide(std::string(buffer, length)) : L"";
    }

    MenuCacheConfig LoadMenuCacheConfig()
    {
        MenuCacheConfig cfg;
        std::wstring dir = GetFileShiftLocalAppDataDir();
        if (dir.empty()) return cfg;

        std::wstringstream ss(ReadLocalCache(dir + L"\\menu-cache.json"));
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
                    cfg.favoritesByCategory[cat] = SplitCommaList(line.substr(eqPos + 1));
                }
            }
        }

        std::wstringstream matrix(ReadLocalCache(dir + L"\\menu-formats.txt"));
        while (std::getline(matrix, line))
        {
            const auto equal = line.find(L'=');
            const auto colon = line.find(L':');
            if (equal == std::wstring::npos || colon == std::wstring::npos || equal >= colon) continue;
            cfg.formats.emplace(line.substr(0, equal), std::make_pair(
                line.substr(equal + 1, colon - equal - 1), SplitCommaList(line.substr(colon + 1))));
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

    std::wstring FormatDisplayTitle(const std::wstring& category, const std::wstring& fmt)
    {
        if (category == L"video" &&
            (fmt == L"mp3" || fmt == L"wav" || fmt == L"flac" || fmt == L"aac" || fmt == L"m4a" || fmt == L"ogg" || fmt == L"opus"))
        {
            return L"Extraer audio " + ToUpper(fmt);
        }
        return L"Convertir a " + ToUpper(fmt);
    }

    bool InspectSelection(IShellItemArray* items, const MenuCacheConfig& config,
        std::wstring& category, std::vector<std::wstring>& targets)
    {
        DWORD count = 0;
        if (!items || FAILED(items->GetCount(&count)) || count == 0) return false;

        for (DWORD index = 0; index < count; ++index)
        {
            IShellItem* item = nullptr;
            if (FAILED(items->GetItemAt(index, &item)) || !item) return false;
            PWSTR path = nullptr;
            const HRESULT hr = item->GetDisplayName(SIGDN_FILESYSPATH, &path);
            item->Release();
            if (FAILED(hr) || !path) return false;
            std::wstring name(path);
            CoTaskMemFree(path);

            const auto dot = name.find_last_of(L'.');
            const auto slash = name.find_last_of(L"\\/");
            const auto ext = dot != std::wstring::npos && (slash == std::wstring::npos || dot > slash)
                ? ToLower(name.substr(dot + 1)) : L"";
            const auto format = config.formats.find(ext);
            if (format == config.formats.end()) return false;
            const auto& [sourceCategory, sourceTargets] = format->second;
            if (index == 0)
            {
                category = sourceCategory;
                targets = sourceTargets;
            }
            else
            {
                if (category != sourceCategory) category.clear();
                targets.erase(std::remove_if(targets.begin(), targets.end(), [&](const auto& target)
                {
                    return std::find(sourceTargets.begin(), sourceTargets.end(), target) == sourceTargets.end();
                }), targets.end());
            }
        }
        return true;
    }

    std::vector<TargetMenuEntry> BuildSubMenu(
        const std::wstring& category, const std::vector<std::wstring>& allSupported, const MenuCacheConfig& cfg)
    {
        std::vector<TargetMenuEntry> entries;
        const auto it = cfg.favoritesByCategory.find(category);
        const auto favorites = it == cfg.favoritesByCategory.end() ? std::vector<std::wstring>{} : it->second;

        auto addFormat = [&](const std::wstring& format)
        {
            if (std::none_of(entries.begin(), entries.end(), [&](const auto& entry) { return entry.formatId == format; }))
                entries.push_back({ format, FormatDisplayTitle(category, format) });
        };
        for (const auto& favorite : favorites)
            if (std::find(allSupported.begin(), allSupported.end(), favorite) != allSupported.end())
                addFormat(favorite);
        for (const auto& format : allSupported)
            addFormat(format);

        entries.push_back({ L"", L"Abrir en FileShift…", true });
        return entries;
    }

    std::wstring ResolveExecutableAdjacentToDll(const wchar_t* exeName)
    {
        std::vector<wchar_t> dllBuffer(32768, L'\0');
        DWORD len = GetModuleFileNameW(g_hModule, dllBuffer.data(), static_cast<DWORD>(dllBuffer.size() - 1));
        if (len == 0 || len >= dllBuffer.size() - 1)
        {
            return {};
        }

        std::wstring fullPath(dllBuffer.data(), len);
        size_t lastSlash = fullPath.find_last_of(L"\\/");
        if (lastSlash != std::wstring::npos)
        {
            return fullPath.substr(0, lastSlash + 1) + exeName;
        }
        return {};
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
    if (!ppszInfotip) return E_POINTER;
    *ppszInfotip = nullptr;
    return E_NOTIMPL;
}

IFACEMETHODIMP SubExplorerCommand::GetCanonicalName(GUID* pguidCommandName)
{
    if (!pguidCommandName) return E_POINTER;
    *pguidCommandName = GUID_NULL;
    return E_NOTIMPL;
}

IFACEMETHODIMP SubExplorerCommand::GetState(IShellItemArray* psiItemArray, BOOL, EXPCMDSTATE* pCmdState) try
{
    if (!pCmdState) return E_POINTER;

    if (psiItemArray && !m_entry.formatId.empty())
    {
        const auto config = LoadMenuCacheConfig();
        std::wstring category;
        std::vector<std::wstring> targets;
        if (!config.enabled || !InspectSelection(psiItemArray, config, category, targets) ||
            std::find(targets.begin(), targets.end(), m_entry.formatId) == targets.end())
        {
            *pCmdState = ECS_HIDDEN;
            return S_OK;
        }
    }

    *pCmdState = ECS_ENABLED;
    return S_OK;
}
catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
catch (...) { return E_FAIL; }

IFACEMETHODIMP SubExplorerCommand::GetFlags(EXPCMDFLAGS* pFlags)
{
    if (!pFlags) return E_POINTER;
    *pFlags = ECF_DEFAULT;
    return S_OK;
}

IFACEMETHODIMP SubExplorerCommand::EnumSubCommands(IEnumExplorerCommand** ppEnum)
{
    if (!ppEnum) return E_POINTER;
    *ppEnum = nullptr;
    return E_NOTIMPL;
}

IFACEMETHODIMP SubExplorerCommand::Invoke(IShellItemArray* psiItemArray, IBindCtx*) try
{
    if (!psiItemArray) return E_INVALIDARG;
    EXPCMDSTATE state;
    if (FAILED(GetState(psiItemArray, FALSE, &state)) || state != ECS_ENABLED)
        return E_INVALIDARG;

    DWORD count = 0;
    if (FAILED(psiItemArray->GetCount(&count)) || count == 0)
        return S_OK;

    std::vector<std::wstring> selectedPaths;
    selectedPaths.reserve(count);

    for (DWORD i = 0; i < count; ++i)
    {
        IShellItem* item = nullptr;
        HRESULT hr = psiItemArray->GetItemAt(i, &item);
        if (FAILED(hr)) return hr;
        PWSTR path = nullptr;
        hr = item->GetDisplayName(SIGDN_FILESYSPATH, &path);
        item->Release();
        if (FAILED(hr)) return hr;
        if (!path) return E_INVALIDARG;
        selectedPaths.emplace_back(path);
        CoTaskMemFree(path);
    }

    std::wstring exePath = ResolveExecutableAdjacentToDll(
        m_entry.isOpenMoreDialog ? L"FileShift.App.exe" : L"fileshift.exe");

    if (exePath.empty()) return E_FAIL;
    std::wstring basePrefix = QuoteWin32Arg(exePath);
    if (!m_entry.isOpenMoreDialog)
    {
        basePrefix += L" convert --explorer --to " + QuoteWin32Arg(m_entry.formatId) + L" --";
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
            if (cmdLine.size() + quotedPath.size() >= 32767)
                return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
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
catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
catch (...) { return E_FAIL; }

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
    if (!pUICommand || (!pceltFetched && celt != 1)) return E_POINTER;

    ULONG fetched = 0;
    while (fetched < celt && m_currentIndex < m_entries.size())
    {
        try { pUICommand[fetched] = new SubExplorerCommand(m_entries[m_currentIndex]); }
        catch (const std::bad_alloc&)
        {
            m_currentIndex -= fetched;
            while (fetched > 0) pUICommand[--fetched]->Release();
            if (pceltFetched) *pceltFetched = 0;
            return E_OUTOFMEMORY;
        }
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
    const auto skipped = std::min(m_entries.size() - m_currentIndex, static_cast<size_t>(celt));
    m_currentIndex += skipped;
    return skipped == celt ? S_OK : S_FALSE;
}

IFACEMETHODIMP EnumExplorerCommand::Reset()
{
    m_currentIndex = 0;
    return S_OK;
}

IFACEMETHODIMP EnumExplorerCommand::Clone(IEnumExplorerCommand** ppenum)
{
    if (!ppenum) return E_POINTER;
    *ppenum = nullptr;
    EnumExplorerCommand* cloned;
    try { cloned = new EnumExplorerCommand(m_entries); }
    catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
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

IFACEMETHODIMP RootExplorerCommand::GetState(IShellItemArray* psiItemArray, BOOL, EXPCMDSTATE* pCmdState) try
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

    std::wstring category;
    std::vector<std::wstring> targets;
    if (!InspectSelection(psiItemArray, cfg, category, targets))
        return S_OK;

    m_cachedSubCommands = BuildSubMenu(category, targets, cfg);
    if (!m_cachedSubCommands.empty())
    {
        *pCmdState = ECS_ENABLED;
    }

    return S_OK;
}
catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
catch (...) { return E_FAIL; }

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

    *ppEnum = nullptr;
    if (m_cachedSubCommands.empty()) return E_NOTIMPL;

    try { *ppEnum = new EnumExplorerCommand(m_cachedSubCommands); }
    catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
    return S_OK;
}
