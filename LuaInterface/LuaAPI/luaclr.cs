﻿using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LuaInterface.LuaAPI
{
	using size_t = System.UIntPtr;

	public static class luaclr
	{
		/// <summary>.NET 4.5 AggressiveInlining. Should be auto discarded on older build targets or otherwise ignored.</summary>
		const MethodImplOptions INLINE = (MethodImplOptions) 0x0100;

		#region reference system

		/// <summary>[-0, +1, m] Pushes a new userdata holding a reference to the specified object. The object can be retrieved by calling <see cref="getref"/>. The object cannot be garbage collected until <see cref="freeref"/> is called.<para>The userdata pushed by this function does not come with a metatable; you should assign it a metatable created by <see cref="newrefmeta(lua.State,int)"/> or <see cref="newrefmeta(lua.State,string,int)"/>.</para><para>The *ref functions are thread-safe; they can used by Lua instances running on different threads.</para></summary>
		public static unsafe void newref(lua.State L, object o, GCHandleType type = GCHandleType.Normal)
		{
			IntPtr* contents = (IntPtr*) lua.newuserdata(L, new UIntPtr((void*) sizeof(IntPtr))).ToPointer();
			*contents = GCHandle.ToIntPtr(GCHandle.Alloc(o, type));
		}
		/// <summary>[-0, +0, -] Frees a reference created by <see cref="newref"/> so the object can be garbage collected. The value at <paramref name="index"/> MUST be a reference created by <see cref="newref"/>; memory corruption or undefined behavior may result for any other value.<para>The *ref functions are thread-safe; they can used by Lua instances running on different threads.</para></summary>
		public static unsafe void freeref(lua.State L, int index)
		{
			Debug.Assert(luaclr.isreflike(L, index));
			IntPtr* contents = (IntPtr*) lua.touserdata(L, index);
			var value = *contents;
			if (value == default(IntPtr))
				return;
			*contents = default(IntPtr);
			GCHandle.FromIntPtr(value).Free();
		}
		/// <summary>[-0, +0, -] Retrieves the object from a reference created by <see cref="newref"/>. The value at <paramref name="index"/> MUST be a reference created by <see cref="newref"/>; memory corruption or undefined behavior may result for any other value.<para>Returns null if the reference has been freed.</para><para>The *ref functions are thread-safe; they can used by Lua instances running on different threads.</para></summary>
		public static unsafe object getref(lua.State L, int index)
		{
			Debug.Assert(luaclr.isreflike(L, index));
			IntPtr* contents = (IntPtr*) lua.touserdata(L, index);
			var handle = *contents;
			return handle == default(IntPtr) ? null : GCHandle.FromIntPtr(handle).Target;
		}
		/// <summary>[-0, +0, -] Checks if an object is a userdata and is the same size as a reference. Does not check the metatable. See also: <see cref="isref"/></summary>
		public static unsafe bool isreflike(lua.State L, int index)
		{
			return lua.type(L, index) == LUA.T.USERDATA
				&& lua.objlen(L, index) == new UIntPtr((void*)sizeof(IntPtr))  ;
		}

		/// <summary>[-0, +1, m] Pushes a new table which contains the minimal fields necessary for a properly functioning ref metatable. (__gc and __metatable) <paramref name="field_count"/> is the number of fields you plan to add, so more space can be pre-allocated.</summary>
		public static void newrefmeta(lua.State L, int field_count)
		{
			luaL.checkstack(L, 3, "luaclr.newrefmeta");
			lua.createtable(L, 0, field_count + 3);

			lua.pushcfunction(L, __gc);
			lua.setfield(L, -2, "__gc");

			lua.pushboolean(L, false);
			lua.setfield(L, -2, "__metatable");

			lua.pushlightuserdata(L, luanet.gettag());
			lua.pushboolean(L, true);
			lua.rawset(L, -3);
		}
		static readonly lua.CFunction __gc = L =>
		{
			if (luaclr.isref(L, 1))
				luaclr.freeref(L, 1);
			return 0;
		};
		/// <summary>[-0, +0, m] Checks if the value at <paramref name="index"/> is a reference-sized userdata with a metatable made by <see cref="newrefmeta(lua.State,int)"/>. See also: <see cref="isreflike"/></summary>
		public static bool isref(lua.State L, int index)
		{
			luaL.checkstack(L, 2, "luaclr.isref");
			index = luanet.absoluteindex(L, index);
			if (!luaclr.isreflike(L, index) || !lua.getmetatable(L, index))
				return false;
			lua.pushlightuserdata(L, luanet.gettag());
			lua.rawget(L, -2);
			bool isref = lua.type(L, -1) != LUA.T.NIL;
			lua.pop(L, 2);
			return isref;
		}
		/// <summary>[-0, +0, m] Checks if the value at <paramref name="index"/> is a metatable made by <see cref="newrefmeta(lua.State,int)"/>. See also: <see cref="isref"/></summary>
		public static bool isrefmeta(lua.State L, int index)
		{
			luaL.checkstack(L, 2, "luaclr.isrefmeta");
			index = luanet.absoluteindex(L, index);
			if (!lua.istable(L, index))
				return false;
			lua.pushlightuserdata(L, luanet.gettag());
			lua.rawget(L, index);
			bool isref = lua.type(L, -1) != LUA.T.NIL;
			lua.pop(L, 1);
			return isref;
		}
		/// <summary>
		/// <para>[-0, +1, m] <see cref="luaL.newmetatable"/> variant that uses <see cref="newrefmeta(lua.State,int)"/> to create the metatable.</para>
		/// <para>luaL.newmetatable: If the registry already has the key tname, returns false. Otherwise, creates a new table to be used as a metatable for userdata, adds it to the registry with key tname, and returns true. In both cases pushes onto the stack the final value associated with tname in the registry.</para>
		/// <para>luaclr.newrefmeta: Pushes a new table which contains the minimal fields necessary for a properly functioning ref metatable. (__gc and __metatable) <paramref name="field_count"/> is the number of fields you plan to add, so more space can be pre-allocated.</para>
		/// </summary>
		public static bool newrefmeta(lua.State L, string tname, int field_count)
		{
			lua.getfield(L, LUA.REGISTRYINDEX, tname);
			if (lua.istable(L, -1))
				return false;
			lua.pop(L, 1);
			luaclr.newrefmeta(L, field_count);
			lua.pushvalue(L, -1);
			lua.setfield(L, LUA.REGISTRYINDEX, tname);
			return true;
		}

		/// <summary>[-0, +0, m] Converts a value at the given acceptable index to a CLR reference. That value must be a CLR reference; otherwise, returns <see langword="null"/>.</summary>
		[MethodImpl(INLINE)] public static object toref(lua.State L, int index) { return luaclr.isref(L, index) ? luaclr.getref(L, index) : null; }

		/// <summary><para>[-0, +0, v] Checks whether the function argument <paramref name="narg"/> is a CLR object and returns this object.</para></summary>
		public static object checkref(lua.State L, int narg)
		{
			if (!luaclr.isref(L, narg))
				return luaL.typerror(L, narg, "CLR object");
			var o = luaclr.getref(L, narg);
			if (o == null)
				return luaL.argerror(L, narg, "CLR object reference has been freed");
			return o;
		}

		/// <summary><para>[-0, +0, v] Checks whether the function argument <paramref name="narg"/> is a CLR object with metatable <paramref name="tname"/> and returns this object.</para><para>This function assumes that <paramref name="tname"/> is a metatable created by luaclr.newrefmeta and does not check it.</para></summary>
		public static object checkref(lua.State L, int narg, string tname)
		{
			if (luaclr.isreflike(L, narg) && lua.getmetatable(L, narg))
			{
				lua.getfield(L, LUA.REGISTRYINDEX, tname);  /* get correct metatable */
				if (lua.rawequal(L, -1, -2))  /* does it have the correct mt? */
				{
					Debug.Assert(luaclr.isrefmeta(L, -1));
					lua.pop(L, 2);  /* remove both metatables */
					var o = luaclr.getref(L, narg);
					if (o == null)
						return luaL.argerror(L, narg, "CLR object reference has been freed");
					return o;
				}
			}
			return luaL.typerror(L, narg, tname);
		}

		/// <summary><para>[-0, +0, v] Checks whether the function argument <paramref name="narg"/> is a class instance of type <typeparamref name="T"/> and returns this object.</para></summary>
		public static T checkref<T>(lua.State L, int narg) where T : class { return luaclr.checkref<T>(L, narg, null); }
		/// <summary><para>[-0, +0, v] Checks whether the function argument <paramref name="narg"/> is a class instance of type <typeparamref name="T"/> and returns this object.</para></summary>
		public static T checkref<T>(lua.State L, int narg, string extramsg) where T : class
		{
			var o = luaclr.checkref(L, narg);
			var ret = o as T;
			if (ret == null)
				luaL.argerror(L, narg, string.Format(extramsg ?? "{0} expected, got {1}", typeof(T).Name, o.GetType().FullName));
			return ret;
		}

		/// <summary><para>[-0, +0, v] Checks whether the function argument <paramref name="narg"/> is a struct instance of type <typeparamref name="T"/> and returns this object.</para></summary>
		public static T checkrefstruct<T>(lua.State L, int narg) where T : struct { return luaclr.checkrefstruct<T>(L, narg, null); }
		/// <summary><para>[-0, +0, v] Checks whether the function argument <paramref name="narg"/> is a struct instance of type <typeparamref name="T"/> and returns this object.</para></summary>
		public static T checkrefstruct<T>(lua.State L, int narg, string extramsg) where T : struct
		{
			var o = luaclr.checkref(L, narg);
			if (o is T == false)
				luaL.argerror(L, narg, string.Format(extramsg ?? "{0} expected, got {1}", typeof(T).Name, o.GetType().FullName));
			return (T) o;
		}

		#endregion


		/// <summary>[-0, +1, m] Pushes a C function onto the stack just like lua_pushcfunction except the delegate will be automatically kept alive by storing an opaque userdata in an upvalue.</summary>
		[MethodImpl(INLINE)] public static void pushcfunction(lua.State L, lua.CFunction f) { luaclr.pushcclosure(L, f, 0); }
		/// <summary>[-n, +1, m] Pushes a new C closure onto the stack. <paramref name="n"/> can be up to 254; the last slot is reserved for a garbage collection userdata that keeps the delegate alive.</summary>
		public static void pushcclosure(lua.State L, lua.CFunction f, int n)
		{
			Debug.Assert(f != null);
			Debug.Assert(n <= 254 && n >= 0);
			luaL.checkstack(L, 2, "luaclr.pushcclosure");

			luaclr.newref(L, f);
			luaclr.newrefmeta(L, "LuaCLR CFunction anchor", 0);
			// nothing to add to this metatable other than gc, it just needs to exist and then not exist
			lua.setmetatable(L, -2);

			lua.pushcclosure(L, f, n+1);
		}

		/// <summary>[-0, +0, m] Retrieves the original delegate from a CFunction created by luaclr. Returns null if it is something else. See: <see cref="pushcfunction"/>, <see cref="pushcclosure"/></summary>
		public static lua.CFunction tocfunction(lua.State L, int index)
		{
			luaL.checkstack(L, 2, "luaclr.tocfunction");
			if (!lua.iscfunction(L, index))
				return null;
			// get the last upvalue, or nil if there are none
			// todo optimization: modify the Lua API to expose upvalue count or do a binary search
			lua.pushnil(L);
			for (int n = 1; n <= 255; ++n)
			{
				if (lua.getupvalue(L, index, n) == null)
					break;
				lua.remove(L, -2);
			}
			var ret = luaclr.isref(L,-1) ? luaclr.getref(L,-1) as lua.CFunction : null;
			lua.pop(L, 1);
			return ret;
		}
	}
}