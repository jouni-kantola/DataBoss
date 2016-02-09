﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace DataBoss.Data
{
	public class FieldMapping<T>
	{
		readonly ParameterExpression source = Expression.Parameter(typeof(T), "source");
		KeyValuePair<string, Expression>[] selectors = new KeyValuePair<string, Expression>[0];

		public int Map(string memberName) {
			var memberInfo = (typeof(T).GetField(memberName) as MemberInfo) ?? typeof(T).GetProperty(memberName);
			if(memberInfo == null)
				throw new InvalidOperationException($"Can't find public field or property '{memberName}'");
			return Map(memberInfo);
		}

		public int Map<TMember>(Expression<Func<T,TMember>> selector) {
			if(selector.Body.NodeType != ExpressionType.MemberAccess)
				throw new NotSupportedException();
			var m = (MemberExpression)selector.Body;
			return Map(m.Member);
		}

		public int Map(MemberInfo memberInfo) => Map(memberInfo.Name, 
			Expression.Convert(
				Expression.MakeMemberAccess(source, memberInfo), 
				typeof(object)));

		public int Map(string name, Func<T, object> selector) => Map(name, 
			Expression.Invoke(Expression.Constant(selector), source));

		int Map(string name, Expression selector) {
			var ordinal = selectors.Length;
			Array.Resize(ref selectors, ordinal + 1);

			selectors[ordinal] = new KeyValuePair<string, Expression>(name, selector);

			return ordinal;
		}

		public string[] GetFieldNames() => Array.ConvertAll(selectors, x => x.Key);

		public Action<T,object[]> GetAccessor() {
			var target = Expression.Parameter(typeof(object[]), "target");
			var body = Enumerable.Range(0, selectors.Length)
				.Select(x => Expression.Assign(
					Expression.ArrayAccess(target, Expression.Constant(x)),
					selectors[x].Value));
			return Expression.Lambda<Action<T,object[]>>(
				Expression.Block(body), true, source, target).Compile();
		}
	}
}