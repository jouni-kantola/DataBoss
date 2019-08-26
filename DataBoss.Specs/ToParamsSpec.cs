using Cone;
using Cone.Core;
using DataBoss.Data;
using DataBoss.Data.SqlServer;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Linq;

namespace DataBoss.Specs
{
	[Describe(typeof(ToParams))]
	public class ToParamsSpec
	{
		SqlParameter[] GetParams<T>(T args) {
			var cmd = new SqlCommand();
			ToParams.AddTo(cmd, args);
			return cmd.Parameters.Cast<SqlParameter>().ToArray();
		}

		public void complext_type() =>
			Check.With(() => GetParams(new { Args = new { Foo = 1, Bar = "Hello" } }))
				.That(
					x => x.Length == 2,
					x => x.Any(p => p.ParameterName == "@Args_Foo"),
					x => x.Any(p => p.ParameterName == "@Args_Bar"));

		[Row(typeof(string))
		,Row(typeof(Guid))
		,Row(typeof(DateTime))
		,Row(typeof(Decimal))
		,Row(typeof(SqlMoney))
		,Row(typeof(SqlDecimal))
		,Row(typeof(byte[]))
		,DisplayAs("{0}", Heading = "has sql type mapping for {0}")]
		public void has_sql_type_mapping_for(Type clrType) => Check.That(() => ToParams.HasSqlTypeMapping(clrType));

		public void object_is_not_considered_complext() {
			var nullableInt = new int?();
			Check.With(() => GetParams(new { Value = nullableInt.HasValue ? (object)nullableInt.Value : DBNull.Value }))
				.That(x => x.Length == 1, x => x.Any(p => p.ParameterName == "@Value"));
		}

		public void null_string() => Check.With(() =>
			GetParams(new { NullString = (string)null }))
			.That(
				xs => xs.Length == 1,
				xs => xs[0].Value == DBNull.Value);

		public void nullable_values() => Check.With(() => 
			GetParams(new {
				HasValue = new int?(1),
				NoInt32 = new int?(),
			}))
			.That(
				xs => xs.Length == 2,
				xs => xs[0].Value.Equals(1),
				xs => xs[0].SqlDbType == SqlDbType.Int,
				xs => xs[1].Value == DBNull.Value,
				xs => xs[1].SqlDbType == SqlDbType.Int);

		public void RowVersion_as_SqlBinary_value() => Check
			.With(() => GetParams(new { RowVersion = new RowVersion(new byte[8])}))
			.That(
				paras => paras.Length == 1,
				paras => paras[0].ParameterName == "@RowVersion",
				paras => paras[0].SqlDbType == SqlDbType.Binary);

		class MyRow { }
		public void IdOf_as_int() {
			var x = GetParams(new { Id = new IdOf<MyRow>(1) });
			Check.That(() => x.Length == 1);
			Check
			.That(
				() => x.Length == 1,
				() => x[0].ParameterName == "@Id",
				() => x[0].SqlDbType == SqlDbType.Int);
		}
	}
}
