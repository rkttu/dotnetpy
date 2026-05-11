#!/usr/bin/env dotnet run
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property LangVersion=preview
#:property ImplicitUsings=enable
#:property Nullable=enable
#:project ../../src/DotNetPy/DotNetPy.csproj

// =============================================================================
// DotNetPy + OpenAI Whisper — Speech-to-Text Sample
// =============================================================================
//
// Demonstrates:
//   - Calling a real ASR model from C# with DotNetPy + uv
//   - Marshalling an audio file path + chunk-level timestamped transcripts
//     between Python and .NET
//   - The isolated-executor pattern: N audio files transcribed by N workers,
//     each holding its own Whisper instance
//
// First run downloads the HuggingFace transformers stack via uv (~700 MB)
// and the whisper-base.en model (~290 MB). Subsequent runs are fast — the
// venv and model are cached.
//
// Audio file: openai/whisper's test sample of John F. Kennedy's 1961
// inaugural address ("Ask not what your country can do for you..."), which
// is in the U.S. public domain.
//
// Prerequisites:
//   - .NET 10 SDK (file-based app)
//   - uv installed (https://docs.astral.sh/uv/getting-started/installation/)
//
// Usage:
//   cd samples/ml-whisper
//   dotnet run sample.cs
// =============================================================================

using System.Diagnostics;
using System.Text;
using DotNetPy;
using DotNetPy.Uv;

Console.OutputEncoding = new UTF8Encoding(false);
Console.WriteLine("=== DotNetPy Speech-to-Text Sample (Whisper) ===\n");

// -----------------------------------------------------------------------------
// 1. Declarative environment: transformers + librosa for audio I/O
// -----------------------------------------------------------------------------
Console.WriteLine("[1] Declarative ML environment");
Console.WriteLine(new string('-', 60));

// Pin Python to 3.12 and the HuggingFace stack to a wheels-friendly combo,
// for the same reasons documented in samples/ml-embeddings: newer
// transformers / safetensors / tokenizers releases force Rust-based source
// builds that fail on a clean Windows box.
using var project = PythonProject.CreateBuilder()
    .WithProjectName("dotnetpy-ml-whisper")
    .WithVersion("1.0.0")
    .WithPythonVersion("==3.12.*")
    .AddDependencies(
        "transformers==4.40.2",
        "tokenizers==0.19.1",
        "safetensors==0.4.3",
        "torch>=2.2,<2.5",
        "librosa>=0.10,<0.11",
        "soundfile>=0.12")
    .Build();

Console.Write("  Resolving env (first run may download ~1 GB)... ");
var sw = Stopwatch.StartNew();
try
{
    await project.InitializeAsync();
    Console.WriteLine($"done in {sw.Elapsed.TotalSeconds:F1}s");
}
catch (Exception ex)
{
    Console.WriteLine($"\n  ✗ Failed to initialise environment: {ex.Message}");
    return 1;
}

Console.WriteLine();

// -----------------------------------------------------------------------------
// 2. Load the Whisper pipeline
// -----------------------------------------------------------------------------
Console.WriteLine("[2] Loading model (whisper-base.en, ~290 MB first run)");
Console.WriteLine(new string('-', 60));

var executor = project.GetExecutor();

sw.Restart();
try
{
    executor.Execute(@"
from transformers import pipeline
import torch
# float32 keeps the sample CPU-portable; switch to torch.float16 + .to('cuda')
# if you have a GPU and want lower latency.
asr = pipeline(
    'automatic-speech-recognition',
    model='openai/whisper-base.en',
    chunk_length_s=30,
    return_timestamps=True,
    torch_dtype=torch.float32,
)
");
    Console.WriteLine($"  Pipeline loaded in {sw.Elapsed.TotalSeconds:F1}s");
}
catch (DotNetPyException ex)
{
    Console.WriteLine($"  ✗ Failed to load pipeline: {ex.Message}");
    return 1;
}

Console.WriteLine();

// -----------------------------------------------------------------------------
// 3. Transcribe a single audio file
// -----------------------------------------------------------------------------
Console.WriteLine("[3] Transcribe");
Console.WriteLine(new string('-', 60));

var audioPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "audio", "jfk.flac"));
if (!File.Exists(audioPath))
{
    // Fall back to the script directory layout (dotnet run sample.cs).
    audioPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "audio", "jfk.flac"));
}
if (!File.Exists(audioPath))
{
    Console.WriteLine($"  ✗ Audio file not found at {audioPath}");
    return 1;
}

Console.WriteLine($"  Audio: {audioPath}");

sw.Restart();
using var transcript = executor.ExecuteAndCapture(@"
out = asr(audio_path)
# Pipeline returns chunks as {'timestamp': (start, end), 'text': '...'}.
# Some chunks can carry None timestamps near boundaries; drop those.
chunks = [
    {'start': float(c['timestamp'][0]), 'end': float(c['timestamp'][1]), 'text': c['text'].strip()}
    for c in out.get('chunks', [])
    if c.get('timestamp') and c['timestamp'][0] is not None and c['timestamp'][1] is not None
]
result = {'text': out['text'].strip(), 'chunks': chunks}
", new Dictionary<string, object?> { { "audio_path", audioPath } });

Console.WriteLine($"  Inference: {sw.Elapsed.TotalSeconds:F2}s");
Console.WriteLine();

if (transcript is not null)
{
    var text = transcript.GetString("text");
    Console.WriteLine("  Transcript:");
    Console.WriteLine($"    \"{text}\"");
    Console.WriteLine();

    Console.WriteLine("  Chunks (with timestamps):");
    foreach (var c in transcript.RootElement.GetProperty("chunks").EnumerateArray())
    {
        var s = c.GetProperty("start").GetDouble();
        var e = c.GetProperty("end").GetDouble();
        var t = c.GetProperty("text").GetString();
        Console.WriteLine($"    [{s,6:F2}s → {e,6:F2}s] {t}");
    }
}
Console.WriteLine();

// -----------------------------------------------------------------------------
// 4. (Bonus) Isolated executors: per-worker private namespaces
// -----------------------------------------------------------------------------
// In a real service you would create one isolated executor per worker thread,
// load the Whisper pipeline once into each, and feed a stream of audio jobs.
// Here we demonstrate the per-worker pattern with three workers each
// transcribing the same clip into its private namespace — under classic GIL
// builds this still serialises through the interpreter, but the pattern is
// what enables true parallelism on free-threaded CPython once the audio
// stack is FT-ready.
// -----------------------------------------------------------------------------
Console.WriteLine("[4] Isolated executors (per-worker namespace pattern)");
Console.WriteLine(new string('-', 60));

sw.Restart();
var results = new System.Collections.Concurrent.ConcurrentBag<string>();

Parallel.For(0, 3, workerId =>
{
    using var iso = Python.CreateIsolated();
    iso.Execute(@"
from transformers import pipeline
import torch
asr = pipeline(
    'automatic-speech-recognition',
    model='openai/whisper-base.en',
    chunk_length_s=30,
    return_timestamps=False,
    torch_dtype=torch.float32,
)
");
    using var r = iso.ExecuteAndCapture(@"
out = asr(audio_path)
result = {'len': len(out['text'].strip()), 'first_15': out['text'].strip()[:15]}
", new Dictionary<string, object?> { { "audio_path", audioPath } });

    var len = r?.GetInt32("len") ?? 0;
    var first = r?.GetString("first_15") ?? "";
    results.Add($"Worker {workerId}: text length={len}, starts with \"{first}…\"");
});

Console.WriteLine($"  3 isolated workers, each with private `asr` pipeline: {sw.Elapsed.TotalSeconds:F1}s");
foreach (var r in results.OrderBy(x => x))
    Console.WriteLine($"    {r}");

Console.WriteLine();
Console.WriteLine("=== Sample Complete ===");
return 0;
