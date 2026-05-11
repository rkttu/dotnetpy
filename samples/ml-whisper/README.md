# ml-whisper

End-to-end speech-to-text from C# using OpenAI's Whisper. The sample loads
`whisper-base.en` via HuggingFace `transformers`, transcribes an audio
file, returns the text plus chunk-level timestamps to .NET, then spins up
three isolated executors that each hold their own Whisper instance.

This is the audio counterpart to [`samples/ml-embeddings`](../ml-embeddings/):
the .NET side stays declarative and tiny, all the model work lives in
Python, and the boundary is a JSON round-trip.

## What it shows

1. **Declarative audio-ML environment** — `PythonProjectBuilder` provisions
   a uv venv with `transformers`, `librosa`, and `soundfile` on first run.
2. **Real ASR inference** — Whisper transcribes
   [`audio/jfk.flac`](audio/jfk.flac) (the OpenAI Whisper test clip of
   John F. Kennedy's 1961 inaugural address, ~11 seconds, U.S. public
   domain).
3. **Structured timestamps** — returns chunk objects with `start`, `end`,
   and `text` fields back to .NET.
4. **Isolated executors** — three workers, each with its own `asr`
   pipeline in a private Python namespace via `Python.CreateIsolated()`.
   The pattern unlocks genuine parallelism on free-threaded CPython once
   the audio stack is FT-ready; under classic GIL builds it still
   serialises through the interpreter.

## Requirements

- .NET 10 SDK (for the file-based-app `dotnet run sample.cs`)
- [uv](https://docs.astral.sh/uv/) on `PATH`
- ~1 GB free disk for the venv (transformers + torch + librosa) and the
  whisper-base.en model (~290 MB)

The sample pins Python to **3.12.x** and the HuggingFace stack to specific
releases (`transformers 4.40.2`, `tokenizers 0.19.1`, `safetensors 0.4.3`,
`torch 2.2–2.4`, `librosa 0.10.x`). Same rationale as
[`ml-embeddings/README.md`](../ml-embeddings/README.md): newer releases
have not shipped wheels for every CPython version yet and would otherwise
force a Rust source build on a clean Windows box.

## Run it

```bash
cd samples/ml-whisper
dotnet run sample.cs
```

The **first run takes a few minutes**: uv resolves Python 3.12 + ~1 GB of
wheels (much of it shared with `ml-embeddings`'s cache), then HuggingFace
downloads `whisper-base.en` (~290 MB). Subsequent runs are fast — the
venv and model are cached.

## Expected output

```text
=== DotNetPy Speech-to-Text Sample (Whisper) ===

[1] Declarative ML environment
  Resolving env (first run may download ~1 GB)... done in 45.8s

[2] Loading model (whisper-base.en, ~290 MB first run)
  Pipeline loaded in 55.1s

[3] Transcribe
  Audio: .../samples/ml-whisper/audio/jfk.flac
  Inference: 7.16s

  Transcript:
    "And so my fellow Americans, ask not what your country can do for you, ask what you can do for your country."

  Chunks (with timestamps):
    [  0.00s →  11.00s] And so my fellow Americans, ask not what your country can do for you, ask what you can do for your country.

[4] Isolated executors (per-worker namespace pattern)
  3 isolated workers, each with private `asr` pipeline: 12.0s
    Worker 0: text length=106, starts with "And so my fello…"
    Worker 1: text length=106, starts with "And so my fello…"
    Worker 2: text length=106, starts with "And so my fello…"

=== Sample Complete ===
```

Timings are from a first run on Windows 11 with broadband; the env-resolve
step is much faster on subsequent runs (uv reuses cached wheels).
Transcript text is deterministic on the same model + audio.

## Audio file attribution

[`audio/jfk.flac`](audio/jfk.flac) is the OpenAI Whisper test sample
([source](https://github.com/openai/whisper/blob/main/tests/jfk.flac)), an
~11 second excerpt of President John F. Kennedy's 1961 inaugural address.
As a work of the U.S. federal government, JFK's address is in the U.S.
public domain.

## Production patterns

For a real ASR service the shape stays similar but you would:

- **Persist the venv** by passing `.WithWorkingDirectory("./.dotnetpy-env")`
  to `PythonProjectBuilder` so each launch reuses the same environment.
- **Load the pipeline once** per worker rather than per request. The
  `Python.CreateIsolated()` pattern in `[4]` is the natural shape for a
  pool: N executors, each with a hot `asr` pipeline, serving inbound jobs.
- **Pick the right Whisper size**: `tiny.en` (~150 MB) for low-latency
  smoke transcription, `base.en` (~290 MB) for a balance of quality and
  speed, `small.en` (~970 MB) or `large-v3` (~3 GB) for accuracy-critical
  workloads — pass the model name through `.WithUvSetting(...)` or a
  config file.

## Related

- [`samples/ml-embeddings`](../ml-embeddings/) — semantic search with
  `sentence-transformers`. Different modality (NLP), same DotNetPy
  patterns.
- [`samples/ml-image-gen`](../ml-image-gen/) — text-to-image with Stable
  Diffusion Turbo. Another modality (vision), same DotNetPy patterns.
- [`docs/FREETHREADED-AUDIT.md`](../../docs/FREETHREADED-AUDIT.md) — the
  audit that backs the `Python.CreateIsolated()` story.
