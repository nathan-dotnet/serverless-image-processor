using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServerlessImageProcessor.Functions;
using System.IO;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddSingleton(s =>
    new CosmosClient(
        Environment.GetEnvironmentVariable("CosmosDbConnectionString"),
        new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway
        }));

builder.Services.AddSingleton(s =>
    new ObjectDetector(Path.Combine(AppContext.BaseDirectory, "Models", "ssd_mobilenet_v1_12.onnx")));

builder.Build().Run();