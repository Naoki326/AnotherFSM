using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using StateMachine;
using StateMachineDemoShared;
using StateMachineWebAssemblyDemo;
using System.Reflection;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.ConfigureContainer(new AutofacServiceProviderFactory((containerBuilder) =>
{
    //注册Module
    Assembly assembly1 = Assembly.Load("StateMachineDemoShared");
    Assembly assembly2 = Assembly.Load("StateMachine.FlowComponent");
    Assembly assembly3 = Assembly.Load("BaseNodes");
    containerBuilder.RegisterAssemblyModules([assembly1, assembly2, assembly3]);

    containerBuilder.RegisterBuildCallback(c =>
    {
        //用一个全局静态的IoC类来获取IoC内部实例
        //IoC.ContainerWrapper = new ContainerWrapper(c);
    });
}));
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMasaBlazor();

await builder.Build().RunAsync();