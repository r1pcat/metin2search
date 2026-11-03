#include "bridge.h"
#include <cstdio>
#include <string>
#include <iostream>
#include "script.h"
#include "test-script.h"
static DWORD target;
static DWORD thread;
static HWND targetWindow;
static long long bestArea=-1;
static BOOL CALLBACK window(HWND w,LPARAM){
 DWORD pid=0;auto tid=GetWindowThreadProcessId(w,&pid);
 if(pid==target&&IsWindowVisible(w)&&!GetWindow(w,GW_OWNER)){
  wchar_t title[256];GetWindowTextW(w,title,256);RECT rect;GetClientRect(w,&rect);
  auto score=(long long)(rect.right-rect.left)*(rect.bottom-rect.top);
  if(!_wcsicmp(title,L"METIN2"))score+=1000000000;
  if(score>bestArea){bestArea=score;thread=tid;targetWindow=w;}
 }return TRUE;
}
static HMODULE checkedBridge(const wchar_t* file){
 wchar_t expected[80];swprintf_s(expected,L"MarketBridge.%hs.dll",M2_BRIDGE_BUILD_ID);auto leaf=wcsrchr(file,L'\\');
 if(_wcsicmp(leaf?leaf+1:file,expected)){std::cout<<"{\"error\":\"Bridge filename/build mismatch\"}"<<std::endl;return nullptr;}
 auto dll=LoadLibraryW(file);using Identity=const char*(__cdecl*)();auto identity=dll?(Identity)GetProcAddress(dll,"MarketBuildId"):nullptr;
 if(!identity&&dll)identity=(Identity)GetProcAddress(dll,"_MarketBuildId");
 if(!identity||strcmp(identity(),M2_BRIDGE_BUILD_ID)){if(dll)FreeLibrary(dll);std::cout<<"{\"error\":\"Host/DLL build identity mismatch\"}"<<std::endl;return nullptr;}return dll;
}
int wmain(int argc,wchar_t** argv){
 if(argc==3&&!wcscmp(argv[1],L"--verify-bridge")){auto dll=checkedBridge(argv[2]);if(!dll)return 8;FreeLibrary(dll);std::cout<<"{\"bridgeVerified\":true,\"buildId\":\"" M2_BRIDGE_BUILD_ID "\"}"<<std::endl;return 0;}
 if(argc==2&&!wcscmp(argv[1],L"--identity")){std::cout<<"{\"abi\":2,\"buildId\":\"" M2_BRIDGE_BUILD_ID "\"}"<<std::endl;return 0;}
 if(argc==3&&!wcscmp(argv[1],L"--check-python")){
  auto python=LoadLibraryW(argv[2]);if(!python){std::cout<<"{\"error\":\"python27.dll yuklenemedi\"}"<<std::endl;return 10;}
  const char* exports[]={"PyRun_StringFlags","PyDict_New","PyDict_GetItemString","PyDict_SetItemString","PyImport_ImportModule","PyModule_GetDict","PyString_AsString","Py_DecRef","Py_IncRef","PyGILState_Ensure","PyGILState_Release","PyErr_Clear","Py_BuildValue","PyCFunction_NewEx","PyErr_SetString","PyExc_RuntimeError"};
  for(auto name:exports)if(!GetProcAddress(python,name)){std::cout<<"{\"missingExport\":\""<<name<<"\"}"<<std::endl;return 11;}
  if(!GetProcAddress(python,"PyString_AsStringAndSize")||
     (!GetProcAddress(python,"PyUnicodeUCS2_FromWideChar")&&!GetProcAddress(python,"PyUnicode_FromWideChar")))return 11;
  auto init=(void(__cdecl*)())GetProcAddress(python,"Py_Initialize");
  auto compile=(void*(__cdecl*)(const char*,const char*,int,void*))GetProcAddress(python,"Py_CompileStringFlags");
  auto print=(void(__cdecl*)())GetProcAddress(python,"PyErr_Print");
  if(!init||!compile||!print)return 12;
  auto noSite=(int*)GetProcAddress(python,"Py_NoSiteFlag");if(noSite)*noSite=1;
  auto ignoreEnv=(int*)GetProcAddress(python,"Py_IgnoreEnvironmentFlag");if(ignoreEnv)*ignoreEnv=1;
  static char pythonHome[32768];wchar_t directory[32768];wcscpy_s(directory,argv[2]);auto last=wcsrchr(directory,L'\\');if(last)*last=0;
  WideCharToMultiByte(CP_ACP,0,directory,-1,pythonHome,sizeof(pythonHome),nullptr,nullptr);
  auto setHome=(void(__cdecl*)(char*))GetProcAddress(python,"Py_SetPythonHome");if(setHome)setHome(pythonHome);
  init();auto code=compile(bridgeScript,"trade_bridge.py",257,nullptr);
  if(!code){print();return 13;}
  auto addModule=(void*(__cdecl*)(const char*))GetProcAddress(python,"PyImport_AddModule");
  auto moduleDict=(void*(__cdecl*)(void*))GetProcAddress(python,"PyModule_GetDict");
  auto run=(void*(__cdecl*)(const char*,int,void*,void*,void*))GetProcAddress(python,"PyRun_StringFlags");
  if(!addModule||!moduleDict||!run)return 14;
  auto dictionary=moduleDict(addModule("__main__"));
  if(!run(testSetup,257,dictionary,dictionary,nullptr)||!run(bridgeScript,257,dictionary,dictionary,nullptr)||
     !run(testAssertions,257,dictionary,dictionary,nullptr)){print();return 15;}
  std::cout<<"{\"python27Exports\":true,\"python27Syntax\":true,\"offlineBridgeFlow\":true}"<<std::endl;return 0;
 }
 if(argc!=3)return 2; target=wcstoul(argv[1],nullptr,10);
 HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,target);
 wchar_t path[32768];DWORD length=32768;
 if(!process||!QueryFullProcessImageNameW(process,0,path,&length)){std::cout<<"{\"error\":\"Hedef surece erisim reddedildi\"}"<<std::endl;return 3;}
 CloseHandle(process);auto leaf=wcsrchr(path,L'\\');
 if(_wcsicmp(leaf?leaf+1:path,L"metin2client.exe")){std::cout<<"{\"error\":\"Hedef metin2client.exe olmali\"}"<<std::endl;return 3;}
 wchar_t legacyName[100];swprintf_s(legacyName,L"Local\\Metin2Search.Trade.%lu",target);
 auto legacy=OpenFileMappingW(FILE_MAP_READ,FALSE,legacyName);auto legacyError=GetLastError();if(legacy||legacyError!=ERROR_FILE_NOT_FOUND){if(legacy)CloseHandle(legacy);std::cout<<"{\"error\":\"Legacy bridge still attached; no new hook installed\"}"<<std::endl;return 5;}
 wchar_t ownerName[100];swprintf_s(ownerName,L"Local\\Metin2Search.TradeOwner.%lu",target);
 auto owner=CreateMutexW(nullptr,TRUE,ownerName);auto ownerError=GetLastError();
 auto ownerState=ownerError==ERROR_ALREADY_EXISTS?WaitForSingleObject(owner,0):WAIT_OBJECT_0;
 if(!owner||(ownerState!=WAIT_OBJECT_0&&ownerState!=WAIT_ABANDONED)){std::cout<<"{\"error\":\"Another bridge owns this client\"}"<<std::endl;return 5;}
 wchar_t expectedDll[80];swprintf_s(expectedDll,L"MarketBridge.%hs.dll",M2_BRIDGE_BUILD_ID);auto dllLeaf=wcsrchr(argv[2],L'\\');
 if(_wcsicmp(dllLeaf?dllLeaf+1:argv[2],expectedDll)){std::cout<<"{\"error\":\"Bridge filename/build mismatch\"}"<<std::endl;return 8;}
 EnumWindows(window,0);if(!thread)return 4;
 wchar_t name[80];swprintf_s(name,L"Local\\Metin2Search.Trade.%hs.%lu",M2_BRIDGE_BUILD_ID,target);
 HANDLE mapping=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(BridgeMemory),name);
 if(!mapping||GetLastError()==ERROR_ALREADY_EXISTS){std::cout<<"{\"error\":\"Bu istemciye zaten bir kopru bagli\"}"<<std::endl;return 5;}
 auto memory=(BridgeMemory*)MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(BridgeMemory));if(!memory)return 6;
 memory->targetWindow=(DWORD)(UINT_PTR)targetWindow;memory->magic=M2_BRIDGE_MAGIC;memory->abi=M2_BRIDGE_ABI;strcpy_s(memory->buildId,M2_BRIDGE_BUILD_ID);
 auto dll=checkedBridge(argv[2]);if(!dll)return 8;
 auto callback=dll?(HOOKPROC)GetProcAddress(dll,"_MarketHook@12"):nullptr;
 if(!callback&&dll)callback=(HOOKPROC)GetProcAddress(dll,"MarketHook");
 HHOOK hook=callback?SetWindowsHookExW(WH_GETMESSAGE,callback,dll,thread):nullptr;
 if(!hook){std::cout<<"{\"error\":\"Windows mesaj koprusu acilamadi\",\"windowsError\":"<<GetLastError()<<"}"<<std::endl;return 7;}
 PostMessageW(targetWindow,WM_NULL,0,0);
 bool firstCommand=true;
 std::string command;
 while(std::getline(std::cin,command)){
  if(command.size()>=sizeof(memory->command)){std::cout<<"{\"error\":\"Komut cok uzun\"}"<<std::endl;continue;}
  if(firstCommand){
   strcpy_s(memory->command,"ping");MemoryBarrier();InterlockedExchange(&memory->state,1);PostMessageW(targetWindow,WM_NULL,0,0);
   auto readyUntil=GetTickCount64()+5000;while(memory->state!=2&&GetTickCount64()<readyUntil)Sleep(2);
   if(memory->state!=2||!strstr(memory->response,"\"nativeReady\":true")||!strstr(memory->response,M2_BRIDGE_BUILD_ID)){std::cout<<"{\"error\":\"Native callback handshake failed; Python and purchases not started\",\"hookHits\":"<<memory->hookHits<<",\"timerHits\":"<<memory->timerHits<<",\"dispatchStage\":"<<memory->stage<<",\"ownerThread\":"<<memory->ownerThread<<",\"timerWindow\":"<<memory->timerWindow<<"}"<<std::endl;break;}
   firstCommand=false;InterlockedExchange(&memory->state,0);
  }
  strcpy_s(memory->command,command.c_str());MemoryBarrier();InterlockedExchange(&memory->state,1);
  if(!PostMessageW(targetWindow,WM_NULL,0,0)){std::cout<<"{\"error\":\"Istemci penceresine mesaj gonderilemedi\",\"windowsError\":"<<GetLastError()<<"}"<<std::endl;break;}
  auto until=GetTickCount64()+15000;
  while(memory->state!=2&&GetTickCount64()<until)Sleep(2);
  if(memory->state!=2){std::cout<<"{\"error\":\"Istemci ana thread yanit vermedi (15 s)\",\"threadId\":"<<thread<<",\"windowHandle\":"<<(unsigned long)targetWindow<<",\"hookHits\":"<<memory->hookHits<<",\"timerHits\":"<<memory->timerHits<<",\"dispatchStage\":"<<memory->stage<<",\"ownerThread\":"<<memory->ownerThread<<",\"timerWindow\":"<<memory->timerWindow<<"}"<<std::endl;break;}
  std::cout<<memory->response<<std::endl;InterlockedExchange(&memory->state,0);
 }
 InterlockedExchange(&memory->closing,1);PostMessageW(targetWindow,WM_NULL,0,0);auto closeUntil=GetTickCount64()+3000;while(!memory->closed&&GetTickCount64()<closeUntil)Sleep(2);
 if(!memory->closed)std::cerr<<"Bridge teardown ACK missing; do not reconnect automatically"<<std::endl;
 UnhookWindowsHookEx(hook);UnmapViewOfFile(memory);CloseHandle(mapping);ReleaseMutex(owner);CloseHandle(owner);return 0;
}
