using System.IO;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.SpeechService;
using ClassIsland.VolcengineTTS.Models;
using Microsoft.Extensions.Logging;

namespace ClassIsland.VolcengineTTS.Services;

/// <summary>
/// 统一封装带本地缓存的语音合成与 ClassIsland 音频播放流程。
/// </summary>
public sealed class VolcengineAudioSynthesisService
{
    private readonly IAudioService _audioService;
    private readonly VolcengineTtsSettings _settings;
    private readonly VolcengineTtsClient _client;
    private readonly TtsAudioCacheService _cacheService;
    private readonly ILogger<VolcengineAudioSynthesisService> _logger;

    public VolcengineAudioSynthesisService(
        IAudioService audioService,
        VolcengineTtsSettings settings,
        VolcengineTtsClient client,
        TtsAudioCacheService cacheService,
        ILogger<VolcengineAudioSynthesisService> logger)
    {
        _audioService = audioService;
        _settings = settings;
        _client = client;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<(byte[] AudioBytes, bool HitCache)> SynthesizeAsync(
        string text,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (Array.Empty<byte>(), false);
        }

        var cacheKey = _cacheService.BuildCacheKey(text, _settings);
        if (_settings.EnableCache)
        {
            var cached = await _cacheService.TryGetAsync(cacheKey, _settings.AudioFormat, cancellationToken);
            if (cached is not null)
            {
                _logger.LogDebug("命中本地 TTS 缓存 ({Key})", cacheKey);
                return (cached, true);
            }
        }

        var audioBytes = await _client.SynthesizeAsync(text, _settings, cancellationToken);
        if (_settings.EnableCache && audioBytes.Length > 0)
        {
            await _cacheService.SaveAsync(
                cacheKey,
                _settings.AudioFormat,
                audioBytes,
                _settings.MaxCacheSizeMb,
                cancellationToken);
        }

        return (audioBytes, false);
    }

    public async Task PlayAsync(byte[] audioBytes, float? overrideVolume, CancellationToken cancellationToken)
    {
        if (audioBytes.Length == 0)
        {
            return;
        }

        var volume = overrideVolume ?? ResolveGlobalPlaybackVolume();
        await using var stream = new MemoryStream(audioBytes, writable: false);
        await _audioService.PlayAudioAsync(stream, volume, cancellationToken);
    }

    private static float ResolveGlobalPlaybackVolume()
    {
        try
        {
            var raw = ISpeechService.GlobalSettings?.SpeechVolume;
            if (raw is null)
            {
                return 1.0f;
            }

            var volume = Convert.ToSingle(raw);
            if (volume > 1.0f)
            {
                volume /= 100.0f;
            }

            return Math.Clamp(volume, 0.0f, 1.0f);
        }
        catch
        {
            return 1.0f;
        }
    }
}
