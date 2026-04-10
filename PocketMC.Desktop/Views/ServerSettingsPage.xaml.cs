using System.Windows.Controls;
using System.Windows.Input;
using PocketMC.Desktop.ViewModels;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;

namespace PocketMC.Desktop.Views
{
    public partial class ServerSettingsPage : Page
    {
        public ServerSettingsViewModel ViewModel { get; }
        private readonly MouseWheelEventHandler _previewMouseWheelHandler;

        public ServerSettingsPage(ServerSettingsViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = ViewModel;
            _previewMouseWheelHandler = OnSettingsPagePreviewMouseWheel;

            // Optional UI logic for tab synchronization and animations can remain here
            Loaded += ServerSettingsPage_Loaded;
            Unloaded += ServerSettingsPage_Unloaded;
            MainTabControl.SelectionChanged += MainTabControl_SelectionChanged;
        }

        private void ServerSettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            AddHandler(UIElement.PreviewMouseWheelEvent, _previewMouseWheelHandler, true);
            QueueTabTransitionAnimation();
        }

        private void ServerSettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            RemoveHandler(UIElement.PreviewMouseWheelEvent, _previewMouseWheelHandler);
            ViewModel.Dispose();
        }

        private bool _isSynchronizingTabSelection;

        private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSynchronizingTabSelection) return;

            if (SidebarList != null && MainTabControl != null && SidebarList.SelectedIndex != -1)
            {
                MainTabControl.SelectedIndex = SidebarList.SelectedIndex;
            }
        }

        private void MainTabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.Source is not TabControl) return;

            if (SidebarList.SelectedIndex != MainTabControl.SelectedIndex)
            {
                _isSynchronizingTabSelection = true;
                SidebarList.SelectedIndex = MainTabControl.SelectedIndex;
                _isSynchronizingTabSelection = false;
            }

            QueueTabTransitionAnimation();
        }

        private void QueueTabTransitionAnimation()
        {
            if (!IsLoaded) return;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new System.Action(AnimateContentAreaTransition));
        }

        private void AnimateContentAreaTransition()
        {
            if (ContentAreaCard == null) return;

            ContentAreaCard.BeginAnimation(OpacityProperty, null);
            if (ContentAreaCard.RenderTransform is System.Windows.Media.TranslateTransform translateTransform)
            {
                translateTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
                translateTransform.Y = 8;
                translateTransform.BeginAnimation(
                    System.Windows.Media.TranslateTransform.YProperty,
                    new DoubleAnimation(8, 0, System.TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }

            ContentAreaCard.Opacity = 0;
            ContentAreaCard.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, System.TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
        }

        private bool _isForwardingMouseWheel;
        private void OnSettingsPagePreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Similar logic from previous file to forward scroll events
            if (_isForwardingMouseWheel || e.OriginalSource is not DependencyObject source) return;

            if (FindAncestor<ScrollBar>(source) != null) return;
            ComboBox? comboBox = FindAncestor<ComboBox>(source);
            if (comboBox?.IsDropDownOpen == true) return;

            ScrollViewer? activeScrollViewer = GetActiveTabScrollViewer();
            if (activeScrollViewer == null || activeScrollViewer.ScrollableHeight <= 0) return;

            e.Handled = true;

            try
            {
                _isForwardingMouseWheel = true;
                int steps = System.Math.Max(1, System.Math.Abs(e.Delta) / Mouse.MouseWheelDeltaForOneLine) * 3;
                for (int i = 0; i < steps; i++)
                {
                    if (e.Delta > 0) activeScrollViewer.LineUp();
                    else activeScrollViewer.LineDown();
                }
            }
            finally
            {
                _isForwardingMouseWheel = false;
            }
        }

        private ScrollViewer? GetActiveTabScrollViewer()
        {
            return MainTabControl.SelectedIndex switch
            {
                0 => PropertiesScrollViewer,
                1 => WorldsScrollViewer,
                2 => PluginsScrollViewer,
                3 => ModsScrollViewer,
                4 => BackupsScrollViewer,
                5 => CrashRestartScrollViewer,
                _ => null
            };
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match) return match;
                DependencyObject? visualParent = null;
                try { visualParent = System.Windows.Media.VisualTreeHelper.GetParent(current); } catch { }
                current = visualParent ?? LogicalTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
