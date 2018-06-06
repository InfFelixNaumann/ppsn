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
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Xml.Linq;
using Neo.IronLua;
using TecWare.DE.Networking;
using TecWare.DE.Stuff;

namespace TecWare.PPSn
{
	#region -- class ThreadSafeMonitor ------------------------------------------------

	/// <summary>Build a monitor, that raises an exception, if the exit gets called in the wrong thread.</summary>
	public sealed class ThreadSafeMonitor : IDisposable
	{
		private readonly object threadLock;
		private readonly int threadId;

		private bool isDisposed = false;

		/// <summary>Enter lock</summary>
		/// <param name="threadLock"></param>
		public ThreadSafeMonitor(object threadLock)
		{
			this.threadLock = threadLock;
			this.threadId = Thread.CurrentThread.ManagedThreadId;

			Monitor.Enter(threadLock);
		} // ctor

		/// <summary></summary>
		~ThreadSafeMonitor()
		{
			Dispose(false);
		} // dtor

		/// <summary>Exit lock</summary>
		public void Dispose()
		{
			GC.SuppressFinalize(this);
			Dispose(true);
		} // proc Dispose

		private void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (isDisposed)
					throw new ObjectDisposedException(nameof(ThreadSafeMonitor));
				if (threadId != Thread.CurrentThread.ManagedThreadId)
					throw new ArgumentException();

				Monitor.Exit(threadLock);
			}
			else if (!isDisposed)
			{
				throw new ArgumentException();
			}
		} // proc Dispose
	} // class ThreadSafeMonitor

	#endregion

	#region -- class BooleanBox -------------------------------------------------------

	internal static class BooleanBox
	{
		public static object GetObject(bool value)
			=> value ? True : False;

		public static object GetObject(bool? value)
			=> value.HasValue ? GetObject(value.Value) : null;

		public static bool GetBool(object value)
			=> Object.Equals(value, True);

		public static bool? GetBoolNullable(object value)
			=> value == null ? (bool?)null : Object.Equals(value, True);

		public static object True { get; } = true;
		public static object False { get; } = false;
	} // class BooleanBox

	#endregion

	#region -- class LogicalContentEnumerator -----------------------------------------

	internal class LogicalContentEnumerator : IEnumerator
	{
	
		private int state = -1;
		private readonly IEnumerator baseItems; // base enumerator
		private readonly object content;
		private readonly Func<object> getContent;

		private LogicalContentEnumerator(IEnumerator baseItems, Func<object> getContent)
		{
			this.baseItems = baseItems;
			this.content = getContent();
			this.getContent = getContent;
		} // ctor

		private object GetContent()
		{
			if (content != getContent())
				throw new InvalidOperationException();
			return content;
		} // func GetContent

		public object Current
			=> state <= 0
				? GetContent()
				: baseItems?.Current;

		public bool MoveNext()
		{
			if (++state <= 0)
				return true;
			else if (state > 0)
				return baseItems?.MoveNext() ?? false;
			return false;
		} // func MoveNext

		public void Reset()
		{
			state = -1;
			baseItems?.Reset();
		} // proc Reset

		internal static IEnumerator GetLogicalEnumerator(DependencyObject d, IEnumerator logicalChildren, Func<object> getContent)
		{
			var content = getContent();
			if (content != null)
			{
				var templatedParent =
					d is FrameworkElement fe
						? fe.TemplatedParent
						: (d is FrameworkContentElement fce ? fce.TemplatedParent : null);

				if (templatedParent != null)
				{
					if (content is DependencyObject obj)
					{
						var p = LogicalTreeHelper.GetParent(obj);
						if (p != null && p != d)
							return logicalChildren;
					}
				}
				return new LogicalContentEnumerator(logicalChildren, getContent);
			}
			return logicalChildren;
		} // func GetLogicalEnumerator
	} // class LogicalElementEnumerator

	#endregion

	#region -- class StuffUI -----------------------------------------------------------

	/// <summary></summary>
	public static class StuffUI
	{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
		public static readonly XNamespace PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
		public static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
		public static readonly XName xnResourceDictionary = PresentationNamespace + "ResourceDictionary";
		public static readonly XName xnKey = XamlNamespace + "Key";
		public static readonly XName xnCode = XamlNamespace + "Code";
		public static readonly XName xnResources = PresentationNamespace + "resources";

		public static readonly XName xnTheme = "theme";
		public static readonly XName xnTemplates = "templates";
		public static readonly XName xnTemplate = "template";
		public static readonly XName xnCondition = "condition";
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

		/// <summary>Search for a Service on an Dependency-object. It will also lookup, all its 
		/// parents on the logical tree.</summary>
		/// <typeparam name="T">Type of the service.</typeparam>
		/// <param name="current">Current object in the logical tree.</param>
		/// <param name="throwException"><c>true</c>, to throw an not found exception.</param>
		/// <returns>The service of the default value.</returns>
		public static T GetControlService<T>(this DependencyObject current, bool throwException = false)
			=> (T)GetControlService(current, typeof(T), throwException);

		/// <summary>Search for a Service on an Dependency-object. It will also lookup, all its
		/// parents in the logical tree.</summary>
		/// <param name="current">Current object in the logical tree.</param>
		/// <param name="serviceType">Type of the service.</param>
		/// <param name="useVisualTree"></param>
		/// <returns>The service of the default value.</returns>
		public static object GetControlService(this DependencyObject current, Type serviceType, bool useVisualTree = false)
		{
			object r = null;

			if (current == null)
				return null;
			else if (current is IServiceProvider sp)
				r = sp.GetService(serviceType);
			else if (serviceType.IsAssignableFrom(current.GetType()))
				r = current;

			if (r != null)
				return r;

			return GetControlService(
				useVisualTree 
					? GetVisualParent(current) 
					: GetLogicalParent(current), serviceType, useVisualTree
			);
		} // func GetControlService

		/// <summary></summary>
		/// <param name="current"></param>
		/// <param name="name"></param>
		/// <param name="comparison"></param>
		/// <returns></returns>
		public static int CompareName(this DependencyObject current, string name, StringComparison comparison = StringComparison.Ordinal)
		{
			switch(current)
			{
				case FrameworkElement fe:
					return String.Compare(fe.Name, name, comparison);
				case FrameworkContentElement fce:
					return String.Compare(fce.Name, name, comparison);
				default:
					return -1;
			}
		} // func CompareName

		/// <summary>Get the logical parent or the template parent.</summary>
		/// <param name="current"></param>
		/// <returns></returns>
		public static DependencyObject GetLogicalParent(this DependencyObject current)
		{
			switch (current)
			{
				case FrameworkContentElement fce:
					return fce.Parent ?? fce.TemplatedParent;
				case FrameworkElement fe:
					return fe.Parent ?? fe.TemplatedParent;
				default:
					return null;
			}
		} // func GetLogicalParent

		/// <summary></summary>
		/// <param name="current"></param>
		/// <param name="typeOfParent"></param>
		/// <returns></returns>
		public static DependencyObject GetLogicalParent(this DependencyObject current, Type typeOfParent)
		{
			var parent = GetLogicalParent(current);
			return parent == null || typeOfParent == null || typeOfParent.IsAssignableFrom(parent.GetType())
				? parent
				: GetLogicalParent(parent, typeOfParent);
		} // func GetVisualParent

		/// <summary></summary>
		/// <param name="current"></param>
		/// <param name="name"></param>
		/// <returns></returns>
		public static DependencyObject GetLogicalParent(this DependencyObject current, string name)
		{
			var parent = GetLogicalParent(current);
			return parent == null || CompareName(parent, name) == 0
				? parent
				: GetLogicalParent(parent, name);
		} // func GetVisualParent


		/// <summary>Get the logical parent or the template parent.</summary>
		/// <param name="current"></param>
		/// <returns></returns>
		public static T GetLogicalParent<T>(this DependencyObject current)
			where T : DependencyObject
		{
			var parent = GetLogicalParent(current);
			return parent is T r
				? r
				: GetLogicalParent<T>(parent);
		} // func GetLogicalParent

		/// <summary></summary>
		/// <param name="current"></param>
		/// <returns></returns>
		public static DependencyObject GetVisualParent(this DependencyObject current)
			=> current is Visual || current is Visual3D ? VisualTreeHelper.GetParent(current) : null;

		/// <summary></summary>
		/// <param name="current"></param>
		/// <param name="typeOfParent"></param>
		/// <returns></returns>
		public static DependencyObject GetVisualParent(this DependencyObject current, Type typeOfParent)
		{
			var parent = GetVisualParent(current);
			if (parent == null && current != null && current.GetType().Name == "PopupRoot")
				parent = GetLogicalParent(current);

			return parent == null || typeOfParent == null || typeOfParent.IsAssignableFrom(parent.GetType())
				? parent
				: GetVisualParent(parent, typeOfParent);
		} // func GetVisualParent

		/// <summary></summary>
		/// <param name="current"></param>
		/// <param name="name"></param>
		/// <returns></returns>
		public static DependencyObject GetVisualParent(this DependencyObject current, string name)
		{
			var parent = GetVisualParent(current);
			return parent == null || CompareName(parent, name) == 0
				? parent
				: GetVisualParent(parent, name);
		} // func GetVisualParent

		/// <summary></summary>
		/// <param name="current"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public static T GetVisualParent<T>(this DependencyObject current)
			where T : DependencyObject
		{
			var parent = GetVisualParent(current);
			return parent is T r
				? r
				: GetVisualParent<T>(parent);
		} // func GetVisualParent

		/// <summary>Find a child in the Visual tree.</summary>
		/// <typeparam name="T">Type of the child</typeparam>
		/// <param name="current">Current visual element.</param>
		/// <returns>Child or <c>null</c>.</returns>
		public static T GetVisualChild<T>(this DependencyObject current)
			where T : DependencyObject
		{
			var c = VisualTreeHelper.GetChildrenCount(current);
			for (var i = 0; i < c; i++)
			{
				var v = VisualTreeHelper.GetChild(current, i);
				if (v is T child)
					return child;
				else
				{
					child = GetVisualChild<T>(v);
					if (child != null)
						return child;
				}
			}
			return default(T);
		} // func GetVisualChild

		/// <summary></summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="value"></param>
		/// <returns></returns>
		public static T ChangeTypeWithConverter<T>(this object value)
			=> (T)ChangeTypeWithConverter(value, typeof(T));

		/// <summary></summary>
		/// <param name="value"></param>
		/// <param name="typeTo"></param>
		/// <returns></returns>
		public static object ChangeTypeWithConverter(this object value, Type typeTo)
		{
			if (value == null)
				return Procs.ChangeType(null, typeTo);
			else if (typeTo.IsAssignableFrom( value.GetType() ))
				return value;
			else
			{
				var convTo = TypeDescriptor.GetConverter(value.GetType());
				if (convTo.CanConvertTo(typeTo))
					return convTo.ConvertTo(null, CultureInfo.InvariantCulture, value, typeTo);
				else
				{
					var convFrom = TypeDescriptor.GetConverter(typeTo);
					if (convFrom.CanConvertFrom(value.GetType()))
						return convFrom.ConvertFrom(null, CultureInfo.InvariantCulture, value);
					else
						return Procs.ChangeType(value, typeTo);
				}
			}
		} // func ChangeTypeWithConverter

		#region -- remove after update DES --
		
		#endregion
	} // class StuffUI

	#endregion

	#region -- class StuffDB ------------------------------------------------------------

	/// <summary>Db-Extensions</summary>
	public static class StuffDB
	{
		/// <summary>Key for extended exception attribute.</summary>
		public const string CommandTextKey = "CommandText";

		/// <summary>Add parameter to an DbCommand</summary>
		/// <param name="command"></param>
		/// <param name="parameterName"></param>
		/// <returns></returns>
		public static DbParameter AddParameter(this DbCommand command, string parameterName)
		{
			var param = command.CreateParameter();
			param.ParameterName = parameterName;
			command.Parameters.Add(param);
			return param;
		} // func AddParameter

		/// <summary>Add parameter to an DbCommand</summary>
		/// <param name="command"></param>
		/// <param name="parameterName"></param>
		/// <param name="dbType"></param>
		/// <param name="value"></param>
		/// <returns></returns>
		public static DbParameter AddParameter(this DbCommand command, string parameterName, DbType dbType, object value = null)
		{
			var param = AddParameter(command, parameterName);
			param.DbType = dbType;
			param.Value = value;
			return param;
		} // func AddParameter

		/// <summary>Execute with CommandText-Attribute.</summary>
		/// <param name="command"></param>
		/// <returns></returns>
		public static int ExecuteNonQueryEx(this DbCommand command)
		{
			try
			{
				return command.ExecuteNonQuery();
			}
			catch (DbException e)
			{
				e.UpdateExceptionWithCommandInfo(command);
				throw;
			}
		} // func ExecuteReaderEx

		/// <summary>Execute with CommandText-Attribute.</summary>
		/// <param name="command"></param>
		/// <returns></returns>
		public static async Task<int> ExecuteNonQueryExAsync(this DbCommand command)
		{
			try
			{
				return await command.ExecuteNonQueryAsync();
			}
			catch (DbException e)
			{
				e.UpdateExceptionWithCommandInfo(command);
				throw;
			}
		} // func ExecuteReaderEx

		/// <summary>Execute with CommandText-Attribute.</summary>
		/// <param name="command"></param>
		/// <param name="commandBehavior"></param>
		/// <returns></returns>
		public static DbDataReader ExecuteReaderEx(this DbCommand command, CommandBehavior commandBehavior = CommandBehavior.Default)
		{
			try
			{
				return command.ExecuteReader(commandBehavior);
			}
			catch (DbException e)
			{
				e.UpdateExceptionWithCommandInfo(command);
				throw;
			}
		} // func ExecuteReaderEx

		/// <summary>Execute with CommandText-Attribute.</summary>
		/// <param name="command"></param>
		/// <param name="commandBehavior"></param>
		/// <returns></returns>
		public static async Task<DbDataReader> ExecuteReaderExAsync(this DbCommand command, CommandBehavior commandBehavior = CommandBehavior.Default)
		{
			try
			{
				return await command.ExecuteReaderAsync(commandBehavior);
			}
			catch (DbException e)
			{
				e.UpdateExceptionWithCommandInfo(command);
				throw;
			}
		} // func ExecuteReaderEx

		/// <summary>Execute with CommandText-Attribute.</summary>
		/// <param name="command"></param>
		/// <returns></returns>
		public static object ExecuteScalarEx(this DbCommand command)
		{
			try
			{
				return command.ExecuteScalar();
			}
			catch (DbException e)
			{
				e.UpdateExceptionWithCommandInfo(command);
				throw;
			}
		} // func ExecuteScalarEx

		/// <summary>Execute with CommandText-Attribute.</summary>
		/// <param name="command"></param>
		/// <returns></returns>
		public static async Task<object> ExecuteScalarExAsync(this DbCommand command)
		{
			try
			{
				return await command.ExecuteScalarAsync();
			}
			catch (DbException e)
			{
				e.UpdateExceptionWithCommandInfo(command);
				throw;
			}
		} // func ExecuteScalarEx

		/// <summary>Add CommandText-Attribute to the exception.</summary>
		/// <param name="e"></param>
		/// <param name="command"></param>
		/// <returns></returns>
		public static void UpdateExceptionWithCommandInfo(this Exception e, DbCommand command)
		{
			var ret = command.CommandText;
			foreach (var parameter in command.Parameters.Cast<DbParameter>())
				ret = ret.Replace(parameter.ParameterName, parameter.Value.ToString());
			e.Data[CommandTextKey] = ret;
		} // proc UpdateExceptionWithCommandInfo

		/// <summary></summary>
		/// <param name="value"></param>
		/// <returns></returns>
		public static bool DbNullOnNeg(long value)
			=> value < 0;

		/// <summary></summary>
		/// <param name="value"></param>
		/// <returns></returns>
		public static object DbNullIfString(this string value)
			=> String.IsNullOrEmpty(value) ? (object)DBNull.Value : value;

		/// <summary></summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="value"></param>
		/// <param name="null"></param>
		/// <returns></returns>
		public static object DbNullIf<T>(this T value, T @null)
			=> Object.Equals(value, @null) ? (object)DBNull.Value : value;

		/// <summary></summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="value"></param>
		/// <param name="null"></param>
		/// <returns></returns>
		public static object DbNullIf<T>(this T value, Func<T, bool> @null)
			=> @null(value) ? (object)DBNull.Value : value;

		/// <summary></summary>
		/// <param name="r"></param>
		/// <param name="columnName"></param>
		/// <param name="throwException"></param>
		/// <returns></returns>
		public static int FindColumnIndex(this IDataRecord r, string columnName, bool throwException = true)
		{
			for(var i = 0;i< r.FieldCount;i++)
			{
				if (String.Compare(r.GetName(i), columnName, StringComparison.OrdinalIgnoreCase) == 0)
					return i;
			}
			if (throwException)
				throw new ArgumentOutOfRangeException(nameof(columnName), columnName, $"Column '{columnName}' not found.");
			return -1;
		} // func FindColumnIndex

		/// <summary></summary>
		/// <param name="r"></param>
		/// <param name="throwException"></param>
		/// <param name="columnNames"></param>
		/// <returns></returns>
		public static int[] FindColumnIndices(this IDataRecord r, bool throwException, params string[] columnNames)
		{
			// init result
			var idx = new int[columnNames.Length];
			for (var i = 0; i < idx.Length; i++)
				idx[i] = -1;

			// match columns
			for (var i = 0; i < r.FieldCount; i++)
			{
				var n = r.GetName(i);
				var j = Array.FindIndex(columnNames, c => String.Compare(n, c, StringComparison.OrdinalIgnoreCase) == 0);
				if(j != -1)
					idx[j] = i;
			}

			// return values
			for (var i = 0; i < idx.Length; i++)
			{
				if (idx[i] == -1)
					throw new ArgumentOutOfRangeException(nameof(columnNames), columnNames[i], $"Column '{columnNames[i]}' not found.");
			}

			return idx;
		} // func FindColumnIndices
	} // class StuffDB

	#endregion

	#region -- class StuffIO ------------------------------------------------------------

	/// <summary></summary>
	public static class StuffIO
	{
		/// <summary>We only use sha256, the prefix should be useful, if the algorithm will be changed in future.</summary>
		/// <returns></returns>
		public static string GetHashPrefix(HashAlgorithm algorithm)
		{
			if (algorithm is SHA256
				|| algorithm is SHA256Managed)
				return "sha256";
			else
				throw new ArgumentOutOfRangeException(nameof(algorithm), "Only sha256 is allowed.");
		} // func GetHashPrefix
		
		/// <summary>Build hash information from hash.</summary>
		/// <param name="hash"></param>
		/// <param name="algorithm"></param>
		/// <returns></returns>
		public static string ConvertHashToString(HashAlgorithm algorithm, byte[] hash)
		{
			if(hash == null ||hash.Length == 0)
				return null;

			var sb = new StringBuilder(GetHashPrefix(algorithm)).Append(':');
			for (var i = 0; i < hash.Length; i++)
				sb.Append(hash[i].ToString("X2"));
			return sb.ToString();
		} // func ConvertHashToString

		/// <summary>Convert a hash string to the algorithm and hash value.</summary>
		/// <param name="hashString"></param>
		/// <param name="algorithm"></param>
		/// <param name="hash"></param>
		/// <returns></returns>
		public static bool TryConvertStringToHash(string hashString, out HashAlgorithm algorithm, out byte[] hash)
		{
			throw new NotImplementedException();
		} // func TryConvertStringToHash

		#region ---- MimeTypes ----------------------------------------------------------

		// ToDo: may need translation
		private static (string Extension, string MimeType, string FriendlyName)[] mimeTypeMapping =
		{
			( "bmp", MimeTypes.Image.Bmp, "Bilddatei" ),
			( "css", MimeTypes.Text.Css, "Textdatei" ),
			( "dat", MimeTypes.Application.OctetStream, "Binärdatei" ),
			( "gif", MimeTypes.Image.Gif, "Bilddatei" ),
			( "htm", MimeTypes.Text.Html, "Textdatei" ),
			( "html", MimeTypes.Text.Html, "Textdatei" ),
			( "ico", MimeTypes.Image.Icon, "Bilddatei" ),
			( "js", MimeTypes.Text.JavaScript, "Textdatei" ),
			( "jpeg", MimeTypes.Image.Jpeg, "Bilddatei" ),
			( "jpg", MimeTypes.Image.Jpeg, "Bilddatei" ),
			( "json", MimeTypes.Text.Json, "Textdatei" ),
			( "log", MimeTypes.Text.Plain, "Textdatei" ),
			( "lua", MimeTypes.Text.Lua, "Textdatei" ),
			( "png", MimeTypes.Image.Png, "Bilddatei" ),
			( "txt", MimeTypes.Text.Plain, "Textdatei" ),
			( "xaml", MimeTypes.Application.Xaml, "Textdatei" ),
			( "xml", MimeTypes.Text.Xml, "Textdatei" ),
			( "ifc", "text/ifc", "ifc-Datei" ),
		};

		private static int FindTypeMappingByExtension(string extension)
		{
			if (String.IsNullOrEmpty(extension))
				return -1;

			if (extension[0] == '.')
				extension = extension.Substring(1);
			
			return Array.FindIndex(mimeTypeMapping, mt => String.Compare(mt.Extension, extension, StringComparison.OrdinalIgnoreCase) == 0);
		} // func FindTypeMappingByExtension

		private static int FindTypeMappingByMimeType(string mimeType)
			=> Array.FindIndex(mimeTypeMapping, mt => mt.MimeType == mimeType);

		/// <summary>Returns the correct mime-type from an file extension.</summary>
		/// <param name="extension"></param>
		/// <returns>MimeType or the <c>DefaultMimeType</c></returns>
		public static string MimeTypeFromExtension(string extension)
		{
			var typeIndex = FindTypeMappingByExtension(extension);
			return typeIndex >= 0
				? mimeTypeMapping[typeIndex].MimeType 
				: DefaultMimeType;
		} // func MimeTypeFromExtension

		/// <summary>Returns the correct mime-type from an file.</summary>
		/// <param name="filename"></param>
		/// <returns>MimeType or the <c>DefaultMimeType</c></returns>
		public static string MimeTypeFromFilename(string filename)
			=> MimeTypeFromExtension(Path.GetExtension(filename));

		/// <summary>Returns a extension for the givven mime-type.</summary>
		/// <param name="mimeType"></param>
		/// <returns></returns>
		public static string ExtensionFromMimeType(string mimeType)
		{
			var idx = FindTypeMappingByMimeType(mimeType); // first index
			return idx >= 0
				? "." + mimeTypeMapping[idx].Extension
				: ".dat";
		} // func ExtensionFromMimeType

		/// <summary>Generates the filter string for FileDialogs</summary>
		/// <param name="mimeType">Mimetypes to include - can also be just the starts p.e. 'image'</param>
		/// <param name="excludeMimeType">Mimetypes to exclude</param>
		/// <returns>a string for the filter</returns>
		public static string FilterFromMimeType(string[] mimeType, string[] excludeMimeType = null)
		{
			var names = new List<string>();
			var extensions = new List<string>();

			foreach (var mt in mimeTypeMapping)
			{
				foreach (var m in mimeType)
				{
					if ((excludeMimeType != null ? Array.IndexOf(excludeMimeType, mt.MimeType) == -1 : true) && mt.MimeType.StartsWith(m))
					{
						if (!names.Exists(i => i == mt.FriendlyName))
							names.Add(mt.FriendlyName);
						if (!extensions.Exists(i => i == "*." + mt.Extension))
							extensions.Add("*." + mt.Extension);
					}
				}
			}

			names.Sort((a, b) => a.CompareTo(b));
			extensions.Sort((a, b) => a.CompareTo(b));

			return String.Join("/", names) + '|' + String.Join(";", extensions);
		} // func FilterFromMimeType

		/// <summary>Returns always octetstream</summary>
		public static string DefaultMimeType => MimeTypes.Application.OctetStream;

		#endregion
	} // class StuffIO

	#endregion

	#region -- class WebRequestHelper ---------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public static class WebRequestHelper
	{
		public static ContentDisposition GetContentDisposition(this WebResponse r, bool createDummy = true)
		{
			//var tmp = r.Headers["Content-Disposition"];

			//if (tmp == null)
			//{
			if (createDummy)
			{
				var cd = new ContentDisposition();

				// try to get a filename
				var path = r.ResponseUri.AbsolutePath;
				var pos = -1;
				if (!String.IsNullOrEmpty(path))
					pos = path.LastIndexOf('/', path.Length - 1);
				if (pos >= 0)
					cd.FileName = path.Substring(pos + 1);
				else
					cd.FileName = path;

				// set the date
				cd.ModificationDate = GetLastModified(r);
				return cd;
			}
			else
				return null;
			//}
			//else
			//	return new ContentDisposition(tmp);
		} // func GetContentDisposition

		public static DateTime GetLastModified(this WebHeaderCollection headers)
			=> DateTime.TryParse(headers[HttpResponseHeader.LastModified], out var lastModified) ? lastModified : DateTime.Now; // todo: format?

		public static DateTime GetLastModified(this WebResponse r)
			=> GetLastModified(r.Headers);

		public static ContentType GetContentType(this WebResponse r)
			=> new ContentType(r.ContentType);

		public static NameValueCollection ParseQuery(this Uri uri)
			=> uri.IsAbsoluteUri
				? HttpUtility.ParseQueryString(uri.Query)
				: ParseQuery(uri.OriginalString);

		public static NameValueCollection ParseQuery(string uri)
		{
			var pos = uri.IndexOf('?');
			return pos == -1
				? emptyCollection
				: HttpUtility.ParseQueryString(uri.Substring(pos + 1));
		} // func ParseQuery

		public static string ParsePath(this Uri uri)
			=> uri.IsAbsoluteUri
				? uri.AbsolutePath
				: ParsePath(uri.OriginalString);

		public static string ParsePath(string uri)
		{
			var pos = uri.IndexOf('?');
			return pos == -1 ? uri : uri.Substring(0, pos);
		} // func ParsePath

		public static (string path, NameValueCollection arguments) ParseUri(this Uri uri)
			=> uri.IsAbsoluteUri
				? (uri.AbsolutePath, HttpUtility.ParseQueryString(uri.Query))
				: ParseUri(uri.OriginalString);

		public static (string path, NameValueCollection arguments) ParseUri(string uri)
		{
			var pos = uri.IndexOf('?');
			return pos == -1
				? (uri, emptyCollection)
				: (uri.Substring(0, pos), HttpUtility.ParseQueryString(uri.Substring(pos + 1)));
		} // func ParseUri

		public static bool EqualUri(Uri uri1, Uri uri2)
		{
			if (uri1.IsAbsoluteUri && uri2.IsAbsoluteUri)
				return uri1.Equals(uri2);
			else if (uri1.IsAbsoluteUri || uri2.IsAbsoluteUri)
				return false;
			else
			{
				(var path1, var args1) = uri1.ParseUri();
				(var path2, var args2) = uri2.ParseUri();

				if (path1 == path2 && args1.Count == args2.Count)
				{
					foreach (var k in args1.AllKeys)
					{
						if (args1[k] != args2[k])
							return false;
					}
					return true;
				}
				else
					return false;
			}
		} // func EqualUri

		private static NameValueCollection emptyCollection = new NameValueCollection();
	} // class WebRequestHelper

	#endregion
}
