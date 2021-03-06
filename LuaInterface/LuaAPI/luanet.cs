﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LuaInterface.LuaAPI
{
	using size_t = System.UIntPtr;

	/// <summary>LuaInterface library functions for working with the Lua API.</summary>
	public static class luanet
	{
		const string DLL = "lua51.dll";
		const CallingConvention CC = CallingConvention.Cdecl;
		/// <summary>.NET 4.5 AggressiveInlining. Should be auto discarded on older build targets or otherwise ignored.</summary>
		const MethodImplOptions INLINE = (MethodImplOptions) 0x0100;

		/// <summary>Flags for Lua access to the CLR.</summary>
		public const BindingFlags LuaBindingFlags = BindingFlags.Public /*| BindingFlags.IgnoreCase/*| BindingFlags.NonPublic*/;

		/// <summary>[-0, +0, -] The address of a static variable in the luanet DLL. The variable's contents are never used. Rather, the address itself serves as a unique identifier for luanet metatables.</summary>
		[DllImport(DLL,CallingConvention=CC,EntryPoint="luanet_gettag")] public static extern IntPtr gettag();

		/// <summary>
		/// [-0, +1, e] Navigates fields nested in an object at the specified index, pushing the value of the specified sub-field. If <paramref name="fields"/> is empty it pushes a copy of the main object.
		/// <para>WARNING: If the IEnumerable throws an exception during enumeration the stack will be left in a +1 state. You must ensure that no exceptions can be thrown or you must catch them and clean up the stack.</para>
		/// </summary>
		public static void getnestedfield(lua.State L, int index, IEnumerable<string> fields)
		{
			luaL.checkstack(L, 2, "luanet.getnestedfield");
			Debug.Assert(fields != null);                StackAssert.Start(L);
			lua.pushvalue(L,index);
			foreach (string field in fields) {
				lua.getfield(L, -1, field);
				lua.remove(L,-2);
			}                                            StackAssert.End(1);
		}
		/// <summary>[-0, +1, e] Navigates fields nested in an object at the top of the stack, pushing the value of the specified sub-field</summary>
		/// <param name="index"></param><param name="L"></param>
		/// <param name="path">A string with field names separated by period characters.</param>
		[MethodImpl(INLINE)] public static void getnestedfield(lua.State L, int index, string path)
		{
			Debug.Assert(path != null);
			luanet.getnestedfield(L, index, path.Split('.'));
		}

		/// <summary>[-(nargs + 1), +(nresults|0), -]</summary>
		[MethodImpl(INLINE)] public static Exception pcall(lua.State L, int nargs, int nresults)
		{
			if (lua.pcall(L, nargs, nresults, 0) == LUA.ERR.Success)
				return null;
			var message = lua.tostring(L,-1) ?? "Error message was a "+lua.type(L,-1).ToString();
			lua.pop(L,1);
			return new LuaScriptException(message, ""); // todo
		}

		/// <summary>[-0, +0, -] Gets a <see cref="Lua"/> instance's internal lua_State pointer.</summary>
		[MethodImpl(INLINE)] public static lua.State getstate(Lua interpreter)
		{
			return interpreter._L;
		}
		/// <summary>[-0, +1, e] Pushes the referenced object onto its owner's stack.</summary>
		[MethodImpl(INLINE)] public static void pushobject<T>(T o)
		where T : LuaBase // generic method rather than just taking a LuaBase parameter might make it easier to do sealed class optimizations
		{
			o.push(o.Owner._L);
		}
		/// <summary>[-0, +1, e] Pushes an arbitrary CLR object onto the stack.</summary>
		[MethodImpl(INLINE)] public static void pushobject(Lua lua, object o)
		{
			lua.translator.push(lua._L, o);
		}
		/// <summary>[-0, +0, m] Gets a Lua object from the stack and translates it to a CLR object.</summary>
		[MethodImpl(INLINE)] public static object getobject(Lua lua, int index)
		{
			return lua.translator.getObject(lua._L, index);
		}

		/// <summary>[-0, +0, -] Checks what kind of index this is. <see cref="IndexTypes.Invalid"/> represents zero and very large negative numbers.</summary>
		public static IndexTypes indextype(int index)
		{
			if (index > 0)  return IndexTypes.Absolute;
			// comparisons with LUA_*INDEX is what Lua does internally
			if (index > LUA.REGISTRYINDEX) return index == 0 ? IndexTypes.Invalid : IndexTypes.Relative;
			if (index >= LUA.GLOBALSINDEX) return IndexTypes.Pseudo;
			if (index >= lua.upvalueindex(256)) return IndexTypes.Upvalue; // http://www.lua.org/manual/5.1/manual.html#3.4
			return IndexTypes.Invalid;
		}
		/// <summary>Returned by <see cref="luanet.indextype"/>.</summary>
		public enum IndexTypes { Invalid, Absolute, Relative, Pseudo, Upvalue }

		/// <summary>[-0, +0, -] Converts relative index to a normal (absolute) index. If the relative index goes out of the stack bounds, 0 is returned. If <paramref name="index"/> is not a relative index it is returned unchanged.</summary>
		public static int absoluteindex(lua.State L, int index)
		{
			if (index >= 0 || index <= LUA.REGISTRYINDEX)
				return index;
			int i = lua.gettop(L) + index + 1;
			return i < 0 ? 0 : i;
		}

		/// <summary>[-0, +0, -] Converts the Lua value at the given acceptable index to the C type lua_Number. The Lua value must be a number or a string convertible to a number; otherwise, false is returned and <paramref name="value"/> is zero.</summary>
		public static bool trygetnumber(lua.State L, int index, out double value)
		{
			var num = value = lua.tonumber(L, index);
			return num != 0 || lua.isnumber(L, index);
		}

		/// <summary>[-0, +0, -] Returns whether the currently executing code was called by Lua.</summary>
		public static bool infunction(lua.State L)
		{
			// this function should resolve to the same thing as "L->ci != L->base_ci" in internal lua code
			var ar = new lua.Debug();
			return lua.getstack(L, 0, ref ar); // returns unsuccessful if there is no stack
		}

		/// <summary>[-0, +0, m] luaL.where clone that returns a string rather than pushing to the stack.</summary>
		public static string where(lua.State L, int level)
		{
			var ar = new lua.Debug();
			if (lua.getstack(L, level, ref ar))  // check function at level
			{
				lua.getinfo(L, "Sl", ref ar);  // get info about it
				if (ar.currentline > 0)  // is there info?
					return string.Format("{0}:{1}: ", ar.short_src, ar.currentline.ToString()); // tostring here avoids boxing
			}
			return "";  // else, no information available...
		}

		/// <summary>[-0, +0, m] Grows the stack size to top + <paramref name="sz"/> elements, throwing a CLR exception if the stack cannot grow to that size.</summary>
		/// <param name="L"></param><param name="sz"></param><param name="mes">An additional text to go into the error message.</param>
		/// <exception cref="LuaScriptException"></exception>
		public static void checkstack(lua.State L, int sz, string mes)
		{
			Debug.Assert(!string.IsNullOrEmpty(mes));
			if (!lua.checkstack(L, sz))
				throw new LuaScriptException(string.Format("stack overflow ({0})", mes), luanet.where(L, 1));
		}
		
		/// <summary>[-0, +0, m] Checks whether the value at the given acceptable index is of the type <paramref name="tname"/> (see <see cref="luaL.newmetatable"/>).</summary>
		public static bool hasmetatable(lua.State L, int index, string tname)
		{
			luaL.checkstack(L, 2, "luanet.hasmetatable");
			if (!lua.getmetatable(L, index))
				return false;
			lua.getfield(L, LUA.REGISTRYINDEX, tname);
			bool ret = lua.rawequal(L, -1, -2);
			lua.pop(L, 2);
			return ret;
		}

		#region summarizetable

		/// <summary>[-0, +0, e] Generates a human readable summary of a table's contents.</summary>
		/// <param name="L"></param><param name="index"></param>
		/// <param name="strings_limit">When the list of string keys grows larger than this it will stop adding more. This limit applies to the number of characters, not the number of keys.</param>
		public static string summarizetable(lua.State L, int index, int strings_limit)
		{
			luaL.checkstack(L,2,"luanet.summarizetable");
			index = luanet.absoluteindex(L, index);
			Debug.Assert(lua.istable(L,index));
			uint objlen;
			unsafe
			{
				void* objlen_p = lua.objlen(L,index).ToPointer();
				if (objlen_p > (void*)uint.MaxValue)
					return "really big";
				objlen = unchecked((uint)objlen_p);
			}
			uint c_str = 0, c_str_added = 0, c_other = 0; // count_
			int len_str = 0;
			// build the output as a list of strings which are only concatenated at the very end
			var o = new List<string>(7) {"length: ", objlen.ToString(), ", non-string/array keys: ", "", ", strings: ", "", " ("};

			var old_top = lua.gettop(L);
			try
			{
				lua.pushnil(L);
				while (lua.next(L,index))
				{
					if (lua.type(L,-2) != LUA.T.STRING)
						++c_other;
					else
					{
						++c_str;
						if (len_str < strings_limit)
						{
							++c_str_added;
							var s = lua.tostring(L,-2);
							if (_containsWhitespace(s))
								s = "“"+s+"”";
							len_str += s.Length + 1;
							o.Add(s);
							switch (lua.type(L,-1))
							{
							case LUA.T.FUNCTION:
								len_str += 2;
								o.Add("() ");
								break;
							case LUA.T.TABLE:
								len_str += 3;
								o.Add("={} ");
								break;
							default:
								o.Add(" ");
								break;
							}
						}
					}
					lua.pop(L,1);
				}
			}
			#if DEBUG
			finally { lua.settop(L, old_top); }
			#else
			catch { lua.settop(L, old_top); throw; }
			#endif

			if (c_str == 0)
				o.RemoveRange(4,3);
			else
			{
				o[5] = c_str.ToString();
				if (c_str != c_str_added)
					o.Add("...)");
				else
				{
					var last = o[o.Count - 1];
					o[o.Count - 1] = last.Substring(0,last.Length-1); // remove the space from the end
					o.Add(")");
				}
			}

			// length can be larger than the actual number of keys if it's a sparse array
			if (objlen <= c_other)
				c_other -= objlen;
			else
				c_other = 0;

			if (c_other == 0)
				o[2] = "";
			else
				o[3] = c_other.ToString();

			return string.Concat(o.ToArray());
		}
		static bool _containsWhitespace(string s)
		{
			for (int i = 0; i < s.Length; ++i)
				if (Char.IsWhiteSpace(s[i])) return true;
			return false;
		}

		#endregion

		#region obsolete
		#if false
		/// <summary>[-0, +1, m] Pushes a delegate onto the stack as a callable userdata.</summary>
		[Obsolete("Use lua.pushcfunction")]
		[DllImport(DLL,CallingConvention=CC,EntryPoint="lua_pushstdcallcfunction")] public static extern void pushstdcallcfunction(lua.State L, luanet.CSFunction function);

		/// <summary>Delegate for functions passed to Lua as function pointers</summary>
		[Obsolete("Use lua.CFunction")]
		[UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int CSFunction(lua.State L);

		/// <summary>[-0, +0, -, requires checkstack(3)] Checks if the object at <paramref name="index"/> has a metatable containing a field with a light userdata key matching <see cref="luanet.gettag"/>.</summary>
		[Obsolete("Use luaclr")] [DllImport(DLL,CallingConvention=CC,EntryPoint="luaL_checkmetatable")] public static extern bool   checkmetatable(lua.State L, int index);
		/// <summary>[-0, +1, m] Pushes a new luanet userdata object, which stores a single integer, onto the stack. The object does not have a metatable by default.</summary>
		[Obsolete("Use luaclr")] [DllImport(DLL,CallingConvention=CC,EntryPoint="luanet_newudata"    )] public static extern int    newudata      (lua.State L, int val);
		/// <summary>[-0, +0, -, requires checkstack(3)] Retrieves the int stored in a luanet userdata object. Returns -1 if <see cref="luanet.checkmetatable"/> fails and the object's metatable isn't luaNet_class, luaNet_searchbase, or luaNet_function.</summary>
		[Obsolete("Use luaclr")] [DllImport(DLL,CallingConvention=CC,EntryPoint="luanet_tonetobject" )] public static extern int    tonetobject   (lua.State L, int index);
		/// <summary>[-0, +0, -] Like <see cref="luanet.tonetobject"/>, but doesn't perform the safety checks. Only use this if you're completely sure the value is a luanet userdata.</summary>
		[Obsolete("Use luaclr")] [DllImport(DLL,CallingConvention=CC,EntryPoint="luanet_rawnetobj"   )] public static extern int    rawnetobj     (lua.State L, int index);
		/// <summary>[-0, +0, -, requires checkstack(2)] Checks if the specified userdata object uses the specified metatable. If so, it does the same thing as <see cref="luanet.rawnetobj"/>. Otherwise, returns -1.</summary>
		[Obsolete("Use luaclr")] [DllImport(DLL,CallingConvention=CC,EntryPoint="luanet_checkudata"  )] public static extern int    checkudata    (lua.State L, int index, string meta);
		#endif
		#endregion
	}
}