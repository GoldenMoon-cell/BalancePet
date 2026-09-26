using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace BalancePet.Wpf.Services;

public static class AppLocalization
{
    public static bool IsEnglish(string? language) => string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase);

    public static string Text(string? language, string chinese, string english)
        => IsEnglish(language) ? english : chinese;

    public static void Apply(DependencyObject root, string? language)
    {
        ApplyNode(root, language, new HashSet<DependencyObject>());
    }

    private static void ApplyNode(DependencyObject node, string? language, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(node)) return;

        if (node is Window window) window.Title = Translate(window.Title, language);
        if (node is TextBlock textBlock) textBlock.Text = Translate(textBlock.Text, language);
        if (node is FrameworkElement element && element.ToolTip is string toolTip) element.ToolTip = Translate(toolTip, language);
        if (node is System.Windows.Controls.Button button && button.Content is string buttonText) button.Content = Translate(buttonText, language);
        if (node is System.Windows.Controls.CheckBox checkBox && checkBox.Content is string checkBoxText) checkBox.Content = Translate(checkBoxText, language);
        if (node is System.Windows.Controls.ComboBox comboBox)
        {
            foreach (var comboItem in comboBox.Items.OfType<ComboBoxItem>())
            {
                // Language names are keyed by their stable tags. Translating
                // the already-translated display text can otherwise make the
                // selected value drift from the actual saved language.
                var tag = comboItem.Tag?.ToString();
                if (string.Equals(tag, "zh-CN", StringComparison.OrdinalIgnoreCase))
                {
                    comboItem.Content = Text(language, "简体中文", "Simplified Chinese");
                    continue;
                }
                if (string.Equals(tag, "en-US", StringComparison.OrdinalIgnoreCase))
                {
                    comboItem.Content = "English";
                    continue;
                }
                if (comboItem.Content is string comboItemText) comboItem.Content = Translate(comboItemText, language);
            }
        }
        if (node is MenuItem menuItem && menuItem.Tag is string menuStyle)
        {
            // MenuItem.Tag is also used by account/profile entries. Only
            // translate tags that resolve to an actual pet style; unknown
            // values must not fall back to DeepSeek here.
            if (PetStyleCatalog.TryGetDefinition(menuStyle, out var definition))
                menuItem.Header = Text(language, definition.ChineseName, definition.EnglishName);
        }
        if (node is HeaderedContentControl headeredContent && headeredContent.Header is string contentHeader)
            headeredContent.Header = Translate(contentHeader, language);
        if (node is ComboBoxItem styleItem && styleItem.Tag is string comboStyle)
        {
            // ComboBoxItem.Tag is shared by preset, refresh, interaction and
            // language selectors. Do not treat an unrelated tag as a pet style:
            // NormalizeId intentionally falls back to DeepSeek for unknown
            // values, which would otherwise overwrite those controls.
            if (PetStyleCatalog.TryGetDefinition(comboStyle, out var definition))
                styleItem.Content = Text(language, definition.ChineseName, definition.EnglishName);
        }
        if (node is HeaderedItemsControl headered && headered.Header is string header) headered.Header = Translate(header, language);
        if (node is ContentControl content && content.Content is string contentText) content.Content = Translate(contentText, language);
        if (node is ComboBoxItem item && item.Content is string itemText) item.Content = Translate(itemText, language);

        // Logical trees also contain Grid RowDefinition/ColumnDefinition
        // objects, which are DependencyObjects but not visual nodes. Calling
        // VisualTreeHelper for those throws and used to crash menu actions
        // that opened a localized window. Only walk the visual tree for actual
        // Visual/Visual3D instances.
        if (node is Visual || node is Visual3D)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                ApplyNode(VisualTreeHelper.GetChild(node, i), language, visited);
        }

        // Some tab contents are present in the logical tree before they are
        // materialized in the visual tree. Walk both trees so every tab is
        // localized even before it has been opened.
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            ApplyNode(child, language, visited);
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
        ("小余额设置", "BalancePet Settings"), ("账户、桌宠与扩展集中管理", "Manage accounts, pet behavior, and extensions in one place"), ("监控账户", "Monitor accounts"), ("新增", "Add"), ("删除", "Delete"), ("启用", "Enabled"),
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
        ("账户与接口", "Accounts & API"), ("桌宠与交互", "Pet & interaction"), ("扩展", "Extensions"), ("外观", "Appearance"), ("高级与迁移", "Advanced & migration"),
        ("设置", "Settings"), ("主题插件", "Theme plugin"), ("让设置窗口融入 Windows，同时保持信息清晰、操作安静。", "Blend the settings window into Windows while keeping information clear and interactions calm."),
        ("当前主题", "Current theme"), ("颜色模式", "Color mode"), ("跟随系统", "Use system setting"), ("浅色", "Light"), ("深色", "Dark"),
        ("窗口材质", "Window material"), ("云母（推荐）", "Mica (recommended)"), ("云母 Alt（更浓）", "Mica Alt (stronger)"), ("亚克力", "Acrylic"), ("实色", "Solid"),
        ("紧凑布局", "Compact layout"), ("在相同窗口中显示更多设置项。", "Show more settings in the same window."),
        ("Windows 11 使用云母，Windows 10 使用 Acrylic；高对比度或关闭透明效果时自动回退为实色。", "Windows 11 uses Mica and Windows 10 uses Acrylic; high contrast or disabled transparency falls back to a solid background."),
        ("打开主题目录", "Open theme folder"), ("导入主题 ZIP", "Import theme ZIP"), ("已启用", "Enabled"),
        ("系统云母底层、Fluent 控件与 BalancePet 青绿色强调色。", "System Mica, Fluent controls, and BalancePet's teal accent."),
        ("外观与操作", "Appearance & interaction"), ("功能开关", "Feature switches"), ("扩展管理", "Extension management"), ("应用偏好", "Application preferences"), ("设置迁移", "Settings migration"),
        ("扩展库中的 ZIP 只负责保存版本；功能扩展安装即启用，需要后台运行的能力会自动启动。", "ZIP files in the extension library only store versions; feature extensions are enabled on installation, and capabilities that require background operation start automatically."),
        ("扩展库操作", "Extension library"), ("扫描库", "Scan library"), ("打开文件夹", "Open folder"), ("导入 ZIP", "Import ZIP"),
        ("扩展不会写入主程序安装目录。禁用或卸载当前正在使用的扩展后，桌宠会回退到内置形象。", "Extensions are not written to the main program directory. Disabling or uninstalling the active extension falls back to a built-in appearance."),
        ("扩展不会写入主程序安装目录。禁用或卸载当前正在使用的资源扩展后，桌宠会回退到内置形象；功能扩展始终在独立进程中运行。", "Extensions are not written to the main program directory. Disabling or uninstalling the active resource extension falls back to a built-in appearance; feature extensions always run in a separate process."),
        ("导出文件不包含访问令牌。迁移到其他用户或电脑后，需要重新填写令牌。", "Exported files do not contain access tokens. Tokens must be entered again after moving to another user or computer."),
        ("扩展运行副本保存在本机用户目录，主程序升级不会删除；资源扩展只加载图片，功能扩展不会加载进主程序进程。", "Installed extension copies are stored for the current Windows user and survive core upgrades; resource extensions load images only, and feature extensions are never loaded into the main process."),
        ("同一扩展的不同版本会合并为一行。扩展库位于主程序目录旁的 extension-library 文件夹，主程序升级不会删除；卸载只移除已安装副本，不删除扩展库中的 ZIP。", "Different versions of the same extension are grouped into one row. The extension library is the extension-library folder beside the main program and survives core upgrades; uninstall removes only the installed copy and keeps the library ZIP."),
        ("扫描扩展库", "Scan extension library"), ("打开扩展库", "Open extension library"), ("导入 ZIP 到扩展库…", "Import ZIP to library..."), ("安装选中", "Install selected"),
        ("安装 ZIP…", "Install ZIP..."), ("启用/禁用", "Enable/disable"), ("启用选中", "Enable selected"), ("禁用选中", "Disable selected"), ("启动选中", "Launch selected"), ("卸载选中", "Uninstall selected"),
        ("资源扩展：", "Resource extension: "), ("扩展已启用。", "Extension enabled."), ("扩展已禁用；已使用它的形象会回退到 DeepSeek。", "Extension disabled; appearances using it fall back to DeepSeek."),
        ("自动检查更新", "Automatic update checks"), ("自动检查扩展更新", "Automatic extension update checks"), ("每次启动时", "At every startup"), ("每天一次（推荐）", "Daily (recommended)"), ("每周一次", "Weekly"), ("仅手动检查", "Manual only"),
        ("桌宠大小", "Pet size"), ("音量", "Volume"), ("按压音效", "Press sound"), ("对话气泡", "Speech bubble"), ("互动动作", "Interaction effects"), ("设置面板动效", "Settings navigation animations"), ("关闭后，设置面板展开和折叠会立即切换，不播放过渡动画。", "When disabled, the settings navigation switches instantly without transition animations."),
        ("随机彩蛋", "Random easter eggs"), ("自动跟随 AI 任务", "Follow AI tasks"), ("读取 CC Switch 当前账户", "Read current CC Switch account"), ("只读读取 CC Switch 当前供应商；令牌只在本机内存中计算指纹，不读取网页登录凭据或上传明文令牌", "Read only the current CC Switch provider; credentials are fingerprinted in memory and web credentials or plaintext tokens are never read or uploaded"), ("系统通知", "System notifications"), ("随 Windows 启动（进入托盘）", "Start with Windows (tray)"),
        ("导入设置", "Import settings"), ("导出设置", "Export settings"), ("保存设置", "Save settings"), ("取消", "Cancel"), ("确定", "OK"), ("应用", "Apply"), ("保存并测试", "Save and test"), ("语言", "Language"),
        ("简体中文", "Simplified Chinese"), ("English", "English"), ("用量统计", "Usage"), ("最近用量", "Recent usage"), ("本机保存的余额变化记录", "Balance changes saved on this computer"),
        ("日期", "Date"), ("消耗", "Usage"), ("共", "Total"), ("发现新版本", "New version available"), ("暂不更新", "Not now"), ("下载并更新", "Download and update"),
        ("下载并启动安装器", "Download and launch installer"), ("更新说明", "Release notes"), ("校验", "Verification"), ("BalancePet 更新", "BalancePet update"),
        ("立即刷新", "Refresh now"), ("显示气泡", "Show bubble"), ("隐藏气泡", "Hide bubble"), ("切换为锁定互动", "Switch to locked interaction"), ("切换为自由拖动", "Switch to free drag"),
        ("切换形象", "Change appearance"), ("当前账户", "Current account"), ("设置面板", "Settings"), ("配置接口", "Configure API"), ("检查更新", "Check for updates"), ("隐藏桌宠", "Hide pet"), ("退出", "Exit"),
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
        ("给这个余额账户起一个容易识别的名称", "Give this balance account an easy-to-recognize name"),
        ("青绡翻到下一页", "Qingxiao turns to the next page"), ("蓝色书签轻轻晃动", "The blue bookmark sways softly"),
        ("余额变化会替你留下一页记录", "I will keep a page for every balance change"), ("护理手册合上了", "The care handbook is closed"),
        ("青绡在看着", "Qingxiao is watching"), ("蓝色书签被碰响了", "The blue bookmark rustled"),
        ("护理手册翻了一页", "The care handbook turned a page"), ("青色丝带绕了一圈", "The teal ribbon curls around"),
        ("今天也要照顾好预算", "Take good care of today's budget"), ("别忘了给自己留一点余量", "Remember to leave yourself some room"),
        ("青绡从书后探出头", "Qingxiao peeks out from behind the book"), ("书页连翻四次", "Four pages turn in a row"),
        ("这一页专门留给你", "This page is just for you"), ("青绡按住书签", "Qingxiao holds down the bookmark"),
        ("护理助手也需要翻页时间", "Even a care assistant needs time to turn the page"), ("蓝色发饰被碰到", "You touched the blue hair clip"),
        ("青绡轻轻偏了偏头", "Qingxiao tilts her head away"), ("不要拽书签", "Do not tug the bookmark"),
        ("长发会和丝带缠在一起", "My hair will tangle with the ribbon"), ("青色丝带晃了一圈", "The teal ribbon makes a little loop"),
        ("这一页先替你留着", "I will save this page for you"), ("青绡有点害羞", "Qingxiao is a little shy"),
        ("护理助手鼓起脸", "The care assistant puffs her cheeks"), ("要听话", "Be good"),
        ("再忙也要记得休息", "Remember to rest, however busy you are"), ("手册翻过一页", "The handbook turns a page"),
        ("青绡握紧了书签", "Qingxiao grips the bookmark"),
        ("青棱抱着书打盹", "Qingleng dozes off with her book"), ("安静待机", "Quietly standing by"),
        ("回来后再一起核对余额", "We can check the balance together when you return"), ("方晶发饰闪了一下", "The crystal hair clip flashes"),
        ("下一次刷新不会错过", "I will not miss the next refresh"), ("猫耳听见了风声", "The cat ears catch a whisper"),
        ("先让思路也休息一会儿", "Let your thoughts rest for a while"), ("青棱在看着", "Qingleng is watching"),
        ("猫耳听见你了", "The cat ears heard you"), ("方晶发饰闪了一格", "The crystal hair clip flashes once"),
        ("黑色小书翻开了", "The little black book opens"), ("模块已经对齐", "The modules are aligned"),
        ("可以继续", "Ready to continue"), ("先确认余额，再开始下一项任务", "Check the balance before starting the next task"),
        ("青棱的猫耳竖起来了", "Qingleng's cat ears perk up"), ("方晶连续闪烁", "The crystal flashes repeatedly"),
        ("这一组输入已经记住了", "This input sequence is now remembered"), ("青棱合上小书", "Qingleng closes the little book"),
        ("模块正在重新对齐", "The modules are realigning"), ("猫耳被碰到", "You touched the cat ears"),
        ("青棱的耳朵轻轻抖了一下", "Qingleng's ears twitch softly"), ("不要戳睡帽", "Do not poke the sleep mask"),
        ("方晶挂饰会歪掉的", "The crystal charm will go crooked"), ("蓝色吊坠响了一声", "The blue pendant chimes"),
        ("模块已经收到你的信号", "The module received your signal"), ("青棱有点害羞", "Qingleng is a little shy"),
        ("猫耳助手抿起嘴", "The cat-eared assistant purses her lips"), ("再戳就把这一项记进小书", "One more poke and it goes in the little book"),
        ("黑色小书亮了一下", "The little black book lights up"), ("青棱对齐了方晶", "Qingleng aligns the crystal"),
        ("玄珏收起画笔", "Xuanjue puts away her brush"), ("稍后再画", "We can draw later"),
        ("回来后继续陪你看余额", "I will watch the balance with you when you return"), ("黑玉画板微微发亮", "The black-jade drawing board glows softly"),
        ("灵感待机", "Inspiration on standby"), ("下一次状态变化我会告诉你", "I will tell you about the next status change"),
        ("墨色龙角安静下来", "The ink-dark horns settle down"), ("好画面和好预算都值得等待", "A good image and a sound budget are worth the wait"),
        ("玄珏在看着", "Xuanjue is watching"), ("墨色龙角被碰到了", "You touched the ink-dark horns"),
        ("画笔在画板上点了一下", "The brush taps the drawing board"), ("黑玉边框亮了一圈", "The black-jade frame lights up"),
        ("灵感也要留白", "Inspiration needs breathing room"), ("慢慢画", "Take your time drawing"),
        ("预算够用，画面才有余地", "A healthy budget leaves room for the image"), ("玄珏偷偷笑了一下", "Xuanjue smiles to herself"),
        ("画笔连点四下", "The brush taps four times"), ("这一笔就画给你", "This stroke is just for you"),
        ("玄珏护住画板", "Xuanjue shields her drawing board"), ("先留点白", "Leave a little blank space"),
        ("灵感也需要一点呼吸空间", "Inspiration needs a little room to breathe"), ("玄珏轻轻偏开了头", "Xuanjue turns her head away"),
        ("不要碰角尖", "Do not touch the horn tips"), ("金色耳饰会跟着晃", "The gold earrings will sway"),
        ("墨紫长发亮了一缕", "A lock of ink-purple hair gleams"), ("灵感来了", "Inspiration has arrived"),
        ("这一点光先留给你", "This glimmer is for you"), ("玄珏有点意外", "Xuanjue looks a little surprised"),
        ("小墨龙眯起眼睛", "The little ink dragon narrows her eyes"), ("看准了", "Look closely"),
        ("别把预算也涂出边界", "Do not paint the budget outside the lines"), ("画笔点亮了黑玉", "The brush lights up the black jade"),
        ("玄珏扶稳画板", "Xuanjue steadies the drawing board"), ("继续画吧", "Keep drawing"),
        ("绒眠缩进软绒里", "Rongmian nestles into the fluff"), ("无限发夹亮了一点", "The infinity hair clip glows"),
        ("我还在呢", "I am still here"), ("白色耳朵轻轻垂下", "The white ears droop softly"),
        ("晚安片刻", "A tiny goodnight"), ("你也记得让眼睛休息一下", "Remember to rest your eyes too"),
        ("绒眠在看着", "Rongmian is watching"), ("白色耳朵抖了一下", "The white ears twitch"),
        ("无限发夹亮起来了", "The infinity hair clip lights up"), ("绒球轻轻碰在一起", "The pom-poms bump together softly"),
        ("今天也软乎乎地稳住", "Keep things soft and steady today"), ("别让额度一下子跑光", "Do not let the quota run out all at once"),
        ("绒眠的耳朵竖起来了", "Rongmian's ears perk up"), ("绒球连晃四次", "The pom-poms bounce four times"),
        ("这份软乎乎送给你", "This bit of softness is for you"), ("绒眠缩进袖口", "Rongmian hides in her sleeves"),
        ("让我缓缓", "Give me a moment"), ("白色耳朵被碰到", "You touched the white ears"),
        ("绒眠轻轻甩了甩耳朵", "Rongmian flicks her ears softly"), ("不要拽卷发", "Do not tug the curls"),
        ("无限发夹会掉下来的", "The infinity hair clip might fall off"), ("绒毛蓬起来了", "The fluffy hair puffs up"),
        ("软乎乎", "So fluffy"), ("今天的好运也分你一点", "Here is a little of today's good luck"),
        ("绒眠有点害羞", "Rongmian is a little shy"), ("绒眠把脸藏进袖口", "Rongmian hides her face in her sleeves"),
        ("胸前绒球晃了晃", "The chest pom-poms sway"), ("绒眠伸了个小懒腰", "Rongmian takes a little stretch"),
        ("先歇会儿", "Take a short break"), ("回来后我还会继续守着余额", "I will keep watching the balance when you return"),
        ("我在值班", "I am on watch"), ("慢慢来", "Take your time"), ("不用一直盯着屏幕", "You do not have to keep watching the screen"),
        ("放心吧", "Rest easy"), ("余额变动会告诉你", "I will tell you when the balance changes"),
        ("轻一点", "Gently"), ("点击角色可以刷新余额", "Click the pet to refresh the balance"),
        ("找到啦", "Found you"), ("我会继续看着余额", "I will keep watching the balance"),
        ("在呢", "Right here"), ("完成后会显示本次消耗", "Usage for this task will appear when it finishes"),
        ("收到", "Got it"), ("被发现了", "You found me"), ("连续互动彩蛋", "Rapid interaction easter egg"),
        ("四连击", "Four-hit combo"), ("先缓一缓", "Let us pause a moment"), ("稍等一下", "One moment"),
        ("连续互动太快啦", "Those interactions are coming too quickly"), ("有点痒", "That tickles"),
        ("记下啦", "Noted"), ("脸颊被碰到", "You touched my cheek"), ("唔", "Oh"),
        ("轻一点嘛", "Be gentle"), ("被戳到了", "You poked me"), ("点击可以刷新余额", "Click to refresh the balance"),
        ("余额变化会及时告诉你", "I will tell you promptly when the balance changes"), ("继续吧", "Keep going")
    };
}
