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
#define _NOTIFY_BINDING_SOURCE_UPDATE
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using Neo.IronLua;
using TecWare.DE.Networking;
using TecWare.DE.Stuff;
using TecWare.PPSn.Controls;

namespace TecWare.PPSn.UI
{
	#region -- class WpfPaneHelper ----------------------------------------------------

	internal static class WpfPaneHelper
	{
		/// <summary>Find a control by name.</summary>
		/// <param name="control"></param>
		/// <param name="key"></param>
		/// <returns><c>null</c> if there is no control or the current thread is not in the correct ui-thread.</returns>
		public static object GetXamlElement(FrameworkElement control, object key)
			=> key is string && control != null && control.Dispatcher.CheckAccess() ? control.FindName((string)key) : null;
	} // class WpfPaneHelper

	#endregion

	#region -- class PpsGenericWpfChildPane -------------------------------------------

	/// <summary>Sub pane implementation</summary>
	public class PpsGenericWpfChildPane : LuaEnvironmentTable, IPpsLuaRequest, IPpsXamlCode, IPpsXamlDynamicProperties
	{
		private readonly BaseWebRequest fileSource;
		private FrameworkElement control;

		/// <summary></summary>
		/// <param name="parentPane"></param>
		/// <param name="paneData"></param>
		/// <param name="fullUri"></param>
		public PpsGenericWpfChildPane(PpsGenericWpfWindowPane parentPane, object paneData, Uri fullUri)
			: base(parentPane)
		{
			if (parentPane == null)
				throw new ArgumentNullException("parentPane");

			this.fileSource = new BaseWebRequest(new Uri(fullUri, "."), Environment.Encoding);

			// create the control
			if (paneData is XDocument xamlCode)
			{
				control = PpsXamlParser.LoadAsync<FrameworkElement>(xamlCode.CreateReader(), new PpsXamlReaderSettings() { Code = this, CloseInput = true }).AwaitTask();
			}
			else if (paneData is LuaChunk luaCode) // run the chunk on the current table
				luaCode.Run(this, this);
			else
				throw new ArgumentException(); // todo:

			control.DataContext = this;
		} // ctor

		#region -- IPpsXamlCode members -----------------------------------------------

		void IPpsXamlCode.CompileCode(Uri uri, string code)
		{
			var compileTask =
				code != null
				? Environment.CompileAsync(code, uri?.OriginalString ?? "dummy.lua", true)
				: Environment.CompileAsync(Request, uri, true);
			compileTask.AwaitTask().Run(this);
		} // proc CompileCode
		
		#endregion

		#region -- IPpsXamlDynamicProperties members ----------------------------------

		void IPpsXamlDynamicProperties.SetValue(string name, object value)
			=> this[name] = value;

		object IPpsXamlDynamicProperties.GetValue(string name)
			=> this[name];
		
		#endregion

		/// <summary></summary>
		/// <param name="key"></param>
		/// <returns></returns>
		protected override object OnIndex(object key)
			=> base.OnIndex(key) ?? WpfPaneHelper.GetXamlElement(control, key);

		[LuaMember("setControl")]
		private FrameworkElement SetControl(object content)
		{
			if (content is FrameworkElement frameworkElement)
				control = frameworkElement;
			else if (content is LuaWpfCreator creator)
				control = creator.GetInstanceAsync<FrameworkElement>(null).AwaitTask();
			else
				throw new ArgumentNullException(nameof(control), "Invalid control type.");

			OnPropertyChanged(nameof(Control));
			return control;
		} // proc UpdateControl

		[LuaMember]
		private object GetResource(object key)
			=> Control.TryFindResource(key);

		/// <summary></summary>
		[LuaMember]
		public FrameworkElement Control => control;
		/// <summary></summary>
		[LuaMember]
		public BaseWebRequest Request => fileSource;
	} // class PpsGenericWpfChildPane 

	#endregion

	#region -- class LuaDataTemplateSelector ------------------------------------------

	/// <summary>Helper, to implement a template selector as a lua function.</summary>
	public sealed class LuaDataTemplateSelector : DataTemplateSelector
	{
		private readonly Delegate selectTemplate;

		/// <summary>Helper, to implement a template selector as a lua function.</summary>
		/// <param name="selectTemplate">Function that gets called.</param>
		public LuaDataTemplateSelector(Delegate selectTemplate)
		{
			this.selectTemplate = selectTemplate;
		} // ctor

		/// <summary>Calls the template selector function.</summary>
		/// <param name="item">Current item.</param>
		/// <param name="container">Container element.</param>
		/// <returns></returns>
		public override DataTemplate SelectTemplate(object item, DependencyObject container)
			=> (DataTemplate)new LuaResult(Lua.RtInvoke(selectTemplate, item, container))[0];
	} // class LuaDataTemplateSelector

	#endregion

	#region -- class PpsGenericWpfWindowPane ------------------------------------------

	/// <summary>Pane that loads a xaml file, or an lua script.</summary>
	public class PpsGenericWpfWindowPane : LuaEnvironmentTable, IPpsWindowPane, IPpsIdleAction, IPpsLuaRequest, IUriContext, IPpsXamlCode, IPpsXamlDynamicProperties, IServiceProvider
	{
		private readonly IPpsWindowPaneManager paneManager;

		private BaseWebRequest fileSource;
		private PpsGenericWpfControl control;

		private LuaTable arguments; // arguments

		/// <summary>Set this to true, to update the document on idle.</summary>
		private bool forceUpdateSource = false;

		#region -- Ctor/Dtor ----------------------------------------------------------

		/// <summary>Create the pane.</summary>
		/// <param name="environment"></param>
		/// <param name="paneManager"></param>
		public PpsGenericWpfWindowPane(PpsEnvironment environment, IPpsWindowPaneManager paneManager)
			: base(environment)
		{
			this.paneManager = paneManager;
			
			Environment.AddIdleAction(this);
		} // ctor

		/// <summary></summary>
		~PpsGenericWpfWindowPane()
		{
			Dispose(false);
		} // dtor

		/// <summary></summary>
		public void Dispose()
		{
			GC.SuppressFinalize(this);
			Dispose(true);
		} // proc Dispose

		/// <summary></summary>
		/// <param name="disposing"></param>
		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				Environment.RemoveIdleAction(this);
				CallMemberDirect("Dispose", new object[0], throwExceptions: false);
			}
		} // proc Dispose

		#endregion

		#region -- Undo/Redo Management -----------------------------------------------

		/*
		 * TextBox default binding is LostFocus, to support changes in long text, we try to
		 * connact undo operations.
		 */
		private BindingExpression currentBindingExpression = null;

		private static bool IsCharKey(Key k)
		{
			switch (k)
			{
				case Key.Back:
				case Key.Return:
				case Key.Space:
				case Key.D0:
				case Key.D1:
				case Key.D2:
				case Key.D3:
				case Key.D4:
				case Key.D5:
				case Key.D6:
				case Key.D7:
				case Key.D8:
				case Key.D9:
				case Key.A:
				case Key.B:
				case Key.C:
				case Key.D:
				case Key.E:
				case Key.F:
				case Key.G:
				case Key.H:
				case Key.I:
				case Key.J:
				case Key.K:
				case Key.L:
				case Key.M:
				case Key.N:
				case Key.O:
				case Key.P:
				case Key.Q:
				case Key.R:
				case Key.S:
				case Key.T:
				case Key.U:
				case Key.V:
				case Key.W:
				case Key.X:
				case Key.Y:
				case Key.Z:
				case Key.NumPad0:
				case Key.NumPad1:
				case Key.NumPad2:
				case Key.NumPad3:
				case Key.NumPad4:
				case Key.NumPad5:
				case Key.NumPad6:
				case Key.NumPad7:
				case Key.NumPad8:
				case Key.NumPad9:
				case Key.Multiply:
				case Key.Add:
				case Key.Separator:
				case Key.Subtract:
				case Key.Decimal:
				case Key.Divide:
				case Key.Oem1:
				case Key.OemPlus:
				case Key.OemComma:
				case Key.OemMinus:
				case Key.OemPeriod:
				case Key.Oem2:
				case Key.Oem3:
				case Key.Oem4:
				case Key.Oem5:
				case Key.Oem6:
				case Key.Oem7:
				case Key.Oem8:
				case Key.Oem102:
					return true;
				default:
					return false;
			}
		} // func IsCharKey

		private void Control_MouseDownHandler(object sender, MouseButtonEventArgs e)
		{
			if (currentBindingExpression != null && currentBindingExpression.IsDirty)
			{
#if DEBUG && NOTIFY_BINDING_SOURCE_UPDATE
				Debug.Print("TextBox force update on mouse.");
#endif
				forceUpdateSource = true;
			}
		} // event Control_MouseDownHandler

		private void Control_KeyUpHandler(object sender, KeyEventArgs e)
		{
			if (currentBindingExpression != null && currentBindingExpression.IsDirty && !IsCharKey(e.Key))
			{
#if DEBUG && NOTIFY_BINDING_SOURCE_UPDATE
				Debug.Print("TextBox force update on keyboard.");
#endif
				forceUpdateSource = true;
			}
		} // event Control_KeyUpHandler

		private void Control_GotKeyboardFocusHandler(object sender, KeyboardFocusChangedEventArgs e)
		{
			if (e.NewFocus is TextBox newTextBox)
			{
				var b = BindingOperations.GetBinding(newTextBox, TextBox.TextProperty);
				var expr = BindingOperations.GetBindingExpression(newTextBox, TextBox.TextProperty);
				if (b != null && (b.UpdateSourceTrigger == UpdateSourceTrigger.Default || b.UpdateSourceTrigger == UpdateSourceTrigger.LostFocus) && expr.Status != BindingStatus.PathError)
				{
					currentBindingExpression = expr;
#if DEBUG && NOTIFY_BINDING_SOURCE_UPDATE
					Debug.Print("Textbox GotFocus");
#endif
				}
			}
		} // event Control_GotKeyboardFocusHandler

		private void Control_LostKeyboardFocusHandler(object sender, KeyboardFocusChangedEventArgs e)
		{
			if (currentBindingExpression != null && e.OldFocus == currentBindingExpression.Target)
			{
#if DEBUG && NOTIFY_BINDING_SOURCE_UPDATE
				Debug.Print("LostFocus");
#endif
				currentBindingExpression = null;
			}
		} // event Control_LostKeyboardFocusHandler

		private void CheckBindingOnIdle()
		{
			if (currentBindingExpression != null && !forceUpdateSource && currentBindingExpression.IsDirty && !PaneManager.IsActive)
			{
#if DEBUG && NOTIFY_BINDING_SOURCE_UPDATE
				Debug.Print("TextBox force update on idle.");
#endif
				forceUpdateSource = true;
			}
		} // proc CheckBindingOnIdle

		#endregion

		#region -- Lua-Interface ------------------------------------------------------

		/// <summary>Get a resource.</summary>
		/// <param name="key"></param>
		/// <returns><c>null</c>, if the resource was not found.</returns>
		[LuaMember]
		private object GetResource(object key)
			=> Control.TryFindResource(key);

		/// <summary>Load a sub control panel.</summary>
		/// <param name="self"></param>
		/// <param name="path"></param>
		/// <param name="initialTable"></param>
		/// <returns></returns>
		[
		LuaMember("requireXaml", true),
		LuaMember("requirePane", true)
		]
		private LuaResult LuaRequirePane(LuaTable self, string path, LuaTable initialTable = null)
		{
			// get the current root
			var webRequest = self.GetMemberValue(nameof(IPpsLuaRequest.Request)) as BaseWebRequest ?? Request;
			var fullUri = webRequest.GetFullUri(path);
			var paneData = Environment.LoadPaneDataAsync(webRequest, initialTable ?? new LuaTable(), fullUri).AwaitTask();
			return new LuaResult(new PpsGenericWpfChildPane(this, paneData, fullUri));
		} // func LuaRequirePane

		/// <summary>Create a PpsCommand object.</summary>
		/// <param name="command"></param>
		/// <param name="canExecute"></param>
		/// <returns></returns>
		[LuaMember("command")]
		private object LuaCommand(Action<PpsCommandContext> command, Func<PpsCommandContext, bool> canExecute = null)
			=> new PpsCommand(command, canExecute);

		/// <summary>Create a DataTemplateSelector</summary>
		/// <param name="func"></param>
		/// <returns></returns>
		[LuaMember("templateSelector")]
		private DataTemplateSelector LuaDataTemplateSelectorCreate(Delegate func)
			=> new LuaDataTemplateSelector(func);

		/// <summary>Disable the current panel.</summary>
		/// <returns></returns>
		[LuaMember("disableUI")]
		public IPpsProgress DisableUI()
			=> null; // PpsWindowPaneHelper.DisableUI(PaneControl);

		/// <summary>Get the default view of a collection.</summary>
		/// <param name="collection"></param>
		/// <returns></returns>
		[LuaMember("getView")]
		private ICollectionView LuaGetView(object collection)
			=> CollectionViewSource.GetDefaultView(collection);

		/// <summary>Create a new CollectionViewSource of a collection.</summary>
		/// <param name="collection"></param>
		/// <returns></returns>
		[LuaMember("createSource")]
		private CollectionViewSource LuaCreateSource(object collection)
		{
			var collectionArgs = collection as LuaTable;
			if (collectionArgs != null)
				collection = collectionArgs.GetMemberValue("Source");

			if (collection == null)
				throw new ArgumentNullException(nameof(collection));

			// get containted list
			if (collection is IListSource listSource)
				collection = listSource.GetList();

			// function views
			if (!(collection is IEnumerable) && Lua.RtInvokeable(collection))
				collection = new LuaFunctionEnumerator(collection);

			var collectionViewSource = new CollectionViewSource();
			using (collectionViewSource.DeferRefresh())
			{
				collectionViewSource.Source = collection;

				if (collectionArgs != null)
				{
					if (collectionArgs.GetMemberValue(nameof(CollectionView.SortDescriptions)) is LuaTable t)
					{
						foreach (var col in t.ArrayList.OfType<string>())
						{
							if (String.IsNullOrEmpty(col))
								continue;

							string propertyName;
							ListSortDirection direction;

							if (col[0] == '+')
							{
								propertyName = col.Substring(1);
								direction = ListSortDirection.Ascending;
							}
							else if (col[0] == '-')
							{
								propertyName = col.Substring(1);
								direction = ListSortDirection.Descending;
							}
							else
							{
								propertyName = col;
								direction = ListSortDirection.Ascending;
							}

							collectionViewSource.SortDescriptions.Add(new SortDescription(propertyName, direction));
						}
					}

					var viewFilter = collectionArgs.GetMemberValue("ViewFilter");
					if (Lua.RtInvokeable(viewFilter))
						collectionViewSource.Filter += (sender, e) => e.Accepted = Procs.ChangeType<bool>(new LuaResult(Lua.RtInvoke(viewFilter, e.Item)));
				}
			}


			if (collectionViewSource.View == null)
				throw new ArgumentNullException("Could not create a collection view.");

			return collectionViewSource;
		} // func LuaCreateSource

		#endregion

		#region -- Load/Unload --------------------------------------------------------

		private Uri GetPaneUri(LuaTable arguments, bool throwException)
		{
			// get the basic template
			var paneFile = arguments.GetOptionalValue("pane", String.Empty);
			return String.IsNullOrEmpty(paneFile)
				? (throwException ? throw new ArgumentException("pane is missing.") : (Uri)null)
				: Environment.Request.GetFullUri(paneFile); // prepare the base
		} // func GetPaneUri

		/// <summary>Compare the arguments.</summary>
		/// <param name="otherArgumens"></param>
		/// <returns></returns>
		public virtual PpsWindowPaneCompareResult CompareArguments(LuaTable otherArgumens)
		{
			var currentPaneUri = GetPaneUri(arguments, false);
			var otherPaneUri = GetPaneUri(otherArgumens, false);

			if (Uri.Compare(currentPaneUri, otherPaneUri, UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped, StringComparison.Ordinal) == 0)
				return PpsWindowPaneCompareResult.Reload;

			return PpsWindowPaneCompareResult.Incompatible;
		} // func CompareArguments

		/// <summary>Load the content of the panel</summary>
		/// <param name="arguments"></param>
		/// <returns></returns>
		public Task LoadAsync(LuaTable arguments)
			=> LoadInternAsync(arguments);

		/// <summary>Load the content of the panel</summary>
		/// <param name="arguments"></param>
		/// <returns></returns>
		protected virtual async Task LoadInternAsync(LuaTable arguments)
		{
			// save the arguments
			this.arguments = arguments;

			// prepare the base
			var paneUri = GetPaneUri(arguments, true);
			fileSource = new BaseWebRequest(new Uri(paneUri, "."), Environment.Encoding);

			// Load the xaml file and code
			var paneData = await Environment.LoadPaneDataAsync(fileSource, arguments, paneUri);

			// Create the Wpf-Control
			if (paneData is XDocument xamlCode)
			{
				control = await PpsXamlParser.LoadAsync<PpsGenericWpfControl>(xamlCode.CreateReader(), new PpsXamlReaderSettings() { Code = this, BaseUri = paneUri });
				control.Resources[PpsEnvironment.WindowPaneService] = this;
				OnControlCreated();
			}
			else if (paneData is LuaChunk luaCode) // run the code to initalize control, setControl should be called.
				Environment.RunScript(luaCode, this, true, this);
			
			// init bindings
			control.DataContext = this;

			DependencyPropertyDescriptor.FromProperty(PpsGenericWpfControl.TitleProperty, typeof(PpsGenericWpfControl)).AddValueChanged(control, ControlTitleChanged);
			DependencyPropertyDescriptor.FromProperty(PpsGenericWpfControl.SubTitleProperty, typeof(PpsGenericWpfControl)).AddValueChanged(control, ControlSubTitleChanged);

			// notify changes on control
			OnPropertyChanged(nameof(Control));
			OnPropertyChanged(nameof(Title));
			OnPropertyChanged(nameof(SubTitle));
		} // proc LoadInternAsync

		/// <summary>Control is created.</summary>
		protected virtual void OnControlCreated()
		{
			Mouse.AddPreviewMouseDownHandler(Control, Control_MouseDownHandler);
			Mouse.AddPreviewMouseDownOutsideCapturedElementHandler(Control, Control_MouseDownHandler);
			Keyboard.AddPreviewGotKeyboardFocusHandler(Control, Control_GotKeyboardFocusHandler);
			Keyboard.AddPreviewLostKeyboardFocusHandler(Control, Control_LostKeyboardFocusHandler);
			Keyboard.AddPreviewKeyUpHandler(Control, Control_KeyUpHandler);
		} // proc OnControlCreated

		/// <summary>Unload the control.</summary>
		/// <param name="commit"></param>
		/// <returns></returns>
		public virtual Task<bool> UnloadAsync(bool? commit = default(bool?))
		{
			if (Members.ContainsKey("UnloadAsync"))
				CallMemberDirect("UnloadAsync", new object[] { commit }, throwExceptions: true);

			if (control != null)
			{
				DependencyPropertyDescriptor.FromProperty(PpsGenericWpfControl.TitleProperty, typeof(PpsGenericWpfControl)).RemoveValueChanged(control, ControlTitleChanged);
				DependencyPropertyDescriptor.FromProperty(PpsGenericWpfControl.SubTitleProperty, typeof(PpsGenericWpfControl)).RemoveValueChanged(control, ControlSubTitleChanged);

				control = null;
				OnPropertyChanged(nameof(Control));
				OnPropertyChanged(nameof(Title));
				OnPropertyChanged(nameof(SubTitle));
			}

			return Task.FromResult(true);
		} // func UnloadAsync

		private void ControlTitleChanged(object sender, EventArgs e)
			=> OnPropertyChanged(nameof(Title));

		private void ControlSubTitleChanged(object sender, EventArgs e)
			=> OnPropertyChanged(nameof(SubTitle));

		#endregion

		#region -- LuaTable, OnIndex --------------------------------------------------

		/// <summary>Also adds logic to find a control by name.</summary>
		/// <param name="key"></param>
		/// <returns></returns>
		protected override object OnIndex(object key)
			=> base.OnIndex(key) ?? WpfPaneHelper.GetXamlElement(control, key); // todo: XamlParser could do this.

		#endregion

		#region -- UpdateSources ------------------------------------------------------

		/// <summary>Update all bindings. Push pending values to the source.</summary>
		[LuaMember]
		public void UpdateSources()
		{
			forceUpdateSource = false;

			foreach (var expr in BindingOperations.GetSourceUpdatingBindings(Control))
			{
				if (!expr.HasError)
					expr.UpdateSource();
			}
		} // proc UpdateSources

		bool IPpsIdleAction.OnIdle(int elapsed)
		{
			if (elapsed > 300)
			{
				if (forceUpdateSource)
					UpdateSources();
				return false;
			}
			else
			{
				CheckBindingOnIdle();
				return forceUpdateSource;
			}
		} // proc OnIdle

		#endregion

		#region -- UpdateControl (setControl) -----------------------------------------

		private void CheckControl()
		{
			if (control == null)
				throw new InvalidOperationException("Control is not created.");
		} // proc CheckControl

		/// <summary>Set within lua code the control.</summary>
		/// <param name="args"></param>
		/// <returns></returns>
		[LuaMember("setControl")]
		private PpsGenericWpfControl UpdateControl(object args)
		{
			PpsGenericWpfControl returnValue;
			if (args is PpsGenericWpfControl paneControl) // force a framework element
				returnValue = paneControl;
			else if (args is LuaTable t) // should be properties for the PpsGenericWpfControl
			{
				var creator = LuaWpfCreator.CreateFactory(Environment.LuaUI, typeof(PpsGenericWpfControl));
				creator.SetTableMembers(t);
				using (var xamlReader = new PpsXamlReader(creator.CreateReader(this), new PpsXamlReaderSettings() { Code = this, CloseInput = true }))
					returnValue = PpsXamlParser.LoadAsync<PpsGenericWpfControl>(xamlReader).AwaitTask();
			}
			else
				throw new ArgumentException(nameof(args));

			control = returnValue;
			OnControlCreated();

			return control;
		} // proc UpdateControl

		#endregion

		#region -- IPpsXamlCode members -----------------------------------------------
		
		void IPpsXamlCode.CompileCode(Uri uri, string code)
		{
			var compileTask =
				code != null
				? Environment.CompileAsync(code, uri?.OriginalString ?? "dummy.lua", true, new KeyValuePair<string, Type>("self", typeof(LuaTable)))
				: Environment.CompileAsync(Request, uri, true, new KeyValuePair<string, Type>("self", typeof(LuaTable)));
			compileTask.AwaitTask().Run(this, this);
		} // proc CompileCode
		
		#endregion

		#region -- IPpsXamlDynamicProperties members ----------------------------------

		void IPpsXamlDynamicProperties.SetValue(string name, object value)
			=> this[name] = value;

		object IPpsXamlDynamicProperties.GetValue(string name)
			=> this[name];

		#endregion

		/// <summary></summary>
		/// <param name="serviceType"></param>
		/// <returns></returns>
		public virtual object GetService(Type serviceType)
		{
			if (serviceType == typeof(IUriContext)
				||serviceType == typeof(IPpsWindowPane))
				return this;
			else if (serviceType == typeof(IPpsWindowPaneManager))
				return paneManager;
			else
				return null;
		} // func GetService

		/// <summary>Arguments of the generic content.</summary>
		[LuaMember]
		public LuaTable Arguments => arguments;

		/// <summary>Title of the pane</summary>
		[LuaMember]
		public string Title
		{
			get => control?.Title;
			set
			{
				CheckControl();
				control.Title = value;
			}
		} // prop Title

			/// <summary>SubTitle of the pane</summary>
		[LuaMember]
		public string SubTitle
		{
			get => control?.SubTitle;
			set
			{
				CheckControl();
				control.SubTitle = value;
			}
		} // prop SubTitle

		/// <summary>Wpf-Control</summary>
		[LuaMember]
		public PpsGenericWpfControl Control => control;
		/// <summary>This member is resolved dynamic, that is the reason the FrameworkElement Control is public.</summary>
		object IPpsWindowPane.Control => control;

		/// <summary>Access the containing window.</summary>
		[LuaMember]
		public IPpsWindowPaneManager PaneManager => paneManager;
		/// <summary>Base web request, for the pane.</summary>
		[LuaMember]
		public BaseWebRequest Request => fileSource;

		/// <summary>BaseUri of the Wpf-Control</summary>
		public Uri BaseUri { get => fileSource?.BaseUri; set => throw new NotSupportedException(); }

		/// <summary>Synchronization to the UI.</summary>
		public Dispatcher Dispatcher => control == null ? Application.Current.Dispatcher : control.Dispatcher;
		/// <summary>Access to the current lua compiler</summary>
		public Lua Lua => Environment.Lua;

		/// <summary>Get the registered commands.</summary>
		public IEnumerable<object> Commands => control?.Commands;

		/// <summary>Is the current pane dirty.</summary>
		public virtual bool IsDirty => false;
	} // class PpsGenericWpfWindowContext

	#endregion
}
