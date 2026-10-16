using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Cashier.UI.Controls;

public static class ComboBoxSearchBehavior
{
    public static readonly DependencyProperty IsKeyboardHighlightProperty =
        DependencyProperty.RegisterAttached(
            "IsKeyboardHighlight",
            typeof(bool),
            typeof(ComboBoxSearchBehavior),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ComboBoxSearchBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly ConditionalWeakTable<ComboBox, SearchState> States = new();

    public static bool GetIsKeyboardHighlight(DependencyObject element) =>
        (bool)element.GetValue(IsKeyboardHighlightProperty);

    public static void SetIsKeyboardHighlight(DependencyObject element, bool value) =>
        element.SetValue(IsKeyboardHighlightProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ComboBox comboBox)
            throw new InvalidOperationException($"{nameof(IsEnabledProperty)} can only be applied to a ComboBox.");

        var state = States.GetValue(comboBox, static combo => new SearchState(combo));
        if ((bool)e.NewValue)
            state.Enable();
        else
            state.Disable();
    }

    private sealed class SearchState(ComboBox comboBox)
    {
        private TextBox? editor;
        private string query = string.Empty;
        private int highlightedIndex = -1;
        private bool isAttached;
        private bool suppressTextChanged;

        public void Enable()
        {
            comboBox.Loaded += OnLoaded;
            comboBox.Unloaded += OnUnloaded;
            if (comboBox.IsLoaded)
                Attach();
        }

        public void Disable()
        {
            comboBox.Loaded -= OnLoaded;
            comboBox.Unloaded -= OnUnloaded;
            Detach();
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => Attach();

        private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

        private void Attach()
        {
            if (isAttached)
                return;

            isAttached = true;
            comboBox.ApplyTemplate();
            comboBox.IsEditable = true;
            comboBox.IsTextSearchEnabled = false;
            if (string.IsNullOrWhiteSpace(TextSearch.GetTextPath(comboBox)) &&
                !string.IsNullOrWhiteSpace(comboBox.DisplayMemberPath))
                TextSearch.SetTextPath(comboBox, comboBox.DisplayMemberPath);
            comboBox.GotKeyboardFocus += OnGotKeyboardFocus;
            comboBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            comboBox.PreviewKeyDown += OnPreviewKeyDown;
            comboBox.SelectionChanged += OnSelectionChanged;
            comboBox.DropDownClosed += OnDropDownClosed;
            comboBox.ItemContainerGenerator.StatusChanged += OnGeneratorStatusChanged;
            ((INotifyCollectionChanged)comboBox.Items).CollectionChanged += OnItemsChanged;
            AttachEditor();
            UpdateItemVisibility();
            if (comboBox.SelectedItem is not null && comboBox.SelectedIndex >= 0)
            {
                var selectedContainer = comboBox.ItemContainerGenerator.ContainerFromIndex(comboBox.SelectedIndex) as ComboBoxItem;
                SetText(DisplayText(comboBox.SelectedItem, selectedContainer));
            }
        }

        private void Detach()
        {
            if (!isAttached)
                return;

            isAttached = false;
            comboBox.GotKeyboardFocus -= OnGotKeyboardFocus;
            comboBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            comboBox.PreviewKeyDown -= OnPreviewKeyDown;
            comboBox.SelectionChanged -= OnSelectionChanged;
            comboBox.DropDownClosed -= OnDropDownClosed;
            comboBox.ItemContainerGenerator.StatusChanged -= OnGeneratorStatusChanged;
            ((INotifyCollectionChanged)comboBox.Items).CollectionChanged -= OnItemsChanged;
            if (editor is not null)
                editor.TextChanged -= OnEditorTextChanged;
            editor = null;
        }

        private void AttachEditor()
        {
            if (comboBox.Template.FindName("PART_EditableTextBox", comboBox) is not TextBox currentEditor)
                return;

            if (ReferenceEquals(editor, currentEditor))
                return;
            if (editor is not null)
                editor.TextChanged -= OnEditorTextChanged;
            editor = currentEditor;
            editor.TextChanged += OnEditorTextChanged;
        }

        private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            OpenUnfiltered();
            comboBox.Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    AttachEditor();
                    if (editor?.IsKeyboardFocusWithin == true)
                        editor.SelectAll();
                }));
        }

        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (comboBox.Template.FindName("DropDownToggle", comboBox) is ToggleButton toggle &&
                IsVisualDescendantOf(e.OriginalSource as DependencyObject, toggle))
                return;

            if (!comboBox.IsDropDownOpen)
            {
                OpenUnfiltered();
                comboBox.Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    new Action(() =>
                    {
                        AttachEditor();
                        if (editor?.IsKeyboardFocusWithin == true)
                            editor.SelectAll();
                    }));
            }
        }

        private static bool IsVisualDescendantOf(DependencyObject? element, DependencyObject ancestor)
        {
            while (element is not null)
            {
                if (ReferenceEquals(element, ancestor))
                    return true;
                element = GetParent(element);
            }
            return false;
        }

        private static DependencyObject? GetParent(DependencyObject element) =>
            element switch
            {
                FrameworkContentElement contentElement => contentElement.Parent,
                ContentElement contentElement => ContentOperations.GetParent(contentElement),
                Visual or Visual3D => VisualTreeHelper.GetParent(element),
                _ => LogicalTreeHelper.GetParent(element)
            };

        private void OpenUnfiltered()
        {
            query = string.Empty;
            highlightedIndex = -1;
            UpdateItemVisibility();
            comboBox.IsDropDownOpen = true;
        }

        private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
        {
            if (suppressTextChanged || editor is null)
                return;

            query = editor.Text;
            highlightedIndex = -1;
            UpdateItemVisibility();
            if (comboBox.IsKeyboardFocusWithin)
                comboBox.IsDropDownOpen = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!comboBox.IsDropDownOpen)
                return;

            switch (e.Key)
            {
                case Key.Down:
                    MoveSelection(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    SelectMatch();
                    e.Handled = true;
                    break;
                case Key.Tab:
                    SelectMatch();
                    break;
                case Key.Escape:
                    ResetSearch(restoreSelectionText: true);
                    e.Handled = true;
                    break;
            }
        }

        private void MoveSelection(int direction)
        {
            var matches = GetMatchingIndices();
            if (matches.Count == 0)
                return;

            var currentPosition = matches.IndexOf(highlightedIndex);
            var nextPosition = currentPosition < 0
                ? direction > 0 ? 0 : matches.Count - 1
                : Math.Clamp(currentPosition + direction, 0, matches.Count - 1);
            highlightedIndex = matches[nextPosition];
            UpdateItemVisibility();
            if (comboBox.ItemContainerGenerator.ContainerFromIndex(highlightedIndex) is ComboBoxItem item)
                item.BringIntoView();
        }

        private void SelectMatch()
        {
            var matches = GetMatchingIndices();
            if (matches.Count == 0)
                return;

            var index = matches.Contains(highlightedIndex)
                ? highlightedIndex
                : matches.Contains(comboBox.SelectedIndex)
                    ? comboBox.SelectedIndex
                    : matches[0];
            suppressTextChanged = true;
            try
            {
                comboBox.SelectedIndex = index;
            }
            finally
            {
                suppressTextChanged = false;
            }
            query = string.Empty;
            highlightedIndex = -1;
            UpdateItemVisibility();
            comboBox.IsDropDownOpen = false;
        }

        private void SetText(string text)
        {
            suppressTextChanged = true;
            try
            {
                comboBox.Text = text;
                if (editor is not null)
                    editor.Text = text;
            }
            finally
            {
                suppressTextChanged = false;
            }
        }

        private void OnDropDownClosed(object? sender, EventArgs e) =>
            ResetSearch(restoreSelectionText: true);

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressTextChanged || !string.IsNullOrWhiteSpace(query) || comboBox.IsDropDownOpen)
                return;

            comboBox.Dispatcher.BeginInvoke(
                DispatcherPriority.DataBind,
                new Action(() =>
                {
                    if (!isAttached || comboBox.IsDropDownOpen || comboBox.SelectedItem is null)
                        return;
                    var selectedIndex = comboBox.SelectedIndex;
                    var item = comboBox.ItemContainerGenerator.ContainerFromIndex(selectedIndex) as ComboBoxItem;
                    SetText(DisplayText(comboBox.SelectedItem, item));
                }));
        }

        private void ResetSearch(bool restoreSelectionText)
        {
            query = string.Empty;
            highlightedIndex = -1;
            if (restoreSelectionText)
                SetText(comboBox.SelectedItem is null ? string.Empty : DisplayText(comboBox.SelectedItem));
            UpdateItemVisibility();
            comboBox.IsDropDownOpen = false;
        }

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
            UpdateItemVisibility();

        private void OnGeneratorStatusChanged(object? sender, EventArgs e)
        {
            AttachEditor();
            UpdateItemVisibility();
        }

        private void UpdateItemVisibility()
        {
            if (!isAttached)
                return;

            for (var index = 0; index < comboBox.Items.Count; index++)
            {
                if (comboBox.ItemContainerGenerator.ContainerFromIndex(index) is not ComboBoxItem container)
                    continue;
                // Hide generated containers instead of filtering the shared collection view.
                SetIsKeyboardHighlight(container, index == highlightedIndex);
                container.Visibility = Matches(comboBox.Items[index], container)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private List<int> GetMatchingIndices()
        {
            var matches = new List<int>();
            for (var index = 0; index < comboBox.Items.Count; index++)
            {
                if (Matches(comboBox.Items[index], comboBox.ItemContainerGenerator.ContainerFromIndex(index) as ComboBoxItem))
                    matches.Add(index);
            }
            return matches;
        }

        private bool Matches(object item, ComboBoxItem? container) =>
            string.IsNullOrWhiteSpace(query) ||
            DisplayText(item, container).Contains(query, StringComparison.CurrentCultureIgnoreCase);

        private string DisplayText(object? item, ComboBoxItem? container = null)
        {
            if (item is null)
                return string.Empty;

            var renderedText = container is null ? string.Empty : ReadRenderedText(container);
            if (!string.IsNullOrWhiteSpace(renderedText))
                return renderedText;

            var textPath = TextSearch.GetTextPath(comboBox);
            var memberPath = !string.IsNullOrWhiteSpace(textPath)
                ? textPath
                : comboBox.DisplayMemberPath;
            var memberText = ReadMemberPath(item, memberPath);
            if (!string.IsNullOrWhiteSpace(memberText))
                return memberText;

            if (item.GetType().IsEnum)
                return (string)new ArabicEnumConverter().Convert(
                    item,
                    typeof(string),
                    string.Empty,
                    System.Globalization.CultureInfo.CurrentCulture);

            var name = ReadMemberPath(item, "Name");
            if (!string.IsNullOrWhiteSpace(name))
                return name;

            return item is string text ? text : string.Empty;
        }

        private static string ReadMemberPath(object item, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            object? value = item;
            foreach (var segment in path.Split('.'))
            {
                if (value is null)
                    return string.Empty;
                var property = TypeDescriptor.GetProperties(value)[segment];
                if (property is null)
                    return string.Empty;
                value = property.GetValue(value);
            }
            return value?.ToString() ?? string.Empty;
        }

        private static string ReadRenderedText(DependencyObject root)
        {
            var parts = new List<string>();
            ReadVisualText(root, parts);
            return string.Join(" ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
        }

        private static void ReadVisualText(DependencyObject root, ICollection<string> parts)
        {
            if (root is TextBlock textBlock)
            {
                if (!string.IsNullOrWhiteSpace(textBlock.Text))
                    parts.Add(textBlock.Text);
                else
                    foreach (var run in textBlock.Inlines.OfType<System.Windows.Documents.Run>())
                        parts.Add(run.Text);
            }
            else if (root is TextBox textBox)
            {
                parts.Add(textBox.Text);
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
                ReadVisualText(VisualTreeHelper.GetChild(root, index), parts);
        }
    }
}
