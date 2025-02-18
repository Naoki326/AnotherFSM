using Autofac;
using BaseNodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StateMachine;
using StateMachineDemoShared;
using System.Reflection;
using Application = System.Windows.Application;

namespace StateMachineWPFDemo
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        [STAThread]
        static void Main(string[] args)
        {
            using IHost host = CreateHostBuilder(args).Build();
            host.Start();

            App app = new();
            app.InitializeComponent();

            app.MainWindow = host.Services.GetRequiredService<MainWindow>();
            app.MainWindow.Show();

            app.Run();
        }

        private static IHostBuilder CreateHostBuilder(string[] args)
        {

            return Host.CreateDefaultBuilder(args)
                .UseServiceProviderFactory(new Autofac.Extensions.DependencyInjection.AutofacServiceProviderFactory())
                .ConfigureContainer<ContainerBuilder>((context, containerBuilder) =>
                {
                    //注册Module
                    Assembly assembly1 = Assembly.Load("StateMachineDemoShared");
                    Assembly assembly2 = Assembly.Load("StateMachine.FlowComponent");
                    Assembly assembly3 = Assembly.Load("BaseNodes");
                    containerBuilder.RegisterAssemblyModules([assembly1, assembly2, assembly3]);
                })
                .ConfigureAppConfiguration((context, configBuilder) =>
                {
                    configBuilder
                        .SetBasePath(context.HostingEnvironment.ContentRootPath)
                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                        .AddJsonFile("appsettings.development.json", optional: true, reloadOnChange: false)
                        .AddEnvironmentVariables()
                        .AddCommandLine(args);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddWpfBlazorWebView();

                    services.AddSingleton(serviceProvider => new MainWindow() { ServiceProvider = serviceProvider });
                    services.AddMasaBlazor();
                });
        }
    }

}
