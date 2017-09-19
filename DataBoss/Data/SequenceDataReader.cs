using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;

namespace DataBoss.Data
{
	static class DataReaderSchemaColumns
	{
		public const string AllowDBNull = "AllowDBNull";
		public const string ColumnSize = "ColumnSize";
	}

	public static class SequenceDataReader
	{
		public static SequenceDataReader<T> Create<T>(IEnumerable<T> data, Action<FieldMapping<T>> mapFields) {
			var fieldMapping = new FieldMapping<T>();
			mapFields(fieldMapping);
			return new SequenceDataReader<T>(data.GetEnumerator(), fieldMapping);
		}

		public static SequenceDataReader<T> Create<T>(IEnumerable<T> data, params string[] members) {
			return Create(data, fields => Array.ForEach(members, x => fields.Map(x)));
		}

		public static SequenceDataReader<T> Create<T>(IEnumerable<T> data, params MemberInfo[] members) {
			return Create(data, fields => Array.ForEach(members, x => fields.Map(x)));
		}
	}

	public class SequenceDataReader<T> : IDataReader
	{
		readonly object[] current;
		readonly Action<T,object[]> accessor;
		readonly IEnumerator<T> data;
		readonly string[] fieldNames;
		readonly Type[] fieldTypes;
		readonly DataBossDbType[] dbTypes;
	
		public SequenceDataReader(IEnumerator<T> data, FieldMapping<T> fields) {
			this.data = data;
			this.fieldNames = fields.GetFieldNames();
			this.fieldTypes = fields.GetFieldTypes();
			this.accessor = fields.GetAccessor();
			this.dbTypes = fields.GetDbTypes();
			this.current = new object[fieldNames.Length];
		}

		public object this[int i] => GetValue(i);
		public object this[string name] => GetValue(GetOrdinal(name));

		public int FieldCount => current.Length;
	
		public bool Read() { 
			if(!data.MoveNext())
				return false;
			accessor(data.Current, current);
			return true;
		}

		public bool NextResult() => false;

		public void Close() { }

		public void Dispose() => data.Dispose();

		public string GetName(int i) => fieldNames[i];
		public Type GetFieldType(int i) => fieldTypes[i];
		public string GetDataTypeName(int i) => dbTypes[i].TypeName;
		public int GetOrdinal(string name) {
			for(var i = 0; i != fieldNames.Length; ++i)
				if(fieldNames[i] == name)
					return i;
			throw new InvalidOperationException($"No field named '{name}' mapped");
		}

		public object GetValue(int i) => current[i];

		public int GetValues(object[] values) {
			var n = Math.Min(current.Length, values.Length);
			Array.Copy(current, values, n);
			return n;
		}

		DataTable IDataReader.GetSchemaTable() {
			var schema = new DataTable();
			var isNullable = schema.Columns.Add(DataReaderSchemaColumns.AllowDBNull, typeof(bool));
			var columnSize = schema.Columns.Add(DataReaderSchemaColumns.ColumnSize, typeof(int));
			for(var i = 0; i != FieldCount; ++i) {
				var r = schema.NewRow();
				var dbType = dbTypes[i];
				r[isNullable] = dbType.IsNullable;
				r[columnSize] = dbType.ColumnSize.HasValue ? (object)dbType.ColumnSize.Value : DBNull.Value;
				schema.Rows.Add(r);
			}
			return schema;
		}

		//SqlBulkCopy.EnableStreaming requires this
		public bool IsDBNull(int i) => GetValue(i) is DBNull;

		int IDataReader.Depth { get { throw new NotSupportedException(); } }
		bool IDataReader.IsClosed { get { throw new NotSupportedException(); } }
		int IDataReader.RecordsAffected { get { throw new NotSupportedException(); } }

		public bool GetBoolean(int i) => (bool)GetValue(i);
		public byte GetByte(int i) => (byte)GetValue(i);
		public long GetBytes(int i, long fieldOffset, byte[] buffer, int bufferoffset, int length) { throw new NotImplementedException(); }
		public char GetChar(int i) => (char)GetValue(i);
		public long GetChars(int i, long fieldoffset, char[] buffer, int bufferoffset, int length) { throw new NotImplementedException(); }
		public Guid GetGuid(int i) => (Guid)GetValue(i);
		public short GetInt16(int i) => (short)GetValue(i);
		public int GetInt32(int i) => (int)GetValue(i);
		public long GetInt64(int i) => (long)GetValue(i);
		public float GetFloat(int i) => (float)GetValue(i);
		public double GetDouble(int i) => (double)GetValue(i);
		public string GetString(int i) => (string)GetValue(i);
		public decimal GetDecimal(int i) => (decimal)GetValue(i);
		public DateTime GetDateTime(int i) => (DateTime)GetValue(i);
		public IDataReader GetData(int i) { throw new NotImplementedException(); }
	}
}
