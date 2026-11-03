#pragma once
#include <windows.h>
#include "build-identity.h"
struct BridgeMemory {
    volatile LONG state;
    volatile LONG closing;
    char command[128];
    char response[524288];
    DWORD targetWindow;
    volatile LONG hookHits;
    volatile LONG timerHits;
    volatile LONG stage;
    DWORD ownerThread;
    DWORD timerWindow;
    DWORD magic;
    DWORD abi;
    char buildId[17];
    volatile LONG closed;
};
