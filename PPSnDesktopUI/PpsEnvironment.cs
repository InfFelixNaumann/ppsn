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
using System.Collections.Specialized;
using System.Data.SQLite;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;
using Neo.IronLua;
using TecWare.DE.Data;
using TecWare.DE.Networking;
using TecWare.DE.Stuff;
using TecWare.PPSn.Controls;
using TecWare.PPSn.Data;
using LExpression = System.Linq.Expressions.Expression;

namespace TecWare.PPSn
{
	#region -- class IPpsEnvironmentDefinition ------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public interface IPpsEnvironmentDefinition
	{
		PpsEnvironment Environment { get; }
		string Name { get; }
	} // interface IPpsEnvironmentDefinition

	#endregion

	#region -- class PpsEnvironmentDefinition -------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Base class of sort and accessable environment items.</summary>
	public abstract class PpsEnvironmentDefinition : IPpsEnvironmentDefinition
	{
		private readonly PpsEnvironment environment;
		private readonly string name;           // internal name of the item

		protected PpsEnvironmentDefinition(PpsEnvironment environment, string name)
		{
			if (String.IsNullOrEmpty(name))
				throw new ArgumentNullException("name");
			this.environment = environment;
			this.name = name;
		} // ctor

		/// <summary>Access to the owning environment.</summary>
		public PpsEnvironment Environment => environment;
		/// <summary>Name of the property.</summary>
		public string Name => name;
	} // class PpsEnvironmentDefinition

	#endregion

	#region -- interface IPpsEnvironmentCollectionInternal ------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	internal interface IPpsEnvironmentCollectionInternal
	{
		int FindItemIndex(string name);

		object this[int index] { get; }

		int CurrentVersion { get; }
		int Count { get; }
	} // interface IPpsEnvironmentCollectionInternal

	#endregion

	#region -- class PpsEnvironmentCollection -------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public sealed class PpsEnvironmentCollection<T> : IPpsEnvironmentCollectionInternal, ICollection, IDictionary<string, T>, INotifyCollectionChanged, IDynamicMetaObjectProvider
		where T : class, IPpsEnvironmentDefinition
	{
		#region -- class ValueCollection --------------------------------------------------

		///////////////////////////////////////////////////////////////////////////////
		/// <summary></summary>
		private class ValueCollection : ICollection<T>
		{
			private PpsEnvironmentCollection<T> owner;

			public ValueCollection(PpsEnvironmentCollection<T> owner)
			{
				this.owner = owner;
			} // ctor

			public void Add(T item)
			{ throw new NotSupportedException(); }
			public void Clear()
			{ throw new NotSupportedException(); }
			public bool Remove(T item)
			{ throw new NotSupportedException(); }

			public void CopyTo(T[] array, int arrayIndex)
			{
				lock (owner.items)
				{
					foreach (var i in owner.keys.Values)
						array[arrayIndex++] = owner.items[i];
				}
			} // prop CopyTo
			public IEnumerator<T> GetEnumerator()
			{
				lock (owner.items)
				{
					foreach (var i in owner.keys.Values)
						yield return owner.items[i];
				}
			} // func GetEnumerator

			IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
			public bool Contains(T item) => owner.items.Contains(item);

			public bool IsReadOnly => true;

			public int Count => owner.Count;
		} // class ValueCollection

		#endregion

		#region -- class PpsEnvironmentMetaObjectProvider ---------------------------------

		private sealed class PpsEnvironmentMetaObjectProvider : DynamicMetaObject
		{
			public PpsEnvironmentMetaObjectProvider(LExpression expression, PpsEnvironmentCollection<T> value)
				: base(expression, BindingRestrictions.Empty, value)
			{
			} // ctor

			private BindingRestrictions GetRestriction(IPpsEnvironmentCollectionInternal value, bool countRestriction)
			{
				var expr = LExpression.AndAlso(
					LExpression.TypeIs(Expression, typeof(IPpsEnvironmentCollectionInternal)),
					LExpression.AndAlso(
						LExpression.Equal(Expression, LExpression.Constant(Value)),
						LExpression.Equal(
							LExpression.Property(
								LExpression.Convert(Expression, typeof(IPpsEnvironmentCollectionInternal)),
								countRestriction ? CountPropertyInfo : CurrentVersionPropertyInfo
							),
							LExpression.Constant(countRestriction ? value.Count : value.CurrentVersion)
						)
					)
				);
				return BindingRestrictions.GetExpressionRestriction(expr);
			} // func GetRestriction

			private DynamicMetaObject BindGetItem(string name, bool throwException)
			{
				var value = (IPpsEnvironmentCollectionInternal)Value;

				var index = value.FindItemIndex(name);
				if (index >= 0)
				{
					return new DynamicMetaObject(
						LExpression.MakeIndex(
							LExpression.Convert(Expression, typeof(IPpsEnvironmentCollectionInternal)),
							ItemsPropertyInfo,
							new LExpression[] { LExpression.Constant(index) }
						), GetRestriction(value, false)
					);
				}
				else
				{
					LExpression expr;
					if (throwException)
					{
						expr = LExpression.Throw(
							LExpression.New(Procs.ArgumentOutOfRangeConstructorInfo2,
								new LExpression[]
								{
									LExpression.Constant(name),
									LExpression.Constant("Could not get environment item.")
								}
							), typeof(object)
						);
					}
					else
						expr = LExpression.Constant(null, typeof(object));

					return new DynamicMetaObject(expr, GetRestriction(value, true));
				}
			} // func BindGetItem

			public override DynamicMetaObject BindGetMember(GetMemberBinder binder)
				=> BindGetItem(binder.Name, false);

			public override DynamicMetaObject BindInvokeMember(InvokeMemberBinder binder, DynamicMetaObject[] args)
				=> BindGetItem(binder.Name, true);

			// -- Static ------------------------------------------------------------

			private static readonly PropertyInfo CountPropertyInfo;
			private static readonly PropertyInfo CurrentVersionPropertyInfo;
			private static readonly PropertyInfo ItemsPropertyInfo;

			static PpsEnvironmentMetaObjectProvider()
			{
				var t = typeof(IPpsEnvironmentCollectionInternal);
				CountPropertyInfo = Procs.GetProperty(t, nameof(IPpsEnvironmentCollectionInternal.Count));
				CurrentVersionPropertyInfo = Procs.GetProperty(t, nameof(IPpsEnvironmentCollectionInternal.CurrentVersion));
				ItemsPropertyInfo = Procs.GetProperty(t, "Item", typeof(int));
			} // sctor
		} // class PpsEnvironmentMetaObjectProvider

		#endregion

		public event NotifyCollectionChangedEventHandler CollectionChanged;

		private readonly PpsEnvironment environment;
		private int currentVersion = 0;
		private List<T> items = new List<T>(); // list with all items
		private Dictionary<string, int> keys = new Dictionary<string, int>(); // list with all active items

		#region -- Ctor/Dtor --------------------------------------------------------------

		public PpsEnvironmentCollection(PpsEnvironment environment)
		{
			this.environment = environment;
		} // ctor

		public DynamicMetaObject GetMetaObject(LExpression parameter)
			=> new PpsEnvironmentMetaObjectProvider(parameter, this);

		#endregion

		public void AppendItem(T item)
		{
			var indexAdded = -1;

			if (item.Environment != environment)
				throw new ArgumentException("Invalid environment.");

			lock (items)
			{
				indexAdded = items.Count;
				items.Add(item);

				// update keys
				keys[item.Name] = indexAdded;
			}

			if (indexAdded != -1)
				OnAddCollection(item);
		} // proc AppendItem

		public void Clear()
		{
			lock (items)
			{
				items.Clear();
				keys.Clear();
				currentVersion++;
			}
			OnResetCollection();
		} // proc Clear

		private int FindIndexByKey(string name)
		{
			lock (items)
			{
				var index = 0;
				return keys.TryGetValue(name, out index) ? index : -1;
			}
		} // func FindIndexByKey

		private void OnResetCollection()
			=> CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

		private void OnAddCollection(object added)
			=> CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, added));

		#region -- IDictionary, IList, ... ------------------------------------------------

		bool IDictionary<string, T>.TryGetValue(string key, out T value)
		{
			lock (items)
			{
				var index = FindIndexByKey(key);
				if (index >= 0)
				{
					value = items[index];
					return true;
				}
				else
				{
					value = null;
					return false;
				}
			}
		} // func IDictionary<string, T>.TryGetValue

		void ICollection<KeyValuePair<string, T>>.CopyTo(KeyValuePair<string, T>[] array, int arrayIndex)
		{
			// normally the locking is done by the consumer
			foreach (var i in keys.Values)
				array[arrayIndex++] = new KeyValuePair<string, T>(items[i].Name, items[i]);
		} // proc ICollection<KeyValuePair<string, T>>.CopyTo

		IEnumerator<KeyValuePair<string, T>> IEnumerable<KeyValuePair<string, T>>.GetEnumerator()
		{
			lock (items)
			{
				foreach (T c in this)
					yield return new KeyValuePair<string, T>(c.Name, c);
			}
		} // func IEnumerable<KeyValuePair<string, T>>.GetEnumerator

		void ICollection.CopyTo(Array array, int index)
		{
			lock (items)
			{
				foreach (var i in keys.Values)
					array.SetValue(items[i], index++);
			}
		} // proc ICollection.CopyTo

		bool IDictionary<string, T>.ContainsKey(string key)
		{
			lock (items)
				return keys.ContainsKey(key);
		} // func IDictionary<string, T>.ContainsKey

		public IEnumerator GetEnumerator()
		{
			lock (items)
			{
				foreach (var i in keys.Values)
					yield return items[i];
			}
		} // func IEnumerable.GetEnumerator

		void IDictionary<string, T>.Add(string key, T value) { throw new NotSupportedException(); }
		bool IDictionary<string, T>.Remove(string key) { throw new NotSupportedException(); }
		T IDictionary<string, T>.this[string key] { get { return this[key]; } set { throw new NotSupportedException(); } }
		ICollection<T> IDictionary<string, T>.Values => new ValueCollection(this);
		ICollection<string> IDictionary<string, T>.Keys => keys.Keys;

		void ICollection<KeyValuePair<string, T>>.Add(KeyValuePair<string, T> item) { throw new NotSupportedException(); }
		bool ICollection<KeyValuePair<string, T>>.Contains(KeyValuePair<string, T> item) { throw new NotSupportedException(); }
		bool ICollection<KeyValuePair<string, T>>.Remove(KeyValuePair<string, T> item) { throw new NotSupportedException(); }

		bool ICollection.IsSynchronized => true;
		object ICollection.SyncRoot => items;
		bool ICollection<KeyValuePair<string, T>>.IsReadOnly => true;

		#endregion

		int IPpsEnvironmentCollectionInternal.FindItemIndex(string name)
		{
			int index;
			return keys.TryGetValue(name, out index) ? index : -1;
		} // func FindItemIndex

		public T this[string name, bool throwExpression = false]
		{
			get
			{
				lock (items)
				{
					var index = 0;
					if (keys.TryGetValue(name, out index))
						return items[index];
					else if (throwExpression)
						throw new ArgumentOutOfRangeException(name);
					else
						return null;
				}
			}
		} // prop this

		/// <summary>Number of items</summary>
		public int Count => keys.Count;
		/// <summary></summary>
		public PpsEnvironment Environment => environment;

		int IPpsEnvironmentCollectionInternal.CurrentVersion => currentVersion;
		object IPpsEnvironmentCollectionInternal.this[int index] => items[index];
	} // class PpsEnvironmentCollection

	#endregion

	#region -- enum PpsEnvironmentState -------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Current state of the environment.</summary>
	public enum PpsEnvironmentState
	{
		/// <summary>Nothing</summary>
		None,
		/// <summary>System environment will be destroyed or is destroyed.</summary>
		Shutdown,
		/// <summary>System is offline.</summary>
		Offline,
		/// <summary>System is offline and tries to connect to the server.</summary>
		OfflineConnect,
		/// <summary>System is online.</summary>
		Online
	} // enum PpsEnvironmentState

	#endregion

	#region -- enum PpsEnvironmentMode --------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Current mode of the environment.</summary>
	public enum PpsEnvironmentMode
	{
		/// <summary>No state.</summary>
		None,
		/// <summary>System offline.</summary>
		Offline,
		/// <summary>System online.</summary>
		Online,
		/// <summary>System closes.</summary>
		Shutdown
	} // enum PpsEnvironmentMode

	#endregion

	#region -- enum PpsEnvironmentModeResult --------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public enum PpsEnvironmentModeResult
	{
		/// <summary>System offline.</summary>
		Offline,
		/// <summary>System online.</summary>
		Online,
		/// <summary>System closes.</summary>
		Shutdown,

		/// <summary>Application data is outdated.</summary>
		NeedsUpdate,
		/// <summary>Start synchronization.</summary>
		NeedsSynchronization,
		/// <summary>State change login failed.</summary>
		LoginFailed,
		/// <summary>Server is not available.</summary>
		ServerConnectFailure
	} // enum PpsEnvironmentModeResult

	#endregion

	#region -- class PpsEnvironment -----------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Base class for application data. Holds information about view
	/// classes, exception, connection, synchronisation and the script 
	/// engine.</summary>
	public partial class PpsEnvironment : LuaGlobalPortable, IPpsShell, IServiceProvider, IDisposable
	{
		private readonly int environmentId;				// unique id of the environment
		private readonly PpsEnvironmentInfo info;		// source information of the environment
		private readonly ICredentials userInfo;			// currently credentials of the user

		private string userName = null;					// display name of the user
		private readonly DirectoryInfo localDirectory = null;	// local directory for the user data
		
		private PpsTraceLog logData = new PpsTraceLog();
		private PpsDataListTemplateSelector dataListTemplateSelector;
		private PpsEnvironmentCollection<PpsDataListItemDefinition> templateDefinitions;

		private readonly List<object> services = new List<object>();

		#region -- Ctor/Dtor --------------------------------------------------------------

		public PpsEnvironment(PpsEnvironmentInfo info, ICredentials userInfo, ResourceDictionary mainResources)
			: base(new Lua())
		{
			if (info == null)
				throw new ArgumentNullException("info");
			if (userInfo == null)
				throw new ArgumentNullException("userInfo");
			if (mainResources == null)
				throw new ArgumentNullException("mainResources");

			this.info = info;
			this.userInfo = userInfo;
			this.userName = PpsEnvironmentInfo.GetUserNameFromCredentials(userInfo);

			this.localDirectory = new DirectoryInfo(Path.Combine(info.LocalPath.FullName, this.Username));
			if (!localDirectory.Exists)
				localDirectory.Create();

			this.activeDataSets = new PpsActiveDataSetsImplementation(this);
			this.objectInfo = new PpsEnvironmentCollection<PpsObjectInfo>(this);
			this.synchronizationWorker = new PpsObjectSynchronizationWorker(this);

			Neo.IronLua.LuaType.RegisterTypeAlias("text", typeof(PpsFormattedStringValue));
			Neo.IronLua.LuaType.RegisterTypeAlias("blob", typeof(byte[]));

			// create ui stuff
			this.mainResources = mainResources;
			this.currentDispatcher = Dispatcher.CurrentDispatcher;
			this.inputManager = InputManager.Current;
			this.synchronizationContext = new DispatcherSynchronizationContext(currentDispatcher);
			this.dataListTemplateSelector = new PpsDataListTemplateSelector(this);
			this.templateDefinitions = new PpsEnvironmentCollection<PpsDataListItemDefinition>(this);

			CreateLuaCompileOptions();

			// enable trace access
			BindingOperations.EnableCollectionSynchronization(logData, logData.SyncRoot,
				(collection, context, accessMethod, writeAccess) => currentDispatcher.Invoke(accessMethod)
			);

			// Start idle implementation
			this.idleTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.ApplicationIdle, (sender, e) => OnIdle(), currentDispatcher);
			inputManager.PreProcessInput += preProcessInputEventHandler = (sender, e) => RestartIdleTimer(e);

			// Register internal uri
			lock (environmentCounterLock)
				this.environmentId = environmentCounter++;

			// initialize local store
			this.baseUri = InitProxy();
			request = new BaseWebRequest(baseUri, Encoding);

			// Register new Data Schemes from, the server
			ActiveDataSets.RegisterDataSetSchema("masterdata", "remote/wpf/masterdata.xml", typeof(PpsDataSetDefinitionDesktop));

			// Register Service
			mainResources[EnvironmentService] = this;

			InitBackgroundNotifier(out backgroundNotifier, out backgroundNotifierModeTransmission);
		} // ctor

		public async Task<PpsEnvironmentModeResult> InitAsync(IProgress<string> progress, bool bootOffline = false)
		{
			// initialize the local database
			var isLocalDbReady = await InitLocalStoreAsync(progress);
			if (!isLocalDbReady && bootOffline)
			{
				if (await MsgBoxAsync("Es steht keine Offline-Version bereit.\nOnline Synchronisation jeztz starten?", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
					return await InitAsync(progress, false);
				else
					return PpsEnvironmentModeResult.NeedsUpdate;
			}

			// check for online mode
		redoConnect:
			progress.Report("Verbinden...");
			var r = await WaitForEnvironmentMode(bootOffline ? PpsEnvironmentMode.Offline : PpsEnvironmentMode.Online);
			switch (r)
			{
				case PpsEnvironmentModeResult.NeedsUpdate:
					if (await MsgBoxAsync("Es steht eine neue Version zur Verfügung.\nUpdate durchführen?", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
					{
						if (await UpdateAsync(progress))
							goto redoConnect;
						else
							return PpsEnvironmentModeResult.NeedsUpdate;
					}
					else
					{
						await WaitForEnvironmentMode(PpsEnvironmentMode.Offline);
						bootOffline = true;
						goto redoConnect;
					}

				case PpsEnvironmentModeResult.NeedsSynchronization:
					if (await masterData.SynchronizationAsync(progress))
						goto redoConnect;
					else
						return PpsEnvironmentModeResult.NeedsSynchronization;
			}
			return r;
		} // func InitAsync

		// true => update successful
		// false => needs restart
		private Task<bool> UpdateAsync(IProgress<string> progress)
		{
			progress.Report("Lade Installationsdateien...");

			// start setup
		
			return Task.FromResult(true);
		} // proc UpdateAsync

		public void Dispose()
		{
			Dispose(true);
		} // proc Dispose

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				SetNewMode(PpsEnvironmentMode.Shutdown);

				services.ForEach(serv => (serv as IDisposable)?.Dispose());

				mainResources.Remove(EnvironmentService);

				inputManager.PreProcessInput -= preProcessInputEventHandler;

				// close handles
				Lua.Dispose();
				// dispose local store
				masterData?.Dispose();
			}
		} // proc Dispose

		#endregion

		public bool IsNetworkPresent
			=> true;
		
		#region -- Services ---------------------------------------------------------------

		public void RegisterService(string key, object service)
		{
			if (services.Exists(c => c.GetType() == service.GetType()))
				throw new InvalidOperationException("service");
			if (this.ContainsKey(key))
				throw new InvalidOperationException("key");

			// dynamic interface
			this[key] = service;

			// static interface
			services.Add(service);
		} // proc RegisterService

		public object GetService(Type serviceType)
		{
			foreach (var service in services)
			{
				var r = (service as IServiceProvider)?.GetService(serviceType);
				if (r != null)
					return r;
			}

			if (serviceType.IsAssignableFrom(GetType()))
				return this;

			return null;
		} // func GetService

		#endregion

		#region -- Background Notifier ----------------------------------------------------

		#region -- class ModeTransission --------------------------------------------------

		///////////////////////////////////////////////////////////////////////////////
		/// <summary></summary>
		private sealed class ModeTransission
		{
			private bool isCompleted = false;
			private readonly PpsEnvironmentMode desiredMode;
			private readonly TaskCompletionSource<PpsEnvironmentModeResult> waitForSuccess;

			public ModeTransission(PpsEnvironmentMode desiredMode)
			{
				if (desiredMode != PpsEnvironmentMode.Online && 
						desiredMode != PpsEnvironmentMode.Offline &&
						desiredMode != PpsEnvironmentMode.Shutdown)
					throw new ArgumentException($"{desiredMode} is not a valid mode transission.");

				this.desiredMode = desiredMode;
				this.waitForSuccess = new TaskCompletionSource<PPSn.PpsEnvironmentModeResult>();
			} // ctor

			public void SetResult(PpsEnvironmentModeResult result)
			{
				if (isCompleted)
					throw new InvalidOperationException();

				waitForSuccess.SetResult(result);
				isCompleted = true;
			} // proc SetResult

			public void SetException(Exception e)
			{
				if (!isCompleted)
				{
					waitForSuccess.SetException(e);
					isCompleted = true;
				}
			} // proc SetException

			public void Cancel()
			{
				if (!isCompleted)
				{
					waitForSuccess.TrySetCanceled();
					isCompleted = true;
				}
			} // proc Cancel

			public PpsEnvironmentMode DesiredMode => desiredMode;
			public Task<PpsEnvironmentModeResult> Task => waitForSuccess.Task;
		} // class StateTransission

		#endregion

		private PpsEnvironmentMode currentMode = PpsEnvironmentMode.None;
		private PpsEnvironmentState currentState = PpsEnvironmentState.None;

		private readonly object modeTransmissionLock = new object();
		private ModeTransission modeTransmission = null;

		private readonly Thread backgroundNotifier;
		private readonly ManualResetEventSlim backgroundNotifierModeTransmission;

		private void InitBackgroundNotifier(out Thread backgroundNotifier, out ManualResetEventSlim backgroundNotifierModeTransmission)
		{
			backgroundNotifierModeTransmission = new ManualResetEventSlim(false);
			backgroundNotifier = new Thread(ExecuteNotifierLoop)
			{
				IsBackground = true,
				Name = $"Environment Notify {environmentId}"
			};
			backgroundNotifier.Start();
		} // proc InitBackgroundNotifier

		private void SetNewMode(PpsEnvironmentMode newMode)
			=> WaitForEnvironmentMode(newMode).Wait();

		private Task<PpsEnvironmentModeResult> WaitForEnvironmentMode(PpsEnvironmentMode desiredMode)
		{
			// is this a new mode
			if (desiredMode == currentMode)
			{
				switch (desiredMode)
				{
					case PpsEnvironmentMode.Offline:
						return Task.FromResult(PpsEnvironmentModeResult.Offline);
					case PpsEnvironmentMode.Online:
						return Task.FromResult(PpsEnvironmentModeResult.Online);
					case PpsEnvironmentMode.Shutdown:
						return Task.FromResult(PpsEnvironmentModeResult.Shutdown);
					default:
						throw new InvalidOperationException();
				}
			}

			// start the mode switching
			lock (modeTransmissionLock)
			{
				var transmission = new ModeTransission(desiredMode);
				modeTransmission = transmission;
				backgroundNotifierModeTransmission.Set();
				return transmission.Task;
			}
		} // func WaitForEnvironmentState

		private bool TryGetModeTransmission(ref ModeTransission result)
		{
			lock (modeTransmissionLock)
			{
				if (modeTransmission == null)
					return false;
				else
				{
					if (result != null)
						result.Cancel();

					result = modeTransmission;
					backgroundNotifierModeTransmission.Reset();
					modeTransmission = null;
					return result != null;
				}
			}
		} // func TryGetModeTransmission
		
		private void ExecuteNotifierLoop()
		{
			var state = PpsEnvironmentState.None;
			ModeTransission currentTransmission = null;
			while (true)
			{
				try
				{
					// new mode requested
					if (TryGetModeTransmission(ref currentTransmission))
					{
						switch (currentTransmission.DesiredMode)
						{
							case PpsEnvironmentMode.Offline:
								state = PpsEnvironmentState.Offline;
								break;
							case PpsEnvironmentMode.Online:
								state = PpsEnvironmentState.OfflineConnect;
								break;
							case PpsEnvironmentMode.Shutdown:
								state = PpsEnvironmentState.Shutdown;
								break;
						}
					}

					// process current state
					UpdatePulicState(state);
					switch (state)
					{
						case PpsEnvironmentState.None: // nothing to do wait for a state
						case PpsEnvironmentState.Offline:
							if (currentTransmission != null)
							{
								currentTransmission.SetResult(PpsEnvironmentModeResult.Offline);
								currentTransmission = null;
							}
							backgroundNotifierModeTransmission.Wait();
							break;

						case PpsEnvironmentState.OfflineConnect:

							// load application info
							var xInfo = Request.GetXmlAsync("remote/info.xml", rootName: "ppsn").Result;
							info.Update(xInfo);
							if (info.IsModified)
								info.Save();

							// new version
							if (!info.IsApplicationLatest)
							{
								// application needs a update
								state = PpsEnvironmentState.None;
								SetTransmissionResult(ref currentTransmission, PpsEnvironmentModeResult.NeedsUpdate);
							}
							else
							{
								// try login for the user
								var xUser = Request.GetXmlAsync("remote/login.xml", rootName: "user").Result;

								userName = xUser.GetAttribute("displayName", userName);
								Dispatcher.BeginInvoke(new Action(() => OnPropertyChanged(nameof(UsernameDisplay))));

								// start synchronization
								if (!masterData.IsSynchronizationStarted || masterData.CheckSynchronizationStateAsync().Result)
									SetTransmissionResult(ref currentTransmission, PpsEnvironmentModeResult.NeedsSynchronization);
								else // mark the system online
									SetTransmissionResult(ref currentTransmission, PpsEnvironmentModeResult.Online);
							}
							state = PpsEnvironmentState.Online;
							break;

						case PpsEnvironmentState.Online:
							// fetch next state on ws-info
							break;

						case PpsEnvironmentState.Shutdown:
							return; // cancel all connections

						default:
							throw new InvalidOperationException("Unknown state.");
					}
				}
				catch (Exception e)
				{
					var ex = Procs.GetInnerException(e);
					if (currentTransmission != null)
					{
						var webEx = ex as WebException;

						switch (webEx?.Status ?? WebExceptionStatus.UnknownError)
						{
							case WebExceptionStatus.Timeout:
							case WebExceptionStatus.ConnectFailure:
								SetTransmissionResult(ref currentTransmission, PpsEnvironmentModeResult.ServerConnectFailure);
								break;
							case WebExceptionStatus.ProtocolError: // todo: detect Login failure
								SetTransmissionResult(ref currentTransmission, PpsEnvironmentModeResult.LoginFailed);
								break;
							default:
								currentTransmission.SetException(ex);
								currentTransmission = null;
								break;
						}
						state = PpsEnvironmentState.None;
					}
					else// todo: exception
					{
						Thread.Sleep(500);
						Debug.Print(ex.ToString());
					}
				}
			}
		} // proc ExecuteNotifierLoop

		private void SetTransmissionResult(ref ModeTransission currentTransmission, PpsEnvironmentModeResult result)
		{
			if (currentTransmission != null)
			{
				currentTransmission.SetResult(result);
				currentTransmission = null;
			}
		} // proc SetTransmissionResult

		private void UpdatePulicState(PpsEnvironmentState state)
		{
			if (currentState != state)
			{
				currentState = state;
				switch (state)
				{
					case PpsEnvironmentState.Offline:
					case PpsEnvironmentState.OfflineConnect:
						currentMode = PpsEnvironmentMode.Offline;
						break;
					case PpsEnvironmentState.Online:
						currentMode = PpsEnvironmentMode.Online;
						break;
					case PpsEnvironmentState.Shutdown:
						currentMode = PpsEnvironmentMode.Shutdown;
						break;
				}
				Dispatcher.BeginInvoke(new Action(
					() =>
					{
						OnPropertyChanged(nameof(CurrentMode));
						OnPropertyChanged(nameof(CurrentState));
					})
				);
			}
		} // proc UpdatePulicState

		#endregion

		#region -- Login/Logout -----------------------------------------------------------

		//protected virtual bool ShowLoginDialog(PpsClientLogin clientLogin)
		//	=> false;

		///// <summary>Gets called to request the user information.</summary>
		///// <param name="type">Authentification type</param>
		///// <param name="realm">Realm of the server.</param>
		///// <param name="count">Counts the login requests.</param>
		///// <returns>User information or <c>null</c> for cancel.</returns>
		//protected virtual ICredentials GetCredentials(ClientAuthentificationInformation authentificationInfo, int count)
		//{
		//	if (authentificationInfo.Type == ClientAuthentificationType.Ntlm && count == 0)
		//		return CredentialCache.DefaultCredentials;
		//	else
		//	{
		//		using (var loginCache = new PpsClientLogin("twppsn:" + info.Uri.AbsoluteUri, authentificationInfo.Realm, count > 1))
		//		{
		//			if (ShowLoginDialog(loginCache))
		//			{
		//				loginCache.Commit();
		//				return loginCache.GetCredentials();
		//			}
		//			else
		//				return null;
		//		}
		//	}
		//} // func GetCredentials

		//public async Task LoginUserAsync()
		//{
		//	var count = 0;
		//	var authentificationInfo = ClientAuthentificationInformation.Ntlm;
		//	try
		//	{
		//		XElement xLogin = null;

		//		while (xLogin == null)
		//		{
		//			// get the user information
		//			userInfo = GetCredentials(authentificationInfo, count++);
		//			if (userInfo == null)
		//			{
		//				ResetLogin();
		//				return;
		//			}

		//			try
		//			{
		//				// try to login with this user
		//				xLogin = await request.GetXmlAsync("remote/login.xml", MimeTypes.Text.Xml, "user");
		//			}
		//			catch (WebException e)
		//			{
		//				var tmp = ClientAuthentificationInformation.Get(e);
		//				if (tmp == null)
		//					throw;
		//				authentificationInfo = tmp;
		//			}
		//		} // while xLogin

		//		userName = xLogin.GetAttribute("displayName", userInfo.ToString());

		//		OnUsernameChanged();
		//		await RefreshAsync();
		//	}
		//	catch
		//	{
		//		ResetLogin();
		//		throw;
		//	}
		//} // proc LoginUser

		//public async Task LogoutUserAsync()
		//{
		//	await Task.Yield();
		//	ResetLogin();
		//} // proc LogoutUser

		//private void ResetLogin()
		//{
		//	userName = null;
		//	userInfo = null;
		//	OnUsernameChanged();
		//} // proc ResetLogin

		//protected virtual void OnUsernameChanged()
		//	=> UsernameChanged?.Invoke(this, EventArgs.Empty);

		///// <summary>Queues a request, to check if the server is available. After this the environment and the cache will be updated.</summary>
		///// <param name="timeout">wait timeout for the server</param>
		///// <returns></returns>
		//public async Task<bool> StartOnlineMode(CancellationToken token)
		//{
		//	XElement xInfo;
		//	try
		//	{
		//		xInfo = await request.GetXmlAsync("remote/info.xml", MimeTypes.Text.Xml, "ppsn");
		//	}
		//	catch (WebException ex)
		//	{
		//		if (ex.Status == WebExceptionStatus.ConnectFailure) // remote host does not respond
		//			return false;
		//		else
		//			throw;
		//	}

		//	// update the current info data
		//	info.Update(xInfo);

		//	if (!isOnline)
		//	{
		//		isOnline = true;
		//		OnIsOnlineChanged();
		//	}

		//	// refresh data
		//	//await RefreshAsync();

		//	return true;
		//} // func StartOnlineMode

		#endregion

		///// <summary>Loads basic data for the environment.</summary>
		///// <returns></returns>
		//public async virtual Task RefreshAsync()
		//{
		//	await Task.Yield();

		//	await RefreshOfflineCacheAsync();
		//	await RefreshDefaultResourcesAsync();
		//	await RefreshTemplatesAsync();


		//	await RefreshMasterDataSchemeAsync();
		//} // proc RefreshAsync

		/// <summary>Internal Id of the environment.</summary>
		public int EnvironmentId => environmentId;
		
		/// <summary>Current user the is logged in.</summary>
		public string Username => userName ?? String.Empty;
		/// <summary>Display name for the user.</summary>
		public string UsernameDisplay => userName;

		/// <summary>The current mode of the environment.</summary>
		public PpsEnvironmentMode CurrentMode => currentMode;
		/// <summary>The current state of the environment.</summary>
		public PpsEnvironmentState CurrentState => currentState;

		/// <summary>Data list items definitions</summary>
		public PpsEnvironmentCollection<PpsDataListItemDefinition> DataListItemTypes => templateDefinitions;
		/// <summary>Basic template selector for the item selector</summary>
		public PpsDataListTemplateSelector DataListTemplateSelector => dataListTemplateSelector;

		/// <summary>Dispatcher of the ui-thread.</summary>
		public Dispatcher Dispatcher => currentDispatcher;
		/// <summary>Synchronisation</summary>
		SynchronizationContext IPpsShell.Context => synchronizationContext;

		/// <summary>Access to the current collected informations.</summary>
		public PpsTraceLog Traces => logData;

		/// <summary>Path of the local data for the user.</summary>
		public DirectoryInfo LocalPath => localDirectory;

		LuaTable IPpsShell.LuaLibrary => this;

		// -- Static --------------------------------------------------------------

		private static object environmentCounterLock = new object();
		private static int environmentCounter = 1;

		/// <summary>Get the environment, that is attached to the current ui-element.</summary>
		/// <param name="ui"></param>
		/// <returns></returns>
		public static PpsEnvironment GetEnvironment(FrameworkElement ui)
			=> (PpsEnvironment)ui.FindResource(EnvironmentService);

		/// <summary>Get the Environment, that is attached to the current application.</summary>
		/// <returns></returns>
		public static PpsEnvironment GetEnvironment()
			=> (PpsEnvironment)Application.Current.FindResource(EnvironmentService);
	} // class PpsEnvironment

	#endregion

	#region -- class LuaEnvironmentTable ------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary>Connects the current table with the Environment</summary>
	public class LuaEnvironmentTable : LuaTable
	{
		private readonly PpsEnvironment environment;
		private readonly LuaEnvironmentTable parentTable;

		public LuaEnvironmentTable(LuaEnvironmentTable parentTable)
		{
			this.environment = parentTable.Environment;
			this.parentTable = parentTable;
		} // ctor

		public LuaEnvironmentTable(PpsEnvironment environment)
		{
			this.environment = environment;
			this.parentTable = null;
		} // ctor

		protected override object OnIndex(object key)
			=> base.OnIndex(key) ?? ((LuaTable)parentTable ?? environment).GetValue(key);

		/// <summary>Optional parent table.</summary>
		[LuaMember()]
		public LuaEnvironmentTable Parent => parentTable;
		/// <summary>Access to the current environemnt.</summary>
		[LuaMember()]
		public PpsEnvironment Environment => environment;
	} // class LuaEnvironmentTable

	#endregion
}
