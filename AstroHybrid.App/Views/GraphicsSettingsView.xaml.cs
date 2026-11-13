using AstroHybrid.App.ViewModels;
using System.Windows.Controls;

namespace AstroHybrid.App.Views
{
    public partial class GraphicsSettingsView : UserControl
    {
        public GraphicsSettingsView()
        {
            // Built before the controls, so every binding sees this page's view model at once.
            if (MainWindowViewModel.Current is MainWindowViewModel main)
            {
                DataContext = new GraphicsSettingsViewModel(main);
            }
            InitializeComponent();
        }
    }
}