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
    //只扫描下面的程序集
    Assembly assembly = Assembly.Load("StateMachine");
    Assembly assembly2 = Assembly.Load("StateMachine.FlowComponent");
    Assembly assembly3 = Assembly.Load("StateMachineDemoShared");
    //这里的assemblies需要覆盖所有包含实现了节点的程序集，以实现脚本自动构建节点
    Assembly[] assemblies = [Assembly.GetEntryAssembly(), assembly, assembly2, assembly3];
    //注册所有的Module
    containerBuilder.RegisterAssemblyModules(assemblies);
    //注册所有使用Component特性的类型
    //自定义的流程节点类型用基于Component特性的FSMNode特性修饰
    containerBuilder.RegisterModule(new AutofacAnnotationModule(assemblies)
        .SetAutoRegisterInterface(true)
        .SetAutoRegisterParentClass(false)
        .SetIgnoreAutoRegisterAbstractClass(true));

    containerBuilder.RegisterBuildCallback(c =>
    {
        //用一个全局静态的IoC类来获取IoC内部实例
        IoC.ContainerWrapper = new ContainerWrapper(c);
    });
}));

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMasaBlazor();

await builder.Build().RunAsync();