using DataBoss.Data.Scripting;
using DataBoss.Data.SqlServer;
using DataBoss.Linq;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace DataBoss.Data
{
	public struct DataBossDbType
	{
		enum BossTypeTag : byte
		{
			Custom = 0,
			TinyInt = 1,
			SmallInt = 2,
			Int = 3,
			BigInt = 4,
			Real = 5,
			Float = 6,
			Bit = 7,
			Char = 8,
			VarChar = 9,
			NChar = 10,
			NVarChar = 11,
			Binary = 12,
			VarBinary = 13,
			Rowversion = 14,
			TagMask = 15,

			IsVariableSize = 1 << 3,
			IsNullable = 1 << 7,
		}

		static readonly (string TypeName, byte Width)[] BossTypes = new(string, byte)[]
		{
			(null, 0),
			("tinyint", 1),
			("smallint", 2),
			("int", 4),
			("bigint", 8),
			("real", 4),
			("float", 8),
			("bit", 0),
			("char", 0),
			("varchar", 0),
			("nchar", 0),
			("nvarchar", 0),
			("binary", 0),
			("varbinary", 0),
			//rowversion
			("binary", 0),
		};

		static Expression ReadRowversionValue(Expression x) => Expression.PropertyOrField(x, nameof(RowVersion.Value));
		static Func<Expression, Expression> CoerceRowVersion = ReadRowversionValue;

		readonly BossTypeTag tag;
		readonly object extra;

		public int? ColumnSize => tag.HasFlag(BossTypeTag.IsVariableSize)
			? (int?)extra
			: IsKnownType(out var knownType) ? BossTypes[(byte)knownType].Width : -1; 

		public string TypeName => IsKnownType(out var knownType) 
			? BossTypes[(byte)knownType].TypeName
			: CustomInfo.TypeName;

		bool IsKnownType(out BossTypeTag typeTag) {
			typeTag = (tag & BossTypeTag.TagMask);
			return typeTag != BossTypeTag.Custom;
		}

		(string TypeName, int? Width) CustomInfo => (ValueTuple<string, int?>)extra;

		public Func<Expression, Expression> Coerce => (tag & BossTypeTag.TagMask) != BossTypeTag.Rowversion 
			? Lambdas.Id
			: CoerceRowVersion;

		public bool IsNullable => tag.HasFlag(BossTypeTag.IsNullable);

		public static DataBossDbType Create(string typeName, int? columnSize, bool isNullable) {
			var tag = TypeTagLookup(ref typeName);
			if(tag == BossTypeTag.Custom)
				return new DataBossDbType(tag, isNullable, (typeName, columnSize));
			return new DataBossDbType(tag, isNullable, columnSize);
		}

		static BossTypeTag TypeTagLookup(ref string typeName) {
			var nameToFind = typeName;
			var n = Array.FindIndex(BossTypes, x => x.TypeName == nameToFind);
			if(n == -1)
				return BossTypeTag.Custom;
			typeName = null;
			return (BossTypeTag)n;
		}

		DataBossDbType(BossTypeTag tag, bool isNullable) : this(tag, isNullable, null)
		{ }

		DataBossDbType(BossTypeTag tag, bool isNullable, object extra) {
			this.tag = tag | (isNullable ? BossTypeTag.IsNullable : 0);
			this.extra = extra;
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

		public static DataBossDbType ToDbType(IDbDataParameter parameter) { 
			var t = MapType(parameter.DbType);
			return t.HasFlag(BossTypeTag.IsVariableSize)
			? new DataBossDbType(t, true, parameter.Size)
			: new DataBossDbType(t, true);
		}

		public string FormatValue(object value) {
			switch(tag & BossTypeTag.TagMask) { 
				default: throw new NotSupportedException($"Can't format {value} of type {value.GetType()} as {ToString()}");
				case BossTypeTag.TinyInt: return ChangeType<byte>(value).ToString();
				case BossTypeTag.SmallInt: return ChangeType<short>(value).ToString();
				case BossTypeTag.Int: return ChangeType<int>(value).ToString();
				case BossTypeTag.BigInt: return ChangeType<long>(value).ToString();
				case BossTypeTag.Real: return ChangeType<float>(value).ToString(CultureInfo.InvariantCulture);
				case BossTypeTag.Float: return ChangeType<double>(value).ToString(CultureInfo.InvariantCulture);
			}
		}

		static T ChangeType<T>(object value) => (T)Convert.ChangeType(value, typeof(T));

		static BossTypeTag MapType(DbType dbType) {
			switch(dbType) {
				default: throw new NotSupportedException($"No mapping for {dbType}.");
				case DbType.Byte: return BossTypeTag.TinyInt;
				case DbType.Int16: return BossTypeTag.SmallInt;
				case DbType.Int32: return BossTypeTag.Int;
				case DbType.Int64: return BossTypeTag.BigInt;
				case DbType.Boolean: return BossTypeTag.Bit;
				case DbType.String: return BossTypeTag.NVarChar;
				case DbType.Binary: return BossTypeTag.Binary;
			}
		}

		static DataBossDbType MapType(Type type, ICustomAttributeProvider attributes, bool canBeNull) {
			var column = attributes.SingleOrDefault<ColumnAttribute>();
			if (column != null && !string.IsNullOrEmpty(column.TypeName))
				return Create(column.TypeName, null, canBeNull);

			switch (type.FullName) {
				case "System.Byte": return new DataBossDbType(BossTypeTag.TinyInt, canBeNull);
				case "System.Int16": return new DataBossDbType(BossTypeTag.SmallInt, canBeNull);
				case "System.Int32": return new DataBossDbType(BossTypeTag.Int, canBeNull);
				case "System.Int64": return new DataBossDbType(BossTypeTag.BigInt, canBeNull);
				case "System.Single": return new DataBossDbType(BossTypeTag.Real, canBeNull);
				case "System.Double": return new DataBossDbType(BossTypeTag.Float, canBeNull);
				case "System.Boolean": return new DataBossDbType(BossTypeTag.Bit, canBeNull);
				case "System.String":
					var maxLength = attributes.SingleOrDefault<MaxLengthAttribute>();
					return new DataBossDbType(attributes.Any<AnsiStringAttribute>() ? BossTypeTag.VarChar: BossTypeTag.NVarChar, canBeNull, maxLength?.Length ?? int.MaxValue);
				case "System.DateTime": return Create("datetime", 8, canBeNull);
				case "System.Data.SqlTypes.SqlMoney": return Create("money", null, canBeNull);
				case "DataBoss.Data.SqlServer.RowVersion": return new DataBossDbType(BossTypeTag.Rowversion, canBeNull, (int?)8);
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
			tag.HasFlag(BossTypeTag.IsVariableSize) ? FormatWideType() : TypeName;

		string FormatWideType() =>
			(!ColumnSize.HasValue || ColumnSize.Value == 1) ? TypeName : $"{TypeName}({FormatWidth(ColumnSize.Value)})";

		static string FormatWidth(int width) => width == int.MaxValue ? "max" : width.ToString();

	}
}