﻿using System;
using System.Diagnostics;
using LuaInterface.LuaAPI;

namespace LuaInterface
{
	/// <summary>Base class for all Lua object references.</summary>
	public abstract class LuaBase : IDisposable
	{
		#region Constructor / State

		protected LuaBase(int reference, Lua interpreter)
		{
			Debug.Assert(interpreter != null);
			Reference = reference;
			Owner = interpreter;
		}

		protected readonly int Reference;

		/// <summary>The Lua instance that contains the referenced object.</summary>
		public Lua Owner
		{
			get
			{
				if (_interpreter == null)
				{
					//Debug.Assert(false, "LuaBase used after disposal!");
					throw new ObjectDisposedException(this.GetType().Name);
				}
				return _interpreter;
			}
			private set { _interpreter = value; }
		}
		public bool IsDisposed { get { return _interpreter == null; } }
		private Lua _interpreter; // should only be directly accessed by the above two members

		~LuaBase()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (this.IsDisposed) return;
			var owner = Owner;
			var L = owner._L;
			if (Reference >= LUA.MinRef && !L.IsNull)
			{
				// it's not safe to do this from the finalizer thread
				if (disposing)
					luaL.unref(L, Reference);
				else
					owner.Leaked(Reference);
			}
			Owner = null;
			//if (disposing)
			//	/* dispose managed objects here */;
		}

		#endregion

		#region Type Safety

		/// <summary>[-0, +1, m] Push the referenced object onto the stack.</summary>
		/// <remarks>
		/// No default implementation is provided because all implementers should be validating the type (see <see cref="CheckType(lua.State,LUA.T)"/>)
		/// If you throw an exception you should leave the stack how it was.
		/// DO NOT call <see cref="rawpush"/> from within your implementation; <see cref="rawpush"/> redirects to <see cref="push"/> in debug builds.
		/// </remarks>
		/// <exception cref="InvalidCastException">Might be thrown if the registry was tampered with or <paramref name="L"/> isn't this reference's owner. Don't bother catching this exception. It should never happen unless you're doing it wrong or there was a security breach.</exception>
		protected internal abstract void push(lua.State L);

		/// <summary>[-0, +1, -] Push the referenced object onto the stack without verifying its type matches the class. For internal use only. Should only be used when calling lua functions that can safely accept any type.</summary>
		/// <remarks>DO NOT call this from within your <see cref="push"/> implementation; it redirects to <see cref="push"/> in debug builds.</remarks>
		protected virtual void rawpush(lua.State L)
		{
			#if DEBUG
			try { push(L); return; }
			catch (Exception ex) { Debug.Fail(ex.ToString()); }
			#endif
			luaL.getref(L, Reference);
		}

		/// <summary>
		/// [-1, +0, v] Pops a value from the top of the stack and returns a reference or throws if it doesn't match the provided type.
		/// Validating the type is critically important because some Lua functions don't check the type at all, potentially corrupting memory when given unexpected input.
		/// </summary>
		protected static int TryRef(lua.State L, Lua interpreter, LUA.T t)
		{
			Debug.Assert(L == interpreter._L);
			var actual = lua.type(L,-1);
			if (actual == t)
				return luaL.@ref(L);
			else
			{
				lua.pop(L, 1);
				throw NewBadTypeError(typeof(LuaBase).Name, t, actual);
			}
		}
		/// <summary>
		/// [-0, +0, v] Checks that the instance references the given type. If it doesn't, the instance is disposed and an exception is thrown.
		/// Validating the type is critically important because some Lua functions don't check the type at all, potentially corrupting memory when given unexpected input.
		/// </summary>
		protected void CheckType(LUA.T t)
		{
			var L = Owner._L;
			luanet.checkstack(L, 1, "LuaBase.CheckType");
			luaL.getref(L, Reference);
			var actual = lua.type(L,-1);
			lua.pop(L,1);
			if (actual != t)
			{
				Dispose();
				throw NewBadTypeError(t, actual);
			}
		}
		/// <summary>
		/// [-(0|1), +0, v] Checks that the value on top of the stack is the given type. If it isn't then the value is popped, the instance is disposed, and an exception is thrown.
		/// Validating the type is critically important because some Lua functions don't check the type at all, potentially corrupting memory when given unexpected input.
		/// </summary>
		protected void CheckType(lua.State L, LUA.T t)
		{
			Debug.Assert(L == Owner._L);
			var actual = lua.type(L,-1);
			if (actual == t) return;
			lua.pop(L, 1);
			Dispose();
			throw NewBadTypeError(t, actual);
		}
		/// <summary>Exception factory for use when a <see cref="LuaBase"/> object references a Lua value of an incorrect type.</summary>
		protected static InvalidCastException NewBadTypeError(string type, object expected, object actual) {
			return new InvalidCastException(string.Format("{0} created with a {2} reference. ({1} expected)", type, expected, actual));
		}
		/// <summary>Exception factory for use when a <see cref="LuaBase"/> object references a Lua value of an incorrect type.</summary>
		protected InvalidCastException NewBadTypeError(object expected, object actual) {
			return NewBadTypeError(this.GetType().Name, expected, actual);
		}

		#endregion

		#region .NET object implementation

		/// <summary>Raw equality. (For table field lookups, Lua uses raw comparisons internally.)</summary>
		public override bool Equals(object o)
		{
			return Equals(o as LuaBase);
		}

		/// <summary>Raw equality. (For table field lookups, Lua uses raw comparisons internally.)</summary>
		public bool Equals(LuaBase o)
		{
			if (o == null || o.Owner != Owner) return false;
			var L = Owner._L;
			luanet.checkstack(L, 2, "LuaBase.Equals");
			rawpush(L);
			o.rawpush(L);
			bool ret = lua.rawequal(L, -1, -2);
			lua.pop(L,2);
			return ret;
		}

		public unsafe override int GetHashCode()
		{
			var L = Owner._L;
			luanet.checkstack(L, 1, "LuaBase.GetHashCode");
			rawpush(L);
			void* ptr = lua.topointer(L, -1);
			lua.pop(L,1);

			if (sizeof(IntPtr) == sizeof(int))
				return (int)ptr;
			else
				return ((ulong)ptr).GetHashCode();
		}

		/// <summary>Full Lua equality which can be controlled with metatables.</summary>
		public bool LuaEquals(LuaBase o)
		{
			if (o == null || o.Owner != Owner) return false;
			var L = Owner._L;
			luanet.checkstack(L, 2, "LuaBase.LuaEquals");
			rawpush(L);
			o.rawpush(L);
			try { return lua.equal(L, -1, -2); }
			finally { lua.pop(L,2); }
		}

		/// <summary>Full Lua equality which can be controlled with metatables.</summary>
		public static bool operator ==(LuaBase left, LuaBase right)
		{
			return (object)left == null
				? (object)right == null
				: left.LuaEquals(right);
		}

		/// <summary>Full Lua equality which can be controlled with metatables.</summary>
		public static bool operator !=(LuaBase left, LuaBase right)
		{
			return (object)left == null
				? (object)right != null
				: !left.LuaEquals(right);
		}

		public override string ToString()
		{
			var L = Owner._L;
			luanet.checkstack(L, 2, "LuaBase.ToString");
			luaL.getref(L, Owner.tostring_ref);
			rawpush(L);
			if (lua.pcall(L, 1, 1, 0) != LUA.ERR.Success)
				throw Owner.ExceptionFromError(L, -2); // -2, pop the error from the stack
			var str = lua.tostring(L, -1);
			lua.pop(L,1);
			return str ?? "";
		}

		#endregion

		#region Indexers

		/// <summary>Indexer for nested string fields of the Lua object.</summary>
		public object this[params string[] path]
		{
			get
			{
				var L = Owner._L;
				luanet.checkstack(L, 1, "LuaBase.get_Indexer(String[])"); StackAssert.Start(L);
				rawpush(L);
				var ret = Owner.getNestedObject(L, -1, path);
				lua.pop(L,1);                                             StackAssert.End();
				return ret;
			}
			set
			{
				var L = Owner._L;
				luanet.checkstack(L, 1, "LuaBase.set_Indexer(String[])"); StackAssert.Start(L);
				rawpush(L);
				Owner.setNestedObject(L, -1, path, value);
				lua.pop(L,1);                                             StackAssert.End();
			}
		}

		/// <summary>Indexer for string fields of the Lua object.</summary>
		public object this[string field]
		{
			get
			{
				var L = Owner._L;
				luanet.checkstack(L, 2, "LuaBase.get_Indexer(String)"); StackAssert.Start(L);
				rawpush(L);
				lua.getfield(L, -1, field);
				var obj = Owner.translator.getObject(L,-1);
				lua.pop(L,2);                                           StackAssert.End();
				return obj;
			}
			set
			{
				var L = Owner._L;
				luanet.checkstack(L, 2, "LuaBase.set_Indexer(String)"); StackAssert.Start(L);
				rawpush(L);
				Owner.translator.push(L,value);
				lua.setfield(L, -2, field);
				lua.pop(L,1);                                           StackAssert.End();
			}
		}

		/// <summary>Indexer for numeric fields of the Lua object.</summary>
		public object this[object field]
		{
			get
			{
				var L = Owner._L;
				luanet.checkstack(L, 2, "LuaBase.get_Indexer(Object)"); StackAssert.Start(L);
				rawpush(L);
				Owner.translator.push(L,field);
				lua.gettable(L,-2);
				var obj = Owner.translator.getObject(L,-1);
				lua.pop(L,2);                                           StackAssert.End();
				return obj;
			}
			set
			{
				var L = Owner._L;
				luanet.checkstack(L, 3, "LuaBase.set_Indexer(Object)"); StackAssert.Start(L);
				rawpush(L);
				Owner.translator.push(L,field);
				Owner.translator.push(L,value);
				lua.settable(L,-3);
				lua.pop(L,1);                                           StackAssert.End();
			}
		}

		/// <summary>Looks up the field and checks for a nil result. The field's value is discarded without performing any Lua to CLR translation.</summary>
		public bool ContainsKey(object field) {
			return this.FieldType(field) != LuaType.Nil;
		}

		/// <summary>Looks up the field and gets its Lua type. The field's value is discarded without performing any Lua to CLR translation.</summary>
		public LuaType FieldType(object field)
		{
			var L = Owner._L;
			luanet.checkstack(L, 2, "LuaBase.FieldType"); StackAssert.Start(L);
			push(L);
			Owner.translator.push(L, field);
			lua.gettable(L,-2);
			var type = lua.type(L, -1);
			lua.pop(L,2);                                 StackAssert.End();
			return (LuaType) type;
		}

		/// <summary>Indexer alternative that only fetches plain Lua value types and strings.</summary>
		public LuaValue GetValue(LuaValue field)
		{
			field.VerifySupport("field");
			var L = Owner._L;
			luanet.checkstack(L, 2, "LuaBase.GetValue"); StackAssert.Start(L);
			rawpush(L);
			field.push(L);
			lua.gettable(L,-2);
			var obj = LuaValue.read(L, -1, false);
			lua.pop(L,2);                                StackAssert.End();
			return obj;
		}

		/// <summary>Indexer alternative that only sets plain Lua value types and strings.</summary>
		public void SetValue(LuaValue field, LuaValue value)
		{
			field.VerifySupport("field"); value.VerifySupport("value");
			var L = Owner._L;
			luanet.checkstack(L, 3, "LuaBase.SetValue"); StackAssert.Start(L);
			rawpush(L);
			field.push(L);
			value.push(L);
			lua.settable(L,-3);
			lua.pop(L,1);                                StackAssert.End();
		}

		#endregion

		#region Length

		/// <summary>Returns the "length" of the value at the given acceptable index: for strings, this is the string length; for tables, this is the result of the length operator ('#'); for userdata, this is the size of the block of memory allocated for the userdata; for other values, it is 0. For tables, note that this is the array length (string etc. keys aren't counted) and it doesn't work reliably on sparse arrays.</summary>
		/// <seealso href="http://www.lua.org/manual/5.1/manual.html#2.5.5"/>
		public int Length { get { return this.LongLength.ToInt32(); } }

		/// <summary>Returns the "length" of the value at the given acceptable index: for strings, this is the string length; for tables, this is the result of the length operator ('#'); for userdata, this is the size of the block of memory allocated for the userdata; for other values, it is 0. For tables, note that this is the array length (string etc. keys aren't counted) and it doesn't work reliably on sparse arrays.</summary>
		/// <seealso href="http://www.lua.org/manual/5.1/manual.html#2.5.5"/>
		public UIntPtr LongLength
		{
			get
			{
				var L = Owner._L;
				luanet.checkstack(L, 1, "LuaBase.LongLength");
				rawpush(L);
				var len = lua.objlen(L, -1);
				lua.pop(L,1);
				return len;
			}
		}

		#endregion

		#region Call

		/// <summary>Calls the object and returns its return values inside an array.</summary>
		public object[] Call(params object[] args)
		{
			return this.call(args, null);
		}

		/// <summary>Calls the function casting return values to the types in returnTypes</summary>
		internal object[] call(object[] args, Type[] returnTypes)
		{
			var L = Owner._L; var translator = Owner.translator;
			int nArgs = args==null ? 0 : args.Length;
			luanet.checkstack(L, nArgs + 1, "LuaBase.call");
			int oldTop=lua.gettop(L);

			translator.push(L,this);

			for(int i = 0; i < nArgs; ++i)
				translator.push(L,args[i]);

			if (lua.pcall(L, nArgs, returnTypes == null ? LUA.MULTRET : returnTypes.Length, 0) != LUA.ERR.Success)
				throw Owner.ExceptionFromError(L, oldTop);

			return translator.popValues(L, oldTop, returnTypes);
		}

		#endregion

		#region Metatable

		/// <summary>Gets/sets the object's metatable. Note that only tables and userdata have individual metatables; setting a function's metatable will alter every function.</summary>
		public LuaTable Metatable
		{
			get
			{
				var L = Owner._L;
				luanet.checkstack(L, 2, "LuaBase.get_Metatable"); StackAssert.Start(L);
				push(L);
				if (lua.getmetatable(L,-1))
				{
					lua.remove(L, -2);                                StackAssert.End(1);
					return new LuaTable(L, Owner);
				}
				lua.pop(L,1);                                     StackAssert.End();
				return null;
			}
			set
			{
				var L = Owner._L;
				luanet.checkstack(L, 2, "LuaBase.set_Metatable");
				var oldTop = lua.gettop(L);
				push(L);
				try
				{
					if (value == null) lua.pushnil(L);
					else value.push(L);
					lua.setmetatable(L, -2);
				}
				finally { lua.settop(L, oldTop); }
			}
		}

		#endregion

		#region Environment

		/// <summary>Gets/sets the object's environment. Only functions, userdata, and threads can have an environment.</summary>
		public LuaTable Environment
		{
			get
			{
				var L = Owner._L;
				luanet.checkstack(L, 2, "LuaBase.get_Environment"); StackAssert.Start(L);
				push(L);
				lua.getfenv(L, -1);
				if (!lua.isnil(L, -1))
				{
					lua.remove(L, -2);                                  StackAssert.End(1);
					return new LuaTable(L, Owner);
				}
				lua.pop(L,2);                                       StackAssert.End();
				return null;
			}
			set
			{
				var L = Owner._L;
				luanet.checkstack(L, 2, "LuaBase.set_Environment");
				var oldTop = lua.gettop(L);
				push(L);
				try
				{
					if (value == null) lua.pushnil(L);
					else value.push(L);
					if (!lua.setfenv(L, -2))
						throw new NotSupportedException("This Lua object cannot have an environment.");
				}
				finally { lua.settop(L, oldTop); }
			}
		}

		#endregion
	}

	/// <summary>Lua types.</summary>
	public enum LuaType
	{
		//None          = LUA.T.NONE,
		Nil           = LUA.T.NIL,
		Boolean       = LUA.T.BOOLEAN,
		LightUserData = LUA.T.LIGHTUSERDATA,
		Number        = LUA.T.NUMBER,
		String        = LUA.T.STRING,
		Table         = LUA.T.TABLE,
		Function      = LUA.T.FUNCTION,
		UserData      = LUA.T.USERDATA,
		Thread        = LUA.T.THREAD,
	}
}