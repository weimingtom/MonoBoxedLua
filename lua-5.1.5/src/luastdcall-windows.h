#include "lua.h"

#define LUA_DLLEXPORT __declspec(dllexport)

/*
** stdcall C function
*/

typedef int (__stdcall *lua_stdcallCFunction) (lua_State *L);

/*
** push stdcall C function
*/
LUA_DLLEXPORT void lua_pushstdcallcfunction(lua_State *L, lua_stdcallCFunction fn);

/*
** safe tostring
*/
LUA_DLLEXPORT void lua_safetostring(lua_State *L,int index,char* buffer);

/*
** check metatable
*/

LUA_DLLEXPORT int luaL_checkmetatable(lua_State *L,int index);

LUA_DLLEXPORT int luanet_tonetobject(lua_State *L,int index);

LUA_DLLEXPORT void luanet_newudata(lua_State *L,int val);

LUA_DLLEXPORT void *luanet_gettag(void);

LUA_DLLEXPORT int luanet_rawnetobj(lua_State *L,int index);

LUA_DLLEXPORT int luanet_checkudata(lua_State *L,int index,const char *meta);
