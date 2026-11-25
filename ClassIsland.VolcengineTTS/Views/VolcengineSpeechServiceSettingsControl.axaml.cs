using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Shared;
using ClassIsland.VolcengineTTS.Models;
using ClassIsland.VolcengineTTS.Services;

namespace ClassIsland.VolcengineTTS.Views;

/// <summary>
/// 火山语音引擎 TTS 设置面板控件。
/// </summary>
public partial class VolcengineSpeechServiceSettingsControl : SpeechProviderControlBase
{
    private readonly VolcengineTtsSettings _settings;
    private readonly TtsAudioCacheService _cacheService;
    private readonly VolcengineAudioSynthesisService _synthesisService;

    private CancellationTokenSource? _testCts;
    private bool _isInitializing;

    public VolcengineSpeechServiceSettingsControl()
        : this(
            IAppHost.GetService<VolcengineTtsSettings>(),
            IAppHost.GetService<TtsAudioCacheService>(),
            IAppHost.GetService<VolcengineAudioSynthesisService>())
    {
    }

    public VolcengineSpeechServiceSettingsControl(
        VolcengineTtsSettings settings,
        TtsAudioCacheService cacheService,
        VolcengineAudioSynthesisService synthesisService)
    {
        _settings = settings;
        _cacheService = cacheService;
        _synthesisService = synthesisService;

        InitializeComponent();
        DataContext = _settings;

        InitializeControls();
        RefreshCacheStats();
    }

    private void InitializeControls()
    {
        _isInitializing = true;
        try
        {
            var protocolCombo = this.FindControl<ComboBox>("ProtocolComboBox");
            if (protocolCombo is not null)
            {
                protocolCombo.SelectedIndex = _settings.Protocol == VolcengineProtocol.V3BigModel ? 0 : 1;
            }

            var presetCombo = this.FindControl<ComboBox>("PresetComboBox");
            if (presetCombo is not null)
            {
                presetCombo.ItemsSource = VolcengineTtsSettings.Presets;
                var current = VolcengineTtsSettings.Presets.FirstOrDefault(p =>
                    string.Equals(p.Id, _settings.Speaker, StringComparison.OrdinalIgnoreCase));
                presetCombo.SelectedItem = current;
            }

            var formatCombo = this.FindControl<ComboBox>("FormatComboBox");
            if (formatCombo is not null)
            {
                formatCombo.SelectedIndex = _settings.AudioFormat.ToLowerInvariant() switch
                {
                    "wav" => 1,
                    "pcm" => 2,
                    _ => 0
                };
            }

            var rateCombo = this.FindControl<ComboBox>("SampleRateComboBox");
            if (rateCombo is not null)
            {
                rateCombo.SelectedIndex = _settings.SampleRate switch
                {
                    16000 => 1,
                    8000 => 2,
                    _ => 0
                };
            }
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void ProtocolComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var oldProtocol = _settings.Protocol;
        var nextProtocol = Equals(item.Tag, "V1Classic")
            ? VolcengineProtocol.V1Classic
            : VolcengineProtocol.V3BigModel;

        if (oldProtocol == nextProtocol)
        {
            return;
        }

        _settings.Protocol = nextProtocol;

        // 当用户仍使用默认地址时，随协议自动切换默认接口地址
        if (string.IsNullOrWhiteSpace(_settings.ApiEndpoint) ||
            _settings.ApiEndpoint == VolcengineTtsSettings.DefaultV3Endpoint ||
            _settings.ApiEndpoint == VolcengineTtsSettings.DefaultV1Endpoint)
        {
            _settings.ApiEndpoint = nextProtocol == VolcengineProtocol.V3BigModel
                ? VolcengineTtsSettings.DefaultV3Endpoint
                : VolcengineTtsSettings.DefaultV1Endpoint;
        }
    }

    private void PresetComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || sender is not ComboBox combo || combo.SelectedItem is not VolcengineVoicePreset preset)
        {
            return;
        }

        _settings.Protocol = preset.Protocol;
        _settings.Speaker = preset.Id;

        if (preset.Protocol == VolcengineProtocol.V3BigModel)
        {
            _settings.ResourceId = preset.RecommendedResourceOrCluster;
            if (_settings.ApiEndpoint == VolcengineTtsSettings.DefaultV1Endpoint)
            {
                _settings.ApiEndpoint = VolcengineTtsSettings.DefaultV3Endpoint;
            }
        }
        else
        {
            _settings.Cluster = preset.RecommendedResourceOrCluster;
            if (_settings.ApiEndpoint == VolcengineTtsSettings.DefaultV3Endpoint)
            {
                _settings.ApiEndpoint = VolcengineTtsSettings.DefaultV1Endpoint;
            }
        }

        var protocolCombo = this.FindControl<ComboBox>("ProtocolComboBox");
        if (protocolCombo is not null)
        {
            _isInitializing = true;
            protocolCombo.SelectedIndex = preset.Protocol == VolcengineProtocol.V3BigModel ? 0 : 1;
            _isInitializing = false;
        }
    }

    private void FormatComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        _settings.AudioFormat = item.Tag?.ToString() ?? "mp3";
    }

    private void SampleRateComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (int.TryParse(item.Tag?.ToString(), out var sampleRate))
        {
            _settings.SampleRate = sampleRate;
        }
    }

    private void ResourceIdPresetComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var value = item.Tag?.ToString();
        if (!string.IsNullOrWhiteSpace(value))
        {
            _settings.ResourceId = value;
        }
    }

    private void ShowTokenCheckBox_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        var tokenBox = this.FindControl<TextBox>("TokenTextBox");
        var checkBox = this.FindControl<CheckBox>("ShowTokenCheckBox");
        if (tokenBox is not null && checkBox is not null)
        {
            tokenBox.PasswordChar = checkBox.IsChecked == true ? '\0' : '•';
        }
    }

    private void ResetEndpointButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _settings.ApiEndpoint = _settings.Protocol == VolcengineProtocol.V3BigModel
            ? VolcengineTtsSettings.DefaultV3Endpoint
            : VolcengineTtsSettings.DefaultV1Endpoint;
    }

    private async void ClearCacheButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _cacheService.ClearAsync();
        RefreshCacheStats();
        SetStatus("已清空本地 TTS 音频缓存。");
    }

    private async void TestSpeakButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var testBox = this.FindControl<TextBox>("TestTextBox");
        var text = testBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("请先输入测试文本。");
            return;
        }

        _testCts?.Cancel();
        _testCts?.Dispose();
        _testCts = new CancellationTokenSource();
        var ct = _testCts.Token;

        SetTestingState(true);
        SetStatus("正在请求火山语音引擎合成...");

        try
        {
            var (audioBytes, hitCache) = await _synthesisService.SynthesizeAsync(text, ct);

            RefreshCacheStats();
            SetStatus(hitCache ? "命中本地缓存，正在播放..." : $"合成成功（{audioBytes.Length / 1024.0:F1} KB），正在播放...");

            await _synthesisService.PlayAsync(audioBytes, 1.0f, ct);
            SetStatus("测试播报完成。");
        }
        catch (OperationCanceledException)
        {
            SetStatus("已停止测试播报。");
        }
        catch (Exception ex)
        {
            SetStatus($"测试失败: {ex.Message}");
        }
        finally
        {
            SetTestingState(false);
        }
    }

    private void StopSpeakButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _testCts?.Cancel();
    }

    private void SetTestingState(bool isTesting)
    {
        var speakBtn = this.FindControl<Button>("TestSpeakButton");
        var stopBtn = this.FindControl<Button>("StopSpeakButton");
        if (speakBtn is not null)
        {
            speakBtn.IsEnabled = !isTesting;
        }

        if (stopBtn is not null)
        {
            stopBtn.IsEnabled = isTesting;
        }
    }

    private void RefreshCacheStats()
    {
        var statsText = this.FindControl<TextBlock>("CacheStatsTextBlock");
        if (statsText is null)
        {
            return;
        }

        var (count, bytes) = _cacheService.GetCacheStats();
        statsText.Text = $"{count} 个缓存文件，共 {bytes / (1024.0 * 1024.0):F2} MB";
    }

    private void SetStatus(string message)
    {
        var statusBlock = this.FindControl<TextBlock>("StatusTextBlock");
        if (statusBlock is null)
        {
            return;
        }

        statusBlock.Text = message;
        statusBlock.IsVisible = !string.IsNullOrWhiteSpace(message);
    }
}
