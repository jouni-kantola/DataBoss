﻿using Cone;
using DataBoss.Schema;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace DataBoss.Specs
{
	class StubAttributeProvider : ICustomAttributeProvider
	{
		readonly List<object> attributes = new List<object>();

		public object[] GetCustomAttributes(bool inherit) {
			return attributes.ToArray();
		}

		public object[] GetCustomAttributes(Type attributeType, bool inherit) {
			return attributes.Where(attributeType.IsInstanceOfType).ToArray();
		}

		public bool IsDefined(Type attributeType, bool inherit) {
			throw new NotImplementedException();
		}

		public StubAttributeProvider Add(Attribute item) {
			attributes.Add(item);
			return this;
		}
	}

	[Describe(typeof(DataBossScripter))]
	public class DataBossScripterSpec
	{
		[DisplayAs("{0} maps to db type {1}")
		,Row(typeof(DateTime?), "datetime")
		,Row(typeof(DateTime), "datetime not null")
		,Row(typeof(long), "bigint not null")
		,Row(typeof(string), "varchar(max)")]
		public void to_db_type(Type type, string dbType) {
			Check.That(() => DataBossScripter.ToDbType(type, new StubAttributeProvider()) == dbType);
		}

		public void Required_string_is_not_null() {
			Check.That(() => DataBossScripter.ToDbType(typeof(string), new StubAttributeProvider().Add(new RequiredAttribute())) == "varchar(max) not null");
		}

		public void MaxLength_controls_string_column_widht() {
			Check.That(() => DataBossScripter.ToDbType(typeof(string), new StubAttributeProvider().Add(new MaxLengthAttribute(31))) == "varchar(31)");
		}

		public void can_script_history_table() {
			var scripter = new DataBossScripter();

			Check.That(() => scripter.Script(typeof(DataBossHistory)) == 
@"create table [__DataBossHistory](
	[Id] bigint not null,
	[Context] varchar(64) not null,
	[Name] varchar(max) not null,
	[StartedAt] datetime not null,
	[FinishedAt] datetime,
	[User] varchar(max),
)");
		}
	}
}
