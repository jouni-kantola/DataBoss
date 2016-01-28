﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cone;

namespace DataBoss.Specs
{
	[Describe(typeof(DataBossMigrator))]
	public class DataBossMigratorSpec
	{
		class TestableMigrationScope : IDataBossMigrationScope
		{
			public void Dispose() { }

			public readonly List<string> ExecutedQueries = new List<string>(); 
			public Func<string, bool> OnExecute;

			public event EventHandler<ErrorEventArgs> OnError;

			public void Begin(DataBossMigrationInfo info) {}

			public bool Execute(string query) {
				var result = true;
				if(OnExecute != null) {
					try {
						result = OnExecute(query);
					} catch(Exception e) {
						result = false;
						OnError?.Invoke(this, new ErrorEventArgs(e));
					}
				}
				ExecutedQueries.Add(query);
				return result;
			}

			public void Done() { }
		}

		public void stops_application_when_migration_scope_is_faulted() {
			var scope = new TestableMigrationScope();
			var migrator = new DataBossMigrator(_ => scope);

			scope.OnExecute += _ => false;
			Check.That(() => migrator.ApplyRange(new[] {
				TextMigration("First!"),
				TextMigration("Second!"),
			}) == false);

			Check.That(
				() => scope.ExecutedQueries.Count == 1,
				() => scope.ExecutedQueries.SequenceEqual(new[] { "First!" }));
		}

		public void breaks_appliction_on_first_query_failure() {
			var scope = new TestableMigrationScope();
			var migrator = new DataBossMigrator(_ => scope);

			scope.OnExecute += _ => false;
			Check.That(() => migrator.ApplyRange(new[] {
				TextMigration("1\nGO\n2"),
			}) == false);

			Check.That(
				() => scope.ExecutedQueries.Count == 1,
				() => string.Join(" - ", scope.ExecutedQueries) == "1");
		}

		IDataBossMigration TextMigration(string s) {
			return new DataBossTextMigration(() => new StringReader(s));
		}
	}
}
