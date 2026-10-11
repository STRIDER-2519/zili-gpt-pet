# 紫璃伴随面板源码 · 离线版

使用 Windows PowerShell 5.1 或 PowerShell 7，在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\companion\build.ps1
```

需要 64 位 Windows 10 / 11 和系统 .NET Framework 4.x。无需第三方依赖。输出位于仓库的 `build/panel/`：将 `紫璃陪伴面板.exe`、`Panel.xaml` 与 `assets/` 一起保留，双击程序即可运行。`assets/zili.png` 保留原有面板图像，与原生宠物最新图集分开；面板不会修改原生宠物。

`Panel.xaml` / `PanelApp.cs` 管理悬浮界面与休息通知；`TimerEngine.cs` 管理单调计时、日累计、暂停、休息提醒与本机持久化。程序完全离线运行，不登录账户、不启动其他服务，不读写账户状态。

默认小卡片仅显示本次陪伴时间和暂停按钮，宽 132；「⋯」展开今日累计与提醒设置，详情宽 300。拖动顶部摆在原生紫璃旁边；面板与原生宠物独立运行。

验证入口：

- `test.ps1`：编译并运行所有离线检查；从仓库根目录执行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\companion\test.ps1`。测试二进制与临时数据仅写入仓库 `build/`，不读取个人计时记录。
- `TimerEngineTests.cs`：跨日、暂停、重启、休眠/锁屏、时钟调整、提醒到期和延后、关闭再开、持久化失败与备份恢复。
- `tests/PanelIndependentQa.cs`：简洁模式操作、提醒开关、非法间隔、保存失败、损坏设置与旧数据兼容。
- 输出程序 `--smoke-test`：在独立 `smoke-data` 中验证操作，并生成详情、小卡片与休息提示预览，测试不会修改用户 `data`。

旧数据中已经移除功能的字段会被忽略；原有日期累计、休息间隔、提醒开关、窗口位置及置顶设置继续读取。下一次正常保存采用当前格式。交付 ZIP 不包含用户数据或测试数据。

普通运行会在 `build/panel/data/` 保存计时与设置。请使用可写目录；发布或分享时不要附带该个人数据文件夹。`build/` 已在仓库忽略规则中排除。

默认连续陪伴 45 分钟后提醒休息；详情可以设置 1–240 分钟、关闭提醒或置顶。提示中的「稍后 5 分钟」延后提醒；「我休息啦」暂停计时并重置连续周期。重新打开程序时本次计时从零开始，今日累计与设置保留。关闭面板不会关闭原生紫璃。
