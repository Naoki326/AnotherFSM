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

// ��Autofac��������񼯳�
builder.Host
    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
    .ConfigureContainer<ContainerBuilder>(containerBuilder =>
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
