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
    //ע�����е�Module
    containerBuilder.RegisterAssemblyModules(assemlies);
    //ע�����а���Component���Ե�����
    //�����Զ�������̽ڵ�
    containerBuilder.RegisterModule(new AutofacAnnotationModule(assemlies)
        .SetAutoRegisterInterface(true)
        .SetAutoRegisterParentClass(false)
        .SetIgnoreAutoRegisterAbstractClass(true));

    containerBuilder.RegisterBuildCallback(c =>
    {
        //����Ĭ�ϵ�ȫ��IoCʵ��
        IoC.ContainerWrapper = new ContainerWrapper(c);
    });
}));

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMasaBlazor();

await builder.Build().RunAsync();