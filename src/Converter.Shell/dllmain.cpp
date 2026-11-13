#include "ExplorerCommand.h"
#include <new>

std::atomic<long> g_dllRefCount{ 0 };
HMODULE g_hModule = nullptr;

class FileShiftClassFactory final : public IClassFactory
{
public:
    FileShiftClassFactory() { ++g_dllRefCount; }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IClassFactory)
        {
            *ppv = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }

    IFACEMETHODIMP_(ULONG) AddRef() override
    {
        return ++m_refCount;
    }

    IFACEMETHODIMP_(ULONG) Release() override
    {
        ULONG count = --m_refCount;
        if (count == 0)
        {
            delete this;
            --g_dllRefCount;
        }
        return count;
    }

    IFACEMETHODIMP CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppv) override
    {
        if (!ppv) return E_POINTER;
        *ppv = nullptr;
        if (pUnkOuter) return CLASS_E_NOAGGREGATION;
        auto* cmd = new (std::nothrow) RootExplorerCommand();
        if (!cmd) return E_OUTOFMEMORY;
        HRESULT hr = cmd->QueryInterface(riid, ppv);
        cmd->Release();
        return hr;
    }

    IFACEMETHODIMP LockServer(BOOL fLock) override
    {
        if (fLock) ++g_dllRefCount;
        else --g_dllRefCount;
        return S_OK;
    }

private:
    ~FileShiftClassFactory() = default;
    std::atomic<ULONG> m_refCount{ 1 };
};

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID)
{
    if (ul_reason_for_call == DLL_PROCESS_ATTACH)
    {
        g_hModule = hModule;
        DisableThreadLibraryCalls(hModule);
    }
    return TRUE;
}

STDAPI DllCanUnloadNow()
{
    return (g_dllRefCount.load() == 0) ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv)
{
    if (!ppv) return E_POINTER;
    *ppv = nullptr;

    if (rclsid == CLSID_FileShiftExplorerCommand)
    {
        auto* factory = new (std::nothrow) FileShiftClassFactory();
        if (!factory) return E_OUTOFMEMORY;
        HRESULT hr = factory->QueryInterface(riid, ppv);
        factory->Release();
        return hr;
    }

    return CLASS_E_CLASSNOTAVAILABLE;
}
