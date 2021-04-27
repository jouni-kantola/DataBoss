using System;
using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Reflection;
using CsvHelper;
using DataBoss.Data;

namespace DataBoss.DataPackage
{
	public class CsvDataReader : IDataReader, IDataRecordReader
	{
		class CsvDataRecord : IDataRecord
		{
			readonly string[] fieldValue;
			readonly BitArray isNull;
			readonly CsvDataReader parent;
			int rowNumber;

			public CsvDataRecord(CsvDataReader parent, int rowNumber, BitArray isNull, string[] fieldValue) {
				this.isNull = isNull;
				this.fieldValue = fieldValue;
				this.parent = parent;
				this.rowNumber = rowNumber;
			}

			public CsvDataRecord Clone() => new CsvDataRecord(parent, rowNumber, new BitArray(isNull), (string[])fieldValue.Clone());

			public void Fill(int rowNumber, CsvReader csv) {
				this.rowNumber = rowNumber;
				for (var i = 0; i != FieldCount; ++i) {
					var value = fieldValue[i] = csv.GetField(i);
					isNull[i] = IsNull(value);
				}
			}

			public object this[int i] => GetValue(i);
			public object this[string name] => GetValue(GetOrdinal(name));

			public int FieldCount => fieldValue.Length;

			public object GetValue(int i) {
				if (CheckedIsNull(i))
					return DBNull.Value;

				try {
					return ChangeType(fieldValue[i], GetFieldType(i),  GetFieldFormat(i));
				}
				catch (FormatException ex) {
					var given = isNull[i] ? "null" : $"'{fieldValue[i]}'";
					throw new InvalidOperationException($"Failed to parse {GetName(i)} of type {GetFieldType(i)} given {given} on line {rowNumber}", ex);
				}
			}
			
			bool CheckedIsNull(int i) {
				if (!isNull[i])
					return false;
				return parent.IsNullable(i) || UnexpectedNull(i);
			}

			bool UnexpectedNull(int i) => 
				throw new InvalidOperationException($"Unexpected null value for {GetName(i)} on line {rowNumber}.");

			public bool IsDBNull(int i) => isNull[i];

			public bool GetBoolean(int i) => GetFieldValue<bool>(i);
			public byte GetByte(int i) => GetFieldValue<byte>(i);
			public char GetChar(int i) => GetFieldValue<char>(i);
			public Guid GetGuid(int i) => GetFieldValue<Guid>(i);
			public short GetInt16(int i) => GetFieldValue<short>(i);
			public int GetInt32(int i) => GetFieldValue<int>(i);
			public long GetInt64(int i) => GetFieldValue<long>(i);
			public float GetFloat(int i) => GetFieldValue<float>(i);
			public double GetDouble(int i) => GetFieldValue<double>(i);
			public decimal GetDecimal(int i) => GetFieldValue<decimal>(i);
			public string GetString(int i) => CheckedIsNull(i) ? default : fieldValue[i];
			public DateTime GetDateTime(int i) => (DateTime)GetValue(i);
			public TimeSpan GetTimeSpan(int i) => GetFieldValue<TimeSpan>(i);

			public CsvInteger GetCsvInteger(int i) => GetFieldValue<CsvInteger>(i);
			public CsvNumber GetCsvNumber(int i) => GetFieldValue<CsvNumber>(i);

			T GetFieldValue<T>(int i) {
				if (CheckedIsNull(i))
					return default;

				var value = fieldValue[i];

				if (typeof(T) == typeof(bool))
					return (T)(object)bool.Parse(value);
				if (typeof(T) == typeof(byte))
					return (T)(object)byte.Parse(value, GetFieldFormat(i));
				if (typeof(T) == typeof(char))
					return (T)(object)char.Parse(value);
				if (typeof(T) == typeof(short))
					return (T)(object)short.Parse(value, GetFieldFormat(i));
				if (typeof(T) == typeof(int))
					return (T)(object)int.Parse(value, GetFieldFormat(i));
				if (typeof(T) == typeof(long))
					return (T)(object)long.Parse(value, GetFieldFormat(i));
				if (typeof(T) == typeof(float))
					return (T)(object)float.Parse(value, GetFieldFormat(i));
				if (typeof(T) == typeof(double))
					return (T)(object)double.Parse(value, GetFieldFormat(i));
				if (typeof(T) == typeof(decimal))
					return (T)(object)decimal.Parse(value, GetFieldFormat(i));

				if (typeof(T) == typeof(TimeSpan))
					return (T)(object)TimeSpan.Parse(value, GetFieldFormat(i));

				if (typeof(T) == typeof(Guid))
					return (T)(object)Guid.Parse(value);

				if (typeof(T) == typeof(CsvInteger))
					return (T)(object)new CsvInteger(value, GetFieldFormat(i));
				if (typeof(T) == typeof(CsvNumber))
					return (T)(object)new CsvNumber(value, GetFieldFormat(i));

				return (T)GetValue(i);
			}

			public int GetValues(object[] values) {
				var n = Math.Min(FieldCount, values.Length);
				for (var i = 0; i != n; ++i)
					values[i] = GetValue(i);
				return n;
			}

			public IDataReader GetData(int i) => throw new NotSupportedException();
			public long GetBytes(int i, long fieldOffset, byte[] buffer, int bufferoffset, int length) => throw new NotImplementedException();
			public long GetChars(int i, long fieldoffset, char[] buffer, int bufferoffset, int length) => throw new NotImplementedException();

			public string GetDataTypeName(int i) => parent.GetDataTypeName(i);
			public Type GetFieldType(int i) => parent.GetFieldType(i);
			IFormatProvider GetFieldFormat(int i) => parent.fieldFormat[i];

			public string GetName(int i) => parent.GetName(i);
			public int GetOrdinal(string name) => parent.GetOrdinal(name);
		}

		class RequiredAttributeProvider : ICustomAttributeProvider
		{
			readonly RequiredAttribute[] RequiredAttribute = new[] { new RequiredAttribute() };

			RequiredAttributeProvider() { }

			public static readonly RequiredAttributeProvider Instance = new RequiredAttributeProvider();

			public object[] GetCustomAttributes(bool inherit) => RequiredAttribute;

			public object[] GetCustomAttributes(Type attributeType, bool inherit) =>
				attributeType == typeof(RequiredAttribute) ? RequiredAttribute : Array.Empty<object>();

			public bool IsDefined(Type attributeType, bool inherit) =>
				attributeType == typeof(RequiredAttribute);
		}

		readonly IFormatProvider[] fieldFormat;
		int rowNumber;
		readonly CsvDataRecord current;

		readonly CsvReader csv;
		readonly DataReaderSchemaTable schema;
	
		public event EventHandler Disposed;

		public CsvDataReader(CsvReader csv, TabularDataSchema tabularSchema, bool hasHeaderRow = true) {
			var fieldCount = tabularSchema.Fields.Count;

			this.csv = csv; 
			this.schema = new DataReaderSchemaTable();
			this.fieldFormat = new IFormatProvider[fieldCount];
			this.current = new CsvDataRecord(this, 0, new BitArray(fieldCount), new string[fieldCount]);

			TranslateSchema(tabularSchema);

			if (hasHeaderRow)
				ValidateHeaderRow();
		}

		void TranslateSchema(TabularDataSchema tabularSchema) {
			var primaryKey = tabularSchema.PrimaryKey?.ToArray() ?? Empty<string>.Array;
			var fieldTypeConverter = new FieldToDbTypeConverter(primaryKey);
			for (var i = 0; i != tabularSchema.Fields.Count; ++i) {
				var field = tabularSchema.Fields[i];
				var (fieldType, dbType, providerType) = fieldTypeConverter.ToDbType(field);
				schema.Add(field.Name, i, fieldType, dbType.IsNullable, field.Constraints?.MaxLength ?? dbType.ColumnSize, dbType.TypeName, providerType);
				if (field.IsNumber())
					fieldFormat[i] = field.GetNumberFormat();
			}
		}

		void ValidateHeaderRow() {
			if(!ReadRow())
				throw new InvalidOperationException("Missing header row.");

			for (var i = 0; i != schema.Count; ++i) {
				var expected = schema[i].ColumnName;
				var actual = csv.GetField(i);
				if (actual != expected)
					throw new InvalidOperationException($"Header mismatch, expected '{expected}' got {actual}");
			}
		}

		bool ReadRow() {
			var ok = csv.Read();
			if(!ok)
				return false;
			++rowNumber;
			return true;
		}

		class FieldToDbTypeConverter
		{
			readonly Type Nullable = typeof(Nullable<>);
			readonly string[] PrimaryKey;

			public FieldToDbTypeConverter(string[] primaryKey) {
				this.PrimaryKey = primaryKey;
			}

			public (Type, DataBossDbType, Type) ToDbType(TabularDataSchemaFieldDescription field) {
				var required = field.Constraints?.IsRequired ?? Array.IndexOf(PrimaryKey, field.Name) != -1;
				return MakeType(required, field.Type switch {
					"boolean" => (typeof(bool), null),
					"datetime" => (typeof(DateTime), null),
					"date" => (typeof(DateTime), null),
					"time" => (typeof(TimeSpan), null),
					"integer" => (typeof(int), typeof(CsvInteger)),
					"number" => (typeof(double), typeof(CsvNumber)),
					"string" => field.Format switch {
						"binary" => (typeof(byte[]), null),
						"uuid" => (typeof(Guid), null),
						_ => (typeof(string), null)
					},
					_ => throw new NotSupportedException($"Don't know how to map '{field.Type}'"),
				});
			}

			(Type, DataBossDbType, Type) MakeType(bool isRequired, (Type Type, Type ProviderType) typle) {
				var (type, providerType) = typle;
				var dbType = isRequired ? GetDbTypeRequired(type) : GetDbType(type);
				return (type, dbType, providerType);
			}

			static DataBossDbType GetDbTypeRequired(Type type) =>
				DataBossDbType.From(type, RequiredAttributeProvider.Instance);

			DataBossDbType GetDbType(Type type) =>
				DataBossDbType.From(type.IsValueType ? Nullable.MakeGenericType(type) : type);
		}

		public int FieldCount => schema.Count;

		public DataTable GetSchemaTable() => schema.ToDataTable();
		
		public bool Read() {
			var ok = ReadRow();
			if(!ok)
				return false;
			current.Fill(rowNumber, csv);
			return true;
		}

		static bool IsNull(string input) => string.IsNullOrEmpty(input);

		bool IsNullable(int i) => schema[i].AllowDBNull;

		public bool NextResult() => false;

		static object ChangeType(string input, Type type, IFormatProvider format) {
			switch(Type.GetTypeCode(type)) {
				case TypeCode.DateTime:
					return DateTime.Parse(input, format);

				case TypeCode.Object:
					if (type == typeof(TimeSpan))
						return TimeSpan.Parse(input, format);
					else if(type == typeof(byte[]))
						return Convert.FromBase64String(input);
					break;
			}
			
			return Convert.ChangeType(input, type, format);
		}

		public Type GetFieldType(int i) => schema[i].ColumnType;
		public Type GetProviderSpecificFieldType(int i) => schema[i].ProviderSpecificDataType;
		public string GetDataTypeName(int i) => schema[i].DataTypeName;
		public string GetName(int i) => schema[i].ColumnName;
		public int GetOrdinal(string name) => schema.GetOrdinal(name);

		public object this[int i] => GetValue(i);
		public object this[string name] => GetValue(GetOrdinal(name));

		public void Close() { }
		public void Dispose() {
			csv.Dispose();
			Disposed?.Invoke(this, EventArgs.Empty);
		}

		public object GetValue(int i) => current.GetValue(i);
		public bool IsDBNull(int i) => current.IsDBNull(i);

		int IDataReader.Depth => throw new NotSupportedException();
		bool IDataReader.IsClosed => throw new NotSupportedException();
		int IDataReader.RecordsAffected => throw new NotSupportedException();

		public bool GetBoolean(int i) => current.GetBoolean(i);
		public byte GetByte(int i) => current.GetByte(i);
		public char GetChar(int i) => current.GetChar(i);
		public Guid GetGuid(int i) => current.GetGuid(i);
		public short GetInt16(int i) => current.GetInt16(i);
		public int GetInt32(int i) => current.GetInt32(i);
		public long GetInt64(int i) => current.GetInt64(i);
		public float GetFloat(int i) => current.GetFloat(i);
		public double GetDouble(int i) => current.GetDouble(i);
		public string GetString(int i) => current.GetString(i);
		public decimal GetDecimal(int i) => current.GetDecimal(i);
		public DateTime GetDateTime(int i) => current.GetDateTime(i);
		public TimeSpan GetTimeSpan(int i) => current.GetTimeSpan(i);

		public CsvInteger GetCsvInteger(int i) => current.GetCsvInteger(i);
		public CsvNumber GetCsvNumber(int i) => current.GetCsvNumber(i);

		public int GetValues(object[] values) {
			var n = Math.Min(FieldCount, values.Length);
			for(var i = 0; i != n; ++i)
				values[i] = GetValue(i);
			return n;
		}

		public IDataReader GetData(int i) => throw new NotSupportedException();
		public long GetBytes(int i, long fieldOffset, byte[] buffer, int bufferoffset, int length) => throw new NotImplementedException();
		public long GetChars(int i, long fieldoffset, char[] buffer, int bufferoffset, int length) => throw new NotImplementedException();

		public IDataRecord GetRecord() => current.Clone();
	}
}
