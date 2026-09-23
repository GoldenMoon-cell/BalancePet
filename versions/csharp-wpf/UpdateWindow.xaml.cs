using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BalancePet.Wpf.Services;
using BalancePet.Wpf.Models;

namespace BalancePet.Wpf;

public partial class UpdateWindow : Window
{
    private static readonly Regex MarkdownLink = new(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.CultureInvariant);

    public UpdateWindow(UpdateRelease release, UpdateInstallPlan plan, PetSettings settings)
    {
        InitializeComponent();
        WindowThemeService.ApplyConfiguredTheme(this, settings);
        var language = settings.Language;
        AppLocalization.Apply(this, language);
        VersionText.Text = release.TagName;
        ReleaseTitleText.Text = string.Equals(release.Name, release.TagName, StringComparison.OrdinalIgnoreCase)
            ? AppLocalization.Text(language, "BalancePet 更新", "BalancePet update")
            : release.Name;
        AddReleaseNotes(release.Body, plan.Asset.Digest, language);
        if (plan.Method == UpdateInstallMethod.Installer)
        {
            InstallHintText.Text = AppLocalization.Text(language,
                "当前安装目录需要管理员权限。下载后将启动安装器；确认 UAC 后由安装器完成替换。",
                "This installation directory requires administrator permission. The installer will launch after download and finish the replacement after UAC confirmation.");
            ConfirmButton.Content = AppLocalization.Text(language, "下载并启动安装器", "Download and launch installer");
        }
    }

    private void AddReleaseNotes(string body, string? digest, string language)
    {
        var sections = new List<(string Title, List<string> Lines)>();
        var title = AppLocalization.Text(language, "更新说明", "Release notes");
        var lines = new List<string>();

        foreach (var rawLine in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                AddSection(sections, title, lines);
                title = CleanMarkdown(line[3..]);
                lines = new List<string>();
                continue;
            }

            lines.Add(CleanMarkdown(line.TrimStart('-', '*', ' ')));
        }
        AddSection(sections, title, lines);

        var verificationTitle = AppLocalization.Text(language, "校验", "Verification");
        if (!string.IsNullOrWhiteSpace(digest) && !sections.Any(section => string.Equals(section.Title, verificationTitle, StringComparison.Ordinal)))
            sections.Add((verificationTitle, new List<string> { digest }));
        if (sections.Count == 0) sections.Add((title, new List<string> { AppLocalization.Text(language, "本版本包含稳定性和体验优化。", "This release includes stability and experience improvements.") }));

        foreach (var section in sections)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            var heading = new TextBlock
            {
                Text = section.Title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            heading.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            block.Children.Add(heading);

            foreach (var line in section.Lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var lineBlock = new TextBlock
                {
                    Text = line,
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    FontFamily = line.StartsWith("SHA-256:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                        ? new System.Windows.Media.FontFamily("Consolas")
                        : System.Windows.SystemFonts.MessageFontFamily,
                    FontSize = line.StartsWith("SHA-256:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? 12 : 14,
                    Margin = new Thickness(0, 0, 0, 7)
                };
                lineBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                block.Children.Add(lineBlock);
            }
            NotesPanel.Children.Add(block);
        }
    }

    private static void AddSection(List<(string Title, List<string> Lines)> sections, string title, List<string> lines)
    {
        if (lines.Count > 0) sections.Add((title, lines));
    }

    private static string CleanMarkdown(string line)
    {
        var text = MarkdownLink.Replace(line, "$1");
        return text.Replace("**", "").Replace("`", "").Trim();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
