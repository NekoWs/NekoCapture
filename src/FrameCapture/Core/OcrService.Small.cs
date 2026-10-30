using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using FrameCapture.Capture;
using FrameCapture.Services;

namespace FrameCapture.Core;

/// <summary>可选 small 引擎的隔离进程通信和逐字坐标映射。</summary>
public static partial class OcrService
{
    static async Task<OcrOutcome> RecognizeSmallAsync(CapturedFrame frame, bool includeImage)
    {
        if (!OcrSmallComponent.Available) return new(OcrStatus.ComponentMissing, "");
        await RecognitionGate.WaitAsync().ConfigureAwait(false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var process = new Process { StartInfo = new ProcessStartInfo(OcrSmallComponent.WorkerPath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(OcrSmallComponent.WorkerPath)!,
        } };
        try
        {
            if (frame.W <= 0 || frame.H <= 0 || (long)frame.W * frame.H > OcrWorkerProtocol.MaxPixels
                || (long)frame.W * frame.H * 4 != frame.Bgra.LongLength) return new(OcrStatus.Failed, "");
            process.StartInfo.ArgumentList.Add(OcrSmallComponent.ModelDirectory!);
            process.StartInfo.Environment["DOTNET_PROCESSOR_COUNT"] = "2";
            process.Start();
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            var header = new byte[12];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, OcrWorkerProtocol.Magic);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), frame.W);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), frame.H);
            await process.StandardInput.BaseStream.WriteAsync(header, timeout.Token);
            await process.StandardInput.BaseStream.WriteAsync(frame.Bgra, timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            string json = await output, diagnostic = await errors;
            var result = JsonSerializer.Deserialize(json, OcrWorkerJson.Default.OcrWorkerResult);
            if (process.ExitCode != 0 || result == null || result.Error.Length > 0)
                throw new InvalidOperationException(result?.Error + diagnostic);
            Trace.Log("small " + diagnostic.Trim());
            var lines = result.Lines.Where(line => line.Confidence >= 0.35 && !string.IsNullOrWhiteSpace(line.Text))
                .Select(ConvertSmallLine).Where(line => line.Words.Count > 0).ToArray();
            string text = string.Join("\n", lines.Select(line => line.Text));
            return new(Classify(text), text) { Lines = lines, Image = includeImage && text.Length > 0 ? BgraImage.ToSource(frame) : null };
        }
        catch (Exception error) { Trace.Log("small OCR: " + error); return new(OcrStatus.Failed, ""); }
        finally
        {
            try { if (process.Id != 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            RecognitionGate.Release();
        }
    }

    static OcrLineInfo ConvertSmallLine(OcrWorkerLine line)
    {
        var first = line.Characters.FirstOrDefault(character => character.Quad.Length == 8);
        if (first == null) return new(line.Text, [], 0, new Point());
        double angle = Math.Atan2(first.Quad[3] - first.Quad[1], first.Quad[2] - first.Quad[0]) * 180 / Math.PI;
        var center = new Point(first.Quad[0], first.Quad[1]);
        var inverse = new RotateTransform(-angle, center.X, center.Y);
        var characters = new Dictionary<int, Rect>();
        int search = 0;
        foreach (var character in line.Characters)
        {
            if (character.Quad.Length != 8 || character.Quad.Any(value => !double.IsFinite(value)) || character.Text.Length == 0) continue;
            int start = line.Text.IndexOf(character.Text, search, StringComparison.Ordinal);
            if (start < 0) continue;
            var bounds = Rect.Empty;
            for (int i = 0; i < 8; i += 2)
            {
                var point = inverse.Transform(new Point(character.Quad[i], character.Quad[i + 1]));
                bounds.Union(new Rect(point, new Size()));
            }
            if (bounds.Width > 0 && bounds.Height > 0) characters[start] = bounds;
            search = start + character.Text.Length;
        }
        var words = new List<OcrWordInfo>();
        foreach (Match match in Regex.Matches(line.Text, @"\S+"))
        {
            var boxes = new List<Rect>();
            foreach (int offset in StringInfo.ParseCombiningCharacters(match.Value))
                if (characters.TryGetValue(match.Index + offset, out var box)) boxes.Add(box);
            if (boxes.Count == 0) continue;
            var bounds = boxes.Aggregate(Rect.Empty, (total, box) => Rect.Union(total, box));
            words.Add(new(match.Value, bounds, match.Index) { CharacterBounds = boxes });
        }
        return new(line.Text, words, angle, center);
    }
}
