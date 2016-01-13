﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace DataBoss
{
	public class DataBossMigrator 
	{
		readonly Func<DataBossMigrationInfo, IDataBossMigrationScope> scopeFactory;

		public DataBossMigrator(Func<DataBossMigrationInfo, IDataBossMigrationScope> scopeFactory) {
			this.scopeFactory = scopeFactory;
		}

		public bool Apply(IDataBossMigration migration) {
			var scope = scopeFactory(migration.Info);
			try {
				scope.Begin(migration.Info);
				foreach(var query in migration.GetQueryBatches()) {
					if(!scope.Execute(query))
						return false;
				}
				return true;
			} finally {
				scope.Done();
			}
		}

		public bool ApplyRange(IEnumerable<IDataBossMigration> migrations) {
			return migrations.All(Apply);
		}
	}
}
