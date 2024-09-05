using System.Windows;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using StateMachineDemoShared;

namespace StateMachineWPFDemo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            BlazorWebView blazorView = new()
            {
                Name = "blazorView",
                HostPage = "wwwroot\\index.html",
                StartPath = "/",
                Services = ServiceProvider,
            };
            blazorView.RootComponents.Add(new RootComponent()
            {
                Selector = "#main",
                ComponentType = typeof(Main),
            });

            AddChild(blazorView);
        }

        public IServiceProvider ServiceProvider { get; set; } = default!;
    }
}