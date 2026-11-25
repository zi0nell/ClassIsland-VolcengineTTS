using System.IO;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Shared.Helpers;
using ClassIsland.VolcengineTTS.Models;
using ClassIsland.VolcengineTTS.Services;
using ClassIsland.VolcengineTTS.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClassIsland.VolcengineTTS;

/// <summary>
/// 火山语音引擎 TTS 插件入口。
/// </summary>
[PluginEntrance]
public class Plugin : PluginBase
{
    private readonly object _saveLock = new();

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        Directory.CreateDirectory(PluginConfigFolder);

        var configPath = Path.Combine(PluginConfigFolder, "Settings.json");
        var settings = ConfigureFileHelper.LoadConfig<VolcengineTtsSettings>(configPath);

        settings.PropertyChanged += (_, _) =>
        {
            lock (_saveLock)
            {
                ConfigureFileHelper.SaveConfig(configPath, settings);
            }
        };

        var cacheDirectory = Path.Combine(PluginConfigFolder, "Cache");

        services.AddSingleton(settings);
        services.AddSingleton(sp =>
            new TtsAudioCacheService(cacheDirectory, sp.GetRequiredService<ILogger<TtsAudioCacheService>>()));
        services.AddSingleton<VolcengineTtsClient>();
        services.AddSingleton<VolcengineAudioSynthesisService>();

        services.AddSpeechProvider<VolcengineSpeechService, VolcengineSpeechServiceSettingsControl>();
    }
}
