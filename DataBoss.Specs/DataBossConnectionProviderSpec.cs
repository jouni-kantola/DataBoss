using Cone;
using DataBoss.Testing;
using System.Data.SqlClient;

namespace DataBoss.Data.Specs
{
	[Describe(typeof(DataBossConnectionProvider))]
	public class DataBossConnectionProviderSpec
	{
		SqlConnectionStringBuilder connectionString;
		DataBossConnectionProvider connections;

		[BeforeAll]
		public void EnsureTestInstance() {
			connectionString = DatabaseSetup.GetTemporaryInstance(nameof(DataBossConnectionProvider) + " Tests");
			DatabaseSetup.RegisterForAutoCleanup();
		}

		[BeforeEach]
		public void given_a_connection_provider() {
			connections = new DataBossConnectionProvider(connectionString);
		}

		public void keeps_live_count() {
			using(var db = connections.NewConnection())
				Check.That(() => connections.LiveConnections == 1);
			Check.That(() => connections.LiveConnections == 0, () => connections.ConnectionsCreated == 1);
		}

		public void provider_statistics() {
			connections.SetStatisticsEnabled(true);
			using(var db = connections.NewConnection()) {
				db.Open();
				db.ExecuteScalar("select 42");
				Check.With(() => connections.RetrieveStatistics())
					.That(stats => stats["SelectCount"] == 1);
			}
		}

		public void keeps_stats_for_disposed_connections() {
			connections.SetStatisticsEnabled(true);
			using(var db = connections.NewConnection()) {
				db.Open();
				db.ExecuteScalar("select 1");
			}
			using(var db = connections.NewConnection()) {
				db.Open();
				db.ExecuteScalar("select 2");
			}
			Check.With(() => connections.RetrieveStatistics())
				.That(stats => stats.SelectCount == 2);
		}

		public void reset_statistics() {
			connections.SetStatisticsEnabled(true);
			using(var db = connections.NewConnection()) {
				db.Open();
				db.ExecuteScalar("select 3");
				connections.ResetStatistics();
				Check.With(() => connections.RetrieveStatistics())
					.That(stats => stats["SelectCount"] == 0);
			}
		}

		public void cleanup() {
			var disposed = false;
			var db = connections.NewConnection();
			db.Disposed += (_, __) => disposed = true;
			connections.Cleanup();
			Check.That(
				() => disposed, 
				() => connections.LiveConnections == 0);
		}

		public void command_owning_connection() {
			var c = connections.NewCommand(CommandOptions.DisposeConnection);
			Assume.That(() => connections.LiveConnections == 1);

			c.Dispose();
			Check.That(() => connections.LiveConnections == 0);
		}
	}
}
