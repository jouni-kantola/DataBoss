using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;

namespace DataBoss.Data
{
	public class DbDataReaderAdapter : DbDataReader
	{
		readonly IDataReader inner;
		readonly IFieldValueReader fieldValueReader;
		Func<int, Type> getProviderSpecificFieldType;
		Func<int, object> getProviderSpecificValue;
		Func<object[], int> getProviderSpecificValues;

		interface IFieldValueReader
		{
			T GetFieldValue<T>(int ordinal);
		}

		class FieldValueReader<TReader> : IFieldValueReader
		{
			readonly TReader instance;

			public FieldValueReader(TReader reader) { 
				this.instance = reader;
			}

			public T GetFieldValue<T>(int ordinal) => FieldValueOfT<T>.GetT(instance, ordinal);

			static class FieldValueOfT<T>
			{
				public static readonly Func<TReader, int, T> GetT = FieldValueReader.MakeFieldReader<TReader, T>();
			}
		}

		class FieldValueReader : IFieldValueReader
		{
			readonly IDataReader reader;

			public FieldValueReader(IDataReader reader) { this.reader = reader; }

			public T GetFieldValue<T>(int ordinal) => reader.GetFieldValue<T>(ordinal);

			public static IFieldValueReader For(IDataReader reader) {
				var readerType = reader.GetType();
				if(readerType.GetMethod(nameof(GetFieldValue)) != null)
					return (IFieldValueReader)Activator.CreateInstance(typeof(FieldValueReader<>).MakeGenericType(readerType), reader);
				return new FieldValueReader(reader);
			}

			internal static Func<TReader, int, T> MakeFieldReader<TReader, T>() {
				var x = Expression.Parameter(typeof(TReader), "x");
				var ordinal = Expression.Parameter(typeof(int), "ordinal");
				var getT = x.Type.GetMethod(nameof(GetFieldValue)).MakeGenericMethod(typeof(T));
				return Expression.Lambda<Func<TReader, int, T>>(Expression.Call(x, getT, ordinal), x, ordinal)
					.Compile();
			}
		}

		public DbDataReaderAdapter(IDataReader inner) { 
			this.inner = inner;
			this.fieldValueReader = FieldValueReader.For(inner);
			this.getProviderSpecificFieldType = InitGetProviderSpecificFiledType;
			this.getProviderSpecificValue = InitGetProvilderSpecificFieldValue;
			this.getProviderSpecificValues = InitGetProviderSpecificValues;
		}

		Type InitGetProviderSpecificFiledType(int ordinal) => 
			UpdateTargetDelegate(ref getProviderSpecificFieldType, inner, nameof(GetProviderSpecificFieldType), nameof(GetFieldType))(ordinal);

		object InitGetProvilderSpecificFieldValue(int ordinal) => 
			UpdateTargetDelegate(ref getProviderSpecificValue, inner, nameof(GetProviderSpecificValue), nameof(GetValue))(ordinal);

		int InitGetProviderSpecificValues(object[] values) =>
			UpdateTargetDelegate(ref getProviderSpecificValues, inner, nameof(GetProviderSpecificValues), nameof(GetValues))(values);

		static T UpdateTargetDelegate<T>(ref T target, object instance, string optional, string fallback) where T : Delegate {
			var type = target.GetType();
			return target = Lambdas.CreateDelegate<T>(instance, type.GetMethod(optional) ?? type.GetMethod(fallback));
		}

		public override object this[int ordinal] => inner[ordinal];
		public override object this[string name] => inner[name];

		public override bool HasRows => throw new NotSupportedException();

		public override int Depth => inner.Depth;
		public override int FieldCount => inner.FieldCount;
		public override bool IsClosed => inner.IsClosed;
		public override int RecordsAffected => inner.RecordsAffected;

		public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
		public override byte GetByte(int ordinal) => inner.GetByte(ordinal);

		public override long GetBytes(int ordinal, long dataOffset, byte[] buffer, int bufferOffset, int length) =>
			inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);

		public override char GetChar(int ordinal) => inner.GetChar(ordinal);
		public override long GetChars(int ordinal, long dataOffset, char[] buffer, int bufferOffset, int length) =>
			inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);

		public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
		public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
		public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
		public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
		public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
		public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
		public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
		public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
		public override string GetString(int ordinal) => inner.GetString(ordinal);
		public override object GetValue(int ordinal) => inner.GetValue(ordinal);
		public override int GetValues(object[] values) => inner.GetValues(values);
		public override object GetProviderSpecificValue(int ordinal) => getProviderSpecificValue(ordinal);
		public override int GetProviderSpecificValues(object[] values) => getProviderSpecificValues(values);
		public override T GetFieldValue<T>(int ordinal) => fieldValueReader.GetFieldValue<T>(ordinal);

		public override DataTable GetSchemaTable() => inner.GetSchemaTable();
		public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
		public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
		public override Type GetProviderSpecificFieldType(int ordinal) => getProviderSpecificFieldType(ordinal);
		public override string GetName(int ordinal) => inner.GetName(ordinal);
		public override int GetOrdinal(string name) => inner.GetOrdinal(name);

		public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);

		public override void Close() => inner.Close();
		public override bool Read() => inner.Read();
		public override bool NextResult() => inner.NextResult();

		public override IEnumerator GetEnumerator() => new DataReaderEnumerator(inner);
	}

	class DataReaderEnumerator : IEnumerator
	{
		readonly IDataReader reader;

		public DataReaderEnumerator(IDataReader reader) {
			this.reader = reader;
		}

		public object Current => (IDataRecord)reader;

		public bool MoveNext() => reader.Read();
		public void Reset() => throw new NotSupportedException();
	}
}
