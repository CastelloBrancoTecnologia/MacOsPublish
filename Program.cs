using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MacOsPublish;

/*
    MacOsPublish - Build and Publish - Produces one Bundle with this structure

    YourApplication.app
        Contents
            ├── MacOS
            │   ├── osx-arm64
            │   │   └── [ your self-contained arm64 YourApplication goes here ]
            │   ├── osx-x64
            │   │   └── [ your self-contained x86_64 YourApplication goes here ]
            │   ├── shared
            │   │   └── [ all common files go here ]
            │   └── YourApplication
            ├── Resources
            │   ├── icon.icns and other assets files
            │   └── [ any other resources go here ]
            └── Info.plist
*/

[SuppressMessage("ReSharper", "LocalizableElement")]
static class Program
{
    static async Task Main(string[] args)
    {
        string version = StripInformationalVersionGitHash(
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "");

        await Console.Out.WriteLineAsync($"MacOsPublish v{version} - © 2025 Castello Branco Technologia LTDA");

        Stopwatch sw = Stopwatch.StartNew();

        using var cts = new CancellationTokenSource();
        
        CancellationToken cancellationToken = cts.Token;

        try
        {
            if (args.Length == 0 || args.Any(arg => arg is "-?" or "-h" or "--help"))
            {
                await ShowHelp();
                
                Environment.Exit(0);
                
                return;
            }

            if (args.Any(arg => arg is "--version"))
            {
                await Console.Out.WriteLineAsync(version);
                
                Environment.Exit(0);
            }
            
            if (!await CheckDependenciesAsync(cancellationToken))
            {
                Environment.Exit(-1);
                
                return;
            }

            string projectFileName = args[0];

            if (!projectFileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) &&
                !projectFileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                await Console.Error.WriteLineAsync("[ERROR] Project file must be a .csproj or .sln");

                Environment.Exit(-1);

                return;
            }

            if (!File.Exists(projectFileName))
            {
                await Console.Error.WriteLineAsync("[ERROR] Project file not found.");
                
                await ShowHelp();
                
                Environment.Exit(-1);

                return;
            }

            string projectDir = Path.GetDirectoryName(projectFileName) ?? Directory.GetCurrentDirectory();
            string outputDir = GetArgValue(args, "--output") ?? $"bin{Path.DirectorySeparatorChar}UniversalBundleApp";
            string plistDir = GetArgValue(args, "--plist-dir") ?? projectDir;
            string signIdentity = GetArgValue(args, "--identity") ?? string.Empty;
            string installerIdentity = GetArgValue(args, "--installer-identity") ?? string.Empty;
            string assemblyVersion = GetArgValue(args, "--AssemmblyVersion") ?? DateTime.Now.ToString("yy.MM.dd.HHmm");
            string profile = GetArgValue(args, "--notarize") ?? "MacOsPublishProfile";
            bool dryRun = args.Contains("--dry-run");
            bool notarize = args.Contains("--notarize");

            (int exitCode, string output, string error) ret = (0, string.Empty, string.Empty);

            if (!string.IsNullOrWhiteSpace(signIdentity) ||
                !string.IsNullOrWhiteSpace(installerIdentity))
            {
                ret = await RunCommandAsync("security", "find-identity -v -p codesigning", cancellationToken);
            }

            if (! string.IsNullOrWhiteSpace(signIdentity) &&
                ! ret.output.Contains(signIdentity))
            {
                await Console.Error.WriteLineAsync(
                    $"[ERROR] Signing identity '{signIdentity}' not found in keychain.");

                Environment.Exit(-1);

                return;
            }
            
            if (! string.IsNullOrWhiteSpace(installerIdentity) &&
                ! ret.output.Contains(installerIdentity))
            {
                await Console.Error.WriteLineAsync(
                    $"[ERROR] Signing identity '{installerIdentity}' not found in keychain.");
                
                Environment.Exit(-1);

                return;
            }
            
            if (dryRun)
                await Console.Out.WriteLineAsync("[INFO] Dry run mode enabled. No files will be generated.");
 
            if (!dryRun)
                Directory.CreateDirectory(outputDir);

            string[] configurations = ["Debug", "Release"];

            foreach (var config in configurations)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                bool success = await GenerateBundleAsync(
                    config, projectFileName, outputDir, signIdentity,
                    installerIdentity, notarize, profile, plistDir,
                    projectDir, dryRun, assemblyVersion, cancellationToken);

                if (!success)
                {
                    await cts.CancelAsync();
                    
                    Environment.Exit(-1);
                    
                    return;
                }
            }

            if (! cancellationToken.IsCancellationRequested)
                await Console.Out.WriteLineAsync("Done");
            else
                await Console.Out.WriteLineAsync("Canceled");
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("[CANCELED] Publishing was canceled.");
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[FATAL ERROR] {ex.Message}");
        }
        finally
        {
            sw.Stop();
            
            await Console.Out.WriteLineAsync($"Time Elapsed: {sw.Elapsed.TotalSeconds:N1} seconds.");
        }
        
        Environment.Exit(cancellationToken.IsCancellationRequested ? -1 : 0);
    }

    private static string StripInformationalVersionGitHash(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return version;
        
        var match = Regex.Match(version, "^(?<v>.+)\\+[a-f0-9]{40}$", RegexOptions.IgnoreCase);
       
        return match.Success ? match.Groups["v"].Value : version;
    }

    private static string? GetArgValue(string[] args, string key)
    {
        int index = Array.IndexOf(args, key);
        return (index >= 0 && index < args.Length - 1) ? args[index + 1] : null;
    }

    private static async Task ShowHelp()
    {
        await Console.Out.WriteLineAsync();
        await Console.Out.WriteLineAsync(MacOsPublishResources.HelpMessage);
    }

    private static async Task<bool> CheckDependenciesAsync(CancellationToken cancellationToken)
    {
        foreach (string tool in new[] { "dotnet", "codesign", "xcrun", "hdiutil" })
        {
            var (code, _, err) = await RunCommandAsync("which", tool, cancellationToken);
            
            if (code != 0)
            {
                await Console.Error.WriteLineAsync($"[ERROR] Required tool not found: {tool} {err}");
 
                return false;
            }
        }

        return true;
    }

    private static async Task<(int exitCode, string output, string error)> RunCommandAsync(string cmd, string args, CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        var outputTcs = new TaskCompletionSource();
        var errorTcs = new TaskCompletionSource();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) outputTcs.TrySetResult();
            else outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) errorTcs.TrySetResult();
            else errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await Task.WhenAll(Task.Run(() => process.WaitForExit(), cancellationToken), outputTcs.Task, errorTcs.Task);

        return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
    }
    
    private static async Task CopyDirectoryAsync(string sourceDir, string destinationDir, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceDir))
            sourceDir = Directory.GetCurrentDirectory();

        if (string.IsNullOrWhiteSpace(destinationDir))
            destinationDir = Directory.GetCurrentDirectory();

        Directory.CreateDirectory(destinationDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            if (cancellationToken.IsCancellationRequested) break;
            
            string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            
            await using FileStream sourceStream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using FileStream destStream = File.Create(destFile);
            await sourceStream.CopyToAsync(destStream, cancellationToken);
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            if (cancellationToken.IsCancellationRequested) break;

            string destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));

            await CopyDirectoryAsync(subDir, destSubDir, cancellationToken);
        }
    }
    
    private static async Task<bool> PublishRidAsync(string rid, string configuration, string assemblyVersion, string macOsDir, bool dryRun, CancellationToken cancellationToken)
    {
        bool publishSingleFile = true; 
        bool publishReadyToRun = false;
        bool tieredCompilation = false;
        bool publishTrimmed = false;
        bool selfContained = true;
        bool includeNativeLibrariesForSelfExtract=true;
        bool includeAllContentForSelfExtract = true;
        
        string cmd = "dotnet";
        string args = $"publish "  
                      + $" --configuration {configuration} "
                      + $" --runtime {rid} "
                      + $" --output bin{Path.DirectorySeparatorChar}{configuration}{Path.DirectorySeparatorChar}{rid}{Path.DirectorySeparatorChar}publish "
                      + $" -p:AssemblyVersion={assemblyVersion} " 
                      + $" -p:PublishReadyToRun={publishReadyToRun} "
                      + $" -p:PublishSingleFile={publishSingleFile} "
                      + $" -p:TieredCompilation={tieredCompilation} "
                      + $" -p:PublishTrimmed={publishTrimmed} " 
                      + $" -p:DebugSymbols={(configuration == "Debug").ToString().ToLower()}"
                      + $" -p:IncludeNativeLibrariesForSelfExtract={includeNativeLibrariesForSelfExtract} "
                      + $" -p:IncludeAllContentForSelfExtract={includeAllContentForSelfExtract} "
                      + $" -p:AppendTargetFrameworkToOutputPath=false " 
                      + $" -nowarn:NU3004,CS8002,CS1591,NU1900 "
                      + (selfContained ? " --self-contained " : " ");
        
        await Console.Out.WriteLineAsync($"Publishing for RID: {rid}");

        if (!dryRun)
        {
            (int exitCode, string output, string error) ret = await RunCommandAsync(cmd, args, cancellationToken);
            
            await Console.Out.WriteLineAsync(ret.output);

            if (ret.exitCode != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                await Console.Out.WriteLineAsync($"[ERROR] Publish failed for {rid}:");
                await Console.Out.WriteLineAsync(ret.error);
                Console.ResetColor();
                return false;
            }
        }
        else
        {
            await Console.Out.WriteLineAsync($"DryRun - executing: {cmd} {args}"); 
        }
        
        Console.ForegroundColor = ConsoleColor.Green;
        await Console.Out.WriteLineAsync($"Publish for {rid} succeeded.");
        Console.ResetColor();

        string dest = Path.Combine(macOsDir, rid);
        
        if (!dryRun)
        {
            await CopyDirectoryAsync($"bin{Path.DirectorySeparatorChar}{configuration}{Path.DirectorySeparatorChar}{rid}{Path.DirectorySeparatorChar}publish", dest, cancellationToken);
        }
        else
        {
            await Console.Out.WriteLineAsync($"DryRun - Copied bin{Path.DirectorySeparatorChar}{configuration}{Path.DirectorySeparatorChar}{rid}{Path.DirectorySeparatorChar}publish to {dest}.");
        }
        
        // if (!dryRun)
        // {
        //     await CopyDirectoryAsync($"bin{Path.DirectorySeparatorChar}{configuration}{Path.DirectorySeparatorChar}AnyCPU{Path.DirectorySeparatorChar}publish", dest, cancellationToken);
        // }
        // else
        // {
        //     await Console.Out.WriteLineAsync($"DryRun - Copied bin{Path.DirectorySeparatorChar}{configuration}{Path.DirectorySeparatorChar}AnyCPU{Path.DirectorySeparatorChar}publish to {dest}.");
        // }
        
        return true;
    }

    private static async Task<bool> GenerateBundleAsync(string configuration, 
                                                        string projectFileName, 
                                                        string outputDir, 
                                                        string signIdentity, 
                                                        string installerIdentity, 
                                                        bool notarize, 
                                                        string profile, 
                                                        string plistDir,
                                                        string projectDir,
                                                        bool dryRun,
                                                        string assemmblyVersion,
                                                        CancellationToken cancellationToken)
    {
        string projectName = Path.GetFileNameWithoutExtension(projectFileName);
        
        string bundleName = $"{projectName}.app";
        
        string bundleDir = Path.Combine(outputDir, configuration, bundleName);

        await Console.Out.WriteLineAsync($"Generating bundle {bundleDir}.");

        if (!dryRun)
        {
            if (Directory.Exists(bundleDir))
                Directory.Delete(bundleDir, recursive: true);

            Directory.CreateDirectory(bundleDir);
        }
        else
        {
            await Console.Out.WriteLineAsync($"DryRun - Removed directory {bundleDir} if exist.");

            await Console.Out.WriteLineAsync($"DryRun - Created directory {bundleDir}.");
        }
        
        string contentsDir = Path.Combine(bundleDir, "Contents");

        if (!dryRun)
        {
            Directory.CreateDirectory(contentsDir);
        }
        else
        {
            await Console.Out.WriteLineAsync($"DryRun - Created directory {contentsDir}.");
        }

        string macOsDir = Path.Combine(contentsDir, "MacOS");

        if (!dryRun)
        {
            Directory.CreateDirectory(macOsDir);
        }
        else
        {
            await Console.Out.WriteLineAsync($"DryRun - Created directory {macOsDir}.");
        }

        string macOsX64Dir = Path.Combine(macOsDir, "osx-x64");

        if (!dryRun)
        {
            Directory.CreateDirectory(macOsX64Dir);
        }
        else
        {
            await Console.Out.WriteLineAsync($"DryRun - Created directory {macOsX64Dir}.");
        }

        string macOsArm64Dir = Path.Combine(macOsDir, "osx-arm64");

        if (!dryRun)
        {
            Directory.CreateDirectory(macOsArm64Dir);
        }
        else
        {
            await Console.Out.WriteLineAsync($"DryRun - Created directory {macOsArm64Dir}.");
        }

        string macOsSharedDir = Path.Combine(macOsDir, "shared");

        if (!dryRun)
        {
            Directory.CreateDirectory(macOsSharedDir);
        }
        else
        {
            await Console.Out.WriteLineAsync($"DryRun - Created directory {macOsSharedDir}.");
        }

        string resourcesDir = Path.Combine(contentsDir, "Resources");

        if (!dryRun)
        {
            Directory.CreateDirectory(resourcesDir);
        }
        else
        {
            await Console.Out.WriteLineAsync($"DryRun - Created directory {resourcesDir}.");
        }

        string executableScriptHost =
            $@"#!{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}sh

DIR=$(dirname ""$0"")
ARM64=$(sysctl -ni hw.optional.arm64)

if [[ ""$ARM64"" == 1 ]]; then
    exec ""$DIR{Path.DirectorySeparatorChar}osx-arm64{Path.DirectorySeparatorChar}{projectName}""
else
    exec ""$DIR{Path.DirectorySeparatorChar}osx-x64{Path.DirectorySeparatorChar}{projectName}""
fi";

        string executableScript = Path.Combine(macOsDir, $"{projectName}.sh");

        if (!dryRun)
        {
            await File.WriteAllTextAsync(executableScript, executableScriptHost, cancellationToken);

            await RunCommandAsync("chmod", $"+x \"{executableScript}\" ", cancellationToken);
        }
        else
        {
            await Console.Out.WriteLineAsync($"DryRun - Created script {executableScript}.");

            await Console.Out.WriteLineAsync($"DryRun - Assigned +x exec attribute to script {executableScript}.");
        }

        string pListFileName = Path.Combine(plistDir, "Info.plist");

        if (!File.Exists(pListFileName))
        {
            await Console.Out.WriteLineAsync($"[ERROR] pList file {pListFileName} not exist.");

            return false;
        }
        
        string destplist = Path.Combine(contentsDir, "Info.plist");
            
        if (!dryRun)
        {
            File.Copy(pListFileName, destplist);
        }
        else
        {
            await Console.Out.WriteLineAsync($"DryRun - Copied plist file {pListFileName} to {destplist}.");
        }

        string entitlementsFileName = Path.Combine(plistDir, "Entitlements.plist");

        string assetsDirName = Path.Combine(projectDir, "Assets");

        if (!dryRun)
        {
            if (Directory.Exists(assetsDirName))
                await CopyDirectoryAsync(assetsDirName, resourcesDir,cancellationToken);
        }
        else
        {
            await Console.Out.WriteLineAsync($"DryRun - Copied assets directory {assetsDirName} to {resourcesDir}.");
        }

        Task<bool>[] publishTasks = [
            PublishRidAsync("osx-x64", configuration, assemmblyVersion, macOsDir, dryRun, cancellationToken),
            PublishRidAsync("osx-arm64", configuration, assemmblyVersion, macOsDir, dryRun, cancellationToken)
        ];

        bool[] results = await Task.WhenAll(publishTasks);

        if (!results.All(success => success))
            return false;

        if (!dryRun)
        {
            await DeduplicateAndLinkCommonFiles(macOsX64Dir, macOsArm64Dir, macOsSharedDir, cancellationToken);
        }
        else
        {
            await Console.Out.WriteLineAsync("DryRun - Deduplicate and link common files.");
        }

        (int exitCode, string output, string error) ret;

        if (File.Exists(entitlementsFileName))
        {
            string cmd = $"{Path.DirectorySeparatorChar}usr{Path.DirectorySeparatorChar}libexec{Path.DirectorySeparatorChar}PlistBuddy";
            string args = $"-c \"Set :CFBundleVersion {assemmblyVersion}\" \"{Path.Combine(contentsDir, "Info.plist")}\" ";

            if (!dryRun)
            {
                ret = await RunCommandAsync(cmd, args, cancellationToken);

                await Console.Out.WriteLineAsync(ret.output);

                if (ret.exitCode != 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    await Console.Out.WriteLineAsync("Entitlements apply Failed!");
                    await Console.Out.WriteLineAsync(ret.error);
                }
            }
            else
            {
                await Console.Out.WriteLineAsync($"DryRun - Executing {cmd} {args}.");
            }

            args = $"-c \"Set :CFBundleShortVersionString {assemmblyVersion}\" \"{Path.Combine(contentsDir, "Info.plist")}\" ";
            if (!dryRun)
            {
                ret = await RunCommandAsync(cmd, args, cancellationToken);

                await Console.Out.WriteLineAsync(ret.output);

                if (ret.exitCode != 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    await Console.Out.WriteLineAsync("Info.plist apply Failed!");
                    await Console.Out.WriteLineAsync(ret.error);
                }
            }
            else
            {
                await Console.Out.WriteLineAsync($"DryRun - Executing {cmd} {args}.");
            }
        }
        
        if (!string.IsNullOrWhiteSpace(signIdentity))
        {
            await Console.Out.WriteLineAsync("[INFO] Starting code signing...");

            string[] files = Directory.GetFiles(macOsDir, "*", SearchOption.AllDirectories);

            foreach (string file in files)
            {
                if (File.Exists(file))
                {
                    await Console.Out.WriteLineAsync($"[INFO] Signing {file}");

                    if (!dryRun)
                    {
                        ret = await RunCommandAsync("codesign",
                            $"--force --timestamp --sign \"{signIdentity}\" \"{file}\"", 
                            cancellationToken);

                        if (ret.exitCode != 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            await Console.Out.WriteLineAsync($"[ERROR] Failed to sign file: {file}");
                            await Console.Out.WriteLineAsync(ret.error);
                            Console.ResetColor();

                            return false;
                        }
                    }
                    else
                    {
                        await Console.Out.WriteLineAsync($"DryRun - executed codesign to signing {file}");
                    }
                }
            }

            await Console.Out.WriteLineAsync($"[INFO] Signing bundle: {bundleDir}");

            if (!dryRun)
            {
                ret = await RunCommandAsync("codesign",
                    $"--force --timestamp --entitlements \"{entitlementsFileName}\" --sign \"{signIdentity}\" \"{bundleDir}\"",
                    cancellationToken);

                if (ret.exitCode != 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    await Console.Out.WriteLineAsync("[ERROR] Failed to sign and apply Entitlements to the bundle .");
                    await Console.Out.WriteLineAsync(ret.error);
                    Console.ResetColor();
                    return false;
                }
            }
            else
            {
                await Console.Out.WriteLineAsync($"DryRun - Executing codesign to sign bundle {bundleDir}.");
            }

            if (!dryRun)
            {
                ret = await RunCommandAsync("codesign",
                    $"--verify --deep --strict --verbose=2 \"{bundleDir}\"", 
                    cancellationToken);

                if (ret.exitCode != 0)
                {
                    await Console.Out.WriteLineAsync("[WARN] Bundle codesign verification failed.");
                    await Console.Out.WriteLineAsync(ret.error);
                }
            }
            else
            {
                await Console.Out.WriteLineAsync($"DryRun - Executing codesign to verify bundle {bundleDir}.");
            }

            if (!dryRun)
            {
                ret = await RunCommandAsync("spctl", $"--assess --type execute --verbose=4 \"{bundleDir}\"", cancellationToken);

                if (ret.exitCode != 0)
                {
                    await Console.Out.WriteLineAsync("[WARN] Bundle not accepted by Gatekeeper.");
                    await Console.Out.WriteLineAsync(ret.error);
                }
            }
            else
            {
                await Console.Out.WriteLineAsync($"DryRun - Executing spctl to notarize and verify gatekeeper..");
            }
        }

        if (!string.IsNullOrWhiteSpace(installerIdentity))
        {
            string pkgPath = Path.Combine(outputDir, $"{projectName}-{assemmblyVersion}-{configuration}.pkg");

            if (!dryRun)
            {
                ret = await RunCommandAsync("productbuild",
                    $"--version {assemmblyVersion} --component \"{bundleDir}\" {Path.DirectorySeparatorChar}Applications \"{pkgPath}\" --sign \"{installerIdentity}\" ", 
                    cancellationToken);

                if (ret.exitCode != 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    await Console.Out.WriteLineAsync("[ERROR] Failed to generate pkg installer.");
                    await Console.Out.WriteLineAsync(ret.error);
                    Console.ResetColor();
                    return false;
                }
            }
            else
            {
                await Console.Out.WriteLineAsync($"DryRun - Executing productbuild to generate pkg installer.");
            }
        }
        
        string applicationsLink = Path.Combine(outputDir, "Applications");

        if (!File.Exists(applicationsLink))
        {
            if (!dryRun)
            {
                var (exitCode, _, err) =
                    await RunCommandAsync("ln", $"-s {Path.DirectorySeparatorChar}Applications \"{applicationsLink}\"", 
                        cancellationToken);

                if (exitCode != 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    await Console.Out.WriteLineAsync($"[WARN] Failed to create {Path.DirectorySeparatorChar}Applications shortcut for DMG:");
                    await Console.Out.WriteLineAsync(err);
                    Console.ResetColor();
                }
            }
            else
            {
                await Console.Out.WriteLineAsync($"DryRun - Created applications link to {applicationsLink}.");
            }
        }
        
        // Exclui arquivos ".DS_Store" que nao devem ir para dmgs

        if (!dryRun)
        {
            await RunCommandAsync("find", $"\"{outputDir}\" -name .DS_Store -delete", cancellationToken);
        }
        else
        {
            await Console.Out.WriteLineAsync($"DryRun - Deleted DS_Store files on bundle.");
        }

        string dmgName = $"{projectName}-{assemmblyVersion}.dmg";
        string dmgPath = Path.Combine(outputDir, "..", dmgName); // Store .dmg *outside* the folder

        await Console.Out.WriteLineAsync($"[INFO] Creating DMG: {dmgPath}");
        
        if (!dryRun)
        {
            ret = await RunCommandAsync("hdiutil",
                $"create -volname \"{projectName}\" -srcfolder \"{outputDir}\" -ov -format UDZO \"{dmgPath}\"", 
                cancellationToken);

            if (ret.exitCode != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                await Console.Out.WriteLineAsync("[ERROR] Failed to create DMG.");
                await Console.Out.WriteLineAsync(ret.error);
                Console.ResetColor();
                return false;
            }
        }
        else
        {
             await Console.Out.WriteLineAsync($"DryRun - Created DMG file {dmgName} on {dmgPath}.");   
        }

        if (!dryRun)
        {
            if (File.Exists(applicationsLink) || Directory.Exists(applicationsLink))
            {
                try
                {
                    File.Delete(applicationsLink);
                }
                catch
                {
                    Directory.Delete(applicationsLink);
                }
            }
        }
        else
        {
            await Console.Out.WriteLineAsync($"DryRun - Deleted applications link to {applicationsLink}.");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        await Console.Out.WriteLineAsync($"[INFO] DMG created: {dmgPath}");
        Console.ResetColor();
        
        if (notarize && !dryRun)
        {
            var check = await RunCommandAsync("xcrun", "notarytool help", cancellationToken);

            if (check.exitCode != 0)
            {
                await Console.Out.WriteLineAsync("[ERROR] xcrun notarytool is not available. Please install Xcode command line tools.");

                return false;
            }
            
            await Console.Out.WriteLineAsync("[INFO] Submitting DMG to Apple Notary Service...");
            
            (int exitCode, string output, string error) result = await RunCommandAsync("xcrun", $"notarytool submit \"{dmgPath}\" --keychain-profile \"{profile}\" --wait", cancellationToken);

            await Console.Out.WriteLineAsync(result.output);

            if (result.exitCode != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                await Console.Out.WriteLineAsync("[ERROR] Notarization failed.");
                await Console.Out.WriteLineAsync(result.error);
                Console.ResetColor();
                return false;
            }

            await Console.Out.WriteLineAsync("[INFO] Stapling notarization ticket...");

            (int exitCode, string output, string error) staple = await RunCommandAsync("xcrun", $"stapler staple \"{dmgPath}\"", cancellationToken);
 
            await Console.Out.WriteLineAsync(staple.output);

            if (staple.exitCode != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                await Console.Out.WriteLineAsync("[ERROR] Stapling failed.");
                await Console.Out.WriteLineAsync(staple.error);
                Console.ResetColor();
                return false;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            await Console.Out.WriteLineAsync("[INFO] Notarization and stapling complete!");
            Console.ResetColor();
        }
            
        return true;
    }

    private static async Task DeduplicateAndLinkCommonFiles(string dir1, string dir2, string outputDir, CancellationToken cancellationToken)
    {
        dir1 = Path.GetFullPath(dir1);
        dir2 = Path.GetFullPath(dir2);
        outputDir = Path.GetFullPath(outputDir);

        Directory.CreateDirectory(outputDir);

        var files1 = await GetTopLevelFilesAsync(dir1, cancellationToken);

        if (cancellationToken.IsCancellationRequested)
            return;

        var files2 = await GetTopLevelFilesAsync(dir2, cancellationToken);

        if (cancellationToken.IsCancellationRequested)
            return;
        
        var hashToPath1 = new Dictionary<string, string>();

        await Console.Out.WriteLineAsync("Indexing files in dir1...");
        
        foreach (var file in files1)
        {
            if (cancellationToken.IsCancellationRequested) return;
            
            string hash = await ComputeFileHashAsync(file, cancellationToken);

            hashToPath1[hash] = file;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            await Console.Out.WriteLineAsync("Comparing with files in dir2...");

            foreach (var file2 in files2)
            {
                if (cancellationToken.IsCancellationRequested) return;

                string hash = await ComputeFileHashAsync(file2, cancellationToken);
                
                if (hashToPath1.TryGetValue(hash, out string? file1))
                {
                    string fileName = Path.GetFileName(file1);
                    
                    string targetPath = Path.Combine(outputDir, fileName);

                    // Ensure unique file name in outputDir
                    int count = 1;
                
                    while (File.Exists(targetPath))
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        targetPath = Path.Combine(outputDir,
                            Path.GetFileNameWithoutExtension(fileName) + $"_{count++}" + Path.GetExtension(fileName));
                    }
                    
                    File.Copy(file1, targetPath);
                    
                    File.Delete(file1);
                    File.Delete(file2);
                    
                    await CreateRelativeSymlinkAsync(file1, targetPath, cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                        return;

                    await CreateRelativeSymlinkAsync(file2, targetPath, cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                        return;

                    await Console.Out.WriteLineAsync($"Linked: {file1} and {file2} -> {targetPath}");
                }
            }

            await Console.Out.WriteLineAsync("Done.");
        }
    }

    private static async Task <List<string>> GetTopLevelFilesAsync(string dir, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        
        await Task.Delay(0, cancellationToken); // 0 equivale a um yeld

        foreach (var file in Directory.GetFiles(dir))
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Ensure it's a file and not a directory symlink or hidden junk
            if (!Directory.Exists(file))
                files.Add(file);
        }
        
        return files;
    }

    private static async Task <string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(await sha.ComputeHashAsync(stream, cancellationToken));
    }

    private static async Task CreateRelativeSymlinkAsync(string originalPath, string targetPath, CancellationToken cancellationToken)
    {
        string relativeTarget = GetRelativePath(Path.GetDirectoryName(originalPath)!, targetPath);

        if (File.Exists(originalPath))
            File.Delete(originalPath);

        var psi = new ProcessStartInfo
        {
            FileName = "ln",
            Arguments = $"-s \"{relativeTarget}\" \"{originalPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)!;
        
        await proc.WaitForExitAsync(cancellationToken);

        if (proc.ExitCode != 0)
        {
            string err = await proc.StandardError.ReadToEndAsync(cancellationToken);
            
            await Console.Error.WriteLineAsync($"Error creating symlink: {err}");
        }
    }

    private static string GetRelativePath(string fromPath, string toPath)
    {
        Uri fromUri = new Uri(AppendSlashIfMissing(fromPath));
        Uri toUri = new Uri(toPath);
        Uri relativeUri = fromUri.MakeRelativeUri(toUri);
        string relativePath = Uri.UnescapeDataString(relativeUri.ToString());
        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string AppendSlashIfMissing(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
    }
}
