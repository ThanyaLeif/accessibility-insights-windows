// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using AccessibilityInsights.CommonUxComponents.Controls;
using AccessibilityInsights.CommonUxComponents.Dialogs;
using AccessibilityInsights.Extensions.Interfaces.IssueReporting;
using AccessibilityInsights.SharedUx.Controls.CustomControls;
using AccessibilityInsights.SharedUx.Enums;
using AccessibilityInsights.SharedUx.FileIssue;
using AccessibilityInsights.SharedUx.Highlighting;
using AccessibilityInsights.SharedUx.Settings;
using AccessibilityInsights.SharedUx.Telemetry;
using AccessibilityInsights.SharedUx.Utilities;
using AccessibilityInsights.SharedUx.ViewModels;
using Axe.Windows.Actions.Contexts;
using Axe.Windows.Core.Bases;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AccessibilityInsights.SharedUx.Controls.TestTabs
{
    /// <summary>
    /// Interaction logic for AutomatedChecksControl.xaml
    /// </summary>
    public partial class AutomatedChecksControl : UserControl
    {
        /// <summary>
        /// Element context
        /// </summary>
        public ElementContext ElementContext { get; set; }

        /// <summary>
        /// Delegate for rerun test
        /// </summary>
        public Action RunAgainTest { get; set; }

        /// <summary>
        /// Data context
        /// </summary>
        new ElementDataContext DataContext;

        /// <summary>
        /// Action to perform when element clicked on in listview
        /// </summary>
        public Action NotifyElementSelected { get; set; }

        /// <summary>
        /// Action to perform when user needs to log into the server
        /// </summary>
        public Action SwitchToServerLogin { get; set; }

        /// <summary>
        /// Overriding LocalizedControlType
        /// </summary>
        /// <returns></returns>
        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new CustomControlOverridingAutomationPeer(this, "pane");
        }

        /// <summary>
        /// App configation
        /// </summary>
        public static ConfigurationModel Configuration
        {
            get
            {
                return ConfigurationManager.GetDefaultInstance()?.AppConfig;
            }
        }

        /// <summary>
        /// Whether there is a screenshot available (if not, checkboxes should be disabled)
        /// </summary>
        public bool ScreenshotAvailable => DataContext != null && DataContext.Screenshot != null;

        /// <summary>
        /// Gets/sets fastpass highlighter visibility, updating as necessary
        /// </summary>
#pragma warning disable CA1822
        public bool HighlightVisibility
#pragma warning restore CA1822
        {
            get
            {
                return Configuration.IsHighlighterOn;
            }
            set
            {
                Configuration.IsHighlighterOn = value;

                var ha = ImageOverlayDriver.GetDefaultInstance();

                if (value)
                {
                    ha.Show();
                    foreach (var svm in SelectedItems)
                    {
                        ha.AddElement(this.ElementContext.Id, svm.Element.UniqueId);
                    }
                    Application.Current.MainWindow.Activate();
                }
                else
                {
                    ha.Hide();
                }
            }
        }

        /// <summary>
        /// Currently selected items
        /// </summary>
        IList<RuleResultViewModel> SelectedItems = new List<RuleResultViewModel>();

        /// <summary>
        /// Set results and populate UI
        /// </summary>
        /// <param name="results"></param>
        public void SetRuleResults(IList<RuleResultViewModel> results)
        {
            if (results != null)
            {
                this.lvResults2.ListView.IsEnabled = true;
                this.lvResults2.ListView.ItemsSource = results;
                this.lvResults2.ListView.Visibility = Visibility.Visible;
                this.lblCongrats.Visibility = Visibility.Collapsed;
                this.lblNoFail.Visibility = Visibility.Collapsed;
                this.gdFailures.Visibility = Visibility.Visible;
                this.gdFailures.Focus();
                var count = results.Where(r => r.RuleResult.Status == Axe.Windows.Core.Results.ScanStatus.Fail).Count();

                switch (count)
                {
                    case 0:
                        this.runFailures.Text = Properties.Resources.AutomatedChecksControl_SetRuleResults_No_failure_was;
                        break;
                    case 1:
                        this.runFailures.Text = Properties.Resources.AutomatedChecksControl_SetRuleResults_1_failure_was;
                        break;
                    default:
                        this.runFailures.Text = String.Format(CultureInfo.InvariantCulture, Properties.Resources.AutomatedChecksControl_SetRuleResults_0_failures_were, results.Count);
                        break;
                }

                CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(lvResults2.ListView.ItemsSource);
                PropertyGroupDescription groupDescription = new PropertyGroupDescription(Properties.Resources.AutomatedChecksControl_SetRuleResults_TitleURL);
                view.GroupDescriptions.Add(groupDescription);
            }
            else
            {
                this.gdFailures.Visibility = Visibility.Collapsed;
                this.lvResults2.ListView.ItemsSource = null;
                this.lblCongrats.Visibility = Visibility.Visible;
                this.lblNoFail.Visibility = Visibility.Visible;
                this.lvResults2.ListView.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Automated Checks mode is left
        /// </summary>
        public static void Hide()
        {
            ImageOverlayDriver.GetDefaultInstance().ClearElements();
        }

        /// Automated Checks mode is shown
        public void Show()
        {
            var ha = ImageOverlayDriver.GetDefaultInstance();
            // set handler here. it will make sure that highliter button is shown and working.
            ha.SetHighlighterButtonClickHandler(TBElem_Click);

            if (this.HighlightVisibility)
            {
                ha.Show();

                foreach (var svm in SelectedItems)
                {
                    ha.AddElement(this.ElementContext.Id, svm.Element.UniqueId);
                }

                /// Only activate the main window if tests have already been run. This ensures we don't lose transient UI.
                /// We need to activate the window so that keyboard focus isn't lost when switching from Tab Stops to
                /// Automated Checks.
                if (this.ElementContext != null)
                {
                    Application.Current.MainWindow.Activate();
                }
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public AutomatedChecksControl()
        {
            InitializeComponent();
            lvResults2.ListView.AddHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler(Thumb_DragDelta), true);
        }

        /// <summary>
        /// Resize column widths
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var thumb = e.OriginalSource as Thumb;
            var header = thumb.TemplatedParent as GridViewColumnHeader;

            if (header != null && header.Content != null)
            {
                if (header.Column.ActualWidth + e.HorizontalChange < header.MinWidth)
                {
                    header.Column.Width = header.MinWidth;
                }
                else if (header.Column.ActualWidth + e.HorizontalChange > header.MaxWidth)
                {
                    header.Column.Width = header.MaxWidth;
                }
                else
                {
                    header.Column.Width = header.Column.ActualWidth + e.HorizontalChange;
                }
            }
        }

        /// <summary>
        /// Handles click on error exclamation point
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TBElem_Click(object sender, RoutedEventArgs e)
        {
            NotifySelected(((TextBlock)sender).Tag as A11yElement);
        }

        /// <summary>
        /// Notify Element selection to swith mode to snapshot
        /// </summary>
        /// <param name="e"></param>
        private void NotifySelected(A11yElement e)
        {
            this.DataContext.FocusedElementUniqueId = e.UniqueId;
            NotifyElementSelected();
        }

        /// <summary>
        /// Sets element context and updates UI
        /// </summary>
        /// <param name="ec"></param>
        public void SetElement(ElementContext ec)
        {
            if (ec == null)
                throw new ArgumentNullException(nameof(ec));

            try
            {
                // set handler here. it will make sure that highliter button is shown and working.
                ImageOverlayDriver.GetDefaultInstance().SetHighlighterButtonClickHandler(TBElem_Click);

                if (this.ElementContext == null || ec.Element != this.ElementContext.Element || this.DataContext != ec.DataContext)
                {
                    var viewModel = new AutomatedChecksCustomListViewModel(ec.DataContext, NotifyElementSelected,
                        SwitchToServerLogin, SetItemsChecked)
                    {
                        ElementContext = ec,
                    };
                    
                    this.lblCongrats.Visibility = Visibility.Collapsed;
                    this.lblNoFail.Visibility = Visibility.Collapsed;
                    this.gdFailures.Visibility = Visibility.Collapsed;
                    this.DataContext = ec.DataContext;
                    this.lvResults2.ViewModel = viewModel;
                    this.lvResults2.CheckBoxSelectAll.IsEnabled = ScreenshotAvailable;
                    this.lvResults2.ListView.ItemsSource = null;
                    this.ElementContext = ec;
                    this.tbGlimpse.Text = string.Format(CultureInfo.InvariantCulture,
                        Properties.Resources.AutomatedChecksControl_TargetFormat, ec.Element.Glimpse);
                    UpdateUI();
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception e)
            {
                e.ReportException();
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }

        /// <summary>
        /// Displays loading indicator and updates UI
        /// </summary>
        /// <param name="forceRefresh"></param>
        public void UpdateUI()
        {
            this.lblCongrats.Visibility = Visibility.Collapsed;
            this.lblNoFail.Visibility = Visibility.Collapsed;
            this.gdFailures.Visibility = Visibility.Collapsed;
            this.lvResults2.ViewModel.AllExpanded = false;
            this.lvResults2.FabricIconExpandAll.GlyphName = FabricIcon.CaretSolidRight;
            this.SelectedItems.Clear();
            this.lvResults2.CheckBoxSelectAll.IsChecked = false;
            HollowHighlightDriver.GetDefaultInstance().Clear();

            if (this.DataContext != null)
            {
                var list = this.DataContext.GetRuleResultsViewModelList();

                SetRuleResults(list);

                if (list != null)
                {
                    var ha = ImageOverlayDriver.GetDefaultInstance();
                    ha.SetImageElement(ElementContext.Id);
                    if (HighlightVisibility)
                    {
                        ha.Show();
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    lvResults2.ViewModel.CheckAllBoxes(lvResults2.ListView, true);
                }, DispatcherPriority.Input);
            }
        }

        /// <summary>
        /// Clears listview
        /// </summary>
        public void ClearUI()
        {
            ImageOverlayDriver.ClearDefaultInstance();
            this.ElementContext = null;
            this.lvResults2.ListView.ItemsSource = null;
            this.tbGlimpse.Text = Properties.Resources.GlimpseTextTarget;
            this.gdFailures.Visibility = Visibility.Collapsed;
            this.SelectedItems.Clear();
        }

        /// <summary>
        /// Rescanns current element
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnRescan_Click(object sender, RoutedEventArgs e)
        {
            this.lvResults2.ListView.IsEnabled = false;
            this.RunAgainTest();
        }

        /// <summary>
        /// Finds all controls of the given type under the given object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="element"></param>
        /// <returns></returns>
        public IEnumerable<T> FindChildren<T>(DependencyObject element) where T : DependencyObject
        {
            if (element != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(element, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }
                    foreach (T descendant in FindChildren<T>(child))
                    {
                        yield return descendant;
                    }
                }
            }
        }

        /// <summary>
        /// Get child element of specified type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="element"></param>
        /// <returns></returns>
        public DependencyObject GetFirstChildElement<T>(DependencyObject element)
        {
            if (element == null)
            {
                return null;
            }

            if (element is T)
            {
                return element as DependencyObject;
            }

            for (int x = 0; x < VisualTreeHelper.GetChildrenCount(element); x++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(element, x);
                var b = GetFirstChildElement<T>(child);

                if (b != null)
                    return b;
            }
            return null;
        }

        /// <summary>
        /// Finds object up parent hierarchy of specified type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        private DependencyObject GetParentElem<T>(DependencyObject obj)
        {
            try
            {
                var par = VisualTreeHelper.GetParent(obj);

                if (par is T)
                {
                    return par;
                }
                else
                {
                    return GetParentElem<T>(par);
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception e)
            {
                e.ReportException();
                return null;
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }

        ///// <summary>
        ///// Custom keyboard nav behavior
        ///// </summary>
        ///// <param name="sender"></param>
        ///// <param name="e"></param>
        //private void lviResults_PreviewKeyDown(object sender, KeyEventArgs e)
        //{
        //    UIElement uie = e.OriginalSource as UIElement;

        //    if ((e.Key == Key.Right || e.Key == Key.Left) && Keyboard.FocusedElement is ListViewItem)
        //    {
        //        if (e.Key == Key.Right)
        //        {
        //            var vb = GetFirstChildElement<CheckBox>(sender as DependencyObject) as CheckBox;
        //            vb.Focus();
        //        }
        //        else
        //        {
        //            var parent = GetParentElem<Expander>(sender as DependencyObject) as Expander;
        //            var vb = GetFirstChildElement<Label>(parent as DependencyObject) as Label;
        //            vb.Focus();
        //        }
        //        e.Handled = true;
        //    }
        //    else if ((e.Key == Key.Right || e.Key == Key.Left) && (Keyboard.FocusedElement is CheckBox || Keyboard.FocusedElement is Button))
        //    {
        //        var elements = new List<DependencyObject>();
        //        elements.Add(GetFirstChildElement<CheckBox>(sender as DependencyObject));
        //        elements.AddRange(FindChildren<Button>(sender as DependencyObject));
        //        int selectedElementIndex = elements.FindIndex(b => b.Equals(Keyboard.FocusedElement));

        //        if (e.Key == Key.Right)
        //        {
        //            if (selectedElementIndex + 1 < elements.Count)
        //            {
        //                ((UIElement)elements.ElementAt(selectedElementIndex + 1)).Focus();
        //            }
        //        }
        //        else if (selectedElementIndex - 1 >= 0)
        //        {
        //            ((UIElement)elements.ElementAt(selectedElementIndex - 1)).Focus();
        //        }
        //        else
        //        {
        //            (sender as ListBoxItem).Focus();
        //        }
        //        System.Diagnostics.Debug.WriteLine(Keyboard.FocusedElement.ToString() + " " + FrameworkElementAutomationPeer.FromElement(Keyboard.FocusedElement as FrameworkElement)?.HasKeyboardFocus());
        //        FrameworkElementAutomationPeer.FromElement(Keyboard.FocusedElement as FrameworkElement)?.RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
        //        e.Handled = true;
        //    }
        //    else if (e.Key == Key.Down || e.Key == Key.Up)
        //    {
        //        (sender as ListViewItem).Focus();
        //        uie = (UIElement)Keyboard.FocusedElement;

        //        if (e.Key == Key.Down)
        //        {
        //            uie.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
        //        }
        //        else
        //        {
        //            var element = uie.PredictFocus(FocusNavigationDirection.Up);
        //            if (element is ListViewItem)
        //            {
        //                uie.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up));
        //            }
        //            else
        //            {
        //                (GetParentElem<GroupItem>(sender as DependencyObject) as UIElement).Focus();
        //            }
        //        }
        //        e.Handled = true;
        //    }
        //    else if (e.Key == Key.Return && Keyboard.FocusedElement is ListViewItem)
        //    {
        //        var btn = GetFirstChildElement<Button>(sender as DependencyObject) as Button;
        //        ButtonElem_Click(btn, e);
        //    }
        //}

        /// <summary>
        /// Add horizontal scroll bar when width is too narrow
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void scrollview_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var view = sender as ScrollViewer;
            if (e.NewSize.Width <= this.tbSubTitle.MinWidth)
            {
                view.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                this.tbSubTitle.Width = this.tbSubTitle.MinWidth;
            }
            else
            {
                view.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                this.tbSubTitle.Width = Double.NaN;
            }
        }

        /// <summary>
        /// Select all items in list
        /// </summary>
        private bool SetItemsChecked(IReadOnlyCollection<Object> lst, bool check)
        {
            var ret = true;
            foreach (RuleResultViewModel itm in lst.AsParallel())
            {
                if (check && !SelectedItems.Contains(itm))
                {
                    SelectedItems.Add(itm);
                    ImageOverlayDriver.GetDefaultInstance().AddElement(this.ElementContext.Id, itm.Element.UniqueId);
                }

                else if (!check && SelectedItems.Contains(itm))
                {
                    SelectedItems.Remove(itm);
                    ImageOverlayDriver.GetDefaultInstance().RemoveElement(this.ElementContext.Id, itm.Element.UniqueId);
                }
                var lvi = lvResults2.ListView.ItemContainerGenerator.ContainerFromItem(itm) as ListViewItem;
                if (lvi != null)
                {
                    lvi.IsSelected = check;
                }
                else
                {
                    ret = false;
                }
            }
            UpdateSelectAll();
            return ret;
        }

        ///// <summary>
        ///// Handles unselecting a listviewitem
        ///// </summary>
        ///// <param name="sender"></param>
        ///// <param name="e"></param>
        //private void ListViewItem_Unselected(object sender, RoutedEventArgs e)
        //{
        //    var lvi = sender as ListViewItem;
        //    var exp = GetParentElem<Expander>(lvi) as Expander;
        //    var cb = GetFirstChildElement<CheckBox>(exp) as CheckBox;
        //    var srvm = lvi.DataContext as RuleResultViewModel;

        //    // ElementContext can be null when app is closed.
        //    if (this.ElementContext != null)
        //    {
        //        ImageOverlayDriver.GetDefaultInstance().RemoveElement(this.ElementContext.Id, srvm.Element.UniqueId);
        //    }

        //    SelectedItems.Remove(srvm);
        //    var groupitem = GetParentElem<GroupItem>(exp) as GroupItem;
        //    var dc = cb.DataContext as CollectionViewGroup;
        //    var itms = dc.Items;
        //    var any = itms.Intersect(SelectedItems).Any();
        //    if (any)
        //    {
        //        cb.IsChecked = null;
        //        groupitem.Tag = "some";
        //    }
        //    else
        //    {
        //        cb.IsChecked = false;
        //        groupitem.Tag = "zero";
        //    }
        //    UpdateSelectAll();
        //}

        /// <summary>
        /// Update select checkbox state
        /// </summary>
        private void UpdateSelectAll()
        {
            if (SelectedItems.Count == 0)
            {
                lvResults2.CheckBoxSelectAll.IsChecked = false;
            }
            else if (SelectedItems.Count == lvResults2.ListView.Items.Count)
            {
                lvResults2.CheckBoxSelectAll.IsChecked = true;
            }
            else
            {
                lvResults2.CheckBoxSelectAll.IsChecked = null;
            }
        }

        ///// <summary>
        ///// Handles list view item selection
        ///// </summary>
        ///// <param name="sender"></param>
        ///// <param name="e"></param>
        //private void ListViewItem_Selected(object sender, RoutedEventArgs e)
        //{
        //    if (ScreenshotAvailable)
        //    {
        //        var lvi = sender as ListViewItem;
        //        var exp = GetParentElem<Expander>(lvi) as Expander;
        //        var cb = GetFirstChildElement<CheckBox>(exp) as CheckBox;
        //        var itm = lvi.DataContext as RuleResultViewModel;
        //        if (!SelectedItems.Contains(itm))
        //        {
        //            SelectedItems.Add(itm);
        //            UpdateSelectAll();
        //            ImageOverlayDriver.GetDefaultInstance().AddElement(this.ElementContext.Id, itm.Element.UniqueId);
        //        }
        //        var groupitem = GetParentElem<GroupItem>(exp) as GroupItem;
        //        var dc = cb.DataContext as CollectionViewGroup;
        //        var itms = dc.Items;
        //        var any = itms.Except(SelectedItems).Any();
        //        if (any)
        //        {
        //            cb.IsChecked = null;
        //            groupitem.Tag = "some"; // used to indicate how many children are selected
        //        }
        //        else
        //        {
        //            cb.IsChecked = true;
        //            groupitem.Tag = "all";
        //        }
        //    }
        //}

        /// <summary>
        /// Call show/hide when tab changes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
#pragma warning disable CA1801 // unused parameter
        private void UserControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
#pragma warning restore CA1801 // unused parameter
        {
            if ((bool)e.NewValue == true)
            {
                Show();
            }
            else
            {
                Hide();
            }
        }

        ///// <summary>
        ///// Custom keyboard behavior for group items
        ///// </summary>
        ///// <param name="sender"></param>
        ///// <param name="e"></param>
        //private void GroupItem_PreviewKeyDown(object sender, KeyEventArgs e)
        //{
        //    var listViewItemParent = GetParentElem<ListViewItem>(Keyboard.FocusedElement as DependencyObject);
        //    if (Keyboard.FocusedElement is ListViewItem || listViewItemParent != null)
        //    {
        //        // Let it be handled by the listviewitem previewkeydown handler
        //        //  - this groupitem_previewkeydown event fires first
        //        return;
        //    }

        //    var gi = sender as GroupItem;
        //    var sp = GetFirstChildElement<StackPanel>(gi) as StackPanel;
        //    var exp = GetParentElem<Expander>(sp) as Expander;

        //    if (e.Key == Key.Right)
        //    {
        //        if (!exp.IsExpanded)
        //        {
        //            exp.IsExpanded = true;
        //        }
        //        e.Handled = true;
        //    }
        //    else if (e.Key == Key.Left)
        //    {
        //        if (exp.IsExpanded)
        //        {
        //            exp.IsExpanded = false;
        //        }

        //        e.Handled = true;
        //    }
        //    else if ((e.Key == Key.Space || e.Key == Key.Enter) && Keyboard.FocusedElement == sender)
        //    {
        //        var cb = GetFirstChildElement<CheckBox>(exp) as CheckBox;
        //        cb.IsChecked = !cb.IsChecked ?? false;
        //        CheckBox_Click(cb, null);
        //        e.Handled = true;
        //    }
        //    else if (e.Key == Key.Down)
        //    {
        //        if (!exp.IsExpanded)
        //        {
        //            gi.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
        //        }
        //        else
        //        {
        //            ListViewItem firstResult = GetFirstChildElement<ListViewItem>(exp) as ListViewItem;
        //            firstResult.Focus();
        //        }
        //        e.Handled = true;
        //    }
        //    else if (e.Key == Key.Up)
        //    {
        //        gi.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up));
        //        e.Handled = true;
        //    }
        //}

        /// <summary>
        /// Handles click on show failures button
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnShowFailures_Click(object sender, RoutedEventArgs e)
        {
            this.HighlightVisibility = !this.HighlightVisibility;
        }

        /// <summary>
        /// Set highlighter toggle off
        /// </summary>
        private void ToggleHighlighterOff()
        {
            this.HighlightVisibility = false;
        }

        /// <summary>
        /// Click on view results in UIA tree button goes to Test Inspect view with POI element selected
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnTree_Click(object sender, RoutedEventArgs e)
        {
            NotifySelected(ElementContext.Element);
        }
    }
}
