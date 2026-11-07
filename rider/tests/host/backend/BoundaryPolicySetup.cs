using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using AnotherFSM.Rider;
using AnotherFSM.Semantics;
using JetBrains.Application.Parts;
using JetBrains.Application.Settings;
using JetBrains.Application.Threading;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.ExternalSources.MetadataViewer;
using JetBrains.ReSharper.ExternalSources.ReSharperIntegration;
using JetBrains.ReSharper.Feature.Services.ExternalSource;
using JetBrains.ReSharper.Feature.Services.ExternalSources.Core;
using JetBrains.ReSharper.Feature.Services.ExternalSources.Core.Impl;
using JetBrains.ReSharper.Feature.Services.ExternalSources.Pdb;
using JetBrains.ReSharper.Feature.Services.Navigation;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Psi.Modules;
using JetBrains.ReSharper.Resources.Shell;

namespace AnotherFSM.Acceptance;

/// <summary>Explicit test setup ONLY. No command => no writes, no legal prompt.
/// Main must first capture the exact known sandbox settings layer while closed and restore it
/// AFTER closing the sandbox. There is intentionally no misleading "restore default values" command.
/// Production and the separate observer below never call Apply. No arbitrary paths/settings/code.
/// </summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class BoundaryPolicySetup
{
    private readonly HashSet<string> consumed = new();
    private Timer? timer;
    private int queued;
    public BoundaryPolicySetup(Lifetime lifetime, ISolution solution, ScriptDocuments scripts)
    {
        var directory = Environment.GetEnvironmentVariable("ANOTHERFSM_ACCEPTANCE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        var started = DateTime.UtcNow;
        timer = new Timer(tick => {
            if (!lifetime.IsAlive || DateTime.UtcNow - started > TimeSpan.FromMinutes(25)) { timer?.Dispose(); return; }
            if (Interlocked.Exchange(ref queued, 1) != 0) return;
            solution.Locks.QueueReadLock(lifetime, "AnotherFSM.SourceBoundary.ExplicitSetup", () => {
                string? runId = null, commandId = null;
                try
                {
                    var path = Path.Combine(directory, "source-policy-command.json");
                    if (!File.Exists(path)) return;
                    using var document = JsonDocument.Parse(File.ReadAllText(path));var command = document.RootElement;
                    commandId = command.GetProperty("commandId").GetString();
                    if (!Guid.TryParse(commandId, out var parsedCommand) || consumed.Contains(commandId!)) return;
                    var authorization = BoundarySourceAuthorization.Read(directory, scripts);
                    runId = authorization.RunId;
                    if (command.GetProperty("runId").GetString() != runId) return;
                    var age = DateTime.UtcNow - command.GetProperty("createdUtc").GetDateTime().ToUniversalTime();
                    if (age < TimeSpan.FromSeconds(-5) || age > TimeSpan.FromMinutes(2)) return;
                    var operation = command.GetProperty("operation").GetString();
                    if (operation is not ("disable-decompile-and-downloads" or "enable-decompile-local-only" or "request-consent"))
                        throw new InvalidOperationException("Only three fixed setup operations are allowed");
                    consumed.Add(commandId!);
                    var id = commandId!;
                    // QueueReadLock is the public primary-thread scheduling seam. Release its locks
                    // and permit the real modal host legal UI; never ask while holding PSI locks.
                    solution.Locks.AllowNestedExecutionAndReleaseLocks("AnotherFSM.SourceBoundary.IndependentSetup", () => {
                        try
                        {
                            if (!lifetime.IsAlive || !solution.Locks.IsOnMainThread() || solution.Locks.ContentModelLocks.IsReadLockHeld || solution.Locks.IsWriteLockHeld())
                                throw new InvalidOperationException("Standalone live primary-thread setup required");
                            BoundarySourceAuthorization.CheckSentinel();
                            var store = Shell.Instance.GetComponent<ISettingsStore>().BindToContextTransient(ContextRange.ApplicationWide);
                            var before = Read(store);
                            if (operation == "request-consent")
                            {
                                bool alreadyAccepted = store.GetValue((DecompilerSettings value) => value.DecompilerLegalNoticeAccepted);
                                Ack(directory, new { runId, commandId = id, backendPid = Environment.ProcessId, status = "awaitingLegalNoticeUI", alreadyAccepted, before });
                                // Host owns the Yes/No dialog and persistence. NEVER SetValue(consent,true).
                                bool accepted = Shell.Instance.GetComponent<DecompiledSourceLegalNotice>().CheckAndAskUser();
                                Ack(directory, new { runId, commandId = id, backendPid = Environment.ProcessId, status = "noticeReturned", alreadyAccepted,
                                    checkAndAskUserResult = accepted, before, after = Read(store), completedUtc = DateTime.UtcNow });
                            }
                            else
                            {
                                // Fixed typed whitelist; neither caller-provided paths nor arbitrary settings.
                                store.SetValue((DecompilerSettings value) => value.DecompileMethodBodies, operation == "enable-decompile-local-only");
                                store.SetValue((PdbNavigationSettings value) => value.Download, false);
                                store.SetValue((PdbNavigationSettings value) => value.UseSymbolFiles, false);
                                store.SetValue((PdbNavigationSettings value) => value.SourceLinkV3, false);
                                Ack(directory, new { runId, commandId = id, backendPid = Environment.ProcessId, status = "applied", operation,
                                    before, after = Read(store), completedUtc = DateTime.UtcNow,
                                    claim = "setup writes only; effective/provider state MUST be independently observed; exact restore is main's post-shutdown responsibility" });
                            }
                            BoundarySourceAuthorization.CheckSentinel();
                        }
                        catch (Exception error) { Ack(directory, new { runId, commandId = id, backendPid = Environment.ProcessId, status = "failed", error = error.GetType().FullName }); }
                    });
                }
                catch (IOException) { }
                catch (JsonException) { }
                catch (Exception error) { Ack(directory, new { runId, commandId, backendPid = Environment.ProcessId, status = "rejected", error = error.GetType().FullName }); }
                finally { Interlocked.Exchange(ref queued, 0); }
            });
        }, null, 250, 250);
        lifetime.OnTermination(() => timer?.Dispose());
    }
    private static object Read(IContextBoundSettingsStore store) => new {
        bodies = store.GetValue((DecompilerSettings value) => value.DecompileMethodBodies),
        consent = store.GetValue((DecompilerSettings value) => value.DecompilerLegalNoticeAccepted),
        download = store.GetValue((PdbNavigationSettings value) => value.Download),
        useSymbols = store.GetValue((PdbNavigationSettings value) => value.UseSymbolFiles),
        sourceLink = store.GetValue((PdbNavigationSettings value) => value.SourceLinkV3)
    };
    private static void Ack(string directory, object value) => BoundarySourceAuthorization.Write(Path.Combine(directory, "source-policy-ack.json"), value);
}

/// <summary>SEPARATE, strictly read-only target-specific observer. Never writes settings,
/// requests consent, generates text, navigates, or chooses Sources/PDB/Source Link providers.</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public sealed class BoundarySourcePolicyObserver
{
    private Timer? timer;private int queued;
    public BoundarySourcePolicyObserver(Lifetime lifetime, ISolution solution, IPsiServices psi, ScriptDocuments scripts, ScriptIndex index)
    {
        var directory = Environment.GetEnvironmentVariable("ANOTHERFSM_ACCEPTANCE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        var started = DateTime.UtcNow;long sequence = 0;
        timer = new Timer(tick => {
            if (!lifetime.IsAlive || DateTime.UtcNow - started > TimeSpan.FromMinutes(25)) { timer?.Dispose(); return; }
            if (Interlocked.Exchange(ref queued, 1) != 0) return;
            solution.Locks.QueueReadLock(lifetime, "AnotherFSM.SourceBoundary.ReadOnly", () => {
                try
                {
                    var auth = BoundarySourceAuthorization.Read(directory, scripts);
                    var caches = solution.GetComponent<IPsiCachesState>();
                    if (!psi.Files.AllDocumentsAreCommitted || !caches.IsInitialUpdateFinished.Value || !caches.IsPrimaryIdle.Value) return;
                    var epoch = index.Epoch;
                    var module = scripts.Modules().Single(module => BoundarySourceAuthorization.Project(module, auth.Root));
                    var catalog = index.Catalog(module);
                    var store = Shell.Instance.GetComponent<ISettingsStore>().BindToContextTransient(ContextRange.ApplicationWide);
                    var sources = solution.GetComponent<IExternalSourcesService>();
                    var targets = new[] { "SourceOfflineKey", "SourceUnauthorizedKey", "SourceMismatchKey", "SourceNoSymbolsKey", "SourceBrokenILKey" }.Select(key => {
                        var binding = NodeBindings.Resolve(new("Observe", key, new(0, 7), new(8, key.Length), false), catalog);
                        var identity = binding.State == BindingState.Determined ? binding.Candidates.Single() : null;
                        var type = identity == null ? null : TypeCatalog.Resolve(psi, identity);
                        var compiled = type as ICompiledElement;var assembly = type?.Module as IAssemblyPsiModule;
                        var info = compiled?.ToNavigationInfo();
                        return new { key, state = binding.State.ToString(), binding.Reason, identity, compiled = compiled != null,
                            metadata = compiled == null || assembly == null ? null : new { clrName = type!.GetClrName().FullName,
                                module = type.Module.GetPersistentID(), assembly = assembly.Assembly.AssemblyName.FullName, token = compiled.Token },
                            providers = sources.OrderedProviders.Where(provider => provider.GetType() == typeof(DecompiledSourcesExternalSourcesProvider)
                                || provider.GetType() == typeof(MetadataViewerExternalSourcesProvider)).Select(provider => new {
                                    id = provider.Id, runtimeType = provider.GetType().FullName,
                                    applicable = info != null && provider.IsApplicableForNavigation(info, ignoreOptions: false)
                                }).ToArray() };
                    }).ToArray();
                    BoundarySourceAuthorization.CheckSentinel();
                    BoundarySourceAuthorization.Write(Path.Combine(directory, "source-policy-observation.json"), new {
                        runId = auth.RunId, backendPid = Environment.ProcessId, seq = ++sequence, sampledUtc = DateTime.UtcNow,
                        epoch, session = index.Session, coherent = epoch == index.Epoch, ready = true,
                        policy = new { rawBodies = store.GetValue((DecompilerSettings value) => value.DecompileMethodBodies),
                            effectiveBodies = solution.GetComponent<TranslatorOptions>().GetDecompileMethodBodiesValue(store),
                            consent = store.GetValue((DecompilerSettings value) => value.DecompilerLegalNoticeAccepted),
                            download = store.GetValue((PdbNavigationSettings value) => value.Download),
                            useSymbols = store.GetValue((PdbNavigationSettings value) => value.UseSymbolFiles),
                            sourceLink = store.GetValue((PdbNavigationSettings value) => value.SourceLinkV3),
                            mode = solution.GetComponent<INavigationModeProvider>().GetNavigationMode().ToString(),
                            active = Shell.Instance.GetComponent<ExternalSourcesActivation>().Active.Value, force = ExternalSourcesForceSourceCookie.Check() }, targets,
                        seam = "read-only public SDK; not host navigation or source identity proof" });
                }
                catch (FileNotFoundException) { }
                catch (IOException) { }
                catch (JsonException) { }
                catch (OperationCanceledException) { }
                catch (Exception error) { BoundarySourceAuthorization.Write(Path.Combine(directory, "source-policy-observer-error.json"), new { error = error.GetType().FullName }); }
                finally { Interlocked.Exchange(ref queued, 0); }
            });
        }, null, 500, 500);
        lifetime.OnTermination(() => timer?.Dispose());
    }
}

internal static class BoundarySourceAuthorization
{
    internal sealed record Authorization(string RunId, string Root);
    internal static bool Project(IPsiModule module, string root) => BindingSettings.ProjectPath(module) is { Length: > 0 } project
        && Equal(project, Path.Combine(root, "Host/BoundaryHost.csproj"));
    private static bool Equal(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static Authorization Read(string directory, ScriptDocuments scripts)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "source-policy-authorization.json")));
        var proof = document.RootElement;
        var runId = proof.GetProperty("runId").GetString()!;
        if (!Guid.TryParse(runId, out var parsedRun)) throw new InvalidOperationException("Missing frontend run nonce");
        var root = Path.GetFullPath(proof.GetProperty("root").GetString()!);
        if (!root.Replace('\\','/').EndsWith("/rider/tests/fixtures/boundary", StringComparison.OrdinalIgnoreCase)
            || !Equal(proof.GetProperty("output").GetString()!, directory) || !File.Exists(Path.Combine(root, "Boundary.sln"))
            || scripts.Modules().Count(module => Project(module, root)) != 1) throw new InvalidOperationException("Actual Boundary.sln module required");
        var rider = Directory.GetParent(root)!.Parent!.Parent!.FullName;
        var box = Path.Combine(rider, ".sandbox");var launcherFile = Path.Combine(box, "launcher.json");
        var launcherBytes = File.ReadAllBytes(launcherFile);
        if (Hash(launcherBytes) != proof.GetProperty("launcherSha256").GetString()) throw new InvalidOperationException("Launcher changed since frontend isolation check");
        using var launcherDocument = JsonDocument.Parse(launcherBytes);var launcher = launcherDocument.RootElement;
        int pid = launcher.GetProperty("windowsPid").GetInt32();
        if (pid != proof.GetProperty("frontendPid").GetInt32() || Process.GetProcessById(pid).HasExited
            || !Equal(launcher.GetProperty("fixture").GetString()!, Path.Combine(root, "Boundary.sln"))
            || !Equal(launcher.GetProperty("cwd").GetString()!, box)) throw new InvalidOperationException("Live isolated frontend mismatch");
        var arguments = launcher.GetProperty("expandedArguments").EnumerateArray().Select(value => value.GetString()!).ToArray();
        var expected = new Dictionary<string, string> {
            ["idea.config.path"] = Path.Combine(box,"config"), ["idea.system.path"] = Path.Combine(box,"system"),
            ["idea.plugins.path"] = Path.Combine(box,"plugins"), ["idea.log.path"] = Path.Combine(Directory.GetParent(rider)!.FullName,".scratch/issue-32/sandbox-ide"),
            ["anotherfsm.acceptance.dir"] = directory
        };
        foreach (var pair in expected)
        {
            var values = arguments.Where(argument => argument.StartsWith("-D" + pair.Key + "=", StringComparison.Ordinal)).ToArray();
            if (values.Length != 1 || !Equal(values[0][(values[0].IndexOf('=') + 1)..],pair.Value)
                || (pair.Key.StartsWith("idea.") && !Equal(proof.GetProperty("properties").GetProperty(pair.Key).GetString()!,pair.Value)))
                throw new InvalidOperationException("Isolated path property mismatch");
        }
        foreach (var key in new[] { "idea.config.import.enabled", "idea.settings.sync.enabled", "idea.initially.ask.config" })
            if (!arguments.Contains("-D" + key + "=false") || proof.GetProperty("properties").GetProperty(key).GetString() != "false") throw new InvalidOperationException("Import/sync enabled");
        var arg = launcher.GetProperty("command")[1].GetString()!;
        if (!arg.StartsWith('@') || Hash(File.ReadAllBytes(arg[1..])) != launcher.GetProperty("argfileSha256").GetString()
            || !File.Exists(Path.Combine(box,"prepared.json"))) throw new InvalidOperationException("Unverified launcher inputs");
        using var backupDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(box, "source-boundary-settings-backup.json")));
        var backup = backupDocument.RootElement;var backupId = backup.GetProperty("backupId").GetString()!;
        if (!Guid.TryParse(backupId,out var parsedBackup) || proof.GetProperty("backupId").GetString() != backupId
            || backup.GetProperty("state").GetString() != "active" || !backup.GetProperty("confirmedClosed").GetBoolean()
            || !Equal(backup.GetProperty("settings").GetString()!,Path.Combine(box,"config/resharper-host/GlobalSettingsStorage.DotSettings"))
            || backup.GetProperty("capturedUtc").GetDateTime().ToUniversalTime() > File.GetLastWriteTimeUtc(launcherFile)
            || Hash(File.ReadAllBytes(Path.Combine(root,"SourceCases/artifacts/settings",backupId,"GlobalSettingsStorage.bytes"))) != backup.GetProperty("sha256").GetString())
            throw new InvalidOperationException("Exact prelaunch settings backup not established");
        CheckSentinel();return new(runId,root);
    }
    internal static void CheckSentinel()
    {
        var marker = Environment.GetEnvironmentVariable("ANOTHERFSM_COMPLEX_EXECUTION_MARKER");
        if (string.IsNullOrWhiteSpace(marker) || !Path.IsPathFullyQualified(marker) || File.Exists(marker)) throw new InvalidOperationException("Configured, absent execution sentinel required");
    }
    internal static void Write(string path, object value)
    {
        File.WriteAllText(path + ".tmp",JsonSerializer.Serialize(value,new JsonSerializerOptions { WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase }));
        File.Move(path + ".tmp",path,true);
    }
}
