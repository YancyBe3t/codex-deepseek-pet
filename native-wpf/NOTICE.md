# NOTICE · 署名与许可拆分

## 素材

- 角色形象、动画原画、设计资料：**YunYueSama** · https://github.com/YunYueSama/codex-deepseek-pet
- 适用许可：大肥鱼项目署名许可 1.0（非 MIT），全文见 `LICENSES/LICENSE-upstream.txt` 与 `LICENSES/ASSET_LICENSE-upstream.md`
- 该许可要求：使用/修改/分享/商用须注明作者与仓库地址；分发实质性内容时保留完整许可文本

## 本目录做了什么修改

1. 用 `scripts/slice-frames.py` 把 `assets/whale/dense-*.png` 按 4 列切成独立帧，同一状态共用一个裁切框
2. 睡眠序列改用全部 32 帧，重排为「伸懒腰 → 坐闭眼 → 往下塌 → 躺下蜷起 → 定格」四段
3. 重写播放器（.NET 8 / WPF）：分段播放、完成回调、循环上限、呼吸与 z z z 的程序化动效
4. 新增：透明置顶窗口、拖拽回弹、气泡打字机、右键菜单、开机自启、本地 HTTP 接口、DeepSeek Harness 深链接唤起、闲时行为调度（小动作 / 自言自语 / 30 分钟入睡）

## 代码

本目录的 C# / XAML 源码采用 MIT（`LICENSES/LICENSE-MIT.txt`）。
**注意：MIT 只覆盖代码，不覆盖 `assets/` 下的图像。**

## 第三方边界

上游已在 `ASSET_LICENSE.md` 中说明：角色参考自社区图片「你这吃白饭的蓝色大肥鱼」，原作者与授权尚待核实，因此上游也无法就底层参考作品额外授权。本项目沿用同样的边界：

- 非 DeepSeek 官方产品，也未获官方背书
- 不对角色设计主张额外权利
- 如你是相关权利人并希望调整署名/使用方式，请开 issue
