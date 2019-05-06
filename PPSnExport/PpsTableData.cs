﻿#region -- copyright --
//
// Licensed under the EUPL, Version 1.1 or - as soon they will be approved by the
// European Commission - subsequent versions of the EUPL(the "Licence"); You may
// not use this work except in compliance with the Licence.
//
// You may obtain a copy of the Licence at:
// http://ec.europa.eu/idabc/eupl
//
// Unless required by applicable law or agreed to in writing, software distributed
// under the Licence is distributed on an "AS IS" basis, WITHOUT WARRANTIES OR
// CONDITIONS OF ANY KIND, either express or implied. See the Licence for the
// specific language governing permissions and limitations under the Licence.
//
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TecWare.DE.Data;
using TecWare.DE.Stuff;
using TecWare.PPSn.Data;

namespace TecWare.PPSn.Export
{
	internal sealed class PpsTableData : ObservableObject, IPpsTableData
	{
		#region -- class PpsColumnInfo ------------------------------------------------

		private sealed class PpsColumnInfo : IPpsTableColumn
		{
			public PpsColumnInfo(IPpsTableColumn c)
			{
				Ascending = c.Ascending;
				Expression = c.Expression;
			} // ctor

			public PpsColumnInfo(string expr, int offset, int length)
			{
				if (expr[offset] == '+')
				{
					Ascending = true;
					offset++;
					length--;
				}
				else if (expr[offset] == '-')
				{
					Ascending = false;
					offset++;
					length--;
				}

				Expression = expr.Substring(offset, length);
			} // ctor

			public StringBuilder ToString(StringBuilder sb)
			{
				if (Ascending.HasValue)
					sb.Append(Ascending.Value ? '+' : '-');
				sb.Append(Expression);
				return sb;
			} // func ToString

			public override string ToString()
			{
				return Ascending.HasValue
					? (Ascending.Value ? "+" : "-") + Expression
					: Expression;
			} // func ToString

			public bool Equals(PpsColumnInfo other)
				=> Ascending == other.Ascending && Expression != other.Expression;

			public PpsDataOrderExpression ToOrder()
				=> XlProcs.ToOrder(this);

			public PpsDataColumnExpression ToColumn()
				=> XlProcs.ToColumn(this);

			public string Expression { get; }
			public bool? Ascending { get; }
		} // class class PpsColumnInfo

		#endregion

		private string displayName = null;
		private string views = null;
		private string filter = null;
		private PpsColumnInfo[] columnInfos = Array.Empty<PpsColumnInfo>();
		
		public PpsShellGetList GetShellList()
		{
			if (IsEmpty)
				return null;
			else
			{
				return new PpsShellGetList(Views)
				{
					Filter = PpsDataFilterExpression.Parse(Filter),
					Columns = columnInfos.Select(c => c.ToColumn()).ToArray(),
					Order = columnInfos.ToOrder().ToArray()
				};
			}
		} // func GetShellList

		Task IPpsTableData.UpdateAsync(string views, string filter, IEnumerable<IPpsTableColumn> columns)
		{
			Views = views;
			Filter = filter;
			SetColumnCore(columns.Select(c => new PpsColumnInfo(c)));

			return Task.CompletedTask;
		} // func UpdateAsync

		private IEnumerable<PpsColumnInfo> ParseColumnInfo(string value)
		{
			foreach (var (startAt, len) in Procs.SplitNewLinesTokens(value))
			{
				if (len > 0)
					yield return new PpsColumnInfo(value, startAt, len);
			}
		} // func ParseColumnInfo

		public string DisplayName
		{
			get => displayName;
			set => Set(ref displayName, value, nameof(DisplayName));
		} // prop DisplayName

		public string Views
		{
			get => views;
			set
			{
				if (Set(ref views, value, nameof(Views)))
					OnPropertyChanged(nameof(IsEmpty));
			}
		} // prop Views

		public string Filter
		{
			get => filter;
			set => Set(ref filter, value, nameof(Filter));
		} // prop Filter

		private static PpsColumnInfo[] EqualColumns(PpsColumnInfo[] columnInfos, IEnumerable< PpsColumnInfo> newColumnInfos)
		{
			using (var nc = newColumnInfos.GetEnumerator())
			{
				var result = new List<PpsColumnInfo>();
				var idx = 0;
				var isNew = false;
				while (nc.MoveNext())
				{
					if (isNew)
						result.Add(nc.Current);
					else if (idx < columnInfos.Length)
					{
						result.Add(nc.Current);

						isNew = !nc.Current.Equals(columnInfos[idx]);
					}
					else
						isNew = true;
				}

				if (!isNew && result.Count == columnInfos.Length)
					return null;

				return result.ToArray();
			}
		} // func EqualColumns

		private void SetColumnCore(IEnumerable<PpsColumnInfo> newColumnInfos)
		{
			var newColumnsArray = EqualColumns(columnInfos, newColumnInfos);
			if (newColumnsArray != null)
			{
				columnInfos = newColumnsArray;
				OnPropertyChanged(nameof(Columns));
			}
		} // proc SetColumnCore

		public string Columns
		{
			get
			{
				if (columnInfos.Length == 0)
					return String.Empty;
				else
				{
					var sb = new StringBuilder();
					foreach (var c in columnInfos)
						c.ToString(sb).AppendLine();
					return sb.ToString();
				}
			}
			set => SetColumnCore(ParseColumnInfo(value));
		} // prop Columns

		IEnumerable<IPpsTableColumn> IPpsTableData.Columns => columnInfos;

		public bool IsEmpty => String.IsNullOrEmpty(views);
	} // class PpsTableData
}
