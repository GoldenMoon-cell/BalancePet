using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BalancePet.Wpf.Services;

namespace BalancePet.SelfTest;

/// <summary>
/// Exercises <see cref="ShippedPetMigration"/> against throwaway directories.
///
/// The migration is destructive by design — it deletes the folders it converts —
/// and it has to be trusted before it is allowed near a real installation. Running
/// it here proves the parts that matter: that a package is actually installed, that
/// the original is only removed once that succeeded, that a failure leaves the
/// original untouched, and that running twice changes nothing the second time.
/// </summary>
internal static class Program
{
    private static readonly string[] RequiredStates =
    [
        "idle.png", "loading.png", "success.png", "low.png", "error.png",
        "clicked.png", "codex-working.png", "codex-done.png", "inactive.png"
    ];

    private static int _failures;

    private static async Task<int> Main()
    {
        // Deliberately not under %TEMP%. The workspace carries a low mandatory
        // label, so anything started from it inherits a low integrity token and is
        // refused when it tries to create a directory in a medium-integrity
        // location. The build output directory is writable, so scratch space lives
        // beside the test binary instead.
        var workspace = Path.Combine(AppContext.BaseDirectory, $"selftest-{Guid.NewGuid():N}");
        var appDirectory = Path.Combine(workspace, "app");
        var extensionsRoot = Path.Combine(workspace, "extensions");
        var realPets = LocateRealPetAssets();

        if (realPets is null)
        {
            Console.WriteLine("找不到真实形象素材，无法运行自测。");
            return 2;
        }

        try
        {
            // --- 1. Two shipped appearances become packages -------------------
            Stage(appDirectory, realPets, "qwen");
            Stage(appDirectory, realPets, "gemini");
            // A distribution appearance sits in the same folder and must be left alone.
            // Converting it cannot succeed -- the manager refuses a package whose style
            // collides with a built-in id -- and that failure stops the completion
            // marker from ever being written, so every launch would stage an archive
            // for it and throw it away again.
            Stage(appDirectory, realPets, "deepseek");
            var result = ShippedPetMigration.Run(appDirectory, extensionsRoot);
            Check("迁移数量 = 2", result.Migrated == 2, $"实际 {result.Migrated}");
            Check("无失败", result.Failed.Count == 0, string.Join("；", result.Failed));
            Check("随程序提供的形象不被转换", Directory.Exists(Path.Combine(appDirectory, "assets", "pets", "deepseek")));
            Check("随程序提供的形象没有装成包", !InstalledStyles(extensionsRoot).Contains("deepseek"));
            Check("完成后写下标记", File.Exists(Path.Combine(workspace, "pet-migration.v1.done")),
                Path.Combine(workspace, "pet-migration.v1.done"));
            Check("qwen 原目录已删除", !Directory.Exists(Path.Combine(appDirectory, "assets", "pets", "qwen")));
            Check("gemini 原目录已删除", !Directory.Exists(Path.Combine(appDirectory, "assets", "pets", "gemini")));
            Check("qwen 包已安装", InstalledStyles(extensionsRoot).Contains("qwen"));
            Check("gemini 包已安装", InstalledStyles(extensionsRoot).Contains("gemini"));
            Check("包的 style 等于内置 id", ManifestStyle(extensionsRoot, "pet.qwen") == "qwen",
                $"实际 {ManifestStyle(extensionsRoot, "pet.qwen")}");
            Check("包内含 9 张状态图", CountStates(extensionsRoot, "pet.qwen") == 9,
                $"实际 {CountStates(extensionsRoot, "pet.qwen")}");

            // --- 2. A second run is a no-op -----------------------------------
            var again = ShippedPetMigration.Run(appDirectory, extensionsRoot);
            Check("重复运行不迁移", again.Migrated == 0, $"实际 {again.Migrated}");
            Check("重复运行不失败", again.Failed.Count == 0, string.Join("；", again.Failed));

            // --- 3. Incomplete artwork is left alone --------------------------
            Stage(appDirectory, realPets, "claude");
            File.Delete(Path.Combine(appDirectory, "assets", "pets", "claude", "idle.png"));
            var partial = ShippedPetMigration.Run(appDirectory, extensionsRoot);
            Check("缺状态图的不迁移", partial.Migrated == 0, $"实际 {partial.Migrated}");
            Check("缺状态图的原目录保留", Directory.Exists(Path.Combine(appDirectory, "assets", "pets", "claude")));

            // --- 4. An unregistered folder is not guessed at ------------------
            var stranger = Path.Combine(appDirectory, "assets", "pets", "notarealpet");
            Directory.CreateDirectory(stranger);
            foreach (var state in RequiredStates) File.Copy(Path.Combine(realPets, "qwen", state), Path.Combine(stranger, state));
            var unknown = ShippedPetMigration.Run(appDirectory, extensionsRoot);
            Check("未登记的形象不动", unknown.Migrated == 0 && Directory.Exists(stranger), $"迁移 {unknown.Migrated}");

            // --- 5. A duplicate of an installed package is only reclaimed -----
            Stage(appDirectory, realPets, "qwen");
            var duplicate = ShippedPetMigration.Run(appDirectory, extensionsRoot);
            Check("重复形象被回收", duplicate.Reclaimed == 1, $"实际 {duplicate.Reclaimed}");
            Check("重复形象不再迁移", duplicate.Migrated == 0, $"实际 {duplicate.Migrated}");
            Check("重复形象目录已删除", !Directory.Exists(Path.Combine(appDirectory, "assets", "pets", "qwen")));

            // --- 6. The animation contract ------------------------------------
            // Frames are found by name, so the numbering and the rule that a gap ends
            // the sequence are the entire contract and are worth pinning down. This
            // runs against a synthetic tree because the point is the naming, not the
            // artwork, and no real appearance has to animate for it to hold.
            var probeRoot = Path.Combine(workspace, "probe");
            var probe = Path.Combine(probeRoot, "assets", "pets", "zz-probe-anim");
            Directory.CreateDirectory(probe);
            var sample = Path.Combine(realPets, "qwen", "idle.png");
            foreach (var name in new[] { "idle.png", "idle-2.png", "idle-3.png", "loading.png" })
                File.Copy(sample, Path.Combine(probe, name));

            var idleFrames = PetStyleCatalog.ResolveStateFrames("zz-probe-anim", "idle", probeRoot);
            Check("首帧沿用原名", idleFrames.Count == 3 && Path.GetFileName(idleFrames[0]) == "idle.png",
                string.Join(",", idleFrames.Select(Path.GetFileName)));
            Check("单帧状态只有一帧", PetStyleCatalog.ResolveStateFrames("zz-probe-anim", "loading", probeRoot).Count == 1);
            Check("没有的状态返回空", PetStyleCatalog.ResolveStateFrames("zz-probe-anim", "success", probeRoot).Count == 0);

            File.Copy(sample, Path.Combine(probe, "idle-5.png"));
            Check("序号断档即终止", PetStyleCatalog.ResolveStateFrames("zz-probe-anim", "idle", probeRoot).Count == 3,
                string.Join(",", PetStyleCatalog.ResolveStateFrames("zz-probe-anim", "idle", probeRoot).Select(Path.GetFileName)));

            for (var index = 4; index <= 9; index++) File.Copy(sample, Path.Combine(probe, $"idle-{index}.png"), overwrite: true);
            Check($"帧数封顶在 {PetStyleCatalog.MaxAnimationFrames}",
                PetStyleCatalog.ResolveStateFrames("zz-probe-anim", "idle", probeRoot).Count == PetStyleCatalog.MaxAnimationFrames,
                $"实际 {PetStyleCatalog.ResolveStateFrames("zz-probe-anim", "idle", probeRoot).Count}");

            // --- 7. The built-in placeholder cannot go missing ----------------
            // It is the only shape guaranteed to be on disk, so it has to survive the
            // id normalisation that decides what gets drawn, and it has to be complete
            // enough to be offered: an appearance missing a state is not listed at all.
            // In the source tree the project directory plays the part of the installed
            // application directory: assets/ sits directly under it, exactly as it does
            // under the install folder at run time.
            var appRoot = Path.GetFullPath(Path.Combine(realPets, "..", ".."));
            Check("占位形象 id 经归一化不变", PetStyleCatalog.NormalizeId(PetStyleCatalog.FallbackId) == PetStyleCatalog.FallbackId,
                PetStyleCatalog.NormalizeId(PetStyleCatalog.FallbackId));
            Check("占位形象是已登记形象", PetStyleCatalog.TryGetDefinition(PetStyleCatalog.FallbackId, out _));
            var placeholder = Path.Combine(realPets, PetStyleCatalog.FallbackId);
            Check("占位形象九张状态图齐全", RequiredStates.All(state => File.Exists(Path.Combine(placeholder, state))));
            var placeholderFrames = PetStyleCatalog.ResolveStateFrames(PetStyleCatalog.FallbackId, "idle", appRoot).Count;
            Check("占位形象自带 idle 动画", placeholderFrames > 1, $"实际 {placeholderFrames} 帧");

            // --- 8. Update requests survive a dropped connection ---------------
            // A drop during the TLS handshake looks exactly like a server that is
            // briefly unreachable, and it used to end the check with an error dialog
            // quoting the transport. These use a stub handler because what is being
            // pinned is the retry policy, not GitHub.
            var flakyAttempts = 0;
            var flaky = new UpdateService(new HttpClient(new StubHandler(_ =>
            {
                if (++flakyAttempts < 2) throw new IOException("Received an unexpected EOF or 0 bytes from the transport stream.");
                return Json("[]");
            })));
            await flaky.CheckAsync("1.0.0");
            Check("断连一次后自动重试", flakyAttempts == 2, $"实际请求 {flakyAttempts} 次");

            var deadAttempts = 0;
            var dead = new UpdateService(new HttpClient(new StubHandler(_ =>
            {
                deadAttempts++;
                throw new IOException("Received an unexpected EOF or 0 bytes from the transport stream.");
            })));
            var deadMessage = "";
            try { await dead.CheckAsync("1.0.0"); }
            catch (HttpRequestException error) { deadMessage = error.Message; }
            Check("重试用尽后报可读原因", deadAttempts == 3 && deadMessage.StartsWith("网络连接中断", StringComparison.Ordinal),
                $"请求 {deadAttempts} 次 / {deadMessage}");
            Check("技术细节仍保留在括号里", deadMessage.Contains("unexpected EOF", StringComparison.Ordinal));

            var notFoundAttempts = 0;
            var missing = new UpdateService(new HttpClient(new StubHandler(_ =>
            {
                notFoundAttempts++;
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            })));
            var notFoundMessage = "";
            try { await missing.CheckAsync("1.0.0"); }
            catch (HttpRequestException error) { notFoundMessage = error.Message; }
            Check("HTTP 错误状态不重试", notFoundAttempts == 1, $"实际请求 {notFoundAttempts} 次");
            Check("HTTP 错误保留状态码", notFoundMessage.Contains("404", StringComparison.Ordinal), notFoundMessage);

            // --- 9. A download cut short resumes instead of starting over -------
            // Restarting an 86 MB transfer on a connection that drops is what makes it
            // never finish, so the partial file has to survive and the next request has
            // to ask only for the rest. These use a stub that really does cut the first
            // response in half, because the interesting part is what lands on disk.
            var payload = new byte[200_000];
            Random.Shared.NextBytes(payload);
            var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

            async Task<(bool Ok, byte[] Bytes, List<long> From)> Download(RangeServer server)
            {
                var service = new UpdateService(new HttpClient(new StubHandler(server.Respond)));
                var asset = new UpdateAsset(UpdateAssetKind.PortableArchive, "x.zip", new Uri("https://github.com/x/y.zip"), digest);
                var path = await service.DownloadAsync(asset);
                var bytes = await File.ReadAllBytesAsync(path);
                File.Delete(path);
                return (bytes.Length == payload.Length && bytes.SequenceEqual(payload), bytes, server.RequestedFrom);
            }

            var resumed = await Download(new RangeServer(payload, dropAfter: 50_000));
            Check("断连后续传得到完整文件", resumed.Ok, $"实际 {resumed.Bytes.Length} 字节");
            Check("第二次请求只取剩余部分", resumed.From.Count == 2 && resumed.From[1] == 50_000, string.Join(",", resumed.From));

            var ignored = await Download(new RangeServer(payload, dropAfter: 50_000) { IgnoreRange = true });
            Check("服务器不支持 Range 时从头重来", ignored.Ok, $"实际 {ignored.Bytes.Length} 字节");

            // --- 10. The two catalogs cannot be mistaken for each other ---------
            // They are fetched from different repositories by URL, and a URL is the one
            // thing that can silently point somewhere else. Each parser has to refuse the
            // other's document, or a swapped address would be accepted and produce a list
            // of entries that cannot be installed. These read the real published files
            // rather than fixtures, so the two shapes cannot drift apart unnoticed.
            var repoRoot = Path.GetFullPath(Path.Combine(appRoot, "..", ".."));
            var appearanceJson = File.ReadAllText(Path.Combine(repoRoot, "skins", "catalog.json"));
            var pluginJson = File.ReadAllText(Path.Combine(repoRoot, "plugin-catalog.json"));

            var appearances = PluginCatalogService.ParseAppearances(appearanceJson);
            Check("形象目录解析出 12 条", appearances.Count == 12, $"实际 {appearances.Count}");
            Check("形象条目都是 pet 类型", appearances.All(item => item.Type == "pet"));
            Check("形象条目可安装", appearances.All(item => item.Id.StartsWith("pet.", StringComparison.Ordinal)
                && item.Sha256.Length == 64 && item.DownloadUrl.EndsWith(".zip", StringComparison.Ordinal)));

            var plugins = PluginCatalogService.Parse(pluginJson);
            Check("插件目录仍解析出 4 条", plugins.Count == 4, $"实际 {plugins.Count}");

            var crossed = "";
            try { PluginCatalogService.ParseAppearances(pluginJson); }
            catch (InvalidDataException error) { crossed = error.Message; }
            Check("插件目录不会被当成形象目录", crossed.Contains("不是形象目录", StringComparison.Ordinal), crossed);

            // What a wrong URL really returns is not another catalog but any JSON at
            // all, which declares no identity.
            var anonymous = "";
            try { PluginCatalogService.ParseAppearances("""{"schema_version":1,"appearances":[]}"""); }
            catch (InvalidDataException error) { anonymous = error.Message; }
            Check("没有标识的文档被拒绝", anonymous.Contains("不是形象目录", StringComparison.Ordinal), anonymous);

            // --- 11. Each appearance's lines travel with its artwork --------------
            // The program keeps only a neutral fallback, so an appearance with no lines
            // of its own must never be handed another character's name.
            //
            // Read from the repository rather than through PetLineCatalog for the sets
            // that are published as packages: resolution deliberately prefers an
            // installed package, and on a machine where those packages predate this file
            // they would answer with the fallback, which says nothing about the files.
            // The two shipped appearances and the placeholder cannot be shadowed, so
            // they exercise the lookup itself.
            // The fallback is checked with an id that cannot exist, because the placeholder
            // has its own lines now and would answer from its file instead.
            var neutralInactive = PetLineCatalog.Resolve("no-such-appearance", "inactive").Select(line => line.Label).ToArray();
            Check("中性文案可用", neutralInactive.Length >= 3, $"{neutralInactive.Length} 条");
            Check("中性文案不提任何角色名",
                neutralInactive.All(label => !label.Contains("汐") && !label.Contains("霁珑")
                    && !label.Contains("澄芽") && !label.Contains("橙析")),
                string.Join(" / ", neutralInactive));
            Check("未安装的形象回落到中性文案",
                PetLineCatalog.Resolve("mistral", "inactive").Select(line => line.Label).SequenceEqual(neutralInactive));
            Check("随程序提供的形象读到自己的文案",
                PetLineCatalog.Resolve("chatgpt", "inactive").Select(line => line.Label).FirstOrDefault()?.Contains("霁珑") == true,
                string.Join(" / ", PetLineCatalog.Resolve("chatgpt", "inactive").Select(line => line.Label)));

            var withArt = new[] { "_placeholder", "chatgpt", "claude", "deepseek", "ernie", "gemini", "glm", "gpt-image2",
                                  "grok", "kimi", "llama", "mimo", "minimax", "qwen", "seedance" };
            var problems = new List<string>();
            foreach (var style in withArt)
            {
                var path = Path.Combine(appRoot, "assets", "pets", style, "lines.json");
                if (!File.Exists(path)) { problems.Add($"{style}：没有 lines.json"); continue; }
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    var root = document.RootElement;
                    if (root.GetProperty("schema_version").GetInt32() != 1) problems.Add($"{style}：schema_version 不是 1");

                    var labels = new List<string>();
                    foreach (var key in new[] { "inactive", "bubble", "streak" })
                    {
                        if (!root.TryGetProperty(key, out var section) || section.GetArrayLength() == 0)
                        {
                            problems.Add($"{style}：缺少 {key}");
                            continue;
                        }
                        foreach (var entry in section.EnumerateArray()) labels.Add(entry.GetProperty("label").GetString() ?? "");
                    }
                    if (!root.TryGetProperty("touch", out var touch)) problems.Add($"{style}：缺少 touch");
                    else
                    {
                        foreach (var zone in new[] { "hair", "mouth", "body" })
                        {
                            if (!touch.TryGetProperty(zone, out var entries) || entries.GetArrayLength() == 0) problems.Add($"{style}：touch 缺少 {zone}");
                            else foreach (var entry in entries.EnumerateArray()) labels.Add(entry.GetProperty("label").GetString() ?? "");
                        }
                    }
                    // A set that is byte-for-byte the fallback means the file is there but
                    // empty in effect, which is worse than not shipping one.
                    if (labels.Count > 0 && labels.SequenceEqual(neutralInactive)) problems.Add($"{style}：与中性文案相同");
                }
                catch (JsonException error) { problems.Add($"{style}：JSON 解析失败 {error.Message}"); }
            }
            Check($"有美术的形象都带自己的文案（{withArt.Length} 套 × 4 类）", problems.Count == 0, string.Join("；", problems));

            Check("澄芽说的是自己的话", ReadLabels(appRoot, "seedance").Any(label => label.Contains("澄芽")));
            Check("橙析说的是自己的话", ReadLabels(appRoot, "mimo").Any(label => label.Contains("橙析")));
            // The placeholder is what a fresh offline install shows, so it says what it is
            // rather than borrowing a character's voice.
            Check("占位形象说的是占位的话", ReadLabels(appRoot, "_placeholder").Any(label => label.Contains("占位")));

            // A file the program cannot parse must fall back rather than fail: a package
            // authored against a future schema still has to draw.
            Check("未知 schema 回落到中性文案", PetLineCatalog.IsReadable("""{"schema_version":99,"inactive":[]}""") == false);
            Check("空文件回落到中性文案", PetLineCatalog.IsReadable("") == false);
            Check("正常文件可读", PetLineCatalog.IsReadable(File.ReadAllText(Path.Combine(appRoot, "assets", "pets", "chatgpt", "lines.json"))));

            // The digest is what makes resuming safe: bytes appended from a source that
            // answered the wrong range would otherwise be installed as an update.
            var corrupted = "";
            try { await Download(new RangeServer(payload, dropAfter: 50_000) { CorruptResume = true }); }
            catch (InvalidDataException error) { corrupted = error.Message; }
            Check("续传拼错的文件被校验拦下", corrupted.Contains("校验失败", StringComparison.Ordinal), corrupted);
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch (IOException) { }
        }

        Console.WriteLine(_failures == 0 ? "\nALL CHECKS PASSED" : $"\n{_failures} CHECK(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static void Check(string what, bool ok, string detail = "")
    {
        if (ok) { Console.WriteLine($"  PASS  {what}"); return; }
        _failures++;
        Console.WriteLine($"  FAIL  {what}{(detail.Length == 0 ? "" : $"  ({detail})")}");
    }

    /// <summary>A response carrying a JSON body, as the update endpoints answer.</summary>
    private static HttpResponseMessage Json(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>Every label in an appearance's lines file, read straight from the repository.</summary>
    private static List<string> ReadLabels(string appRoot, string style)
    {
        var labels = new List<string>();
        var path = Path.Combine(appRoot, "assets", "pets", style, "lines.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        foreach (var key in new[] { "inactive", "bubble", "streak" })
        {
            if (!root.TryGetProperty(key, out var section)) continue;
            foreach (var entry in section.EnumerateArray()) labels.Add(entry.GetProperty("label").GetString() ?? "");
        }
        if (root.TryGetProperty("touch", out var touch))
        {
            foreach (var zone in touch.EnumerateObject())
                foreach (var entry in zone.Value.EnumerateArray())
                    labels.Add(entry.GetProperty("label").GetString() ?? "");
        }
        return labels;
    }

    /// <summary>
    /// Answers every request with whatever the test asks for, including by throwing,
    /// so the update request policy can be exercised without a network.
    /// </summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    /// <summary>
    /// Serves a payload over ranged requests and cuts the first one short, so a
    /// download that resumes can be exercised without a network.
    /// </summary>
    private sealed class RangeServer(byte[] payload, int dropAfter)
    {
        /// <summary>Answers 200 with the whole file however it is asked, like a host without Range.</summary>
        public bool IgnoreRange { get; init; }

        /// <summary>Returns wrong bytes for a resumed range, to prove the digest catches it.</summary>
        public bool CorruptResume { get; init; }

        public List<long> RequestedFrom { get; } = new();

        public HttpResponseMessage Respond(HttpRequestMessage request)
        {
            var from = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
            var first = RequestedFrom.Count == 0;
            RequestedFrom.Add(from);
            if (from > 0 && IgnoreRange) from = 0;

            if (from >= payload.Length) return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);

            var body = payload[(int)from..];
            if (from > 0 && CorruptResume) body = body.Reverse().ToArray();
            var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new DripContent(body, first ? dropAfter : int.MaxValue)
            };
            response.Content.Headers.ContentLength = body.Length;
            return response;
        }
    }

    /// <summary>Yields the payload, then fails the way a dropped connection does.</summary>
    private sealed class DripContent(byte[] body, int dropAfter) : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new DripStream(body, dropAfter));
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;
        protected override bool TryComputeLength(out long length) { length = body.Length; return true; }
    }

    private sealed class DripStream(byte[] body, int dropAfter) : Stream
    {
        private int _position;

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_position >= dropAfter) throw new IOException("Received an unexpected EOF or 0 bytes from the transport stream.");
            var take = Math.Min(Math.Min(buffer.Length, dropAfter - _position), body.Length - _position);
            if (take <= 0) return 0;
            body.AsSpan(_position, take).CopyTo(buffer);
            _position += take;
            return take;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Copies a real appearance into a throwaway application directory.</summary>
    private static void Stage(string appDirectory, string realPets, string style)
    {
        var target = Path.Combine(appDirectory, "assets", "pets", style);
        Directory.CreateDirectory(target);
        foreach (var state in RequiredStates) File.Copy(Path.Combine(realPets, style, state), Path.Combine(target, state), overwrite: true);
    }

    /// <summary>
    /// Locates the repository's real artwork so the packages built here contain
    /// genuine images rather than fixtures that could pass with the wrong paths.
    /// </summary>
    private static string? LocateRealPetAssets()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "versions", "csharp-wpf", "assets", "pets");
            if (Directory.Exists(Path.Combine(candidate, "qwen")) && Directory.Exists(Path.Combine(candidate, "gemini"))) return candidate;
            directory = directory.Parent;
        }
        return null;
    }

    private static IReadOnlyList<string> InstalledStyles(string extensionsRoot)
    {
        var manager = new PetExtensionManager(extensionsRoot);
        return manager.GetInstalled().Select(info => info.StyleId).ToArray();
    }

    private static string ManifestStyle(string extensionsRoot, string packageId)
    {
        var packageRoot = Path.Combine(extensionsRoot, packageId);
        if (!Directory.Exists(packageRoot)) return "";
        var manifest = Directory.EnumerateFiles(packageRoot, "manifest.json", SearchOption.AllDirectories).FirstOrDefault();
        if (manifest is null) return "";
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        return document.RootElement.TryGetProperty("style", out var style) ? style.GetString() ?? "" : "";
    }

    private static int CountStates(string extensionsRoot, string packageId)
        => Directory.Exists(Path.Combine(extensionsRoot, packageId))
            ? Directory.EnumerateFiles(Path.Combine(extensionsRoot, packageId), "*.png", SearchOption.AllDirectories).Count()
            : 0;
}
