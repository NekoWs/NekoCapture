using System.Text.Json;
using NekoCapture.Core;
using Sdcb.SimdPaddleOCR;

namespace NekoCapture.OcrWorker;

/// <summary>单次 CPU OCR 请求，进程退出时释放模型和推理内存。</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        OcrWorkerResult response;
        try
        {
            if (args.Length != 1) throw new ArgumentException("Expected the model directory.");
            using var input = new BinaryReader(Console.OpenStandardInput());
            if (input.ReadInt32() != OcrWorkerProtocol.Magic) throw new InvalidDataException("Unknown OCR protocol.");
            int width = input.ReadInt32(), height = input.ReadInt32();
            long pixels = (long)width * height;
            if (width <= 0 || height <= 0 || pixels > OcrWorkerProtocol.MaxPixels) throw new InvalidDataException("Invalid OCR image size.");
            var image = new byte[checked((int)pixels * 4)];
            input.BaseStream.ReadExactly(image);
            for (int i = 0; i < image.Length; i += 4)
            {
                int alpha = image[i + 3];
                if (alpha == 255) continue;
                for (int channel = 0; channel < 3; channel++) image[i + channel] = (byte)((image[i + channel] * alpha + 255 * (255 - alpha)) / 255);
                image[i + 3] = 255;
            }
            var options = new PaddleOcrOptions
            {
                DetIntraOpThreads = 2, LineWorkerCount = 1,
                Detector = new() { Backend = OcrBackend.Cpu, LimitSideLength = 1600, MaxPooledSessions = 1 },
                Classifier = new() { Backend = OcrBackend.Cpu, MaxPooledSessions = 1 },
                Recognizer = new() { Backend = OcrBackend.Cpu, MaxPooledSessions = 1 },
            };
            string directory = args[0];
            using var engine = PaddleOcrAll.Load(Path.Combine(directory, "det.onnx"), Path.Combine(directory, "cls.onnx"),
                Path.Combine(directory, "rec.onnx"), Path.Combine(directory, "dict.txt"), options);
            var result = engine.Run(image, width, height, returnCtcAlignment: true, format: ImagePixelFormat.Bgra32);
            response = new() { Lines = result.Lines.Select(line => new OcrWorkerLine
            {
                Text = line.Text, Confidence = line.RecognitionScore,
                Characters = line.EstimateCharacterBoxes().Select(character => new OcrWorkerCharacter
                {
                    Text = character.Text,
                    Quad = [character.X1, character.Y1, character.X2, character.Y2,
                        character.X3, character.Y3, character.X4, character.Y4],
                }).ToArray(),
            }).ToArray() };
            Console.Error.WriteLine($"OCR peak working set: {System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64 / 1048576d:0.0} MiB");
        }
        catch (Exception error) { response = new() { Error = error.ToString() }; }
        Console.Write(JsonSerializer.Serialize(response, OcrWorkerJson.Default.OcrWorkerResult));
        return response.Error.Length == 0 ? 0 : 1;
    }
}
