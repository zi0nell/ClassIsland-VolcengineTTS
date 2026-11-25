using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClassIsland.VolcengineTTS.Models;
using Microsoft.Extensions.Logging;

namespace ClassIsland.VolcengineTTS.Services;

/// <summary>
/// 火山语音引擎合成异常。
/// </summary>
public sealed class VolcengineTtsException : Exception
{
    public int? ErrorCode { get; }
    public HttpStatusCode? HttpStatusCode { get; }

    public VolcengineTtsException(string message, int? errorCode = null, HttpStatusCode? httpStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        HttpStatusCode = httpStatusCode;
    }
}

/// <summary>
/// 火山语音引擎（豆包语音合成 V3 大模型 &amp; V1 经典接口）HTTP 客户端。
/// </summary>
public sealed class VolcengineTtsClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<VolcengineTtsClient> _logger;

    public VolcengineTtsClient(ILogger<VolcengineTtsClient> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan // 每次请求通过 CancellationTokenSource 控制超时
        };
    }

    public async Task<byte[]> SynthesizeAsync(
        string text,
        VolcengineTtsSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<byte>();
        }

        ValidateSettings(settings);

        var maxRetries = Math.Max(0, settings.MaxRetryCount);
        Exception? lastException = null;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.RequestTimeoutSeconds, 3, 120)));

            try
            {
                return settings.Protocol == VolcengineProtocol.V3BigModel
                    ? await SynthesizeV3Async(text, settings, timeoutCts.Token)
                    : await SynthesizeV1Async(text, settings, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (VolcengineTtsException ex) when (!IsTransientError(ex) || attempt == maxRetries)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                lastException = ex;
                var delay = TimeSpan.FromMilliseconds(300 * Math.Pow(2, attempt));
                _logger.LogWarning(ex, "火山引擎 TTS 请求失败，准备进行第 {Attempt}/{MaxRetries} 次重试", attempt + 1, maxRetries);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
                break;
            }
        }

        throw lastException ?? new VolcengineTtsException("火山语音合成失败。");
    }

    private async Task<byte[]> SynthesizeV3Async(
        string text,
        VolcengineTtsSettings settings,
        CancellationToken cancellationToken)
    {
        var endpoint = settings.ResolveEndpoint();
        var requestId = Guid.NewGuid().ToString();
        var format = NormalizeRequestFormat(settings.AudioFormat);
        var sampleRate = NormalizeSampleRate(settings.SampleRate);

        var audioParams = new JsonObject
        {
            ["format"] = format,
            ["sample_rate"] = sampleRate,
            ["speech_rate"] = settings.SpeechRatePercent,
            ["loudness_rate"] = settings.LoudnessPercent,
            ["enable_timestamp"] = false
        };

        if (!string.IsNullOrWhiteSpace(settings.Emotion))
        {
            audioParams["emotion"] = settings.Emotion.Trim();
            audioParams["emotion_scale"] = settings.EmotionScale;
        }

        var payload = new JsonObject
        {
            ["user"] = new JsonObject
            {
                ["uid"] = string.IsNullOrWhiteSpace(settings.UserId) ? "classisland-user" : settings.UserId.Trim()
            },
            ["req_params"] = new JsonObject
            {
                ["text"] = text,
                ["speaker"] = settings.Speaker.Trim(),
                ["audio_params"] = audioParams
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        var resourceId = string.IsNullOrWhiteSpace(settings.ResourceId) ? "seed-tts-2.0" : settings.ResourceId.Trim();
        var token = settings.AccessToken.Trim();

        request.Headers.TryAddWithoutValidation("X-Api-Resource-Id", resourceId);
        request.Headers.TryAddWithoutValidation("X-Api-Request-Id", requestId);

        if (!string.IsNullOrWhiteSpace(settings.AppId))
        {
            request.Headers.TryAddWithoutValidation("X-Api-App-Id", settings.AppId.Trim());
            request.Headers.TryAddWithoutValidation("X-Api-Access-Key", token);
        }
        else
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", token);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new VolcengineTtsException(
                $"火山 V3 接口返回 HTTP {(int)response.StatusCode}: {ExtractErrorMessage(errorBody)}",
                httpStatusCode: response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        using var audioBuffer = new MemoryStream();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // 兼容可能带 SSE 前缀的分片
            var jsonLine = line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? line[5..].Trim()
                : line.Trim();

            if (string.IsNullOrEmpty(jsonLine) || jsonLine == "[DONE]")
            {
                continue;
            }

            using var doc = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;

            var code = root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number
                ? codeEl.GetInt32()
                : 0;

            if (code == 20000000)
            {
                // V3 流式结束标识
                break;
            }

            if (code != 0)
            {
                var msg = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "未知错误";
                throw new VolcengineTtsException($"火山 V3 合成失败 (code={code}): {msg}", errorCode: code);
            }

            if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.String)
            {
                var base64 = dataEl.GetString();
                if (!string.IsNullOrWhiteSpace(base64))
                {
                    var chunk = Convert.FromBase64String(base64);
                    await audioBuffer.WriteAsync(chunk, cancellationToken);
                }
            }
        }

        var rawAudio = audioBuffer.ToArray();
        if (rawAudio.Length == 0)
        {
            throw new VolcengineTtsException("火山 V3 接口未返回任何有效音频数据。");
        }

        return format == "pcm" ? WrapPcmAsWav(rawAudio, sampleRate) : rawAudio;
    }

    private async Task<byte[]> SynthesizeV1Async(
        string text,
        VolcengineTtsSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.AppId))
        {
            throw new VolcengineTtsException("使用 V1 经典接口时必须填写 App ID。");
        }

        var endpoint = settings.ResolveEndpoint();
        var format = NormalizeRequestFormat(settings.AudioFormat);
        var sampleRate = NormalizeSampleRate(settings.SampleRate);
        var speedRatio = Math.Clamp(1.0 + settings.SpeechRatePercent / 100.0, 0.2, 3.0);
        var volumeRatio = Math.Clamp(1.0 + settings.LoudnessPercent / 100.0, 0.1, 3.0);

        var audioObj = new JsonObject
        {
            ["voice_type"] = settings.Speaker.Trim(),
            ["encoding"] = format,
            ["rate"] = sampleRate,
            ["speed_ratio"] = Math.Round(speedRatio, 2),
            ["volume_ratio"] = Math.Round(volumeRatio, 2),
            ["pitch_ratio"] = Math.Round(settings.PitchRatio, 2)
        };

        if (!string.IsNullOrWhiteSpace(settings.Emotion))
        {
            audioObj["emotion"] = settings.Emotion.Trim();
            audioObj["enable_emotion"] = true;
            audioObj["emotion_scale"] = settings.EmotionScale;
        }

        var payload = new JsonObject
        {
            ["app"] = new JsonObject
            {
                ["appid"] = settings.AppId.Trim(),
                ["token"] = settings.AccessToken.Trim(),
                ["cluster"] = string.IsNullOrWhiteSpace(settings.Cluster) ? "volcano_tts" : settings.Cluster.Trim()
            },
            ["user"] = new JsonObject
            {
                ["uid"] = string.IsNullOrWhiteSpace(settings.UserId) ? "classisland-user" : settings.UserId.Trim()
            },
            ["audio"] = audioObj,
            ["request"] = new JsonObject
            {
                ["reqid"] = Guid.NewGuid().ToString(),
                ["text"] = text,
                ["text_type"] = "plain",
                ["operation"] = "query"
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer;{settings.AccessToken.Trim()}");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new VolcengineTtsException(
                $"火山 V1 接口返回 HTTP {(int)response.StatusCode}: {ExtractErrorMessage(body)}",
                httpStatusCode: response.StatusCode);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var code = root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number
            ? codeEl.GetInt32()
            : -1;

        if (code != 3000)
        {
            var msg = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : body;
            throw new VolcengineTtsException($"火山 V1 合成失败 (code={code}): {msg}", errorCode: code);
        }

        if (!root.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.String)
        {
            throw new VolcengineTtsException("火山 V1 响应中缺少音频 data 字段。");
        }

        var base64 = dataEl.GetString();
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new VolcengineTtsException("火山 V1 响应的音频 data 为空。");
        }

        var rawAudio = Convert.FromBase64String(base64);
        return format == "pcm" ? WrapPcmAsWav(rawAudio, sampleRate) : rawAudio;
    }

    private static void ValidateSettings(VolcengineTtsSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            throw new VolcengineTtsException("未配置火山引擎 Access Token / API Key。");
        }

        if (string.IsNullOrWhiteSpace(settings.Speaker))
        {
            throw new VolcengineTtsException("未配置音色 ID（Speaker / Voice Type）。");
        }

        if (!Uri.TryCreate(settings.ResolveEndpoint(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new VolcengineTtsException("API 地址格式无效，请检查是否包含 http:// 或 https://。");
        }
    }

    private static bool IsTransientError(VolcengineTtsException ex)
    {
        if (ex.HttpStatusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.RequestTimeout
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout)
        {
            return true;
        }

        return false;
    }

    private static string NormalizeRequestFormat(string? format)
    {
        var normalized = (format ?? "mp3").Trim().ToLowerInvariant();
        return normalized is "mp3" or "wav" or "pcm" ? normalized : "mp3";
    }

    private static int NormalizeSampleRate(int sampleRate)
    {
        return sampleRate is 8000 or 16000 or 24000 ? sampleRate : 24000;
    }

    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "无响应体";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var msgEl))
            {
                return msgEl.GetString() ?? body;
            }
        }
        catch
        {
            // Ignore parse error and return truncated raw body
        }

        return body.Length > 300 ? body[..300] : body;
    }

    /// <summary>
    /// 为 16-bit 单声道 PCM 裸流添加标准 44 字节 RIFF/WAV 头，便于 ClassIsland 音频引擎播放。
    /// </summary>
    private static byte[] WrapPcmAsWav(byte[] pcmData, int sampleRate)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        var byteRate = sampleRate * channels * (bitsPerSample / 8);
        const short blockAlign = channels * (bitsPerSample / 8);

        var wav = new byte[44 + pcmData.Length];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4, 4), 36 + pcmData.Length);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(wav, 12);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16, 4), 16); // Subchunk1Size (PCM)
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20, 2), 1);  // AudioFormat (PCM = 1)
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22, 2), channels);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32, 2), blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34, 2), bitsPerSample);
        Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40, 4), pcmData.Length);
        Buffer.BlockCopy(pcmData, 0, wav, 44, pcmData.Length);
        return wav;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
