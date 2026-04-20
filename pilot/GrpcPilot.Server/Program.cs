using GrpcPilot.Server.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to listen on a fixed port with HTTP/2 (required for gRPC)
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5050, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

builder.Services.AddGrpc();

var app = builder.Build();
app.MapGrpcService<GreeterService>();
app.Run();
