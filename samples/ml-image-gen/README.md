# ml-image-gen

End-to-end text-to-image generation from C# using
[`stabilityai/sd-turbo`](https://huggingface.co/stabilityai/sd-turbo) — a
single-step distilled Stable Diffusion variant — via HuggingFace
`diffusers`. The sample loads the pipeline, generates a PNG from a text
prompt, returns image metadata (path, size, latency) to .NET, then spins
up isolated executors that each hold their own pipeline and produce
their own image.

This is the vision counterpart to [`samples/ml-embeddings`](../ml-embeddings/)
(NLP) and [`samples/ml-whisper`](../ml-whisper/) (audio). Same DotNetPy
patterns — declarative environment via `PythonProjectBuilder`, the
shared singleton for the warm-up flow, and `Python.CreateIsolated()`
for the per-worker shape.

## What it shows

1. **Declarative diffusion environment** — `PythonProjectBuilder`
   provisions a uv venv with `diffusers`, `transformers`, `accelerate`,
   `torch`, and `Pillow` on first run.
2. **Real diffusion inference** — SD-Turbo generates a 512×512 image
   from a text prompt in one inference step.
3. **Metadata round-trip** — Python writes the PNG to disk; .NET gets
   back `{path, width, height, size_bytes, elapsed_seconds}`. The binary
   never crosses the boundary, which keeps the JSON marshalling cheap
   even at high resolutions.
4. **Isolated executors** — two workers, each with its own SD-Turbo
   pipeline in a private Python namespace, generating different images
   from different prompts. The pattern unlocks genuine parallelism on
   free-threaded CPython once the diffusion stack ships FT-safe builds.

## Requirements

- .NET 10 SDK (for the file-based-app `dotnet run sample.cs`)
- [uv](https://docs.astral.sh/uv/) on `PATH`
- ~4 GB free disk for the venv and the SD-Turbo model
- ~4 GB RAM for inference (peak, briefly)
- Patience on first run — CPU inference is ~15–60 seconds per image
  depending on hardware. A recent GPU brings that down to ~1–3 seconds.

The sample pins Python to **3.12.x** and the HuggingFace stack to specific
releases (`diffusers 0.27–0.31`, `transformers 4.40.2`, `tokenizers 0.19.1`,
`safetensors 0.4.3`, `torch 2.2–2.4`, `accelerate ≥0.26`, `Pillow ≥10`).
Same wheels-friendly rationale as the other samples in this directory.

## Run it

```bash
cd samples/ml-image-gen
dotnet run sample.cs
```

The **first run takes a while**: uv resolves Python 3.12 + the diffusers
stack (much of it shared with `ml-embeddings`/`ml-whisper` caches), then
HuggingFace downloads `stabilityai/sd-turbo` (~2 GB). Each generated
image is then a single CPU inference step.

Generated PNGs land in `samples/ml-image-gen/output/`:

- `generated.png` — the warm-up generation from the shared singleton.
- `worker_0.png`, `worker_1.png` — the two isolated-executor outputs.

## Expected output

```text
=== DotNetPy Text-to-Image Sample (Stable Diffusion Turbo) ===

[1] Declarative ML environment
  Resolving env (first run may download ~2 GB)... done in 108.2s

[2] Loading model (stabilityai/sd-turbo, ~2 GB first run)
  Pipeline loaded in 494.2s

[3] Generate
  Prompt: "a serene mountain lake at sunset, oil painting style, dramatic clouds"
  Generating (CPU, single step, may take 15–60s)... done in 31.3s

  Saved:   .../samples/ml-image-gen/output/generated.png
  Size:    512×512 px, 434,242 bytes
  Inference time (Python-side): 31.19s

[4] Isolated executors (per-worker namespace pattern)
  2 isolated workers, each with private `worker_pipe`: 45.6s total
    Worker 0: "a robotic owl perched on a circuit board, neon lighting" → worker_0.png (40.8s)
    Worker 1: "a cup of coffee on a wooden desk, morning light, photorealistic" → worker_1.png (42.6s)

  All generated images are in: .../samples/ml-image-gen/output

=== Sample Complete ===
```

Timings are from a first run on Windows 11, CPU-only, with broadband; most
of the 494 s in step `[2]` is the one-time SD-Turbo download. Subsequent
runs skip that and the pipeline loads in seconds. Per-image inference is
~30–45 s on CPU; on a recent GPU it drops to 1–3 s.

Image content varies across torch / diffusers patch versions because the
random-init seed is not pinned — set
`generator=torch.Generator().manual_seed(N)` in the Python block if you
need reproducible outputs.

Generated PNGs are deliberately `.gitignore`d; only the `.cs` and the
README live in the repo. The output directory is created on first run.

## Production patterns

Realistic deployment differs from this sample in three ways:

1. **GPU** — set `torch_dtype=torch.float16` and call `pipe.to('cuda')` in
   the load step. Single-step generation drops from ~15–60 s to ~1–3 s.
2. **One pipeline per worker, many requests per pipeline** — the
   isolated-executor `[4]` section in the sample loads a fresh pipeline
   per worker for demonstration. In production you would load one
   pipeline per worker thread at startup and reuse it across requests,
   pulling prompts from a queue.
3. **Persistent venv** — pass `.WithWorkingDirectory("./.dotnetpy-env")`
   to `PythonProjectBuilder` so each launch reuses the same Python
   environment instead of creating a temp dir.

## Memory note

Each loaded SD-Turbo pipeline holds ~2 GB of weights in RAM. The sample
caps the isolated-executor section at 2 concurrent workers to stay
comfortable on a 16 GB machine. If you bump that number, account for
~2 GB per additional worker.

## Related

- [`samples/ml-embeddings`](../ml-embeddings/) — semantic search with
  `sentence-transformers`. NLP modality, smaller deps.
- [`samples/ml-whisper`](../ml-whisper/) — speech-to-text with Whisper.
  Audio modality, ~290 MB model, fast CPU inference.
- [`docs/FREETHREADED-AUDIT.md`](../../docs/FREETHREADED-AUDIT.md) — the
  audit that backs the `Python.CreateIsolated()` story.
