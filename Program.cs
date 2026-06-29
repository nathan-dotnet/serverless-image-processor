using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServerlessImageProcessor.Functions;
using System.IO;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

string keyVaultUri = Environment.GetEnvironmentVariable("KeyVaultUri") 
    ?? throw new InvalidOperationException("KeyVaultUri is not configured.");

var credential = new DefaultAzureCredential();
var secretClient = new SecretClient(new Uri(keyVaultUri), credential);
string cosmosConnectionString = secretClient.GetSecret("CosmosDbConnectionString").Value.Value;

builder.Services.AddSingleton(s =>
    new CosmosClient(
        cosmosConnectionString,
        new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway
        }));

builder.Services.AddSingleton(s =>
    new ObjectDetector(Path.Combine(AppContext.BaseDirectory, "Models", "ssd_mobilenet_v1_12.onnx")));

builder.Build().Run();