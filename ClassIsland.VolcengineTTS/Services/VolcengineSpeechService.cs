using System.Collections.Concurrent;
using ClassIsland.Core.Abstractions.Services.SpeechService;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace ClassIsland.VolcengineTTS.Services;

/// <summary>
/// 火山语音引擎 TTS 服务实现，接入 ClassIsland 语音提供方管线。
/// </summary>
[SpeechProviderInfo("classisland.speech.volcengine-tts", "火山语音引擎 TTS")]
public sealed class VolcengineSpeechService : ISpeechService, IDisposable
{
    private readonly VolcengineAudioSynthesisService _synthesisService;
    private readonly ILogger<VolcengineSpeechService> _logger;

    private readonly ConcurrentQueue<string> _speechQueue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _queueControlLock = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Task _workerTask;

    private CancellationTokenSource _playbackCts = new();
    private bool _disposed;

    public VolcengineSpeechService(
        VolcengineAudioSynthesisService synthesisService,
        ILogger<VolcengineSpeechService> logger)
    {
        _synthesisService = synthesisService;
        _logger = logger;
        _workerTask = Task.Run(ProcessQueueLoopAsync);
    }

    public void EnqueueSpeechQueue(string text)
    {
        if (_disposed || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _speechQueue.Enqueue(text.Trim());
        _signal.Release();
    }

    public void ClearSpeechQueue()
    {
        while (_speechQueue.TryDequeue(out _))
        {
        }

        lock (_queueControlLock)
        {
            try
            {
                _playbackCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _playbackCts.Dispose();
            _playbackCts = new CancellationTokenSource();
        }
    }

    private CancellationToken GetCurrentPlaybackToken()
    {
        lock (_queueControlLock)
        {
            return _playbackCts.Token;
        }
    }

    private async Task ProcessQueueLoopAsync()
    {
        while (!_disposeCts.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(_disposeCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (!_speechQueue.TryDequeue(out var text))
            {
                continue;
            }

            var playbackToken = GetCurrentPlaybackToken();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, playbackToken);

            try
            {
                var (audioBytes, _) = await _synthesisService.SynthesizeAsync(text, linkedCts.Token);
                await _synthesisService.PlayAsync(audioBytes, null, linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("火山语音播报已取消: {Text}", text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "火山语音播报失败: {Text}", text);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();
        ClearSpeechQueue();

        try
        {
            _workerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore shutdown wait errors
        }

        lock (_queueControlLock)
        {
            _playbackCts.Dispose();
        }

        _disposeCts.Dispose();
        _signal.Dispose();
    }
}
