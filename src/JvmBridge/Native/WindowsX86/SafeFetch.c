#define WIN32_LEAN_AND_MEAN
#define PSAPI_VERSION 2
#include <windows.h>
#include <psapi.h>
#include <stdint.h>

#if !defined(_M_IX86) && !defined(__i386__)
#error This compatibility handler is only for Windows x86.
#endif

typedef struct JvmModule
{
    uintptr_t start;
    uintptr_t end;
    struct JvmModule *next;
} JvmModule;

static SRWLOCK registration_lock = SRWLOCK_INIT;
static JvmModule *volatile modules;
static PVOID handler;
static volatile LONG recovered;

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)instance;
    (void)reason;
    (void)reserved;
    return TRUE;
}

static BOOL read_memory(const void *address, void *buffer, SIZE_T size)
{
    SIZE_T copied;
    return ReadProcessMemory(GetCurrentProcess(), address, buffer, size, &copied) && copied == size;
}

static BOOL is_jvm_caller(uintptr_t address)
{
    JvmModule *module = InterlockedCompareExchangePointer((PVOID volatile *)&modules, NULL, NULL);
    for (; module != NULL; module = module->next)
    {
        if (address >= module->start && address < module->end)
        {
            MEMORY_BASIC_INFORMATION memory;
            return VirtualQuery((void *)address, &memory, sizeof(memory)) == sizeof(memory)
                && memory.AllocationBase == (void *)module->start && memory.State == MEM_COMMIT
                && memory.Type == MEM_IMAGE && !(memory.Protect & PAGE_GUARD)
                && (memory.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY));
        }
    }
    return FALSE;
}

static LONG CALLBACK recover_safefetch(EXCEPTION_POINTERS *exception)
{
    // OpenJDK's old x86 stub loads errValue into EAX, the address into ECX,
    // then probes [ECX]. Only that exact read may resume at the following ret.
    // JDK-8185734 describes the missing enclosing SEH handler in native hosts;
    // JDK-8283326 replaced this stub with locally protected reads in newer JVMs.
    static const unsigned char expected[] = { 0x8b, 0x44, 0x24, 0x08, 0x8b, 0x4c, 0x24, 0x04, 0x8b, 0x01, 0xc3 };
    EXCEPTION_RECORD *record = exception->ExceptionRecord;
    CONTEXT *context = exception->ContextRecord;
    if (record->ExceptionCode != EXCEPTION_ACCESS_VIOLATION || record->ExceptionFlags != 0
        || record->NumberParameters != 2 || record->ExceptionInformation[0] != 0
        || (uintptr_t)record->ExceptionAddress != context->Eip || context->Eip < 8)
        return EXCEPTION_CONTINUE_SEARCH;

    const unsigned char *entry = (const unsigned char *)(context->Eip - 8);
    MEMORY_BASIC_INFORMATION memory;
    if (VirtualQuery(entry, &memory, sizeof(memory)) != sizeof(memory) || memory.State != MEM_COMMIT
        || memory.Type != MEM_PRIVATE || (memory.Protect & PAGE_GUARD)
        || !(memory.Protect & (PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE))
        || (uintptr_t)entry - (uintptr_t)memory.BaseAddress > memory.RegionSize - sizeof(expected))
        return EXCEPTION_CONTINUE_SEARCH;

    // ReadProcessMemory also safely rejects inaccessible code/stack snapshots.
    // The VEH never enters managed code, allocates, locks, or dereferences the
    // faulting data address. Other access violations remain unhandled here.
    unsigned char instructions[sizeof(expected)];
    uintptr_t arguments[3];
    if (!read_memory(entry, instructions, sizeof(instructions))
        || !read_memory((void *)context->Esp, arguments, sizeof(arguments)))
        return EXCEPTION_CONTINUE_SEARCH;
    for (unsigned int index = 0; index < sizeof(expected); index++)
        if (instructions[index] != expected[index])
            return EXCEPTION_CONTINUE_SEARCH;
    if (arguments[1] != context->Ecx || arguments[1] != record->ExceptionInformation[1]
        || arguments[2] != context->Eax || !is_jvm_caller(arguments[0]))
        return EXCEPTION_CONTINUE_SEARCH;

    context->Eip += 2;
    InterlockedIncrement(&recovered);
    return EXCEPTION_CONTINUE_EXECUTION;
}

__declspec(dllexport) DWORD __cdecl JvmBridgeInstallSafeFetchHandler(HMODULE jvm)
{
    MODULEINFO information;
    if (!K32GetModuleInformation(GetCurrentProcess(), jvm, &information, sizeof(information)))
        return GetLastError();

    DWORD error = ERROR_SUCCESS;
    AcquireSRWLockExclusive(&registration_lock);
    for (JvmModule *module = modules; module != NULL; module = module->next)
        if (module->start == (uintptr_t)information.lpBaseOfDll)
            goto done;

    JvmModule *module = HeapAlloc(GetProcessHeap(), 0, sizeof(JvmModule));
    if (module == NULL)
    {
        error = ERROR_NOT_ENOUGH_MEMORY;
        goto done;
    }

    // Both modules and immutable registration records live for the process.
    // A VEH must never point into a DLL unloaded by an AssemblyLoadContext.
    HMODULE pinned;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            (LPCWSTR)(uintptr_t)&recover_safefetch, &pinned)
        || !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            (LPCWSTR)jvm, &pinned))
    {
        error = GetLastError();
        HeapFree(GetProcessHeap(), 0, module);
        goto done;
    }
    if (handler == NULL)
        handler = AddVectoredExceptionHandler(1, recover_safefetch);
    if (handler == NULL)
    {
        error = ERROR_NOT_ENOUGH_MEMORY;
        HeapFree(GetProcessHeap(), 0, module);
        goto done;
    }

    module->start = (uintptr_t)information.lpBaseOfDll;
    module->end = module->start + information.SizeOfImage;
    module->next = modules;
    InterlockedExchangePointer((PVOID volatile *)&modules, module);
done:
    ReleaseSRWLockExclusive(&registration_lock);
    return error;
}

// Native regression tests can verify that a real fault was recovered.
__declspec(dllexport) DWORD __cdecl JvmBridgeSafeFetchRecoveryCount(void)
{
    return (DWORD)InterlockedCompareExchange(&recovered, 0, 0);
}
