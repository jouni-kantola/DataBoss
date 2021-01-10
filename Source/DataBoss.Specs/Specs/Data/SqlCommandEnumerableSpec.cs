using System;
using System.Data.SqlClient;
using CheckThat;
using Xunit;

namespace DataBoss.Data
{
	[Trait("Category", "Database")]
	public class SqlCommandEnumerableSpec : IDisposable
	{
		SqlConnection Db;

		RetryStrategy retryAlways => (n, e) => true;
		int rowsRead;
		int ReadInt0(SqlDataReader r) {
			++rowsRead;
			return r.GetInt32(0);
		}

		public SqlCommandEnumerableSpec() {
			Db = new SqlConnection("Server=.;Integrated Security=SSPI");
			Db.Open();
			rowsRead = 0;
		}

		void IDisposable.Dispose() => Db.Dispose();

		[Fact]
		public void Single_raises_appropriate_exception_when_more_than_one_element() {
			var rows = IntRows("select * from (values(1),(2))Foo(Id)");

			Check.Exception<InvalidOperationException>(() => rows.Single(retryAlways));
		}

		[Fact]
		public void Single_raises_appropriate_exception_when_no_element() {
			var rows = IntRows("select top 0 * from (values(1),(2))Foo(Id)");

			Check.Exception<InvalidOperationException>(() => rows.Single(retryAlways));
		}

		[Fact]
		public void Single_consumes_at_most_two_elements() {
			var rows = IntRows("select * from (values(1),(2),(3))Foo(Id)");
			try { rows.Single(retryAlways); } catch { }

			Check.That(() => rowsRead <= 2);
		}

		[Fact]
		public void SingleOrDefault_raises_appropriate_exception_when_more_than_one_element() {
			var rows = IntRows("select * from (values(1),(2))Foo(Id)");

			Check.Exception<InvalidOperationException>(() => rows.SingleOrDefault(retryAlways));
		}

		[Fact]
		public void SingleOrDefault_returns_default_when_no_element() {
			var rows = IntRows("select top 0 * from (values(1),(2))Foo(Id)");

			Check.That(() => rows.SingleOrDefault(retryAlways) == default(int));
		}

		[Fact]
		public void SingleOrDefault_consumes_at_most_two_elements() {
			var rows = IntRows("select * from (values(1),(2),(3))Foo(Id)");
			try { rows.SingleOrDefault(retryAlways); } catch { }

			Check.That(() => rowsRead <= 2);
		}

		DbCommandEnumerable<SqlCommand, SqlDataReader, int> IntRows(string query) =>
			new DbCommandEnumerable<SqlCommand, SqlDataReader, int>(() => Db.CreateCommand(query), x => x.ExecuteReader(), (r,_) => ReadInt0, null);
	}
}
