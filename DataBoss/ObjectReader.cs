using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace DataBoss
{
	public class ObjectReader
	{
		static readonly MethodInfo IsDBNull = typeof(IDataRecord).GetMethod("IsDBNull");

		public IEnumerable<T> Read<T>(IDataReader source) {
			var converter = MakeConverter<T>(source).Compile();

			while(source.Read()) {
				yield return converter(source);
			}
		} 

		class FieldMap
		{
			readonly Dictionary<string, int> fields = new Dictionary<string, int>();
			Dictionary<string, FieldMap> subFields;

			public int MinOrdinal => fields.Count == 0 ? -1 : fields.Min(x => x.Value);

			public void Add(string name, int ordinal) {
				if(name.Contains('.')) {
					var parts = name.Split('.');
					var x = this;
					var n = 0;
					for(; n != parts.Length - 1; ++n)
						x = x[parts[n]];
					x.Add(parts[n], ordinal);
				}
				else fields[name] = ordinal;
			}

			public bool TryGetOrdinal(string key, out int ordinal) => fields.TryGetValue(key, out ordinal);
			public bool TryGetSubMap(string key, out FieldMap subMap) {
				if(subFields != null && subFields.TryGetValue(key, out subMap))
					return true;
				subMap = null;
				return false;
			}

			FieldMap this[string name] {
				get {
					if(subFields == null)
						subFields = new Dictionary<string, FieldMap>();
					return subFields.GetOrAdd(name, _ => new FieldMap());
				}
			}
		}

		class ConverterFactory
		{
			public ParameterExpression arg0 = Expression.Parameter(typeof(IDataRecord), "x");

			Expression ReadField(Type fieldType, int ordinal) {
				var recordType = fieldType;
				var o = Expression.Constant(ordinal);
				if (fieldType == typeof(string) || IsNullable(fieldType, ref recordType))
					return Expression.Condition(
						Expression.Call(arg0, IsDBNull, o),
						Expression.Default(fieldType),
						Convert(Expression.Call(arg0, GetGetMethod(arg0, recordType), o), fieldType));

				return Convert(Expression.Call(arg0, GetGetMethod(arg0, fieldType), o), fieldType);
			}

			public Expression<Func<IDataRecord,T>> Converter<T>(FieldMap map) => Expression.Lambda<Func<IDataRecord,T>>(MemberInit(typeof(T), map), arg0);

			static Expression Convert(Expression expr, Type targetType) {
				return expr.Type == targetType
					? expr
					: Expression.Convert(expr, targetType);
			} 
			
			Expression MemberInit(Type fieldType, FieldMap map) =>
				Expression.MemberInit(
					GetCtor(map, fieldType),
					GetFields(map, fieldType)
				);

			NewExpression GetCtor(FieldMap map, Type fieldType)
			{
				var ctors = fieldType.GetConstructors()
					.Select(ctor => new { ctor, p = ctor.GetParameters() })
					.OrderByDescending(x => x.p.Length);
				foreach(var item in ctors) {
					var pn = new Expression[item.p.Length];
					if(TryMaParameters(map, item.p, ReadField, pn))
						return Expression.New(item.ctor, pn);
				}

				return Expression.New(fieldType);
			}

			static bool TryMaParameters(FieldMap map, ParameterInfo[] parameters, Func<Type, int, Expression> read, Expression[] exprs) {
				int ordinal;
				for(var i = 0; i != parameters.Length; ++i)
					if(!map.TryGetOrdinal(parameters[i].Name, out ordinal))
						return false;
					else exprs[i] = read(parameters[i].ParameterType, ordinal);
				return true;
			} 

			IEnumerable<MemberAssignment> GetFields(FieldMap fieldMap, Type targetType) {
				return targetType
					.GetFields()
					.Where(x => !x.IsInitOnly)
					.Select(x => {
						var ordinal = 0;
						if(fieldMap.TryGetOrdinal(x.Name, out ordinal))
							return new { ordinal, binding = Expression.Bind(x, ReadField(x.FieldType, ordinal)) };

						FieldMap subField;
						if(fieldMap.TryGetSubMap(x.Name, out subField))
							return new { ordinal = subField.MinOrdinal, binding = Expression.Bind(x, MemberInit(x.FieldType, subField)) };

						return new { ordinal = -1, binding = (MemberAssignment)null };
					})
					.Where(x => x.binding != null)
					.OrderBy(x => x.ordinal)
					.Select(x => x.binding);
			}

			private static bool IsNullable(Type fieldType, ref Type recordType) {
				var isNullable = fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(Nullable<>);
				if(isNullable)
					recordType = fieldType.GetGenericArguments()[0];
				return isNullable;
			}

			private static MethodInfo GetGetMethod(Expression arg0, Type fieldType) {
				var getter = arg0.Type.GetMethod("Get" + MapFieldType(fieldType));
				if(getter != null)
					return getter;

				if(fieldType == typeof(byte[])) {
					var getValue = arg0.Type.GetMethod("GetValue");
					if(getValue != null)
						return getValue;
				}

				throw new NotSupportedException("Can't read field of type:" + fieldType);
			}

			private static string MapFieldType(Type fieldType) {
				switch(fieldType.FullName) {
					case "System.Single": return "Float";
					case "System.Object": return "Value";
				}
				return fieldType.Name;
			}
		}

		public static Expression<Func<IDataRecord, T>> MakeConverter<T>(IDataReader reader) {
			var fieldMap = new FieldMap();
			for(var i = 0; i != reader.FieldCount; ++i)
				fieldMap.Add(reader.GetName(i), i);
			var rr = new ConverterFactory();
			return rr.Converter<T>(fieldMap);
		}
	}
}