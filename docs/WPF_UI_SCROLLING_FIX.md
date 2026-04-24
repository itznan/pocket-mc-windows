# WPF-UI NavigationView Scrolling Fix

This document explains the common issue with mouse wheel scrolling when using `Wpf.Ui.Controls.NavigationView` and how to implement a robust, page-level fix. This pattern is successfully used in `ServerSettingsPage` and `AppSettingsPage`.

## The Problem

When using WPF-UI's `NavigationView` to host pages, you might encounter an issue where the mouse wheel does not scroll the `ScrollViewer` inside your page. This happens due to two interacting factors:

1. **Child Controls Consume Events:** Elements like `CardExpander` or `ListView` might mark the `PreviewMouseWheel` or `MouseWheel` event as `Handled = true`, preventing it from bubbling up to your page's `ScrollViewer`.
2. **Infinite Height from NavigationView:** The `NavigationView` itself encapsulates the hosted page within its own internal `ScrollViewer`. Because of this, the `Page` is given an "infinite" available height to grow into. When your page's internal `ScrollViewer` (e.g., `MainScrollViewer`) is given infinite height, its `ScrollableHeight` becomes `0` (because it doesn't need to scroll its content; it can just expand). Therefore, any custom logic trying to scroll `MainScrollViewer` fails because `ScrollableHeight <= 0`.

## The Solution

To fix this reliably, we use a two-part pattern: **Disable the Parent ScrollViewer** and **Aggressively Intercept Mouse Wheel Events**.

### Step 1: Disable the Parent ScrollViewer

When the page loads, we must walk up the visual tree and disable the scrollbars on any parent `ScrollViewer` (specifically, the one injected by `NavigationView`). This forces the outer container to constrain the height of our page, meaning our local `MainScrollViewer` actually gets a constrained height and a positive `ScrollableHeight`.

```csharp
private void DisableParentScrollViewer(DependencyObject obj)
{
    var parent = VisualTreeHelper.GetParent(obj);
    while (parent != null)
    {
        if (parent is ScrollViewer sv)
        {
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
        parent = VisualTreeHelper.GetParent(parent);
    }
}
```

### Step 2: Aggressively Intercept Mouse Wheel Events

We must attach a page-level event handler for `PreviewMouseWheel` using `AddHandler` with `handledEventsToo = true`. This is the *only* way in WPF to catch an event after a child control has already marked it as `Handled`.

```csharp
private readonly MouseWheelEventHandler _previewMouseWheelHandler;
private bool _isForwardingMouseWheel;

public MyPage()
{
    InitializeComponent();
    _previewMouseWheelHandler = OnPagePreviewMouseWheel;
}

private void MyPage_Loaded(object sender, RoutedEventArgs e)
{
    // 1. Catch wheel events even when internal WPF-UI controls consume them
    AddHandler(UIElement.PreviewMouseWheelEvent, _previewMouseWheelHandler, true);
    
    // 2. Disable the NavigationView's internal ScrollViewer
    DisableParentScrollViewer(this);
}

private void MyPage_Unloaded(object sender, RoutedEventArgs e)
{
    RemoveHandler(UIElement.PreviewMouseWheelEvent, _previewMouseWheelHandler);
}
```

### Step 3: Implement the Scroll Handler

The handler must smartly decide when to scroll our main scroll viewer and when to let the child control handle it (e.g., if the user is scrolling an open dropdown or a text box).

```csharp
private void OnPagePreviewMouseWheel(object sender, MouseWheelEventArgs e)
{
    if (_isForwardingMouseWheel || e.OriginalSource is not DependencyObject source)
        return;

    // 1. Never intercept if a ScrollBar thumb is being dragged
    if (FindAncestor<ScrollBar>(source) != null)
        return;

    // 2. Skip if inside an OPEN ComboBox dropdown (let it scroll its own list)
    var comboBox = FindAncestor<ComboBox>(source);
    if (comboBox?.IsDropDownOpen == true)
        return;

    // 3. Skip if inside a Popup (ComboBox dropdown popup, tooltip, etc.)
    if (FindAncestor<Popup>(source) != null)
        return;

    // 4. Forward the scroll to MainScrollViewer
    if (MainScrollViewer == null || MainScrollViewer.ScrollableHeight <= 0)
        return;

    e.Handled = true;

    try
    {
        _isForwardingMouseWheel = true;
        
        // Scroll by 3 lines per notch for a responsive feel
        int steps = Math.Max(1, Math.Abs(e.Delta) / Mouse.MouseWheelDeltaForOneLine) * 3;
        for (int i = 0; i < steps; i++)
        {
            if (e.Delta > 0)
                MainScrollViewer.LineUp();
            else
                MainScrollViewer.LineDown();
        }
    }
    finally
    {
        _isForwardingMouseWheel = false;
    }
}

private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
{
    while (current != null)
    {
        if (current is T match)
            return match;
        DependencyObject? visualParent = null;
        try { visualParent = VisualTreeHelper.GetParent(current); } catch { }
        current = visualParent ?? LogicalTreeHelper.GetParent(current);
    }
    return null;
}
```

## Why Window-Level Hooks Fail

Previous attempts tried to use a Window-level hook (e.g., handling `PreviewMouseWheel` in `MainWindow.xaml.cs`) combined with `FindVisualChild<ScrollViewer>`. 

This approach is flawed because `FindVisualChild` performs a depth-first search. In a complex WPF-UI template, the first `ScrollViewer` it finds is often an internal one (like the one inside `NavigationView` or `CardExpander`), **not** the `MainScrollViewer` of the page. By handling it at the `Page` level, we can directly reference our named `MainScrollViewer` and avoid visual tree searching ambiguities.
