# ml-embeddings

End-to-end semantic search from C# using HuggingFace `sentence-transformers`.
The sample loads `all-MiniLM-L6-v2`, encodes a small corpus, embeds a query,
and returns the top-K most similar sentences — all driven from a single
`.cs` file via [`DotNetPy`](../../README.md) + [uv](https://docs.astral.sh/uv/).

This is the demo that justifies "*Python interop, reimagined*": the
model-side work stays in Python (where the ecosystem lives), the orchestration
and data flow stay in C# (where your app lives), and the boundary is a clean
JSON round-trip.

## What it shows

1. **Declarative ML environment** — `PythonProjectBuilder` provisions a uv
   venv with `sentence-transformers` (which pulls in `torch` + `transformers`)
   on first run.
2. **Real model inference** — encodes 8 sentences + a query, computes cosine
   similarity, returns top-3 matches with scores.
3. **Bidirectional marshalling** — `string[]` from .NET → Python, structured
   scored results (`rank`, `score`, `text`) Python → .NET.
4. **Raw embeddings** — returns the full 384-dim float vector for downstream
   use (e.g. feeding into a vector database).
5. **Isolated executors** — 3 workers, each with its own
   `worker_model` reference via `Python.CreateIsolated()`. Demonstrates the
   per-worker namespace pattern that becomes truly parallel under free-threaded
   Python (3.13t / 3.14t).

## Requirements

- .NET 10 SDK (for the file-based-app `dotnet run sample.cs`)
- [uv](https://docs.astral.sh/uv/) on `PATH`
- ~1.5 GB free disk for the venv (torch + transformers) and the model

The sample pins Python to **3.12.x** and the HuggingFace stack to specific
releases (`sentence-transformers 2.7.0`, `transformers 4.40.2`,
`tokenizers 0.19.1`, `safetensors 0.4.3`, `torch 2.2–2.4`). The pin avoids
two pitfalls on a clean Windows machine: uv preferring a free-threaded
interpreter (which lacks pre-built ML wheels and forces a Rust source
build), and newer `safetensors` / `tokenizers` releases that have not
shipped wheels for every CPython version yet. uv will download the matching
Python 3.12 build automatically if you don't already have it.

## Run it

```bash
cd samples/ml-embeddings
dotnet run sample.cs
```

The **first run takes a few minutes**: uv downloads CPython 3.12 (if not
already cached), then resolves `torch` (~700 MB) and related wheels, and
finally HuggingFace downloads `all-MiniLM-L6-v2` (~90 MB). The working
directory is a temp folder that is recreated each run, but uv's wheel cache
and HuggingFace's model cache (`~/.cache/huggingface/`) survive, so
subsequent runs finish in seconds.

## Expected output (abridged)

```text
=== DotNetPy Semantic Search Sample (sentence-transformers) ===

[1] Declarative ML environment
  Resolving env (first run may download ~1 GB)... done in 82.0s
  Working dir: C:\...\dotnetpy-projects\dotnetpy-ml-embeddings-...

[2] Loading model (all-MiniLM-L6-v2, ~90 MB first run)
  Model loaded in 39.2s

[3] Semantic search
  Query: "Tell me about programming languages"
  Corpus size: 8 sentences, embedding+search in 2.15s

  Top-3 most similar:
    1. [score 0.578] Python is a popular programming language for data science.
    2. [score 0.370] C# and .NET are great for building enterprise applications.
    3. [score 0.203] Rust offers memory safety without garbage collection.

[4] Raw embeddings → .NET
  Embedding dimension: 384
  First 5 components:  [-0.0382, 0.0329, -0.0055, 0.0144, -0.0403]

[5] Isolated executors (per-worker namespace pattern)
  3 isolated workers, each with private `worker_model`: 2.4s
    Worker 0: encoded 2 texts, dim=384
    Worker 1: encoded 2 texts, dim=384
    Worker 2: encoded 2 texts, dim=384

=== Sample Complete ===
```

Timings are from a first run on Windows 11 with broadband; subsequent runs
skip the env-resolve download and finish in seconds. Exact similarity
scores vary slightly across torch builds; the **ordering** of the top
matches should be stable.

## Free-threaded Python notes

`sentence-transformers` ⇒ `transformers` ⇒ `torch` ⇒ NumPy. As of this
sample's authorship NumPy 2.1+ supports PEP 703 free-threaded builds; torch
is in active migration. Until the upstream ML stack is fully FT-ready, the
shared singleton or isolated executors will still work under `python3.13t` /
`python3.14t` for the **interop layer itself**, but Python-side ML
performance under FT may be limited by what each library has shipped.

DotNetPy's own audit and free-threaded verification matrix is documented in
[`docs/FREETHREADED-AUDIT.md`](../../docs/FREETHREADED-AUDIT.md).

## Production patterns

The shape of this sample is intentionally small. A realistic deployment looks
similar but persists more state:

- **Persistent venv** — pass `.WithWorkingDirectory("./.dotnetpy-env")` to
  `PythonProjectBuilder` so the environment is reused across runs instead of
  being recreated in a temp dir.
- **Model reuse** — load the model once per process (or once per worker for
  the isolated-executor pattern) instead of per request.
- **Vector store** — push the raw `float[384]` embeddings into your vector DB
  of choice (Qdrant, pgvector, FAISS via Python, etc.) and only round-trip
  the small scored results back to .NET.

## Related

- [`samples/declarative-python`](../declarative-python/) — the underlying
  `PythonProjectBuilder` pattern in more detail.
- [`samples/native-aot`](../native-aot/) — drives the AOT-compiled
  `dotnetpy-native.dll` through C exports; matches "single-binary ML
  inference" deployments.
- [`docs/COMPARISON.md`](../../docs/COMPARISON.md) — decision tree across the
  C#-Python interop landscape.
