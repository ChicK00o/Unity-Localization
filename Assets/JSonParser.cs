﻿/* https://github.com/ysharplanguage/FastJsonParser
 * Copyright (c) 2013, 2014 Cyril Jandia
 *
 * http://www.cjandia.com/
 *
Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
``Software''), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ``AS IS'', WITHOUT WARRANTY OF ANY KIND, EXPRESS
OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL CYRIL JANDIA BE LIABLE FOR ANY CLAIM, DAMAGES OR
OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
OTHER DEALINGS IN THE SOFTWARE.

Except as contained in this notice, the name of Cyril Jandia shall
not be used in advertising or otherwise to promote the sale, use or
other dealings in this Software without prior written authorization
from Cyril Jandia.

Inquiries : ysharp {dot} design {at} gmail {dot} com
 *
 */

// On GitHub:
// https://github.com/ysharplanguage/FastJsonParser

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace System.Text.Json
{
	public class JsonParser
	{
		private const string TypeTag1 = "__type";
		private const string TypeTag2 = "$type";
		
		private static readonly byte[] HEX = new byte[128];
		private static readonly bool[] HXD = new bool[128];
		private static readonly char[] ESC = new char[128];
		private static readonly bool[] IDF = new bool[128];
		private static readonly bool[] IDN = new bool[128];
		private const int EOF = (char.MaxValue + 1);
		private const int ANY = 0;
		private const int LBS = 128;
		private const int TDS = 128;
		
		private IDictionary<Type, int> rtti = new Dictionary<Type, int>();
		private TypeInfo[] types = new TypeInfo[TDS];
		
		private Func<int, object>[] parse = new Func<int, object>[128];
		private StringBuilder lsb = new StringBuilder();
		private char[] lbf = new char[LBS];
		private char[] stc = new char[1];
		private TextReader str;
		private Func<int, int> Char;
		private Action<int> Next;
		private Func<int> Read;
		private Func<int> Space;
		private string txt;
		private int len;
		private int lln;
		private int chr;
		private int at;
		
		internal class EnumInfo
		{
			internal string Name;
			internal long Value;
			internal int Len;
		}
		
		internal class ItemInfo
		{
			internal string Name;
			internal Action<object, JsonParser, int, int> Set;
			internal Type Type;
			internal int Outer;
			internal int Len;
		}
		
		internal class TypeInfo
		{
			private static readonly HashSet<Type> WellKnown = new HashSet<Type>();
			
			internal Func<Type, object, object, int, Func<object, object>> Select;
			internal Func<JsonParser, int, object> Parse;
			internal Func<object> Ctor;
			internal EnumInfo[] Enums;
			internal ItemInfo[] Props;
			internal ItemInfo Dico;
			internal ItemInfo List;
			internal bool IsStruct;
			internal bool IsEnum;
			internal Type EType;
			internal Type Type;
			internal int Inner;
			internal int Key;
			internal int T;
			
			static TypeInfo()
			{
				WellKnown.Add(typeof(bool));
				WellKnown.Add(typeof(char));
				WellKnown.Add(typeof(byte));
				WellKnown.Add(typeof(short));
				WellKnown.Add(typeof(int));
				WellKnown.Add(typeof(long));
				WellKnown.Add(typeof(float));
				WellKnown.Add(typeof(double));
				WellKnown.Add(typeof(decimal));
				WellKnown.Add(typeof(DateTime));
				WellKnown.Add(typeof(DateTimeOffset));
				WellKnown.Add(typeof(string));
			}
			
			private Func<object> GetCtor(Type clr, bool list)
			{
				var type = (!list ? ((clr == typeof(object)) ? typeof(Dictionary<string, object>) : clr) : typeof(List<>).MakeGenericType(clr));
				var ctor = type.GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.CreateInstance, null, System.Type.EmptyTypes, null);
				if (ctor != null)
				{
					var dyn = new System.Reflection.Emit.DynamicMethod("", typeof(object), null, typeof(string), true);
					var il = dyn.GetILGenerator();
					il.Emit(System.Reflection.Emit.OpCodes.Newobj, ctor);
					il.Emit(System.Reflection.Emit.OpCodes.Ret);
					return (Func<object>)dyn.CreateDelegate(typeof(Func<object>));
				}
				return null;
			}
			
			private Func<object> GetCtor(Type clr, Type key, Type value)
			{
				var type = typeof(Dictionary<,>).MakeGenericType(key, value);
				var ctor = (type = (((type != clr) && clr.IsClass) ? clr : type)).GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.CreateInstance, null, System.Type.EmptyTypes, null);
				var dyn = new System.Reflection.Emit.DynamicMethod("", typeof(object), null, typeof(string), true);
				var il = dyn.GetILGenerator();
				il.Emit(System.Reflection.Emit.OpCodes.Newobj, ctor);
				il.Emit(System.Reflection.Emit.OpCodes.Ret);
				return (Func<object>)dyn.CreateDelegate(typeof(Func<object>));
			}
			
			private EnumInfo[] GetEnumInfos(Type type)
			{
				var einfo = new Dictionary<string, EnumInfo>();
				foreach (var name in System.Enum.GetNames(type))
					einfo.Add(name, new EnumInfo { Name = name, Value = Convert.ToInt64(System.Enum.Parse(type, name)), Len = name.Length });
				return einfo.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
			}
			
			private ItemInfo GetItemInfo(Type type, string name, System.Reflection.MethodInfo setter)
			{
				var method = new System.Reflection.Emit.DynamicMethod("Set" + name, null, new Type[] { typeof(object), typeof(JsonParser), typeof(int), typeof(int) }, typeof(string), true);
				var parse = GetParserParse(GetParseName(type));
				var il = method.GetILGenerator();
				il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
				il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
				il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2);
				il.Emit(System.Reflection.Emit.OpCodes.Callvirt, parse);
				if (type.IsValueType && (parse.ReturnType == typeof(object)))
					il.Emit(System.Reflection.Emit.OpCodes.Unbox_Any, type);
				if (parse.ReturnType.IsValueType && (type == typeof(object)))
					il.Emit(System.Reflection.Emit.OpCodes.Box, parse.ReturnType);
				il.Emit(System.Reflection.Emit.OpCodes.Callvirt, setter);
				il.Emit(System.Reflection.Emit.OpCodes.Ret);
				return new ItemInfo { Type = type, Name = name, Set = (Action<object, JsonParser, int, int>)method.CreateDelegate(typeof(Action<object, JsonParser, int, int>)), Len = name.Length };
			}
			
			private ItemInfo GetItemInfo(Type type, Type key, Type value, System.Reflection.MethodInfo setter)
			{
				var method = new System.Reflection.Emit.DynamicMethod("Add", null, new Type[] { typeof(object), typeof(JsonParser), typeof(int), typeof(int) }, typeof(string), true);
				var sBrace = typeof(JsonParser).GetMethod("SBrace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				var eBrace = typeof(JsonParser).GetMethod("EBrace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				var kColon = typeof(JsonParser).GetMethod("KColon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				var sComma = typeof(JsonParser).GetMethod("SComma", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				var vParse = GetParserParse(GetParseName(value));
				var kParse = GetParserParse(GetParseName(key));
				var il = method.GetILGenerator();
				il.DeclareLocal(key);
				il.DeclareLocal(value);
				
				il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
				il.Emit(System.Reflection.Emit.OpCodes.Callvirt, sBrace);
				
				il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
				il.Emit(System.Reflection.Emit.OpCodes.Callvirt, kColon);
				
				il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
				il.Emit(System.Reflection.Emit.OpCodes.Ldarg_3);
				il.Emit(System.Reflection.Emit.OpCodes.Callvirt, kParse);
				if (key.IsValueType && (kParse.ReturnType == typeof(object)))
					il.Emit(System.Reflection.Emit.OpCodes.Unbox_Any, key);
				if (kParse.ReturnType.IsValueType && (key == typeof(object)))
					il.Emit(System.Reflection.Emit.OpCodes.Box, kParse.ReturnType);
				il.Emit(System.Reflection.Emit.OpCodes.Stloc_0);
				
				il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
				il.Emit(System.Reflection.Emit.OpCodes.Callvirt, sComma);
				
				il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
				il.Emit(System.Reflection.Emit.OpCodes.Callvirt, kColon);
				
				il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
				il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2);
				il.Emit(System.Reflection.Emit.OpCodes.Callvirt, vParse);
				if (value.IsValueType && (vParse.ReturnType == typeof(object)))
					il.Emit(System.Reflection.Emit.OpCodes.Unbox_Any, value);
				if (vParse.ReturnType.IsValueType && (value == typeof(object)))
					il.Emit(System.Reflection.Emit.OpCodes.Box, vParse.ReturnType);
				il.Emit(System.Reflection.Emit.OpCodes.Stloc_1);
				
				il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
				il.Emit(System.Reflection.Emit.OpCodes.Callvirt, eBrace);
				
				il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
				il.Emit(System.Reflection.Emit.OpCodes.Ldloc_0);
				il.Emit(System.Reflection.Emit.OpCodes.Ldloc_1);
				il.Emit(System.Reflection.Emit.OpCodes.Callvirt, setter);
				il.Emit(System.Reflection.Emit.OpCodes.Ret);
				return new ItemInfo { Type = type, Name = String.Empty, Set = (Action<object, JsonParser, int, int>)method.CreateDelegate(typeof(Action<object, JsonParser, int, int>)) };
			}
			
			private Type GetEnumUnderlyingType(Type enumType)
			{
				return enumType.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)[0].FieldType;
			}
			
			protected string GetParseName(Type type)
			{
				var typeName = (!WellKnown.Contains(type) ? ((type.IsEnum && WellKnown.Contains(GetEnumUnderlyingType(type))) ? GetEnumUnderlyingType(type).Name : null) : type.Name);
				return ((typeName != null) ? String.Concat("Parse", typeName) : null);
			}
			
			protected System.Reflection.MethodInfo GetParserParse(string pName)
			{
				return typeof(JsonParser).GetMethod((pName ?? "Val"), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			}
			
			protected TypeInfo(Type type, int self, Type eType, Type kType, Type vType)
			{
				var props = ((self > 2) ? type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public) : new System.Reflection.PropertyInfo[] { });
				var infos = new Dictionary<string, ItemInfo>();
				IsStruct = type.IsValueType;
				IsEnum = type.IsEnum;
				EType = eType;
				Type = type;
				T = self;
				Ctor = (((kType != null) && (vType != null)) ? GetCtor(Type, kType, vType) : GetCtor((EType ?? Type), (EType != null)));
				for (var i = 0; i < props.Length; i++)
				{
					System.Reflection.PropertyInfo pi;
					System.Reflection.MethodInfo set;
					if ((pi = props[i]).CanWrite && ((set = pi.GetSetMethod()).GetParameters().Length == 1))
						infos.Add(pi.Name, GetItemInfo(pi.PropertyType, pi.Name, set));
				}
				Dico = (((kType != null) && (vType != null)) ? GetItemInfo(Type, kType, vType, typeof(Dictionary<,>).MakeGenericType(kType, vType).GetMethod("Add", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)) : null);
				List = ((EType != null) ? GetItemInfo(EType, String.Empty, typeof(List<>).MakeGenericType(EType).GetMethod("Add", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)) : null);
				Enums = (IsEnum ? GetEnumInfos(Type) : null);
				Props = infos.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
			}
		}
		
		internal class TypeInfo<T> : TypeInfo
		{
			internal Func<JsonParser, int, T> Value;
			
			private Func<JsonParser, int, R> GetParseFunc<R>(string pName)
			{
				var parse = GetParserParse(pName ?? "Key");
				if (parse != null)
				{
					var method = new System.Reflection.Emit.DynamicMethod(parse.Name, typeof(R), new Type[] { typeof(JsonParser), typeof(int) }, typeof(string), true);
					var il = method.GetILGenerator();
					il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
					il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
					il.Emit(System.Reflection.Emit.OpCodes.Callvirt, parse);
					il.Emit(System.Reflection.Emit.OpCodes.Ret);
					return (Func<JsonParser, int, R>)method.CreateDelegate(typeof(Func<JsonParser, int, R>));
				}
				return null;
			}
			
			internal TypeInfo(int self, Type eType, Type kType, Type vType)
				: base(typeof(T), self, eType, kType, vType)
			{
				var value = (Value = GetParseFunc<T>(GetParseName(typeof(T))));
				Parse = (Func<JsonParser, int, object>)((parser, outer) => value(parser, outer));
			}
		}
		
		static JsonParser()
		{
			for (char c = '0'; c <= '9'; c++) { HXD[c] = true; HEX[c] = (byte)(c - 48); }
			for (char c = 'A'; c <= 'F'; c++) { HXD[c] = HXD[c + 32] = true; HEX[c] = HEX[c + 32] = (byte)(c - 55); }
			ESC['/'] = '/'; ESC['\\'] = '\\';
			ESC['b'] = '\b'; ESC['f'] = '\f'; ESC['n'] = '\n'; ESC['r'] = '\r'; ESC['t'] = '\t'; ESC['u'] = 'u';
			for (int c = ANY; c < 128; c++) if (ESC[c] == ANY) ESC[c] = (char)c;
			for (int c = '0'; c <= '9'; c++) IDN[c] = true;
			IDF['_'] = IDN['_'] = true;
			for (int c = 'A'; c <= 'Z'; c++) IDF[c] = IDN[c] = IDF[c + 32] = IDN[c + 32] = true;
		}
		
		private Exception Error(string message) { return new Exception(System.String.Format("{0} at {1} (found: '{2}')", message, at, ((chr < EOF) ? ("\\" + chr) : "EOF"))); }
		private void Reset(Func<int> read, Action<int> next, Func<int, int> achar, Func<int> space) { at = -1; chr = ANY; Read = read; Next = next; Char = achar; Space = space; }
		
		private int StreamSpace() { if (chr <= ' ') while ((chr = (str.Read(stc, 0, 1) > 0) ? stc[0] : EOF) <= ' ') ; return chr; }
		private int StreamRead() { return (chr = (str.Read(stc, 0, 1) > 0) ? stc[0] : EOF); }
		private void StreamNext(int ch) { if (chr != ch) throw Error("Unexpected character"); chr = ((str.Read(stc, 0, 1) > 0) ? stc[0] : EOF); }
		private int StreamChar(int ch)
		{
			if (lln >= LBS)
			{
				if (lsb.Length == 0)
					lsb.Append(new string(lbf, 0, lln));
				lsb.Append((char)ch);
			}
			else
				lbf[lln++] = (char)ch;
			return (chr = (str.Read(stc, 0, 1) > 0) ? stc[0] : EOF);
		}
		
		private int StringSpace() { if (chr <= ' ') while ((++at < len) && ((chr = txt[at]) <= ' ')) ; return chr; }
		private int StringRead() { return (chr = (++at < len) ? txt[at] : EOF); }
		private void StringNext(int ch) { if (chr != ch) throw Error("Unexpected character"); chr = ((++at < len) ? txt[at] : EOF); }
		private int StringChar(int ch)
		{
			if (lln >= LBS)
			{
				if (lsb.Length == 0)
					lsb.Append(new string(lbf, 0, lln));
				lsb.Append((char)ch);
			}
			else
				lbf[lln++] = (char)ch;
			return (chr = (++at < len) ? txt[at] : EOF);
		}
		
		private int Esc(int ec)
		{
			int cp = 0, ic = -1, ch;
			if (ec == 'u')
			{
				while ((++ic < 4) && ((ch = Read()) <= 'f') && HXD[ch]) { cp *= 16; cp += HEX[ch]; }
				if (ic < 4) throw Error("Invalid Unicode character");
				ch = cp;
			}
			else
				ch = ESC[ec];
			Read();
			return ch;
		}
		
		private int CharEsc(int ec)
		{
			int cp = 0, ic = -1, ch;
			if (ec == 'u')
			{
				while ((++ic < 4) && ((ch = Read()) <= 'f') && HXD[ch]) { cp *= 16; cp += HEX[ch]; }
				if (ic < 4) throw Error("Invalid Unicode character");
				ch = cp;
			}
			else
				ch = ESC[ec];
			Char(ch);
			return ch;
		}
		
		private EnumInfo GetEnumInfo(TypeInfo type)
		{
			var a = type.Enums; int n = a.Length, c = 0, i = 0, ch;
			var e = false;
			EnumInfo ei;
			if (n > 0)
			{
				while (true)
				{
					if ((ch = chr) == '"') { Read(); return (((i < n) && (c > 0)) ? a[i] : null); }
					if (e = (ch == '\\')) ch = Read();
					if (ch < EOF) { if (!e || (ch >= 128)) Read(); else { ch = Esc(ch); e = false; } } else break;
					while ((i < n) && ((c >= (ei = a[i]).Len) || (ei.Name[c] != ch))) i++; c++;
				}
			}
			return null;
		}
		
		private bool ParseBoolean(int outer)
		{
			int ch = Space();
			bool k;
			if (k = ((outer > 0) && (ch == '"'))) ch = Read();
			switch (ch)
			{
			case 'f': Read(); Next('a'); Next('l'); Next('s'); Next('e'); if (k) Next('"'); return false;
			case 't': Read(); Next('r'); Next('u'); Next('e'); if (k) Next('"'); return true;
			default: throw Error("Bad boolean");
			}
		}
		
		private char ParseChar(int outer)
		{
			int ch = Space();
			if (ch == '"')
			{
				ch = Read();
				lln = 0;
					switch (ch) { case '\\': ch = Read(); CharEsc(ch); Next('"'); break; default: Char(ch); Next('"'); break; }
				return lbf[0];
			}
			throw Error("Bad character");
		}
		
		private byte ParseByte(int outer)
		{
			bool b = false, k;
			int ch = Space();
			byte n = 0;
			TypeInfo t;
			if (k = ((outer > 0) && (ch == '"')))
			{
				ch = Read();
				if ((t = types[outer]).IsEnum && ((ch < '0') || (ch > '9')))
				{
					var e = GetEnumInfo(t);
					if (e == null) throw Error(System.String.Format("Bad enum value ({0})", t.Type.FullName));
					return (byte)e.Value;
				}
			}
			while ((ch >= '0') && (ch <= '9') && (b = true)) { checked { n *= 10; n += (byte)(ch - 48); } ch = Read(); }
			if (!b) throw Error("Bad number (byte)"); if (k) Next('"');
			return n;
		}
		
		private short ParseInt16(int outer)
		{
			short it = 1, n = 0;
			bool b = false, k;
			int ch = Space();
			TypeInfo t;
			if (k = ((outer > 0) && (ch == '"')))
			{
				ch = Read();
				if ((t = types[outer]).IsEnum && (ch != '-') && ((ch < '0') || (ch > '9')))
				{
					var e = GetEnumInfo(t);
					if (e == null) throw Error(System.String.Format("Bad enum value ({0})", t.Type.FullName));
					return (short)e.Value;
				}
			}
			if (ch == '-') { ch = Read(); it = (short)-it; }
			while ((ch >= '0') && (ch <= '9') && (b = true)) { checked { n *= 10; n += (short)(ch - 48); } ch = Read(); }
			if (!b) throw Error("Bad number (short)"); if (k) Next('"');
			return (short)checked(it * n);
		}
		
		private int ParseInt32(int outer)
		{
			int it = 1, n = 0;
			bool b = false, k;
			int ch = Space();
			TypeInfo t;
			if (k = ((outer > 0) && (ch == '"')))
			{
				ch = Read();
				if ((t = types[outer]).IsEnum && (ch != '-') && ((ch < '0') || (ch > '9')))
				{
					var e = GetEnumInfo(t);
					if (e == null) throw Error(System.String.Format("Bad enum value ({0})", t.Type.FullName));
					return (int)e.Value;
				}
			}
			if (ch == '-') { ch = Read(); it = -it; }
			while ((ch >= '0') && (ch <= '9') && (b = true)) { checked { n *= 10; n += (ch - 48); } ch = Read(); }
			if (!b) throw Error("Bad number (int)"); if (k) Next('"');
			return checked(it * n);
		}
		
		private long ParseInt64(int outer)
		{
			long it = 1, n = 0;
			bool b = false, k;
			int ch = Space();
			TypeInfo t;
			if (k = ((outer > 0) && (ch == '"')))
			{
				ch = Read();
				if ((t = types[outer]).IsEnum && (ch != '-') && ((ch < '0') || (ch > '9')))
				{
					var e = GetEnumInfo(t);
					if (e == null) throw Error(System.String.Format("Bad enum value ({0})", t.Type.FullName));
					return e.Value;
				}
			}
			if (ch == '-') { ch = Read(); it = -it; }
			while ((ch >= '0') && (ch <= '9') && (b = true)) { checked { n *= 10; n += (ch - 48); } ch = Read(); }
			if (!b) throw Error("Bad number (long)"); if (k) Next('"');
			return checked(it * n);
		}
		
		private float ParseSingle(int outer)
		{
			bool b = false, k;
			int ch = Space();
			string s;
			lsb.Length = 0; lln = 0;
			if (k = ((outer > 0) && (ch == '"'))) ch = Read();
			if (ch == '-') ch = Char(ch);
			while ((ch >= '0') && (ch <= '9') && (b = true)) ch = Char(ch);
			if (ch == '.') { ch = Char(ch); while ((ch >= '0') && (ch <= '9')) ch = Char(ch); }
			if ((ch == 'e') || (ch == 'E')) { ch = Char(ch); if ((ch == '-') || (ch == '+')) ch = Char(ch); while ((ch >= '0') && (ch <= '9')) ch = Char(ch); }
			if (!b) throw Error("Bad number (float)"); if (k) Next('"');
			s = ((lsb.Length > 0) ? lsb.ToString() : new string(lbf, 0, lln));
			return float.Parse(s);
		}
		
		private double ParseDouble(int outer)
		{
			bool b = false, k;
			int ch = Space();
			string s;
			lsb.Length = 0; lln = 0;
			if (k = ((outer > 0) && (ch == '"'))) ch = Read();
			if (ch == '-') ch = Char(ch);
			while ((ch >= '0') && (ch <= '9') && (b = true)) ch = Char(ch);
			if (ch == '.') { ch = Char(ch); while ((ch >= '0') && (ch <= '9')) ch = Char(ch); }
			if ((ch == 'e') || (ch == 'E')) { ch = Char(ch); if ((ch == '-') || (ch == '+')) ch = Char(ch); while ((ch >= '0') && (ch <= '9')) ch = Char(ch); }
			if (!b) throw Error("Bad number (double)"); if (k) Next('"');
			s = ((lsb.Length > 0) ? lsb.ToString() : new string(lbf, 0, lln));
			return double.Parse(s);
		}
		
		private decimal ParseDecimal(int outer)
		{
			bool b = false, k;
			int ch = Space();
			string s;
			lsb.Length = 0; lln = 0;
			if (k = ((outer > 0) && (ch == '"'))) ch = Read();
			if (ch == '-') ch = Char(ch);
			while ((ch >= '0') && (ch <= '9') && (b = true)) ch = Char(ch);
			if (ch == '.') { ch = Char(ch); while ((ch >= '0') && (ch <= '9')) ch = Char(ch); }
			if (!b) throw Error("Bad number (decimal)"); if (k) Next('"');
			s = ((lsb.Length > 0) ? lsb.ToString() : new string(lbf, 0, lln));
			return decimal.Parse(s);
		}
		
		private void PastKey()
		{
			var ch = Space();
			var e = false;
			if (ch == '"')
			{
				Read();
				while (true)
				{
					if ((ch = chr) == '"') { Read(); return; }
					if (e = (ch == '\\')) ch = Read();
					if (ch < EOF) { if (!e || (ch >= 128)) Read(); else { Esc(ch); e = false; } } else break;
				}
			}
			throw Error("Bad key");
		}
		
		private string ParseString(int outer)
		{
			var ch = Space();
			var e = false;
			if (ch == '"')
			{
				Read();
				lsb.Length = 0; lln = 0;
				while (true)
				{
					if ((ch = chr) == '"') { Read(); return ((lsb.Length > 0) ? lsb.ToString() : new string(lbf, 0, lln)); }
					if (e = (ch == '\\')) ch = Read();
					if (ch < EOF) { if (!e || (ch >= 128)) Char(ch); else { CharEsc(ch); e = false; } } else break;
				}
			}
			if (ch == 'n')
				return (string)Null(0);
			throw Error((outer >= 0) ? "Bad string" : "Bad key");
		}
		
		private DateTimeOffset ParseDateTimeOffset(int outer)
		{
			DateTimeOffset dateTimeOffset;
			if (!DateTimeOffset.TryParse(ParseString(0), System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.RoundtripKind, out dateTimeOffset))
				throw Error("Bad date/time offset");
			return dateTimeOffset;
		}
		
		private DateTime ParseDateTime(int outer)
		{
			DateTime dateTime;
			if (!DateTime.TryParse(ParseString(0), System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.RoundtripKind, out dateTime))
				throw Error("Bad date/time");
			return dateTime;
		}
		
		private ItemInfo GetPropInfo(TypeInfo type)
		{
			var a = type.Props; int ch = Space(), n = a.Length, c = 0, i = 0;
			var e = false;
			ItemInfo pi;
			if (ch == '"')
			{
				Read();
				while (true)
				{
					if ((ch = chr) == '"') { Read(); return (((i < n) && (c > 0)) ? a[i] : null); }
					if (e = (ch == '\\')) ch = Read();
					if (ch < EOF) { if (!e || (ch >= 128)) Read(); else { ch = Esc(ch); e = false; } } else break;
					while ((i < n) && ((c >= (pi = a[i]).Len) || (pi.Name[c] != ch))) i++; c++;
				}
			}
			throw Error("Bad key");
		}
		
		private object Error(int outer) { throw Error("Bad value"); }
		private object Null(int outer) { Read(); Next('u'); Next('l'); Next('l'); return null; }
		private object False(int outer) { Read(); Next('a'); Next('l'); Next('s'); Next('e'); return false; }
		private object True(int outer) { Read(); Next('r'); Next('u'); Next('e'); return true; }
		
		private object Num(int outer)
		{
			var ch = chr;
			var b = false;
			lsb.Length = 0; lln = 0;
			if (ch == '-') ch = Char(ch);
			while ((ch >= '0') && (ch <= '9') && (b = true)) ch = Char(ch);
			if (ch == '.') { ch = Char(ch); while ((ch >= '0') && (ch <= '9')) ch = Char(ch); }
			if ((ch == 'e') || (ch == 'E')) { ch = Char(ch); if ((ch == '-') || (ch == '+')) ch = Char(ch); while ((ch >= '0') && (ch <= '9')) ch = Char(ch); }
			if (!b) throw Error("Bad number");
			return ((lsb.Length > 0) ? lsb.ToString() : new string(lbf, 0, lln));
		}
		
		private object Key(int outer) { return ParseString(-1); }
		
		private object Str(int outer) { var s = ParseString(0); if ((outer != 2) || ((s != null) && (s.Length == 1))) return ((outer == 2) ? (object)s[0] : s); else throw Error("Bad character"); }
		
		private object Obj(int outer)
		{
			var cached = types[outer]; var hash = types[cached.Key]; var select = cached.Select; var ctor = cached.Ctor;
			var mapper = (null as Func<object, object>);
			var typed = ((outer > 0) && (cached.Dico == null) && (ctor != null));
			var parse = hash.Parse;
			var keyed = hash.T;
			var ch = chr;
			if (ch == '{')
			{
				object obj;
				Read();
				ch = Space();
				if (ch == '}')
				{
					Read();
					return ctor();
				}
				obj = null;
				while (ch < EOF)
				{
					var prop = (typed ? GetPropInfo(cached) : null);
					var slot = (!typed ? parse(this, keyed) : null);
					Func<object, object> read = null;
					Space();
					Next(':');
					if (slot != null)
					{
						if ((select == null) || ((read = select(cached.Type, obj, slot, -1)) != null))
						{
							var val = Val(cached.Inner);
							var key = (slot as string);
							if (obj == null)
							{
								if ((key != null) && ((String.Compare(key, TypeTag1) == 0) || (String.Compare(key, TypeTag2) == 0)))
								{
									obj = (((key = (val as string)) != null) ? (cached = types[Entry(Type.GetType(key, true), null)]).Ctor() : ctor());
									typed = !(obj is IDictionary);
								}
								else
									obj = ctor();
							}
							if (!typed)
								((IDictionary)obj).Add(slot, val);
						}
						else
							Val(0);
					}
					else if (prop != null)
					{
						if ((select == null) || ((read = select(cached.Type, obj, prop.Name, -1)) != null))
						{
							obj = (obj ?? ctor());
							prop.Set(obj, this, prop.Outer, 0);
						}
						else
							Val(0);
					}
					else
						Val(0);
					mapper = (mapper ?? read);
					ch = Space();
					if (ch == '}')
					{
						mapper = (mapper ?? Identity);
						Read();
						return mapper(obj ?? ctor());
					}
					Next(',');
					ch = Space();
				}
			}
			throw Error("Bad object");
		}
		
		private void SBrace() { Next('{'); }
		private void EBrace() { Space(); Next('}'); }
		private void KColon() { PastKey(); Space(); Next(':'); }
		private void SComma() { Space(); Next(','); }
		
		private object Arr(int outer)
		{
			var cached = types[(outer != 0) ? outer : 1]; var select = cached.Select; var dico = (cached.Dico != null);
			var mapper = (null as Func<object, object>);
			var items = (dico ? cached.Dico : cached.List);
			var val = cached.Inner;
			var key = cached.Key;
			var ch = chr;
			var i = -1;
			if (ch == '[')
			{
				object obj;
				Read();
				ch = Space();
				obj = cached.Ctor();
				if (ch == ']')
				{
					Read();
					if (cached.Type.IsArray)
					{
						IList list = (IList)obj;
						var array = Array.CreateInstance(cached.EType, list.Count);
						list.CopyTo(array, 0);
						return array;
					}
					return obj;
				}
				while (ch < EOF)
				{
					Func<object, object> read = null;
					i++;
					if (dico || (select == null) || ((read = select(cached.Type, obj, null, i)) != null))
						items.Set(obj, this, val, key);
					else
						Val(0);
					mapper = (mapper ?? read);
					ch = Space();
					if (ch == ']')
					{
						mapper = (mapper ?? Identity);
						Read();
						if (cached.Type.IsArray)
						{
							IList list = (IList)obj;
							var array = Array.CreateInstance(cached.EType, list.Count);
							list.CopyTo(array, 0);
							return mapper(array);
						}
						return mapper(obj);
					}
					Next(',');
					ch = Space();
				}
			}
			throw Error("Bad array");
		}
		
		private object Val(int outer)
		{
			return parse[Space() & 0x7f](outer);
		}
		
		private Type GetElementType(Type type)
		{
			if (type.IsArray)
				return type.GetElementType();
			else if ((type != typeof(string)) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
				return (type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object));
			else
				return null;
		}
		
		private Type Realizes(Type type, Type generic)
		{
			var itfs = type.GetInterfaces();
			foreach (var it in itfs)
				if (it.IsGenericType && it.GetGenericTypeDefinition() == generic)
					return type;
			if (type.IsGenericType && type.GetGenericTypeDefinition() == generic)
				return type;
			if (type.BaseType == null)
				return null;
			return Realizes(type.BaseType, generic);
		}
		
		private bool GetKeyValueTypes(Type type, out Type key, out Type value)
		{
			var generic = (Realizes(type, typeof(Dictionary<,>)) ?? Realizes(type, typeof(IDictionary<,>)));
			var kvPair = ((generic != null) && (generic.GetGenericArguments().Length == 2));
			value = (kvPair ? generic.GetGenericArguments()[1] : null);
			key = (kvPair ? generic.GetGenericArguments()[0] : null);
			return kvPair;
		}
		
		private int Closure(int outer, IDictionary<Type, Func<Type, object, object, int, Func<object, object>>> filter)
		{
			var prop = types[outer].Props;
			for (var i = 0; i < prop.Length; i++) prop[i].Outer = Entry(prop[i].Type, filter);
			return outer;
		}
		
		private int Entry(Type type, IDictionary<Type, Func<Type, object, object, int, Func<object, object>>> filter)
		{
			Func<Type, object, object, int, Func<object, object>> select = null;
			int outer;
			if (!rtti.TryGetValue(type, out outer))
			{
				Type et, kt, vt;
				bool dico = GetKeyValueTypes(type, out kt, out vt);
				et = (!dico ? GetElementType(type) : null);
				outer = rtti.Count;
				types[outer] = (TypeInfo)Activator.CreateInstance(typeof(TypeInfo<>).MakeGenericType(type), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new object[] { outer, et, kt, vt }, null);
				rtti.Add(type, outer);
				types[outer].Inner = ((et != null) ? Entry(et, filter) : (dico ? Entry(vt, filter) : 0));
				if (dico) types[outer].Key = Entry(kt, filter);
			}
			if ((filter != null) && filter.TryGetValue(type, out select))
				types[outer].Select = select;
			return Closure(outer, filter);
		}
		
		private T DoParse<T>(string input, IDictionary<Type, Func<Type, object, object, int, Func<object, object>>> filter)
		{
			var outer = Entry(typeof(T), filter);
			len = input.Length;
			txt = input;
			Reset(StringRead, StringNext, StringChar, StringSpace);
			return (typeof(T).IsValueType ? ((TypeInfo<T>)types[outer]).Value(this, outer) : (T)Val(outer));
		}
		
		private T DoParse<T>(TextReader input, IDictionary<Type, Func<Type, object, object, int, Func<object, object>>> filter)
		{
			var outer = Entry(typeof(T), filter);
			str = input;
			Reset(StreamRead, StreamNext, StreamChar, StreamSpace);
			return (typeof(T).IsValueType ? ((TypeInfo<T>)types[outer]).Value(this, outer) : (T)Val(outer));
		}
		
		public JsonParser()
		{
			parse['n'] = Null; parse['f'] = False; parse['t'] = True;
			parse['0'] = parse['1'] = parse['2'] = parse['3'] = parse['4'] =
				parse['5'] = parse['6'] = parse['7'] = parse['8'] = parse['9'] =
					parse['-'] = Num; parse['"'] = Str; parse['{'] = Obj; parse['['] = Arr;
			for (var input = 0; input < 128; input++) parse[input] = (parse[input] ?? Error);
			Entry(typeof(object), null);
			Entry(typeof(List<object>), null);
			Entry(typeof(char), null);
		}
		
		public static object Identity(object obj) { return obj; }
		
		public static readonly Func<object, object> Skip = null;
		
		public T Parse<T>(string input) { return Parse<T>(input, null); }
		
		public T Parse<T>(string input, IDictionary<Type, Func<Type, object, object, int, Func<object, object>>> mappers)
		{
			if (input == null) throw new ArgumentNullException("input", "cannot be null");
			return DoParse<T>(input, mappers);
		}
		
		public T Parse<T>(TextReader input) { return Parse<T>(input, null); }
		
		public T Parse<T>(TextReader input, IDictionary<Type, Func<Type, object, object, int, Func<object, object>>> mappers)
		{
			if (input == null) throw new ArgumentNullException("input", "cannot be null");
			return DoParse<T>(input, mappers);
		}
		
		public T Parse<T>(Stream input) { return Parse<T>(input, null as Encoding); }
		
		public T Parse<T>(Stream input, Encoding encoding) { return Parse<T>(input, encoding, null); }
		
		public T Parse<T>(Stream input, IDictionary<Type, Func<Type, object, object, int, Func<object, object>>> mappers) { return Parse<T>(input, null, mappers); }
		
		public T Parse<T>(Stream input, Encoding encoding, IDictionary<Type, Func<Type, object, object, int, Func<object, object>>> mappers)
		{
			if (input == null) throw new ArgumentNullException("input", "cannot be null");
			return DoParse<T>((encoding != null) ? new StreamReader(input, encoding) : new StreamReader(input), mappers);
		}
	}
}