using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Newtonsoft.Json;
using SkiaSharp;

namespace ServerlessImageProcessor.Functions
{
    public class DetectedObject
    {
        [JsonProperty("Label")]
        public string Label { get; set; } = string.Empty;

        [JsonProperty("Confidence")]
        public float Confidence { get; set; }
    }

    public class ObjectDetector : IDisposable
    {
        private readonly InferenceSession _session;
        private const float ConfidenceThreshold = 0.5f;

        // Standard 90-class COCO label map used by the TF Object Detection API
        // (note: IDs 12, 26, 29, 30, 45, 66, 68, 69, 71, 83 are intentionally skipped/merged upstream)
        private static readonly Dictionary<int, string> CocoLabels = new()
        {
            {1, "person"}, {2, "bicycle"}, {3, "car"}, {4, "motorcycle"}, {5, "airplane"},
            {6, "bus"}, {7, "train"}, {8, "truck"}, {9, "boat"}, {10, "traffic light"},
            {11, "fire hydrant"}, {13, "stop sign"}, {14, "parking meter"}, {15, "bench"},
            {16, "bird"}, {17, "cat"}, {18, "dog"}, {19, "horse"}, {20, "sheep"},
            {21, "cow"}, {22, "elephant"}, {23, "bear"}, {24, "zebra"}, {25, "giraffe"},
            {27, "backpack"}, {28, "umbrella"}, {31, "handbag"}, {32, "tie"}, {33, "suitcase"},
            {34, "frisbee"}, {35, "skis"}, {36, "snowboard"}, {37, "sports ball"}, {38, "kite"},
            {39, "baseball bat"}, {40, "baseball glove"}, {41, "skateboard"}, {42, "surfboard"},
            {43, "tennis racket"}, {44, "bottle"}, {46, "wine glass"}, {47, "cup"}, {48, "fork"},
            {49, "knife"}, {50, "spoon"}, {51, "bowl"}, {52, "banana"}, {53, "apple"},
            {54, "sandwich"}, {55, "orange"}, {56, "broccoli"}, {57, "carrot"}, {58, "hot dog"},
            {59, "pizza"}, {60, "donut"}, {61, "cake"}, {62, "chair"}, {63, "couch"},
            {64, "potted plant"}, {65, "bed"}, {67, "dining table"}, {70, "toilet"}, {72, "tv"},
            {73, "laptop"}, {74, "mouse"}, {75, "remote"}, {76, "keyboard"}, {77, "cell phone"},
            {78, "microwave"}, {79, "oven"}, {80, "toaster"}, {81, "sink"}, {82, "refrigerator"},
            {84, "book"}, {85, "clock"}, {86, "vase"}, {87, "scissors"}, {88, "teddy bear"},
            {89, "hair drier"}, {90, "toothbrush"}
        };

        public ObjectDetector(string modelPath)
        {
            _session = new InferenceSession(modelPath);
        }

        public List<DetectedObject> Detect(SKBitmap bitmap)
        {
            int height = bitmap.Height;
            int width = bitmap.Width;

            // Build a [1, height, width, 3] uint8 tensor — raw RGB pixel values, no normalization
            var inputTensor = new DenseTensor<byte>(new[] { 1, height, width, 3 });
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    inputTensor[0, y, x, 0] = pixel.Red;
                    inputTensor[0, y, x, 1] = pixel.Green;
                    inputTensor[0, y, x, 2] = pixel.Blue;
                }
            }

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("inputs", inputTensor)
            };

            using var results = _session.Run(inputs);

            var detectionScores = results.First(r => r.Name == "detection_scores").AsTensor<float>();
            var detectionClasses = results.First(r => r.Name == "detection_classes").AsTensor<float>();

            var detected = new List<DetectedObject>();
            int count = detectionScores.Dimensions[1];

            for (int i = 0; i < count; i++)
            {
                float score = detectionScores[0, i];
                if (score < ConfidenceThreshold)
                    continue;

                int classId = (int)detectionClasses[0, i];
                string label = CocoLabels.TryGetValue(classId, out var name) ? name : $"class_{classId}";

                detected.Add(new DetectedObject { Label = label, Confidence = score });
            }

            return detected.OrderByDescending(d => d.Confidence).ToList();
        }

        public void Dispose()
        {
            _session.Dispose();
        }
    }
}