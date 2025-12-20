#!/usr/bin/env dotnet run
#:sdk Microsoft.NET.Sdk.Web
#:project ..\..\DotNetPy\DotNetPy.csproj

// =============================================================================
// DotNetPy + ASP.NET Core Minimal API Example
// =============================================================================
// 
// Example exposing Python Sentiment Analysis as a Web API
//
// Prerequisites:
//   1. .NET 10 SDK
//   2. uv installed (https://docs.astral.sh/uv/)
//   3. Python packages: uv pip install textblob
//   4. TextBlob corpora: python -m textblob.download_corpora
//
// Usage:
//   dotnet run
//   curl -X POST http://localhost:5000/api/analyze -H "Content-Type: application/json" -d '{"text":"I love this product!"}'
//
// =============================================================================

using DotNetPy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// JSON options configuration (AOT compatible)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});

var app = builder.Build();

// =============================================================================
// Python Worker Thread - Process all Python calls on a single thread
// =============================================================================
var pythonWorkQueue = new BlockingCollection<PythonWorkItem>();
var pythonWorkerCts = new CancellationTokenSource();
var pythonReady = new ManualResetEventSlim(false);
Exception? pythonInitError = null;

// Python 전용 워커 스레드 시작
var pythonWorkerThread = new Thread(() =>
{
    // Python initialization is performed on this thread
    try
    {
        Python.Initialize();
        var python = Python.GetInstance();
        
        // Preload TextBlob
        python.Execute("from textblob import TextBlob");
        Console.WriteLine("✓ TextBlob initialized successfully");
        
        pythonReady.Set();
        
        // Work processing loop
        foreach (var workItem in pythonWorkQueue.GetConsumingEnumerable(pythonWorkerCts.Token))
        {
            try
            {
                var result = workItem.Work(python);
                workItem.TaskCompletionSource.SetResult(result);
            }
            catch (Exception ex)
            {
                workItem.TaskCompletionSource.SetException(ex);
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Normal shutdown
    }
    catch (Exception ex)
    {
        pythonInitError = ex;
        pythonReady.Set();
    }
})
{
    Name = "PythonWorker",
    IsBackground = true
};
pythonWorkerThread.Start();

// Wait for worker thread to be ready
pythonReady.Wait();
if (pythonInitError != null)
{
    Console.WriteLine($"✗ Python initialization failed: {pythonInitError.Message}");
    Console.WriteLine("  Install with: uv pip install textblob && python -m textblob.download_corpora");
    return 1;
}

// Helper to submit Python work to the worker thread
Task<object?> ExecutePythonAsync(Func<DotNetPyExecutor, object?> work)
{
    var workItem = new PythonWorkItem(work);
    pythonWorkQueue.Add(workItem);
    return workItem.TaskCompletionSource.Task;
}

// =============================================================================
// API Endpoints
// =============================================================================

// Health check
app.MapGet("/", () => Results.Ok(new HealthCheckResponse("healthy", "DotNetPy Sentiment API")));

// Sentiment Analysis API
app.MapPost("/api/analyze", async (SentimentRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new ErrorResponse("Text is required"));
    }

    try
    {
        var response = await ExecutePythonAsync(python =>
        {
            using var result = python.ExecuteAndCapture(@"
from textblob import TextBlob

blob = TextBlob(text)
result = {
    'polarity': blob.sentiment.polarity,
    'subjectivity': blob.sentiment.subjectivity,
    'word_count': len(blob.words),
    'sentence_count': len(blob.sentences),
    'noun_phrases': list(blob.noun_phrases)[:10]
}
", new Dictionary<string, object?> { { "text", request.Text } });

            var polarity = result?.GetDouble("polarity") ?? 0;
            var subjectivity = result?.GetDouble("subjectivity") ?? 0;

            return new SentimentResponse
            {
                Text = request.Text,
                Polarity = polarity,
                Subjectivity = subjectivity,
                Sentiment = polarity switch
                {
                    > 0.1 => "positive",
                    < -0.1 => "negative",
                    _ => "neutral"
                },
                WordCount = result?.GetInt32("word_count") ?? 0,
                SentenceCount = result?.GetInt32("sentence_count") ?? 0
            };
        });

        return Results.Ok((SentimentResponse)response!);
    }
    catch (DotNetPyException ex)
    {
        return Results.Problem($"Python error: {ex.Message}");
    }
});

// Batch Analysis API
app.MapPost("/api/analyze/batch", async (BatchSentimentRequest request) =>
{
    if (request.Texts == null || request.Texts.Length == 0)
    {
        return Results.BadRequest(new ErrorResponse("Texts array is required"));
    }

    try
    {
        var response = await ExecutePythonAsync(python =>
        {
            using var result = python.ExecuteAndCapture(@"
from textblob import TextBlob

results = []
for text in texts:
    blob = TextBlob(text)
    pol = blob.sentiment.polarity
    results.append({
        'text': text[:100],
        'polarity': pol,
        'subjectivity': blob.sentiment.subjectivity,
        'sentiment': 'positive' if pol > 0.1 else ('negative' if pol < -0.1 else 'neutral')
    })

polarities = [r['polarity'] for r in results]
result = {
    'items': results,
    'average_polarity': sum(polarities) / len(polarities) if polarities else 0,
    'positive_count': sum(1 for r in results if r['sentiment'] == 'positive'),
    'negative_count': sum(1 for r in results if r['sentiment'] == 'negative'),
    'neutral_count': sum(1 for r in results if r['sentiment'] == 'neutral')
}
", new Dictionary<string, object?> { { "texts", request.Texts } });

            return new BatchSentimentResponse
            {
                TotalCount = request.Texts.Length,
                AveragePolarity = result?.GetDouble("average_polarity") ?? 0,
                PositiveCount = result?.GetInt32("positive_count") ?? 0,
                NegativeCount = result?.GetInt32("negative_count") ?? 0,
                NeutralCount = result?.GetInt32("neutral_count") ?? 0
            };
        });

        return Results.Ok((BatchSentimentResponse)response!);
    }
    catch (DotNetPyException ex)
    {
        return Results.Problem($"Python error: {ex.Message}");
    }
});

// Keyword Extraction API
app.MapPost("/api/keywords", async (SentimentRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new ErrorResponse("Text is required"));
    }

    try
    {
        var response = await ExecutePythonAsync(python =>
        {
            using var result = python.ExecuteAndCapture(@"
from textblob import TextBlob
from collections import Counter

blob = TextBlob(text)
noun_phrases = list(blob.noun_phrases)

stopwords = {'the', 'a', 'an', 'is', 'are', 'was', 'were', 'be', 'been', 'being',
             'have', 'has', 'had', 'do', 'does', 'did', 'will', 'would', 'could',
             'should', 'may', 'might', 'must', 'shall', 'can', 'to', 'of', 'in',
             'for', 'on', 'with', 'at', 'by', 'from', 'as', 'into', 'through',
             'and', 'or', 'but', 'if', 'then', 'else', 'when', 'where', 'why',
             'how', 'all', 'each', 'every', 'both', 'few', 'more', 'most', 'other',
             'some', 'such', 'no', 'not', 'only', 'own', 'same', 'so', 'than',
             'too', 'very', 'just', 'it', 'its', 'this', 'that', 'these', 'those'}

words = [word.lower() for word in blob.words if word.lower() not in stopwords and len(word) > 2]
word_freq = Counter(words).most_common(10)

result = {
    'noun_phrases': noun_phrases[:15],
    'top_words': [{'word': w, 'count': c} for w, c in word_freq],
    'language': str(blob.detect_language()) if len(text) > 20 else 'unknown'
}
", new Dictionary<string, object?> { { "text", request.Text } });

            return result?.ToDictionary();
        });

        return Results.Ok((Dictionary<string, object?>)response!);
    }
    catch (DotNetPyException ex)
    {
        return Results.Problem($"Python error: {ex.Message}");
    }
});

// Python environment info
app.MapGet("/api/info", () =>
{
    var pythonInfo = Python.CurrentPythonInfo;
    return Results.Ok(new InfoResponse
    {
        Python = new PythonInfoResponse
        {
            Version = pythonInfo?.Version?.ToString(),
            Architecture = pythonInfo?.Architecture.ToString(),
            Source = pythonInfo?.Source.ToString(),
            Executable = pythonInfo?.ExecutablePath
        },
        Dotnet = new DotnetInfoResponse
        {
            Version = Environment.Version.ToString(),
            Os = Environment.OSVersion.ToString()
        }
    });
});

// Cleanup on shutdown
app.Lifetime.ApplicationStopping.Register(() =>
{
    pythonWorkerCts.Cancel();
    pythonWorkQueue.CompleteAdding();
});

Console.WriteLine("\n🚀 DotNetPy Sentiment API is running!");
Console.WriteLine("   http://localhost:5000\n");
Console.WriteLine("Endpoints:");
Console.WriteLine("  GET  /              - Health check");
Console.WriteLine("  POST /api/analyze   - Analyze sentiment of text");
Console.WriteLine("  POST /api/analyze/batch - Batch sentiment analysis");
Console.WriteLine("  POST /api/keywords  - Extract keywords from text");
Console.WriteLine("  GET  /api/info      - Python/Runtime info\n");

app.Run("http://localhost:5000");

return 0;

// =============================================================================
// Python Worker Infrastructure
// =============================================================================

class PythonWorkItem
{
    public Func<DotNetPyExecutor, object?> Work { get; }
    public TaskCompletionSource<object?> TaskCompletionSource { get; } = new();

    public PythonWorkItem(Func<DotNetPyExecutor, object?> work)
    {
        Work = work;
    }
}

// =============================================================================
// Request/Response Models
// =============================================================================

record SentimentRequest(string Text);

record BatchSentimentRequest(string[] Texts);

record SentimentResponse
{
    public string Text { get; init; } = "";
    public double Polarity { get; init; }
    public double Subjectivity { get; init; }
    public string Sentiment { get; init; } = "";
    public int WordCount { get; init; }
    public int SentenceCount { get; init; }
}

record BatchSentimentResponse
{
    public int TotalCount { get; init; }
    public double AveragePolarity { get; init; }
    public int PositiveCount { get; init; }
    public int NegativeCount { get; init; }
    public int NeutralCount { get; init; }
}

record HealthCheckResponse(string Status, string Service);

record ErrorResponse(string Error);

record InfoResponse
{
    public PythonInfoResponse? Python { get; init; }
    public DotnetInfoResponse? Dotnet { get; init; }
}

record PythonInfoResponse
{
    public string? Version { get; init; }
    public string? Architecture { get; init; }
    public string? Source { get; init; }
    public string? Executable { get; init; }
}

record DotnetInfoResponse
{
    public string? Version { get; init; }
    public string? Os { get; init; }
}

// AOT JSON 직렬화 컨텍스트
[JsonSerializable(typeof(SentimentRequest))]
[JsonSerializable(typeof(BatchSentimentRequest))]
[JsonSerializable(typeof(SentimentResponse))]
[JsonSerializable(typeof(BatchSentimentResponse))]
[JsonSerializable(typeof(HealthCheckResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(InfoResponse))]
[JsonSerializable(typeof(PythonInfoResponse))]
[JsonSerializable(typeof(DotnetInfoResponse))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
