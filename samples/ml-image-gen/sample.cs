#!/usr/bin/env dotnet run
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property LangVersion=preview
#:property ImplicitUsings=enable
#:property Nullable=enable
#:project ../../src/DotNetPy/DotNetPy.csproj

// =============================================================================
// DotNetPy + Stable Diffusion Turbo — Text-to-Image Sample
// =============================================================================
//
// Demonstrates:
//   - Calling a real diffusion model from C# with DotNetPy + uv
//   - Marshalling a text prompt to Python, getting back image metadata
//     (saved file path, size, latency) — the binary stays Python-side
//   - Per-prompt parallelism via isolated executors
//
// First run downloads the HuggingFace stack via uv (~2 GB once accelerate
// and friends are resolved) and the stabilityai/sd-turbo model (~2 GB).
// Subsequent runs are fast — the venv and model are cached.
//
// SD-Turbo is a one-step text-to-image diffusion model. On CPU it takes
// roughly 15–60 seconds per image depending on hardware (and 1–3 seconds
// on a recent GPU). The sample stays on CPU by default so it runs out of
// the box.
//
// Prerequisites:
//   - .NET 10 SDK (file-based app)
//   - uv installed (https://docs.astral.sh/uv/getting-started/installation/)
//   - ~4 GB free disk + ~4 GB RAM for inference
//
// Usage:
//   cd samples/ml-image-gen
//   dotnet run sample.cs
// =============================================================================

using System.Diagnostics;
using System.Text;
using DotNetPy;
using DotNetPy.Uv;

Console.OutputEncoding = new UTF8Encoding(false);
Console.WriteLine("=== DotNetPy Text-to-Image Sample (Stable Diffusion Turbo) ===\n");

// -----------------------------------------------------------------------------
// 1. Declarative environment: diffusers + transformers stack
// -----------------------------------------------------------------------------
Console.WriteLine("[1] Declarative ML environment");
Console.WriteLine(new string('-', 60));

// Same wheels-friendly pinning rationale as samples/ml-embeddings and
// samples/ml-whisper: newer transformers / safetensors / tokenizers
// releases force Rust-based source builds that fail on a clean Windows
// box. diffusers 0.27–0.31 is the matching window for transformers 4.40.
using var project = PythonProject.CreateBuilder()
    .WithProjectName("dotnetpy-ml-image-gen")
    .WithVersion("1.0.0")
    .WithPythonVersion("==3.12.*")
    .AddDependencies(
        "diffusers>=0.27,<0.32",
        "transformers==4.40.2",
        "tokenizers==0.19.1",
        "safetensors==0.4.3",
        "torch>=2.2,<2.5",
        "accelerate>=0.26",
        "Pillow>=10")
    .Build();

Console.Write("  Resolving env (first run may download ~2 GB)... ");
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
// 2. Load the SD-Turbo pipeline
// -----------------------------------------------------------------------------
Console.WriteLine("[2] Loading model (stabilityai/sd-turbo, ~2 GB first run)");
Console.WriteLine(new string('-', 60));

var executor = project.GetExecutor();

sw.Restart();
try
{
    executor.Execute(@"
import torch
from diffusers import AutoPipelineForText2Image
# Stay on CPU + float32 so the sample runs out-of-the-box. For a GPU host
# you would set torch_dtype=torch.float16 and call pipe.to('cuda').
pipe = AutoPipelineForText2Image.from_pretrained(
    'stabilityai/sd-turbo',
    torch_dtype=torch.float32,
    safety_checker=None,
    requires_safety_checker=False,
)
pipe.set_progress_bar_config(disable=True)
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
// 3. Generate an image from a prompt
// -----------------------------------------------------------------------------
Console.WriteLine("[3] Generate");
Console.WriteLine(new string('-', 60));

var prompt = "a serene mountain lake at sunset, oil painting style, dramatic clouds";
var outDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
Directory.CreateDirectory(outDir);

Console.WriteLine($"  Prompt: \"{prompt}\"");
Console.Write("  Generating (CPU, single step, may take 15–60s)... ");

sw.Restart();
using var meta = executor.ExecuteAndCapture(@"
import time, os
t0 = time.time()
# SD-Turbo is trained for 1 inference step and guidance_scale=0.
img = pipe(prompt=prompt, num_inference_steps=1, guidance_scale=0.0).images[0]
elapsed = time.time() - t0
out_path = os.path.join(out_dir, 'generated.png')
img.save(out_path)
size_bytes = os.path.getsize(out_path)
result = {
    'path': out_path,
    'width': img.size[0],
    'height': img.size[1],
    'size_bytes': size_bytes,
    'elapsed_seconds': elapsed,
}
", new Dictionary<string, object?>
{
    { "prompt", prompt },
    { "out_dir", outDir },
});

Console.WriteLine($"done in {sw.Elapsed.TotalSeconds:F1}s");
Console.WriteLine();

if (meta is not null)
{
    Console.WriteLine($"  Saved:   {meta.GetString("path")}");
    Console.WriteLine($"  Size:    {meta.GetInt32("width")}×{meta.GetInt32("height")} px, {meta.GetInt32("size_bytes"),0:N0} bytes");
    Console.WriteLine($"  Inference time (Python-side): {meta.GetDouble("elapsed_seconds"):F2}s");
}
Console.WriteLine();

// -----------------------------------------------------------------------------
// 4. (Bonus) Isolated executors: per-worker private pipelines
// -----------------------------------------------------------------------------
// Each worker loads its own SD-Turbo pipeline into its own isolated namespace.
// On CPU this still serialises through Python, but the pattern unlocks
// genuine parallelism on free-threaded CPython once diffusers + torch ship
// FT-safe builds.
//
// Two prompts is intentional — three would push memory hard on a 16 GB
// machine because each pipeline holds ~2 GB of weights.
// -----------------------------------------------------------------------------
Console.WriteLine("[4] Isolated executors (per-worker namespace pattern)");
Console.WriteLine(new string('-', 60));

var prompts = new[]
{
    "a robotic owl perched on a circuit board, neon lighting",
    "a cup of coffee on a wooden desk, morning light, photorealistic",
};

sw.Restart();
var results = new System.Collections.Concurrent.ConcurrentBag<string>();

Parallel.For(0, prompts.Length, idx =>
{
    using var iso = Python.CreateIsolated();
    iso.Execute(@"
import torch
from diffusers import AutoPipelineForText2Image
worker_pipe = AutoPipelineForText2Image.from_pretrained(
    'stabilityai/sd-turbo',
    torch_dtype=torch.float32,
    safety_checker=None,
    requires_safety_checker=False,
)
worker_pipe.set_progress_bar_config(disable=True)
");
    using var r = iso.ExecuteAndCapture(@"
import os, time
t0 = time.time()
img = worker_pipe(prompt=p, num_inference_steps=1, guidance_scale=0.0).images[0]
out_path = os.path.join(out_dir, f'worker_{i}.png')
img.save(out_path)
result = {'path': out_path, 'elapsed': time.time() - t0}
", new Dictionary<string, object?>
    {
        { "p", prompts[idx] },
        { "out_dir", outDir },
        { "i", idx },
    });

    var path = r?.GetString("path") ?? "(unknown)";
    var elapsed = r?.GetDouble("elapsed") ?? 0;
    results.Add($"Worker {idx}: \"{prompts[idx]}\" → {Path.GetFileName(path)} ({elapsed:F1}s)");
});

Console.WriteLine($"  {prompts.Length} isolated workers, each with private `worker_pipe`: {sw.Elapsed.TotalSeconds:F1}s total");
foreach (var r in results.OrderBy(x => x))
    Console.WriteLine($"    {r}");

Console.WriteLine();
Console.WriteLine($"  All generated images are in: {outDir}");
Console.WriteLine();
Console.WriteLine("=== Sample Complete ===");
return 0;
