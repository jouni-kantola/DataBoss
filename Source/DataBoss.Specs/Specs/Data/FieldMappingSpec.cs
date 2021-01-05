using System;
using System.Linq.Expressions;
using CheckThat;
using Xunit;

namespace DataBoss.Data
{
	public class FieldMappingSpec
	{
		class MyThing
		{
			public int Value;
		}

		[Fact]
		public void untyped_lambda_mapping_must_have_correct_parameter_type() {
			var fieldMapping = new FieldMapping<MyThing>();
			Check.Exception<InvalidOperationException>(() => fieldMapping.Map("Borken", MakeLambda((string x) => x)));
		}

		[Fact]
		public void untyped_lambda_mapping() {
			var fieldMapping = new FieldMapping<MyThing>();
			fieldMapping.Map("LambdaValue", MakeLambda((MyThing x) => x.Value));
			var accessor = fieldMapping.GetAccessor();
			var result = new object[1];
			accessor(new MyThing { Value = 1 }, result);
			Check.That(() => (int)result[0] == 1);
		}

		[Fact]
		public void lambdas_not_wrapped_uncessarily() {
			var fieldMapping = new FieldMapping<MyThing>();
			Func<MyThing, int> failToGetValue = x => { throw new InvalidOperationException(); };
			fieldMapping.Map("Borken", MakeLambda((MyThing x) => failToGetValue(x)));

			Check.That(() => fieldMapping.GetAccessorExpression().Body.ToString().StartsWith("(target[0] = Convert(Invoke(value("));
		}

		[Fact]
		public void static_member_lambda_mapping() {
			var fieldMapping = new FieldMapping<MyThing>();
			fieldMapping.Map("Empty", MakeLambda((MyThing x) => string.Empty));

			Check.That(() => fieldMapping.GetAccessorExpression().Body.ToString() == "(target[0] = Convert(String.Empty))");
		}

		[Fact]
		public void nullable_without_value_is_DBNull() {
			var item = new { Value = (int?)null };
			var fieldMapping = new FieldMapping(item.GetType());
			fieldMapping.MapAll();

			Check.That(() => fieldMapping.GetSelector(0).ToString() == "(source.Value ?? )");
		}

		#pragma warning disable CS0649
		class MyThingWithStaticMember
		{
			public string TheAnswer;
			public static string TheQuestion;
			public static float YourBoat => 42;
		}
		#pragma warning restore CS0649

		[Fact]
		public static void MapAll_ignores_static_fields() {
			var mapping = new FieldMapping<MyThingWithStaticMember>();

			mapping.MapAll();

			Check.That(() => mapping.GetFieldNames().Length == 1);
		}

		LambdaExpression MakeLambda<TArg, TResult>(Expression<Func<TArg, TResult>> expr) => expr;
	}
}
