using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DeepSeekPet;

public partial class MainWindow : Window
{
    public const double BaseW = 320;
    public const double BaseH = 420;

    private static readonly Dictionary<string, (double Fps, bool Loop)> Clips = new(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"] = (8, true),
        ["walk"] = (14, true),
        ["wave"] = (14, false),
        ["jump"] = (15, false),
        ["eat"] = (11, false),
        ["dance"] = (13, true),
        ["sneak"] = (11, false),
        ["sleep"] = (10, false),
    };

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _rnd = new();

    private SpriteLibrary _sprites = null!;
    private BitmapSource[] _frames = Array.Empty<BitmapSource>();
    private int _frameIndex = -1;

    private double _t;
    private string _clip = "idle";
    private double _clipTime;
    private bool _asleep;

    // 分段播放（伸懒腰只播前几帧、睡着后循环最后几帧）
    private int _from, _to;
    private double _fps = 8;
    private bool _loop = true;
    private Action? _onDone;
    private double _clipLimit;          // 循环动作的最长停留时间（秒），0=不限

    // 行为：闲置多久自动睡、随机小动作的窗口期
    private const double SleepAfterIdleSeconds = 1800;  // 30 分钟没互动才休息
    private const double MutterMinSeconds = 150;        // 自言自语的最短间隔（秒）
    private const double MutterMaxSeconds = 360;        // 最长间隔
    private double _idleSeconds;
    private bool _sleeping;
    private string _lastIdleAction = string.Empty;

    private double _offsetY, _tilt, _squash = 1;
    private double _hopT = -1, _landT = -1;

    private double _bubbleAge = 9999, _bubbleLife = 1;
    private string _bubbleFull = string.Empty;
    private int _bubbleChars = -1;

    private bool _pressing, _dragged;
    private Point _pressCursor, _pressWindow;

    private double _idleActionIn = 330;
    private double _mutterIn = 120;
    private double _scale = 0.85;
    private string? _harnessPath;
    private double _wantLeft = double.NaN;
    private double _wantTop = double.NaN;

    private AgentBridge? _bridge;
    private int _bridgePort;
    private MenuItem? _autostartItem;

    public MainWindow()
    {
        InitializeComponent();
        _sprites = new SpriteLibrary(Path.Combine(AppContext.BaseDirectory, "assets", "frames"));
        Loaded += OnLoaded;
        Closed += OnClosed;
        _timer.Tick += OnTick;
        BuildMenu();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplySettings();
        ApplyPlacement();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ApplyPlacement();
        Play("idle");
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            ApplyPlacement();
            App.Log($"[pet] placed at {Left:0},{Top:0} size {Width:0}x{Height:0} scale {_scale}");
            Say(PetBrain.Greeting(), 4.5);
        }));
        _timer.Start();
        StartBridge();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        SaveSettings();
        _bridge?.Dispose();
    }

    // ==================== sprites ====================

    /// <summary>播放一个动作。maxSeconds 只对循环动作有效（比如跳一会儿舞就停）。</summary>
    public void Play(string clip, double maxSeconds = 0)
    {
        if (!Clips.TryGetValue(clip, out var info))
        {
            clip = "idle";
            info = Clips["idle"];
        }

        BitmapSource[] frames = _sprites.Get(clip);
        if (frames.Length == 0)
        {
            if (clip != "idle") Play("idle");
            return;
        }

        Action? done = null;
        if (!info.Loop)
        {
            done = clip switch
            {
                // 睡下之后定格在最后一帧（完全闭眼），动态交给呼吸和 z z z
                "sleep" => () => PlayTail("sleep", 1, 1.0),
                _ => () => Play("idle")
            };
        }

        PlayFrames(clip, frames, 0, frames.Length, info.Fps, info.Loop, done, info.Loop ? maxSeconds : 0);
    }

    private void PlayFrames(string clip, BitmapSource[] frames, int from, int to, double fps, bool loop, Action? onDone, double limit = 0)
    {
        _clip = clip;
        _frames = frames;
        _from = Math.Clamp(from, 0, frames.Length);
        _to = Math.Clamp(to, _from, frames.Length);
        _fps = fps > 0 ? fps : 8;
        _loop = loop;
        _onDone = onDone;
        _clipLimit = limit;
        _clipTime = 0;
        _frameIndex = -1;
        _asleep = clip == "sleep";
        UpdateSleepVisuals();
    }

    private void PlayTail(string clip, int tail, double fps)
    {
        BitmapSource[] frames = _sprites.Get(clip);
        if (frames.Length == 0) { Play("idle"); return; }
        int start = Math.Max(0, frames.Length - Math.Max(1, tail));
        PlayFrames(clip, frames, start, frames.Length, fps, true, null);
    }

    private void SetFrame(int index)
    {
        if (index == _frameIndex || index < 0 || index >= _frames.Length) return;
        _frameIndex = index;
        PetImage.Source = _frames[index];
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _t, 0.001, 0.06);
        _t = now;

        _clipTime += dt;

        int count = _to - _from;
        if (count > 0)
        {
            double framePos = _clipTime * _fps;

            if (_loop)
            {
                SetFrame(_from + (int)Math.Floor(framePos) % count);
                if (_clipLimit > 0 && _clipTime >= _clipLimit) Play("idle");
            }
            else if (framePos >= count)
            {
                Action? done = _onDone;
                _onDone = null;
                if (done is null) Play("idle");
                else done();
            }
            else
            {
                SetFrame(_from + (int)framePos);
            }
        }

        UpdateMotion(dt);
        UpdateBubble(dt);
        UpdateSleepBehaviour(dt);
        UpdateIdleActions(dt);
        UpdateMutter(dt);
    }

    private void UpdateMotion(double dt)
    {
        double breath = Math.Sin(_t * 1.6);
        double bob = breath * 1.4;
        double tilt = Math.Sin(_t * 0.8) * 0.8;

        if (_hopT >= 0)
        {
            _hopT += dt;
            double p = _hopT / 0.5;
            if (p >= 1)
            {
                _hopT = -1;
                _landT = 0;
            }
            else
            {
                bob = -38 * Math.Sin(Math.PI * p);
                _squash = 1 + 0.06 * Math.Sin(Math.PI * p);
            }
        }

        if (_landT >= 0)
        {
            _landT += dt;
            double p = _landT / 0.55;
            if (p >= 1)
            {
                _landT = -1;
                _squash = 1;
            }
            else
            {
                _squash = 1 - 0.16 * Math.Exp(-5.5 * p) * Math.Cos(9 * p);
                bob = 0;
            }
        }

        if (_dragged)
        {
            bob = -8;
            double x = Mouse.GetPosition(this).X;
            tilt = Math.Clamp((x - Width * 0.5) / (24 * _scale), -9, 9);
            _squash = 1.02;
        }

        if (_asleep)
        {
            // 睡着后没有逐帧变化，靠缓慢的起伏 + 轻微缩放表现呼吸
            double slow = Math.Sin(_t * 1.15);
            bob = slow * 1.7;
            _squash = 1 + 0.014 * slow;
        }

        _offsetY = bob;
        _tilt = tilt;

        MoveT.Y = _offsetY;
        TiltR.Angle = _tilt;
        SquashS.ScaleY = _squash;
        SquashS.ScaleX = 1 + (1 - _squash) * 0.55;
        Shadow.Width = 132 * Math.Clamp(1 - _offsetY / 260.0, 0.72, 1.06);
        Shadow.Opacity = 0.30 * Math.Clamp(1 + _offsetY / 260.0, 0.45, 1.0);
    }

    // ==================== 睡眠 & 闲逛 ====================

    /// <summary>任何交互都会重置闲置计时；如果她正睡着，就叫醒她。</summary>
    private void Touch()
    {
        _idleSeconds = 0;
        _mutterIn = MutterMinSeconds;
        if (_sleeping) WakeUp();
    }

    /// <summary>闲着发呆时，隔一会儿自言自语一句（独立于小动作的计时）。</summary>
    private void UpdateMutter(double dt)
    {
        if (_sleeping || _asleep || _dragged) return;
        if (_clip != "idle") return;
        if (_idleSeconds < 45) return;       // 刚互动完别马上叨叨

        _mutterIn -= dt;
        if (_mutterIn > 0) return;

        _mutterIn = MutterMinSeconds + _rnd.NextDouble() * (MutterMaxSeconds - MutterMinSeconds);
        Say(PetBrain.Mutter(), 4.4);
    }

    private void UpdateSleepBehaviour(double dt)
    {
        _idleSeconds += dt;

        if (!_sleeping && !_dragged && !_asleep && _idleSeconds >= SleepAfterIdleSeconds)
            GoToSleep(auto: true);

        if (!_asleep)
        {
            ZzzGroup.Opacity = 0;
            return;
        }

        double phase = (_t * 0.3) % 1.0;
        double phase2 = (_t * 0.3 + 0.42) % 1.0;
        ZzzGroup.Opacity = 0.95;
        Zzz1.Opacity = Math.Max(0, 1 - phase) * 0.9;
        Zzz2.Opacity = Math.Max(0, 1 - phase2) * 0.7;
        Canvas.SetTop(Zzz1, 62 - 40 * phase);
        Canvas.SetLeft(Zzz1, 22 + 16 * phase);
        Canvas.SetTop(Zzz2, 40 - 46 * phase2);
        Canvas.SetLeft(Zzz2, 46 + 14 * phase2);
    }

    private void UpdateSleepVisuals()
    {
        if (!_asleep) ZzzGroup.Opacity = 0;
    }

    /// <summary>先去伸个懒腰、打个哈欠，再蜷起来睡下。</summary>
    public void GoToSleep(bool auto)
    {
        if (_sleeping) return;
        _sleeping = true;
        _idleSeconds = 0;

        Say(auto ? "主人，我要去休息了，有事叫我～" : PetBrain.SleepLine(), 4.2);

        BitmapSource[] frames = _sprites.Get("sleep");
        if (frames.Length == 0)
        {
            Play("sleep");
            return;
        }

        // 32 帧的完整入睡过程：伸懒腰 → 坐下闭眼 → 撑不住往下塌 → 躺下蜷成一团
        (int From, int To, double Fps)[] stages =
        {
            (0, Math.Min(6, frames.Length), 3.2),
            (Math.Min(6, frames.Length), Math.Min(16, frames.Length), 6.5),
            (Math.Min(16, frames.Length), Math.Min(24, frames.Length), 6.0),
            (Math.Min(24, frames.Length), frames.Length, 5.5),
        };
        PlaySleepStage(frames, stages, 0);
    }

    private void PlaySleepStage(BitmapSource[] frames, (int From, int To, double Fps)[] stages, int index)
    {
        if (index >= stages.Length)
        {
            PlayTail("sleep", 1, 1.0);   // 定格在躺平闭眼的最后一帧
            return;
        }

        (int from, int to, double fps) = stages[index];
        if (to <= from)
        {
            PlaySleepStage(frames, stages, index + 1);
            return;
        }

        PlayFrames("sleep", frames, from, to, fps, false, () => PlaySleepStage(frames, stages, index + 1));
    }

    public void WakeUp()
    {
        if (!_sleeping && !_asleep) return;
        _sleeping = false;
        _idleSeconds = 0;
        Play("idle");
        Say(PetBrain.WakeLine(), 2.6);
    }

    private void UpdateIdleActions(double dt)
    {
        if (_sleeping || _asleep || _dragged) return;
        if (_clip != "idle") return;

        _idleActionIn -= dt;
        if (_idleActionIn > 0) return;

        // 窗口期再拉长：5~9 分钟才自己动一次，别抢你的注意力
        _idleActionIn = 300 + _rnd.NextDouble() * 240;

        string[] pool = { "wave", "dance", "sneak", "eat", "jump" };
        string pick = pool[_rnd.Next(pool.Length)];
        if (pick == _lastIdleAction && pool.Length > 1)
        {
            do { pick = pool[_rnd.Next(pool.Length)]; } while (pick == _lastIdleAction);
        }
        _lastIdleAction = pick;

        Play(pick, pick == "dance" ? 4.5 : 0);
        if (_rnd.NextDouble() < 0.25) Say(PetBrain.IdleLine(), 3.2);
    }

    // ==================== speech ====================

    public void Say(string text, double seconds = 4.2)
    {
        _bubbleFull = text ?? string.Empty;
        _bubbleAge = 0;
        _bubbleLife = seconds + _bubbleFull.Length * 0.055;
        _bubbleChars = -1;
        BubbleSizer.Text = _bubbleFull;
        BubbleText.Text = string.Empty;
        BubbleLayer.Opacity = 0;
    }

    private void UpdateBubble(double dt)
    {
        if (_bubbleAge >= _bubbleLife) return;

        _bubbleAge += dt;
        int chars = (int)(_bubbleAge * 28);
        if (chars > _bubbleFull.Length) chars = _bubbleFull.Length;
        if (chars != _bubbleChars)
        {
            _bubbleChars = chars;
            BubbleText.Text = chars >= _bubbleFull.Length ? _bubbleFull : _bubbleFull[..chars];
        }

        double fadeIn = Math.Clamp(_bubbleAge / 0.15, 0, 1);
        double fadeOut = Math.Clamp((_bubbleLife - _bubbleAge) / 0.45, 0, 1);
        BubbleLayer.Opacity = Math.Min(fadeIn, fadeOut);

        if (_bubbleAge >= _bubbleLife)
        {
            BubbleLayer.Opacity = 0;
            BubbleText.Text = string.Empty;
            BubbleSizer.Text = string.Empty;
        }
    }

    // ==================== actions ====================

    public void Poke()
    {
        Touch();
        _hopT = 0;
        Play("wave");
        Say(PetBrain.RandomPoke(), 3.0);
    }

    public void Feed()
    {
        Touch();
        Play("eat");
        Say(PetBrain.FeedLine(), 3.0);
    }

    /// <summary>双击她 → 把 DeepSeek Harness 桌面端叫到前面（没开就启动）。</summary>
    public void OpenHarness()
    {
        Touch();

        if (HarnessLauncher.TryFocusExisting())
        {
            Play("wave");
            Say("工作台本来就在，给你拉到前面了。", 3.0);
            return;
        }

        if (!HarnessLauncher.RequestShow(_harnessPath))
        {
            Play("sneak");
            Say("找不到 DeepSeek Harness……右键菜单里可以手动指定一次。", 5.0);
            return;
        }

        Play("wave");
        Say("好，我叫它把窗口打开——它启动比较慢，等我一下。", 3.2);
        StartHarnessWatch();
    }

    private DispatcherTimer? _harnessWatch;
    private int _harnessWatchTicks;

    /// <summary>
    /// 深链接是异步的，而且这台机器上窗口要几十秒才出来，所以持续盯着，
    /// 一旦出现就把它拉到前台。
    /// </summary>
    private void StartHarnessWatch()
    {
        _harnessWatch?.Stop();
        _harnessWatchTicks = 0;

        _harnessWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _harnessWatch.Tick += (_, _) =>
        {
            _harnessWatchTicks++;

            if (HarnessLauncher.TryFocusExisting())
            {
                _harnessWatch?.Stop();
                _harnessWatch = null;
                App.Log($"[harness] window appeared after {_harnessWatchTicks * 0.7:0.0}s");
                Say("来了来了。", 2.4);
                return;
            }

            if (_harnessWatchTicks == 14) // ~10s
                Say("还在起…它冷启动有点慢。", 3.0);

            if (_harnessWatchTicks >= 75) // ~52s
            {
                _harnessWatch?.Stop();
                _harnessWatch = null;
                App.Log("[harness] gave up after 52s");
                Say("等了快一分钟还没窗口……它可能是真卡住了。托盘图标右键退出，再点我一次。", 8.0);
            }
        };
        _harnessWatch.Start();
    }

    private void ChooseHarness()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选一下 DeepSeek Harness 的主程序",
            Filter = "程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = @"D:\DeepSeek Harness"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _harnessPath = dialog.FileName;
            SaveSettings();
            Say("记住了，下次双击我就叫它。", 3.0);
        }
    }

    public void ToggleSleep()
    {
        if (_sleeping) WakeUp();
        else GoToSleep(auto: false);
    }

    // ==================== interaction ====================

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Touch();
        _pressing = true;
        _dragged = false;
        _pressCursor = PointToScreen(e.GetPosition(this));
        _pressWindow = new Point(Left, Top);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_pressing) return;

        Point now = PointToScreen(e.GetPosition(this));
        double dx = now.X - _pressCursor.X;
        double dy = now.Y - _pressCursor.Y;
        if (!_dragged && Math.Abs(dx) + Math.Abs(dy) > 5)
        {
            _dragged = true;
            _landT = -1;
        }
        if (_dragged)
        {
            Left = _pressWindow.X + dx;
            Top = _pressWindow.Y + dy;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        bool dragged = _dragged;
        _pressing = false;
        _dragged = false;

        if (dragged)
        {
            _landT = 0;
            SaveSettings();
        }
        else
        {
            HandleClick();
        }
    }

    private DispatcherTimer? _clickTimer;

    /// <summary>单击=摸摸，双击=喂食；用定时器自己区分，避免鼠标捕获打断系统双击判定。</summary>
    private void HandleClick()
    {
        if (_clickTimer is not null)
        {
            _clickTimer.Stop();
            _clickTimer = null;
            OpenHarness();
            return;
        }

        _clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer?.Stop();
            _clickTimer = null;
            Poke();
        };
        _clickTimer.Start();
    }

    // ==================== menu ====================

    private void BuildMenu()
    {
        var menu = new ContextMenu
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13
        };

        menu.Items.Add(new MenuItem { Header = "大肥鱼", IsEnabled = false, FontWeight = FontWeights.SemiBold });
        menu.Items.Add(new Separator());

        Add(menu, "摸摸头", () => Poke());
        Add(menu, "打开 DeepSeek Harness（双击同款）", () => OpenHarness());
        Add(menu, "喂一口 token", () => Feed());
        Add(menu, "跳一下", () => { _hopT = 0; Play("jump"); });
        Add(menu, "跳支舞", () => { Play("dance"); Say("看好了，只跳一次。", 2.6); });
        Add(menu, "睡觉 / 叫醒", () => ToggleSleep());
        Add(menu, "说句话", () => Say(PetBrain.IdleLine(), 3.4));

        menu.Items.Add(new Separator());

        var size = new MenuItem { Header = "大小" };
        foreach ((string label, double value) in new[] { ("小 (65%)", 0.65), ("标准 (85%)", 0.85), ("大 (110%)", 1.1), ("特大 (140%)", 1.4) })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = Math.Abs(_scale - value) < 0.01 };
            double v = value;
            item.Click += (_, _) => ApplyScale(v);
            size.Items.Add(item);
        }
        menu.Items.Add(size);

        Add(menu, "选择工作台程序…", () => ChooseHarness());

        _autostartItem = new MenuItem { Header = "开机自启动", IsCheckable = true, IsChecked = Autostart.IsEnabled() };
        _autostartItem.Click += (_, _) =>
        {
            bool want = _autostartItem.IsChecked;
            bool ok = Autostart.SetEnabled(want);
            _autostartItem.IsChecked = Autostart.IsEnabled();
            if (!ok)
            {
                MessageBox.Show("没能写入注册表启动项，可能被安全策略拦住了。\n可以用同目录的「设置开机自启.bat」再试一次。",
                    "开机自启动", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                Say(want ? "好，下次开机我自己爬出来。" : "行，那我不吵你了。", 3.0);
            }
        };
        menu.Items.Add(_autostartItem);
        menu.Opened += (_, _) => _autostartItem.IsChecked = Autostart.IsEnabled();

        Add(menu, "回到右下角", () => ResetPosition());
        Add(menu, "关于…", () => MessageBox.Show(
            "大肥鱼 · 桌面宠物\n\n外形素材来自开源项目 codex-deepseek-pet（作者 YunYueSama），" +
            "本程序在原素材基础上做了重排与二次开发。\n\n" +
            "左键：摸摸 / 拖动\n双击：打开 DeepSeek Harness\n右键：菜单（喂食、跳舞、大小、开机自启…）\n\n" +
            $"本地接口：http://127.0.0.1:{_bridgePort}/status",
            "关于", MessageBoxButton.OK, MessageBoxImage.Information));

        menu.Items.Add(new Separator());
        Add(menu, "退 出", () => Close());

        ContextMenu = menu;
    }

    private static void Add(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    // ==================== window / settings ====================

    private void ApplyScale(double scale)
    {
        if (!double.IsNaN(Left) && !double.IsNaN(Top)) { _wantLeft = Left; _wantTop = Top; }
        _scale = Math.Clamp(scale, 0.5, 2.0);
        Root.LayoutTransform = new ScaleTransform(_scale, _scale);
        Width = BaseW * _scale;
        Height = BaseH * _scale;
        ClampPlacement();
        ApplyPlacement();
        SaveSettings();
    }

    private void ResetPosition()
    {
        _wantLeft = double.NaN;
        _wantTop = double.NaN;
        ClampPlacement();
        ApplyPlacement();
        SaveSettings();
    }

    private void ClampPlacement()
    {
        Rect wa = SystemParameters.WorkArea;
        double w = Width, h = Height;
        double minX = wa.Left - w * 0.45;
        double maxX = wa.Right - w * 0.55;
        double minY = wa.Top - h * 0.12;
        double maxY = wa.Bottom - h * 0.5;
        if (double.IsNaN(_wantLeft) || _wantLeft < minX || _wantLeft > maxX)
            _wantLeft = wa.Right - w - 40;
        if (double.IsNaN(_wantTop) || _wantTop < minY || _wantTop > maxY)
            _wantTop = wa.Bottom - h + 12;
    }

    private void ApplyPlacement()
    {
        if (double.IsNaN(_wantLeft) || double.IsNaN(_wantTop)) return;
        Left = _wantLeft;
        Top = _wantTop;
    }

    private string SettingsPath => Path.Combine(AppContext.BaseDirectory, "pet-settings.json");

    private sealed class PetSettings
    {
        public double? Left { get; set; }
        public double? Top { get; set; }
        public double Scale { get; set; } = 0.85;
        public string? HarnessPath { get; set; }
    }

    private void ApplySettings()
    {
        var s = new PetSettings();
        try
        {
            if (File.Exists(SettingsPath))
                s = JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(SettingsPath)) ?? new PetSettings();
        }
        catch { }

        _scale = Math.Clamp(s.Scale, 0.5, 2.0);
        Root.LayoutTransform = new ScaleTransform(_scale, _scale);
        Width = BaseW * _scale;
        Height = BaseH * _scale;
        _harnessPath = s.HarnessPath;
        _wantLeft = s.Left ?? double.NaN;
        _wantTop = s.Top ?? double.NaN;
        ClampPlacement();
    }

    private void SaveSettings()
    {
        try
        {
            if (!double.IsNaN(Left) && !double.IsNaN(Top)) { _wantLeft = Left; _wantTop = Top; }
            var s = new PetSettings { Left = _wantLeft, Top = _wantTop, Scale = _scale, HarnessPath = _harnessPath };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s));
        }
        catch { }
    }

    // ==================== agent bridge ====================

    private void StartBridge()
    {
        try
        {
            _bridge = new AgentBridge(17888, HandleBridgeRequest, msg => App.Log("[bridge] " + msg));
            _bridgePort = _bridge.Port;
            App.Log($"[pet] bridge on 127.0.0.1:{_bridgePort}");
        }
        catch (Exception ex)
        {
            App.Log("[pet] bridge failed: " + ex);
        }
    }

    private string HandleBridgeRequest(string method, string rawPath, string body)
    {
        string path = rawPath;
        string query = string.Empty;
        int q = rawPath.IndexOf('?');
        if (q >= 0) { path = rawPath[..q]; query = rawPath[(q + 1)..]; }

        return Dispatcher.Invoke(() =>
        {
            try
            {
                using var doc = string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body);
                JsonElement root = doc?.RootElement ?? default;

                string Get(string name)
                {
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out JsonElement v))
                        return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? string.Empty) : v.ToString();
                    foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] kv = pair.Split('=', 2);
                        if (kv.Length == 2 && kv[0] == name) return Uri.UnescapeDataString(kv[1]);
                    }
                    return string.Empty;
                }

                switch (path.TrimEnd('/'))
                {
                    case "":
                    case "/status":
                        return StatusJson();

                    case "/say":
                    {
                        Touch();
                        string text = Get("text");
                        if (string.IsNullOrWhiteSpace(text)) text = PetBrain.IdleLine();
                        Say(text);
                        string clip = Get("clip");
                        if (!string.IsNullOrEmpty(clip)) Play(clip);
                        return JsonSerializer.Serialize(new { ok = true, text, clip = _clip });
                    }

                    case "/emote":
                    case "/play":
                    {
                        Touch();
                        string clip = Get("clip");
                        if (string.IsNullOrEmpty(clip)) clip = Get("name");
                        if (string.IsNullOrEmpty(clip)) clip = "wave";
                        if (string.Equals(clip, "sleep", StringComparison.OrdinalIgnoreCase)) GoToSleep(auto: false);
                        else Play(clip);
                        string text = Get("text");
                        if (!string.IsNullOrEmpty(text)) Say(text);
                        return JsonSerializer.Serialize(new { ok = true, clip = _clip });
                    }

                    case "/debug":
                    {
                        double dx = double.TryParse(Get("x"), out double px) ? px : 136;
                        double dy = double.TryParse(Get("y"), out double py) ? py : 285;
                        Point rootPoint = Root.TranslatePoint(new Point(dx, dy), Root);
                        var hit = VisualTreeHelper.HitTest(Root, rootPoint);
                        string hitName = hit?.VisualHit is null ? "null" : hit.VisualHit.GetType().Name;
                        string bounds = "n/a";
                        try
                        {
                            Point p0 = PetImage.TransformToAncestor(this).Transform(new Point(0, 0));
                            Point p1 = PetImage.TransformToAncestor(this).Transform(new Point(PetImage.ActualWidth, PetImage.ActualHeight));
                            bounds = $"{p0.X:0},{p0.Y:0} -> {p1.X:0},{p1.Y:0}";
                        }
                        catch { }
                        return JsonSerializer.Serialize(new
                        {
                            ok = true,
                            hit = hitName,
                            rootPoint = $"{rootPoint.X:0},{rootPoint.Y:0}",
                            imageBounds = bounds,
                            rootSize = $"{Root.ActualWidth:0}x{Root.ActualHeight:0}",
                            imageSize = $"{PetImage.ActualWidth:0}x{PetImage.ActualHeight:0}",
                            windowOffset = $"{Left:0},{Top:0}",
                            winSize = $"{Width:0}x{Height:0}"
                        });
                    }

                    case "/pet":
                        Poke();
                        return JsonSerializer.Serialize(new { ok = true, clip = _clip });

                    case "/windows":
                    {
                        var rows = new List<object>();
                        Native.EnumWindows((hWnd, _) =>
                        {
                            Native.GetWindowThreadProcessId(hWnd, out uint pid);
                            try
                            {
                                string name = Process.GetProcessById((int)pid).ProcessName;
                                if (!name.Contains("Harness", StringComparison.OrdinalIgnoreCase)) return true;
                                Native.GetWindowRect(hWnd, out Native.RECT rect);
                                var title = new System.Text.StringBuilder(200);
                                Native.GetWindowText(hWnd, title, 200);
                                var cls = new System.Text.StringBuilder(200);
                                Native.GetClassName(hWnd, cls, 200);
                                rows.Add(new
                                {
                                    hwnd = hWnd.ToInt64(),
                                    pid,
                                    proc = name,
                                    size = $"{rect.Right - rect.Left}x{rect.Bottom - rect.Top}",
                                    at = $"{rect.Left},{rect.Top}",
                                    visible = Native.IsWindowVisible(hWnd),
                                    iconic = Native.IsIconic(hWnd),
                                    owner = Native.GetWindow(hWnd, 4) != IntPtr.Zero,
                                    title = title.ToString(),
                                    cls = cls.ToString()
                                });
                            }
                            catch { }
                            return true;
                        }, IntPtr.Zero);
                        return JsonSerializer.Serialize(new { ok = true, count = rows.Count, rows });
                    }

                    case "/harness":
                    {
                        string configured = Get("path");
                        if (!string.IsNullOrWhiteSpace(configured))
                        {
                            _harnessPath = configured.Trim('"');
                            SaveSettings();
                        }

                        if (Get("dry") is "1" or "true")
                        {
                            string? resolved = HarnessLauncher.Resolve(_harnessPath, out string source);
                            return JsonSerializer.Serialize(new { ok = resolved is not null, path = resolved, source });
                        }

                        (bool ok, string message) = HarnessLauncher.LaunchOrFocus(_harnessPath);
                        Say(message, 3.0);
                        return JsonSerializer.Serialize(new { ok, message, configured = _harnessPath });
                    }

                    case "/feed":
                        Feed();
                        return JsonSerializer.Serialize(new { ok = true, clip = _clip });

                    case "/mutter":
                    {
                        string line = PetBrain.Mutter();
                        Say(line, 4.4);
                        return JsonSerializer.Serialize(new { ok = true, text = line });
                    }

                    case "/idle":
                    {
                        // 调试用：把"多久没互动"直接拨到某个秒数，方便验证自动休眠
                        if (double.TryParse(Get("seconds"), out double seconds))
                            _idleSeconds = Math.Max(0, seconds);
                        if (double.TryParse(Get("mutter"), out double mutter))
                            _mutterIn = Math.Max(0, mutter);
                        if (double.TryParse(Get("action"), out double action))
                            _idleActionIn = Math.Max(0, action);
                        return JsonSerializer.Serialize(new
                        {
                            ok = true,
                            idleSeconds = (int)_idleSeconds,
                            threshold = (int)SleepAfterIdleSeconds,
                            mutterIn = (int)_mutterIn,
                            actionIn = (int)_idleActionIn
                        });
                    }

                    case "/sleep":
                        GoToSleep(auto: false);
                        return JsonSerializer.Serialize(new { ok = true, clip = _clip, sleeping = _sleeping });

                    case "/wake":
                        WakeUp();
                        return JsonSerializer.Serialize(new { ok = true, clip = _clip, sleeping = _sleeping });

                    case "/move":
                    {
                        if (double.TryParse(Get("x"), out double x)) Left = x;
                        if (double.TryParse(Get("y"), out double y)) Top = y;
                        SaveSettings();
                        return JsonSerializer.Serialize(new { ok = true, x = (int)Left, y = (int)Top });
                    }

                    case "/autostart":
                    {
                        string value = Get("value");
                        if (string.IsNullOrEmpty(value)) value = "toggle";

                        bool want = value.ToLowerInvariant() switch
                        {
                            "on" or "1" or "true" or "enable" => true,
                            "off" or "0" or "false" or "disable" => false,
                            _ => !Autostart.IsEnabled()
                        };

                        bool ok = Autostart.SetEnabled(want);
                        if (_autostartItem is not null) _autostartItem.IsChecked = Autostart.IsEnabled();
                        return JsonSerializer.Serialize(new { ok, autostart = Autostart.IsEnabled(), exe = Autostart.ExecutablePath });
                    }

                    case "/scale":
                    {
                        if (double.TryParse(Get("value"), out double v)) ApplyScale(v);
                        return JsonSerializer.Serialize(new { ok = true, scale = _scale });
                    }

                    case "/quit":
                        Dispatcher.BeginInvoke(new Action(Close));
                        return JsonSerializer.Serialize(new { ok = true, message = "bye" });

                    default:
                        return JsonSerializer.Serialize(new
                        {
                            ok = false,
                            error = "unknown endpoint",
                            endpoints = new[]
                            {
                                "GET /status", "POST /say {text,clip}", "POST /emote {name}", "POST /pet", "POST /harness", "POST /feed", "POST /mutter",
                                "POST /sleep", "POST /wake", "POST /move {x,y}", "POST /scale {value}", "POST /autostart {value}", "POST /quit"
                            }
                        });
                }
            }
            catch (Exception ex)
            {
                App.Log("[bridge] error " + ex);
                return JsonSerializer.Serialize(new { ok = false, error = ex.Message });
            }
        });
    }

    public string StatusJson()
    {
        return JsonSerializer.Serialize(new
        {
            ok = true,
            state = _clip,
            asleep = _asleep,
            sleeping = _sleeping,
            idleSeconds = (int)_idleSeconds,
            actionIn = (int)_idleActionIn,
            mutterIn = (int)_mutterIn,
            x = (int)Left,
            y = (int)Top,
            width = (int)Width,
            height = (int)Height,
            scale = Math.Round(_scale, 2),
            speaking = _bubbleAge < _bubbleLife,
            autostart = Autostart.IsEnabled(),
            text = _bubbleFull,
            pid = Environment.ProcessId,
            port = _bridgePort
        });
    }
}
