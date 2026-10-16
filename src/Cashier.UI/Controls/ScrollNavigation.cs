using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Cashier.UI.Controls;

public static class ScrollNavigation
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ScrollNavigation),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly ConditionalWeakTable<ScrollViewer, NavigationState> States = new();

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ScrollViewer scrollViewer)
            throw new InvalidOperationException($"{nameof(IsEnabledProperty)} can only be applied to a ScrollViewer.");

        var state = States.GetValue(scrollViewer, static viewer => new NavigationState(viewer));
        if ((bool)e.NewValue)
            state.Enable();
        else
            state.Disable();
    }

    private sealed class NavigationState(ScrollViewer scrollViewer)
    {
        private ScrollNavigationAdorner? adorner;
        private AdornerLayer? layer;

        public void Enable()
        {
            scrollViewer.Loaded += OnLoaded;
            scrollViewer.Unloaded += OnUnloaded;
            if (scrollViewer.IsLoaded)
                Attach();
        }

        public void Disable()
        {
            scrollViewer.Loaded -= OnLoaded;
            scrollViewer.Unloaded -= OnUnloaded;
            scrollViewer.ScrollChanged -= OnScrollChanged;
            DetachAdorner();
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => Attach();

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            scrollViewer.ScrollChanged -= OnScrollChanged;
            DetachAdorner();
        }

        private void Attach()
        {
            scrollViewer.ScrollChanged -= OnScrollChanged;
            scrollViewer.ScrollChanged += OnScrollChanged;

            if (adorner is null)
            {
                layer = AdornerLayer.GetAdornerLayer(scrollViewer);
                if (layer is null)
                {
                    Trace.TraceError("Scroll navigation could not attach because the ScrollViewer has no AdornerLayer.");
                    return;
                }

                adorner = new ScrollNavigationAdorner(scrollViewer);
                layer.Add(adorner);
            }
            UpdateState();
        }

        private void DetachAdorner()
        {
            if (adorner is not null && layer is not null)
                layer.Remove(adorner);
            adorner = null;
            layer = null;
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateState();

        private void UpdateState() => adorner?.UpdateState(
            scrollViewer.ScrollableHeight,
            scrollViewer.VerticalOffset,
            scrollViewer.ViewportHeight);
    }

    private sealed class ScrollNavigationAdorner : Adorner
    {
        private readonly VisualCollection children;
        private readonly Button upButton;
        private readonly Button downButton;
        private readonly ScrollViewer scrollViewer;
        private bool canScroll;

        public ScrollNavigationAdorner(ScrollViewer scrollViewer) : base(scrollViewer)
        {
            this.scrollViewer = scrollViewer;
            IsHitTestVisible = true;
            upButton = CreateButton(
                "M 12,19 L 12,5 M 6,11 L 12,5 L 18,11",
                "التمرير إلى أعلى");
            downButton = CreateButton(
                "M 12,5 L 12,19 M 6,13 L 12,19 L 18,13",
                "التمرير إلى أسفل");
            upButton.Click += OnScrollUp;
            downButton.Click += OnScrollDown;
            children = new VisualCollection(this) { upButton, downButton };
        }

        protected override int VisualChildrenCount => children.Count;

        protected override Visual GetVisualChild(int index) => children[index];

        protected override Size MeasureOverride(Size constraint)
        {
            upButton.Measure(new Size(42, 42));
            downButton.Measure(new Size(42, 42));
            return new Size(
                GetFiniteDimension(constraint.Width, scrollViewer.RenderSize.Width),
                GetFiniteDimension(constraint.Height, scrollViewer.RenderSize.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            const double buttonSize = 42;
            const double gap = 5;
            const double inset = 10;
            var left = scrollViewer.FlowDirection == FlowDirection.RightToLeft
                ? inset
                : Math.Max(inset, finalSize.Width - buttonSize - inset);
            var top = Math.Max(inset, finalSize.Height - (buttonSize * 2) - gap - inset);
            upButton.Arrange(new Rect(left, top, buttonSize, buttonSize));
            downButton.Arrange(new Rect(left, top + buttonSize + gap, buttonSize, buttonSize));
            return finalSize;
        }

        public void UpdateState(double scrollableHeight, double verticalOffset, double viewportHeight)
        {
            canScroll = scrollableHeight > 0.5 && viewportHeight > 0;
            upButton.Visibility = canScroll ? Visibility.Visible : Visibility.Collapsed;
            downButton.Visibility = canScroll ? Visibility.Visible : Visibility.Collapsed;
            upButton.IsEnabled = canScroll && verticalOffset > 0.5;
            downButton.IsEnabled = canScroll && verticalOffset < scrollableHeight - 0.5;
        }

        private static Button CreateButton(string geometry, string toolTip)
        {
            var button = new Button
            {
                Content = new Path
                {
                    Data = Geometry.Parse(geometry),
                    Width = 20,
                    Height = 20,
                    Stretch = Stretch.Uniform,
                    StrokeThickness = 2.25,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round
                },
                Width = 42,
                Height = 42,
                MinWidth = 42,
                MinHeight = 42,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
                ToolTip = toolTip,
                Focusable = true,
                IsTabStop = false
            };
            System.Windows.Automation.AutomationProperties.SetName(button, toolTip);
            if (System.Windows.Application.Current?.TryFindResource("ScrollNavigationButton") is Style buttonStyle)
                button.Style = buttonStyle;
            button.SetResourceReference(Control.BackgroundProperty, "CardBackgroundBrush");
            button.SetResourceReference(Control.ForegroundProperty, "PrimaryTextBrush");
            if (button.Content is Path arrow)
                arrow.SetResourceReference(Shape.StrokeProperty, "PrimaryTextBrush");
            return button;
        }

        private static double GetFiniteDimension(double available, double actual) =>
            double.IsFinite(available) && available >= 0
                ? available
                : double.IsFinite(actual) && actual >= 0
                    ? actual
                    : 0;

        private void OnScrollUp(object sender, RoutedEventArgs e)
        {
            ScrollByPage(-1);
            e.Handled = true;
        }

        private void OnScrollDown(object sender, RoutedEventArgs e)
        {
            ScrollByPage(1);
            e.Handled = true;
        }

        private void ScrollByPage(int direction)
        {
            var delta = Math.Max(48, scrollViewer.ViewportHeight * 0.8);
            scrollViewer.ScrollToVerticalOffset(
                Math.Clamp(scrollViewer.VerticalOffset + (delta * direction), 0, scrollViewer.ScrollableHeight));
        }
    }
}
