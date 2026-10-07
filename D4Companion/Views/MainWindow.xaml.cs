using D4Companion.State;
using D4Companion.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace D4Companion.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private readonly Dictionary<CompanionPage, FrameworkElement> _pages = new();
        private readonly Dictionary<CompanionPage, RadioButton> _navButtons = new();

        private bool _isCompactMode = false;
        private Rect _fullModeBounds = Rect.Empty;
        private CompactAdvisorView? _compactView;

        public MainWindow()
        {
            bool isDesignMode = DesignerProperties.GetIsInDesignMode(this);

            // Only set DataContext when not in Design-mode
            if (!isDesignMode)
            {
                DataContext = App.Current.Services.GetRequiredService<MainWindowViewModel>();
            }

            InitializeComponent();

            if (isDesignMode) return;

            // Create the original pages up front, in the same order as before, so their
            // view models start listening for hotkeys and overlay messages at launch.
            _pages[CompanionPage.Builds] = new BuildsView();
            _pages[CompanionPage.Trading] = new TradeView();
            _pages[CompanionPage.Logging] = new LoggingView();
            _pages[CompanionPage.Debug] = new DebugView();
            _pages[CompanionPage.Settings] = new SettingsView();
            _pages[CompanionPage.Overview] = new OverviewView();
            _pages[CompanionPage.Gear] = new GearView();
            _pages[CompanionPage.Crafting] = new CraftingView();

            LoggingBadge.DataContext = _pages[CompanionPage.Logging].DataContext;

            _navButtons[CompanionPage.Overview] = NavOverview;
            _navButtons[CompanionPage.Gear] = NavGear;
            _navButtons[CompanionPage.Crafting] = NavCrafting;
            _navButtons[CompanionPage.Builds] = NavBuilds;
            _navButtons[CompanionPage.Settings] = NavSettings;
            _navButtons[CompanionPage.Trading] = NavTrading;
            _navButtons[CompanionPage.Logging] = NavLogging;
            _navButtons[CompanionPage.Debug] = NavDebug;

            var state = App.Current.Services.GetRequiredService<CompanionState>();
            state.NavigationRequested += (s, page) => Navigate(page);

            Navigate(CompanionPage.Overview);
        }

        public void Navigate(CompanionPage page)
        {
            if (_isCompactMode) ToggleCompactMode();

            if (_navButtons.TryGetValue(page, out var button) && button.IsChecked != true)
            {
                // Checking the button raises NavButton_Checked, which shows the page.
                button.IsChecked = true;
                return;
            }

            ShowPage(page);
        }

        private void ShowPage(CompanionPage page)
        {
            if (_pages.TryGetValue(page, out var view))
            {
                PageHost.Content = view;
            }
        }

        private void NavButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: string tag } && Enum.TryParse(tag, out CompanionPage page))
            {
                ShowPage(page);
            }
        }

        private void CompactModeButton_Click(object sender, RoutedEventArgs e) => ToggleCompactMode();

        private void ToggleCompactMode()
        {
            _isCompactMode = !_isCompactMode;

            if (_isCompactMode)
            {
                _fullModeBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;

                _compactView ??= new CompactAdvisorView();
                CompactHost.Content = _compactView;
                FullLayout.Visibility = Visibility.Collapsed;
                CompactLayout.Visibility = Visibility.Visible;
                CompactModeButtonText.Text = "Full window";

                Width = 440;
                Height = Math.Min(Height, 820);
                ApplyCompactTopMost();
            }
            else
            {
                CompactLayout.Visibility = Visibility.Collapsed;
                FullLayout.Visibility = Visibility.Visible;
                CompactModeButtonText.Text = "Compact mode";

                if (!_fullModeBounds.IsEmpty)
                {
                    Left = _fullModeBounds.Left;
                    Top = _fullModeBounds.Top;
                    Width = _fullModeBounds.Width;
                    Height = _fullModeBounds.Height;
                }

                // Hand Topmost back to the user's setting (keeps the binding intact).
                GetBindingExpression(TopmostProperty)?.UpdateTarget();
            }
        }

        private void CompactTopMost_Changed(object sender, RoutedEventArgs e) => ApplyCompactTopMost();

        private void ApplyCompactTopMost()
        {
            if (!_isCompactMode) return;
            SetCurrentValue(TopmostProperty, CompactTopMost.IsChecked == true);
        }
    }
}
