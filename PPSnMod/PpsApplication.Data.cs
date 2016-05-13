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
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Neo.IronLua;
using TecWare.DE.Networking;
using TecWare.DE.Server;
using TecWare.DE.Server.Http;
using TecWare.DE.Stuff;
using TecWare.DE.Data;
using TecWare.PPSn.Server.Data;
using TecWare.PPSn.Server.Sql;
using static TecWare.PPSn.Server.PpsStuff;

namespace TecWare.PPSn.Server
{
	#region -- class PpsFieldDescription --------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public sealed class PpsFieldDescription : IPpsColumnDescription, IPropertyReadOnlyDictionary, IEnumerable<PropertyValue>
	{
		private const string DisplayNameAttributeString = "displayName";
		private const string MaxLengthAttributeString = "maxLength";
		private const string DataTypeAttributeString = "dataType";

		private readonly PpsDataSource source;
		private readonly string name;
		private readonly XElement xDefinition;
		private IPpsColumnDescription columnDescription = null;

		private readonly Lazy<string> displayName;
		private readonly Lazy<int> maxLength;
		private readonly Lazy<Type> dataType;

		public PpsFieldDescription(PpsDataSource source, string name, XElement xDefinition)
		{
			this.source = source;
			this.name = name;
			this.xDefinition = xDefinition;

			displayName = new Lazy<string>(() => xDefinition.GetAttribute(DisplayNameAttributeString, null) ?? GetFieldAttribute(DisplayNameAttributeString, name));
			maxLength = new Lazy<int>(() => GetFieldAttribute(MaxLengthAttributeString, Int32.MaxValue));
			dataType = new Lazy<Type>(() => GetDataTypeFromAttribute() ?? GetFieldAttribute(DataTypeAttributeString, typeof(string)));
		} // ctor

		internal void SetColumnDescription(IPpsColumnDescription columnDescription)
		{
			this.columnDescription = columnDescription;
		} // ResolveColumnDescription

		private Type GetDataTypeFromAttribute()
		{
			var typeString = xDefinition.GetAttribute(DataTypeAttributeString, null);
			if (String.IsNullOrEmpty(typeString))
				return null;

			try
			{
				return LuaType.GetType(typeString, lateAllowed: false).Type;
			}
			catch
			{
				return typeof(string);
			}
		} // func GetDataTypeFromAttribute

		public T GetFieldAttribute<T>(string attributeName, T @default)
		{
			var f = xDefinition.Elements(xnFieldAttribute).FirstOrDefault(x => String.Compare(x.GetAttribute("name", String.Empty), attributeName, StringComparison.OrdinalIgnoreCase) == 0);
			if (f?.Value== null)
				return @default;

			try
			{
				if (typeof(T) == typeof(Type))
					return (T)(object)LuaType.GetType(f.Value, lateAllowed: false).Type;
				else
					return f.Value.ChangeType<T>();
			}
			catch
			{
				return @default;
			}
		} // func GetFieldAttribute

		public bool TryGetProperty(string name, out object value)
		{
			value = null;
			return false;
		} // func TryGetProperty

		public IEnumerator<PropertyValue> GetEnumerator()
		{
			yield break;
		}

		IEnumerator IEnumerable.GetEnumerator()
			=> GetEnumerator();

		public PpsDataSource DataSource => source;
		public string Name => name;

		public IPpsColumnDescription NativeColumnDescription => columnDescription;

		public string DisplayName => displayName.Value;

		public int MaxLength => columnDescription?.MaxLength ?? maxLength.Value;
		public Type DataType => columnDescription?.DataType ?? dataType.Value;
		public bool IsIdentity => columnDescription?.IsIdentity ?? false;
	} // class PpsFieldDescription

	#endregion

	#region -- class PpsViewDefinition --------------------------------------------------

	public sealed class PpsViewDescription
	{
		private readonly IPpsSelectorToken selectorToken;
		private readonly string displayName;
		
		// filter, sort

		public PpsViewDescription(IPpsSelectorToken selectorToken, string displayName)
		{
			this.selectorToken = selectorToken;
			this.displayName = displayName;
		} // ctor
				
		public string Name => selectorToken.Name;
		public string DisplayName => displayName;
		//public string SecurityToken => DEConfigItem.SecuritySys;

		public IPpsSelectorToken SelectorToken => selectorToken;
	} // class PpsViewDefinition

	#endregion

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public partial class PpsApplication
	{
		#region -- class PpsViewDefinitionInit --------------------------------------------

		///////////////////////////////////////////////////////////////////////////////
		/// <summary></summary>
		private sealed class PpsViewDescriptionInit
		{
			private readonly PpsDataSource source;
			private readonly string name;
			private readonly XElement xDefinition;
			
			public PpsViewDescriptionInit(PpsDataSource source, string name, XElement xDefinition)
			{
				this.source = source;
				this.name = name;
				this.xDefinition = xDefinition;
			} // ctor

			public async Task<PpsViewDescription> InitializeAsync()
			{
				var sourceDescription = xDefinition.Element(xnSource);
				if (sourceDescription == null)
					throw new DEConfigurationException(xDefinition, "source definition is missing.");

				var selectorToken = await source.CreateSelectorToken(name, sourceDescription);
				var view = new PpsViewDescription(selectorToken, xDefinition.GetAttribute("displayName", name));

				return view;
			} // proc InitializeAsync
		} // class PpsViewDefinitionInit

		#endregion

		#region -- class DependencyElement ------------------------------------------------

		///////////////////////////////////////////////////////////////////////////////
		/// <summary></summary>
		private sealed class DependencyElement
		{
			private readonly PpsDataSource source;
			private readonly string name;
			private readonly string[] inherited;
			private readonly XElement element;

			public DependencyElement(PpsDataSource source, XElement element)
			{
				this.source = source;

				this.name = element.GetAttribute("name", String.Empty);
				if (String.IsNullOrEmpty(this.name))
					throw new DEConfigurationException(element, "@name is empty.");

				var inheritedStrings = element.GetAttribute("inherited", String.Empty);
				if (String.IsNullOrEmpty(inheritedStrings))
					this.inherited = null;
				else
				{
					var tmp = inheritedStrings.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
					inherited = tmp.Length == 0 ? null : tmp;
				}

				this.element = element;
			} // ctor

			public override string ToString()
				=> $"{GetType().Name}: {name}";

			public PpsDataSource Source => source;
			public string Name => name;
			public string[] Inherited => inherited;
			public XElement Element => element;

			private bool IsRegistered { get; set; } = false;

			private static void RegisterDependencyItem(Stack<string> stack, List<DependencyElement> list, DependencyElement dependencyElement, Action<PpsDataSource, string, XElement> registerMethod)
			{
				if (dependencyElement.inherited != null)
				{
					foreach (var cur in dependencyElement.inherited)
					{
						// find the current dependency - lax
						var index = list.FindIndex(c => String.Compare(c.Name, cur, StringComparison.OrdinalIgnoreCase) == 0);
						if (index >= 0 && !list[index].IsRegistered)
						{
							// check recursion
							if (stack.FirstOrDefault(c => String.Compare(c, cur, StringComparison.OrdinalIgnoreCase) == 0) != null)
								throw new ArgumentOutOfRangeException("todo: rek");

							// register the dependency
							stack.Push(cur);
							RegisterDependencyItem(stack, list, list[index], registerMethod);
							list[index].IsRegistered = true;
						}
					}
				}

				registerMethod(dependencyElement.Source, dependencyElement.Name, dependencyElement.Element);
			} // proc RegisterDependencyItem

			public static void RegisterList(List<DependencyElement> list, Action<PpsDataSource, string, XElement> registerMethod)
			{
				var stack = new Stack<string>();

				// resolve dependencies
				for (var i = 0; i < list.Count; i++)
				{
					if (list[i] != null)
					{
						stack.Clear();
						RegisterDependencyItem(stack, list, list[i], registerMethod);
						list[i].IsRegistered = true;
					}
				}
			} // proc RegisterList
		} // class DependencyElement

		#endregion

		private PpsSqlExDataSource mainDataSource;

		private Dictionary<string, PpsFieldDescription> fieldDescription = new Dictionary<string, PpsFieldDescription>(StringComparer.OrdinalIgnoreCase);
		private Dictionary<string, PpsViewDescription> viewController = new Dictionary<string, PpsViewDescription>(StringComparer.OrdinalIgnoreCase);
		private Dictionary<string, PpsDataSetServerDefinition> datasetDefinitions = new Dictionary<string, PpsDataSetServerDefinition>(StringComparer.OrdinalIgnoreCase);

		#region -- Init/Done --------------------------------------------------------------

		private void InitData()
		{
			//mainDataSource = new PpsSqlExDataSource(this, "Data Source=Gurke,6444;Initial Catalog=PPSn01;Integrated Security=True");
		} // proc InitData

		private void BeginReadConfigurationData(IDEConfigLoading config)
		{
			// find mainDataSource
			var mainDataSourceName = config.ConfigNew.GetAttribute("mainDataSource", String.Empty);
			if (String.IsNullOrEmpty(mainDataSourceName))
				throw new DEConfigurationException(config.ConfigNew, "@mainDataSource is empty.");

			var newMainDataSource = this.UnsafeFind(mainDataSourceName);
			if (newMainDataSource == null)
				throw new DEConfigurationException(config.ConfigNew, String.Format("@mainDataSource '{0}' not found.", mainDataSourceName));
			if(!(newMainDataSource is PpsSqlExDataSource))
				throw new DEConfigurationException(config.ConfigNew, String.Format("@mainDataSource '{0}' is a unsupported data source.", mainDataSourceName));

			config.EndReadAction(() => mainDataSource = (PpsSqlExDataSource)newMainDataSource);
		} // proc BeginReadConfigurationData

		private void BeginEndConfigurationData(IDEConfigLoading config)
		{
			var fieldDeclarationList = new List<DependencyElement>();
			var viewDeclarationList = new List<DependencyElement>();
			var datasetDeclarationList = new List<DependencyElement>();

			// register views, columns, ...
			// we add or overide elements, but there is no deletion -> reboot
			foreach (var xRegister in config.ConfigNew.Elements(xnRegister))
			{
				// evaluate the source, that is connected to the objects
				var sourceName = xRegister.GetAttribute("source", String.Empty);
				var source = String.IsNullOrEmpty(sourceName) ? null : GetDataSource(sourceName, true);

				foreach (var xNode in xRegister.Elements())
				{
					if (xNode.Name == xnField)
						fieldDeclarationList.Add(new DependencyElement(source, xNode));
					else if (xNode.Name == xnView)
						viewDeclarationList.Add(new DependencyElement(source, xNode));
					else if (xNode.Name == xnDataSet)
						datasetDeclarationList.Add(new DependencyElement(source, xNode));
					else
						throw new NotImplementedException();
				}
			}

			DependencyElement.RegisterList(fieldDeclarationList, RegisterField); // register all fields
			DependencyElement.RegisterList(viewDeclarationList, RegisterView); // register all views
			DependencyElement.RegisterList(datasetDeclarationList, RegisterDataSet); // register all datasets
		} // proc BeginEndConfigurationData

		private void DoneData()
		{
		} // proc DoneData

		#endregion

		#region -- Register ---------------------------------------------------------------

		private void RegisterField(PpsDataSource source, string name, XElement x)
		{
			var fieldInfo = new PpsFieldDescription(source, name, x); // create generic field definition
			fieldDescription[name] = fieldInfo;
			if (source != null) // create a provider specific field
			{
				RegisterInitializationTask(10001, "Resolve columns", () =>
				{
					fieldInfo.SetColumnDescription(source.GetColumnDescription(name));
					return Task.CompletedTask;
				});
			}
		} // proc RegisterField

		public void RegisterView(IPpsSelectorToken selectorToken, string displayName = null)
		{
			RegisterView(new PpsViewDescription(selectorToken, displayName ?? selectorToken.Name));
		} // func RegisterView

		private void RegisterView(PpsDataSource source, string name, XElement x)
		{
			var cur = new PpsViewDescriptionInit(source, name, x);
			RegisterInitializationTask(10002, "Build views", async () => RegisterView(await cur.InitializeAsync()));
		} // func RegisterView

		private void RegisterView(PpsViewDescription view)
		{
			lock (viewController)
				viewController[view.Name] = view;
		} // func RegisterView

		private void RegisterDataSet(PpsDataSource source, string name, XElement x)
		{
			var datasetDefinition = source == null ? new PpsDataSetServerDefinition(this, name, x) : source.CreateDocumentDescription(this, name, x);
			lock (datasetDefinitions)
				datasetDefinitions.Add(datasetDefinition.Name, datasetDefinition);
		} // void RegisterDataSet

		#endregion

		public PpsDataSource GetDataSource(string name, bool throwException)
		{
			using (this.EnterReadLock())
				return (PpsDataSource)this.UnsafeChildren.FirstOrDefault(c => c is PpsDataSource && String.Compare(c.Name, name, StringComparison.OrdinalIgnoreCase) == 0);
		} // func GetDataSource

		public PpsFieldDescription GetFieldDescription(string name, bool throwException = true)
		{
			PpsFieldDescription fieldInfo;
			if (fieldDescription.TryGetValue(name, out fieldInfo))
				return fieldInfo;
			else if (throwException)
				throw new ArgumentOutOfRangeException(); // todo:
			else
				return null;
		} // func GetFieldDescription

		public PpsViewDescription GetViewDefinition(string name, bool throwException = true)
		{
			PpsViewDescription viewInfo;
			lock (viewController)
			{
				if (viewController.TryGetValue(name, out viewInfo))
					return viewInfo;
				else if (throwException)
					throw new ArgumentOutOfRangeException(); // todo:
				else
					return null;
			}
		} // func GetViewDefinition

		public PpsDataSetServerDefinition GetDataSetDefinition(string name, bool throwException = true)
		{
			lock (datasetDefinitions)
			{
				PpsDataSetServerDefinition datasetDefinition;
				if (datasetDefinitions.TryGetValue(name, out datasetDefinition))
					return datasetDefinition;
				else if (throwException)
					throw new ArgumentOutOfRangeException($"Dataset definition '{name}' not found.");
				else
					return null;
			}
		} // func GetDataSetDefinition

		private void WriteDataRow(XmlWriter xml, IDataValues row, string[] columnNames)
		{
			xml.WriteStartElement("r");
			for (var i = 0; i < columnNames.Length; i++)
			{
				if (columnNames[i] != null)
				{
					var v = row[i];
					if (v != null)
						xml.WriteElementString(columnNames[i], v.ChangeType<string>());
				}
			}
			xml.WriteEndElement();
		} // proc WriteDataRow

		[DEConfigHttpAction("viewget", IsSafeCall = false)]
		private void HttpViewGetAction(IDEContext r)
		{
			// v=views,...&filter=list&sort=list=&start&count
			// ???views => view,view2(c1+c2),view3(c3+c4)
			// sort: +FIELD,-FIELD,:DEF
			// ???filter: :DEF-and-not-or-xor-contains-

			var startAt = r.GetProperty("s", 0);
			var count = r.GetProperty("c", Int32.MaxValue);

			var ctx = r.GetUser<IPpsPrivateDataContext>();

			var selector = ctx.CreateSelector(
				r.GetProperty<string>("v", null),
				r.GetProperty<string>("f", null),
				r.GetProperty<string>("o", null),
				true
			);

			// emit the selector
			using (var tw = r.GetOutputTextWriter(MimeTypes.Text.Xml))
			using (var xml = XmlWriter.Create(tw, GetSettings(tw)))
			{
				xml.WriteStartDocument();
				xml.WriteStartElement("view");

				// execute the complete statemet
				using (var enumerator = selector.GetEnumerator(startAt, count))
				{
					bool emitCurrentRow = false;
					
					// extract the columns, optional before the fetch operation
					var columnDefinition = enumerator as IDataColumns;
					if (columnDefinition == null)
					{
						if (enumerator.MoveNext())
						{
							emitCurrentRow = true;
							columnDefinition = enumerator.Current;
						}
						else
							count = 0; // no rows
					}

					// emit column description
					string[] columnNames = null;
					if (columnDefinition != null)
					{
						columnNames = new string[columnDefinition.ColumnCount];

						xml.WriteStartElement("fields");
						for (var i = 0; i < columnNames.Length; i++)
						{
							var nativeColumnName = columnDefinition.ColumnNames[i];
							var fieldDefinition = selector.GetFieldDescription(nativeColumnName);

							if (fieldDefinition == null)
							{
								columnNames[i] = null;
								continue;
							}
							else
							{
								columnNames[i] = nativeColumnName;

								new XElement(nativeColumnName,
									new XAttribute("type", LuaType.GetType(fieldDefinition.DataType).AliasOrFullName),
									new XAttribute("field", fieldDefinition.Name)
								).WriteTo(xml);
							}
						}
						xml.WriteEndElement();
					}

					// emit first row
					xml.WriteStartElement("rows");
					if (emitCurrentRow)
					{
						WriteDataRow(xml, enumerator.Current, columnNames);
						count--;
					}

					// emit all rows
					while (count > 0)
					{
						if (!enumerator.MoveNext())
							break;

						WriteDataRow(xml, enumerator.Current, columnNames);

						count--;
					}

					xml.WriteEndElement();
				}

				xml.WriteEndElement();
				xml.WriteEndDocument();
			}
		} // func HttpViewGetAction

		public PpsDataSource MainDataSource => mainDataSource;
	} // class PpsApplication
}
