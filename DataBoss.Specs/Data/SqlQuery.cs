using Cone;
using System.Collections.Generic;

namespace DataBoss.Data.Specs
{
	[Describe(typeof(SqlQuery))]
	public class SqlQuerySpec
	{
		public void select_ctor() => Check
			.With(() => SqlQuery.Select(() => new KeyValuePair<int, string>(SqlQuery.Column<int>("TheTable", "Id"), SqlQuery.Column<string>("TheTable", "Text"))))
			.That(q => q.ToString() == "select [key] = TheTable.Id, [value] = TheTable.Text");

		public class MyRow<T> { public T Whatever; }

		public void select_member_init() => Check
			.With(() => SqlQuery.Select(() => new MyRow<float> { Whatever = SqlQuery.Column<float>("Your", "Boat") }))
			.That(q => q.ToString() == "select [Whatever] = Your.Boat");
	}
}
