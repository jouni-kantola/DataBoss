using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace DataBoss.Data
{
	public class ConverterFactory
	{
		class ConverterContext
		{
			ConverterContext(ParameterExpression arg0, MethodInfo isDBNull) {
				this.Arg0 = arg0;
				this.IsDBNull = isDBNull;
			}

			public static ConverterContext Create<TRecord>() where TRecord : IDataRecord {
				var record = typeof(TRecord);
				return new ConverterContext(Expression.Parameter(record, "x"), 
					record.GetMethod(nameof(IDataRecord.IsDBNull)) ?? typeof(IDataRecord).GetMethod(nameof(IDataRecord.IsDBNull)));
			}

			public readonly ParameterExpression Arg0;
			public readonly MethodInfo IsDBNull;

			public Expression ReadField(Type fieldType, Expression ordinal) => 
				Expression.Call(Arg0, GetGetMethod(fieldType), ordinal);

			public MethodInfo GetGetMethod(Type fieldType) {
				var getterName = "Get" + MapFieldType(fieldType);
				var getter = Arg0.Type.GetMethod(getterName) ?? typeof(IDataRecord).GetMethod("Get" + MapFieldType(fieldType));
				if(getter != null)
					return getter;

				throw new NotSupportedException($"Can't read field of type: {fieldType} given {Arg0.Type}");
			}

			public Expression DbNullToDefault(FieldMapItem field, Expression o, Type itemType, Expression readIt) {
				if(!field.AllowDBNull)
					return readIt;
				return Expression.Condition(
					Expression.Call(Arg0, IsDBNull, o),
					Expression.Default(itemType),
					readIt);
			}
		}
		readonly ConverterCollection customConversions;
		readonly IConverterCache converterCache;

		public ConverterFactory(ConverterCollection customConversions) : this(customConversions, new ConcurrentConverterCache())
		{ }

		public ConverterFactory(ConverterCollection customConversions, IConverterCache converterCache) {
			this.customConversions = new ConverterCollection(customConversions);
			this.converterCache = converterCache;
		}

		public DataRecordConverter<TReader, T> GetConverter<TReader, T>(TReader reader) where TReader : IDataReader =>
			new DataRecordConverter<TReader, T>(converterCache.GetOrAdd(reader, typeof(T), (map, result) => BuildConverter(ConverterContext.Create<TReader>(), map, result)));

		LambdaExpression BuildConverter(ConverterContext context, FieldMap map, Type result) => 
			Expression.Lambda(MemberInit(context, result, map), context.Arg0);

		Expression MemberInit(ConverterContext context, Type fieldType, FieldMap map) =>
			Expression.MemberInit(
				GetCtor(context, map, fieldType),
				GetMembers(context, map, fieldType));

		NewExpression GetCtor(ConverterContext context, FieldMap map, Type fieldType) {
			var ctors = fieldType.GetConstructors()
				.Select(ctor => new { ctor, p = ctor.GetParameters() })
				.OrderByDescending(x => x.p.Length);
			foreach(var item in ctors) {
				var pn = new Expression[item.p.Length];
				if(TryMapParameters(context, map, item.p, pn))
					return Expression.New(item.ctor, pn);
			}

			if(fieldType.IsValueType)
				return Expression.New(fieldType);

			throw new InvalidOperationException("No suitable constructor found for " + fieldType);
		}

		ArraySegment<MemberAssignment> GetMembers(ConverterContext context, FieldMap map, Type targetType) {
			var fields = targetType.GetFields().Select(x => new { x.Name, x.FieldType, Member = (MemberInfo)x });
			var props = targetType.GetProperties().Where(x => x.CanWrite).Select(x => new { x.Name, FieldType = x.PropertyType, Member = (MemberInfo)x });
			var members = fields.Concat(props).ToArray();
			var ordinals = new int[members.Length];
			var bindings = new MemberAssignment[members.Length];
			var found = 0;
			KeyValuePair<int, Expression> binding;
			foreach(var x in members) {
				if(!TryReadOrInit(context, map, x.FieldType, x.Name, out binding))
					continue;
				ordinals[found] = binding.Key;
				bindings[found] = Expression.Bind(x.Member, binding.Value);
				++found;
			}
			Array.Sort(ordinals, bindings, 0, found);
			return new ArraySegment<MemberAssignment>(bindings, 0, found);
		}

		bool TryMapParameters(ConverterContext context, FieldMap map, ParameterInfo[] parameters, Expression[] exprs) {
			KeyValuePair<int, Expression> binding;
			for(var i = 0; i != parameters.Length; ++i) {
				if(!TryReadOrInit(context, map, parameters[i].ParameterType, parameters[i].Name, out binding))
					return false;
				exprs[i] = binding.Value;
			}
			return true;
		}

		bool TryReadOrInit(ConverterContext context, FieldMap map, Type itemType, string itemName, out KeyValuePair<int, Expression> found) {
			FieldMapItem field;
			if (map.TryGetOrdinal(itemName, out field)) {
				var o = Expression.Constant(field.Ordinal);
				Expression convertedField;
				if(!TryConvertField(context.ReadField(field.FieldType, o), itemType, out convertedField) 
				&& !(field.ProviderSpecificFieldType != null && TryConvertField(context.ReadField(field.ProviderSpecificFieldType, o), itemType, out convertedField)))
					throw new InvalidOperationException($"Can't read '{itemName}' of type {itemType.Name} given {field.FieldType.Name}");

				found = new KeyValuePair<int, Expression>(field.Ordinal, context.DbNullToDefault(field, o, itemType, convertedField));
				return true;
			}

			FieldMap subMap;
			if(map.TryGetSubMap(itemName, out subMap)) {
				found = new KeyValuePair<int, Expression>(subMap.MinOrdinal, MemberInit(context, itemType, subMap));
				return true;
			}

			found = default(KeyValuePair<int, Expression>);
			return false;
		}

		bool TryConvertField(Expression rawField, Type to, out Expression convertedField) {
			var from = rawField.Type;
			if (from == to) {
				convertedField = rawField;
				return true;
			}

			
			convertedField = GetConversionOrDefault(rawField, to);
			if(convertedField == null 
			&& TryGetConverter(rawField, to, out convertedField))
				return true;

			return convertedField != null;
		}

		Expression GetConversionOrDefault(Expression rawField, Type to) {
			var from = rawField.Type;

			if (to.TryGetNullableTargetType(out var baseType)) {
				if((baseType == from))
					return Expression.Convert(rawField, to);
				else if(TryGetConverter(rawField, baseType, out var customNullabeConversion))
					return Expression.Convert(customNullabeConversion, to);
			}

			return null;
		}

		bool TryGetConverter(Expression rawField, Type to, out Expression converter) {
			if(customConversions.TryGetConverter(rawField, to, out converter))
				return true;

			if (rawField.Type == typeof(object) && to == typeof(byte[])) {
				converter = Expression.Convert(rawField, to);
				return true;
			}

			return false;
		}

		static string MapFieldType(Type fieldType) {
			switch(fieldType.FullName) {
				case "System.Single": return "Float";
				case "System.Object": return "Value";
				case "System.Byte[]": return "Value";
			}
			return fieldType.Name;
		}
	}
}