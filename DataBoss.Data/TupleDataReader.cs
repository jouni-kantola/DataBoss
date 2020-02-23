using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Linq;
using System.Data;

namespace DataBoss.Data
{
	public interface IRecordDataReader : IDataReader
	{
		IDataRecord GetCurrentRecord();
	}

	public class TupleDataReader
	{
		public static TupleDataReader<T> Create<T>(IEnumerable<T> items) {
			var schema = GetSchema(typeof(T).GetGenericArguments());
			return new TupleDataReader<T>(items.GetEnumerator(), schema);
		}

		static DataReaderSchemaTable GetSchema(params Type[] fieldTypes) {
			var schema = new DataReaderSchemaTable();
			for (var i = 0; i != fieldTypes.Length; ++i) {
				var (t, isNullable) = GetFieldType(fieldTypes[i]);
				schema.Add(ItemName(i), i, t, isNullable);
			}

			return schema;
		}

		static (Type FieldType, bool IsNullable) GetFieldType(Type t) {
			var isNullable = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>);
			return isNullable ? (t.GetGenericArguments()[0], true) : (t, false);
		}

		static internal string ItemName(int i) => $"Item{i + 1}";

		internal static class ItemOf<T, TValue>
		{
			public static Func<T, int, TValue> GetValue = GetAccessor<T, TValue>();
		}

		internal static Func<T, int, bool> GetIsNull<T>() {
			var arg0 = Expression.Parameter(typeof(T), "x");
			var index = Expression.Parameter(typeof(int), "i");

			var n = 0;
			var items = typeof(T).GetGenericArguments()
				.Select(x => (Ordinal: n++, Field: GetFieldType(x)));

			return Expression.Lambda<Func<T, int, bool>>(
				Expression.Switch(
					index,
					Expression.Throw(Expression.New(typeof(InvalidOperationException)), typeof(bool)),
					null,
					items.Select(x => Expression.SwitchCase(
						GetIsNull(arg0, x.Ordinal, x.Field.IsNullable),
						Expression.Constant(x.Ordinal)))),
				arg0, index).Compile();
		}

		static Expression GetIsNull(Expression arg0, int ordinal, bool isNullable) {
			var get = Expression.MakeMemberAccess(arg0, arg0.Type.GetField(ItemName(ordinal)));
			if (isNullable)
				return Expression.Not(Expression.MakeMemberAccess(get, get.Type.GetProperty(nameof(Nullable<int>.HasValue))));

			if (!get.Type.IsValueType)
				return Expression.ReferenceEqual(get, Expression.Constant(null, get.Type));

			return Expression.Constant(false);
		}

		internal static Func<T, int, TValue> GetAccessor<T, TValue>() {
			var arg0 = Expression.Parameter(typeof(T), "x");
			var index = Expression.Parameter(typeof(int), "i");

			var n = 0;
			var items = typeof(T).GetGenericArguments()
				.Select(x => (Ordinal: n++, Field: GetFieldType(x)))
				.Where(x => x.Field.FieldType == typeof(TValue));


			return Expression.Lambda<Func<T, int, TValue>>(
				Expression.Switch(
					index,
					Expression.Throw(Expression.New(typeof(InvalidCastException)), typeof(TValue)),
					null,
					items.Select(x => Expression.SwitchCase(
						GetValue(arg0, x.Ordinal, x.Field.IsNullable),
						Expression.Constant(x.Ordinal)))),
				arg0, index).Compile();
		}

		internal static Func<T, int, object> GetAccessor<T>() {
			var arg0 = Expression.Parameter(typeof(T), "x");
			var index = Expression.Parameter(typeof(int), "i");

			var n = 0;
			var items = typeof(T).GetGenericArguments()
				.Select(x => (Ordinal: n++, Field: GetFieldType(x)));

			return Expression.Lambda<Func<T, int, object>>(
				Expression.Switch(
					index,
					Expression.Throw(Expression.New(typeof(InvalidOperationException)), typeof(object)),
					null,
					items.Select(x => Expression.SwitchCase(
						Expression.Convert(GetValue(arg0, x.Ordinal, x.Field.IsNullable), typeof(object)),
						Expression.Constant(x.Ordinal)))),
				arg0, index).Compile();
		}

		static Expression GetValue(Expression arg0, int ordinal, bool isNullable) {
			var get = Expression.MakeMemberAccess(arg0, arg0.Type.GetField(ItemName(ordinal)));
			if (!isNullable)
				return get;
			return Expression.Call(get, get.Type.GetMethod(nameof(Nullable<int>.GetValueOrDefault), Type.EmptyTypes));
		}
	}

	public class TupleDataRecord<T> : IDataRecord
	{
		static readonly Func<T, int, bool> ItemIsNull = TupleDataReader.GetIsNull<T>();
		static readonly Func<T, int, object> GetItem = TupleDataReader.GetAccessor<T>();
		static class ItemOf<TValue>
		{
			public static readonly Func<T, int, TValue> GetItem = TupleDataReader.GetAccessor<T, TValue>();
		}

		public readonly DataReaderSchemaTable Schema;
		public T Current;

		internal TupleDataRecord(DataReaderSchemaTable schema) {
			this.Schema = schema;
		}

		public TValue GetValue<TValue>(int i) => ItemOf<TValue>.GetItem(Current, i);
		public object GetValue(int i) => IsDBNull(i) ? DBNull.Value : GetItem(Current, i);

		public int FieldCount => Schema.Count;
		public object this[int i] => GetValue(i);
		public object this[string name] => GetValue(GetOrdinal(name));

		public bool IsDBNull(int i) => ItemIsNull(Current, i);

		public string GetName(int i) => Schema[i].ColumnName;
		public Type GetFieldType(int i) => Schema[i].ColumnType;
		public int GetOrdinal(string name) => Schema.GetOrdinal(name);

		public bool GetBoolean(int i) => GetValue<bool>(i);
		public byte GetByte(int i) => GetValue<byte>(i);
		public char GetChar(int i) => GetValue<char>(i);
		public Guid GetGuid(int i) => GetValue<Guid>(i);
		public short GetInt16(int i) => GetValue<short>(i);
		public int GetInt32(int i) => GetValue<int>(i);
		public long GetInt64(int i) => GetValue<long>(i);
		public float GetFloat(int i) => GetValue<float>(i);
		public double GetDouble(int i) => GetValue<double>(i);
		public string GetString(int i) => GetValue<string>(i);
		public decimal GetDecimal(int i) => GetValue<decimal>(i);
		public DateTime GetDateTime(int i) => GetValue<DateTime>(i);

		public int GetValues(object[] values) {
			var end = Math.Min(values.Length, FieldCount);
			for (var i = 0; i != end; ++i)
				values[i] = GetValue(i);
			return end;
		}

		public string GetDataTypeName(int i) {
			throw new NotImplementedException();
		}

		public long GetBytes(int i, long fieldOffset, byte[] buffer, int bufferoffset, int length) {
			throw new NotImplementedException();
		}

		public long GetChars(int i, long fieldoffset, char[] buffer, int bufferoffset, int length) {
			throw new NotImplementedException();
		}

		public IDataReader GetData(int i) {
			throw new NotImplementedException();
		}
	}

	public class TupleDataReader<T> : IRecordDataReader 
	{
		readonly TupleDataRecord<T> record;
		IEnumerator<T> items;

		public TupleDataReader(IEnumerator<T> items, DataReaderSchemaTable schema) {
			this.items = items;
			this.record = new TupleDataRecord<T>(schema);
		}

		public IDataRecord GetCurrentRecord() => new TupleDataRecord<T>(record.Schema) { Current = record.Current };

		TValue GetValue<TValue>(int i) => record.GetValue<TValue>(i);

		public object this[int i] => GetValue(i);
		public object this[string name] => GetValue(GetOrdinal(name));

		public int Depth => 0;
		public bool IsClosed => items == null;
		public int RecordsAffected => -1;
		public int FieldCount => record.FieldCount;

		public void Close() {
			items.Dispose();
			items = null;
		}

		public void Dispose() => items?.Dispose();

		public DataTable GetSchemaTable() => record.Schema.ToDataTable();

		public bool GetBoolean(int i) => GetValue<bool>(i);
		public byte GetByte(int i) => GetValue<byte>(i);
		public char GetChar(int i) => GetValue<char>(i);
		public DateTime GetDateTime(int i) => GetValue<DateTime>(i);
		public decimal GetDecimal(int i) => GetValue<decimal>(i);
		public double GetDouble(int i) => GetValue<double>(i);
		public float GetFloat(int i) => GetValue<float>(i);
		public Guid GetGuid(int i) => GetValue<Guid>(i);
		public short GetInt16(int i) => GetValue<short>(i);
		public int GetInt32(int i) => GetValue<int>(i);
		public long GetInt64(int i) => GetValue<long>(i);
		public string GetString(int i) => GetValue<string>(i);

		public Type GetFieldType(int i) => record.GetFieldType(i);
		public string GetName(int i) => record.GetName(i);
		public int GetOrdinal(string name) => record.GetOrdinal(name);

		public object GetValue(int i) => record.GetValue(i);
		public int GetValues(object[] values) => record.GetValues(values);

		public long GetBytes(int i, long fieldOffset, byte[] buffer, int bufferoffset, int length) => record.GetBytes(i, fieldOffset, buffer, bufferoffset, length);
		public long GetChars(int i, long fieldoffset, char[] buffer, int bufferoffset, int length) => record.GetChars(i, fieldoffset, buffer, bufferoffset, length);
		public IDataReader GetData(int i) => record.GetData(i);
		public string GetDataTypeName(int i) => record.GetDataTypeName(i);

		public bool IsDBNull(int i) => record.IsDBNull(i);

		public bool Read() {
			var r = items.MoveNext();
			if(r)
				record.Current = items.Current;
			return r;
		}

		public bool NextResult() => false;
	}
}
