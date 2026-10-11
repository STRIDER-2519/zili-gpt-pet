# 发布到 GitHub

## 上传成一个项目仓库

1. 解压交付包，打开包含 `README.md` 的项目目录。
2. 在 GitHub 新建一个空仓库，例如 `zili-gpt-pet`。公开或私有由维护者决定。
3. 把项目目录中的文件上传到仓库根目录；保留文件夹层级、隐藏文件和中文文件名。不要只把整个 ZIP 当作仓库唯一文件。
4. 确认首页能显示 README 及动作预览，使用说明链接能打开。

使用 Git 上传时，在项目根目录运行：

```text
git init
git add .
git commit -m "Add Zili GPT Pet v3"
git branch -M main
git remote add origin <你的仓库地址>
git push -u origin main
```

把 `<你的仓库地址>` 换成刚创建的仓库地址。若已经存在 Git 仓库，沿用原仓库设置，不必重新初始化或添加同名远程。

## 给普通使用者提供下载

可在仓库创建 Release，版本名例如 `v0.3.0`。附上项目 ZIP 与可选面板 ZIP；Release 说明中链接 `docs/使用说明.md`，并注明原生图集使用 v2 布局，面板适用于 Windows。

`downloads/zili-companion-v0.3-windows.zip` 已包含可运行面板，普通用户无需编译。`pets/zili-gpt-pet/spritesheet.png` 可单独作为原生宠物素材提供；本地导入时应同时带上同目录 `pet.json`。

## 后续更新

- 修改图片后更新 `verification/artifact-manifest.json` 中的哈希，运行素材检查。
- 不提交运行产生的 `data/`、测试数据、登录文件或密钥；已提供 `.gitignore`。
- 成品图集与预览分开存放；不要用截图替换透明 PNG。
- 维护者可在发布前按需要添加自己的代码与美术许可证；当前包没有擅自指定许可。
