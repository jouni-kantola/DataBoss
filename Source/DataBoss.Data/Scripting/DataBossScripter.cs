using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq;
using System.Text;
using DataBoss.Linq;

namespace DataBoss.Data.Scripting
{
	public class DataBossScripter
	{
		readonly ISqlDialect dialect;

		class DataBossTable
		{
			readonly List<DataBossTableColumn> columns;

			public static DataBossTable From(Type tableType) {
				var tableAttribute = tableType.Single<TableAttribute>();
				return new DataBossTable(tableAttribute.Name, tableAttribute.Schema, 
					tableType.GetFields()
					.Select(field => new {
						field,
						column = field.SingleOrDefault<ColumnAttribute>()
					}).Where(x => x.column != null)
					.Select(x => new {
						Order = x.column.Order == -1 ? int.MaxValue : x.column.Order,
						x.field.FieldType,
						Field = x.field,
						Name = x.column.Name ?? x.field.Name,
					})
					.OrderBy(x => x.Order)
					.Select(x => new DataBossTableColumn(DataBossDbType.From(x.FieldType, x.Field), x.Field, x.Name)));
			}

			public DataBossTable(string name, string schema, IEnumerable<DataBossTableColumn> columns) {
				this.Name = name;
				this.Schema = schema;
				this.columns = columns.ToList();
			}

			public readonly string Name;
			public readonly string Schema;

			public string FullName => string.IsNullOrEmpty(Schema) ? $"[{Name}]" : $"[{Schema}].[{Name}]";
			public IReadOnlyList<DataBossTableColumn> Columns => columns.AsReadOnly();
			public int GetOrdinal(string name) => columns.FindIndex(x => x.Name == name);
		}

		public DataBossScripter(ISqlDialect dialect) { 
			this.dialect = dialect;	
		}

		public string CreateMissing(Type tableType) {
			var table = DataBossTable.From(tableType);

			var result = new StringBuilder();
			result.AppendFormat("if object_id('{0}', 'U') is null begin", table.FullName)
				.AppendLine();
			ScriptTable(table, result)
				.AppendLine();
			return ScriptConstraints(table, result)
				.AppendLine()
				.AppendLine("end")
				.ToString();
		}

		public string ScriptTable(Type tableType) =>
			ScriptTable(DataBossTable.From(tableType), new StringBuilder()).ToString();

		public string ScriptTable(string name, IDataReader reader) {
			var table = new DataBossTable(name, string.Empty, GetColumns(reader));
			return ScriptTable(table, new StringBuilder()).ToString();
		}

		public string ScriptValuesTable(string name, IDataReader reader) {
			var result = new StringBuilder("(values");
			var columns = GetColumns(reader);
			var row = new object[reader.FieldCount];
			while (reader.Read()) {
				result.Append("\r\n  (");
				reader.GetValues(row);
				for (var i = 0; i != columns.Length; ++i)
					result.Append(columns[i].ColumnType.FormatValue(row[i])).Append(", ");
				result.Length -= 2;
				result.Append("),");
			}
			result.Length -= 1;
			result.Append(") ").Append(name).Append('(');
			foreach(var item in columns)
				result.Append('[').Append(item.Name).Append("], ");
			result.Length -= 2;
			result.Append(')');
			return result.ToString();
		}

		public DataBossTableColumn[] GetColumns(IDataReader reader) {
			var schema = reader.GetSchemaTable();
			var isNullable = schema.Columns[DataReaderSchemaColumns.AllowDBNull.Name];
			var columnSize = schema.Columns[DataReaderSchemaColumns.ColumnSize.Name];
			var columns = new DataBossTableColumn[reader.FieldCount];
			for (var i = 0; i != reader.FieldCount; ++i) {
				var r = schema.Rows[i];
				columns[i] = new DataBossTableColumn(DataBossDbType.Create(
					reader.GetDataTypeName(i),
					(columnSize == null || r[columnSize] is DBNull) ? new int?() : (int)r[columnSize],
					(bool)r[isNullable]),
					NullAttributeProvider.Instance,
					reader.GetName(i));
			}
			return columns;
		}

		StringBuilder ScriptTable(DataBossTable table, StringBuilder result) {
			result.Append("create table ");
			AppendTableName(result, table)
				.Append("(");
			
			var sep = "\r\n\t";
			foreach(var item in table.Columns) {
				ScriptColumn(result.Append(sep), item);
				sep = ",\r\n\t";
			}

			result.AppendLine();
			return result.Append(')');
		}

		StringBuilder ScriptColumn(StringBuilder result, DataBossTableColumn column) =>
			result.Append(dialect.FormatName(column.Name)).Append(' ').Append(dialect.GetTypeName(column.ColumnType));

		public string ScriptConstraints(Type tableType) {
			var result = new StringBuilder();
			ScriptConstraints(DataBossTable.From(tableType), result);
			return result.ToString();
		}

		StringBuilder ScriptConstraints(DataBossTable table, StringBuilder result) {
			var clustered = table.Columns.Where(x => x.Any<ClusteredAttribute>())
				.Select(x => x.Name)
				.ToList();
			if(clustered.Count > 0)
				AppendTableName(result.AppendFormat("create clustered index IX_{0}_{1} on ", table.Name, string.Join("_", clustered)), table)
				.AppendFormat("({0})", string.Join(",", clustered))
				.AppendLine();

			var keys = table.Columns.Where(x => x.Any<KeyAttribute>())
				.Select(x => x.Name)
				.ToList();
			if(keys.Count > 0) {
				result.AppendFormat(result.Length == 0 ? string.Empty : Environment.NewLine);
				AppendTableName(result.AppendFormat("alter table "), table)
					.AppendLine()
					.AppendFormat("add constraint PK_{0} primary key(", table.Name)
					.Append(string.Join(",", keys))
					.Append(")");
			}
			return result;
		}

		public string Select(Type rowType, Type tableType) {
			var table = DataBossTable.From(tableType);

			var columns = rowType.GetFields()
				.Where(x => !x.IsInitOnly)
				.Select(x => (x.Name, Ordinal: table.GetOrdinal(x.Name)))
				.OrderBy(x => x.Ordinal);

			var result = new StringBuilder()
				.AppendFormat("select {0} from ", string.Join(", ", columns.Select(x => x.Name)));
			return AppendTableName(result, table).ToString();
		}

		StringBuilder AppendTableName(StringBuilder target, DataBossTable table) {
			if(!string.IsNullOrEmpty(table.Schema))
				target.Append(dialect.FormatName(table.Schema)).Append('.');
			return target.Append(dialect.FormatName(table.Name));
		}
	}
}