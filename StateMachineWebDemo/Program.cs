using Autofac;
using Autofac.Annotation;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using StateMachine;
using StateMachineDemoShared;
using StateMachineWebDemo;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// 将Autofac容器与服务集成
builder.Host
    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
    .ConfigureContainer<ContainerBuilder>(containerBuilder =>
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
        services.AddCascadingAuthenticationState();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.AccessDeniedPath = "/Account/Deny";
                options.LoginPath = "/";
            });
        services.AddAuthorizationCore();
        services.AddScoped<AuthenticationStateProvider, WholeAuthStateProvider>();
        services.AddMasaBlazor();

        services.AddRazorComponents().AddInteractiveServerComponents();
    });


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStaticFiles();

app.UseRouting();


app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddAdditionalAssemblies([typeof(Main).Assembly])
    .AddInteractiveServerRenderMode();

app.Run();
