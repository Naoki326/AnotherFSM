using Microsoft.AspNetCore.SignalR;
using StateMachine;
using FSMDemo.API.Hubs;
using FSMDemo.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<IFSMNodeFactory, AssemblyScanningNodeFactory>();
builder.Services.AddSingleton<EngineManager>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(origin =>
              {
                  if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
                  return uri.Scheme == "http"
                         && (uri.Host == "localhost" || uri.Host == "127.0.0.1");
              })
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.MapControllers();
app.MapHub<ExecutionHub>("/hubs/execution");

// Wire EngineManager events to SignalR
var engineManager = app.Services.GetRequiredService<EngineManager>();
var hubContext = app.Services.GetRequiredService<IHubContext<ExecutionHub>>();

engineManager.StateChanged += async (state, currentNode) =>
{
    await hubContext.Clients.Group("Execution").SendAsync("StateChanged", new
    {
        state = state.ToString(),
        currentNode
    });
};

engineManager.NodeActivated += async (nodeName) =>
{
    await hubContext.Clients.Group("Execution").SendAsync("NodeActivated", nodeName);
};

engineManager.NodeDeactivated += async (nodeName) =>
{
    await hubContext.Clients.Group("Execution").SendAsync("NodeDeactivated", nodeName);
};

app.Run();
