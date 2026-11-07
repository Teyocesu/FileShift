#include "ExplorerCommand.h"
#include <shlobj.h>
#include <algorithm>
#include <cwctype>
#include <fstream>
#include <sstream>

namespace
{
    std::wstring ToLower(std::wstring input)
    {
        std::transform(input.begin(), input.end(), input.begin(),
            [](wchar_t c) { return static_cast<wchar_t>(std::towlower(c)); });
        return input;
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
            ext == L".ico" || ext == L".heic")
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

    std::vector<TargetMenuEntry> BuildSubMenuForCategory(const std::wstring& category, const std::wstring& singleExt)
    {
        std::vector<TargetMenuEntry> entries;

        auto addTarget = [&](const std::wstring& fmt, const std::wstring& label)
        {
            std::wstring dotFmt = L"." + fmt;
            if (dotFmt == singleExt || (fmt == L"jpeg" && singleExt == L".jpg"))
                return;
            entries.push_back({ fmt, label, false });
        };

        if (category == L"image")
        {
            addTarget(L"jpeg", L"Convertir a JPEG");
            addTarget(L"png", L"Convertir a PNG");
            addTarget(L"webp", L"Convertir a WebP");
            addTarget(L"avif", L"Convertir a AVIF");
            addTarget(L"bmp", L"Convertir a BMP");
            addTarget(L"tiff", L"Convertir a TIFF");
            addTarget(L"pdf", L"Convertir a PDF");
        }
        else if (category == L"svg")
        {
            addTarget(L"png", L"Convertir a PNG");
            addTarget(L"jpeg", L"Convertir a JPEG");
            addTarget(L"webp", L"Convertir a WebP");
            addTarget(L"pdf", L"Convertir a PDF");
        }
        else if (category == L"video")
        {
            addTarget(L"mp4", L"Convertir a MP4");
            addTarget(L"mov", L"Convertir a MOV");
            addTarget(L"webm", L"Convertir a WebM");
            addTarget(L"gif", L"Convertir a GIF");
            addTarget(L"mp3", L"Extraer MP3");
            addTarget(L"wav", L"Extraer WAV");
        }
        else if (category == L"audio")
        {
            addTarget(L"mp3", L"Convertir a MP3");
            addTarget(L"m4a", L"Convertir a M4A");
            addTarget(L"wav", L"Convertir a WAV");
            addTarget(L"flac", L"Convertir a FLAC");
            addTarget(L"opus", L"Convertir a Opus");
        }
        else if (category == L"pdf")
        {
            addTarget(L"png", L"Convertir a PNG");
            addTarget(L"jpeg", L"Convertir a JPEG");
        }
        else if (category == L"document")
        {
            addTarget(L"pdf", L"Convertir a PDF");
        }

        entries.push_back({ L"", L"Más formatos…", true });
        return entries;
    }

    std::wstring ResolveExecutableAdjacentToDll(const wchar_t* exeName)
    {
        wchar_t dllPath[MAX_PATH] = {};
        if (GetModuleFileNameW(g_hModule, dllPath, MAX_PATH) == 0)
        {
            return exeName;
        }
        PathRemoveFileSpecW(dllPath);
        wchar_t fullExe[MAX_PATH] = {};
        PathCombineW(fullExe, dllPath, exeName);
        return fullExe;
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

IFACEMETHODIMP SubExplorerCommand::GetState(IShellItemArray*, BOOL, EXPCMDSTATE* pCmdState)
{
    *pCmdState = ECS_ENABLED;
    return S_OK;
}

IFACEMETHODIMP SubExplorerCommand::GetFlags(EXPCMDFLAGS* pFlags)
{
    *pFlags = ECF_DEFAULT;
    return S_OK;
}

IFACEMETHODIMP SubExplorerCommand::EnumSubCommands(IEnumExplorerCommand** ppEnum)
{
    *ppEnum = nullptr;
    return E_NOTIMPL;
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

    std::wstring cmdLine = QuoteWin32Arg(exePath);
    if (!m_entry.isOpenMoreDialog)
    {
        cmdLine += L" convert --to " + QuoteWin32Arg(m_entry.formatId) + L" --";
    }

    for (const auto& path : selectedPaths)
    {
        cmdLine += L" " + QuoteWin32Arg(path);
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
    *pguidCommandName = CLSID_FileShiftExplorerCommand;
    return S_OK;
}

IFACEMETHODIMP RootExplorerCommand::GetState(IShellItemArray* psiItemArray, BOOL, EXPCMDSTATE* pCmdState)
{
    *pCmdState = ECS_HIDDEN;
    m_cachedSubCommands.clear();

    if (!psiItemArray) return S_OK;

    DWORD count = 0;
    if (FAILED(psiItemArray->GetCount(&count)) || count == 0)
        return S_OK;

    std::wstring commonCategory;
    std::wstring singleExt;

    for (DWORD i = 0; i < count; ++i)
    {
        IShellItem* psi = nullptr;
        if (FAILED(psiItemArray->GetItemAt(i, &psi)) || !psi)
            return S_OK;

        PWSTR pszName = nullptr;
        HRESULT hr = psi->GetDisplayName(SIGDN_FILESYSPATH, &pszName);
        psi->Release();

        if (FAILED(hr) || !pszName)
            return S_OK;

        LPCWSTR extPtr = PathFindExtensionW(pszName);
        std::wstring ext = extPtr ? ToLower(extPtr) : L"";
        CoTaskMemFree(pszName);

        std::wstring category = ClassifyExtensionCategory(ext);
        if (category.empty())
            return S_OK;

        if (i == 0)
        {
            commonCategory = category;
            singleExt = ext;
        }
        else
        {
            if (category != commonCategory)
                return S_OK;
            if (ext != singleExt)
                singleExt.clear();
        }
    }

    m_cachedSubCommands = BuildSubMenuForCategory(commonCategory, singleExt);
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
    *pFlags = ECF_HASSUBCOMMANDS;
    return S_OK;
}

IFACEMETHODIMP RootExplorerCommand::EnumSubCommands(IEnumExplorerCommand** ppEnum)
{
    if (!ppEnum) return E_POINTER;
    *ppEnum = new EnumExplorerCommand(m_cachedSubCommands);
    return S_OK;
}
