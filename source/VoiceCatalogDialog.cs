using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace AisoundStudio;

public sealed class VoiceCatalogDialog : Window
{
    const string CatalogHome = "https://voice-models.com/";
    const string SearchEndpoint = "https://voice-models.com/fetch_data.php";
    readonly CookieContainer cookies = new();
    readonly HttpClient http;
    readonly string voicesDirectory;
    readonly TextBox query = new() { MinWidth = 280 };
    readonly ListBox results = new() { MinHeight = 190, MaxHeight = 280, DisplayMemberPath = "Display" };
    readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 58 };
    readonly TextBlock status = new() { Text = "输入名称后搜索 voice-models.com。", TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar downloadProgress = new() { Height = 5, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
    readonly Button searchButton;
    readonly Button downloadButton;
    readonly Button cancelDownloadButton;
    readonly Button openCatalogButton;
    readonly Button openSourceButton;
    CancellationTokenSource? downloadCancellation;
    public VoiceRecord? DownloadedVoice { get; private set; }

    public VoiceCatalogDialog(string voicesDirectory)
    {
        http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All, CookieContainer = cookies }) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AisoundStudio/1.0");
        this.voicesDirectory = Path.GetFullPath(voicesDirectory);
        Title = "在线搜索 RVC 音色";
        Width = 770;
        Height = 680;
        MinWidth = 680;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Color("#192028");
        Foreground = Color("#F0F4F8");
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });

        searchButton = new Button { Content = "搜索", Padding = new Thickness(16, 9, 16, 9), Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        searchButton.Click += async (_, _) => await SearchAsync();
        downloadButton = new Button { Content = "下载并添加到音色库", Padding = new Thickness(16, 9, 16, 9), IsEnabled = false };
        downloadButton.Click += async (_, _) => await DownloadSelectedAsync();
        cancelDownloadButton = new Button { Content = "取消下载", Padding = new Thickness(12, 8, 12, 8), Visibility = Visibility.Collapsed };
        cancelDownloadButton.Click += (_, _) => downloadCancellation?.Cancel();
        openCatalogButton = new Button { Content = "打开目录网页", Padding = new Thickness(12, 8, 12, 8) };
        openCatalogButton.Click += (_, _) => OpenWebPage(results.SelectedItem is VoiceCatalogEntry item ? item.CatalogUrl : new Uri(CatalogHome));
        openSourceButton = new Button { Content = "核对文件来源", Padding = new Thickness(12, 8, 12, 8), IsEnabled = false };
        openSourceButton.Click += (_, _) => { if (results.SelectedItem is VoiceCatalogEntry item) OpenWebPage(item.SourceUrl); };

        results.SelectionChanged += (_, _) => UpdateSelectedDetails();
        var panel = new StackPanel { Margin = new Thickness(23) };
        panel.Children.Add(new TextBlock { Text = "搜索社区 RVC 音色", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(Text("搜索来自 voice-models.com；模型文件由社区成员托管，结果中的许可信息和质量并不保证。", "#A5B3C0"));
        var queryRow = new Grid { Margin = new Thickness(0, 15, 0, 12) };
        queryRow.ColumnDefinitions.Add(new ColumnDefinition()); queryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        queryRow.Children.Add(query); Grid.SetColumn(searchButton, 1); queryRow.Children.Add(searchButton); panel.Children.Add(queryRow);
        panel.Children.Add(results);
        panel.Children.Add(details);
        panel.Children.Add(Text("下载来源", "#A5B3C0"));
        panel.Children.Add(new TextBlock { Text = CatalogHome, Foreground = Color("#ADF0D1"), Margin = new Thickness(0, 2, 0, 10) });
        panel.Children.Add(Text("本地保存位置：软件目录下的 voices 文件夹。", "#A5B3C0"));
        panel.Children.Add(Text(".pth 模型由社区上传，声屿不会验证其授权或安全性。此类文件可能在加载时执行代码；只下载你信任且有权使用的模型。", "#E9C88C"));
        panel.Children.Add(downloadProgress);
        panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 13, 0, 0) };
        buttons.Children.Add(openCatalogButton); buttons.Children.Add(openSourceButton);
        buttons.Children.Add(cancelDownloadButton); buttons.Children.Add(downloadButton);
        var close = new Button { Content = "关闭", Padding = new Thickness(16, 9, 16, 9), IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(close); panel.Children.Add(buttons);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
        Closed += (_, _) => { downloadCancellation?.Cancel(); http.Dispose(); };
        Loaded += (_, _) => query.Focus();
    }

    async Task SearchAsync()
    {
        var term = query.Text.Trim();
        if (term.Length < 2) { status.Text = "请输入至少两个字符。"; return; }
        searchButton.IsEnabled = false; downloadButton.IsEnabled = false; results.ItemsSource = null;
        status.Text = "正在查询 voice-models.com…";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, SearchEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["page"] = "1", ["search"] = term })
            };
            request.Headers.Referrer = new Uri(CatalogHome);
            request.Headers.UserAgent.ParseAdd("AisoundStudio/1.0");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Accept.ParseAdd("application/json, text/javascript, */*; q=0.01");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var table = json.RootElement.TryGetProperty("table", out var value) ? value.GetString() ?? "" : "";
            var entries = ParseEntries(table);
            results.ItemsSource = entries;
            status.Text = entries.Count == 0 ? "没有找到结果，或目录页面结构已变化。可以打开 voice-models.com 手动查看。" : $"找到 {entries.Count} 个结果。选择一项查看模型页面和文件来源。";
            if (entries.Count > 0) results.SelectedIndex = 0;
        }
        catch (Exception e) { status.Text = "搜索失败：" + e.Message + "\n你仍可在 voice-models.com 查看目录。"; }
        finally { searchButton.IsEnabled = true; }
    }

    static List<VoiceCatalogEntry> ParseEntries(string html)
    {
        var output = new List<VoiceCatalogEntry>();
        foreach (Match row in Regex.Matches(html, @"<tr\b[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var links = new List<(string Text, Uri Uri)>();
            foreach (Match anchor in Regex.Matches(row.Groups[1].Value, @"<a\b(?<attrs>[^>]*)>(?<text>.*?)</a\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var href = Regex.Match(anchor.Groups["attrs"].Value, @"\bhref\s*=\s*(?:""(?<d>[^""]*)""|'(?<s>[^']*)'|(?<b>[^\s>]+))", RegexOptions.IgnoreCase);
                if (!href.Success) continue;
                var target = WebUtility.HtmlDecode(href.Groups["d"].Success ? href.Groups["d"].Value : href.Groups["s"].Success ? href.Groups["s"].Value : href.Groups["b"].Value);
                if (!Uri.TryCreate(new Uri(CatalogHome), target, out var uri) || uri.Scheme != Uri.UriSchemeHttps) continue;
                var label = WebUtility.HtmlDecode(Regex.Replace(anchor.Groups["text"].Value, "<[^>]+>", " ")).Trim();
                links.Add((label, uri));
            }
            var modelLink = links.FirstOrDefault(x => x.Uri.Host.Equals("voice-models.com", StringComparison.OrdinalIgnoreCase) && x.Uri.AbsolutePath.StartsWith("/model/", StringComparison.OrdinalIgnoreCase));
            if (modelLink.Uri == null) continue;
            var source = links.LastOrDefault(x => !x.Uri.Host.Equals("voice-models.com", StringComparison.OrdinalIgnoreCase) && !x.Uri.Host.EndsWith("easyaivoice.com", StringComparison.OrdinalIgnoreCase));
            if (source.Uri == null) continue;
            var rowText = WebUtility.HtmlDecode(Regex.Replace(row.Groups[1].Value, "<[^>]+>", " "));
            rowText = Regex.Replace(rowText, @"\s+", " ").Trim();
            var name = modelLink.Text;
            if (string.IsNullOrWhiteSpace(name)) name = rowText.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "RVC 音色";
            var creatorLink = links.FirstOrDefault(x => x.Uri.AbsolutePath.StartsWith("/creator/", StringComparison.OrdinalIgnoreCase));
            var size = Regex.Match(rowText, @"\b[\d,.]+\s*(?:MB|GB)\b", RegexOptions.IgnoreCase).Value;
            var item = new VoiceCatalogEntry(name, creatorLink.Text, size, modelLink.Uri, source.Uri);
            if (!output.Any(x => x.CatalogUrl == item.CatalogUrl)) output.Add(item);
        }
        return output;
    }

    void UpdateSelectedDetails()
    {
        if (results.SelectedItem is not VoiceCatalogEntry entry) { details.Text = ""; downloadButton.IsEnabled = false; return; }
        details.Text = $"{entry.Name}\n上传者：{(string.IsNullOrWhiteSpace(entry.Creator) ? "目录未提供" : entry.Creator)}　大小：{(string.IsNullOrWhiteSpace(entry.Size) ? "目录未提供" : entry.Size)}\n目录页：{entry.CatalogUrl}\n文件来源：{entry.SourceUrl}";
        downloadButton.IsEnabled = IsDownloadSupported(entry.SourceUrl);
        openSourceButton.IsEnabled = true;
        if (!downloadButton.IsEnabled) status.Text = "此来源暂不支持应用内直下；可先查看目录页中的模型文件链接。";
    }

    static bool IsDownloadSupported(Uri uri) =>
        uri.Host.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Equals("drive.usercontent.google.com", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Equals("hf.co", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(uri.AbsolutePath).Equals(".pth", StringComparison.OrdinalIgnoreCase);

    async Task DownloadSelectedAsync()
    {
        if (results.SelectedItem is not VoiceCatalogEntry entry) return;
        var answer = MessageBox.Show(this,
            $"将从 {entry.SourceUrl.Host} 下载社区模型“{entry.Name}”并加入本地音色库。\n\n声屿无法核实该文件的授权或安全性；.pth 模型可能在加载时执行代码。请先核对来源，只在你信任且有权使用时继续。\n\n确认下载？",
            "确认下载社区音色", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        downloadCancellation = new CancellationTokenSource(TimeSpan.FromHours(2));
        var token = downloadCancellation.Token;
        searchButton.IsEnabled = false; downloadButton.IsEnabled = false; cancelDownloadButton.Visibility = Visibility.Visible;
        downloadProgress.Visibility = Visibility.Visible; downloadProgress.IsIndeterminate = true; status.Text = "正在读取模型文件信息…";
        var staging = Path.Combine(voicesDirectory, ".download-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(voicesDirectory);
            var files = await ResolveFilesAsync(entry, token);
            Directory.CreateDirectory(staging);
            var modelTemp = Path.Combine(staging, "model.pth.part");
            await DownloadFileAsync(files.ModelUrl, modelTemp, token);
            string? indexTemp = null;
            if (files.IndexUrl != null)
            {
                indexTemp = Path.Combine(staging, "model.index.part");
                await DownloadFileAsync(files.IndexUrl, indexTemp, token);
            }
            var name = UniqueVoiceName(entry.Name);
            var target = Path.Combine(voicesDirectory, name);
            Directory.CreateDirectory(voicesDirectory);
            Directory.Move(staging, target);
            var modelPath = Path.Combine(target, name + ".pth");
            File.Move(Path.Combine(target, "model.pth.part"), modelPath);
            var indexPath = "";
            if (indexTemp != null)
            {
                indexPath = Path.Combine(target, name + ".index");
                File.Move(Path.Combine(target, "model.index.part"), indexPath);
            }
            File.WriteAllText(Path.Combine(target, "source.txt"), $"目录：{entry.CatalogUrl}{Environment.NewLine}文件：{files.ModelUrl}{Environment.NewLine}下载时间：{DateTime.Now:O}{Environment.NewLine}", new System.Text.UTF8Encoding(false));
            DownloadedVoice = new VoiceRecord
            {
                Id = "online_" + Guid.NewGuid().ToString("N"), Name = name, Model = modelPath, Index = indexPath,
                Source = "voice-models.com · " + entry.SourceUrl.Host, Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Version = "在线模型", SampleRate = "需环境识别"
            };
            status.Text = "下载完成：已保存在软件目录下的 voices 文件夹，并加入本地音色库。";
        }
        catch (OperationCanceledException) { status.Text = "下载已取消；未完成的临时文件已清理。"; }
        catch (Exception e) { status.Text = "下载失败：" + e.Message; }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            downloadCancellation.Dispose(); downloadCancellation = null;
            searchButton.IsEnabled = true; UpdateSelectedDetails(); cancelDownloadButton.Visibility = Visibility.Collapsed;
            downloadProgress.IsIndeterminate = false; downloadProgress.Visibility = Visibility.Collapsed;
        }
        if (DownloadedVoice != null && IsVisible) DialogResult = true;
    }

    async Task<(Uri ModelUrl, Uri? IndexUrl)> ResolveFilesAsync(VoiceCatalogEntry entry, CancellationToken token)
    {
        var uri = entry.SourceUrl;
        if (uri.Host.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("drive.usercontent.google.com", StringComparison.OrdinalIgnoreCase))
        {
            var id = GoogleDriveId(uri);
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("目录没有提供有效的 Google Drive 文件编号。请在网页上打开来源检查。" );
            return (new Uri("https://drive.google.com/uc?export=download&confirm=t&id=" + Uri.EscapeDataString(id)), null);
        }
        if (uri.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("hf.co", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2) throw new InvalidOperationException("Hugging Face 来源链接缺少仓库名称。" );
            var owner = segments[0]; var repo = segments[1];
            var resolveAt = Array.IndexOf(segments, "resolve");
            if (resolveAt < 0) resolveAt = Array.IndexOf(segments, "blob");
            var isFile = resolveAt >= 0 && resolveAt + 2 < segments.Length;
            if (isFile)
            {
                var filePath = string.Join('/', segments.Skip(resolveAt + 2));
                var extension = Path.GetExtension(filePath);
                if (!extension.Equals(".pth", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Hugging Face 文件链接不是 .pth 模型。" );
                return (new Uri($"https://huggingface.co/{owner}/{repo}/resolve/{segments[resolveAt + 1]}/{EscapePath(filePath)}?download=true"), null);
            }
            var api = new Uri($"https://huggingface.co/api/models/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}");
            using var response = await http.GetAsync(api, token); response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var siblings = json.RootElement.TryGetProperty("siblings", out var files) && files.ValueKind == JsonValueKind.Array
                ? files.EnumerateArray().Select(x => x.TryGetProperty("rfilename", out var name) ? name.GetString() ?? "" : "").Where(x => x.Length > 0).ToList()
                : [];
            var models = siblings.Where(x => Path.GetExtension(x).Equals(".pth", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(x).StartsWith("G_", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(x).StartsWith("D_", StringComparison.OrdinalIgnoreCase)
                && !x.Contains("pretrained", StringComparison.OrdinalIgnoreCase)).ToList();
            if (models.Count != 1) throw new InvalidOperationException(models.Count == 0
                ? "Hugging Face 仓库里没有找到唯一的 .pth 音色模型。"
                : "该仓库包含多个 .pth 文件，暂时无法自动判断主音色。请先在网页确认并选择具体文件。" );
            var model = models[0]; var stem = Path.GetFileNameWithoutExtension(model);
            var index = siblings.FirstOrDefault(x => Path.GetExtension(x).Equals(".index", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(x).StartsWith(stem, StringComparison.OrdinalIgnoreCase));
            var modelUrl = new Uri($"https://huggingface.co/{owner}/{repo}/resolve/main/{EscapePath(model)}?download=true");
            var indexUrl = index == null ? null : new Uri($"https://huggingface.co/{owner}/{repo}/resolve/main/{EscapePath(index)}?download=true");
            return (modelUrl, indexUrl);
        }
        if (Path.GetExtension(uri.AbsolutePath).Equals(".pth", StringComparison.OrdinalIgnoreCase)) return (uri, null);
        throw new InvalidOperationException("这个文件托管来源暂不支持应用内直下。请打开目录页检查文件链接。" );
    }

    async Task DownloadFileAsync(Uri uri, string destination, CancellationToken token)
    {
        status.Text = "正在从 " + uri.Host + " 下载模型…";
        using var response = await GetDownloadResponseAsync(uri, token);
        var size = response.Content.Headers.ContentLength;
        if (size is > 2_147_483_648) throw new InvalidOperationException("模型文件超过 2 GB，已停止下载。" );
        downloadProgress.IsIndeterminate = size == null || size <= 0;
        var received = 0L;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true);
        var buffer = new byte[1024 * 128];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), token);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            received += read;
            if (received > 2_147_483_648) throw new InvalidDataException("模型文件超过 2 GB，已停止下载。");
            if (size is > 0) { downloadProgress.IsIndeterminate = false; downloadProgress.Value = Math.Clamp(received * 100d / size.Value, 0, 100); }
            status.Text = size is > 0 ? $"正在从 {uri.Host} 下载模型… {received / 1048576d:0.0} / {size.Value / 1048576d:0.0} MB" : $"正在从 {uri.Host} 下载模型… {received / 1048576d:0.0} MB";
        }
        if (received < 1024 * 1024) throw new InvalidDataException("下载结果过小，不像有效的 RVC 模型文件。" );
        await output.FlushAsync(token);
    }

    async Task<HttpResponseMessage> GetDownloadResponseAsync(Uri initial, CancellationToken token)
    {
        var current = initial;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await http.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = response.StatusCode;
                response.Dispose();
                throw new HttpRequestException("模型来源返回 HTTP " + (int)statusCode + "。请打开来源网页核对分享权限后重试。");
            }
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase)) return response;

            string html;
            try { html = await response.Content.ReadAsStringAsync(token); }
            finally { response.Dispose(); }
            var next = GoogleDriveConfirmation(current, html);
            if (next == null) throw new InvalidOperationException("来源返回了网页而不是模型文件。请打开网页检查分享权限，或在网页上手动确认下载。" );
            current = next;
        }
        throw new InvalidOperationException("Google Drive 仍未返回模型文件。请打开来源网页确认分享权限后重试。" );
    }

    Uri? GoogleDriveConfirmation(Uri current, string html)
    {
        if (!IsGoogleHost(current.Host)) return null;
        var decoded = WebUtility.HtmlDecode(html);
        var warning = cookies.GetCookies(current).Cast<Cookie>().FirstOrDefault(x => x.Name.StartsWith("download_warning", StringComparison.OrdinalIgnoreCase));
        var href = Regex.Match(decoded, @"href\s*=\s*[""'](?<url>[^""']*confirm=[^""']*)[""']", RegexOptions.IgnoreCase);
        if (href.Success && Uri.TryCreate(current, href.Groups["url"].Value, out var linked)
            && linked.Scheme == Uri.UriSchemeHttps && IsGoogleHost(linked.Host)) return linked;
        var token = warning?.Value ?? Regex.Match(decoded, @"(?:[?&]|&amp;)confirm=([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase).Groups[1].Value;
        var id = GoogleDriveId(current);
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(id)) return null;
        return new Uri("https://drive.google.com/uc?export=download&confirm=" + Uri.EscapeDataString(token) + "&id=" + Uri.EscapeDataString(id));
    }

    static string? GoogleDriveId(Uri uri)
    {
        var path = Regex.Match(uri.AbsolutePath, @"/file/d/([^/]+)", RegexOptions.IgnoreCase);
        if (path.Success) return Uri.UnescapeDataString(path.Groups[1].Value);
        return uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2)).Where(x => x.Length == 2 && x[0].Equals("id", StringComparison.OrdinalIgnoreCase))
            .Select(x => Uri.UnescapeDataString(x[1])).FirstOrDefault();
    }

    static bool IsGoogleHost(string host) => host.Equals("google.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase);

    static void OpenWebPage(Uri uri)
    {
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception e) { MessageBox.Show("无法打开网页：" + e.Message, "声屿", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    string UniqueVoiceName(string original)
    {
        var name = SafeName(original);
        var candidate = name; var suffix = 2;
        while (Directory.Exists(Path.Combine(voicesDirectory, candidate))) candidate = $"{name} ({suffix++})";
        return candidate;
    }

    static string SafeName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        value = Regex.Replace(value, @"[. ]+$", "").Trim();
        return string.IsNullOrWhiteSpace(value) ? "在线 RVC 音色" : value[..Math.Min(value.Length, 64)];
    }
    static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    static TextBlock Text(string value, string color) => new() { Text = value, Foreground = Color(color), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8), LineHeight = 21 };
    static SolidColorBrush Color(string hex) => new((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));
}

public sealed record VoiceCatalogEntry(string Name, string Creator, string Size, Uri CatalogUrl, Uri SourceUrl)
{
    public string Display => $"{Name}   ·   {(string.IsNullOrWhiteSpace(Creator) ? "社区" : Creator)}   ·   {(string.IsNullOrWhiteSpace(Size) ? "大小未知" : Size)}";
}
