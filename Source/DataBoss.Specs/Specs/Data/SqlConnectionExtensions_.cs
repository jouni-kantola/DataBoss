using System;
using System.Data.SqlClient;
using System.Linq;
using CheckThat;
using DataBoss.Diagnostics;
using DataBoss.Testing.SqlServer;
using Xunit;

namespace DataBoss.Data
{
	public sealed class SqlConnectionExtensions_ : IDisposable
	{
		readonly SqlServerTestDb testDb;
		readonly SqlConnection Connection;

		public SqlConnectionExtensions_() { 
			this.testDb = SqlServerTestDb.GetOrCreate("DataBoss");
			Connection = new SqlConnection(testDb.ConnectionString);
			Connection.Open();
		}

		void IDisposable.Dispose() {
			Connection.Dispose();
			testDb.Dispose();
		}

		#pragma warning disable CS0649
		class MyStuffRow
		{
			public int Id;
			public string Value;
		}
		#pragma warning restore CS0649

		[Fact]
		public void insert_and_retreive_ids_is_zippable() { 
			var destinationTableName = "#MyStuff";
			Connection.ExecuteNonQuery($"create table {destinationTableName}(Id int identity, Value varchar(max) not null,)");

			var newItems = new[]{ 
				new { Value = "First" },
				new { Value = "Second" },
				new { Value = "Third" },
			};
			var newIds = Connection.InsertAndGetIdentities(destinationTableName, newItems);
			var myStuff = SqlObjectReader.Create(Connection).Read<MyStuffRow>($"select * from {destinationTableName}");

			Check.With(() => newIds.Zip(myStuff, (x, item) => new { SequenceId = x, item.Id, item.Value }).ToList())
				.That(
					zipped => zipped[0] == new { SequenceId = 1, Id = 1, Value = "First" },
					zipped => zipped[1] == new { SequenceId = 2, Id = 2, Value = "Second" },
					zipped => zipped[2] == new { SequenceId = 3, Id = 3, Value = "Third" });
		}

		[Fact]
		public void db_info() => Check
			.With(() => Connection.GetDatabaseInfo())
			.That(db => db.DatabaseName == (string)Connection.ExecuteScalar("select db_name()"));

		[Fact]
		public void ExecuteNonQuery_flows_local_transaction() {
			using var tx = Connection.BeginTransaction();
			Connection.ExecuteNonQuery(tx, "select 42");
		}

		[Fact]
		public void into_supports_transactions() {
			using var tx = Connection.BeginTransaction();
			Connection.Into(tx, "#TempRows", SequenceDataReader.Items(new { Id = 1 }));
			Connection.Into(tx, "#TempRows2", new[] { new { Id = 2 } });

		}
	}
}
