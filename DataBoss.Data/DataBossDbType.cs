using DataBoss.Data.Scripting;
using DataBoss.Data.SqlServer;
using DataBoss.Linq;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;

namespace DataBoss.Data
{
	public struct DataBossDbType
	{
		static Func<Expression,Expression> Nop = x => x;

		public readonly string TypeName;
		public readonly int? ColumnSize;
		public readonly Func<Expression, Expression> Coerce;
		public readonly bool IsNullable;

		public DataBossDbType(string name, int? columnSize, bool isNullable) : this(name, columnSize, isNullable, Nop) { }
		public DataBossDbType(string name, int? columnSize, bool isNullable, Func<Expression,Expression> coerce) {
			this.TypeName = name;
			this.ColumnSize = columnSize;
			this.IsNullable = isNullable;
			this.Coerce = coerce;
		}

		public static DataBossDbType ToDbType(Type type) => ToDbType(type, type);
		public static DataBossDbType ToDbType(Type type, ICustomAttributeProvider attributes) {
			var canBeNull = !type.IsValueType && !attributes.Any<RequiredAttribute>();
			if (type.TryGetNullableTargetType(out var newTargetType)) {
				canBeNull = true;
				type = newTargetType;
			}
			return MapType(type, attributes, canBeNull);
		}

		static DataBossDbType MapType(Type type, ICustomAttributeProvider attributes, bool canBeNull) {
			var column = attributes.SingleOrDefault<ColumnAttribute>();
			if (column != null && !string.IsNullOrEmpty(column.TypeName))
				return new DataBossDbType(column.TypeName, null, canBeNull);

			switch (type.FullName) {
				case "System.Byte": return new DataBossDbType("tinyint", 1, canBeNull);
				case "System.Int16": return new DataBossDbType("smallint", 2, canBeNull);
				case "System.Int32": return new DataBossDbType("int", 4, canBeNull);
				case "System.Int64": return new DataBossDbType("bigint", 8, canBeNull);
				case "System.Single": return new DataBossDbType("real", 4, canBeNull);
				case "System.Double": return new DataBossDbType("float", 8, canBeNull);
				case "System.Boolean": return new DataBossDbType("bit", 1, canBeNull);
				case "System.String":
					var maxLength = attributes.SingleOrDefault<MaxLengthAttribute>();
					return new DataBossDbType(attributes.Any<AnsiStringAttribute>() ? "varchar" : "nvarchar", maxLength?.Length ?? int.MaxValue, canBeNull);
				case "System.DateTime": return new DataBossDbType("datetime", 8, canBeNull);
				case "System.Data.SqlTypes.SqlMoney": return new DataBossDbType("money", null, canBeNull);
				case "DataBoss.Data.SqlServer.RowVersion": return new DataBossDbType("binary", 8, canBeNull, x => Expression.PropertyOrField(x, nameof(RowVersion.Value)));
				default:
					throw new NotSupportedException("Don't know how to map " + type.FullName + " to a db type.\nTry providing a TypeName using System.ComponentModel.DataAnnotations.Schema.ColumnAttribute.");
			}
		}

		public static SqlDbType ToSqlDbType(Type type) {
			switch(type.FullName) {
				case "System.Byte": return SqlDbType.TinyInt;
				case "System.Int16": return SqlDbType.SmallInt;
				case "System.Int32": return SqlDbType.Int;
				case "System.Int64": return SqlDbType.BigInt;
				case "System.Single": return SqlDbType.Real;;
				case "System.Double": return SqlDbType.Float;
				case "System.Boolean": return SqlDbType.Bit;
				case "System.DateTime": return SqlDbType.DateTime;
			}
			return SqlDbType.NVarChar;
		}

		public static bool operator==(DataBossDbType a, DataBossDbType b) =>
			a.TypeName == b.TypeName && a.IsNullable == b.IsNullable;

		public static bool operator!=(DataBossDbType a, DataBossDbType b) => !(a == b);

		public override string ToString() => FormatType() + (IsNullable ? string.Empty : " not null");

		public override int GetHashCode() => TypeName.GetHashCode();

		public override bool Equals(object obj) => (obj is DataBossDbType && this == (DataBossDbType)obj) || obj.Equals(this);

		string FormatType() => 
			IsWideType(TypeName) ? FormatWideType() : TypeName;

		string FormatWideType() =>
			(!ColumnSize.HasValue || ColumnSize.Value == 1) ? TypeName : $"{TypeName}({FormatWidth(ColumnSize.Value)})";

		static string FormatWidth(int width) => width == int.MaxValue ? "max" : width.ToString();

		static bool IsWideType(string typeName) {
			switch(typeName) {
				default: return false;
				case "binary": return true;
				case "varbinary": return true;
				case "char": return true;
				case "varchar": return true;
				case "nchar": return true;
				case "nvarchar": return true;
			}
		}
	}
}