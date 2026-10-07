using D4Companion.Interfaces;
using D4Companion.State;
using D4Companion.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows.Controls;

namespace D4Companion.Views
{
    public partial class BuildsView : UserControl
    {
        public BuildsView()
        {
            InitializeComponent();

            if (DesignerProperties.GetIsInDesignMode(this)) return;

            // The original editor stays in the page so its import dialog, hotkeys and overlay messages keep working.
            var editor = new AffixView();
            AdvancedEditorHost.Content = editor;

            var services = App.Current.Services;
            DataContext = new BuildsViewModel(
                services.GetRequiredService<IAffixManager>(),
                services.GetRequiredService<CompanionState>(),
                (AffixViewModel)editor.DataContext);
        }
    }
}
