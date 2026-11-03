#include "bridge.h"
#include <cstdio>
#include <cstring>
#include "script.h"
static BridgeMemory* shared;
static UINT_PTR timer;
static HWND timerWindow;
static HMODULE selfModule;
static void* globals;
static bool initialized;
extern "C" __declspec(dllexport) const char* __cdecl MarketBuildId(){return M2_BRIDGE_BUILD_ID;}
using Obj=void;
static Obj* (__cdecl *Run)(const char*,int,Obj*,Obj*,void*);
static Obj* (__cdecl *DictNew)();
static Obj* (__cdecl *DictGet)(Obj*,const char*);
static int (__cdecl *DictSet)(Obj*,const char*,Obj*);
static Obj* (__cdecl *Import)(const char*);
static Obj* (__cdecl *ModuleDict)(Obj*);
static char* (__cdecl *StringData)(Obj*);
static void (__cdecl *DecRef)(Obj*);
static void (__cdecl *IncRef)(Obj*);
static int (__cdecl *GIL)();
static void (__cdecl *ReleaseGIL)(int);
static void (__cdecl *ClearError)();
static Obj* (__cdecl *BuildValue)(const char*,...);
static Obj* (__cdecl *CFunctionNew)(void*,Obj*,Obj*);
static void (__cdecl *SetError)(Obj*,const char*);
static Obj** runtimeError;
static int (__cdecl *StringSize)(Obj*,char**,int*);
static Obj* (__cdecl *UnicodeWide)(const wchar_t*,int);
static long (__cdecl *IntAsLong)(Obj*);
static Obj* (__cdecl *IntFromLong)(long);
static Obj* (__cdecl *ListNew)(int);
static int (__cdecl *ListAppend)(Obj*,Obj*);
static DWORD address(DWORD original){return (DWORD)GetModuleHandleW(nullptr)+original-0x710000;}
static DWORD u32(DWORD pointer){return *(DWORD*)pointer;}
static Obj* fail(const char* message){SetError(*runtimeError,message);return nullptr;}
static Obj* __cdecl snapshot(Obj*,Obj*) {
 __try {
  const DWORD entries[][2]={{0x031ee2cc,0x02dfa6f0},{0x031ee2dc,0x02dfa890},{0x031ee38c,0x02dfa670},
   {0x031ee41c,0x02dfb0b0},{0x031ee42c,0x02dfb1a0},{0x031ee44c,0x02dfb200}};
  for(auto& pair:entries)if(u32(address(pair[0]))!=address(pair[1]))return fail("Istemci adres profili/metot tablosu eslesmedi");
  auto player=u32(address(0x035ed9e4));if(player<65536)return fail("Player manager hazir degil");
  if(*(BYTE*)(player+0x3d289)!=1)return fail("Kanal 1 gerekli");
  auto manager=u32(address(0x035f1d34));if(manager<65536)return fail("Shop manager hazir degil");
  auto count=u32(manager+0x44ac),total=u32(manager+0x44b0),page=u32(manager+0x44b4);
  if(count>11||total>10000000||page>1000000)return fail("Ticaret Cami sonuc sayaci gecersiz");
  char hex[11*187*2+1];const char* digits="0123456789abcdef";
  auto raw=(BYTE*)(manager+0x3ca0);
  for(DWORD i=0;i<count*187;i++){hex[2*i]=digits[raw[i]>>4];hex[2*i+1]=digits[raw[i]&15];}hex[count*187*2]=0;
  auto open=u32(manager+0x34),priv=u32(manager+0x38);
  if(open>1||priv>1)return fail("Pazar bayraklari gecersiz");
  return BuildValue("{s:i,s:i,s:i,s:s,s:i,s:i}","count",count,"total",total,"page",page,"raw",hex,"shopOpen",open,"shopPrivate",priv);
 } __except(EXCEPTION_EXECUTE_HANDLER){return fail("Native adres okunamadi; istemci profili eslesmiyor");}
}
struct Method {const char* name;Obj*(__cdecl *function)(Obj*,Obj*);int flags;const char* doc;};
static Method snapshotMethod={"_native_snapshot",snapshot,4,"Validated trade result snapshot"};
static Obj* __cdecl gameWindow(Obj*,Obj*) {
 __try {
  auto network=u32(address(0x035ed794));
  if(network<65536)return fail("Network manager hazir degil");
  auto object=(Obj*)u32(network+0x3b0);
  if((DWORD)object<65536)return fail("GameWindow referansi hazir degil");
  IncRef(object);return object;
 } __except(EXCEPTION_EXECUTE_HANDLER){return fail("GameWindow referansi okunamadi");}
}
static Method gameWindowMethod={"_native_game_window",gameWindow,4,"Network game phase Python window"};
static Obj* __cdecl text1254(Obj*,Obj* value){
 char* bytes=nullptr;int length=0;if(StringSize(value,&bytes,&length)!=0)return nullptr;
 if(length>65536)return fail("Metin boyutu siniri asildi");
 wchar_t wide[65536];auto count=MultiByteToWideChar(1254,0,bytes,length,wide,65536);
 if(count==0&&length!=0)return fail("Turkce metin donusturulemedi");
 return UnicodeWide(wide,count);
}
static Method textMethod={"_native_text",text1254,8,"Windows CP1254 text"};

struct ChatTailLine {DWORD pointer,timeBits,kind,codePage,length;char text[1025];};
static bool readChatTail(ChatTailLine* lines,DWORD& captured){
 __try {
  auto object=u32(address(0x035f19fc));if(object<65536)return false;
  if(u32(object+4)!=address(0x03076070)||u32(address(0x03076074))!=address(0x02c65b50))return false;
  auto map=u32(object+12),size=u32(object+16),offset=u32(object+20),count=u32(object+24);
  if(count>300)return false;
  captured=0;if(!count)return true;
  if(map<65536||size<8||size>65536||(size&(size-1))!=0)return false;
  auto start=count>16?count-16:0;
  for(DWORD i=start;i<count;i++){
   auto absolute=offset+i;auto block=u32(map+((absolute>>2)&(size-1))*4);if(block<65536)return false;
   auto pointer=u32(block+(absolute&3)*4);if(pointer<65536)return false;
   auto& line=lines[captured];line.pointer=pointer;line.kind=u32(pointer+4);line.timeBits=u32(pointer+8);
   line.length=u32(pointer+0x90);auto capacity=u32(pointer+0x94);line.codePage=u32(pointer+0xa8);
   if(line.kind>255||line.length>1024||capacity<line.length||capacity>1048576)return false;
   auto data=capacity>15?u32(pointer+0x80):pointer+0x80;if(data<65536)return false;
   if(*(char*)(data+line.length)!=0)return false;
   memcpy(line.text,(void*)data,line.length);line.text[line.length]=0;
   if(u32(pointer+8)!=line.timeBits||u32(pointer+0x90)!=line.length)return false;
   captured++;
  }
  return u32(address(0x035f19fc))==object&&u32(object+12)==map&&u32(object+16)==size&&u32(object+20)==offset&&u32(object+24)==count;
 }__except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
static Obj* __cdecl chatTail(Obj*,Obj*){
 ChatTailLine lines[16];DWORD captured=0;
 if(!readChatTail(lines,captured))return fail("Chat tail memory profile did not match");
 auto result=ListNew(0);if(!result)return nullptr;
 for(DWORD i=0;i<captured;i++){
  auto& line=lines[i];wchar_t wide[1025];UINT codePage=line.codePage?line.codePage:1254;
  if(codePage!=1254&&codePage!=1252&&codePage!=65001)continue;
  auto count=MultiByteToWideChar(codePage,0,line.text,line.length,wide,1025);
  if(!count&&line.length)continue;
  auto text=UnicodeWide(wide,count);if(!text){DecRef(result);return nullptr;}
  auto entry=BuildValue("{s:k,s:k,s:k,s:k,s:O}","pointer",line.pointer,"timeBits",line.timeBits,"kind",line.kind,"codePage",line.codePage,"text",text);
  DecRef(text);if(!entry){DecRef(result);return nullptr;}
  auto appended=ListAppend(result,entry);DecRef(entry);if(appended<0){DecRef(result);return nullptr;}
 }
 return result;
}
static Method chatTailMethod={"_native_chat_tail",chatTail,4,"Bounded read-only native chat tail"};

static DWORD rva(DWORD offset){return (DWORD)GetModuleHandleW(nullptr)+offset;}
static bool bytesMatch(DWORD at,const BYTE* expected,const char* mask,size_t length){
 __try {for(size_t i=0;i<length;i++)if(mask[i]=='x'&&*(const BYTE*)(at+i)!=expected[i])return false;return true;}
 __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
static const DWORD kPlayerGlobal=0x02EDD9E4,kNetworkGlobal=0x02EDD794,kSendItemUse=0x0266D4D0;
static const DWORD kSlotBase=0x84,kSlotStride=0x89,kCountOffset=0x08,kCapacityOffset=0x3BA90,kChannelOffset=0x3d289;
static Obj* __cdecl worldInfo(Obj*,Obj*){
 __try {
  auto player=u32(rva(kPlayerGlobal));int ready=player>=65536?1:0,channel=-1,capacity=0;
  if(ready){channel=*(BYTE*)(player+kChannelOffset);capacity=(int)u32(player+kCapacityOffset);}
  return BuildValue("{s:i,s:i,s:i}","player",ready,"channel",channel,"capacity",capacity);
 }__except(EXCEPTION_EXECUTE_HANDLER){return fail("Oyuncu bilgisi okunamadi");}
}
static Method worldMethod={"_native_world",worldInfo,4,"Read-only player pointer/channel/inventory capacity"};
static bool inventory(DWORD& player,int& capacity){
 player=u32(rva(kPlayerGlobal));if(player<65536)return false;
 capacity=(int)u32(player+kCapacityOffset);return capacity>0&&capacity<=200;
}
static Obj* __cdecl findItem(Obj*,Obj* value){
 long vnum=IntAsLong(value);if(vnum<=0){ClearError();return fail("Esya numarasi gecersiz");}
 __try {
  DWORD player=0;int capacity=0;if(!inventory(player,capacity))return fail("Envanter hazir degil");
  for(int slot=0;slot<capacity;slot++){
   auto base=player+kSlotBase+(DWORD)slot*kSlotStride;
   if(u32(base)==(DWORD)vnum&&*(BYTE*)(base+kCountOffset)>0)return IntFromLong(slot);
  }
  return IntFromLong(-1);
 }__except(EXCEPTION_EXECUTE_HANDLER){return fail("Envanter okunamadi");}
}
static Method findItemMethod={"_native_find_item",findItem,8,"Find an inventory slot by item number"};
static Obj* __cdecl useItem(Obj*,Obj* value){
 long slot=IntAsLong(value);if(slot<0||slot>=200){ClearError();return fail("Envanter yuvasi gecersiz");}
 __try {
  static const BYTE prologue[]={0x55,0x8B,0xEC,0x83,0xEC,0x18,0x89,0x4D,0xF8,0xC6,0x45,0xFF,0x4F,0x8D,0x45,0xFF,0x50,0x8B,0x4D,0xF8};
  static const char mask[]="xxxxxxxxxxxxxxxxxxxx";
  if(!bytesMatch(rva(kSendItemUse),prologue,mask,sizeof(prologue)))return fail("Esya kullanma fonksiyonu imzasi eslesmedi");
  auto network=u32(rva(kNetworkGlobal));if(network<65536)return fail("Ag yoneticisi hazir degil");
  DWORD player=0;int capacity=0;if(!inventory(player,capacity)||slot>=capacity)return fail("Envanter hazir degil");
  if(u32(player+kSlotBase+(DWORD)slot*kSlotStride)==0)return fail("Yuva bos");
  using UseFn=bool(__thiscall*)(void*,DWORD);
  bool sent=((UseFn)rva(kSendItemUse))((void*)network,1u|((DWORD)slot<<8));
  return BuildValue("i",sent?1:0);
 }__except(EXCEPTION_EXECUTE_HANDLER){return fail("Esya kullanma cagrisi hata verdi");}
}
static Method useItemMethod={"_native_use_item",useItem,8,"Send one inventory item-use request"};

static const DWORD kPackManagerSingleton=0x035f137c,kMappedFileCtor=0x02e95350,kPackGet=0x02ed4c20,kMappedFileDtor=0x02e95450;
static const DWORD kMappedSizeOffset=0x11c;
static void reply(const char* text);
static bool packProfile(){
 static const BYTE get[]={0x55,0x8B,0xEC,0x56,0x8B,0xF1,0x57,0x83,0x7E,0x08,0x00};
 static const BYTE ctor[]={0x55,0x8B,0xEC,0x51,0x56,0x8B,0xF1,0x89,0x75,0xFC,0xE8};
 static const BYTE dtor[]={0x55,0x8B,0xEC,0x6A,0xFF,0x68};
 return bytesMatch(address(kPackGet),get,"xxxxxxxxxxx",sizeof(get))&&bytesMatch(address(kMappedFileCtor),ctor,"xxxxxxxxxxx",sizeof(ctor))
  &&bytesMatch(address(kMappedFileDtor),dtor,"xxxxxx",sizeof(dtor));
}
static bool packNameOk(const char* name){
 auto length=strlen(name);if(!length||length>200||strstr(name,".."))return false;
 for(size_t i=0;i<length;i++){char c=name[i];
  if(!((c>='a'&&c<='z')||(c>='A'&&c<='Z')||(c>='0'&&c<='9')||c==' '||c=='_'||c=='-'||c=='.'||c=='/'||c==':'))return false;}
 return true;
}
static bool extractDirectory(wchar_t* directory,size_t capacity){
 if(!selfModule||!GetModuleFileNameW(selfModule,directory,(DWORD)capacity))return false;
 auto slash=wcsrchr(directory,L'\\');if(!slash)return false;*slash=0;
 if(wcslen(directory)+10>=capacity)return false;wcscat_s(directory,capacity,L"\\extract");
 CreateDirectoryW(directory,nullptr);return true;
}
static long long dumpOne(const wchar_t* root,const char* name){
 if(!packNameOk(name))return -2;
 wchar_t relative[260];auto count=MultiByteToWideChar(CP_ACP,0,name,-1,relative,200);if(count<=0)return -2;
 for(wchar_t* p=relative;*p;p++)if(*p==L'/')*p=L'\\'; else if(*p==L':')*p=L'_';
 wchar_t* start=relative;while(*start==L'\\')start++;
 wchar_t path[600];if(swprintf_s(path,L"%ls\\%ls",root,start)<0)return -2;
 static BYTE mapped[1024];memset(mapped,0,sizeof(mapped));
 long long size=-1;const void* data=nullptr;
 __try {
  auto manager=u32(address(kPackManagerSingleton));if(manager<65536)return -2;
  ((void(__fastcall*)(void*))address(kMappedFileCtor))(mapped);
  bool found=((bool(__thiscall*)(void*,void*,const char*,const void**))address(kPackGet))((void*)manager,mapped,name,&data);
  if(found&&data){
   size=*(DWORD*)(mapped+kMappedSizeOffset);
   if(size<0||size>512*1024*1024)size=-2;
   else{
    for(wchar_t* p=path+wcslen(root)+1;*p;p++)if(*p==L'\\'){*p=0;CreateDirectoryW(path,nullptr);*p=L'\\';}
    HANDLE file=CreateFileW(path,GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)size=-2;
    else{
     const BYTE* cursor=(const BYTE*)data;long long left=size;bool ok=true;
     while(left>0&&ok){DWORD chunk=left>1048576?1048576:(DWORD)left,written=0;ok=WriteFile(file,cursor,chunk,&written,nullptr)&&written==chunk;cursor+=chunk;left-=chunk;}
     CloseHandle(file);if(!ok)size=-2;
    }
   }
  }
  ((void(__fastcall*)(void*))address(kMappedFileDtor))(mapped);
 }__except(EXCEPTION_EXECUTE_HANDLER){return -2;}
 return size;
}
static void dumpFiles(unsigned startIndex){
 if(!packProfile()){reply("{\"error\":\"Pack okuyucu imzasi eslesmedi; dosya cikarma kapali\"}");return;}
 wchar_t root[400];if(!extractDirectory(root,400)){reply("{\"error\":\"extract klasoru hazirlanamadi\"}");return;}
 wchar_t requestPath[500];swprintf_s(requestPath,L"%ls\\_request.txt",root);
 HANDLE request=CreateFileW(requestPath,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
 if(request==INVALID_HANDLE_VALUE){reply("{\"error\":\"extract\\\\_request.txt bulunamadi\"}");return;}
 DWORD length=GetFileSize(request,nullptr);if(length==INVALID_FILE_SIZE||length>8*1024*1024){CloseHandle(request);reply("{\"error\":\"Istek dosyasi gecersiz\"}");return;}
 char* text=(char*)VirtualAlloc(nullptr,length+1,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);DWORD got=0;
 if(!text||!ReadFile(request,text,length,&got,nullptr)||got!=length){CloseHandle(request);if(text)VirtualFree(text,0,MEM_RELEASE);reply("{\"error\":\"Istek dosyasi okunamadi\"}");return;}
 CloseHandle(request);text[length]=0;
 wchar_t resultPath[500];swprintf_s(resultPath,L"%ls\\_result.txt",root);
 HANDLE result=CreateFileW(resultPath,startIndex?FILE_APPEND_DATA:GENERIC_WRITE,FILE_SHARE_READ,nullptr,startIndex?OPEN_ALWAYS:CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
 const unsigned maxLines=200000;auto lines=(char**)VirtualAlloc(nullptr,sizeof(char*)*maxLines,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);
 if(!lines){VirtualFree(text,0,MEM_RELEASE);reply("{\"error\":\"Bellek ayrilamadi\"}");return;}
 unsigned total=0;
 for(char* line=text;*line&&total<maxLines;){
  char* end=line;while(*end&&*end!='\n')end++;char* next=*end?end+1:end;*end=0;
  while(end>line&&(end[-1]=='\r'||end[-1]==' '))*--end=0;
  if(*line)lines[total++]=line;
  line=next;
 }
 unsigned index=startIndex,ok=0,missing=0,bad=0;ULONGLONG begin=GetTickCount64();
 while(index<total&&GetTickCount64()-begin<8000){
  auto size=dumpOne(root,lines[index]);
  char row[400];int n=sprintf_s(row,"%lld\t%s\r\n",size,lines[index]);
  if(result!=INVALID_HANDLE_VALUE&&n>0){DWORD w=0;WriteFile(result,row,(DWORD)n,&w,nullptr);}
  if(size>=0)ok++;else if(size==-1)missing++;else bad++;
  index++;
 }
 if(result!=INVALID_HANDLE_VALUE)CloseHandle(result);VirtualFree(lines,0,MEM_RELEASE);VirtualFree(text,0,MEM_RELEASE);
 char out[256];sprintf_s(out,"{\"done\":%u,\"ok\":%u,\"missing\":%u,\"failed\":%u,\"next\":%u,\"total\":%u}",index-startIndex,ok,missing,bad,index,total);
 reply(out);
}
#include "navigation.h"

template<class T> static bool bind(HMODULE m,T& p,const char* name){p=reinterpret_cast<T>(GetProcAddress(m,name));return p!=nullptr;}
static void reply(const char* text){
 if(strlen(text)>=sizeof(shared->response))text="{\"error\":\"Kopru cevap boyutu siniri asildi\"}";
 strcpy_s(shared->response,text);MemoryBarrier();InterlockedExchange(&shared->state,2);
}
static bool python() {
 HMODULE p=GetModuleHandleW(L"python27.dll");
 if(!p)return false;
 return bind(p,Run,"PyRun_StringFlags")&&bind(p,DictNew,"PyDict_New")&&bind(p,DictGet,"PyDict_GetItemString")&&
 bind(p,DictSet,"PyDict_SetItemString")&&bind(p,Import,"PyImport_ImportModule")&&bind(p,ModuleDict,"PyModule_GetDict")&&
 bind(p,StringData,"PyString_AsString")&&bind(p,DecRef,"Py_DecRef")&&bind(p,IncRef,"Py_IncRef")&&bind(p,GIL,"PyGILState_Ensure")&&
 bind(p,ReleaseGIL,"PyGILState_Release")&&bind(p,ClearError,"PyErr_Clear")&&bind(p,BuildValue,"Py_BuildValue")&&
 bind(p,CFunctionNew,"PyCFunction_NewEx")&&bind(p,SetError,"PyErr_SetString")&&bind(p,runtimeError,"PyExc_RuntimeError")&&
 bind(p,StringSize,"PyString_AsStringAndSize")&&
 bind(p,IntAsLong,"PyInt_AsLong")&&bind(p,IntFromLong,"PyInt_FromLong")&&
 bind(p,ListNew,"PyList_New")&&bind(p,ListAppend,"PyList_Append")&&
 (bind(p,UnicodeWide,"PyUnicodeUCS2_FromWideChar")||bind(p,UnicodeWide,"PyUnicode_FromWideChar"));
}
static bool execute(const char* script){auto result=Run(script,257,globals,globals,nullptr);if(!result){ClearError();return false;}DecRef(result);return true;}
static void process() {
 InterlockedExchange(&shared->stage,10);
 if(!strcmp(shared->command,"ping")){reply("{\"nativeReady\":true,\"buildId\":\"" M2_BRIDGE_BUILD_ID "\",\"abi\":2}");return;}
 {unsigned start=0;char extra=0;
  if(!strncmp(shared->command,"dumpfiles ",10)){
   if(sscanf_s(shared->command,"dumpfiles %u %c",&start,&extra,1u)!=1||start>200000){reply("{\"error\":\"Gecersiz dumpfiles komutu\"}");return;}
   dumpFiles(start);return;
  }}
 if(!python()){reply("{\"error\":\"Python 2.7 API bulunamadi\"}");return;}
 InterlockedExchange(&shared->stage,11);
 auto lock=GIL();
 InterlockedExchange(&shared->stage,12);
 if(!initialized){
  globals=DictNew();auto builtins=Import("__builtin__");
  if(!globals||!builtins){ClearError();ReleaseGIL(lock);reply("{\"error\":\"Python globals olusturulamadi\"}");return;}
  DictSet(globals,"__builtins__",ModuleDict(builtins));DecRef(builtins);
  auto native=CFunctionNew(&snapshotMethod,nullptr,nullptr);if(!native){ClearError();ReleaseGIL(lock);reply("{\"error\":\"Native snapshot fonksiyonu olusturulamadi\"}");return;}
  DictSet(globals,"_native_snapshot",native);DecRef(native);
  auto textFunction=CFunctionNew(&textMethod,nullptr,nullptr);
  if(!textFunction){ClearError();ReleaseGIL(lock);reply("{\"error\":\"Native metin fonksiyonu olusturulamadi\"}");return;}
  DictSet(globals,"_native_text",textFunction);DecRef(textFunction);
  auto windowFunction=CFunctionNew(&gameWindowMethod,nullptr,nullptr);
  if(!windowFunction){ClearError();ReleaseGIL(lock);reply("{\"error\":\"GameWindow API olusturulamadi\"}");return;}
  DictSet(globals,"_native_game_window",windowFunction);DecRef(windowFunction);
  auto chatFunction=CFunctionNew(&chatTailMethod,nullptr,nullptr);
  if(!chatFunction){ClearError();ReleaseGIL(lock);reply("{\"error\":\"Chat reader API unavailable\"}");return;}
  DictSet(globals,"_native_chat_tail",chatFunction);DecRef(chatFunction);
  struct Registered {const char* name;Method* method;} registered[]={{"_native_world",&worldMethod},{"_native_find_item",&findItemMethod},{"_native_use_item",&useItemMethod},{"_native_navigation",&navigationMethod},{"_native_walk",&walkMethod},{"_native_stop_walk",&stopWalkMethod}};
  for(auto& entry:registered){
   auto function=CFunctionNew(entry.method,nullptr,nullptr);
   if(!function){ClearError();ReleaseGIL(lock);reply("{\"error\":\"Oturum API'si olusturulamadi\"}");return;}
   DictSet(globals,entry.name,function);DecRef(function);
  }
  initialized=execute(bridgeScript);
  InterlockedExchange(&shared->stage,13);
 }
 char call[256];int a=-1,b=-1,c=-1;unsigned int g1=0,g2=0,w1=0,w2=0;
 if(!strcmp(shared->command,"wallet"))strcpy_s(call,"_bridge_dispatch('wallet')");
 else if(!strcmp(shared->command,"chattail"))strcpy_s(call,"_bridge_dispatch('chattail')");
 else if(!strcmp(shared->command,"inspect"))strcpy_s(call,"_bridge_dispatch('inspect')");
 else if(!strcmp(shared->command,"searchfallback"))strcpy_s(call,"_bridge_dispatch('searchfallback')");
 else if(!strcmp(shared->command,"snapshot"))strcpy_s(call,"_bridge_dispatch('snapshot')");
 else if(!strcmp(shared->command,"details"))strcpy_s(call,"_bridge_dispatch('details')");
 else if(!strncmp(shared->command,"capture ",8)){
  __int64 after=-1;char extra=0;
  if(sscanf_s(shared->command+8,"%lld %c",&after,&extra,1u)!=1||after<0){ReleaseGIL(lock);reply("{\"error\":\"Invalid capture generation\"}");return;}
  sprintf_s(call,"_bridge_dispatch('capture',%lld)",after);
 }
 else if(!strcmp(shared->command,"stop"))strcpy_s(call,"_bridge_dispatch('stop')");
 else if(!strcmp(shared->command,"gamestate"))strcpy_s(call,"_bridge_dispatch('gamestate')");
 else if(!strcmp(shared->command,"rotationstatus"))strcpy_s(call,"_bridge_dispatch('rotationstatus')");
 else if(!strcmp(shared->command,"characterlist"))strcpy_s(call,"_bridge_dispatch('characterlist')");
 else if(!strcmp(shared->command,"rotationprepare"))strcpy_s(call,"_bridge_dispatch('rotationprepare')");
 else if(!strcmp(shared->command,"navinfo"))strcpy_s(call,"_bridge_dispatch('navinfo')");
 else if(!strcmp(shared->command,"navgrid"))strcpy_s(call,"_bridge_dispatch('navgrid')");
 else if(!strcmp(shared->command,"navstop"))strcpy_s(call,"_bridge_dispatch('navstop')");
 else if(!strcmp(shared->command,"storageopen"))strcpy_s(call,"_bridge_dispatch('storageopen')");
 else if(!strcmp(shared->command,"storageclose"))strcpy_s(call,"_bridge_dispatch('storageclose')");
 else if(!strcmp(shared->command,"rotationlogout"))strcpy_s(call,"_bridge_dispatch('rotationlogout')");
 else if(!strncmp(shared->command,"storagepassword ",16)){
  char hex[33]={},extra=0;bool valid=sscanf_s(shared->command,"storagepassword %32s %c",hex,(unsigned)sizeof(hex),&extra,1u)==1;
  auto length=strlen(hex);valid=valid&&length>=2&&length<=32&&length%2==0;
  for(size_t i=0;i<length;i++)if(!strchr("0123456789abcdefABCDEF",hex[i]))valid=false;
  if(!valid){ReleaseGIL(lock);reply("{\"error\":\"Depo parola bicimi gecersiz\"}");return;}
  sprintf_s(call,"_bridge_dispatch('storagepassword','%s')",hex);
 }
 else if(sscanf_s(shared->command,"navwalk %d %d",&a,&b)==2&&a>0&&b>0&&a<=300000&&b<=300000)sprintf_s(call,"_bridge_dispatch('navwalk',%d,%d)",a,b);
 else if(sscanf_s(shared->command,"storagedeposit %d %d",&a,&b)==2&&a>=0&&a<200&&b>=0&&b<270)sprintf_s(call,"_bridge_dispatch('storagedeposit',%d,%d)",a,b);
 else if(sscanf_s(shared->command,"storagewithdraw %d %d",&a,&b)==2&&a>=0&&a<200&&b>=0&&b<270)sprintf_s(call,"_bridge_dispatch('storagewithdraw',%d,%d)",a,b);
 else if(!strcmp(shared->command,"loginserver"))strcpy_s(call,"_bridge_dispatch('loginserver')");
 else if(!strcmp(shared->command,"loginchannel"))strcpy_s(call,"_bridge_dispatch('loginchannel')");
 else if(!strcmp(shared->command,"loginconnect"))strcpy_s(call,"_bridge_dispatch('loginconnect')");
 else if(!strcmp(shared->command,"useglass"))strcpy_s(call,"_bridge_dispatch('useglass')");
 else if(!strncmp(shared->command,"loginprepare ",13)){
  char hex[101]={};bool valid=sscanf_s(shared->command,"loginprepare %100s",hex,(unsigned)sizeof(hex))==1;auto length=strlen(hex);
  valid=valid&&length>0&&length%2==0&&length<=100;
  for(size_t i=0;i<length;i++)if(!strchr("0123456789abcdefABCDEF",hex[i]))valid=false;
  if(!valid){ReleaseGIL(lock);reply("{\"error\":\"Giris hedefi komutu gecersiz\"}");return;}
  sprintf_s(call,"_bridge_dispatch('loginprepare','%s')",hex);
 }
 else if(sscanf_s(shared->command,"loginenter %d",&a)==1&&a>=0&&a<5)sprintf_s(call,"_bridge_dispatch('loginenter',%d)",a);
 else if(!strcmp(shared->command,"salestart"))strcpy_s(call,"_bridge_dispatch('salestart')");
 else if(!strcmp(shared->command,"salepoll"))strcpy_s(call,"_bridge_dispatch('salepoll')");
 else if(!strcmp(shared->command,"salestop"))strcpy_s(call,"_bridge_dispatch('salestop')");
 else if(!strcmp(shared->command,"purchasestart"))strcpy_s(call,"_bridge_dispatch('purchasestart')");
 else if(!strcmp(shared->command,"purchasepoll"))strcpy_s(call,"_bridge_dispatch('purchasepoll')");
 else if(!strcmp(shared->command,"purchasestop"))strcpy_s(call,"_bridge_dispatch('purchasestop')");
 else if(!strcmp(shared->command,"purchasetiming 500"))strcpy_s(call,"_bridge_dispatch('purchasetiming',500)");
 else if(!strcmp(shared->command,"purchasetiming 1000"))strcpy_s(call,"_bridge_dispatch('purchasetiming',1000)");
 else if(!strncmp(shared->command,"purchasenext ",13)){
  char nonce[33]={},extra=0;bool valid=sscanf_s(shared->command,"purchasenext %32s %c",nonce,(unsigned)sizeof(nonce),&extra,1u)==1&&strlen(nonce)==32;
  for(size_t i=0;i<strlen(nonce);i++)if(!strchr("0123456789abcdef",nonce[i]))valid=false;
  if(!valid){ReleaseGIL(lock);reply("{\"error\":\"Invalid purchase completion\"}");return;}
  sprintf_s(call,"_bridge_dispatch('purchasenext','%s')",nonce);
 }
 else if(!strncmp(shared->command,"purchaseinspect ",16)){
  unsigned int slot=99;long long generation=-1;char nonce[33]={},extra=0;
  bool valid=sscanf_s(shared->command,"purchaseinspect %u %lld %32s %c",&slot,&generation,nonce,(unsigned)sizeof(nonce),&extra,1u)==3;
  valid=valid&&slot<11&&generation>=0&&strlen(nonce)==32;
  for(size_t i=0;i<strlen(nonce);i++)if(!strchr("0123456789abcdef",nonce[i]))valid=false;
  if(!valid){ReleaseGIL(lock);reply("{\"error\":\"Invalid purchase inspect\"}");return;}
  sprintf_s(call,"_bridge_dispatch('purchaseinspect',%u,%lld,'%s')",slot,generation,nonce);
 }
 else if(!strncmp(shared->command,"purchasebegin ",14)){
  char nonce[33]={},extra=0;long long budget=-1;
  bool valid=sscanf_s(shared->command,"purchasebegin %32s %lld %c",nonce,(unsigned)sizeof(nonce),&budget,&extra,1u)==2;
  valid=valid&&budget>=0&&budget<=126588&&strlen(nonce)==32;
  for(size_t i=0;i<strlen(nonce);i++)if(!strchr("0123456789abcdef",nonce[i]))valid=false;
  if(!valid){ReleaseGIL(lock);reply("{\"error\":\"Invalid purchase begin\"}");return;}
  sprintf_s(call,"_bridge_dispatch('purchasebegin','%s',%lld)",nonce,budget);
 }
 else if(!strcmp(shared->command,"marketpoll"))strcpy_s(call,"_bridge_dispatch('marketpoll')");
 else if(!strcmp(shared->command,"marketend"))strcpy_s(call,"_bridge_dispatch('marketend')");
 else if(!strncmp(shared->command,"marketbegin ",12)){
  unsigned int vid=0;char seller[51]={};bool valid=sscanf_s(shared->command,"marketbegin %u %50s",&vid,seller,(unsigned)sizeof(seller))==2;
  auto length=strlen(seller);valid=valid&&vid>0&&vid<=0x7fffffff&&length>0&&length%2==0;
  for(size_t i=0;i<length;i++)if(!strchr("0123456789abcdefABCDEF",seller[i]))valid=false;
  if(!valid){ReleaseGIL(lock);reply("{\"error\":\"Pazar dogrulama komutu gecersiz\"}");return;}
  sprintf_s(call,"_bridge_dispatch('marketbegin',%u,'%s')",vid,seller);
 }
 else if(sscanf_s(shared->command,"setsort %d",&a)==1&&a>=0&&a<=32)sprintf_s(call,"_bridge_dispatch('setsort',%d)",a);
 else if(sscanf_s(shared->command,"searchfast %d %d %d",&a,&b,&c)==3&&a>=0&&a<256&&b>=0&&b<256&&c>=0&&c<256)sprintf_s(call,"_bridge_dispatch('searchfast',%d,%d,%d)",a,b,c);
 else if(sscanf_s(shared->command,"searchprice %d %d %d %u %u %u %u",&a,&b,&c,&g1,&g2,&w1,&w2)==7&&a>=0&&a<256&&b>=0&&b<256&&c>=0&&c<256&&g1<=g2&&w1<=w2&&g2<=2000000000u&&w2<=1000000u)
  sprintf_s(call,"_bridge_dispatch('searchprice',%d,%d,%d,%u,%u,%u,%u)",a,b,c,g1,g2,w1,w2);
 else if(sscanf_s(shared->command,"search %d %d %d",&a,&b,&c)==3&&a>=0&&a<256&&b>=0&&b<256&&c>=0&&c<256)
  sprintf_s(call,"_bridge_dispatch('search',%d,%d,%d)",a,b,c);
 else if(sscanf_s(shared->command,"page %d",&a)==1&&a>0&&a<=1000000)sprintf_s(call,"_bridge_dispatch('page',%d)",a);
 else {ReleaseGIL(lock);reply("{\"error\":\"Gecersiz komut\"}");return;}
 if(!initialized||!execute(call)){ReleaseGIL(lock);reply("{\"error\":\"Python koprusu baslatilamadi\"}");return;}
 auto result=DictGet(globals,"_bridge_response");auto text=result?StringData(result):nullptr;
 reply(text?text:"{\"error\":\"Python sonuc okunamadi\"}");
 ReleaseGIL(lock);
}
static void CALLBACK tick(HWND,UINT,UINT_PTR,DWORD) {
 if(shared)InterlockedIncrement(&shared->timerHits);
 if(shared&&shared->closing){
  if(initialized){strcpy_s(shared->command,"stop");
  __try {process();} __except(EXCEPTION_EXECUTE_HANDLER) {}}
  InterlockedExchange(&shared->closed,1);
  auto old=shared;shared=nullptr;KillTimer(timerWindow,timer);UnmapViewOfFile(old);return;
 }
 if(shared&&shared->state==1){
  InterlockedExchange(&shared->stage,3);
  __try {process();} __except(EXCEPTION_EXECUTE_HANDLER){reply("{\"error\":\"Native adres erisim hatasi; tarama durduruldu\"}");}
 }
}
extern "C" __declspec(dllexport) LRESULT CALLBACK MarketHook(int code,WPARAM w,LPARAM l) {
 if(code>=0&&!shared){
  wchar_t name[80];swprintf_s(name,L"Local\\Metin2Search.Trade.%hs.%lu",M2_BRIDGE_BUILD_ID,GetCurrentProcessId());
  HANDLE mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name);
  if(mapping){
   auto candidate=(BridgeMemory*)MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(BridgeMemory));CloseHandle(mapping);
   if(candidate&&candidate->magic==M2_BRIDGE_MAGIC&&candidate->abi==M2_BRIDGE_ABI&&!strcmp(candidate->buildId,M2_BRIDGE_BUILD_ID)&&candidate->state==1)shared=candidate;else if(candidate)UnmapViewOfFile(candidate);
  }
  if(shared){
   InterlockedIncrement(&shared->hookHits);shared->ownerThread=GetCurrentThreadId();
   InterlockedExchange(&shared->stage,1);
   if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,(LPCWSTR)&MarketHook,&selfModule)){reply("{\"error\":\"Callback module pin failed\"}");return CallNextHookEx(nullptr,code,w,l);}
   timerWindow=(HWND)(UINT_PTR)shared->targetWindow;
   DWORD pid=0;auto tid=GetWindowThreadProcessId(timerWindow,&pid);
   if(tid!=GetCurrentThreadId()||pid!=GetCurrentProcessId()){reply("{\"error\":\"Ana thread/pencere eslesmedi\"}");return CallNextHookEx(nullptr,code,w,l);}
   shared->timerWindow=(DWORD)(UINT_PTR)timerWindow;
   timer=SetTimer(timerWindow,(UINT_PTR)&tick,10,tick);
   InterlockedExchange(&shared->stage,2);
   if(!timer)reply("{\"error\":\"Ana thread zamanlayicisi acilamadi\"}");
  }
 }
 return CallNextHookEx(nullptr,code,w,l);
}
BOOL WINAPI DllMain(HINSTANCE,DWORD,LPVOID){return TRUE;}
