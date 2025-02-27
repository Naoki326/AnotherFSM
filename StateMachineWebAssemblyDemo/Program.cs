using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using DemoNodes;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using StateMachine;
using StateMachineWebAssemblyDemo;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.ConfigureContainer(new AutofacServiceProviderFactory((containerBuilder) =>
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

}));
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMasaBlazor();

await builder.Build().RunAsync();