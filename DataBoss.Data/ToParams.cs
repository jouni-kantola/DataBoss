using System;
using System.Collections;
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

		static class Extractor<TCommand, TArg>
		{
			internal static Action<TCommand, TArg> CreateParameters = 
				(Action<TCommand, TArg>)CreateExtractor(typeof(TCommand), typeof(TArg))
				.Compile();
		}

		static LambdaExpression CreateExtractor(Type commandType, Type argType) {
			var command = Expression.Parameter(commandType);
			var args = Expression.Parameter(argType);

			return Expression.Lambda(
				Expression.Block(
					ExtractValues(command, "@", args).Concat(new[]{ Expression.Empty() })), 
					command, args);
		}

		static IEnumerable<Expression> ExtractValues(Expression target, string prefix, Expression input) {
			foreach(var value in input.Type.GetProperties()
				.Where(x => x.CanRead)
				.Concat<MemberInfo>(input.Type.GetFields())
			) {
				var name = prefix + value.Name;
				var readMember = Expression.MakeMemberAccess(input, value);
				if (HasSqlTypeMapping(readMember.Type))
					yield return MakeParameter(target, name, readMember);
				else if (readMember.Type.IsNullable())
					yield return MakeParameterFromNullable(target, name, readMember);
				else if(readMember.Type == typeof(RowVersion)) {
					yield return MakeRowVersionParameter(target, name, readMember);
				}
				else
					foreach (var item in ExtractValues(target, name + "_", readMember))
						yield return item;
			}
		}

		public static bool HasSqlTypeMapping(Type t) => t.IsPrimitive || mappedTypes.Contains(t);

		static Expression MakeParameter(Expression target, string name, Expression value) { 
			var newParameter = Expression.Call(target, typeof(IDbCommand).GetMethod(nameof(IDbCommand.CreateParameter)), null);
			var p = Expression.Variable(newParameter.Type);
			
			var setP = Expression.Assign(p, newParameter);
			
			var setName = Expression.Assign(
					Expression.MakeMemberAccess(p, typeof(IDataParameter).GetProperty(nameof(IDataParameter.ParameterName))),
					Expression.Constant(name));
			var setValue = Expression.Assign(
					Expression.MakeMemberAccess(p, typeof(IDataParameter).GetProperty(nameof(IDbDataParameter.Value))),
					Expression.Convert(value, typeof(object)));

			var targetPs = Expression.MakeMemberAccess(target, typeof(IDbCommand).GetProperty(nameof(IDbCommand.Parameters)));
			var addP = Expression.Call(targetPs, typeof(IList).GetMethod(nameof(IList.Add)), p);

			return Expression.Block(new[]{ p }, setP, setName, setValue, addP);
		}

		static Expression MakeRowVersionParameter(Expression target, string name, Expression value) {
			var createParameter = target.Type.GetMethod(nameof(IDbCommand.CreateParameter), Type.EmptyTypes);
			var newParameter = Expression.Call(target, createParameter, null);
			var p = Expression.Variable(newParameter.Type);

			var setP = Expression.Assign(p, newParameter);

			var setName = Expression.Assign(
					Expression.MakeMemberAccess(p, typeof(IDataParameter).GetProperty(nameof(IDataParameter.ParameterName))),
					Expression.Constant(name));
			var setValue = Expression.Assign(
					Expression.MakeMemberAccess(p, typeof(IDataParameter).GetProperty(nameof(IDbDataParameter.Value))),
					Expression.Convert(Expression.Field(value, nameof(RowVersion.Value)), typeof(object)));

			var setType = newParameter.Type == typeof(SqlParameter)
				? Expression.Assign(
					Expression.MakeMemberAccess(p, typeof(SqlParameter).GetProperty(nameof(SqlParameter.SqlDbType))),
					Expression.Constant(SqlDbType.Binary))
				: Expression.Assign(
					Expression.MakeMemberAccess(p, typeof(IDataParameter).GetProperty(nameof(IDataParameter.DbType))),
					Expression.Constant(DbType.Binary));

			var targetPs = Expression.MakeMemberAccess(target, typeof(IDbCommand).GetProperty(nameof(IDbCommand.Parameters)));
			var addP = Expression.Call(targetPs, typeof(IList).GetMethod(nameof(IList.Add)), p);

			return Expression.Block(new[] { p }, setP, setName, setValue, setType, addP);
		}

		static Expression MakeParameterFromNullable(Expression target, string name, Expression value) {
			var newParameter = Expression.Call(target, typeof(IDbCommand).GetMethod(nameof(IDbCommand.CreateParameter)), null);
			var p = Expression.Variable(newParameter.Type);

			var setP = Expression.Assign(p, newParameter);

			var setName = Expression.Assign(
					Expression.MakeMemberAccess(p, typeof(IDataParameter).GetProperty(nameof(IDataParameter.ParameterName))),
					Expression.Constant(name));
			var setValue = Expression.Assign(
					Expression.MakeMemberAccess(p, typeof(IDataParameter).GetProperty(nameof(IDbDataParameter.Value))),
						Expression.Condition(
							Expression.MakeMemberAccess(value, value.Type.GetProperty(nameof(Nullable<int>.HasValue))),
								Expression.Convert(Expression.MakeMemberAccess(value, value.Type.GetProperty(nameof(Nullable<int>.Value))), typeof(object)),
								Expression.Block(
									Expression.Assign(
										Expression.MakeMemberAccess(p, typeof(IDataParameter).GetProperty(nameof(IDataParameter.DbType))), Expression.Constant(DataBossDbType.ToDbType(value.Type.GetGenericArguments()[0]))),
									Expression.Constant(DBNull.Value, typeof(object)))));

			var targetPs = Expression.MakeMemberAccess(target, typeof(IDbCommand).GetProperty(nameof(IDbCommand.Parameters)));
			var addP = Expression.Call(targetPs, typeof(IList).GetMethod(nameof(IList.Add)), p);

			return Expression.Block(new[] { p }, setP, setName, setValue, addP);;
		}

		public static void AddTo<T>(IDbCommand command, T args) => 
			Extractor<IDbCommand,T>.CreateParameters(command, args);
		
		public static void AddTo<T>(SqlCommand command, T args) => 
			Extractor<SqlCommand, T>.CreateParameters(command, args);

		public static SqlParameter[] Invoke<T>(T args) {
			var cmd = new SqlCommand();
			AddTo(cmd, args);
			return cmd.Parameters.Cast<SqlParameter>().ToArray();
		}
	}
}