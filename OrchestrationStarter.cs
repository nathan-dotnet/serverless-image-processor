
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace ServerlessImageProcessor.Functions
{
    public class OrchestrationStarter
    {
        private readonly ILogger<OrchestrationStarter> _logger;

        public OrchestrationStarter(ILogger<OrchestrationStarter> logger)
        {
            _logger = logger;
        }

        [Function(nameof(StartImageProcessing))]
        public async Task StartImageProcessing(
            [BlobTrigger("uploads-v2/{name}")] byte[] imageBytes,
            string name,
            [DurableClient] DurableTaskClient client)
        {
            string instanceId = await client.ScheduleNewOrchestrationInstanceAsync(nameof(ImageProcessingOrchestration.ImageProcessingOrchestrator),
            new OrchestratorInput { FileName = name, ImageBytes = imageBytes });

            _logger.LogInformation("Started orchestration {InstanceId} for {FileName}", instanceId, name);
        }
    }
}