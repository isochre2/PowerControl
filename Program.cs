using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using PowerControl;
using PowerControl.Services;
using System;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();

builder.Services.AddSingleton<ControlWorker>();
builder.Services.AddSingleton<ShutdownWorker>();

builder.Services.AddHostedService(provider => provider.GetRequiredService<ControlWorker>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<ShutdownWorker>());
builder.Services.AddSingleton(serviceProvider => new ControlService(serviceProvider.GetRequiredService<ILogger<ControlService>>(), serviceProvider));

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<ControlService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
