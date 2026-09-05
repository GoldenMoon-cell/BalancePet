using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BalancePet.Wpf.Services;

public static class AppLocalization
{
    public static bool IsEnglish(string? language) => string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase);

    public static string Text(string? language, string chinese, string english)
        => IsEnglish(language) ? english : chinese;

    public static void Apply(DependencyObject root, string? language)
    {
        ApplyNode(root, language);
    }

    private static void ApplyNode(DependencyObject node, string? language)
    {
        if (node is Window window) window.Title = Translate(window.Title, language);
        if (node is TextBlock textBlock) textBlock.Text = Translate(textBlock.Text, language);
        if (node is FrameworkElement element && element.ToolTip is string toolTip) element.ToolTip = Translate(toolTip, language);
        if (node is System.Windows.Controls.Button button && button.Content is string buttonText) button.Content = Translate(buttonText, language);
        if (node is System.Windows.Controls.CheckBox checkBox && checkBox.Content is string checkBoxText) checkBox.Content = Translate(checkBoxText, language);
        if (node is System.Windows.Controls.ComboBox comboBox)
            foreach (var comboItem in comboBox.Items.OfType<ComboBoxItem>())
                if (comboItem.Content is string comboItemText) comboItem.Content = Translate(comboItemText, language);
        if (node is MenuItem menuItem && menuItem.Tag is string menuStyle)
        {
            var definition = PetStyleCatalog.All.FirstOrDefault(value => string.Equals(value.Id, PetStyleCatalog.NormalizeId(menuStyle), StringComparison.OrdinalIgnoreCase));
            if (definition is not null) menuItem.Header = Text(language, definition.ChineseName, definition.EnglishName);
        }
        if (node is ComboBoxItem styleItem && styleItem.Tag is string comboStyle)
        {
            var definition = PetStyleCatalog.All.FirstOrDefault(value => string.Equals(value.Id, PetStyleCatalog.NormalizeId(comboStyle), StringComparison.OrdinalIgnoreCase));
            if (definition is not null) styleItem.Content = Text(language, definition.ChineseName, definition.EnglishName);
        }
        if (node is HeaderedItemsControl headered && headered.Header is string header) headered.Header = Translate(header, language);
        if (node is ContentControl content && content.Content is string contentText) content.Content = Translate(contentText, language);
        if (node is ComboBoxItem item && item.Content is string itemText) item.Content = Translate(itemText, language);

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            ApplyNode(VisualTreeHelper.GetChild(node, i), language);
    }

    public static string Translate(string? value, string? language)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? "";
        var english = IsEnglish(language);
        foreach (var pair in Pairs)
        {
            if (english && string.Equals(value, pair.Chinese, StringComparison.Ordinal)) return pair.English;
            if (!english && string.Equals(value, pair.English, StringComparison.Ordinal)) return pair.Chinese;
        }
        return value;
    }

    private static readonly (string Chinese, string English)[] Pairs =
    {
        ("小余额设置", "BalancePet Settings"), ("监控账户", "Monitor accounts"), ("新增", "Add"), ("删除", "Delete"), ("启用", "Enabled"),
        ("接口预设", "Endpoint preset"), ("自动识别（推荐）", "Automatic detection (recommended)"), ("通用 /v1/usage", "Generic /v1/usage"),
        ("New API /api/usage/token", "New API /api/usage/token"), ("自定义接口", "Custom endpoint"), ("中转站地址", "Relay site address"),
        ("只填写站点根地址，例如 https://example.com；程序会自动补全余额接口。", "Enter only the site root, such as https://example.com; BalancePet completes the balance endpoint."),
        ("余额 API 地址", "Balance API endpoint"), ("填写中转站文档中的余额查询 URL，不是网站首页或聊天接口。", "Enter the balance URL from your relay provider's documentation, not the website or chat endpoint."),
        ("认证方式", "Authentication"), ("Bearer Token（只填令牌）", "Bearer token (token only)"), ("完整 Authorization（需含 Bearer）", "Full Authorization (include Bearer)"),
        ("中转站会话（websee-session）", "Relay session (websee-session)"), ("自定义 Header", "Custom header"), ("x-api-key", "x-api-key"),
        ("自定义 Header 名", "Custom header name"), ("访问令牌（留空保持现有令牌；无令牌时跳过测试）", "Access token (leave blank to keep the current token; empty tokens skip the test)"),
        ("余额 JSON 路径", "Balance JSON path"), ("货币", "Currency"), ("自动刷新间隔", "Automatic refresh"), ("关闭自动刷新", "Disable automatic refresh"),
        ("每 30 秒", "Every 30 seconds"), ("每 1 分钟", "Every 1 minute"), ("每 5 分钟", "Every 5 minutes"), ("每 15 分钟", "Every 15 minutes"),
        ("每 30 分钟", "Every 30 minutes"), ("每 1 小时", "Every hour"), ("自定义", "Custom"), ("低余额阈值", "Low-balance threshold"),
        ("宠物形象", "Pet appearance"), ("交互模式", "Interaction mode"), ("自由拖动", "Free drag"), ("锁定互动", "Locked interaction"),
        ("DeepSeek 小鲸鱼「澜汐」", "DeepSeek Whale \"Lanxi\""), ("ChatGPT 小白龙「霁珑」", "ChatGPT White Dragon \"Jilong\""),
        ("MiniMax 小海螺「绯音」", "MiniMax Shell \"Feiyin\""), ("Gemini 小星猫「星璃」", "Gemini Star Cat \"Xingli\""),
        ("Grok 小恶魔「烬斧」", "Grok Little Demon \"Jinfu\""),
        ("Claude 小书灵「丹笺」", "Claude Little Book Spirit \"Danqian\""), ("Kimi 小棱镜「虹谱」", "Kimi Little Prism \"Hongpu\""),
        ("Qwen 小折扇「绀华」", "Qwen Folding Fan \"Ganhua\""), ("Ernie 小病书灵「青绡」", "Ernie Little Book Spirit \"Qingxiao\""),
        ("GLM 小方灵「青棱」", "GLM Little Square Spirit \"Qingleng\""), ("GPT Image 2 小墨龙「玄珏」", "GPT Image 2 Ink Dragon \"Xuanjue\""),
        ("Llama 小羊驼「绒眠」", "Llama Alpaca \"Rongmian\""), ("MiMo 小兔码师「橙析」", "MiMo Bunny Coder \"Chengxi\""),
        ("Mistral 小猫骑士「麦霜」", "Mistral Cat Knight \"Maishuang\""), ("OpenCode 小码灵「墨枢」", "OpenCode Code Sprite \"Moshu\""),
        ("Perplexity 小探灯「青鉴」", "Perplexity Little Lantern \"Qingjian\""), ("RWKV 小乌鸦「夜翎」", "RWKV Little Raven \"Yeling\""),
        ("Seedence 小星晶「澄芽」", "Seedence Little Star Crystal \"Chengya\""), ("素材尚未完成", "Assets are not ready"),
        ("自动检查更新", "Automatic update checks"), ("每次启动时", "At every startup"), ("每天一次（推荐）", "Daily (recommended)"), ("每周一次", "Weekly"), ("仅手动检查", "Manual only"),
        ("桌宠大小", "Pet size"), ("音量", "Volume"), ("按压音效", "Press sound"), ("对话气泡", "Speech bubble"), ("互动动作", "Interaction effects"),
        ("随机彩蛋", "Random easter eggs"), ("自动跟随 AI 任务", "Follow AI tasks"), ("识别 AI 登录账户", "Detect AI login accounts"), ("客户端仅上报账户类型、API 地址和令牌指纹；BalancePet 不读取网页登录凭据或明文令牌", "Clients report only account type, API address, and token fingerprint; BalancePet never reads web credentials or plaintext tokens"), ("系统通知", "System notifications"), ("随 Windows 启动（进入托盘）", "Start with Windows (tray)"),
        ("导入设置", "Import settings"), ("导出设置", "Export settings"), ("保存设置", "Save settings"), ("取消", "Cancel"), ("保存并测试", "Save and test"), ("语言", "Language"),
        ("简体中文", "Simplified Chinese"), ("English", "English"), ("用量统计", "Usage"), ("最近用量", "Recent usage"), ("本机保存的余额变化记录", "Balance changes saved on this computer"),
        ("日期", "Date"), ("消耗", "Usage"), ("共", "Total"), ("发现新版本", "New version available"), ("暂不更新", "Not now"), ("下载并更新", "Download and update"),
        ("下载并启动安装器", "Download and launch installer"), ("更新说明", "Release notes"), ("校验", "Verification"), ("BalancePet 更新", "BalancePet update"),
        ("立即刷新", "Refresh now"), ("显示气泡", "Show bubble"), ("隐藏气泡", "Hide bubble"), ("切换为锁定互动", "Switch to locked interaction"), ("切换为自由拖动", "Switch to free drag"),
        ("切换形象", "Change appearance"), ("当前账户", "Current account"), ("配置接口", "Configure API"), ("检查更新", "Check for updates"), ("隐藏桌宠", "Hide pet"), ("退出", "Exit"),
        ("账户余额", "Account balance"), ("点击角色刷新", "Click the pet to refresh"), ("没有监控账户", "No monitor accounts"), ("没有启用账户", "No enabled accounts"),
        ("账户未启用", "Account disabled"), ("接口未配置", "API not configured"), ("正在查询", "Querying"), ("查询成功", "Query succeeded"), ("刷新失败", "Refresh failed"), ("余额偏低", "Low balance"),
        ("正在刷新", "Refreshing"), ("请稍候", "Please wait"), ("还没查询", "Not queried yet"), ("交互模式", "Interaction mode"), ("形象已切换", "Appearance changed"), ("切换失败", "Switch failed"),
        ("设置已保存", "Settings saved"), ("已切换账户", "Account switched"), ("未配置账户", "No accounts configured"), ("AI 工作中", "AI working"), ("任务结束", "Task finished"),
        ("点击立即刷新获取余额", "Click Refresh now to retrieve the balance"), ("未保存", "Not saved"), ("余额与状态已切换", "Balance and status switched"),
        ("可以拽嘴角和提呆毛", "Drag the mouth corners or hair tuft"), ("按住角色即可移动", "Hold the pet to move it"), ("正在处理", "Processing"),
        ("上一轮查询尚未完成", "The previous query has not finished"), ("当前不需要更新", "No update is needed"), ("正在更新", "Updating"),
        ("下载并校验中", "Downloading and verifying"), ("安装器已启动", "Installer started"), ("更新完成", "Update complete"),
        ("BalancePet 已重新启动", "BalancePet has restarted"), ("已是最新版本", "You are up to date"),
        ("仅控制自动刷新；手动刷新固定至少间隔 5 秒。", "Controls automatic refresh only; manual refresh has a fixed 5-second cooldown."),
        ("请输入自动刷新秒数，最少 30 秒", "Enter automatic refresh seconds (minimum 30)."),
        ("给这个余额账户起一个容易识别的名称", "Give this balance account an easy-to-recognize name")
    };
}
