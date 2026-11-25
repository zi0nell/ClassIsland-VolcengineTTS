using System.IO;
using System.Security.Cryptography;
using System.Text;
using ClassIsland.VolcengineTTS.Models;
using Microsoft.Extensions.Logging;

namespace ClassIsland.VolcengineTTS.Services;

/// <summary>
/// 本地 TTS 音频短语持久化缓存服务，避免相同播报内容重复消耗接口额度。
/// </summary>
public sealed class TtsAudioCacheService
{
    private readonly string _cacheDirectory;
    private readonly ILogger<TtsAudioCacheService> _logger;
    private readonly SemaphoreSlim _pruneLock = new(1, 1);

    public TtsAudioCacheService(string cacheDirectory, ILogger<TtsAudioCacheService> logger)
    {
        _cacheDirectory = cacheDirectory;
        _logger = logger;
        Directory.CreateDirectory(_cacheDirectory);
    }

    public string CacheDirectory => _cacheDirectory;

    public string BuildCacheKey(string text, VolcengineTtsSettings settings)
    {
        var payload = string.Join('|',
            settings.Protocol,
            settings.Protocol == VolcengineProtocol.V3BigModel ? settings.ResourceId : settings.Cluster,
            settings.Speaker.Trim(),
            NormalizeFormat(settings.AudioFormat),
            settings.SampleRate,
            settings.SpeechRatePercent,
            settings.LoudnessPercent,
            settings.PitchRatio.ToString("F2"),
            settings.Emotion.Trim(),
            settings.EmotionScale.ToString("F2"),
            text);

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public async Task<byte[]?> TryGetAsync(string key, string format, CancellationToken cancellationToken)
    {
        var path = GetCacheFilePath(key, format);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var data = await File.ReadAllBytesAsync(path, cancellationToken);
            if (data.Length == 0)
            {
                return null;
            }

            // 更新访问时间以便容量淘汰时保留高频短语
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return data;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "读取 TTS 缓存文件失败: {Path}", path);
            return null;
        }
    }

    public async Task SaveAsync(string key, string format, byte[] audioBytes, int maxCacheSizeMb, CancellationToken cancellationToken)
    {
        if (audioBytes.Length == 0)
        {
            return;
        }

        Directory.CreateDirectory(_cacheDirectory);
        var path = GetCacheFilePath(key, format);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            await File.WriteAllBytesAsync(tempPath, audioBytes, cancellationToken);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "写入 TTS 缓存文件失败: {Path}", path);
            TryDeleteFile(tempPath);
            return;
        }

        await PruneIfNeededAsync(maxCacheSizeMb);
    }

    public Task ClearAsync()
    {
        return Task.Run(() =>
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_cacheDirectory))
            {
                TryDeleteFile(file);
            }
        });
    }

    public (int FileCount, long TotalBytes) GetCacheStats()
    {
        if (!Directory.Exists(_cacheDirectory))
        {
            return (0, 0);
        }

        var count = 0;
        long totalBytes = 0;
        foreach (var file in new DirectoryInfo(_cacheDirectory).EnumerateFiles())
        {
            if (file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count++;
            totalBytes += file.Length;
        }

        return (count, totalBytes);
    }

    private async Task PruneIfNeededAsync(int maxCacheSizeMb)
    {
        var maxBytes = (long)Math.Max(10, maxCacheSizeMb) * 1024L * 1024L;
        if (!await _pruneLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                return;
            }

            var files = new DirectoryInfo(_cacheDirectory)
                .GetFiles()
                .Where(f => !f.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();

            var totalBytes = files.Sum(f => f.Length);
            foreach (var file in files)
            {
                if (totalBytes <= maxBytes)
                {
                    break;
                }

                var length = file.Length;
                if (TryDeleteFile(file.FullName))
                {
                    totalBytes -= length;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理过期 TTS 缓存失败");
        }
        finally
        {
            _pruneLock.Release();
        }
    }

    private string GetCacheFilePath(string key, string format)
    {
        var ext = NormalizeFormat(format);
        return Path.Combine(_cacheDirectory, $"{key}.{ext}");
    }

    public static string NormalizeFormat(string? format)
    {
        var normalized = (format ?? "mp3").Trim().ToLowerInvariant();
        return normalized switch
        {
            "wav" => "wav",
            "pcm" => "wav", // PCM 合成后封装为 WAV 头以兼容播放器
            _ => "mp3"
        };
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
