﻿namespace EfEnumToLookup.LookupGenerator
{
	using System;
	using System.Collections.Generic;
	using System.Data.SqlClient;
	using System.Text;

	class SqlServerHandler : IDbHandler
	{
		/// <summary>
		/// The size of the Name field that will be added to the generated lookup tables.
		/// Adjust to suit your data if required, defaults to 255.
		/// </summary>
		public int NameFieldLength { get; set; }

		/// <summary>
		/// Prefix to add to all the generated tables to separate help group them together
		/// and make them stand out as different from other tables.
		/// Defaults to "Enum_" set to null or "" to not have any prefix.
		/// </summary>
		public string TableNamePrefix { get; set; }

		/// <summary>
		/// Suffix to add to all the generated tables to separate help group them together
		/// and make them stand out as different from other tables.
		/// Defaults to "" set to null or "" to not have any suffix.
		/// </summary>
		public string TableNameSuffix { get; set; }


		public void Apply(LookupDbModel model, bool useParameters, Action<string, IEnumerable<SqlParameter>> runSql)
		{
			CreateTables(model.Lookups, (sql) => runSql(sql, null));
			PopulateLookups(model.Lookups, useParameters, runSql);
			AddForeignKeys(model.References, (sql) => runSql(sql, null));
		}

		private void CreateTables(IEnumerable<LookupData> enums, Action<string> runSql)
		{
			foreach (var lookup in enums)
			{
				runSql(string.Format(
					@"IF OBJECT_ID('{0}', 'U') IS NULL
begin
	CREATE TABLE [{0}] (Id {2} PRIMARY KEY, Name nvarchar({1}));
	exec sys.sp_addextendedproperty @name=N'MS_Description', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',
		@level1name=N'{0}', @value=N'Automatically generated. Contents will be overwritten on app startup. Table & contents generated by https://github.com/timabell/ef-enum-to-lookup';
end",
					TableName(lookup.Name), NameFieldLength, NumericSqlType(lookup.NumericType)));
			}
		}

		private void AddForeignKeys(IEnumerable<EnumReference> refs, Action<string> runSql)
		{
			foreach (var enumReference in refs)
			{
				var fkName = string.Format("FK_{0}_{1}", enumReference.ReferencingTable, enumReference.ReferencingField);

				var sql = string.Format(
					" IF OBJECT_ID('{0}', 'F') IS NULL ALTER TABLE [{1}] ADD CONSTRAINT {0} FOREIGN KEY ([{2}]) REFERENCES [{3}] (Id);",
					fkName, enumReference.ReferencingTable, enumReference.ReferencingField, TableName(enumReference.EnumType.Name)
				);

				runSql(sql);
			}
		}

		private void PopulateLookups(IEnumerable<LookupData> lookupData, bool useParameters, Action<string, IEnumerable<SqlParameter>> runSql)
		{
			foreach (var lookup in lookupData)
			{
				PopulateLookup(lookup, useParameters, runSql);
			}
		}

		private void PopulateLookup(LookupData lookup, bool useParameters, Action<string, IEnumerable<SqlParameter>> runSql)
		{
			var sb = new StringBuilder();
			sb.AppendLine(string.Format("CREATE TABLE #lookups (Id int, Name nvarchar({0}) COLLATE database_default);", NameFieldLength));
			var parameters = new List<SqlParameter>();
			int paramIndex = 0;
			foreach (var value in lookup.Values)
			{
				var id = value.Id;
				var name = value.Name;
				var idParamName = string.Format("id{0}", paramIndex++);
				var nameParamName = string.Format("name{0}", paramIndex++);
				if (useParameters)
				{
					sb.AppendLine(string.Format("INSERT INTO #lookups (Id, Name) VALUES (@{0}, @{1});", idParamName, nameParamName));
				}
				else
				{
					sb.AppendLine(string.Format("INSERT INTO #lookups (Id, Name) VALUES ({0}, N'{1}');", id, SanitizeSqlString(name)));
				}
				parameters.Add(new SqlParameter(idParamName, id));
				parameters.Add(new SqlParameter(nameParamName, name));
			}

			sb.AppendLine(string.Format(@"
MERGE INTO [{0}] dst
	USING #lookups src ON src.Id = dst.Id
	WHEN MATCHED AND src.Name <> dst.Name THEN
		UPDATE SET Name = src.Name
	WHEN NOT MATCHED THEN
		INSERT (Id, Name)
		VALUES (src.Id, src.Name)
	WHEN NOT MATCHED BY SOURCE THEN
		DELETE
;"
				, TableName(lookup.Name)));

			sb.AppendLine("DROP TABLE #lookups;");
			runSql(sb.ToString(), parameters);
		}

		private string SanitizeSqlString(string value)
		{
			return value.Replace("'", "''");
		}

		private string TableName(string enumName)
		{
			return string.Format("{0}{1}{2}", TableNamePrefix, enumName, TableNameSuffix);
		}

		private static string NumericSqlType(Type numericType)
		{
			if (numericType == typeof(byte))
			{
				return "tinyint";
			}
			return "int";
		}
	}
}
