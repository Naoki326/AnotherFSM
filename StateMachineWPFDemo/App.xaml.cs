using Autofac;
using Autofac.Annotation;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Authorization;
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
                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .ConfigureContainer<ContainerBuilder>((context, containerBuilder) =>
                {
                    Assembly assembly = Assembly.Load("StateMachine");
                    Assembly assembly2 = Assembly.Load("StateMachine.FlowComponent");
                    Assembly assembly3 = Assembly.Load("StateMachineDemoShared");
                    Assembly[] assemlies = [Assembly.GetEntryAssembly(), assembly, assembly2, assembly3];
                    //注册所有的Module
                    containerBuilder.RegisterAssemblyModules(assemlies);
                    //注册所有包含Component特性的类型
                    //包括自定义的流程节点
                    containerBuilder.RegisterModule(new AutofacAnnotationModule(assemlies)
                        .SetAutoRegisterInterface(true)
                        .SetAutoRegisterParentClass(false)
                        .SetIgnoreAutoRegisterAbstractClass(true));

                    containerBuilder.RegisterBuildCallback(c =>
                    {
                        //配置默认的全局IoC实例
                        IoC.ContainerWrapper = new ContainerWrapper(c);
                    });
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

                    services.AddCascadingAuthenticationState();
                    services.AddAuthorizationCore();
                    services.AddScoped<AuthenticationStateProvider, WholeAuthStateProvider>();
                    services.AddMasaBlazor();
                });
        }
    }

}
