using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;


namespace ServerlessImageProcessor.Functions
{
    public class OrchestratorInput
    {
        public string FileName { get; set; } = string.Empty;
        public byte[] ImageBytes { get; set; } = Array.Empty<byte>();
    }

    public class DeadLetterMessage
    {
        public string FileName { get; set; } = string.Empty;
        public string FailedStep { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime FailedAtUtc { get; set; }
    }
    
    public static class ImageProcessingOrchestration
    {
        [Function(nameof(ImageProcessingOrchestrator))]
        public static async Task ImageProcessingOrchestrator(
            [OrchestrationTrigger] TaskOrchestrationContext context
        )
        {
            var input = context.GetInput<OrchestratorInput>()
            ?? throw new InvalidOperationException("Orchestrator received no input.");

            var retryOptions = TaskOptions.FromRetryPolicy(new RetryPolicy(
                maxNumberOfAttempts: 3,
                firstRetryInterval: TimeSpan.FromSeconds(2),
                backoffCoefficient: 2.0
            ));

            string currentStep = "ResizeAndWatermark";
            try
            {
                var resizeResult = await context.CallActivityAsync<ResizeResult>(nameof(PipelineActivities.ResizeAndWatermarkActivity), input.ImageBytes, retryOptions);

                currentStep = "DetectObjects";
                var  detectedObjects = await context.CallActivityAsync<List<DetectedObject>>(nameof(PipelineActivities.DetectObjectsActivity), input.ImageBytes, retryOptions);

                currentStep = "WriteProcessedBlob";
                await context.CallActivityAsync(
                    nameof(PipelineActivities.WriteProcessedBlobActivity),
                    new WriteProcessedBlobInput { FileName = input.FileName, ProcessedBytes = resizeResult.ProcessedBytes },
                    retryOptions
                );

                currentStep = "WriteMetadata";
                await context.CallActivityAsync(
                    nameof(PipelineActivities.WriteMetadataActivity),
                    new WriteMetadataInput
                    {
                        FileName = input.FileName,
                        OriginalWidth = resizeResult.OriginalWidth,
                        OriginalHeight = resizeResult.OriginalHeight,
                        NewWidth = resizeResult.NewWidth,
                        NewHeight = resizeResult.NewHeight,
                        OriginalSizeBytes = input.ImageBytes.Length,
                        ProcessedSizeBytes = resizeResult.ProcessedBytes.Length,
                        DetectedObjects = detectedObjects
                    },
                    retryOptions);
            }
            catch (Exception ex)
            {
                //All retries exhausted on whichever step failed - push to dead-letter instead of losing it silently.
                await context.CallActivityAsync(nameof(PipelineActivities.PushToDeadLetterActivity),new DeadLetterMessage
                {
                    FileName = input.FileName,
                    FailedStep = currentStep,
                    ErrorMessage = ex.Message,
                    FailedAtUtc = context.CurrentUtcDateTime
                });
            }
        }
    }
}