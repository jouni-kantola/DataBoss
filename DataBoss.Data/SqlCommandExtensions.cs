using System.Data.SqlClient;

namespace DataBoss.Data
{
	public static class SqlCommandExtensions
	{
		public static SqlDataReader ExecuteReader(this SqlCommand cmd, RetryStrategy retry) =>
			retry.Execute(() => cmd.ExecuteReader());

		public static SqlDataReader ExecuteReader(this SqlCommand cmd, string cmdText) {
			cmd.CommandText = cmdText;
			return cmd.ExecuteReader();
		}

		public static SqlDataReader ExecuteReader(this SqlCommand cmd, string cmdText, RetryStrategy retry) =>
			retry.Execute(() => cmd.ExecuteReader(cmdText));

		public static SqlDataReader ExecuteReader<T>(this SqlCommand cmd, string cmdText, T args) =>
			cmd.WithQuery(cmdText, args).ExecuteReader();

		public static SqlDataReader ExecuteReader<T>(this SqlCommand cmd, string cmdText, T args, RetryStrategy retry) =>
			retry.Execute(() => cmd.ExecuteReader(cmdText, args));

		public static object ExecuteScalar(this SqlCommand cmd, string cmdText) {
			cmd.CommandText = cmdText;
			return cmd.ExecuteScalar();
		}

		public static object ExecuteScalar(this SqlCommand cmd, string cmdText, RetryStrategy retry) =>
			retry.Execute(() => cmd.ExecuteScalar(cmdText));

		public static object ExecuteScalar<T>(this SqlCommand cmd, string cmdText, T args) =>
			cmd.WithQuery(cmdText, args).ExecuteScalar();

		public static object ExecuteScalar<T>(this SqlCommand cmd, string cmdText, T args, RetryStrategy retry) =>
			retry.Execute(() => cmd.ExecuteScalar(cmdText, args));

		public static int ExecuteNonQuery(this SqlCommand cmd, string cmdText) {
			cmd.CommandText = cmdText;
			return cmd.ExecuteNonQuery();
		}

		public static int ExecuteNonQuery(this SqlCommand cmd, string cmdText, RetryStrategy retry) =>
			retry.Execute(() => cmd.ExecuteNonQuery(cmdText));

		public static int ExecuteNonQuery<T>(this SqlCommand cmd, string cmdText, T args) =>
			cmd.WithQuery(cmdText, args).ExecuteNonQuery();

		public static int ExecuteNonQuery<T>(this SqlCommand cmd, string cmdText, T args, RetryStrategy retry) =>
			retry.Execute(() => cmd.ExecuteNonQuery(cmdText, args));

		static SqlCommand WithQuery<T>(this SqlCommand cmd, string cmdText, T args) {
			cmd.CommandText = cmdText;
			cmd.Parameters.Clear();
			cmd.Parameters.AddRange(ToParams.Invoke(args));
			return cmd;
		}
	}
}