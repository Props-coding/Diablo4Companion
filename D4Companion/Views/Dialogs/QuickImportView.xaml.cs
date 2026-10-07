using D4Companion.ViewModels.Dialogs;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using System;
using System.Windows;
using System.Windows.Controls;

namespace D4Companion.Views.Dialogs
{
    public partial class QuickImportView : UserControl
    {
        public QuickImportView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (_, _) => LinkBox.Focus();
        }

        /// <summary>True when the player chose "Other options" and wants the full import window.</summary>
        public bool OtherOptionsRequested { get; private set; }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is QuickImportViewModel oldViewModel) oldViewModel.AddCompleted -= OnAddCompleted;
            if (e.NewValue is QuickImportViewModel newViewModel) newViewModel.AddCompleted += OnAddCompleted;
        }

        private void OnAddCompleted(object? sender, EventArgs e) => Close();

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void OtherOptions_Click(object sender, RoutedEventArgs e)
        {
            OtherOptionsRequested = true;
            Close();
        }

        private async void Close()
        {
            var dialog = this.TryFindParent<BaseMetroDialog>();
            if (dialog != null && Application.Current.MainWindow is MetroWindow window)
            {
                await window.HideMetroDialogAsync(dialog);
            }
        }
    }
}
