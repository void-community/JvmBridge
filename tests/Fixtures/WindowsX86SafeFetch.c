#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

typedef DWORD (__cdecl *InstallHandler)(HMODULE);
typedef DWORD (__cdecl *RecoveryCount)(void);
typedef LONG (__cdecl *Probe)(const LONG *, LONG);

static InstallHandler install;
static Probe probe;
static LONG *inaccessible;

static DWORD WINAPI exercise_thread(void *unused)
{
    (void)unused;
    if (install(GetModuleHandleW(NULL)) != ERROR_SUCCESS)
        return 1;
    for (int index = 0; index < 100; index++)
        if (probe(inaccessible, 0x12345678) != 0x12345678)
            return 2;
    return 0;
}

int main(int argc, char **argv)
{
    if (argc != 3)
        return 2;
    HMODULE library = LoadLibraryA(argv[1]);
    if (library == NULL)
        return 3;
    install = (InstallHandler)(uintptr_t)GetProcAddress(library, "JvmBridgeInstallSafeFetchHandler");
    RecoveryCount count = (RecoveryCount)(uintptr_t)GetProcAddress(library, "JvmBridgeSafeFetchRecoveryCount");
    if (install == NULL || count == NULL)
        return 4;
    HMODULE caller = strcmp(argv[2], "caller") == 0 ? GetModuleHandleW(L"kernel32.dll") : GetModuleHandleW(NULL);
    if (install(caller) != ERROR_SUCCESS || install(caller) != ERROR_SUCCESS || !FreeLibrary(library))
        return 5;

    // Run the legacy stub on a guaranteed unreadable page. Its caller is a
    // registered native image, as it is for HotSpot's handle validator.
    unsigned char instructions[] = { 0x8b, 0x44, 0x24, 0x08, 0x8b, 0x4c, 0x24, 0x04, 0x8b, 0x01, 0xc3 };
    if (strcmp(argv[2], "signature") == 0)
        instructions[3] = 0x04;
    void *code = VirtualAlloc(NULL, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
    inaccessible = VirtualAlloc(NULL, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_NOACCESS);
    if (code == NULL || inaccessible == NULL)
        return 6;
    memcpy(code, instructions, sizeof(instructions));
    DWORD previous;
    if (!VirtualProtect(code, 4096, PAGE_EXECUTE_READ, &previous) || !FlushInstructionCache(GetCurrentProcess(), code, sizeof(instructions)))
        return 7;
    probe = (Probe)(uintptr_t)code;

    if (strcmp(argv[2], "recover") != 0)
    {
        puts("UNRELATED_FAULT_CONTROL");
        fflush(stdout);
        if (strcmp(argv[2], "read") == 0)
            return *(volatile LONG *)inaccessible;
        if (strcmp(argv[2], "write") == 0)
            *(volatile LONG *)inaccessible = 1;
        else if (strcmp(argv[2], "execute") == 0)
            ((void (__cdecl *)(void))(uintptr_t)inaccessible)();
        else if (strcmp(argv[2], "signature") == 0 || strcmp(argv[2], "caller") == 0)
            return probe(inaccessible, 123);
        return 8;
    }

    LONG value = 42;
    if (probe(&value, -1) != 42 || count() != 0 || probe(inaccessible, 0) != 0 || probe(inaccessible, -1) != -1)
        return 9;
    HANDLE threads[4];
    for (unsigned int index = 0; index < 4; index++)
    {
        threads[index] = CreateThread(NULL, 0, exercise_thread, NULL, 0, NULL);
        if (threads[index] == NULL)
            return 10;
    }
    if (WaitForMultipleObjects(4, threads, TRUE, 30000) != WAIT_OBJECT_0)
        return 11;
    for (unsigned int index = 0; index < 4; index++)
    {
        DWORD result;
        if (!GetExitCodeThread(threads[index], &result) || result != 0 || !CloseHandle(threads[index]))
            return 12;
    }
    if (count() != 402 || !VirtualFree(code, 0, MEM_RELEASE) || !VirtualFree(inaccessible, 0, MEM_RELEASE))
        return 13;
    puts("SAFEFETCH_RECOVERED=402");
    return 0;
}
