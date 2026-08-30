using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BalancePet.Wpf.Services;

namespace BalancePet.Wpf;

public partial class UpdateWindow : Window
{
    private static readonly Regex MarkdownLink = new(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.CultureInvariant);

    public UpdateWindow(UpdateRelease release)
    {
        InitializeComponent();
        VersionText.Text = release.TagName;
        ReleaseTitleText.Text = string.Equals(release.Name, release.TagName, StringComparison.OrdinalIgnoreCase) ? "BalancePet 更新" : release.Name;
        AddReleaseNotes(release.Body, release.Digest);
    }

    private void AddReleaseNotes(string body, string? digest)
    {
        var sections = new List<(string Title, List<string> Lines)>();
        var title = "更新说明";
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

        if (!string.IsNullOrWhiteSpace(digest) && !sections.Any(section => string.Equals(section.Title, "校验", StringComparison.Ordinal)))
            sections.Add(("校验", new List<string> { digest }));
        if (sections.Count == 0) sections.Add(("更新说明", new List<string> { "本版本包含稳定性和体验优化。" }));

        foreach (var section in sections)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            block.Children.Add(new TextBlock
            {
                Text = section.Title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 61, 120)),
                Margin = new Thickness(0, 0, 0, 8)
            });

            foreach (var line in section.Lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                block.Children.Add(new TextBlock
                {
                    Text = line,
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(57, 70, 104)),
                    FontFamily = line.StartsWith("SHA-256:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                        ? new System.Windows.Media.FontFamily("Consolas")
                        : System.Windows.SystemFonts.MessageFontFamily,
                    FontSize = line.StartsWith("SHA-256:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? 12 : 14,
                    Margin = new Thickness(0, 0, 0, 7)
                });
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
