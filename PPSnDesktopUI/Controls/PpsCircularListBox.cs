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
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace TecWare.PPSn.Controls
{
	#region -- class PpsCircularListBox -----------------------------------------------

	/// <summary></summary>
	public class PpsCircularListBox : ItemsControl
	{
		private const string partItemBorder = "PART_ItemBorder";
		private PpsCircularView circularListView = null;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
		private static readonly DependencyPropertyKey hasTwoItemsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasTwoItems), typeof(bool), typeof(PpsCircularListBox), new FrameworkPropertyMetadata(BooleanBox.False));
		public static readonly DependencyProperty HasTowItemsProperty = hasTwoItemsPropertyKey.DependencyProperty;

		public static readonly DependencyProperty ListViewCountProperty = DependencyProperty.Register(nameof(ListViewCount), typeof(int), typeof(PpsCircularListBox), new FrameworkPropertyMetadata(9, new PropertyChangedCallback(OnListViewCountChanged)));
		public static readonly DependencyProperty ListProperty = DependencyProperty.Register(nameof(List), typeof(IList), typeof(PpsCircularListBox), new FrameworkPropertyMetadata(null, new PropertyChangedCallback(OnListChanged)));
		public static readonly DependencyProperty SelectedItemProperty = Selector.SelectedItemProperty.AddOwner(typeof(PpsCircularListBox), new FrameworkPropertyMetadata(null, new PropertyChangedCallback(OnSelectedItemChanged)));
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

		/// <summary></summary>
		public PpsCircularListBox()
		{
			CommandBindings.Add(new CommandBinding(
				ApplicationCommands.NotACommand,
				(sender, e) =>
				{
					if (e.Parameter is int parm)
					{
						EnsureFocus();
						circularListView.Move(parm);
					}
				},
				(sender, e) => e.CanExecute = circularListView != null)
			);
		} // ctor

		private static void OnListChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
			=> ((PpsCircularListBox)d).OnListChanged((IList)e.NewValue, (IList)e.OldValue);

		private static void OnListViewCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
			=> ((PpsCircularListBox)d).OnListViewCountChanged((int)e.NewValue, (int)e.OldValue);

		private void OnListChanged(IList newValue, IList oldValue)
			=> Initialize(newValue, ListViewCount);

		private void OnListViewCountChanged(int newValue, int oldValue)
			=> Initialize(List, newValue);

		private void Initialize(IList items, int listViewCount)
		{
			circularListView = new PpsCircularView(items, listViewCount);
			if (items.Count == 2)
				HasTwoItems = true;
			ItemsSource = circularListView;
		} // proc Initialize
		
		private void CollectionChanged_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
				case NotifyCollectionChangedAction.Remove:
				case NotifyCollectionChangedAction.Reset:
				case NotifyCollectionChangedAction.Move:
				case NotifyCollectionChangedAction.Replace:
					SelectedItem = circularListView.Count > 0 ? circularListView[0] : null;
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		} // event CollectionChanged_CollectionChanged

		private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
			=> ((PpsCircularListBox)d).OnSelectedItemChanged(e.NewValue, e.OldValue);

		/// <summary></summary>
		/// <param name="newValue"></param>
		/// <param name="oldValue"></param>
		protected virtual void OnSelectedItemChanged(object newValue, object oldValue)
			=> circularListView?.MoveTo(newValue);

		private void SelectNextItem()
			=> circularListView?.Move(1);

		private void SelectPreviousItem()
			=> circularListView?.Move(-1);

		private void EnsureFocus()
		{
			if (IsFocused)
				return;
			var focusScope = FocusManager.GetFocusScope(this);
			FocusManager.SetFocusedElement(focusScope, this);
			Keyboard.Focus(this);
		} // proc EnsureFocus

		private int CalculateItemIndex(double mouseY, double itemHeight)
			=> (int)mouseY / (int)itemHeight;

		/// <summary></summary>
		/// <param name="e"></param>
		protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
		{
			e.Handled = true;
			base.OnMouseLeftButtonDown(e);
			EnsureFocus();
			if (e.OriginalSource is Border border && String.Compare(border.Name, partItemBorder, false) == 0)
			{
				var idx = CalculateItemIndex(e.GetPosition(this).Y + (HasTwoItems ? border.ActualHeight : 0d), border.ActualHeight);
				circularListView.MoveTo(idx);
			}
		} // proc OnMouseLeftButtonDown

		/// <summary></summary>
		/// <param name="e"></param>
		protected override void OnKeyDown(KeyEventArgs e)
		{
			var key = e.Key;
			if (key == Key.System)
				key = e.SystemKey;

			switch (key)
			{
				case Key.Down:
					e.Handled = true;
					SelectNextItem();
					break;
				case Key.Up:
					e.Handled = true;
					SelectPreviousItem();
					break;
			}

			base.OnKeyDown(e);
		} // proc OnKeyDown

		/// <summary></summary>
		/// <param name="e"></param>
		protected override void OnMouseWheel(MouseWheelEventArgs e)
		{
			EnsureFocus();
			if (e.Delta > 0)
				SelectPreviousItem();
			else if (e.Delta < 0)
				SelectNextItem();

			e.Handled = true;
			base.OnMouseWheel(e);
		} // proc OnMouseWheel

		/// <summary></summary>
		/// <param name="item"></param>
		/// <returns></returns>
		protected override bool IsItemItsOwnContainerOverride(object item)
			=> item is ContentControl;

		/// <summary></summary>
		/// <returns></returns>
		protected override DependencyObject GetContainerForItemOverride()
			=> new ContentControl();

		/// <summary>Source list</summary>
		public IList List { get => (IList)GetValue(ListProperty); set => SetValue(ListProperty, value); }
		/// <summary>Visible list items.</summary>
		public int ListViewCount { get => (int)GetValue(ListViewCountProperty); set => SetValue(ListViewCountProperty, value); }
		/// <summary></summary>
		public object SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
		/// <summary>Has the base list only two items.</summary>
		public bool HasTwoItems { get => BooleanBox.GetBool(GetValue(HasTowItemsProperty)); private set => SetValue(hasTwoItemsPropertyKey, BooleanBox.GetObject(value)); }

		static PpsCircularListBox()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(PpsCircularListBox), new FrameworkPropertyMetadata(typeof(PpsCircularListBox)));
		} // sctor
	} // class PpsCircularListBox

	#endregion

	#region -- class PpsMultiCircularListBox ------------------------------------------

	/// <summary></summary>
	public class PpsMultiCircularListBox : ContentControl
	{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
		public static readonly DependencyProperty ListViewCountProperty = PpsCircularListBox.ListViewCountProperty.AddOwner(typeof(PpsMultiCircularListBox), new FrameworkPropertyMetadata(9));
		public static readonly DependencyProperty ListSourceProperty = DependencyProperty.Register(nameof(ListSource), typeof(object), typeof(PpsMultiCircularListBox), new FrameworkPropertyMetadata(null, new PropertyChangedCallback(OnListSourceChanged)));
		public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.Register(nameof(SelectedItems), typeof(object[]), typeof(PpsMultiCircularListBox), new FrameworkPropertyMetadata(null));

		public static readonly RoutedEvent SelectedItemsChangedEvent = EventManager.RegisterRoutedEvent(nameof(SelectedItemsChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(PpsMultiCircularListBox));
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

		/// <summary></summary>
		public event EventHandler SelectedItemsChanged { add => AddHandler(SelectedItemsChangedEvent, value); remove => RemoveHandler(SelectedItemsChangedEvent, value); }

		private readonly ObservableCollection<IList> parts = new ObservableCollection<IList>();
		
		/// <summary></summary>
		public PpsMultiCircularListBox()
		{
		} // ctor
		
		private static void OnListSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
			=> ((PpsMultiCircularListBox)d).OnListSourceChanged(e.NewValue, e.OldValue);

		/// <summary></summary>
		/// <param name="newValue"></param>
		/// <param name="oldValue"></param>
		protected virtual void OnListSourceChanged(object newValue, object oldValue)
		{
			parts.Clear();

			if (newValue is IList l)
				parts.Add(l);
			if(newValue is IEnumerable e)
			{
				foreach (var c in e)
					parts.Add((IList)c);
			}
		} // proc OnListSourceChanged

		/// <summary>Access parts</summary>
		public IList Parts => parts;

		/// <summary>Visible list items.</summary>
		public int ListViewCount { get => (int)GetValue(ListViewCountProperty); set => SetValue(ListViewCountProperty, value); }
		/// <summary></summary>
		public object ListSource { get => GetValue(ListSourceProperty); set => SetValue(ListSourceProperty, value); }
		/// <summary></summary>
		public object[] SelectedItems { get => (object[])GetValue(SelectedItemsProperty); set => SetValue(SelectedItemsProperty, value); }

		static PpsMultiCircularListBox()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(PpsMultiCircularListBox), new FrameworkPropertyMetadata(typeof(PpsMultiCircularListBox)));
		} // sctor
	} // class PpsMultiCircularListBox

	#endregion
}
