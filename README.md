# ClassIsland-VolcengineTTS

为 **ClassIsland 2.x** 开发的火山语音引擎（豆包语音合成 TTS）插件，支持在课前提醒、通知播报、自动化语音等场景接入火山引擎高品质语音合成服务。

## 功能特性

- **双协议支持**：
  - **豆包语音大模型 V3（单向流式 HTTP）**：`https://openspeech.bytedance.com/api/v3/tts/unidirectional`，支持 `seed-tts-2.0`、`seed-tts-1.0`、`seed-icl-2.0` 等资源模型及 NDJSON 流式分片拼接。
  - **经典语音合成 V1（HTTP）**：`https://openspeech.bytedance.com/api/v1/tts`，支持 `volcano_tts` 集群及 `BV..._streaming` 系列音色。
- **灵活鉴权与网关配置**：支持 `App ID + Access Token` 鉴权及 V3 `X-Api-Key` 鉴权，支持自定义 API 地址（反代/网关）。
- **音色预设与音频调节**：内置多款豆包大模型 2.0/1.0 与经典 V1 音色预设，支持自定义音色 ID、音频格式（MP3 / WAV / PCM）、采样率、语速、音量增益、音调及可选情感标签。
- **本地短语持久化缓存**：基于参数与文本 SHA-256 哈希缓存音频至插件配置目录，相同播报自动命中本地缓存，支持容量上限淘汰与一键清理。
- **队列管理与实时测试**：支持顺序播报与打断清空队列（`ClearSpeechQueue`），设置页内置实时合成与试听测试。

## 火山引擎控制台参数准备

1. 登录 [火山引擎控制台](https://console.volcengine.com/) 并开通 **豆包语音 / 语音合成（TTS）** 服务。
2. 创建应用并获取：
   - **App ID**
   - **Access Token**（或 V3 API Key）
   - **Resource ID**（V3 常用 `seed-tts-2.0` 或 `seed-tts-1.0`；声音复刻可使用 `seed-icl-2.0`）或 **Cluster**（V1 通常为 `volcano_tts`）。
3. 在音色列表中确认所选音色与当前 Resource ID / Cluster 匹配（例如 `zh_female_xiaohe_uranus_bigtts` 配合 `seed-tts-2.0`）。

## 构建与打包（.NET 8 SDK）

> 本仓库面向 ClassIsland 2.x（.NET 8 + Avalonia）。

```bash
dotnet build ClassIsland.VolcengineTTS/ClassIsland.VolcengineTTS.csproj --configuration Release
```

构建完成后，SDK 会自动在 `ClassIsland.VolcengineTTS/cipx/ClassIsland.VolcengineTTS.cipx` 生成插件安装包（也可直接推送至 GitHub 触发 `.github/workflows/build.yml` 自动构建并上传 Artifact）。

## 在 ClassIsland 中安装与启用

1. 打开 ClassIsland → **设置** → **插件** → **从本地安装**，选择生成的 `.cipx` 文件并重启 ClassIsland。
2. 前往 **设置** → **语音**，将语音提供方切换为 **火山语音引擎 TTS**。
3. 在下方配置面板填入 `App ID`、`Access Token`、`Resource ID` 和音色，点击 **测试合成并播放** 验证无误即可使用。
