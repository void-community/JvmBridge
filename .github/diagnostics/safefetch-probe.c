#include <windows.h>
#include <stdint.h>
#include <string.h>

// Diagnostic prototype only: identify the exact old HotSpot x86 SafeFetch
// instruction sequence and its native JVM caller before resuming at ret.
static uintptr_t jvm_start;
static uintptr_t jvm_end;
static volatile LONG recovered;

static LONG CALLBACK recover_safefetch(EXCEPTION_POINTERS *exception)
{
    static const unsigned char stub[] = {0x8b,0x44,0x24,0x08,0x8b,0x4c,0x24,0x04,0x8b,0x01,0xc3};
    EXCEPTION_RECORD *record = exception->ExceptionRecord;
    CONTEXT *context = exception->ContextRecord;
    if (record->ExceptionCode != EXCEPTION_ACCESS_VIOLATION || record->NumberParameters != 2 || record->ExceptionInformation[0] != 0 || record->ExceptionFlags != 0)
        return EXCEPTION_CONTINUE_SEARCH;
    __try {
        const unsigned char *entry = (const unsigned char *)(context->Eip - 8);
        MEMORY_BASIC_INFORMATION memory;
        uintptr_t *stack = (uintptr_t *)context->Esp;
        if (VirtualQuery(entry, &memory, sizeof(memory)) != sizeof(memory) || memory.State != MEM_COMMIT || memory.Type != MEM_PRIVATE || !(memory.Protect & (PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE)) || memory.Protect & PAGE_GUARD)
            return EXCEPTION_CONTINUE_SEARCH;
        if ((uintptr_t)entry + sizeof(stub) > (uintptr_t)memory.BaseAddress + memory.RegionSize || memcmp(entry, stub, sizeof(stub)) != 0)
            return EXCEPTION_CONTINUE_SEARCH;
        if (stack[0] < jvm_start || stack[0] >= jvm_end || stack[1] != context->Ecx || stack[1] != record->ExceptionInformation[1] || stack[2] != context->Eax)
            return EXCEPTION_CONTINUE_SEARCH;
        context->Eip += 2;
        InterlockedIncrement(&recovered);
        return EXCEPTION_CONTINUE_EXECUTION;
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        return EXCEPTION_CONTINUE_SEARCH;
    }
}

__declspec(dllexport) int __cdecl JvmBridgeDiagnosticInstall(HMODULE jvm)
{
    IMAGE_DOS_HEADER *dos = (IMAGE_DOS_HEADER *)jvm;
    IMAGE_NT_HEADERS *pe = (IMAGE_NT_HEADERS *)((char *)jvm + dos->e_lfanew);
    jvm_start = (uintptr_t)jvm;
    jvm_end = jvm_start + pe->OptionalHeader.SizeOfImage;
    return AddVectoredExceptionHandler(1, recover_safefetch) != NULL;
}

__declspec(dllexport) LONG __cdecl JvmBridgeDiagnosticCount(void)
{
    return InterlockedCompareExchange(&recovered, 0, 0);
}
