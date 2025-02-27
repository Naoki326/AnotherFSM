using System.Reflection;
using Autofac;
using DemoNodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StateMachine;
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
                    Assembly assembly3 = Assembly.Load("DemoNodes");
                    containerBuilder.RegisterAssemblyModules([assembly1, assembly2, assembly3]);

                    // 注册一个AutofacNodeFactory为单例，构造FSMEngine时可选该对象为参数
                    containerBuilder.RegisterType<AutofacNodeFactory>().As<IFSMNodeFactory>().SingleInstance();

                    // 手动注入GroupNode和ParallelNode
                    if (typeof(GroupNode).GetCustomAttribute(typeof(FSMNodeAttribute)) is FSMNodeAttribute attr)
                    {
                        containerBuilder.RegisterType<GroupNode>().Keyed<IFSMNode>(attr.Key);
                    }
                    if (typeof(ParallelNode).GetCustomAttribute(typeof(FSMNodeAttribute)) is FSMNodeAttribute attr2)
                    {
                        containerBuilder.RegisterType<ParallelNode>().Keyed<IFSMNode>(attr2.Key);
                    }

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
