# native-wpf · 大肥鱼原生桌宠

> **双击 `启动大肥鱼.bat` 就能用。** 不需要 Node、不需要 npm install、不需要安装包 —— 首次运行会自动编译一次，之后秒开。
> 唯一要求：Windows + .NET 8 桌面运行时（大多数装了 .NET 的机器已经有了）。

把上游 Electron 版桌宠的**形象与动画**，接到一个 .NET 8 / WPF 写的原生外壳上。

- 不是重做美术：所有帧都来自仓库根目录的 `assets/whale/`，本目录只做切帧、播放和交互
- 不是替代上游：上游的陪伴面板、AI 聊天、专注计时、喂食小游戏都在 Electron 版里，这里没有
- 存在的理由：**离线、免 Node、免 npm install**，双击就能跑，而且能被 agent 通过本地 HTTP 驱动

## 跑起来

需要 Windows + [.NET 8 桌面运行时](https://dotnet.microsoft.com/download)。

```text
双击 启动大肥鱼.bat          # 没编译过会自动 dotnet build 一次
```

或者直接跑 `bin/Release/net8.0-windows/DeepSeekPet.exe`。

## 行为

| 场景 | 表现 |
| --- | --- |
| 左键单击 | 摸摸头：跳一下、挥手、说一句 |
| 左键双击 | 打开 DeepSeek Harness（该应用注册了 `dsh://open`，本程序会先用深链接、再退到 exe） |
| 按住拖动 | 拎起来（会歪头），松手落下回弹，位置会记住 |
| 右键 | 喂食 / 跳 / 跳舞 / 睡觉 / 大小 65–140% / 开机自启 / 回右下角 / 退出 |
| 闲着 | 每 **5–9 分钟**随机来个小动作（挥手、跳舞、偷看、吃东西、跳一下），不连着重样，跳舞 4.5 秒封顶 |
| 闲着 | 每 **2.5–6 分钟**自言自语一句（30 句，6 种人格，洗牌抽签不重复） |
| 闲置 **30 分钟** | 伸懒腰 → 坐下 → 撑不住 → 躺平蜷起来，定格闭眼，头顶冒 z z z |
| 碰她一下 | 醒了，并把 30 分钟重新计时 |

睡觉用的是上游 `dense-sleep.png` 的**全部 32 帧**（上游运行版只用了前 16 帧），所以最后能真的躺下去。

## 让 agent 驱动她

只监听 127.0.0.1，默认端口 17888（被占用会顺延，端口见菜单「关于」）。

```powershell
Invoke-RestMethod http://127.0.0.1:17888/status
Invoke-RestMethod http://127.0.0.1:17888/say -Method Post -ContentType 'application/json; charset=utf-8' `
  -Body ([Text.Encoding]::UTF8.GetBytes('{"text":"构建完成，我盯着日志呢","clip":"wave"}'))
Invoke-RestMethod "http://127.0.0.1:17888/emote?name=dance"
```

端点：`/status` `/say` `/emote` `/pet` `/feed` `/mutter` `/sleep` `/wake` `/move` `/scale` `/autostart` `/harness` `/quit`。
另有调试用的 `/idle?seconds=&mutter=&action=`。

## 自己重新切帧

帧已经提交在 `assets/frames/`，一般不用动。想改裁切或加状态：

```bash
python scripts/slice-frames.py     # 读 ../assets/whale/dense-*.png，写到 ./assets/frames/
```

脚本会把同一状态的所有帧**用同一个裁切框**，这样站姿/躺姿切换时比例不会跳。

## 许可与署名

**素材不是我的。** 角色形象、动画原画来自 [codex-deepseek-pet](https://github.com/YunYueSama/codex-deepseek-pet)，作者 **YunYueSama**。

- `assets/frames/` 是它的原图切帧，**沿用上游许可**（`LICENSES/LICENSE-upstream.txt`、`LICENSES/ASSET_LICENSE-upstream.md`），不是 MIT
- 本目录的 **C# / XAML 代码**用 MIT（`LICENSES/LICENSE-MIT.txt`）
- 修改说明：切帧并重新编排播放顺序；睡眠改为 32 帧四段式并定格；新增窗口外壳、本地接口、行为调度
- 上游声明该角色源自社区二创、底层参考图权利未完全核实 —— 本项目同样**非官方**，也不代表 DeepSeek 立场

细节见 `NOTICE.md`。
