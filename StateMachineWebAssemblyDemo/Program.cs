using Autofac;
using Autofac.Annotation;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using StateMachine;
using StateMachineDemoShared;
using StateMachineWebAssemblyDemo;
using System.Reflection;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.ConfigureContainer(new AutofacServiceProviderFactory((containerBuilder) =>
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
}));

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMasaBlazor();

await builder.Build().RunAsync();