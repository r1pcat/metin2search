#pragma once
#include <cmath>
static const DWORD kCharManager=0x02EDD980,kBackground=0x02EDD8E8;
static const DWORD kMoveTo=0x024F8740,kActorStop=0x027F0B50,kIsAttr=0x027EA4A0;
static bool navProfile(){
 static const BYTE move[]={0x55,0x8b,0xec,0x83,0xec,0x18,0xa1};
 static const BYTE attr[]={0x55,0x8b,0xec,0x56,0x8b,0xf1,0xe8,0x85,0xe1,0xff,0xff,0x84,0xc0,0x75,0x05,0x5e,0x5d,0xc2,0x0c,0x00};
 static const BYTE stop[]={0x55,0x8b,0xec,0x56,0x8b,0xf1,0xe8,0x25,0x5e,0x00,0x00,0x84,0xc0,0x75,0x30,0x8b,0xce,0xe8,0x5a,0x5d};
 return u32(address(0x031ee2cc))==address(0x02dfa6f0)&&u32(address(0x031ee2dc))==address(0x02dfa890)
  &&!memcmp((void*)rva(kMoveTo),move,sizeof(move))&&u32(rva(kMoveTo)+7)==address(0x031f9cd4)
  &&!memcmp((void*)rva(kIsAttr),attr,sizeof(attr))&&!memcmp((void*)rva(kActorStop),stop,sizeof(stop));
}
static DWORD navPlayer(){
 auto manager=u32(rva(kCharManager));if(manager<65536)return 0;
 auto instance=u32(manager+0x0c);return instance>=65536?instance:0;
}
static float navFloat(DWORD p){return *(float*)p;}
static bool terrainLoaded(DWORD bg,float x,float y){
 auto outdoor=u32(bg+0x44);if(outdoor<65536)return false;
 int gx=(int)(x/25600.f)-(int)*(BYTE*)(outdoor+0x98);
 int gy=(int)(y/25600.f)-(int)*(BYTE*)(outdoor+0x9a);
 if(gx < -1||gx>1||gy < -1||gy>1)return false;
 return u32(outdoor+0x54+(gx+3*gy+4)*4)>=65536;
}
static bool navClear(DWORD bg,float x,float y){
 if(!std::isfinite(x)||!std::isfinite(y)||x<1||y<1||!terrainLoaded(bg,x,y))return false;
 using Attr=bool(__thiscall*)(void*,float,float,BYTE);
 return !((Attr)rva(kIsAttr))((void*)bg,x,y,1);
}
struct NavObject {float x,y;};
static Obj* __cdecl navigation(Obj*,Obj* value){
 long withGrid=IntAsLong(value);if(withGrid!=0&&withGrid!=1){ClearError();return fail("Navigation argument invalid");}
 __try {
  if(!navProfile())return fail("Navigation address profile mismatch");
  auto player=navPlayer();auto bg=u32(rva(kBackground));auto manager=u32(rva(kCharManager));
  DWORD inv=0;int capacity=0;if(!player||bg<65536||!inventory(inv,capacity)||*(BYTE*)(inv+kChannelOffset)!=1)return fail("Navigation world unavailable");
  float x=fabsf(navFloat(player+0x7c4)),y=fabsf(navFloat(player+0x7c8));
  if(!std::isfinite(x)||!std::isfinite(y)||x>300000||y>300000)return fail("Navigation coordinates invalid");
  auto head=u32(manager+0x20),count=u32(manager+0x24);
  if(head<65536||count>20000||*(BYTE*)(head+13)!=1)return fail("Navigation entity tree invalid");
  DWORD stack[20001];int pending=0,seen=0;stack[pending++]=u32(head+4);
  DWORD npcVid=0;float npcX=39756.8f,npcY=23696.6f,best=1.e30f;
  NavObject objects[2048];int objectCount=0;
  while(pending){
   auto node=stack[--pending];if(node==head)continue;
   if(node<65536||++seen>(int)count||*(BYTE*)(node+13)!=0||pending>19997)return fail("Navigation entity tree changed");
   stack[pending++]=u32(node);stack[pending++]=u32(node+8);
   auto instance=u32(node+20);if(instance<65536)return fail("Navigation entity unavailable");
   auto type=u32(instance+0x744);float ox=fabsf(navFloat(instance+0x7c4)),oy=fabsf(navFloat(instance+0x7c8));
   if(!std::isfinite(ox)||!std::isfinite(oy))continue;
   if(type==5&&objectCount<2048)objects[objectCount++]={ox,oy};
   if(type==1&&u32(instance+0x250)==9005){
    float d=(x-ox)*(x-ox)+(y-oy)*(y-oy);
    if(d<best){best=d;npcVid=u32(node+16);npcX=ox;npcY=oy;}
   }
  }
  if(seen!=(int)count||u32(manager+0x20)!=head||u32(manager+0x24)!=count)return fail("Navigation snapshot changed");
  const int side=71;char grid[side*side+1];grid[0]=0;
  float originX=floorf(x/100.f)*100.f-3500.f,originY=floorf(y/100.f)*100.f-3500.f;
  if(withGrid){
   for(int row=0;row<side;row++)for(int col=0;col<side;col++){
    float cx=originX+col*100.f,cy=originY+row*100.f;
    bool clear=navClear(bg,cx,cy)&&navClear(bg,cx-45,cy)&&navClear(bg,cx+45,cy)&&navClear(bg,cx,cy-45)&&navClear(bg,cx,cy+45);
    for(int j=0;clear&&j<objectCount;j++)if((cx-objects[j].x)*(cx-objects[j].x)+(cy-objects[j].y)*(cy-objects[j].y)<250.f*250.f)clear=false;
    grid[row*side+col]=clear?'0':'1';
   }grid[side*side]=0;
  }
  return BuildValue("{s:f,s:f,s:i,s:f,s:f,s:i,s:i,s:f,s:f,s:s}","x",(double)x,"y",(double)y,"npcVid",npcVid,"npcX",(double)npcX,"npcY",(double)npcY,"side",side,"cell",100,"originX",(double)originX,"originY",(double)originY,"grid",grid);
 }__except(EXCEPTION_EXECUTE_HANDLER){return fail("Navigation memory unavailable");}
}
static Method navigationMethod={"_native_navigation",navigation,8,"Validated NPC/loaded terrain snapshot"};
static Obj* __cdecl walkTo(Obj*,Obj* value){
 auto text=StringData(value);int x=0,y=0;char extra=0;
 if(!text||sscanf_s(text,"%d %d %c",&x,&y,&extra,1u)!=2||x<1||y<1||x>300000||y>300000){ClearError();return fail("Walk coordinates invalid");}
 __try {
  DWORD inv=0;int cap=0;auto actor=navPlayer();auto bg=u32(rva(kBackground));
  if(!navProfile()||!actor||bg<65536||!inventory(inv,cap)||*(BYTE*)(inv+kChannelOffset)!=1)return fail("Walk profile/world unavailable");
  float dx=(float)x-fabsf(navFloat(actor+0x7c4)),dy=(float)y-fabsf(navFloat(actor+0x7c8));
  if(dx*dx+dy*dy<4.f)return BuildValue("i",0);
  if(dx*dx+dy*dy>600.f*600.f||!navClear(bg,(float)x,(float)y))return fail("Walk waypoint is too far or blocked");
  using Move=bool(__thiscall*)(void*,const float*);float xy[2]={(float)x,(float)y};
  ((Move)rva(kMoveTo))((void*)actor,xy);return BuildValue("i",1);
 }__except(EXCEPTION_EXECUTE_HANDLER){return fail("Native walk failed");}
}
static Method walkMethod={"_native_walk",walkTo,8,"One ordinary client movement waypoint"};
static Obj* __cdecl stopWalk(Obj*,Obj*){
 __try{DWORD player=0;int cap=0;auto actor=navPlayer();if(actor&&navProfile()&&inventory(player,cap)&&*(BYTE*)(player+kChannelOffset)==1){using Stop=void(__thiscall*)(void*,float);((Stop)rva(kActorStop))((void*)(actor+0x2f8),.08f);}return BuildValue("i",1);}
 __except(EXCEPTION_EXECUTE_HANDLER){return fail("Native stop failed");}
}
static Method stopWalkMethod={"_native_stop_walk",stopWalk,4,"Stop current ordinary movement"};
