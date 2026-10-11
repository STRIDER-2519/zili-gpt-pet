# 紫璃 Zili · GPT Pet

白发、紫瞳的小龙娘，陪你工作，也会趴着休息。

本项目提供原生 GPT Pet 动作图集、预览、制作提示词，以及可选的 Windows 离线计时面板。当前图集为 **v3 动作衔接修复版**。

![动作预览](media/hover-fix.gif)

## 快速开始

1. 先读 [中文使用说明](docs/使用说明.md)。
2. Windows 用户可双击根目录 `安装紫璃.cmd`，随后到原生客户端的宠物设置里点 **Refresh / 刷新**，选择 **紫璃 v3**。也可手动复制整个 [pets/zili-gpt-pet](pets/zili-gpt-pet) 文件夹，详见使用说明。
3. 如需计时和休息提醒，解压 [Windows 离线面板](downloads/zili-companion-v0.3-windows.zip)，双击其中的 `紫璃陪伴面板.exe`。

原生宠物与计时面板独立运行；只使用紫璃时，无需启动面板。

## 这版有什么

- 趴卧待机：呼吸、眨眼、尾尖轻摆。
- 鼠标悬停：直立小跳，已移除跳跃中的趴伏帧。
- 左右拖动：轻快跑步，尾巴向后舒展、尾尖轻翘。
- 等待输入：传统红色虎头帽、鹿角、拨浪鼓。
- 招手、思考工作、失败反应、检查成果与十六个看向方向。
- 可选面板：本次和今日计时、暂停、休息提醒；完全离线。

## 文档与目录

| 内容 | 位置 |
| --- | --- |
| 安装、操作、更新、常见问题 | [中文使用说明](docs/使用说明.md) |
| 动作触发与原生限制 | [动作说明](docs/动作说明.md) |
| 图集检查、导出与面板编译 | [开发说明](docs/开发说明.md) |
| 上传仓库与制作 Release | [发布到 GitHub](docs/发布到GitHub.md) |
| 可直接导入的原生宠物目录 | `pets/zili-gpt-pet/` |
| 动图、视频和检查图 | `media/` |
| 采用的生成源图、提示词 | `source-art/`、`prompts/` |
| 离线面板源码 | `companion/` |
| 可移植素材工具 | `tools/` |
| 可直接使用的面板包 | `downloads/` |
| 成品校验与制作记录 | `verification/` |

当前图集：透明 PNG，1536×2288，8 列×11 行，单格 192×208，73 个有效帧。具体触发由原生客户端决定；本项目不修改宿主事件或账户接口。

## 开发与校验

```powershell
python -m pip install -r requirements.txt
python tools/pet_assets.py validate
python tools/pet_assets.py extract --output build/frames
python tools/pet_assets.py preview --output build/previews
```

Windows 面板的编译与检查见 [开发说明](docs/开发说明.md)。

## 版本与许可

见 [CHANGELOG](CHANGELOG.md) 和 [素材及许可说明](NOTICE.md)。这是独立定制作品，未声称由 OpenAI 官方制作或背书。当前交付尚未指定开源许可，维护者可另行选择代码与美术的授权方式。
