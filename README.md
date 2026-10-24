# 声屿 · Aisound Studio

Windows 桌面音频工具，提供人声分离、音色训练与转换、翻唱、视频转音频和本地音色库。

## 下载与安装

Windows 安装程序会显示在 [Releases](https://github.com/UniverseFires/AisoundStudio/releases) 页面。GitHub Actions 会构建安装包并检查安装和启动；检查通过后，发布版本时会将 `AisoundStudio-Setup.exe` 附到该版本。

在 Releases 页面下载 `AisoundStudio-Setup.exe`，双击启动安装。安装开始时可选择简体中文或 English，随后可以选择安装目录；默认位置为 `%LOCALAPPDATA%\Programs\AisoundStudio`。

> 作者是开源新手，项目仍在持续学习和完善。欢迎大家提问、指出问题或给予建议，我会认真记录并逐步改进。

## 第一次使用

1. 安装并启动后，可先到“环境与设置”查看运行环境状态。
2. 首次使用翻唱、人声分离或训练时，如果 AI 环境缺失，软件会询问是否下载，并显示保存位置和组件来源；完整环境需要数 GB 网络流量。
3. 在“音色库”搜索社区目录，核对来源后下载自己有权使用的音色，再到“制作翻唱”选择歌曲和音色。

完整 AI 翻唱需要兼容的 NVIDIA 显卡和驱动。遇到问题时，欢迎提交 GitHub Issue，并附上 Windows 版本、显卡型号、操作步骤和错误日志；请勿公开上传私人音频或未经许可的模型。

## 隐私与本地文件

- 安装包只包含程序源码构建结果，不包含开发者的音色、任务历史、导出音频、训练素材、下载缓存或本机绝对路径。
- 程序运行时在软件目录保存自己的设置和任务记录；在线音色下载到软件目录的 `voices` 文件夹。新安装不会带入开发者机器上的记录。
- AI 环境默认为软件目录下的 `AIEnvironment` 子文件夹。用户可以在下载确认框或设置页更改位置。
- 卸载程序不会主动删除用户创建的 AI 环境、音色和记录文件。

安装器默认按当前用户安装到 `%LOCALAPPDATA%\Programs\AisoundStudio`，不要求管理员权限；安装时可以更改目录。这样默认的 AI 环境子文件夹可写。

## AI 环境下载

只有在 AI 功能需要的环境缺失时，程序才询问是否下载。确认框会显示保存位置和来源；确认后才开始下载。首次安装会使用数 GB 网络流量。

下载组件的来源：

- RVC 引擎：[RVC Project](https://github.com/RVC-Project/Retrieval-based-Voice-Conversion-WebUI)
- Python 3.12：[Python Software Foundation](https://www.python.org/downloads/release/python-31210/)
- NVIDIA PyTorch：[PyTorch 官方 CUDA wheel 索引](https://download.pytorch.org/whl/)
- AI 模型：[Hugging Face 上的 VoiceConversionWebUI 模型库](https://huggingface.co/lj1995/VoiceConversionWebUI)

AI 音频处理目前要求受支持的 NVIDIA 显卡和驱动。当前引擎下载流程会按检测到的显卡选择 CUDA 组件；未检测到 NVIDIA 驱动时会停止，不会开始大型下载。Python 3.12.10 是 Python 3.12 系列最后提供 Windows 安装程序的版本；更新的 3.12 安全发行版目前由 Python.org 以源码形式发布。

## 在线音色

音色库可以搜索 [voice-models.com](https://voice-models.com/) 并将支持的 `.pth` 文件下载到本地。当前应用内直下支持 Google Drive、Hugging Face 和直接 `.pth` 文件；Hugging Face 仓库中的唯一匹配 `.index` 文件会一并下载。下载前会展示目录页、文件托管来源并要求确认。

voice-models.com 是社区目录，不是 RVC 官方模型库。上传者的授权、模型质量和安全性未由本项目核实。模型文件不随源码或安装包分发；只下载并使用你信任且有权使用的模型。目录网站没有稳定的官方搜索 API，程序使用其公开网页当前使用的查询端点；如果网站改版，搜索可能暂时不可用。

本仓库的 MIT 许可只覆盖本项目代码，不自动覆盖 RVC、Python、PyTorch、其他运行时组件或下载的音色模型；它们各自适用的许可由其上游项目或上传者决定。

## 构建 Windows 安装包

需要 Windows、.NET 10 SDK 和 Inno Setup 编译器（`ISCC.exe`）。运行：

```powershell
.\build-installer.ps1
```

安装程序输出到 `artifacts\AisoundStudio-Setup.exe`。构建脚本只从 `source` 项目发布，不会读取或复制外部音色、记录或导出文件夹。推送到 `main` 后，GitHub Actions 会构建、测试并提供工作流产物。要更新现有 `v1.0.0` 安装程序附件，提交信息需包含 `[replace-v1.0.0]`；其他普通提交不会改动 Release。

## 项目结构

```text
source/                  WPF 桌面程序和 Python bridge
source/backend/          AI 环境安装与模型准备脚本
installer/               Inno Setup 安装器定义与语言文件
build-installer.ps1      Windows 发布与安装包构建入口
```

