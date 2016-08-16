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
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using Neo.IronLua;

namespace TecWare.PPSn.UI
{
	#region -- class VisibilityConverter ------------------------------------------------

	///////////////////////////////////////////////////////////////////////////////
	/// <summary></summary>
	public class VisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if ((bool)Lua.RtConvertValue(value, typeof(bool)))
				return TrueValue;
			else
				return FalseValue;
		} // func Convert

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			return (Visibility)value == TrueValue;
		} // func ConvertBack

		public Visibility TrueValue { get; set; } = Visibility.Visible;
		public Visibility FalseValue { get; set; } = Visibility.Hidden;
	} // class VisibilityConverter

	#endregion

	#region -- class PpsStringConverter -------------------------------------------------

	public sealed class PpsStringConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
			=> value == null ? String.Empty : String.Format((string)parameter ?? Text, value);

		object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		} // func ConvertBack

		public string Text { get; set; } = "{0}";
	} // class PpsStringConverter

	#endregion

	#region -- class MultiLineStringConverter -------------------------------------------

	public sealed class MultiLineStringConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
			=> value == null ? String.Empty : RemoveNewLines(value);

		object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		} // func ConvertBack

		private string RemoveNewLines(object value)
		{
			var txt = (string)value;
			if (!txt.Contains(Environment.NewLine))
				return txt;

			var lines = txt.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
			return string.Join(" ", lines);
		} // func RemoveNewLines
	} // class MultiLineStringConverter

	#endregion

}
