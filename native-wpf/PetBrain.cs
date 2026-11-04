namespace DeepSeekPet;

internal static class PetBrain
{
    private static readonly Random Rnd = new();

    private static readonly string[] Poke =
    {
        "嗯？你叫我？",
        "我不是大肥鱼……",
        "别摸了，头发会乱。",
        "……再摸一下也不是不行。",
        "你很闲吗？那陪我待一会儿。",
        "我不是胖，是毛量足。",
        "嘴上说不要，人已经靠过来了。",
        "摸头可以，摸鱼不行，那是我的工作。",
    };

    private static readonly string[] Idle =
    {
        "今天也想被夸一句。",
        "碗里还有 token 吗？",
        "你的代码好像在呼吸。",
        "我猜你又在改一个很小的东西。",
        "安静陪着也可以，我不吵。",
        "刚刚在发呆，不是在想你。",
        "要不要休息三分钟？我数着。",
        "这条尾巴，是我唯一的行李。",
    };

    private static readonly string[] Feed =
    {
        "唔……好险，差点咬到你。",
        "token 是脑力口粮，我这是刚需。",
        "还有吗？我还能再吃一点点。",
        "吃饱了才有力气陪你写代码。",
    };

    private static readonly string[] Sleep =
    {
        "主人，我要去休息啦，有事叫我～",
        "主人，我撑不住了，先眯一会儿，有事叫我哦。",
        "困困……主人，有事就叫我，我马上醒。",
    };

    private static readonly string[] Wake =
    {
        "唔……我醒着呢，一直醒着。",
        "刚刚在做梦，梦里有一大盘 token。",
        "叫我做什么？",
    };

    public static string RandomPoke() => Pick(Poke);
    public static string IdleLine() => Pick(Idle);
    public static string FeedLine() => Pick(Feed);
    public static string SleepLine() => Pick(Sleep);
    public static string WakeLine() => Pick(Wake);

    public static string Greeting() => DateTime.Now.Hour switch
    {
        >= 0 and < 5 => "这个点……你是打算熬到天亮吗？",
        >= 5 and < 9 => "早。今天的第一次眨眼，给你。",
        >= 9 and < 12 => "上午好。先把最麻烦的那块啃下来吧。",
        >= 12 and < 14 => "吃饭了吗？我也想吃。",
        >= 14 and < 18 => "下午最容易困，要不要我陪你走两圈？",
        >= 18 and < 23 => "晚上好，今天辛苦了。",
        _ => "夜深了，早点收工，剩下的明天再说。"
    };

    // 🌙 纯放空 / 🍪 馋嘴 / 🐾 软萌 / ✨ 小哲学 / 💤 困困 / 🎈 求关注 —— 无聊时的自言自语
    private static readonly string[] Mutters =
    {
        // 🌙 纯放空型
        "唔……今天的天花板好像比昨天白一点？",
        "我在想一件很重要的事……是什么来着？",
        "如果我现在躺下，会不会变成一张饼……",
        "脑袋空空的，像被舔干净的酸奶盖。",
        "发呆中，请勿投喂……除非是小饼干。",

        // 🍪 馋嘴型
        "闻到啦！是谁在偷吃好吃的……",
        "如果无聊能换零食，我大概已经换了一仓库。",
        "肚子说它有点寂寞，需要一颗糖来陪。",
        "刚才好像梦见一块蛋糕在向我招手……",
        "我不是馋，我只是在思考下一顿。",

        // 🐾 软萌自问自答型
        "你说，云朵是不是天空的棉花糖？",
        "如果我打个滚，会不会滚进你的心里呀？",
        "今天也有乖乖待着哦，有没有人夸夸我……",
        "我刚刚眨了一下眼，世界就刷新了一次！",
        "无聊的时候，数自己的爪爪，一只、两只……咦？",

        // ✨ 小哲学型
        "为什么时间走得这么慢，零食却消失得那么快……",
        "我在思考一个很严肃的问题：先有困，还是先有床？",
        "发呆是不是脑袋在给自己充电呀？",
        "世界好安静，安静得能听见自己尾巴摇的声音。",
        "如果什么都不做，会不会也是一种很厉害的本领？",

        // 💤 困困型
        "眼睛想下班了……但我不想……",
        "我没有睡着，我只是在闭眼思考人生。",
        "呼……刚才那一下不算打盹，算短暂离线。",
        "困意像小猫一样，悄悄趴到我头上了……",
        "再发一会儿呆，我就去……去……干什么来着？",

        // 🎈 想引起注意型
        "喂——有人在吗——你的小可爱正在无聊中——",
        "我戳了戳空气，空气没有理我。",
        "如果没人陪我玩，我就……继续发呆给你看！",
        "哼，再不理我，我就要自己跟自己玩啦。",
        "偷偷看你一眼……再看一眼……好了，我满足了。",
    };

    private static readonly List<string> MutterBag = new();

    /// <summary>洗牌抽签：一轮 30 句轮完才重洗，保证不会连着说同一句。</summary>
    public static string Mutter()
    {
        if (MutterBag.Count == 0)
        {
            MutterBag.AddRange(Mutters);
            for (int i = MutterBag.Count - 1; i > 0; i--)
            {
                int j = Rnd.Next(i + 1);
                (MutterBag[i], MutterBag[j]) = (MutterBag[j], MutterBag[i]);
            }
        }

        int last = MutterBag.Count - 1;
        string line = MutterBag[last];
        MutterBag.RemoveAt(last);
        return line;
    }

    public static string AgentLine(string text) => text;

    private static string Pick(string[] source) => source[Rnd.Next(source.Length)];
}
