#!/usr/bin/env dotnet run
#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property LangVersion=preview
#:property ImplicitUsings=enable
#:property Nullable=enable
#:project ../../src/DotNetPy/DotNetPy.csproj

// =============================================================================
// DotNetPy + HuggingFace sentence-transformers — Semantic Search Sample
// =============================================================================
//
// Demonstrates:
//   - Calling a real ML model from C# with DotNetPy + uv
//   - Marshalling a .NET string[] into Python and structured results back
//   - End-to-end semantic search: encode corpus + query, return top-K hits
//
// First run downloads PyTorch / transformers / sentence-transformers via uv
// (around 1 GB) and the all-MiniLM-L6-v2 model (~90 MB). Subsequent runs are
// fast — the venv and model are cached.
//
// Prerequisites:
//   - .NET 10 SDK (file-based app)
//   - uv installed (https://docs.astral.sh/uv/getting-started/installation/)
//
// Usage:
//   cd samples/ml-embeddings
//   dotnet run sample.cs
// =============================================================================

using System.Diagnostics;
using System.Text;
using DotNetPy;
using DotNetPy.Uv;

Console.OutputEncoding = new UTF8Encoding(false);
Console.WriteLine("=== DotNetPy Semantic Search Sample (sentence-transformers) ===\n");

// -----------------------------------------------------------------------------
// 1. Declarative Python environment: torch + sentence-transformers via uv
// -----------------------------------------------------------------------------
Console.WriteLine("[1] Declarative ML environment");
Console.WriteLine(new string('-', 60));

// Pin Python to 3.12 and the HuggingFace stack to a well-wheeled release
// combo. Default discovery would otherwise prefer the highest available
// interpreter on the machine (often a free-threaded 3.14t when developers
// have one installed alongside DotNetPy's own audit work), and FT builds
// don't yet have pre-built wheels for tokenizers / safetensors, forcing a
// Rust toolchain build that fails on a clean Windows box.
using var project = PythonProject.CreateBuilder()
    .WithProjectName("dotnetpy-ml-embeddings")
    .WithVersion("1.0.0")
    .WithPythonVersion("==3.12.*")
    .AddDependencies(
        "sentence-transformers==2.7.0",
        "transformers==4.40.2",
        "tokenizers==0.19.1",
        "safetensors==0.4.3",
        "torch>=2.2,<2.5")
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

Console.WriteLine($"  Working dir: {project.WorkingDirectory}");
Console.WriteLine();

// -----------------------------------------------------------------------------
// 2. Load the SentenceTransformer model
// -----------------------------------------------------------------------------
Console.WriteLine("[2] Loading model (all-MiniLM-L6-v2, ~90 MB first run)");
Console.WriteLine(new string('-', 60));

var executor = project.GetExecutor();

sw.Restart();
try
{
    executor.Execute(@"
import numpy as np
from sentence_transformers import SentenceTransformer
model = SentenceTransformer('all-MiniLM-L6-v2')
");
    Console.WriteLine($"  Model loaded in {sw.Elapsed.TotalSeconds:F1}s");
}
catch (DotNetPyException ex)
{
    Console.WriteLine($"  ✗ Failed to load model: {ex.Message}");
    return 1;
}

Console.WriteLine();

// -----------------------------------------------------------------------------
// 3. Encode a small corpus + run a semantic query (top-K cosine similarity)
// -----------------------------------------------------------------------------
Console.WriteLine("[3] Semantic search");
Console.WriteLine(new string('-', 60));

var corpus = new[]
{
    "The quick brown fox jumps over the lazy dog.",
    "A fast auburn canine leaps above a sleepy hound.",
    "Python is a popular programming language for data science.",
    "C# and .NET are great for building enterprise applications.",
    "Climate change is one of the biggest challenges of our time.",
    "Carbon emissions are accelerating global warming.",
    "Pizza is delicious with various toppings.",
    "Rust offers memory safety without garbage collection.",
};

var query = "Tell me about programming languages";

sw.Restart();
using var hits = executor.ExecuteAndCapture(@"
# Encode corpus + query in a single Python call. NumPy stays Python-side;
# only the scored top-K (small JSON) crosses the .NET boundary.
corpus_emb = model.encode(corpus, normalize_embeddings=True)
query_emb = model.encode([query], normalize_embeddings=True)[0]

# Cosine similarity (vectors are L2-normalised, so dot product = cosine).
sims = corpus_emb @ query_emb

# Top-K by descending similarity.
k = 3
top_idx = np.argsort(-sims)[:k]
result = [
    {'rank': int(rank + 1), 'score': float(sims[i]), 'text': corpus[int(i)]}
    for rank, i in enumerate(top_idx)
]
", new Dictionary<string, object?> { { "corpus", corpus }, { "query", query } });

Console.WriteLine($"  Query: \"{query}\"");
Console.WriteLine($"  Corpus size: {corpus.Length} sentences, embedding+search in {sw.Elapsed.TotalSeconds:F2}s");
Console.WriteLine();
Console.WriteLine("  Top-3 most similar:");

if (hits is not null)
{
    foreach (var element in hits.RootElement.EnumerateArray())
    {
        var rank = element.GetProperty("rank").GetInt32();
        var score = element.GetProperty("score").GetDouble();
        var text = element.GetProperty("text").GetString();
        Console.WriteLine($"    {rank}. [score {score:F3}] {text}");
    }
}
Console.WriteLine();

// -----------------------------------------------------------------------------
// 4. Return raw embeddings to .NET (useful for downstream pipelines)
// -----------------------------------------------------------------------------
Console.WriteLine("[4] Raw embeddings → .NET");
Console.WriteLine(new string('-', 60));

using var rawEmb = executor.ExecuteAndCapture(@"
emb = model.encode(['Hello, world!']).tolist()
result = {'dim': len(emb[0]), 'first_5': emb[0][:5]}
");

if (rawEmb is not null)
{
    var dim = rawEmb.GetInt32("dim");
    var firstFive = new List<double>();
    foreach (var v in rawEmb.RootElement.GetProperty("first_5").EnumerateArray())
        firstFive.Add(v.GetDouble());

    Console.WriteLine($"  Embedding dimension: {dim}");
    Console.WriteLine($"  First 5 components:  [{string.Join(", ", firstFive.Select(x => x.ToString("F4")))}]");
}
Console.WriteLine();

// -----------------------------------------------------------------------------
// 5. (Bonus) Isolated executors: per-worker private namespaces
// -----------------------------------------------------------------------------
// Each isolated executor owns its own Python namespace, so concurrent workers
// can keep their own `model` reference, batch state, etc. without colliding.
// Under classic GIL builds this still serialises through the interpreter; the
// pattern unlocks genuine parallelism on free-threaded CPython (3.13t/3.14t),
// once your ML libraries themselves are FT-ready.
// -----------------------------------------------------------------------------
Console.WriteLine("[5] Isolated executors (per-worker namespace pattern)");
Console.WriteLine(new string('-', 60));

var batches = new[]
{
    new[] { "I love pizza", "Pasta is great" },
    new[] { "Programming is fun", "Tests matter" },
    new[] { "Climate change is real", "Sustainability matters" },
};

sw.Restart();
var results = new System.Collections.Concurrent.ConcurrentBag<string>();

Parallel.For(0, batches.Length, batchIdx =>
{
    using var iso = Python.CreateIsolated();
    // sys.path is process-wide and was already set up by the shared executor
    // when project.GetExecutor() ran, so this isolated worker can import the
    // venv's packages without re-running LoadVirtualEnvironment.
    iso.Execute(@"
from sentence_transformers import SentenceTransformer
worker_model = SentenceTransformer('all-MiniLM-L6-v2')
");

    using var batchResult = iso.ExecuteAndCapture(@"
emb = worker_model.encode(texts).tolist()
result = {'count': len(emb), 'dim': len(emb[0]) if emb else 0}
", new Dictionary<string, object?> { { "texts", batches[batchIdx] } });

    var count = batchResult?.GetInt32("count") ?? 0;
    var dim = batchResult?.GetInt32("dim") ?? 0;
    results.Add($"Worker {batchIdx}: encoded {count} texts, dim={dim}");
});

Console.WriteLine($"  3 isolated workers, each with private `worker_model`: {sw.Elapsed.TotalSeconds:F1}s");
foreach (var r in results.OrderBy(x => x))
    Console.WriteLine($"    {r}");

Console.WriteLine();
Console.WriteLine("=== Sample Complete ===");
return 0;
