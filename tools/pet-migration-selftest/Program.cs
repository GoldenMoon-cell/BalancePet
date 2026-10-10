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
            // DeepSeek used to ship with the program, and conversion was refused for it.
            // It is a package now, so an installation upgrading from that version has its
            // folder converted like any other -- which is how an existing user keeps the
            // appearance without downloading anything.
            Stage(appDirectory, realPets, "deepseek");
            // Planted before the run on purpose. This is the file a previous release left
            // behind after a migration run found nothing to convert -- true at the time,
            // because DeepSeek and ChatGPT were built-ins and were skipped. Moving them
            // out of the built-in list is precisely what makes them convertible, so a
            // marker that outlives that change must not block it. Every installation
            // upgrading across that release has this file, so the conversions asserted
            // just below are also the assertion that it no longer stops anything.
            File.WriteAllText(Path.Combine(workspace, "pet-migration.v1.done"), "2026-01-01T00:00:00+00:00");
            var result = ShippedPetMigration.Run(appDirectory, extensionsRoot);
            Check("迁移数量 = 3", result.Migrated == 3, $"实际 {result.Migrated}");
            Check("无失败", result.Failed.Count == 0, string.Join("；", result.Failed));
            Check("原随程序提供的形象也被转换",
                !Directory.Exists(Path.Combine(appDirectory, "assets", "pets", "deepseek"))
                && InstalledStyles(extensionsRoot).Contains("deepseek"));
            // Left where it is rather than swept away: nothing reads it, and deleting a
            // file in a directory the program shares with the user is not free.
            Check("遗留的完成标记原样保留", File.Exists(Path.Combine(workspace, "pet-migration.v1.done")));
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

            // The budget is sixty requests an hour for a whole IP address, unauthenticated,
            // and a shared address spends it through nobody's fault in particular — measured
            // on this machine, a virtual private network's exit had spent all sixty and every
            // automatic check was answering 403. So the check costs one request: the release
            // endpoint carries its assets inline, digests and all, and asking for them
            // separately doubled the price of the commonest thing this program does.
            var checkCalls = new List<string>();
            using (var oneCall = new HttpClient(new StubHandler(request =>
            {
                lock (checkCalls) checkCalls.Add(request.RequestUri!.AbsolutePath);
                return Json("""
                    {"tag_name":"v9.9.9","name":"9.9.9","draft":false,"prerelease":false,"body":"notes",
                     "assets":[{"name":"BalancePet-9.9.9-win-x64.zip",
                                "browser_download_url":"https://github.com/o/r/releases/download/v9.9.9/BalancePet-9.9.9-win-x64.zip",
                                "digest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]}
                    """);
            })))
            {
                var service = new UpdateService(oneCall, workspace);
                var release = await service.CheckAsync("1.5.0");
                Check("更新检查只花一次请求", checkCalls.Count == 1, string.Join(",", checkCalls));
                Check("内联资产里认得出更新包",
                    release?.PortableArchive is not null && release.PortableArchive.Name == "BalancePet-9.9.9-win-x64.zip",
                    release?.PortableArchive?.Name ?? "(没认出来)");
                Check("内联资产的哈希被带上（下载仍会校验）",
                    release?.PortableArchive?.Digest == "sha256:" + new string('a', 64),
                    release?.PortableArchive?.Digest ?? "(没有)");
            }

            // 403 is not a network fault and must not read like one: the address is out of
            // allowance, it refills by itself, and saying so is the difference between a
            // user waiting an hour and a user reinstalling the program.
            var forbidden = new UpdateService(new HttpClient(new StubHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { ReasonPhrase = "rate limit exceeded" };
                response.Headers.Add("x-ratelimit-reset", "1791041679");
                return response;
            })));
            var forbiddenMessage = "";
            try { await forbidden.CheckAsync("1.0.0"); }
            catch (HttpRequestException error) { forbiddenMessage = error.Message; }
            Check("403 说明是配额而不是网络故障",
                forbiddenMessage.Contains("60", StringComparison.Ordinal)
                && forbiddenMessage.Contains("出口 IP", StringComparison.Ordinal)
                && forbiddenMessage.Contains("23:34", StringComparison.Ordinal)
                && forbiddenMessage.Contains("与你本机的网络是否通畅无关", StringComparison.Ordinal),
                forbiddenMessage);

            // Two faults in this program's own event log each killed it: a font family whose
            // file is missing threw while the settings window built its font list, and a
            // catalog load used an HTTP client the window had already disposed. The second
            // is prevented rather than swallowed — a cancelled token stops the load before
            // it reaches the next source, so the request that would touch the disposed
            // client is never sent.
            var catalogRequests = 0;
            using (var closing = new HttpClient(new StubHandler(_ =>
            {
                Interlocked.Increment(ref catalogRequests);
                return Json("{}");
            })))
            {
                var catalog = new PluginCatalogService(closing);
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                var cancelledMessage = "";
                try { await catalog.LoadAsync(cancelled.Token); }
                catch (OperationCanceledException error) { cancelledMessage = error.GetType().Name; }
                Check("窗口关掉后不再向已释放的客户端发请求",
                    catalogRequests == 0 && cancelledMessage == "OperationCanceledException",
                    $"请求 {catalogRequests} 次 / {cancelledMessage}");
            }

            // The font guard, which is what ended the process when a user opened the
            // settings window. The throw itself cannot be reproduced on demand — a family
            // naming a missing file reads back as no faces, and a hand-written registry
            // entry pointing at a missing file is skipped by the font stack rather than
            // surfaced, both measured — so what is pinned is the promise a later edit could
            // quietly undo: whatever reading throws, the answer is "cannot be offered".
            // The first exception below is the exact class from the event log, and the rest
            // are the ones a narrowed catch would plausibly forget next.
            // Wrapped, because the failure mode being tested for is an exception escaping
            // the guard: without the wrapper that exception would end the whole run instead
            // of reporting one failed check, which is a worse way to learn about it.
            var guardSkipsEverything = false;
            try
            {
                guardSkipsEverything =
                    !BalancePet.Wpf.SettingsWindow.TryRead(() => throw new System.IO.FileNotFoundException("字体文件不在了"), out _)
                    && !BalancePet.Wpf.SettingsWindow.TryRead(() => throw new System.IO.IOException("磁盘读不到"), out _)
                    && !BalancePet.Wpf.SettingsWindow.TryRead(() => throw new System.Runtime.InteropServices.COMException("字体子系统拒绝"), out _)
                    && !BalancePet.Wpf.SettingsWindow.TryRead(() => throw new UnauthorizedAccessException("没有权限"), out _);
            }
            catch (Exception)
            {
                guardSkipsEverything = false;
            }
            Check("读字体失败一律当作「跳过」，不挑异常类型",
                guardSkipsEverything,
                "有异常类没被拦下 —— 那正是崩溃的原因");

            var readable = BalancePet.Wpf.SettingsWindow.TryReadFaces(
                new System.Windows.Media.FontFamily("Segoe UI"), out var goodFaces);
            Check("正常字体族仍然读得出来",
                readable && goodFaces.Count > 0,
                $"Segoe UI → {(readable ? $"{goodFaces.Count} 个字面" : "读取失败")}");

            // The changelog is handed to the message centre as notification events: the
            // host keeps producing, the extension does the presenting. Two things make that
            // work and both are easy to get wrong — the order, and only ever once.
            var feedWith = """
                {"schema_version":1,"notices":[
                  {"seq":4,"date":"2026-10-01","area":"规范","title":"第四条","summary":"四","url":"https://example.com/4"},
                  {"seq":5,"date":"2026-10-02","area":"文档","title":"第五条","summary":"五","url":"https://example.com/5"},
                  {"seq":6,"date":"2026-10-03","area":"在线内容","title":"第六条","summary":"六","url":"https://example.com/6"}]}
                """;
            Check("喂给消息中心的那份通告能被解析", NoticeFeed.Publish(feedWith), "解析失败");
            var handover = NoticeFeed.RecordableFrom(4);
            Check("只交出水印之后的，且按时间从旧到新",
                handover.Count == 2 && handover[0].Seq == 5 && handover[1].Seq == 6,
                string.Join(",", handover.Select(item => item.Seq)));
            Check("水印推进后再问就是空的（不会每半小时重复写一遍）",
                NoticeFeed.RecordableFrom(6).Count == 0,
                $"{NoticeFeed.RecordableFrom(6).Count} 条");

            // And the record itself, in a directory of its own: the file is read by
            // extensions at a fixed path, so the store only takes a directory so that this
            // can be tested without writing to the profile.
            var eventDirectory = Path.Combine(workspace, "notification-events");
            using (var events = new NotificationEventStore(eventDirectory))
            {
                // Awaited here for the same reason the host awaits them: a queued append is
                // discarded when the store is disposed, so reading the file straight after
                // an un-awaited write is a race, not a test.
                await events.RecordNotice(6, "2026-10-03", "在线内容", "第六条", "六的摘要", "https://example.com/6");
                await events.Record("账户余额", "12.34", "一次普通气泡");

                var lines = File.ReadAllLines(Path.Combine(eventDirectory, "notification-events.ndjson"))
                    .Where(line => line.Trim().Length > 0).ToArray();
                Check("通告与气泡各占一行", lines.Length == 2, $"实际 {lines.Length} 行");
                using (var noticeLine = JsonDocument.Parse(lines[0]))
                {
                    var root = noticeLine.RootElement;
                    Check("通告事件带 notice 类别、原文链接与自己的日期",
                        root.GetProperty("category").GetString() == "notice"
                        && root.GetProperty("url").GetString() == "https://example.com/6"
                        && root.GetProperty("occurred_at").GetString()!.StartsWith("2026-10-03", StringComparison.Ordinal)
                        && root.GetProperty("amount").GetString() == "在线内容",
                        lines[0]);
                    Check("通告的事件 id 由序号决定（重复写入能被认出来）",
                        root.GetProperty("event_id").GetString() == "notice-6",
                        root.GetProperty("event_id").GetString() ?? "(没有)");
                }
                Check("普通气泡事件没有 url（那个字段是通告专用的）",
                    !lines[1].Contains("\"url\"", StringComparison.Ordinal),
                    lines[1]);
            }

            // The record a user can actually read afterwards. Written where the environment
            // points it, because a process started from this workspace cannot write to the
            // profile directory, and trimmed rather than left to grow: a fault that repeats
            // on a timer must not fill a disk.
            var crashPath = Path.Combine(workspace, "crash.log");
            Environment.SetEnvironmentVariable("BALANCEPET_CRASH_LOG", crashPath);
            try
            {
                CrashLog.Write("自测", new InvalidOperationException("连不上想象中的服务器"));
                var written = File.ReadAllText(crashPath);
                Check("崩溃会留下可读的记录",
                    written.Contains("自测", StringComparison.Ordinal)
                    && written.Contains("连不上想象中的服务器", StringComparison.Ordinal)
                    && written.Contains(DateTime.Now.ToString("yyyy-MM-dd"), StringComparison.Ordinal),
                    written.Length > 120 ? written[..120] : written);

                File.WriteAllText(crashPath, new string('x', 600 * 1024));
                CrashLog.Write("自测", new InvalidOperationException("第二次"));
                Check("记录过大时重来而不是无限增长",
                    new FileInfo(crashPath).Length < 512 * 1024,
                    $"{new FileInfo(crashPath).Length / 1024} KB");
            }
            finally
            {
                Environment.SetEnvironmentVariable("BALANCEPET_CRASH_LOG", null);
            }

            // --- 9. A download cut short resumes instead of starting over -------
            // Restarting an 86 MB transfer on a connection that drops is what makes it
            // never finish, so the partial file has to survive and the next request has
            // to ask only for the rest. These use a stub that really does cut the first
            // response in half, because the interesting part is what lands on disk.
            //
            // The download lands wherever Path.GetTempPath() points, and the test has to
            // redirect that for the same reason the scratch directories above are not
            // under %TEMP%: started from this workspace the process runs at low integrity,
            // and a low-integrity process is refused when it writes to a medium-integrity
            // location. Pointing the variable at the test's own scratch directory keeps
            // the assertion about resuming rather than about where the file may be
            // written, and it leaves the caller's %TEMP% alone.
            Environment.SetEnvironmentVariable("TEMP", workspace);
            Environment.SetEnvironmentVariable("TMP", workspace);
            var updateDownloads = Path.Combine(workspace, "update-downloads");

            var payload = new byte[200_000];
            Random.Shared.NextBytes(payload);
            var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

            async Task<(bool Ok, byte[] Bytes, List<long> From)> Download(RangeServer server)
            {
                // A directory of the test's own: the downloader keeps a partial file now, so
                // a test that let it default would write into the real installation's
                // downloads folder and could resume from whatever was there.
                var service = new UpdateService(new HttpClient(new StubHandler(server.Respond)), updateDownloads);
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

            // Whenever a domestic mirror is configured it is asked for first, and GitHub is
            // the fallback rather than the other way round: the mirror exists because
            // GitHub is the slow path here, and the two name the same bytes, so a download
            // can change hosts part way through and still be checked once at the end.
            //
            // The empty base is passed explicitly rather than relying on Base being unset:
            // Base is filled in once the mirror exists, and a test that reads it would
            // change meaning on the day the mirror is published.
            var noMirror = DownloadMirror.Candidates(
                new Uri("https://github.com/GoldenMoon-cell/BalancePet/releases/download/v1.5.0/BalancePet-1.5.0-win-x64.zip"),
                "v1.5.0", "");
            Check("没配置镜像时只有 GitHub 一个地址",
                noMirror.Count == 1 && noMirror[0].Host == "github.com",
                string.Join(",", noMirror.Select(uri => uri.Host)));

            // The shape the mirror has to have, and the order: the mirror is asked for
            // first because it exists precisely because GitHub is the slow path here.
            var mirrorUrls = DownloadMirror.Candidates(
                new Uri("https://github.com/GoldenMoon-cell/BalancePet/releases/download/v1.5.0/BalancePet-1.5.0-win-x64.zip"),
                "v1.5.0", "https://gitee.com/example/balancepet/releases/download");
            Check("配好镜像后镜像在前、GitHub 在后",
                mirrorUrls.Count == 2 && mirrorUrls[0].Host == "gitee.com" && mirrorUrls[1].Host == "github.com",
                string.Join(",", mirrorUrls.Select(uri => uri.Host)));
            Check("镜像地址保留了标签与文件名",
                mirrorUrls[0].AbsoluteUri == "https://gitee.com/example/balancepet/releases/download/v1.5.0/BalancePet-1.5.0-win-x64.zip",
                mirrorUrls[0].AbsoluteUri);
            Check("标签里的特殊字符会被转义",
                DownloadMirror.Candidates(new Uri("https://github.com/o/r/releases/download/v1.0.0/x.zip"), "v1.0.0+build/2", "https://gitee.com/example/m/releases/download")[0]
                    .AbsoluteUri.EndsWith("/v1.0.0%2Bbuild%2F2/x.zip", StringComparison.Ordinal),
                DownloadMirror.Candidates(new Uri("https://github.com/o/r/releases/download/v1.0.0/x.zip"), "v1.0.0+build/2", "https://gitee.com/example/m/releases/download")[0].AbsoluteUri);

            // Both addresses missing is the case a user actually sees when a release has not
            // been mirrored yet: a readable failure, after the mirror has been asked and
            // before GitHub has. The order matters for the report as much as for the fetch.
            var official = new Uri("https://github.com/GoldenMoon-cell/BalancePet/releases/download/v1.5.0/BalancePet-1.5.0-win-x64.zip");
            var askedHosts = new List<string>();
            using (var mirrorHttp = new HttpClient(new StubHandler(request =>
            {
                askedHosts.Add(request.RequestUri!.Host);
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            })))
            {
                var service = new UpdateService(mirrorHttp, updateDownloads);
                var asset = new UpdateAsset(UpdateAssetKind.PortableArchive, "x.zip", official, digest, "v1.5.0");
                var missingMessage = "";
                try { await service.DownloadAsync(asset); }
                catch (HttpRequestException error) { missingMessage = error.Message; }
                var expected = DownloadMirror.Configured ? 2 : 1;
                Check("两个地址都 404 时报告可读的失败",
                    missingMessage.Length > 0 && askedHosts.Count == expected,
                    $"{missingMessage} / {string.Join(",", askedHosts)}");
            }

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
            // The expected set is derived from the artwork rather than pinned to a
            // number. A hard-coded count fails every time an appearance is added, and
            // the fix is always to bump it -- which is exactly how a catalog that
            // silently *lost* an entry gets waved through by the same edit that adds
            // one. Comparing the two sets says which appearance is missing instead.
            var publishable = Directory.EnumerateDirectories(realPets)
                .Where(directory => !Path.GetFileName(directory).StartsWith('_'))
                .Where(directory => RequiredStates.All(state => File.Exists(Path.Combine(directory, state))))
                .Select(directory => $"pet.{Path.GetFileName(directory)}")
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            var catalogued = appearances.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var notPublished = publishable.Except(catalogued, StringComparer.Ordinal).ToArray();
            var notOnDisk = catalogued.Except(publishable, StringComparer.Ordinal).ToArray();
            Check($"形象目录覆盖全部 {publishable.Length} 套有素材的形象",
                notPublished.Length == 0 && notOnDisk.Length == 0,
                $"未收录 {string.Join(",", notPublished)}；多余 {string.Join(",", notOnDisk)}");
            Check("形象条目都是 pet 类型", appearances.All(item => item.Type == "pet"));            Check("形象条目可安装", appearances.All(item => item.Id.StartsWith("pet.", StringComparison.Ordinal)
                && item.Sha256.Length == 64 && item.DownloadUrl.EndsWith(".zip", StringComparison.Ordinal)));

            // A release drops every appearance folder except the placeholder, and the only
            // way an appearance reaches a user is being packed out of the folder it was
            // dropped from. So a folder that the catalog does not know how to extract is
            // artwork that disappears for everyone: it is not in the installer, and there
            // is no package of it either. This is the direction the old hand-written
            // exclusion list got wrong -- the list named fourteen folders, a fifteenth
            // appearance arrived, and its artwork shipped inside the installer instead of
            // being left out. Its replacement is a rule that cannot go stale, which makes
            // this the half that still needs saying out loud.
            var folders = Directory.EnumerateDirectories(realPets)
                .Where(directory => !Path.GetFileName(directory).StartsWith('_'))
                .Select(directory => Path.GetFileName(directory))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var extractable = PetStyleCatalog.Extractable
                .Select(style => style.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unextractable = folders.Where(name => !extractable.Contains(name)).ToArray();
            Check($"有素材的 {folders.Length} 套形象都能被发布构建打包出来",
                unextractable.Length == 0,
                $"发布构建会丢掉：{string.Join(",", unextractable)}（未列入 PetStyleCatalog.Extractable）");

            // Every entry in the file has to survive parsing. This used to name a number, which says
            // nothing about the parser and goes stale the moment a plugin is added or retired: it read
            // four while the file held three and reported that as a failure. What matters is that none
            // of them is silently dropped on the way in -- the same thing the checks above ask of the
            // appearance folder, and for the same reason.
            var declared = System.Text.Json.JsonDocument.Parse(pluginJson)
                .RootElement.GetProperty("plugins").GetArrayLength();
            var plugins = PluginCatalogService.Parse(pluginJson);
            Check($"目录里的 {declared} 条插件都能解析出来", plugins.Count == declared, $"实际 {plugins.Count}");

            // An appearance nobody here has published, shaped exactly like the ones that
            // are: this is the contract a third party reads, so it is worth knowing that
            // the host accepts it rather than only that it accepts its own file.
            var foreign = PluginCatalogService.ParseAppearances("""
                {"catalog":"balancepet.appearances","schema_version":1,"appearances":[
                  {"id":"pet.zz-probe","type":"pet","name":"取图探针","name_en":"Icon probe",
                   "description":"临时条目。","version":"1.0.0","min_core_version":"0.5.0",
                   "download_url":"https://github.com/o/r/releases/download/skins-1.0.0/pet.opencode-1.0.0.zip",
                   "sha256":"0000000000000000000000000000000000000000000000000000000000000000",
                   "icon_url":"https://raw.githubusercontent.com/o/r/main/previews/opencode.png",
                   "repository_url":"https://github.com/o/r",
                   "release_url":"https://github.com/o/r/releases/tag/skins-1.0.0",
                   "categories":["appearance"]}]}
                """);
            Check("第三方形象条目被接受", foreign.Count == 1, $"实际 {foreign.Count} 条");

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
                PetLineCatalog.Resolve("no-such-appearance", "inactive").Select(line => line.Label).SequenceEqual(neutralInactive));
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
                    if (root.GetProperty("schema_version").GetInt32() != PetLineCatalog.CurrentSchemaVersion) problems.Add($"{style}：没有使用当前双语台词 schema");

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

            var englishProblems = new List<string>();
            foreach (var styleId in publishable)
            {
                var style = styleId["pet.".Length..];
                var path = Path.Combine(appRoot, "assets", "pets", style, "lines.json");
                if (!File.Exists(path)) { englishProblems.Add($"{style}：没有 lines.json"); continue; }
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    foreach (var category in new[] { "inactive", "bubble", "streak" })
                    {
                        if (!document.RootElement.TryGetProperty(category, out var entries)) continue;
                        CheckEnglishEntries(entries, $"{style}.{category}", englishProblems);
                    }
                    if (document.RootElement.TryGetProperty("touch", out var touch))
                        foreach (var zone in touch.EnumerateObject())
                            CheckEnglishEntries(zone.Value, $"{style}.touch.{zone.Name}", englishProblems);
                }
                catch (JsonException error) { englishProblems.Add($"{style}：JSON 解析失败 {error.Message}"); }
            }
            Check($"全部 {publishable.Length} 套形象的彩蛋都有完整英文版本", englishProblems.Count == 0, string.Join("；", englishProblems.Take(8)));

            var englishPlaceholder = PetLineCatalog.Resolve("_placeholder", "bubble")
                .Select(line => line.ForLanguage("en-US")).ToArray();
            Check("英文界面采用双语台词的英文版本",
                englishPlaceholder.Length > 0 && englishPlaceholder.All(line => !ContainsCjk(line.Label) && !ContainsCjk(line.Amount) && !ContainsCjk(line.Hint)),
                string.Join(" / ", englishPlaceholder.Select(line => $"{line.Label} | {line.Amount} | {line.Hint}")));
            Check("中文界面仍使用原始中文台词",
                PetLineCatalog.Resolve("_placeholder", "bubble").FirstOrDefault()?.Label.Contains("占位") == true);

            Check("澄芽说的是自己的话", ReadLabels(appRoot, "seedance").Any(label => label.Contains("澄芽")));
            Check("橙析说的是自己的话", ReadLabels(appRoot, "mimo").Any(label => label.Contains("橙析")));
            // The placeholder is what a fresh offline install shows, so it says what it is
            // rather than borrowing a character's voice.
            Check("占位形象说的是占位的话", ReadLabels(appRoot, "_placeholder").Any(label => label.Contains("占位")));

            // A file the program cannot parse must fall back rather than fail: a package
            // authored against a future schema still has to draw.
            Check("未知 schema 回落到中性文案", PetLineCatalog.IsReadable("""{"schema_version":99,"inactive":[]}""") == false);
            Check("空文件回落到中性文案", PetLineCatalog.IsReadable("") == false);
            Check("旧版单语 lines schema 仍可读", PetLineCatalog.IsReadable("""{"schema_version":1,"inactive":[]}""") == true);
            Check("正常文件可读", PetLineCatalog.IsReadable(File.ReadAllText(Path.Combine(appRoot, "assets", "pets", "chatgpt", "lines.json"))));

            // --- 12. The served lines layer ------------------------------------
            // The lines are also published from the appearance repository, because a package
            // is mostly artwork: correcting one word used to mean a new package version and a
            // download of megabytes per appearance. Measured across the published set, giving
            // every appearance its lines cost 147 MB of transfer for 36 KB of text.
            //
            // A synthetic document rather than the published one. The published copy is
            // generated from these very package files, so it cannot tell "the served lines
            // were used" apart from "the package's were" -- a label that appears nowhere else
            // can. This runs last because publishing replaces what every later lookup sees.
            const string marker = """{"schema_version":1,"lines":{"chatgpt":{"bubble":[{"label":"在线文案生效","amount":"ok","hint":"h"}]},"_placeholder":{"bubble":[{"label":"旧版占位在线","amount":"ok","hint":"h"}]}}}""";
            Check("在线文案优先于包内文案",
                PetLineCatalog.PublishRemote(marker)
                && PetLineCatalog.Resolve("chatgpt", "bubble").Select(line => line.Label).SingleOrDefault() == "在线文案生效",
                string.Join(" / ", PetLineCatalog.Resolve("chatgpt", "bubble").Select(line => line.Label)));
            // Refusing the document has to leave the previous one in force. Clearing it would
            // turn an unrecognised schema into characters losing their voices.
            Check("无法识别的在线文档不会被采用",
                PetLineCatalog.PublishRemote("""{"schema_version":99,"lines":{}}""") == false
                && PetLineCatalog.PublishRemote("{ not json") == false
                && PetLineCatalog.PublishRemote("") == false
                && PetLineCatalog.Resolve("chatgpt", "bubble").Select(line => line.Label).SingleOrDefault() == "在线文案生效");
            var legacyFallback = PetLineCatalog.Resolve("_placeholder", "bubble").FirstOrDefault()?.ForLanguage("en-US").Label;
            Check("旧版在线文案缺少英文时回退到包内文案",
                legacyFallback == "Oh, friend—there you are", legacyFallback ?? "<none>");

            var servedJson = File.ReadAllText(Path.Combine(repoRoot, "skins", "lines.json"));
            using (var servedDocument = JsonDocument.Parse(servedJson))
            {
                var servedStyles = servedDocument.RootElement.GetProperty("lines").EnumerateObject()
                    .Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                // The placeholder is excluded deliberately: it ships inside the program and is
                // not published as an appearance, so its file is always there anyway.
                var expectedServed = publishable.Select(id => id["pet.".Length..]).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                var absent = expectedServed.Except(servedStyles, StringComparer.Ordinal).ToArray();
                Check($"在线文案覆盖全部 {expectedServed.Length} 套已发布形象",
                    absent.Length == 0 && servedStyles.Length == expectedServed.Length,
                    $"文档 {servedStyles.Length} 套；缺 {string.Join(",", absent)}");
            }
            Check("在线文案可被采用", PetLineCatalog.PublishRemote(servedJson));
            // And with the real document in force, every appearance still answers from it.
            Check("在线文案生效后形象仍说自己的话",
                PetLineCatalog.Resolve("chatgpt", "bubble").Select(line => line.Label).Any(label => label.Contains("霁珑")),
                string.Join(" / ", PetLineCatalog.Resolve("chatgpt", "bubble").Select(line => line.Label)));
            Check("在线目录的英文版本可被采用",
                PetLineCatalog.Resolve("chatgpt", "bubble").Any(line =>
                    !string.IsNullOrWhiteSpace(line.EnglishLabel)
                    && line.ForLanguage("en-US").Label == line.EnglishLabel
                    && line.ForLanguage("en-US").Amount == line.EnglishAmount
                    && line.ForLanguage("en-US").Hint == line.EnglishHint));

            // --- 13. Changelog notices -----------------------------------------
            // A notice is about something that changed outside a release. The watermark is
            // what keeps it from being announced twice, and from a fresh installation
            // announcing everything ever published -- so the watermark is the part worth
            // pinning down.
            const string notices = """
                {"schema_version":1,"notices":[
                  {"seq":1,"date":"2026-10-01","area":"文档","title":"第一条","url":"https://example.com/1"},
                  {"seq":3,"date":"2026-10-02","area":"规范","title":"第三条","summary":"摘要","url":"https://example.com/3"},
                  {"seq":2,"date":"2026-10-02","area":"在线内容","title":"第二条","url":"https://example.com/2"}
                ]}
                """;
            Check("通告文档可读", NoticeFeed.Publish(notices) && NoticeFeed.All.Count == 3, $"实际 {NoticeFeed.All.Count}");
            Check("通告按序号从新到旧", NoticeFeed.All.Select(item => item.Seq).SequenceEqual(new[] { 3, 2, 1 }),
                string.Join(",", NoticeFeed.All.Select(item => item.Seq)));
            Check("水位之内不再重复提示",
                NoticeFeed.NewerThan(2).Select(item => item.Seq).SequenceEqual(new[] { 3 })
                && NoticeFeed.NewerThan(3).Count == 0,
                string.Join(",", NoticeFeed.NewerThan(2).Select(item => item.Seq)));
            // A fresh installation records this without showing anything, which is why it has
            // to be the highest published number rather than the newest it happened to read.
            Check("最高序号即首次运行的静默水位", NoticeFeed.NewestSeq == 3, $"实际 {NoticeFeed.NewestSeq}");

            // An entry the program cannot act on is dropped on its own; the rest of the
            // changelog still arrives. One publisher's typo must not hide every other note.
            const string partlyBroken = """
                {"schema_version":1,"notices":[
                  {"seq":5,"date":"2026-10-02","area":"文档","title":"好的","url":"https://example.com/ok"},
                  {"seq":4,"date":"2026-10-02","area":"文档","title":"外链不是 https","url":"http://example.com/insecure"},
                  {"seq":4,"date":"2026-10-02","area":"文档","title":"重复序号","url":"https://example.com/dup"},
                  {"seq":0,"date":"2026-10-02","area":"文档","title":"序号非正","url":"https://example.com/zero"},
                  {"seq":6,"date":"2026-10-02","area":"文档","url":"https://example.com/notitle"}
                ]}
                """;
            Check("无法打开的链接被单独丢弃",
                NoticeFeed.Publish(partlyBroken) && NoticeFeed.All.Count == 1 && NoticeFeed.All[0].Seq == 5,
                $"{NoticeFeed.All.Count} 条：{string.Join(",", NoticeFeed.All.Select(item => item.Seq))}");

            // An unrecognised document replaces nothing: the previous notes stay, so the
            // window shows something older rather than emptying itself.
            Check("无法识别的通告文档不会清空记录",
                NoticeFeed.Publish("""{"schema_version":99,"notices":[]}""") == false
                && NoticeFeed.Publish("{ not json") == false
                && NoticeFeed.Publish("") == false
                && NoticeFeed.All.Count == 1 && NoticeFeed.All[0].Seq == 5);

            var publishedNotices = File.ReadAllText(Path.Combine(repoRoot, "notices.json"));
            Check("随仓库发布的通告文档可读", NoticeFeed.Publish(publishedNotices) && NoticeFeed.All.Count > 0,
                $"{NoticeFeed.All.Count} 条");
            // The watermark only moves forward, so a sequence that went backwards would make
            // the next note compare as already read.
            var sequences = NoticeFeed.All.Select(item => item.Seq).ToArray();
            Check("通告序号严格递增且不重复",
                sequences.Distinct().Count() == sequences.Length && sequences.OrderByDescending(value => value).SequenceEqual(sequences),
                string.Join(",", sequences));

            // The digest is what makes resuming safe: bytes appended from a source that
            // answered the wrong range would otherwise be installed as an update. Its own
            // package name, because a file that is complete answers a range request with
            // 416 and never reaches the digest at all.
            var corrupted = "";
            try { await Download(new RangeServer(payload, dropAfter: 50_000) { CorruptResume = true }); }
            catch (Exception error) { corrupted = $"{error.GetType().Name}: {error.Message}"; }
            Check("续传拼错的文件被校验拦下", corrupted.Contains("校验失败", StringComparison.Ordinal), corrupted);

            // --- 14. The pictures the catalog points at --------------------------
            // The list has to draw an appearance nobody has downloaded, so a catalog
            // entry may name a picture and the program fetches it. Nothing here is
            // allowed to be load-bearing: a picture that never arrives leaves a row
            // that still installs, which is why a failure is a null rather than an
            // exception the caller has to remember to catch.
            var iconWorkspace = Path.Combine(workspace, "icons");
            var iconBytes = File.ReadAllBytes(Path.Combine(realPets, "_placeholder", "idle.png"));

            PluginCatalogRecord IconRecord(string id, string version, string url)
                => new() { Id = id, Type = "pet", Name = id, Version = version, IconUrl = url };

            var iconUrl = "https://raw.githubusercontent.com/GoldenMoon-cell/BalancePet/main/previews/ok.png";
            var iconRecord = IconRecord("pet.ok", "1.0.0", iconUrl);

            // A host the catalog is not trusted to name is the one shape the validator
            // has to refuse, and it has to refuse it without losing the entry: an
            // extension whose thumbnail is malformed is still an extension.
            var iconKept = PluginCatalogService.Parse("""
                {"schema_version":1,"plugins":[
                  {"id":"balancepet.ext.feature.probe","type":"feature","name":"探针","version":"1.0.0",
                   "download_url":"https://github.com/o/r/releases/download/v1/p.zip","sha256":"0000000000000000000000000000000000000000000000000000000000000000",
                   "repository_url":"https://github.com/o/r","release_url":"https://github.com/o/r/releases/tag/v1",
                   "icon_url":"https://example.com/not-allowed.png"},
                  {"id":"balancepet.ext.feature.probe2","type":"feature","name":"探针二","version":"1.0.0",
                   "download_url":"https://github.com/o/r/releases/download/v1/p.zip","sha256":"0000000000000000000000000000000000000000000000000000000000000000",
                   "repository_url":"https://github.com/o/r","release_url":"https://github.com/o/r/releases/tag/v1",
                   "icon_url":"https://raw.githubusercontent.com/o/r/main/ok.png"}]}
                """);
            Check("来源不可信的图示被丢掉而不是丢掉条目",
                iconKept.Count == 2 && iconKept[0].IconUrl.Length == 0 && iconKept[1].IconUrl.EndsWith("ok.png", StringComparison.Ordinal),
                $"{iconKept.Count} 条 / '{iconKept[0].IconUrl}' / '{iconKept[1].IconUrl}'");

            var iconRequests = 0;
            var iconFetcher = new PluginIconService(iconWorkspace);
            var iconReloader = new PluginIconService(iconWorkspace);
            using (var iconClient = new HttpClient(new StubHandler(_ =>
            {
                iconRequests++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(iconBytes) };
            })))
            {
                var fetched = await iconFetcher.FetchAsync(iconClient, iconRecord);
                Check("图示能取回来并解码", fetched is not null, "取回为 null");
                Check("只请求一次", iconRequests == 1, $"实际 {iconRequests} 次");
                Check("内存里已缓存", iconFetcher.Cached(iconRecord) is not null);
                Check("落盘的文件名带版本", File.Exists(iconFetcher.CachePath(iconRecord)) && Path.GetFileName(iconFetcher.CachePath(iconRecord)) == "pet.ok-1.0.0.png",
                    Path.GetFileName(iconFetcher.CachePath(iconRecord)));

                await iconFetcher.FetchAsync(iconClient, iconRecord);
                Check("第二次不再请求", iconRequests == 1, $"实际 {iconRequests} 次");

                // A new instance is what the next launch is: it has the file and none of
                // the memory, which is the state the first paint has to be able to draw.
                iconReloader.LoadFromDisk(new[] { iconRecord });
                Check("下次启动直接读磁盘", iconReloader.Cached(iconRecord) is not null);

                // A different version is a different picture: a republished appearance
                // has redrawn art, and reusing the old file would keep showing the old
                // face after the update that changed it.
                var iconNext = IconRecord("pet.ok", "1.1.0", iconUrl);
                Check("换了版本就是另一张图", iconReloader.Cached(iconNext) is null, "沿用了旧版本的图示");
                Check("两个版本的缓存文件不互相覆盖", iconReloader.CachePath(iconNext) != iconReloader.CachePath(iconRecord));
            }

            using (var iconOffline = new HttpClient(new StubHandler(_ => throw new HttpRequestException("no network"))))
            {
                var iconUnavailable = new PluginIconService(iconWorkspace);
                var iconAnswer = await iconUnavailable.FetchAsync(iconOffline, IconRecord("pet.down", "1.0.0", iconUrl));
                Check("取不到时返回空而不是抛出", iconAnswer is null);
                Check("失败被记住，不逐行重试", iconUnavailable.HasFailed(IconRecord("pet.down", "1.0.0", iconUrl)));

                var iconJunk = new PluginIconService(iconWorkspace);
                using var iconNotAnImage = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("this is not a png"))
                }));
                var iconUndecodable = await iconJunk.FetchAsync(iconNotAnImage, IconRecord("pet.junk", "1.0.0", iconUrl));
                Check("不是图片的内容被拒绝", iconUndecodable is null);
            }

            // Every kind has a drawing, and an id nobody has heard of still gets one:
            // that is what makes this table not a list of which extensions exist.
            foreach (var iconKind in new[] { "pet", "feature", "theme", "browser" })
            {
                var kindDrawing = PluginIconCatalog.Resolve(iconKind, "");
                Check($"{iconKind} 有兜底图示", !kindDrawing.Stroked.IsEmpty());
            }
            var iconUnregistered = PluginIconCatalog.Resolve("feature", "balancepet.ext.feature.something-new");
            Check("没登记过的扩展也有图示", !iconUnregistered.Stroked.IsEmpty());
            var iconThemed = PluginIconCatalog.Resolve("theme", "balancepet.theme.mica");
            Check("主题图标额外有一块实心", iconThemed.Filled is not null, "缺少实心部分");
            Check("图示是冻结的，可安全共用", iconThemed.Stroked.IsFrozen && iconUnregistered.Stroked.IsFrozen);

            // --- 15. The mirror of a declared address ---------------------------
            // GitHub's raw host is refused on some networks while a public mirror of
            // the same repository answers, so every document is looked for at both. The
            // second address is derived rather than declared, which is what keeps a
            // catalog from aiming the program at a host of its choosing.
            Check("raw 地址能推出镜像",
                GitHubContentReader.MirrorOf("https://raw.githubusercontent.com/GoldenMoon-cell/BalancePet-Pets/main/previews/qwen.png")
                    == "https://cdn.jsdelivr.net/gh/GoldenMoon-cell/BalancePet-Pets@main/previews/qwen.png",
                GitHubContentReader.MirrorOf("https://raw.githubusercontent.com/GoldenMoon-cell/BalancePet-Pets/main/previews/qwen.png") ?? "null");
            Check("其它主机不推镜像",
                GitHubContentReader.MirrorOf("https://cdn.jsdelivr.net/gh/o/r@main/x.png") is null
                && GitHubContentReader.MirrorOf("https://example.com/x.png") is null
                && GitHubContentReader.MirrorOf("not a url") is null);
            Check("路径里带目录也能推",
                GitHubContentReader.MirrorOf("https://raw.githubusercontent.com/o/r/v1.2.3/a/b/c.png")
                    == "https://cdn.jsdelivr.net/gh/o/r@v1.2.3/a/b/c.png");
            Check("候选地址按顺序给出，镜像在后",
                GitHubContentReader.Candidates("https://raw.githubusercontent.com/o/r/main/a.png").Count == 2);
            Check("推不出镜像时只有原地址",
                GitHubContentReader.Candidates("https://example.com/a.png").Count == 1);

            // The changelog was the last document still going straight to GitHub, and the
            // one where going straight there shows: a note that needs a manual refresh is a
            // note nobody reads. It goes through the same reader as everything else now.
            var noticeCandidates = GitHubContentReader.Candidates(NoticeFeed.Url);
            var noticeHosts = noticeCandidates.Select(url => new Uri(url).Host).ToArray();
            Check("通告也走镜像",
                noticeCandidates.Count == 2
                && noticeHosts[0] == "raw.githubusercontent.com"
                && noticeHosts[1] == "cdn.jsdelivr.net"
                && new Uri(noticeCandidates[1]).AbsolutePath.EndsWith("/BalancePet@main/notices.json", StringComparison.Ordinal),
                string.Join(",", noticeHosts));

            // The first address failing is the whole point of the list, so the stub
            // refuses the raw host and answers the mirror.
            var mirrorRequests = new List<string>();
            using (var viaMirror = new HttpClient(new StubHandler(request =>
            {
                mirrorRequests.Add(request.RequestUri!.Host);
                if (request.RequestUri.Host.Contains("raw.githubusercontent", StringComparison.Ordinal))
                    throw new HttpRequestException("connection refused");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(iconBytes) };
            })))
            {
                var service = new PluginIconService(iconWorkspace);
                var throughMirror = await service.FetchAsync(viaMirror, IconRecord("pet.mirror", "1.0.0", iconUrl));
                Check("原地址被拒后走镜像取到图", throughMirror is not null && mirrorRequests.Count == 2,
                    $"{mirrorRequests.Count} 次：{string.Join(",", mirrorRequests)}");
            }

            // An address that never answers is the case this exists for — a host that
            // drops the connection instead of refusing it. The mirror has to be asked
            // without waiting for that address's whole timeout, or every fetch on such a
            // network costs one timeout before it can start.
            var hanging = new TaskCompletionSource<HttpResponseMessage>();
            var asked = new List<string>();
            using (var blackhole = new HttpClient(new AsyncStubHandler(request =>
            {
                lock (asked) asked.Add(request.RequestUri!.Host);
                if (request.RequestUri.Host.Contains("raw.githubusercontent", StringComparison.Ordinal))
                    return hanging.Task;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(iconBytes) });
            })))
            {
                var started = DateTime.UtcNow;
                var hedged = await GitHubContentReader.DownloadAsync(
                    blackhole, iconUrl, 1024 * 1024, "image/png", "BalancePet-SelfTest/1.0");
                var elapsed = DateTime.UtcNow - started;
                Check("原地址无响应时不等它超时，直接问镜像",
                    hedged.Mirrored && hedged.Bytes.SequenceEqual(iconBytes) && elapsed < TimeSpan.FromSeconds(5),
                    $"{elapsed.TotalMilliseconds:F0} ms，{asked.Count} 次请求");
                Check("镜像地址是推导出来的那一个",
                    hedged.Url == "https://cdn.jsdelivr.net/gh/GoldenMoon-cell/BalancePet@main/previews/ok.png",
                    hedged.Url);
            }
            hanging.SetCanceled();

            // --- 16. The four states one tile can be in ---------------------------
            // These are the states a machine with everything installed never shows, so
            // they are exactly the ones worth pinning down here. A row with no picture
            // anywhere is finished as it is; a row with one is waiting, and waiting has
            // to be told apart from failed, or a row that is still loading would wear
            // the mark that means it never will.
            static string States(PluginCatalogItemView view) => string.Join(",", new[]
            {
                view.IconGlyphVisibility == System.Windows.Visibility.Visible ? "glyph" : "",
                view.IconImageVisibility == System.Windows.Visibility.Visible ? "image" : "",
                view.IconBusyVisibility == System.Windows.Visibility.Visible ? "busy" : "",
                view.IconFallbackVisibility == System.Windows.Visibility.Visible ? "mark" : ""
            }.Where(value => value.Length > 0));

            var plain = new PluginCatalogItemView(
                new PluginCatalogRecord { Id = "balancepet.ext.feature.plain", Type = "feature", Version = "1.0.0" }, null, false);
            plain.SetIconBusy(true);
            Check("没有图源的行直接画图示", States(plain) == "glyph", States(plain));

            var waiting = new PluginCatalogItemView(
                new PluginCatalogRecord { Id = "pet.remote", Type = "pet", Version = "1.0.0", IconUrl = iconUrl }, null, false);
            waiting.SetIconBusy(true);
            Check("有图源的行先转圈", States(waiting) == "busy", States(waiting));
            waiting.SetIcon(null);
            Check("取不到之后才是程序标记", States(waiting) == "mark", States(waiting));

            var arrived = new PluginCatalogItemView(
                new PluginCatalogRecord { Id = "pet.here", Type = "pet", Version = "1.0.0" }, null, false, "qwen");
            Check("本机已安装的形象算有图源", arrived.HasPictureSource);
            arrived.SetIconBusy(true);
            Check("本机形象没有图源也能立刻画", States(arrived) != "glyph", States(arrived));
            arrived.SetIcon(iconFetcher.Cached(iconRecord));
            Check("拿到图之后只剩图", States(arrived) == "image", States(arrived));

            // --- 17. A package Windows will not move ------------------------------
            // Installing used to fail outright when Windows refused to move the staged
            // directory, which it does while anything still holds a handle inside it: a
            // virus scanner reading files written a moment ago. Measured by installing
            // one appearance over and over, fourteen attempts in sixty were refused, so
            // this is a user pressing Install, not a test artefact. A handle held open
            // on purpose reproduces the refusal on demand, which is what makes the
            // recovery testable rather than hoped for.
            var commitRoot = Path.Combine(workspace, "commit");
            var staged = Path.Combine(commitRoot, ".staging", "one");
            Directory.CreateDirectory(Path.Combine(staged, "assets", "pets", "qwen"));
            File.WriteAllText(Path.Combine(staged, "manifest.json"), "{}");
            var stagedImage = Path.Combine(staged, "assets", "pets", "qwen", "idle.png");
            File.WriteAllBytes(stagedImage, iconBytes);

            var committed = Path.Combine(commitRoot, "pet.probe", "1.0.0");
            Directory.CreateDirectory(Path.GetDirectoryName(committed)!);
            using (var held = new FileStream(stagedImage, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                StagedPackage.Commit(staged, committed);
            }
            Check("占用导致移动被拒时，改用复制装好",
                File.Exists(Path.Combine(committed, "manifest.json"))
                && File.ReadAllBytes(Path.Combine(committed, "assets", "pets", "qwen", "idle.png")).SequenceEqual(iconBytes),
                string.Join(",", Directory.Exists(committed) ? Directory.GetFiles(committed, "*", SearchOption.AllDirectories).Select(Path.GetFileName) : Array.Empty<string>()));

            // The version directory is what "installed" means, so a failed commit must not
            // leave one behind for the next launch to find.
            var refusedRoot = Path.Combine(commitRoot, "pet.refused", "1.0.0");
            Directory.CreateDirectory(Path.GetDirectoryName(refusedRoot)!);
            var blocked = Path.Combine(commitRoot, ".staging", "two");
            Directory.CreateDirectory(blocked);
            File.WriteAllText(Path.Combine(blocked, "manifest.json"), "{}");
            var failed = "";
            using (var held = new FileStream(Path.Combine(blocked, "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { StagedPackage.Commit(blocked, refusedRoot); }
                catch (IOException error) { failed = error.Message; }
                catch (UnauthorizedAccessException error) { failed = error.Message; }
            }
            Check("彻底失败时不留半个版本目录", failed.Length > 0 && !Directory.Exists(refusedRoot), failed);

            // --- 18. Installing an appearance survives a dropped connection ---------
            // An appearance package is twelve to seventeen megabytes, and this network
            // cuts a long transfer whenever it likes: measured on the real asset, one
            // attempt in four completed over HTTP/1.1 and two in four over HTTP/2, every
            // failure about three seconds in. The updater used to keep its partial file
            // and ask for the rest while the extension installer started over, which on
            // such a link is the difference between a slow install and one that never
            // finishes. Same stub as the updater's resume test, because it is the same
            // machinery now.
            var packageRoot = Path.Combine(workspace, "extension-resume");
            Directory.CreateDirectory(packageRoot);
            var skin = new byte[180_000];
            Random.Shared.NextBytes(skin);
            var skinDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(skin)).ToLowerInvariant();
            var skinServer = new RangeServer(skin, dropAfter: 60_000);
            using (var resumeHttp = new HttpClient(new StubHandler(skinServer.Respond)))
            {
                var downloads = new ExtensionUpdateService(resumeHttp, packageRoot);
                var release = new ExtensionUpdateRelease
                {
                    Id = "pet.qwen",
                    Type = "pet",
                    Version = "1.2.0",
                    PackageName = "pet.qwen-1.2.0.zip",
                    DownloadUrl = "https://github.com/GoldenMoon-cell/BalancePet-Pets/releases/download/skins-1.2.0/pet.qwen-1.2.0.zip",
                    Digest = skinDigest
                };

                var downloaded = await downloads.DownloadAsync(release);
                var bytes = await File.ReadAllBytesAsync(downloaded);

                Check("断流的形象包能续传下完", bytes.Length == skin.Length && bytes.SequenceEqual(skin),
                    $"{bytes.Length} / {skin.Length} 字节");
                Check("续传确实只取了剩余部分",
                    skinServer.RequestedFrom.Count == 2 && skinServer.RequestedFrom[1] == 60_000,
                    string.Join(",", skinServer.RequestedFrom));

                // The name is the whole reason a second attempt can continue the first: a
                // fresh name per call is a file that is never resumed.
                Check("落点路径按包名固定", Path.GetFileName(downloaded) == "pet.qwen-1.2.0.zip" && downloaded.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase),
                    downloaded);

                // An abandoned attempt has to leave its bytes behind, or "try again"
                // means "start over" — and on a link that cuts a transfer every few
                // seconds, starting over is how a download never finishes.
                // A server that drops every connection still lets a resuming client finish,
                // which is the point — so this one hands over less each time than the file
                // needs in total: twenty kilobytes an attempt against a hundred and eighty,
                // and six attempts, leaves the download unfinished with bytes on disk.
                var alwaysDrops = new ExtensionUpdateService(
                    new HttpClient(new StubHandler(new RangeServer(skin, dropAfter: 20_000) { DropEvery = true }.Respond)), packageRoot);
                var interruptedRelease = new ExtensionUpdateRelease
                {
                    Id = "pet.interrupted",
                    Type = "pet",
                    Version = "1.0.0",
                    PackageName = "pet.interrupted-1.0.0.zip",
                    DownloadUrl = "https://github.com/GoldenMoon-cell/BalancePet-Pets/releases/download/skins-1.0.0/pet.interrupted-1.0.0.zip",
                    Digest = skinDigest
                };
                var gaveUp = "";
                try { await alwaysDrops.DownloadAsync(interruptedRelease); }
                catch (Exception error) { gaveUp = error.Message; }
                var interruptedPath = Path.Combine(packageRoot, "pet.interrupted-1.0.0.zip");
                Check("下载彻底失败时留下已收到的字节",
                    gaveUp.Length > 0 && File.Exists(interruptedPath) && new FileInfo(interruptedPath).Length > 0,
                    $"{gaveUp} / {(File.Exists(interruptedPath) ? new FileInfo(interruptedPath).Length : 0)} 字节");

                // Wrong bytes are the one thing that must not be kept: they are not a
                // partial download of this package, so resuming from them fails the digest
                // for ever and leaves the user no way out. Its own package name, because a
                // file that is already complete answers a range request with 416 and never
                // reaches the digest at all.
                var corruptRelease = new ExtensionUpdateRelease
                {
                    Id = "pet.corrupt",
                    Type = "pet",
                    Version = "1.0.0",
                    PackageName = "pet.corrupt-1.0.0.zip",
                    DownloadUrl = "https://github.com/GoldenMoon-cell/BalancePet-Pets/releases/download/skins-1.0.0/pet.corrupt-1.0.0.zip",
                    Digest = skinDigest
                };
                var wrong = new ExtensionUpdateService(new HttpClient(new StubHandler(
                    new RangeServer(skin, dropAfter: 60_000) { CorruptResume = true }.Respond)), packageRoot);
                var skinRefused = "";
                try { await wrong.DownloadAsync(corruptRelease); }
                catch (InvalidDataException error) { skinRefused = error.Message; }
                Check("续传拼错的形象包被校验拦下", skinRefused.Contains("校验失败", StringComparison.Ordinal), skinRefused);
                Check("校验失败的包被删掉，不会一直卡在同一个文件上",
                    !File.Exists(Path.Combine(packageRoot, "pet.corrupt-1.0.0.zip")));
                File.Delete(downloaded);

                // A download directory that only grows is a download directory that fills a
                // disk: a version that was superseded halfway through would sit there for
                // the life of the installation.
                var stale = Path.Combine(packageRoot, "pet.old-0.9.0.zip");
                File.WriteAllBytes(stale, new byte[1024]);
                File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-3));
                var fresh = Path.Combine(packageRoot, "pet.recent-1.0.0.zip");
                File.WriteAllBytes(fresh, new byte[1024]);
                await downloads.DownloadAsync(release);
                Check("昨天的半成品会被清掉，今天的留着",
                    !File.Exists(stale) && File.Exists(fresh),
                    $"stale={File.Exists(stale)} fresh={File.Exists(fresh)}");
            }

            // --- 19. A mirror that cannot resume, and a host that can --------------
            // Measured against the real mirror: its CDN answers a range request with the
            // whole file, so a dropped transfer there starts again from nothing. Six
            // attempts would be six copies of the same seventy megabytes over a link that
            // drops, which is worse than not having a mirror at all. What has to happen:
            // the mirror is tried, gives up quickly once it is known to ignore ranges, and
            // the bytes it did deliver are handed to GitHub, which continues them — because
            // both addresses serve the same file and the digest decides at the end.
            var mirrored = new byte[500_000];
            Random.Shared.NextBytes(mirrored);
            var mirroredDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(mirrored)).ToLowerInvariant();
            var mirrorServer = new RangeServer(mirrored, dropAfter: 200_000) { IgnoreRange = true, DropEvery = true };
            var githubServer = new RangeServer(mirrored, dropAfter: 200_000);
            var mirrorHosts = new List<string>();
            var mirrorDownloads = Path.Combine(workspace, "mirror-downloads");
            Directory.CreateDirectory(mirrorDownloads);

            using (var mixedHttp = new HttpClient(new StubHandler(request =>
            {
                var host = request.RequestUri!.Host;
                lock (mirrorHosts) mirrorHosts.Add(host);
                return host.Contains("gitee", StringComparison.OrdinalIgnoreCase)
                    ? mirrorServer.Respond(request)
                    : githubServer.Respond(request);
            })))
            {
                var service = new UpdateService(mixedHttp, mirrorDownloads);
                var asset = new UpdateAsset(
                    UpdateAssetKind.PortableArchive,
                    "mirrored.zip",
                    new Uri("https://github.com/GoldenMoon-cell/BalancePet/releases/download/v9.9.9/mirrored.zip"),
                    mirroredDigest,
                    "v9.9.9");

                // Only meaningful when a mirror is configured; without one the candidates
                // are GitHub alone and this is the resume test again.
                var hadMirror = DownloadMirror.Candidates(asset.DownloadUri, asset.Tag).Count > 1;
                var path = await service.DownloadAsync(asset);
                var throughMirror = await File.ReadAllBytesAsync(path);
                Check("镜像断流后由 GitHub 续完", throughMirror.SequenceEqual(mirrored),
                    $"{throughMirror.Length} / {mirrored.Length} 字节");
                if (hadMirror)
                {
                    Check("先问镜像，再问 GitHub",
                        mirrorHosts.Count > 0 && mirrorHosts[0].Contains("gitee", StringComparison.OrdinalIgnoreCase)
                        && mirrorHosts[^1].Contains("github", StringComparison.OrdinalIgnoreCase),
                        string.Join(",", mirrorHosts));
                    Check("镜像已收到的字节被 GitHub 接着写，而不是从头再来",
                        githubServer.RequestedFrom.Count > 0 && githubServer.RequestedFrom[0] == 200_000,
                        $"GitHub 第一段从 {string.Join(",", githubServer.RequestedFrom)} 开始");
                    Check("镜像被问的次数很少（它不支持续传，多问就是多重下整包）",
                        mirrorHosts.Count(host => host.Contains("gitee", StringComparison.OrdinalIgnoreCase)) <= 2,
                        $"{mirrorHosts.Count(host => host.Contains("gitee", StringComparison.OrdinalIgnoreCase))} 次");
                }
            }
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch (IOException) { }
        }

        Console.WriteLine(_failures == 0 ? "\nALL CHECKS PASSED" : $"\n{_failures} CHECK(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static void CheckEnglishEntries(JsonElement entries, string path, List<string> problems)
    {
        var index = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("en", out var english) || english.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{path}[{index}]：缺少 en");
                index++;
                continue;
            }
            foreach (var field in new[] { "label", "amount", "hint" })
            {
                if (!english.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    problems.Add($"{path}[{index}]：英文 {field} 为空");
                else if (ContainsCjk(value.GetString()!))
                    problems.Add($"{path}[{index}]：英文 {field} 含有中文");
            }
            index++;
        }
    }

    private static bool ContainsCjk(string value) => value.Any(character => character is >= '\u3400' and <= '\u9fff');

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
    /// The same, for a test that has to answer later — or never. A host that drops
    /// connections rather than refusing them cannot be reproduced by a handler that
    /// always returns immediately.
    /// </summary>
    private sealed class AsyncStubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
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

        /// <summary>
        /// Drops every connection rather than only the first, so a client that retries and
        /// resumes still cannot finish. Needed to test what happens when a download gives
        /// up: a server that drops only the first response is a server that succeeds on the
        /// second, which is the resume case and not this one.
        /// </summary>
        public bool DropEvery { get; init; }

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
                Content = new DripContent(body, DropEvery || first ? dropAfter : int.MaxValue)
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
