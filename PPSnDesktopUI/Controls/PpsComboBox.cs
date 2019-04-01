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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using TecWare.PPSn.Data;

namespace TecWare.PPSn.Controls
{
	#region -- class PpsComboBoxItem --------------------------------------------------

	/// <summary>Item container for the data list.</summary>
	public class PpsComboBoxItem : ComboBoxItem
	{
		/// <summary></summary>
		/// <param name="e"></param>
		protected override void OnMouseEnter(MouseEventArgs e)
		{
			if (ParentComboBox != null && ParentComboBox.IsFilterable)
			{
				var focusAble = Focusable;
				try
				{
					// OnMouseEnter will set focus by default, mark the container as not focusable
					Focusable = false;
					base.OnMouseEnter(e);
				}
				finally
				{
					Focusable = focusAble;
				}
			}
			else
				base.OnMouseEnter(e);
		} // proc OnMouseEnter

		internal PpsComboBox ParentComboBox => ItemsControl.ItemsControlFromItemContainer(this) as PpsComboBox;

		static PpsComboBoxItem()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(PpsComboBoxItem), new FrameworkPropertyMetadata(typeof(PpsComboBoxItem)));
		} // sctor

	} // class PpsComboBoxItem

	#endregion

	#region -- class PpsComboBox ------------------------------------------------------

	/// <summary></summary>
	[TemplatePart(Name = FilterBoxTemplateName, Type = typeof(PpsTextBox))]
	public class PpsComboBox : ComboBox, IPpsNullableControl
	{
		/// <summary>Template name for the filter box</summary>
		public const string FilterBoxTemplateName = "PART_FilterBox";

		#region -- UserFilterText - Property ------------------------------------------

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
		public static readonly DependencyProperty UserFilterTextProperty = DependencyProperty.Register(nameof(UserFilterText), typeof(string), typeof(PpsComboBox), new FrameworkPropertyMetadata(null, new PropertyChangedCallback(OnUserFilterTextChanged)));

		private static readonly DependencyPropertyKey isFilteredPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsFiltered), typeof(bool), typeof(PpsComboBox), new FrameworkPropertyMetadata(BooleanBox.False));
		public static readonly DependencyProperty IsFilteredProperty = isFilteredPropertyKey.DependencyProperty;
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

		private static void OnUserFilterTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
			=> ((PpsComboBox)d).UpdateFilterExpression();

		private void UpdateFilterExpression()
		{
			if (filterView != null && AllowFilter)
			{
				var expr = PpsDataFilterExpression.Parse(UserFilterText);
				filterView.FilterExpression = expr;
				SetValue(isFilteredPropertyKey, expr == PpsDataFilterExpression.True);
			}
			else
			{
				SetValue(isFilteredPropertyKey, false);
				if (filterView != null)
					filterView.FilterExpression = PpsDataFilterExpression.True;
			}
		} // proc UpdateFilterExpression

		/// <summary>Filter the collection view, this property is used by the template. Type is string because we do not want to change the source e.g. spaces. </summary>
		public string UserFilterText { get => (string)GetValue(UserFilterTextProperty); set => SetValue(UserFilterTextProperty, value); }
		/// <summary>Is the view filterd, currently.</summary>
		public bool IsFiltered => BooleanBox.GetBool(GetValue(IsFilteredProperty));

		#endregion

		#region -- AllowFilter, IsFilterable - Property -------------------------------

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
		public static readonly DependencyProperty AllowFilterProperty = PpsDataListBox.AllowFilterProperty.AddOwner(typeof(PpsComboBox), new FrameworkPropertyMetadata(BooleanBox.True, new PropertyChangedCallback(OnAllowFilterChanged)));

		private static readonly DependencyPropertyKey isFilterablePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsFilterable), typeof(bool), typeof(PpsComboBox), new FrameworkPropertyMetadata(BooleanBox.True));
		public static readonly DependencyProperty IsFilterableProperty = isFilterablePropertyKey.DependencyProperty;
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

		private void UpdateFilterable()
			=> SetValue(isFilterablePropertyKey, BooleanBox.GetObject(filterBox != null && filterView != null && AllowFilter));

		private static void OnAllowFilterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
			=> ((PpsComboBox)d).UpdateFilterable();

		/// <summary>Allow filter</summary>
		public bool AllowFilter { get => BooleanBox.GetBool(GetValue(AllowFilterProperty)); set => SetValue(AllowFilterProperty, BooleanBox.GetObject(value)); }

		/// <summary>Is filterable</summary>
		public bool IsFilterable => BooleanBox.GetBool(GetValue(IsFilterableProperty));

		#endregion

		#region -- IsNullable - property ----------------------------------------------

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
		public static readonly DependencyProperty IsNullableProperty = PpsTextBox.IsNullableProperty.AddOwner(typeof(PpsComboBox));
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

		/// <summary>Is this value nullable.</summary>
		public bool IsNullable { get => BooleanBox.GetBool(GetValue(IsNullableProperty)); set => SetValue(IsNullableProperty, BooleanBox.GetObject(value)); }

		#endregion

		#region -- HintLabel - property -----------------------------------------------

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
		public static readonly DependencyProperty HintLabelTextProperty = DependencyProperty.Register(nameof(HintLabelText), typeof(string), typeof(PpsComboBox), new FrameworkPropertyMetadata(null, new PropertyChangedCallback(OnHintLabelTextChanged)));
		private static readonly DependencyPropertyKey hasHintLabelPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasHintLabel), typeof(bool), typeof(PpsComboBox), new FrameworkPropertyMetadata(BooleanBox.False));
		public static readonly DependencyProperty HasHintLabelProperty = hasHintLabelPropertyKey.DependencyProperty;
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

		private static void OnHintLabelTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var comboBox = (PpsComboBox)d;
			if (e.NewValue is string label && !String.IsNullOrEmpty(label))
				comboBox.HasHintLabel = true;
			else
				comboBox.HasHintLabel = false;
		} // proc OnHintLabelTextChanged

		/// <summary>Optional text to show, when no value is selected</summary>
		public string HintLabelText { get => (string)(GetValue(HintLabelTextProperty)); set => SetValue(HintLabelTextProperty, value); }
		/// <summary>Has the Combobox a HintLabel?</summary>
		public bool HasHintLabel { get => BooleanBox.GetBool(GetValue(HasHintLabelProperty)); private set => SetValue(hasHintLabelPropertyKey, BooleanBox.GetObject(value)); }

		#endregion

		#region -- IsClearButtonVisible - property ------------------------------------

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
		public static readonly DependencyProperty IsClearButtonVisibleProperty = DependencyProperty.Register(nameof(IsClearButtonVisible), typeof(bool), typeof(PpsComboBox), new FrameworkPropertyMetadata(BooleanBox.False));
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

		/// <summary>Is the ClearButton visible?</summary>
		public bool IsClearButtonVisible { get => BooleanBox.GetBool(GetValue(IsClearButtonVisibleProperty)); set => SetValue(IsClearButtonVisibleProperty, BooleanBox.GetObject(value)); }

		#endregion

		#region -- GeometryName - property --------------------------------------------

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
		public static readonly DependencyProperty GeometryNameProperty = PpsGeometryImage.GeometryNameProperty.AddOwner(typeof(PpsComboBox));
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

		/// <summary>The property defines the resource to be loaded to show the dropdown-symbol.</summary>
		public string GeometryName { get => (string)GetValue(GeometryNameProperty); set => SetValue(GeometryNameProperty, value); }

		#endregion

		private IPpsDataRowViewFilter filterView = null;
		private PpsTextBox filterBox;

		bool IPpsNullableControl.CanClear => IsEnabled && IsNullable && SelectedIndex >= 0;

		/// <summary>Clear index</summary>
		public void Clear()
		{
			SelectedIndex = -1;
		} // proc ClearValue

		/// <summary></summary>
		/// <param name="oldValue"></param>
		/// <param name="newValue"></param>
		protected override void OnItemsSourceChanged(IEnumerable oldValue, IEnumerable newValue)
		{
			base.OnItemsSourceChanged(oldValue, newValue);

			// get view
			var view = CollectionViewSource.GetDefaultView(newValue);
			filterView = view as IPpsDataRowViewFilter;

			UpdateFilterExpression();
			UpdateFilterable();
		} // proc OnItemsSourceChanged

		/// <summary></summary>
		public override void OnApplyTemplate()
		{
			base.OnApplyTemplate();

			filterBox = (PpsTextBox)GetTemplateChild(FilterBoxTemplateName);
			if (filterBox != null)
			{
				filterBox.PreviewKeyDown += FilterBox_PreviewKeyDown;
			}

			UpdateFilterable();
		} // proc OnApplyTemplate

		private void FilterBox_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			if (filterBox.IsKeyboardFocusWithin)
			{
				switch (e.Key)
				{
					case Key.Up:
					case Key.Down:
						OnKeyDown(e); // will change focus
						filterBox.Focus(); // enforce focus
						break;
					case Key.Return:
					case Key.Escape:
						OnKeyDown(e);
						break;
				}
			}
		} // event FilterBox_PreviewKeyDown

		/// <summary>leave clean</summary>
		/// <param name="e"></param>
		protected override void OnDropDownClosed(EventArgs e)
		{
			base.OnDropDownClosed(e);
			UserFilterText = String.Empty;
		} // proc OnDropDownClosed

		/// <summary></summary>
		/// <param name="e"></param>
		protected override void OnDropDownOpened(EventArgs e)
		{
			base.OnDropDownOpened(e);
			if (IsFilterable)
			{
				Focus();
				Dispatcher.BeginInvoke(DispatcherPriority.Send, new Func<bool>(filterBox.Focus));
			}
		} // proc OnDropDownOpened

		/// <summary></summary>
		/// <param name="e"></param>
		protected override void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e)
		{
			base.OnIsKeyboardFocusWithinChanged(e);
			if (IsDropDownOpen && IsFilterable)
				filterBox.Focus();
		} // proc OnIsKeyboardFocusWithinChanged

		#region -- Item Container -----------------------------------------------------

		/// <summary></summary>
		/// <returns></returns>
		protected override DependencyObject GetContainerForItemOverride()
			=> new PpsComboBoxItem();

		/// <summary></summary>
		/// <param name="item"></param>
		/// <returns></returns>
		protected override bool IsItemItsOwnContainerOverride(object item)
			=> item is PpsComboBoxItem;

		/// <summary></summary>
		/// <param name="element"></param>
		/// <param name="item"></param>
		protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
		{
			base.PrepareContainerForItemOverride(element, item);

			if (element is PpsComboBoxItem container)
			{
				if (container.ContentTemplate == null
					&& container.ContentTemplateSelector == null
					&& container.ContentStringFormat == null)
				{
					container.ContentTemplateSelector = PpsShellWpf.GetShell(this).DefaultDataTemplateSelector;
				}
			}
		} // proc PrepareContainerForItemOverride

		#endregion

		static PpsComboBox()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(PpsComboBox), new FrameworkPropertyMetadata(typeof(PpsComboBox)));
			PpsControlCommands.RegisterClearCommand(typeof(PpsComboBox));
		}
	} // class PpsComboBox

	#endregion

	#region -- class PpsComboBoxToggleButton ------------------------------------------

	/// <summary></summary>
	public class PpsComboBoxToggleButton : ToggleButton
	{
		/// <summary>The name of the resource</summary>
		public static readonly DependencyProperty GeometryNameProperty = PpsGeometryImage.GeometryNameProperty.AddOwner(typeof(PpsComboBoxToggleButton));

		/// <summary>The property defines the resource to be loaded to show the dropdown-symbol.</summary>
		public string GeometryName { get => (string)GetValue(GeometryNameProperty); set => SetValue(GeometryNameProperty, value); }

		static PpsComboBoxToggleButton()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(PpsComboBoxToggleButton), new FrameworkPropertyMetadata(typeof(PpsComboBoxToggleButton)));
		}
	} // class PpsComboBoxToggleButton

	#endregion
}
