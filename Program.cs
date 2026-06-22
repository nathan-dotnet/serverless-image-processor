using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddSingleton(s =>
    new CosmosClient(
        Environment.GetEnvironmentVariable("CosmosDbConnectionString"),
        new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway
        }));
        
builder.Build().Run();