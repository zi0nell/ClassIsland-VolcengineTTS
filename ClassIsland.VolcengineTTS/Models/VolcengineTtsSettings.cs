using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ClassIsland.VolcengineTTS.Models;

/// <summary>
/// 火山语音引擎接入协议版本。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VolcengineProtocol
{
    /// <summary>
    /// 豆包语音大模型 V3 单向流式 HTTP 接口 (/api/v3/tts/unidirectional)。
    /// </summary>
    V3BigModel = 0,

    /// <summary>
    /// 火山引擎经典语音合成 V1 HTTP 接口 (/api/v1/tts)。
    /// </summary>
    V1Classic = 1
}

/// <summary>
/// 预设音色条目。
/// </summary>
public sealed record VolcengineVoicePreset(
    string Id,
    string DisplayName,
    VolcengineProtocol Protocol,
    string RecommendedResourceOrCluster)
{
    public override string ToString() => $"{DisplayName} ({Id})";
}

/// <summary>
/// 火山语音引擎 TTS 插件配置模型。
/// </summary>
public sealed class VolcengineTtsSettings : INotifyPropertyChanged
{
    public const string DefaultV3Endpoint = "https://openspeech.bytedance.com/api/v3/tts/unidirectional";
    public const string DefaultV1Endpoint = "https://openspeech.bytedance.com/api/v1/tts";

    private VolcengineProtocol _protocol = VolcengineProtocol.V3BigModel;
    private string _apiEndpoint = DefaultV3Endpoint;
    private string _appId = string.Empty;
    private string _accessToken = string.Empty;
    private string _resourceId = "seed-tts-2.0";
    private string _cluster = "volcano_tts";
    private string _speaker = "zh_female_xiaohe_uranus_bigtts";
    private string _audioFormat = "mp3";
    private int _sampleRate = 24000;
    private int _speechRatePercent = 0;
    private int _loudnessPercent = 0;
    private double _pitchRatio = 1.0;
    private string _emotion = string.Empty;
    private double _emotionScale = 1.0;
    private string _userId = "classisland-user";
    private bool _enableCache = true;
    private int _maxCacheSizeMb = 200;
    private int _requestTimeoutSeconds = 15;
    private int _maxRetryCount = 2;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 协议版本（V3 豆包大模型 / V1 经典接口）。
    /// </summary>
    public VolcengineProtocol Protocol
    {
        get => _protocol;
        set
        {
            if (SetField(ref _protocol, value))
            {
                OnPropertyChanged(nameof(IsV3Protocol));
                OnPropertyChanged(nameof(IsV1Protocol));
            }
        }
    }

    [JsonIgnore]
    public bool IsV3Protocol => Protocol == VolcengineProtocol.V3BigModel;

    [JsonIgnore]
    public bool IsV1Protocol => Protocol == VolcengineProtocol.V1Classic;

    /// <summary>
    /// 自定义 API 请求地址（留空时根据协议自动使用官方默认地址）。
    /// </summary>
    public string ApiEndpoint
    {
        get => _apiEndpoint;
        set => SetField(ref _apiEndpoint, value);
    }

    /// <summary>
    /// 火山引擎控制台应用的 App ID（使用 V3 纯 API Key 鉴权时可留空）。
    /// </summary>
    public string AppId
    {
        get => _appId;
        set => SetField(ref _appId, value);
    }

    /// <summary>
    /// Access Token / X-Api-Key。
    /// </summary>
    public string AccessToken
    {
        get => _accessToken;
        set => SetField(ref _accessToken, value);
    }

    /// <summary>
    /// V3 资源 ID（如 seed-tts-2.0、seed-tts-1.0、seed-icl-2.0）。
    /// </summary>
    public string ResourceId
    {
        get => _resourceId;
        set => SetField(ref _resourceId, value);
    }

    /// <summary>
    /// V1 业务集群（通常为 volcano_tts 或 volcano_icl）。
    /// </summary>
    public string Cluster
    {
        get => _cluster;
        set => SetField(ref _cluster, value);
    }

    /// <summary>
    /// 音色 ID（V3 的 speaker 或 V1 的 voice_type）。
    /// </summary>
    public string Speaker
    {
        get => _speaker;
        set => SetField(ref _speaker, value);
    }

    /// <summary>
    /// 音频编码格式（mp3 / wav / pcm）。
    /// </summary>
    public string AudioFormat
    {
        get => _audioFormat;
        set => SetField(ref _audioFormat, value);
    }

    /// <summary>
    /// 采样率（8000 / 16000 / 24000）。
    /// </summary>
    public int SampleRate
    {
        get => _sampleRate;
        set => SetField(ref _sampleRate, value);
    }

    /// <summary>
    /// 语速调节百分比（-50 到 100，0 为正常语速）。
    /// </summary>
    public int SpeechRatePercent
    {
        get => _speechRatePercent;
        set => SetField(ref _speechRatePercent, Math.Clamp(value, -50, 100));
    }

    /// <summary>
    /// 音量增益百分比（-50 到 100，0 为默认音量）。
    /// </summary>
    public int LoudnessPercent
    {
        get => _loudnessPercent;
        set => SetField(ref _loudnessPercent, Math.Clamp(value, -50, 100));
    }

    /// <summary>
    /// 音调倍率（主要供 V1 经典接口使用，0.1 到 3.0，默认 1.0）。
    /// </summary>
    public double PitchRatio
    {
        get => _pitchRatio;
        set => SetField(ref _pitchRatio, Math.Clamp(value, 0.1, 3.0));
    }

    /// <summary>
    /// 可选情感标签（如 happy、sad、angry、neutral 等，留空表示默认）。
    /// </summary>
    public string Emotion
    {
        get => _emotion;
        set => SetField(ref _emotion, value);
    }

    /// <summary>
    /// 情感强度（仅部分支持情感的音色生效，通常 1.0 - 5.0）。
    /// </summary>
    public double EmotionScale
    {
        get => _emotionScale;
        set => SetField(ref _emotionScale, Math.Clamp(value, 0.5, 5.0));
    }

    /// <summary>
    /// 请求体中的用户标识 uid。
    /// </summary>
    public string UserId
    {
        get => _userId;
        set => SetField(ref _userId, value);
    }

    /// <summary>
    /// 是否启用本地短语持久化缓存。
    /// </summary>
    public bool EnableCache
    {
        get => _enableCache;
        set => SetField(ref _enableCache, value);
    }

    /// <summary>
    /// 本地缓存最大空间（MB）。
    /// </summary>
    public int MaxCacheSizeMb
    {
        get => _maxCacheSizeMb;
        set => SetField(ref _maxCacheSizeMb, Math.Clamp(value, 10, 4096));
    }

    /// <summary>
    /// HTTP 请求超时时间（秒）。
    /// </summary>
    public int RequestTimeoutSeconds
    {
        get => _requestTimeoutSeconds;
        set => SetField(ref _requestTimeoutSeconds, Math.Clamp(value, 3, 120));
    }

    /// <summary>
    /// 失败重试次数。
    /// </summary>
    public int MaxRetryCount
    {
        get => _maxRetryCount;
        set => SetField(ref _maxRetryCount, Math.Clamp(value, 0, 5));
    }

    public string ResolveEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(ApiEndpoint))
        {
            return ApiEndpoint.Trim();
        }

        return Protocol == VolcengineProtocol.V3BigModel
            ? DefaultV3Endpoint
            : DefaultV1Endpoint;
    }

    public static IReadOnlyList<VolcengineVoicePreset> Presets { get; } = new[]
    {
        // 豆包语音大模型 V3 音色
        new VolcengineVoicePreset("zh_female_xiaohe_uranus_bigtts", "小何 2.0（女声·自然清晰）", VolcengineProtocol.V3BigModel, "seed-tts-2.0"),
        new VolcengineVoicePreset("zh_female_vv_uranus_bigtts", "Vivi 2.0（女声·温柔甜美）", VolcengineProtocol.V3BigModel, "seed-tts-2.0"),
        new VolcengineVoicePreset("zh_male_m191_uranus_bigtts", "云舟 2.0（男声·沉稳播报）", VolcengineProtocol.V3BigModel, "seed-tts-2.0"),
        new VolcengineVoicePreset("zh_female_shuangkuaisisi_emo_v2_mars_bigtts", "爽快思思（女声·多情感）", VolcengineProtocol.V3BigModel, "seed-tts-1.0"),
        new VolcengineVoicePreset("zh_female_roumeinvyou_emo_v2_mars_bigtts", "柔美女友（女声·温柔情感）", VolcengineProtocol.V3BigModel, "seed-tts-1.0"),
        new VolcengineVoicePreset("zh_male_yangguangqingnian_emo_v2_mars_bigtts", "阳光青年（男声·多情感）", VolcengineProtocol.V3BigModel, "seed-tts-1.0"),
        new VolcengineVoicePreset("zh_female_cancan_mars_bigtts", "灿灿（女声·通用大模型）", VolcengineProtocol.V3BigModel, "seed-tts-1.0"),

        // 经典 V1 音色
        new VolcengineVoicePreset("BV001_streaming", "通用女声（经典 BV001）", VolcengineProtocol.V1Classic, "volcano_tts"),
        new VolcengineVoicePreset("BV002_streaming", "通用男声（经典 BV002）", VolcengineProtocol.V1Classic, "volcano_tts"),
        new VolcengineVoicePreset("BV700_streaming", "灿灿（经典 BV700）", VolcengineProtocol.V1Classic, "volcano_tts"),
        new VolcengineVoicePreset("BV701_streaming", "擎苍（经典 BV701）", VolcengineProtocol.V1Classic, "volcano_tts"),
        new VolcengineVoicePreset("BV405_streaming", "甜美小源（经典 BV405）", VolcengineProtocol.V1Classic, "volcano_tts"),
        new VolcengineVoicePreset("BV123_streaming", "阳光青年（经典 BV123）", VolcengineProtocol.V1Classic, "volcano_tts")
    };

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
