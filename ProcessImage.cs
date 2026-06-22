using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using SkiaSharp;

namespace ServerlessImageProcessor.Functions
{
    public class ProcessImage
    {
        private readonly ILogger<ProcessImage> _logger;
        private readonly CosmosClient _cosmosClient;
        private readonly ObjectDetector _objectDetector;
        private const string DatabaseName = "ImageProcessingDb";
        private const string ContainerName = "ImageMetadata";
        private const int MaxWidth = 800;
        private const string WatermarkText = "(c) ServerlessImageProcessor";

        public ProcessImage(ILogger<ProcessImage> logger, CosmosClient cosmosClient, ObjectDetector objectDetector)
        {
            _logger = logger;
            _cosmosClient = cosmosClient;
            _objectDetector = objectDetector;
        }

        // Triggers when a blob lands in the "uploads" container.
        // Output binding writes the processed result straight to "processed-images".
        [Function("ProcessImage")]
        [BlobOutput("processed-images/{name}")]
        public async Task<byte[]> Run(
            [BlobTrigger("uploads/{name}", Connection = "AzureWebJobsStorage")] byte[] imageBytes,
            string name)
        {
            _logger.LogInformation("Processing blob: {Name}, Size: {Size} bytes", name, imageBytes.Length);

            using var original = SKBitmap.Decode(imageBytes);
            if (original == null)
            {
                _logger.LogError("Could not decode image: {Name}", name);
                throw new InvalidOperationException($"Unsupported or corrupt image: {name}");
            }

            int originalWidth = original.Width;
            int originalHeight = original.Height;

            // Run object detection on the original decoded image, before resizing/watermarking.
            List<DetectedObject> detectedObjects = _objectDetector.Detect(original);
            _logger.LogInformation("Detected {Count} object(s) in {Name}", detectedObjects.Count, name);

            // Resize, preserving aspect ratio. Skip upscaling if already small.
            float scale = Math.Min(1f, (float)MaxWidth / original.Width);
            int newWidth = (int)(original.Width * scale);
            int newHeight = (int)(original.Height * scale);

            using var resized = original.Resize(new SKImageInfo(newWidth, newHeight), SKSamplingOptions.Default);
            if (resized == null)
            {
                throw new InvalidOperationException($"Resize failed for: {name}");
            }

            // Draw the resized bitmap onto a canvas, then add a watermark on top.
            using var surface = SKSurface.Create(new SKImageInfo(newWidth, newHeight));
            var canvas = surface.Canvas;
            canvas.DrawBitmap(resized, 0, 0);

            using var font = new SKFont(SKTypeface.Default, Math.Max(14, newWidth / 25));
            using var paint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(180),
                IsAntialias = true
            };

            font.MeasureText(WatermarkText, out SKRect textBounds, paint);
            float x = newWidth - textBounds.Width - 12;
            float y = newHeight - 12;
            canvas.DrawText(WatermarkText, x, y, SKTextAlign.Left, font, paint);

            using var snapshot = surface.Snapshot();
            using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, 85);
            byte[] processedBytes = encoded.ToArray();

            await WriteMetadataAsync(name, originalWidth, originalHeight, newWidth, newHeight,
                imageBytes.Length, processedBytes.Length, detectedObjects);

            return processedBytes;
        }

        private async Task WriteMetadataAsync(
            string fileName, int originalWidth, int originalHeight,
            int newWidth, int newHeight, int originalSizeBytes, int processedSizeBytes,
            List<DetectedObject> detectedObjects)
        {
            var container = _cosmosClient.GetContainer(DatabaseName, ContainerName);

            var record = new ImageMetadata
            {
                Id = Guid.NewGuid().ToString(),
                FileName = fileName,
                ProcessedAtUtc = DateTime.UtcNow,
                OriginalWidth = originalWidth,
                OriginalHeight = originalHeight,
                ProcessedWidth = newWidth,
                ProcessedHeight = newHeight,
                OriginalSizeBytes = originalSizeBytes,
                ProcessedSizeBytes = processedSizeBytes,
                DetectedObjects = detectedObjects
            };

            await container.CreateItemAsync(record, new PartitionKey(record.FileName));
            _logger.LogInformation("Wrote metadata for {FileName} to Cosmos DB", fileName);
        }
    }

    public class ImageMetadata
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("FileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonProperty("ProcessedAtUtc")]
        public DateTime ProcessedAtUtc { get; set; }

        [JsonProperty("OriginalWidth")]
        public int OriginalWidth { get; set; }

        [JsonProperty("OriginalHeight")]
        public int OriginalHeight { get; set; }

        [JsonProperty("ProcessedWidth")]
        public int ProcessedWidth { get; set; }

        [JsonProperty("ProcessedHeight")]
        public int ProcessedHeight { get; set; }

        [JsonProperty("OriginalSizeBytes")]
        public int OriginalSizeBytes { get; set; }

        [JsonProperty("ProcessedSizeBytes")]
        public int ProcessedSizeBytes { get; set; }

        [JsonProperty("DetectedObjects")]
        public List<DetectedObject> DetectedObjects { get; set; } = new();
    }
}