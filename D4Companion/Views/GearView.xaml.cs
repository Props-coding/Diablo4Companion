using D4Companion.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows.Controls;

namespace D4Companion.Views
{
    public partial class GearView : UserControl
    {
        public GearView()
        {
            // Shared singleton, so every screen sees the same state.
            if (!DesignerProperties.GetIsInDesignMode(this))
            {
                DataContext = App.Current.Services.GetRequiredService<GearViewModel>();
            }

            InitializeComponent();
        }
    }
}
