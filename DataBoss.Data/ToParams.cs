using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DataBoss.Data.SqlServer;

namespace DataBoss.Data
{
	public static class ToParams
	{
		static HashSet<Type> mappedTypes = new HashSet<Type> {
			typeof(object),
			typeof(string),
			typeof(DateTime),
			typeof(Decimal),
			typeof(Guid),
			typeof(SqlDecimal),
			typeof(SqlMoney),
			typeof(byte[]),
			typeof(SqlBinary),
		};

		static class Extractor<T>
		{
			internal static Func<T, SqlParameter[]> GetParameters = (Func<T, SqlParameter[]>)CreateExtractor(typeof(T)).Compile();
		}

		static readonly ConstructorInfo SqlParameterCtor = typeof(SqlParameter).GetConstructor(new[] { typeof(string), typeof(object) });
		static readonly ConstructorInfo SqlEmptyCtor = typeof(SqlParameter).GetConstructor(new[] { typeof(string), typeof(SqlDbType) });

		static LambdaExpression CreateExtractor(Type type) {
			var typedInput = Expression.Parameter(type);

			return Expression.Lambda(
				Expression.NewArrayInit(
					typeof(SqlParameter),
					ExtractValues(type, "@", typedInput)
				), typedInput);
		}

		static IEnumerable<Expression> ExtractValues(Type type, string prefix, Expression input) {
			foreach(var value in type.GetProperties()
				.Where(x => x.CanRead)
				.Concat<MemberInfo>(type.GetFields())
			) {
				var name = prefix + value.Name;
				var readMember = Expression.MakeMemberAccess(input, value);
				if (HasSqlTypeMapping(readMember.Type))
					yield return MakeSqlParameter(name, readMember);
				else if (readMember.Type.IsNullable())
					yield return MakeParameterFromNullable(name, readMember);
				else if(readMember.Type == typeof(RowVersion)) {
					yield return Expression.MemberInit(
						Expression.New(
							typeof(SqlParameter).GetConstructor(new[] { typeof(string), typeof(SqlDbType)}),
							Expression.Constant(name),
							Expression.Constant(SqlDbType.Binary)),
						Expression.Bind(
							typeof(SqlParameter).GetProperty(nameof(SqlParameter.SqlValue)), 
							Expression.Convert(Expression.Field(readMember, nameof(RowVersion.Value)), typeof(object))));
				}
				else
					foreach (var item in ExtractValues(readMember.Type, name + "_", readMember))
						yield return item;
			}
		}

		public static bool HasSqlTypeMapping(Type t) => t.IsPrimitive || mappedTypes.Contains(t);

		static Expression MakeSqlParameter(string name, Expression value) => 
			Expression.New(SqlParameterCtor, Expression.Constant(name), Expression.Convert(value, typeof(object)));

		static Expression MakeParameterFromNullable(string name, Expression value) =>
				Expression.Condition(Expression.MakeMemberAccess(value, value.Type.GetProperty(nameof(Nullable<int>.HasValue))),
					Expression.New(SqlParameterCtor, Expression.Constant(name), Expression.Convert(Expression.MakeMemberAccess(value, value.Type.GetProperty(nameof(Nullable<int>.Value))), typeof(object))), 
					Expression.MemberInit(
						Expression.New(SqlEmptyCtor, Expression.Constant(name), Expression.Constant(DataBossDbType.ToSqlDbType(value.Type.GetGenericArguments().Single()))),
						Expression.Bind(typeof(SqlParameter).GetProperty(nameof(SqlParameter.Value)), Expression.Constant(DBNull.Value))
					));

		public static SqlParameter[] Invoke<T>(T input) => Extractor<T>.GetParameters(input);

		public static void AddTo<T>(IDbCommand command, T args) => AddTo((SqlParameterCollection)command.Parameters, args);
		public static void AddTo<T>(SqlCommand command, T args) => AddTo(command.Parameters, args);
		public static void AddTo<T>(SqlParameterCollection parameters, T args) => parameters.AddRange(Invoke(args));
	}
}