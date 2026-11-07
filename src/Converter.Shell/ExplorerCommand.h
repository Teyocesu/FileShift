#pragma once

#include <windows.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <string>
#include <vector>
#include <atomic>
#include <mutex>

// {D87E4B9A-6C21-4F08-9E3A-5A18C42B7101}
static const CLSID CLSID_FileShiftExplorerCommand =
{ 0xd87e4b9a, 0x6c21, 0x4f08, { 0x9e, 0x3a, 0x5a, 0x18, 0xc4, 0x2b, 0x71, 0x01 } };

extern std::atomic<long> g_dllRefCount;
extern HMODULE g_hModule;

struct TargetMenuEntry
{
    std::wstring formatId;
    std::wstring title;
    std::wstring requiredCategory;
    bool isOpenMoreDialog = false;
    std::vector<TargetMenuEntry> children;
};

class SubExplorerCommand final : public IExplorerCommand
{
public:
    explicit SubExplorerCommand(TargetMenuEntry entry);

    // IUnknown
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    IFACEMETHODIMP_(ULONG) AddRef() override;
    IFACEMETHODIMP_(ULONG) Release() override;

    // IExplorerCommand
    IFACEMETHODIMP GetTitle(IShellItemArray* psiItemArray, LPWSTR* ppszName) override;
    IFACEMETHODIMP GetIcon(IShellItemArray* psiItemArray, LPWSTR* ppszIcon) override;
    IFACEMETHODIMP GetToolTip(IShellItemArray* psiItemArray, LPWSTR* ppszInfotip) override;
    IFACEMETHODIMP GetCanonicalName(GUID* pguidCommandName) override;
    IFACEMETHODIMP GetState(IShellItemArray* psiItemArray, BOOL fOkToBeSlow, EXPCMDSTATE* pCmdState) override;
    IFACEMETHODIMP Invoke(IShellItemArray* psiItemArray, IBindCtx* pbc) override;
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* pFlags) override;
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** ppEnum) override;

private:
    ~SubExplorerCommand() = default;
    std::atomic<ULONG> m_refCount{ 1 };
    TargetMenuEntry m_entry;
};

class EnumExplorerCommand final : public IEnumExplorerCommand
{
public:
    explicit EnumExplorerCommand(const std::vector<TargetMenuEntry>& entries);

    // IUnknown
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    IFACEMETHODIMP_(ULONG) AddRef() override;
    IFACEMETHODIMP_(ULONG) Release() override;

    // IEnumExplorerCommand
    IFACEMETHODIMP Next(ULONG celt, IExplorerCommand** pUICommand, ULONG* pceltFetched) override;
    IFACEMETHODIMP Skip(ULONG celt) override;
    IFACEMETHODIMP Reset() override;
    IFACEMETHODIMP Clone(IEnumExplorerCommand** ppenum) override;

private:
    ~EnumExplorerCommand() = default;
    std::atomic<ULONG> m_refCount{ 1 };
    std::vector<TargetMenuEntry> m_entries;
    size_t m_currentIndex{ 0 };
};

class RootExplorerCommand final : public IExplorerCommand
{
public:
    RootExplorerCommand();

    // IUnknown
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override;
    IFACEMETHODIMP_(ULONG) AddRef() override;
    IFACEMETHODIMP_(ULONG) Release() override;

    // IExplorerCommand
    IFACEMETHODIMP GetTitle(IShellItemArray* psiItemArray, LPWSTR* ppszName) override;
    IFACEMETHODIMP GetIcon(IShellItemArray* psiItemArray, LPWSTR* ppszIcon) override;
    IFACEMETHODIMP GetToolTip(IShellItemArray* psiItemArray, LPWSTR* ppszInfotip) override;
    IFACEMETHODIMP GetCanonicalName(GUID* pguidCommandName) override;
    IFACEMETHODIMP GetState(IShellItemArray* psiItemArray, BOOL fOkToBeSlow, EXPCMDSTATE* pCmdState) override;
    IFACEMETHODIMP Invoke(IShellItemArray* psiItemArray, IBindCtx* pbc) override;
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* pFlags) override;
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** ppEnum) override;

private:
    ~RootExplorerCommand() = default;
    std::atomic<ULONG> m_refCount{ 1 };
    std::mutex m_mutex;
    std::vector<TargetMenuEntry> m_cachedSubCommands;
};
