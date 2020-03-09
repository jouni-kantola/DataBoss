using System;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using Cone;
using DataBoss.Data;

namespace DataBoss.Specs
{
	[Describe(typeof(ConverterFactory))]
	public class ConverterFactorySpec
	{
		public void reuse_converter_for_matching_field_map() {
			var factory = new ConverterFactory(new ConverterCollection());
			var reader0 = SequenceDataReader.Create(new[]{ new { key = 0, value = "0"}}, x => x.MapAll());
			var reader1 = SequenceDataReader.Create(new[] { 1 }, x => {
				x.Map("key", item => item);
				x.Map("value", item => item.ToString());
			});

			Check.That(() => factory.GetConverter<IDataReader, KeyValuePair<int, string>>(reader0).Compiled == factory.GetConverter<IDataReader, KeyValuePair<int, string>>(reader1).Compiled);
		}

		public void factory_expression_converter() {
			var factory = new ConverterFactory(new ConverterCollection());
			var reader = SequenceDataReader.Create(new[] { new { key = 1, } }, x => x.MapAll());
			reader.Read();
			Check.With(() => factory.Compile(reader, (int key) => new KeyValuePair<int, string>(key, key.ToString())))
				.That(
					converter => converter(reader).Key == reader.GetInt32(0), 
					converter => converter(reader).Value == reader.GetInt32(0).ToString());
		}

		public void throw_on_unexpected_null() {
			var factory = new ConverterFactory(new ConverterCollection());
			var reader = new SimpleDataReader(new KeyValuePair<string, Type>("value", typeof(int)));
			reader.Add(new[]{ (object)null });
			reader.SetNullable(0, true);

			reader.Read();
			var converter = factory.Compile(reader, (int value) => new KeyValuePair<int, string>(value, value.ToString()));
			var ex = Check.Exception<InvalidCastException>(() => converter(reader));
			Check.That(() => ex.Message.Contains("'value'"));
		}

		public void no_throw_on_expected_null() {
			var factory = new ConverterFactory(new ConverterCollection());
			var reader = new SimpleDataReader(
				new KeyValuePair<string, Type>("id", typeof(int)),
				new KeyValuePair<string, Type>("value", typeof(string)));
			reader.Add(new[] { (object)null, (object)null });
			reader.SetNullable(0, true);
			reader.SetNullable(1, true);

			reader.Read();
			var converter = factory.Compile(reader, (int? id, string value) => Tuple.Create(id, value));
			Check.With(() => converter(reader)).That(
				x => x.Item1 == null,
				x => x.Item2 == null);
		}

		public void factory_expression_ctor_reuse() {
			var factory = new ConverterFactory(new ConverterCollection(), new ConcurrentConverterCache());
			var reader = SequenceDataReader.Create(new[] { new { x = 1, } }, x => x.MapAll());
			Check.That(() => Equals(
				factory.Compile<IDataReader, int, KeyValuePair<int, int>>(reader, x => new KeyValuePair<int, int>(x, x)),
				factory.Compile<IDataReader, int, KeyValuePair<int, int>>(reader, x => new KeyValuePair<int, int>(x, x))));
		}
	}

	[Describe(typeof(ConverterCacheKey))]
	public class ConverterCacheKeySpec
	{
		public void ctor_key() {
			IDataReader r = SequenceDataReader.Create(new[] { new { x = 1 } });
			var created = ConverterCacheKey.TryCreate(r, Expr<int, KeyValuePair<int, int>>(x => new KeyValuePair<int, int>(x, x)), out var key);
			Check.That(() => created);
			Check.That(() => key.ToString() == "System.Data.IDataReader(System.Int32)->.ctor(System.Int32 _0, System.Int32 _0)");
		}

		static Expression<Func<TArg0, T>> Expr<TArg0, T>(Expression<Func<TArg0, T>> e) => e;
	}

}
