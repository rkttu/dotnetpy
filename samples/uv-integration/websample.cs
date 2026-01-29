#!/usr/bin/env dotnet run
#:sdk Microsoft.NET.Sdk.Web
#:project ../../src/DotNetPy/DotNetPy.csproj

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

// HTML Test UI
app.MapGet("/", () => Results.Content("""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>DotNetPy Sentiment API</title>
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); min-height: 100vh; padding: 20px; }
        .container { max-width: 900px; margin: 0 auto; }
        h1 { color: white; text-align: center; margin-bottom: 30px; text-shadow: 2px 2px 4px rgba(0,0,0,0.2); }
        .card { background: white; border-radius: 12px; padding: 24px; margin-bottom: 20px; box-shadow: 0 10px 40px rgba(0,0,0,0.2); }
        .card h2 { color: #333; margin-bottom: 16px; font-size: 1.3em; border-bottom: 2px solid #667eea; padding-bottom: 8px; }
        textarea { width: 100%; padding: 12px; border: 2px solid #e0e0e0; border-radius: 8px; font-size: 14px; resize: vertical; min-height: 100px; transition: border-color 0.3s; }
        textarea:focus { outline: none; border-color: #667eea; }
        .btn-group { display: flex; gap: 10px; margin-top: 16px; flex-wrap: wrap; }
        button { padding: 12px 24px; border: none; border-radius: 8px; font-size: 14px; font-weight: 600; cursor: pointer; transition: all 0.3s; }
        .btn-primary { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; }
        .btn-primary:hover { transform: translateY(-2px); box-shadow: 0 4px 12px rgba(102, 126, 234, 0.4); }
        .btn-secondary { background: #f0f0f0; color: #333; }
        .btn-secondary:hover { background: #e0e0e0; }
        .result { margin-top: 20px; padding: 16px; background: #f8f9fa; border-radius: 8px; display: none; }
        .result.show { display: block; }
        .result pre { white-space: pre-wrap; word-wrap: break-word; font-size: 13px; color: #333; }
        .result.error { background: #fee; border-left: 4px solid #e74c3c; }
        .result.success { background: #efe; border-left: 4px solid #27ae60; }
        .sentiment-badge { display: inline-block; padding: 4px 12px; border-radius: 20px; font-size: 12px; font-weight: 600; margin-left: 8px; }
        .sentiment-positive { background: #d4edda; color: #155724; }
        .sentiment-negative { background: #f8d7da; color: #721c24; }
        .sentiment-neutral { background: #fff3cd; color: #856404; }
        .info-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 16px; }
        .info-item { padding: 16px; background: #f8f9fa; border-radius: 8px; }
        .info-item label { font-size: 12px; color: #666; display: block; margin-bottom: 4px; }
        .info-item span { font-size: 16px; font-weight: 600; color: #333; }
        .loading { display: none; align-items: center; gap: 8px; color: #666; }
        .loading.show { display: flex; }
        .spinner { width: 20px; height: 20px; border: 3px solid #f3f3f3; border-top: 3px solid #667eea; border-radius: 50%; animation: spin 1s linear infinite; }
        @keyframes spin { 0% { transform: rotate(0deg); } 100% { transform: rotate(360deg); } }
        .stats { display: flex; gap: 20px; flex-wrap: wrap; margin-top: 12px; }
        .stat { text-align: center; }
        .stat-value { font-size: 24px; font-weight: 700; color: #667eea; }
        .stat-label { font-size: 12px; color: #666; }
    </style>
</head>
<body>
    <div class="container">
        <h1>🐍 DotNetPy Sentiment API</h1>
        
        <!-- Single Text Analysis -->
        <div class="card">
            <h2>📊 Sentiment Analysis</h2>
            <textarea id="singleText" placeholder="Enter text to analyze sentiment...">I absolutely love this product! It's amazing and works perfectly.</textarea>
            <div class="btn-group">
                <button class="btn-primary" onclick="analyzeSentiment()">Analyze Sentiment</button>
                <button class="btn-secondary" onclick="extractKeywords()">Extract Keywords</button>
            </div>
            <div class="loading" id="singleLoading"><div class="spinner"></div>Analyzing...</div>
            <div class="result" id="singleResult"></div>
        </div>

        <!-- Batch Analysis -->
        <div class="card">
            <h2>📦 Batch Analysis</h2>
            <textarea id="batchText" placeholder="Enter multiple texts (one per line)...">This is wonderful!
I'm not happy with the service.
The weather is okay today.
Best purchase I ever made!
Terrible experience, would not recommend.</textarea>
            <div class="btn-group">
                <button class="btn-primary" onclick="batchAnalyze()">Analyze Batch</button>
            </div>
            <div class="loading" id="batchLoading"><div class="spinner"></div>Processing batch...</div>
            <div class="result" id="batchResult"></div>
        </div>

        <!-- System Info -->
        <div class="card">
            <h2>ℹ️ System Information</h2>
            <button class="btn-secondary" onclick="getInfo()">Get System Info</button>
            <div class="loading" id="infoLoading"><div class="spinner"></div>Loading...</div>
            <div class="result" id="infoResult"></div>
        </div>
    </div>

    <script>
        async function apiCall(url, method = 'GET', body = null) {
            const options = { method, headers: { 'Content-Type': 'application/json' } };
            if (body) options.body = JSON.stringify(body);
            const response = await fetch(url, options);
            return response.json();
        }

        function showLoading(id, show) {
            document.getElementById(id).classList.toggle('show', show);
        }

        function showResult(id, data, isError = false) {
            const el = document.getElementById(id);
            el.className = 'result show ' + (isError ? 'error' : 'success');
            el.innerHTML = '<pre>' + JSON.stringify(data, null, 2) + '</pre>';
        }

        async function analyzeSentiment() {
            const text = document.getElementById('singleText').value;
            if (!text.trim()) return alert('Please enter some text');
            
            showLoading('singleLoading', true);
            try {
                const data = await apiCall('/api/analyze', 'POST', { text });
                const badge = `<span class="sentiment-badge sentiment-${data.sentiment}">${data.sentiment.toUpperCase()}</span>`;
                document.getElementById('singleResult').className = 'result show success';
                document.getElementById('singleResult').innerHTML = `
                    <strong>Sentiment: ${badge}</strong>
                    <div class="stats">
                        <div class="stat"><div class="stat-value">${data.polarity.toFixed(3)}</div><div class="stat-label">Polarity</div></div>
                        <div class="stat"><div class="stat-value">${data.subjectivity.toFixed(3)}</div><div class="stat-label">Subjectivity</div></div>
                        <div class="stat"><div class="stat-value">${data.wordCount}</div><div class="stat-label">Words</div></div>
                        <div class="stat"><div class="stat-value">${data.sentenceCount}</div><div class="stat-label">Sentences</div></div>
                    </div>
                `;
            } catch (e) {
                showResult('singleResult', { error: e.message }, true);
            }
            showLoading('singleLoading', false);
        }

        async function extractKeywords() {
            const text = document.getElementById('singleText').value;
            if (!text.trim()) return alert('Please enter some text');
            
            showLoading('singleLoading', true);
            try {
                const data = await apiCall('/api/keywords', 'POST', { text });
                showResult('singleResult', data);
            } catch (e) {
                showResult('singleResult', { error: e.message }, true);
            }
            showLoading('singleLoading', false);
        }

        async function batchAnalyze() {
            const texts = document.getElementById('batchText').value.split('\n').filter(t => t.trim());
            if (texts.length === 0) return alert('Please enter at least one text');
            
            showLoading('batchLoading', true);
            try {
                const data = await apiCall('/api/analyze/batch', 'POST', { texts });
                document.getElementById('batchResult').className = 'result show success';
                document.getElementById('batchResult').innerHTML = `
                    <div class="stats">
                        <div class="stat"><div class="stat-value">${data.totalCount}</div><div class="stat-label">Total</div></div>
                        <div class="stat"><div class="stat-value" style="color:#27ae60">${data.positiveCount}</div><div class="stat-label">Positive</div></div>
                        <div class="stat"><div class="stat-value" style="color:#e74c3c">${data.negativeCount}</div><div class="stat-label">Negative</div></div>
                        <div class="stat"><div class="stat-value" style="color:#f39c12">${data.neutralCount}</div><div class="stat-label">Neutral</div></div>
                        <div class="stat"><div class="stat-value">${data.averagePolarity.toFixed(3)}</div><div class="stat-label">Avg Polarity</div></div>
                    </div>
                `;
            } catch (e) {
                showResult('batchResult', { error: e.message }, true);
            }
            showLoading('batchLoading', false);
        }

        async function getInfo() {
            showLoading('infoLoading', true);
            try {
                const data = await apiCall('/api/info');
                document.getElementById('infoResult').className = 'result show success';
                document.getElementById('infoResult').innerHTML = `
                    <div class="info-grid">
                        <div class="info-item"><label>Python Version</label><span>${data.python?.version || 'N/A'}</span></div>
                        <div class="info-item"><label>Architecture</label><span>${data.python?.architecture || 'N/A'}</span></div>
                        <div class="info-item"><label>Source</label><span>${data.python?.source || 'N/A'}</span></div>
                        <div class="info-item"><label>.NET Version</label><span>${data.dotnet?.version || 'N/A'}</span></div>
                        <div class="info-item"><label>OS</label><span>${data.dotnet?.os || 'N/A'}</span></div>
                    </div>
                `;
            } catch (e) {
                showResult('infoResult', { error: e.message }, true);
            }
            showLoading('infoLoading', false);
        }
    </script>
</body>
</html>
""", "text/html"));

// Health check (JSON)
app.MapGet("/health", () => Results.Ok(new HealthCheckResponse("healthy", "DotNetPy Sentiment API")));

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

Console.WriteLine("Endpoints:");
Console.WriteLine("  GET  /              - Health check");
Console.WriteLine("  POST /api/analyze   - Analyze sentiment of text");
Console.WriteLine("  POST /api/analyze/batch - Batch sentiment analysis");
Console.WriteLine("  POST /api/keywords  - Extract keywords from text");
Console.WriteLine("  GET  /api/info      - Python/Runtime info");
Console.WriteLine();

app.Run();

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
