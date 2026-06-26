using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SkiaSharp;

namespace ServerlessImageProcessor.Functions
{
    public class ResizeResult
    {
        [JsonProperty("ProcessedBytes")]
        public byte[] ProcessedBytes { get; set; } = Array.Empty<byte>();

        [JsonProperty("OriginalWidth")]
        public int OriginalWidth { get; set; }

        [JsonProperty("OriginalHeight")]
        public int OriginalHeight { get; set; }

        [JsonProperty("NewWidth")]
        public int NewWidth { get; set; }

        [JsonProperty("NewHeigth")]
        public int NewHeight { get; set; }
    }

    public class WriteMetadataInput
    {
        public string FileName { get; set; } = string.Empty;
        public int OriginalWidth { get; set; }
        public int OriginalHeight { get; set; }
        public int NewWidth { get; set; }
        public int NewHeight { get; set; }
        public int OriginalSizeBytes { get; set; }
        public int ProcessedSizeBytes { get; set; }
        public List<DetectedObject> DetectedObjects { get; set; } = new();
    }

    public class WriteProcessedBlobInput
    {
        public string FileName { get; set; } = string.Empty;
        public byte[] ProcessedBytes { get; set; } = Array.Empty<byte>();
    }

    public class PipelineActivities
    {
        private readonly ILogger<PipelineActivities> _logger;
        private readonly CosmosClient _cosmosClient;
        private readonly ObjectDetector _objectDetector;
        private const string DatabaseName = "ImageProcessingDb";
        private const string ContainerName = "ImageMetadata";
        private const int MaxWidth = 800;
        private const string WatermarkText = "(c) ServerlessImageProcessor";

        public PipelineActivities(ILogger<PipelineActivities> logger, CosmosClient cosmosClient, ObjectDetector objectDetector)
        {
            _logger = logger;
            _cosmosClient = cosmosClient;
            _objectDetector = objectDetector;
        }

        [Function(nameof(ResizeAndWatermarkActivity))]
        public ResizeResult ResizeAndWatermarkActivity([ActivityTrigger] byte[]imageBytes)
        {
            using var original = SKBitmap.Decode(imageBytes);
            if (original == null)
                throw new InvalidOperationException("Could not decode image.");

            int originalWidth = original.Width;
            int originalHeight = original.Height;

            float scale = Math.Min(1f, (float)MaxWidth / original.Width);
            int newWidth = (int)(original.Width * scale);
            int newHeight = (int)(original.Height * scale);

            using var resized = original.Resize(new SKImageInfo(newWidth, newHeight),SKSamplingOptions.Default);
            if (resized == null)
                throw new InvalidOperationException("Resize failed.");

            using var surface = SKSurface.Create(new SKImageInfo(newWidth, newHeight));

            var canvas = surface.Canvas;
            canvas.DrawBitmap(resized, 0, 0 );

            using var paint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(180),
                IsAntialias = true
            };
            using var font = new SKFont(SKTypeface.Default, Math.Max(14, newWidth / 25));

            font.MeasureText(WatermarkText, out SKRect textBounds, paint);
            float x = newWidth - textBounds.Width - 12;
            float y = newHeight - 12; 
            canvas.DrawText(WatermarkText, x, y, SKTextAlign.Left, font, paint);

            using var snapshot = surface.Snapshot();
            using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, 85);

            return new ResizeResult
            {
                ProcessedBytes = encoded.ToArray(),
                OriginalWidth = originalWidth,
                OriginalHeight = originalHeight,
                NewWidth = newWidth,
                NewHeight = newHeight
            };
        }

        [Function(nameof(DetectObjectsActivity))]
        public List<DetectedObject> DetectObjectsActivity([ActivityTrigger] byte[] imageBytes)
        {
            using var bitmap = SKBitmap.Decode(imageBytes);
            if (bitmap == null)
                throw new InvalidOperationException("Could not decode image for detection.");

            return _objectDetector.Detect(bitmap);
        }

        [Function(nameof(WriteProcessedBlobActivity))]
        public async Task WriteProcessedBlobActivity([ActivityTrigger] WriteProcessedBlobInput input)
        {
            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";
            var blobServiceClient = new BlobServiceClient(connectionString);
            var containerClient =  blobServiceClient.GetBlobContainerClient("processed-images");
            await containerClient.CreateIfNotExistsAsync();

            var blobClient = containerClient.GetBlobClient(input.FileName);
            using var stream = new MemoryStream(input.ProcessedBytes);
            await blobClient.UploadAsync(stream, overwrite: true);
        }

        [Function(nameof(WriteMetadataActivity))]
        public async Task WriteMetadataActivity([ActivityTrigger] WriteMetadataInput input)
        {   
            var container = _cosmosClient.GetContainer(DatabaseName, ContainerName);

            var record = new ImageMetadata
            {
                Id = Guid.NewGuid().ToString(),
                FileName = input.FileName,
                ProcessedAtUtc = DateTime.UtcNow,
                OriginalWidth = input.OriginalWidth,
                OriginalHeight = input.OriginalHeight,
                ProcessedWidth = input.NewWidth,
                ProcessedHeight = input.NewHeight,
                OriginalSizeBytes = input.OriginalSizeBytes,
                ProcessedSizeBytes = input.ProcessedSizeBytes,
                DetectedObjects = input.DetectedObjects
            };

            await container.CreateItemAsync(record, new PartitionKey(record.FileName));
            _logger.LogInformation("Wrote metadata for {FileName} to Cosmos DB", input.FileName);
        }

        [Function(nameof(PushToDeadLetterActivity))]
        public async Task PushToDeadLetterActivity([ActivityTrigger] DeadLetterMessage message)
        {
            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";
            var queueServiceClient = new Azure.Storage.Queues.QueueServiceClient(connectionString);
            var queueClient = queueServiceClient.GetQueueClient("image-processing-deadletter");
            await queueClient.CreateIfNotExistsAsync();

            string json = JsonConvert.SerializeObject(message);
            await queueClient.SendMessageAsync(json);

            _logger.LogWarning("Moved {FileName} to dead-letter queue after failing at step {Step}: {Error}",
                message.FileName, message.FailedStep, message.ErrorMessage);


        }


    }
}