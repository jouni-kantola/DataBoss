using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace DataBoss.Data
{
	class BufferedDataReader : IDataReader
	{
		readonly IDataReader reader;
		readonly IDataRecordReader records;
		readonly BlockingCollection<IReadOnlyCollection<IDataRecord>> buffer = new BlockingCollection<IReadOnlyCollection<IDataRecord>>();
		IEnumerator<IDataRecord> items;
		readonly Thread recordReader;
		ExceptionDispatchInfo readError;

		public BufferedDataReader(IDataReader reader) {
			this.reader = reader;
			this.records = reader.AsDataRecordReader();
			this.items = Enumerable.Empty<IDataRecord>().GetEnumerator();
			this.recordReader = new Thread(ReadRecords) {
				IsBackground = true,
				Name = nameof(BufferedDataReader),
			};
			recordReader.Start();
		}

		void ReadRecords() {
			try {
				var chunk = NewChunk();
				while (records.Read()) {
					chunk.Add(records.GetRecord());
					if (chunk.Count == chunk.Capacity) {
						buffer.Add(chunk);
						chunk = NewChunk();
					}
				}
				if (chunk.Count != 0)
					buffer.Add(chunk);
			} catch(Exception e) {
				Interlocked.Exchange(ref readError, ExceptionDispatchInfo.Capture(e));
			} finally {
				buffer.CompleteAdding();
			}
		}

		List<IDataRecord> NewChunk() => new List<IDataRecord>(8);

		IDataRecord Current => items.Current;

		public object this[int i] => Current[i];
		public object this[string name] => Current[name];

		public int Depth => reader.Depth;
		public bool IsClosed => reader.IsClosed;
		public int RecordsAffected => reader.RecordsAffected;
		public int FieldCount => reader.FieldCount;

		public bool NextResult() => false;
		public bool Read() {
			try {
				for (; ; ) {
					if (items.MoveNext())
						return true;
					items.Dispose();

					if (!buffer.TryTake(out var next, -1))
						return false;
					items = next.GetEnumerator();
				}
			} finally {
				if (readError != null)
					readError.Throw();
			}
		}
		public void Close() => reader.Close();
		public void Dispose() => reader.Dispose();

		public DataTable GetSchemaTable() => reader.GetSchemaTable();
		public string GetName(int i) => reader.GetName(i);
		public int GetOrdinal(string name) => reader.GetOrdinal(name);
		public Type GetFieldType(int i) => reader.GetFieldType(i);

		public bool IsDBNull(int i) => Current.IsDBNull(i);
		public bool GetBoolean(int i) => Current.GetBoolean(i);
		public byte GetByte(int i) => Current.GetByte(i);
		public long GetBytes(int i, long fieldOffset, byte[] buffer, int bufferoffset, int length) => Current.GetBytes(i, fieldOffset, buffer, bufferoffset, length);
		public char GetChar(int i) => Current.GetChar(i);
		public long GetChars(int i, long fieldoffset, char[] buffer, int bufferoffset, int length) => Current.GetChars(i, fieldoffset, buffer, bufferoffset, length);
		public IDataReader GetData(int i) => Current.GetData(i);
		public string GetDataTypeName(int i) => Current.GetDataTypeName(i);
		public DateTime GetDateTime(int i) => Current.GetDateTime(i);
		public decimal GetDecimal(int i) => Current.GetDecimal(i);
		public double GetDouble(int i) => Current.GetDouble(i);
		public float GetFloat(int i) => Current.GetFloat(i);
		public Guid GetGuid(int i) => Current.GetGuid(i);
		public short GetInt16(int i) => Current.GetInt16(i);
		public int GetInt32(int i) => Current.GetInt32(i);
		public long GetInt64(int i) => Current.GetInt64(i);
		public string GetString(int i) => Current.GetString(i);
		public object GetValue(int i) => Current.GetValue(i);
		public int GetValues(object[] values) => Current.GetValues(values);
	}
}
