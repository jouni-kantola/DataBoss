﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Cone;
using Cone.Core;

namespace DataBoss.Specs
{
	[Describe(typeof(ObjectReader))]
	public class ObjectReaderSpec
	{
		class SimpleDataReader : IDataReader
		{
			readonly string[] names;
			
			readonly List<object[]> records = new List<object[]>();
			int currentRecord;
			public SimpleDataReader(params string[] names) {
				this.names = names;
			}

			public void Add(params object[] record) {
				if(record.Length != names.Length)
					throw new InvalidOperationException("Invalid record length");
				records.Add(record);
			}

			public int Count => records.Count;
			public int FieldCount => names.Length;

			public bool Read() {
				if(currentRecord == records.Count)
					return false;
				++currentRecord;
				return true;
			}
			public string GetName(int i) { return names[i]; }
			public object GetValue(int i) { return records[currentRecord - 1][i]; }

			public void Dispose() { }

			public string GetDataTypeName(int i) { throw new NotImplementedException(); }
			public Type GetFieldType(int i) { throw new NotImplementedException(); }

			public int GetValues(object[] values) { throw new NotImplementedException(); }

			public int GetOrdinal(string name) { throw new NotImplementedException(); }

			public bool GetBoolean(int i) { throw new NotImplementedException(); }

			public byte GetByte(int i) { throw new NotImplementedException(); }

			public long GetBytes(int i, long fieldOffset, byte[] buffer, int bufferoffset, int length) { throw new NotImplementedException(); }

			public char GetChar(int i) { throw new NotImplementedException(); }

			public long GetChars(int i, long fieldoffset, char[] buffer, int bufferoffset, int length) { throw new NotImplementedException(); }

			public Guid GetGuid(int i) { throw new NotImplementedException(); }

			public short GetInt16(int i) { throw new NotImplementedException(); }

			public int GetInt32(int i) { throw new NotImplementedException(); }

			public long GetInt64(int i) { return (long)GetValue(i); }

			public float GetFloat(int i) { throw new NotImplementedException(); }

			public double GetDouble(int i) { throw new NotImplementedException(); }

			public string GetString(int i) { return (string)GetValue(i); }

			public decimal GetDecimal(int i) { throw new NotImplementedException(); }

			public DateTime GetDateTime(int i) { throw new NotImplementedException(); }

			public IDataReader GetData(int i) { throw new NotImplementedException(); }

			public bool IsDBNull(int i) { throw new NotImplementedException(); }

			object IDataRecord.this[int i]
			{
				get { throw new NotImplementedException(); }
			}

			object IDataRecord.this[string name]
			{
				get { throw new NotImplementedException(); }
			}

			public void Close() { throw new NotImplementedException(); }

			public DataTable GetSchemaTable() { throw new NotImplementedException(); }

			public bool NextResult() { throw new NotImplementedException(); }

			public int Depth { get { throw new NotImplementedException(); } }
			public bool IsClosed { get { throw new NotImplementedException(); } }
			public int RecordsAffected { get { throw new NotImplementedException(); } }
		}

		public void converts_all_rows() {
			var source = new SimpleDataReader("Id", "Context", "Name");
			source.Add(1L, "", "First");
			source.Add(2L, "", "Second");
			var reader = new ObjectReader();
			Check.That(() => reader.Read<DataBossMigrationInfo>(source).Count() == source.Count);
		}

		public void reads_public_fields() {
			var source = new SimpleDataReader("Id", "Context", "Name");
			source.Add(1L, "", "First");
			var reader = new ObjectReader();
			var read = reader.Read<DataBossMigrationInfo>(source).Single();
			Check.That(
				() => read.Id == 1,
				() => read.Context == "",
				() => read.Name == "First");
		}

		public void conversion_expression() {
			var source = new SimpleDataReader("Id", "Context", "Name");
			var reader = new ObjectReader();
			var formatter = new ExpressionFormatter(GetType());
			Check.That(() => formatter.Format(reader.GetConverter<DataBossMigrationInfo>(source)) == "x => new DataBossMigrationInfo(){ Id = x.GetInt64(0), Context = x.GetString(1), Name = x.GetString(2) }");
		}

	}
}
