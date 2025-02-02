using System;
using System.Data;
using System.Linq.Expressions;

namespace DataBoss.Data
{
	public interface ISqlDialect
	{
		string ParameterPrefix { get; }

		string FormatName(string columnName);
		string GetTypeName(DataBossDbType dbType);
		bool TryCreateDialectSpecificParameter(string name, Expression readMember, out Expression create);
	}

	public interface IDataBossConnection : IDisposable
	{
		ISqlDialect Dialect { get; }
		ConnectionState State { get; }

		void Open();

		IDbTransaction BeginTransaction(string transactionName);

		void CreateTable(string destinationTable, IDataReader data);
		void Insert(string destinationTable, IDataReader rows, DataBossBulkCopySettings settings);

		IDbCommand CreateCommand();
		IDbCommand CreateCommand(string cmdText);
		IDbCommand CreateCommand<T>(string cmdText, T args);
		IDbCommand CreateCommand(string cmdText, object args);
	}
}