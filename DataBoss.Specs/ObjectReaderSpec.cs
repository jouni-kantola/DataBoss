using Cone;
using Cone.Core;
using DataBoss.Data;
using DataBoss.Migrations;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DataBoss.Specs
{
	[Describe(typeof(ObjectReader))]
	public class ObjectReaderSpec
	{
		static KeyValuePair<string, Type> Col<T>(string name) => new KeyValuePair<string, Type>(name, typeof(T));

		public void converts_all_rows() {
			var source = new SimpleDataReader(Col<long>("Id"), Col<string>("Context"), Col<string>("Name")) {
				{ 1L, "", "First" },
				{ 2L, "", "Second" }
			};
			Check.That(() => ObjectReader.For(source).Read<DataBossMigrationInfo>().Count() == source.Count);
		}

		public void works_given_interface_reference() {
			var source = new SimpleDataReader(Col<long>("Id"), Col<string>("Context"), Col<string>("Name")) {
				{ 1L, "", "First" },
				{ 2L, "", "Second" }
			};
			Check.That(() => ObjectReader.For(source as IDataReader).Read<DataBossMigrationInfo>().Count() == source.Count);
		}

		public void reads_public_fields() {
			var source = new SimpleDataReader(Col<long>("Id"), Col<string>("Context"), Col<string>("Name")) { { 1L, "", "First" } };
			var read = ObjectReader.For(source).Read<DataBossMigrationInfo>().Single();
			Check.That(
				() => read.Id == 1,
				() => read.Context == "",
				() => read.Name == "First");
		}

		public void converter_fills_public_fields() {
			var source = SequenceDataReader.Create(new[] { new DataBossMigrationInfo() }, x => { 
				x.Map("Id");
				x.Map("Context");
				x.Map("Name");
			});
			var formatter = new ExpressionFormatter(GetType());
			Check.That(() => formatter.Format(ObjectReader.MakeConverter<SequenceDataReader<DataBossMigrationInfo>, DataBossMigrationInfo>(source)) == 
			"x => new DataBossMigrationInfo { Id = x.GetInt64(0), Context = x.IsDBNull(1) ? default(string) : x.GetString(1), Name = x.IsDBNull(2) ? default(string) : x.GetString(2) }");
		}

		class ValueRow<T> { public T Value; }

		[Row(typeof(float), 3.14f)
		,Row(typeof(double), 42.17)
		,Row(typeof(int), int.MaxValue)
		,Row(typeof(short), short.MaxValue)
		,Row(typeof(byte), byte.MaxValue)
		, DisplayAs("{0}", Heading = "supports field of type")]
		public void supports_field_of_type(Type type, object value) {
			var check = (Action<object>)Delegate.CreateDelegate(typeof(Action<object>), GetType().GetMethod("CheckTSupport", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(type));
			check(value);
		}

		static void CheckTSupport<T>(object value) {
			var source = new SimpleDataReader(Col<T>("Value"));
			var expected = new ValueRow<T> { Value = (T)value };
			source.Add(expected.Value);
			Check.With(() => ObjectReader.For(source).Read<ValueRow<T>>().ToArray())
			.That(
				rows => rows.Length == 1,
				rows => rows[0].Value.Equals(expected.Value));
		}

		public void supports_binary_field() {
			var expected = new ValueRow<byte[]> { Value = Encoding.UTF8.GetBytes("Hello World!") };
			var source = new SimpleDataReader(Col<byte[]>("Value")) { expected.Value };
			Check.With(() => ObjectReader.For(source).Read<ValueRow<byte[]>>().ToArray())
			.That(
				rows => rows.Length == 1,
				rows => rows[0].Value == expected.Value);
		}

		public void supports_nested_fields() {
			var expected = new ValueRow<ValueRow<int>> { Value = new ValueRow<int> { Value = 42 } };
			var source = new SimpleDataReader(Col<int>("Value.Value")) { expected.Value.Value };
			Check.With(() => ObjectReader.For(source).Read<ValueRow<ValueRow<int>>>().ToArray())
			.That(
				rows => rows.Length == 1,
				rows => rows[0].Value.Value == expected.Value.Value);
		}

		public void supports_deeply_nested_fields() {
			var expected = new ValueRow<ValueRow<ValueRow<int>>> { Value = new ValueRow<ValueRow<int>> { Value = new ValueRow<int> { Value = 42 } } };
			var source = new SimpleDataReader(Col<int>("Value.Value.Value")) { expected.Value.Value.Value };
			Check.With(() => ObjectReader.For(source).Read<ValueRow<ValueRow<ValueRow<int>>>>().ToArray())
			.That(
				rows => rows.Length == 1,
				rows => rows[0].Value.Value.Value == expected.Value.Value.Value
			);
		}

		public void can_read_nullable_field() {
			var source = new SimpleDataReader(Col<float>("Value.Value")) {
				3.14f,
			};
			Check.With(() => ObjectReader.For(source).Read<ValueRow<StructRow<float>>>())
			.That(rows => rows.First().Value.Value == 3.14f);
		}

		public void null_source_becomes_default_value() {
			var rows = ObjectReader.For(SequenceDataReader.Create(new[] { new StructRow<int?> { Value = null }, new StructRow<int?> { Value = 1 } }, x => x.MapAll()));
			Check.With(() => rows.Read<StructRow<int>>().ToArray())
				.That(xs => xs[0].Value == default(int), xs => xs[1].Value == 1);		
		}

		struct StructRow<T> 
		{
			public StructRow(T ctorValue) {  this.Value = ctorValue; this.ReadonlyValue = ctorValue; }
			public T Value; 
			public readonly T ReadonlyValue;
		}

		public void can_read_structs() {
			var expected = new StructRow<float> { Value = 3.14f };
			var source = new SimpleDataReader(Col<float>("Value")) { expected.Value };
			Check.With(() => ObjectReader.For(source).Read<StructRow<float>>().ToArray())
			.That(
				rows => rows.Length == 1,
				rows => rows[0].Value == expected.Value);	
		}

		public void can_read_nullable_structs() {
			var expected = 3.14f;
			var source = new SimpleDataReader(Col<float>("Value.ctorValue")) { expected };
			Check.With(() => ObjectReader.For(source).Read<StructRow<StructRow<float>?>>().ToArray())
			.That(
				rows => rows.Length == 1,
				rows => rows[0].Value.Value.Value == expected);
		}

		public void null_nullable_struct() {
			var source = new SimpleDataReader(Col<float>("Value.ctorValue")) { new object[]{ null } };
			source.SetNullable(0, true);
			var r = ObjectReader.For(source).Read<StructRow<StructRow<float>?>>().ToArray();
			Check.With(() => r)
			.That(
				rows => rows.Length == 1,
				rows => rows[0].Value.HasValue == false);
		}

		struct WithNullable
		{
			public int? CanBeNull;
			public int NotNull; 
		}
		public void nullable_nullable() {
			var source = new SimpleDataReader(Col<int>("Value.CanBeNull"), Col<int>("Value.NotNull")) { new object[] { null, 1 } };
			source.SetNullable(0, true);
			var r = ObjectReader.For(source).Read<StructRow<WithNullable?>>().ToArray();
			Check.With(() => r)
			.That(
				rows => rows.Length == 1,
				rows => rows[0].Value.Equals(new WithNullable { NotNull = 1 }));
		}

		struct RowOf<T>
		{
			public T Item;
		}

		public void row_with_nullable_missing_field() {
			var source = new SimpleDataReader(Col<int>("Item.ctorValue")) { new object[] { null } };
			source.SetNullable(0, true);
			var r = ObjectReader.For(source).Read<RowOf<StructRow<int>?>>().ToArray();
			Check.With(() => r)
			.That(
				rows => rows.Length == 1,
				rows => rows[0].Item.HasValue == false);

		}

		public void row_with_nullab_missing_field2() {
			var source = new SimpleDataReader(Col<int>("Item.key"), Col<int>("Item.value")) { new object[] { null, null} };
			source.SetNullable(0, true);
			source.SetNullable(1, true);
			var r = ObjectReader.For(source).Read<RowOf<KeyValuePair<int,int?>?>>().ToArray();
			Check.With(() => r)
			.That(
				rows => rows.Length == 1,
				rows => rows[0].Item.HasValue == false);
		}

		class MyThing<T>
		{
			public MyThing(T value) { this.Value = value; }
			public T Value { get; }
		}

		public void can_use_parameterized_ctor() {
			var expected = new MyThing<int>(42);
			var source = new SimpleDataReader(Col<int>("value")) { expected.Value };
			Check.With(() => ObjectReader.For(source).Read<MyThing<int>>().ToArray())
			.That(
				rows => rows.Length == 1,
				rows => rows[0].Value == expected.Value);
		}

		public void ctors_can_have_complex_arguments() {
			var expected = new MyThing<MyThing<int>> (new MyThing<int>(42));
			var source = new SimpleDataReader(Col<int>("value.value")) { expected.Value.Value };
			Check.With(() => ObjectReader.For(source).Read<MyThing<MyThing<int>>>().ToArray())
			.That(
				rows => rows.Length == 1,
				rows => rows[0].Value.Value == expected.Value.Value);
		}

		public void Action_support() {
			var expected = new MyThing<int>(42);
			var source = new SimpleDataReader(Col<int>("value")) { expected.Value };
			MyThing<int> actual = null;
			ObjectReader.For(source).Read((MyThing<int> x) => actual = x);
			Check.That(() => actual != null);
			Check.That(() => actual.Value == expected.Value); 
		}

		class ValueProp<T>
		{
			public T Value { get; set; }
		}

		public void can_read_props() {
			var expected = new ValueProp<float> { Value = 2.78f };
			var source = new SimpleDataReader(Col<float>("Value")) { expected.Value };
			Check.With(() => ObjectReader.For(source).Read<ValueProp<float>>().ToArray())
			.That(
				rows => rows.Length == 1,
				rows => rows[0].Value == expected.Value);
		}

		public void provides_type_context_on_type_missmatch() {
			var expected = new ValueProp<float> { Value = 2.78f };
			var source = new SimpleDataReader(Col<int>("Value")) { expected.Value };
			var ex = Check.Exception<InvalidConversionException>(() => ObjectReader.For(source).Read<ValueProp<float>>().ToArray());
			Check.That(() => ex.Type == typeof(ValueProp<float>));
		}

		public void detect_type_mismatch() {
			var expected = new ValueProp<float> { Value = 2.78f };
			var source = new SimpleDataReader(Col<float>("Value")) { expected.Value };
			Check.Exception<InvalidOperationException>(() => ObjectReader.For(source).Read<ValueProp<int>>().Count());
		}

		public void custom_converter() {
			var expected = new ValueProp<int> { Value = 42 };
			var source = new SimpleDataReader(Col<int>("Value")) { expected.Value };
			var reader = ObjectReader
				.For(source)
				.WithConverter((int x) => x.ToString());
			Check
				.With(() => reader.Read<ValueProp<string>>().ToArray())
				.That(x => x[0].Value == "42");
		}

		public void custom_converter_nullable_handling() {
			var expected = new ValueProp<int?> { Value = 12345 };
			var source = new SimpleDataReader(Col<int>("Value")) { expected.Value };
			var reader = ObjectReader
				.For(source)
				.WithConverter((int x) => (long)x);
			Check
				.With(() => reader.Read<ValueProp<long?>>().ToArray())
				.That(x => x[0].Value == 12345);
		}

		public void custom_converter_null_item_handling() {
			var expected = new ValueProp<int?> { Value = 12345 };
			var source = new SimpleDataReader(Col<int>("Value")) { expected.Value };
			var reader = ObjectReader
				.For(source)
				.WithConverter((int x) => (long)x);
			Check
				.With(() => reader.Read<ValueProp<long?>>().ToArray())
				.That(x => x[0].Value == 12345);
		}

		public void doesnt_attempt_to_set_readonly_fields() {
			var source = new SimpleDataReader(Col<int>("ReadonlyValue"));
			source.Add(1);
			Check
				.With(() => ObjectReader.For(source).Read<StructRow<int>>().ToArray())
				.That(x => x[0].Value == 0);
		}
	}
}