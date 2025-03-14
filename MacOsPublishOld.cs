// using System.Diagnostics;
// using System.Diagnostics.CodeAnalysis;
// using System.Reflection;
// using System.Security.Cryptography;
// using System.Text.RegularExpressions;
//
// namespace MacOsPublish;
//
// /*
//     MacOsPublish - Build and Publish - Produces one Bundle with this structure
//     
//     YourApplication.app
//         Contents
//             ├── MacOS
//             │   ├── osx-arm64
//             │   │   └── [ your self-contained arm64 YourApplication goes here ]
//             │   ├── osx-x64
//             │   │   └── [ your self-contained x86_64 YourApplication goes here ]
//             │   ├── shared
//             │   │   └── [ all common files go here ]
//             │   └── YourApplication
//             ├── Resources
//             │   ├── icon.icns and other assets files
//             │   └── [ any other resources go here ]
//             └── Info.plist
// */
//
// [SuppressMessage("ReSharper", "LocalizableElement")]
// static class Program
// {
//     static async Task Main(string[] args)
//     {
//         string macOsPublishVersion = StripInformationalVersionGitHash(Assembly
//             .GetExecutingAssembly()
//             .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
//             ?.InformationalVersion ?? "");
//         
//         Console.WriteLine($"MacOsPublish V{macOsPublishVersion} - Copyright (C) 2025 Castello Branco Technologia LTDA");
//
//         Stopwatch sw = Stopwatch.StartNew();
//
//         try
//         {
//             if (args.Length < 1 || args.Contains("-?") || args.Contains("-h") || args.Contains("-help"))
//             {
//                 ShowHelp();
//
//                 return;
//             }
//
//             if (! await CheckDependencies())
//             {
//                 return;
//             }
//
//             string projectFileName = args[0];
//
//             if (! projectFileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) &&
//                 ! projectFileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) )
//             {
//                 
//                 Console.WriteLine("Project must be an valid .csproj or .sln file.");
//
//                 return;
//             }
//
//             if (!File.Exists(projectFileName))
//             {
//                 Console.WriteLine("Project not found");
//
//                 ShowHelp();
//
//                 return;
//             }
//             
//             string projectDir = Path.GetDirectoryName(projectFileName) ?? Directory.GetCurrentDirectory();
//
//             bool dryRun = false;
//
//             if (args.Contains("--dry-run"))
//             {
//                 dryRun = true;
//                 
//                 Console.WriteLine("[INFO] Dry run mode. None files will be generated...");
//             }
//             
//             string plistDir = projectDir;
//             
//             int indexPlist = Array.IndexOf(args, "--plist-dir");
//
//             if (indexPlist >= 0)
//             {
//                 if (indexPlist < args.Length - 1)
//                 {
//                     plistDir = args[indexPlist + 1];
//                 }
//             }
//             
//             string outputDir = "bin/UniversalBundleApp";
//
//             int indexOutput = Array.IndexOf(args, "--output");
//
//             if (indexOutput >= 0)
//             {
//                 if (indexOutput < args.Length - 1)
//                 {
//                     outputDir = args[indexOutput + 1];
//                 }
//             }
//             
//             string assemmblyVersion = DateTime.Now.ToString("yy.MM.dd.HHmm");
//
//             int indexAssemmblyVersion = Array.IndexOf(args, "--AssemmblyVersion");
//
//             if (indexAssemmblyVersion >= 0)
//             {
//                 if (indexAssemmblyVersion < args.Length - 1)
//                 {
//                     assemmblyVersion = args[indexAssemmblyVersion + 1];
//                 }
//             }
//
//             if (!dryRun)
//             {
//                 Directory.CreateDirectory(outputDir);
//             }
//
//             string signIdentity = "";
//
//             int indexIdentity = Array.IndexOf(args, "--identity");
//
//             if (indexIdentity >= 0)
//             {
//                 if (indexIdentity < args.Length - 1)
//                 {
//                     signIdentity = args[indexIdentity + 1];
//                 }
//             }
//             
//             string installerIdentity = "";
//
//             int indexInstallerIdentity = Array.IndexOf(args, "--installer-identity");
//
//             if (indexInstallerIdentity >= 0)
//             {
//                 if (indexInstallerIdentity < args.Length - 1)
//                 {
//                     installerIdentity = args[indexInstallerIdentity + 1];
//                 }
//             }
//             
//             bool notarize = false;
//             string profile = "MacOsPublishProfile";
//
//             int index = Array.IndexOf(args, "--notarize");
//
//             if (index >= 0)
//             {
//                 notarize = true;
//
//                 if (index < args.Length - 1)
//                 {
//                     profile = args[index + 1];
//                 }
//             }
//
//             string[] configurations = ["Debug", "Release"];
//             
//             foreach (string configuration in configurations)
//             {
//                 if (! await GenerateBundleAsync(configuration, 
//                                                 projectFileName, 
//                                                 outputDir, 
//                                                 signIdentity, 
//                                                 installerIdentity, 
//                                                 notarize, 
//                                                 profile, 
//                                                 plistDir,
//                                                 projectDir, 
//                                                 dryRun, 
//                                                 assemmblyVersion))
//                 {
//                     Environment.Exit(-1);
//
//                     break;
//                 }
//             }
//
//             Console.WriteLine("Done");
//         }
//         finally
//         {
//             sw.Stop();
//
//             Console.WriteLine($"Time Elapsed: {sw.Elapsed.TotalSeconds:N1} seconds.");
//         }
//     }
//     
//     private static string StripInformationalVersionGitHash(string version)
//     {
//         if (string.IsNullOrWhiteSpace(version))
//             return version;
//
//         // Match + followed by exactly 40 lowercase hex digits at the end
//         
//         var match = Regex.Match(version, @"^(?<version>.+)\+[a-f0-9]{40}$", RegexOptions.IgnoreCase);
//
//         if (match.Success)
//             return match.Groups["version"].Value;
//
//         return version;
//     }
//
//     private static async Task<bool> CheckDependencies()
//     {
//         foreach (var tool in new[] { "dotnet", "codesign", "xcrun", "hdiutil" })
//         {
//             var (code, _, err) = await Program.RunCommandAsync("which", tool);
//             
//             if (code != 0)
//             {
//                 Console.WriteLine($"[ERROR] Required tool not found: {tool}\n{err}");
//                 
//                 return false;
//             }
//         }
//
//         return true;
//     }
//     
//     private static void ShowHelp()
//     {
//         Console.WriteLine();
//         
//         Console.WriteLine(MacOsPublishResources.HelpMessage); 
//             
//         Environment.Exit(-1);
//     }
//     
//     private static async Task<(int exitCode, string output, string error)> RunCommandAsync(string cmd, string args)
//     {
//         var process = new Process
//         {
//             StartInfo = new ProcessStartInfo
//             {
//                 FileName = cmd,
//                 Arguments = args,
//                 RedirectStandardOutput = true,
//                 RedirectStandardError = true,
//                 UseShellExecute = false,
//                 CreateNoWindow = true
//             },
//             EnableRaisingEvents = true
//         };
//
//         var outputBuilder = new System.Text.StringBuilder();
//         var errorBuilder = new System.Text.StringBuilder();
//
//         var outputTcs = new TaskCompletionSource<bool>();
//         var errorTcs = new TaskCompletionSource<bool>();
//
//         process.OutputDataReceived += (_, e) =>
//         {
//             if (e.Data == null)
//                 outputTcs.TrySetResult(true);
//             else
//                 outputBuilder.AppendLine(e.Data);
//         };
//
//         process.ErrorDataReceived += (_, e) =>
//         {
//             if (e.Data == null)
//                 errorTcs.TrySetResult(true);
//             else
//                 errorBuilder.AppendLine(e.Data);
//         };
//
//         process.Start();
//
//         process.BeginOutputReadLine();
//         process.BeginErrorReadLine();
//
//         await Task.WhenAll(
//             Task.Run(() => process.WaitForExit()),
//             outputTcs.Task,
//             errorTcs.Task
//         );
//
//         return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
//     }
//     
//     private static async Task CopyDirectoryAsync(string sourceDir, string destinationDir)
//     {
//         if (string.IsNullOrWhiteSpace(sourceDir))
//             sourceDir = Directory.GetCurrentDirectory();
//
//         if (string.IsNullOrWhiteSpace(destinationDir))
//             destinationDir = Directory.GetCurrentDirectory();
//
//         Directory.CreateDirectory(destinationDir);
//
//         foreach (string file in Directory.GetFiles(sourceDir))
//         {
//             string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
//
//             // Use async file copy
//             await using FileStream sourceStream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read);
//             await using FileStream destStream = File.Create(destFile);
//             await sourceStream.CopyToAsync(destStream);
//         }
//
//         foreach (string subDir in Directory.GetDirectories(sourceDir))
//         {
//             string destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
//             await CopyDirectoryAsync(subDir, destSubDir);
//         }
//     }
//     
//     private static async Task<bool> PublishRidAsync(string rid, string configuration, string assemblyVersion, string macOsDir, bool dryRun)
//     {
//         bool publishSingleFile = true; 
//         bool publishReadyToRun = false;
//         bool tieredCompilation = false;
//         bool publishTrimmed = false;
//         bool selfContained = true;
//         bool includeNativeLibrariesForSelfExtract=true;
//         bool includeAllContentForSelfExtract = true;
//         
//         string cmd = "dotnet";
//         string args = $"publish "  
//                       + $" --configuration {configuration} "
//                       + $" --runtime {rid} "
//                       + $" --output bin/{configuration}/{rid}/publish "
//                       + $" -p:AssemblyVersion={assemblyVersion} " 
//                       + $" -p:PublishReadyToRun={publishReadyToRun} "
//                       + $" -p:PublishSingleFile={publishSingleFile} "
//                       + $" -p:TieredCompilation={tieredCompilation} "
//                       + $" -p:PublishTrimmed={publishTrimmed} " 
//                       + $" -p:DebugSymbols={(configuration == "Debug").ToString().ToLower()}"
//                       + $" -p:IncludeNativeLibrariesForSelfExtract={includeNativeLibrariesForSelfExtract} "
//                       + $" -p:IncludeAllContentForSelfExtract={includeAllContentForSelfExtract} "
//                       + $" -p:AppendTargetFrameworkToOutputPath=false " 
//                       + $" -nowarn:NU3004,CS8002,CS1591,NU1900 "
//                       + (selfContained ? " --self-contained " : " ");
//         
//         Console.WriteLine($"Publishing for RID: {rid}");
//
//         if (!dryRun)
//         {
//             (int exitCode, string output, string error) ret = await RunCommandAsync(cmd, args);
//             
//             Console.WriteLine(ret.output);
//
//             if (ret.exitCode != 0)
//             {
//                 Console.ForegroundColor = ConsoleColor.Red;
//                 Console.WriteLine($"[ERROR] Publish failed for {rid}:");
//                 Console.WriteLine(ret.error);
//                 Console.ResetColor();
//                 return false;
//             }
//         }
//         else
//         {
//             Console.WriteLine($"DryRun - executing: {cmd} {args}"); 
//         }
//         
//         Console.ForegroundColor = ConsoleColor.Green;
//         Console.WriteLine($"Publish for {rid} succeeded.");
//         Console.ResetColor();
//
//         string dest = Path.Combine(macOsDir, rid);
//         
//         if (!dryRun)
//         {
//             await CopyDirectoryAsync($"bin/{configuration}/{rid}/publish", dest);
//         }
//         else
//         {
//             Console.WriteLine($"DryRun - Copied bin/{configuration}/{rid}/publish to {dest}.");
//         }
//
//         await Task.Delay(500);
//         
//         return true;
//     }
//
//     private static async Task<bool> GenerateBundleAsync(string configuration, 
//                                                         string projectFileName, 
//                                                         string outputDir, 
//                                                         string signIdentity, 
//                                                         string installerIdentity, 
//                                                         bool notarize, 
//                                                         string profile, 
//                                                         string plistDir,
//                                                         string projectDir,
//                                                         bool dryRun,
//                                                         string assemmblyVersion)
//     {
//         string projectName = Path.GetFileNameWithoutExtension(projectFileName);
//         
//         string bundleName = $"{projectName}.app";
//         
//         string bundleDir = Path.Combine(outputDir, configuration, bundleName);
//
//         Console.WriteLine($"Generating bundle {bundleDir}.");
//
//         if (!dryRun)
//         {
//             if (Directory.Exists(bundleDir))
//                 Directory.Delete(bundleDir, recursive: true);
//
//             Directory.CreateDirectory(bundleDir);
//         }
//         else
//         {
//             Console.WriteLine($"DryRun - Removed directory {bundleDir} if exist.");
//
//             Console.WriteLine($"DryRun - Created directory {bundleDir}.");
//         }
//         
//         string contentsDir = Path.Combine(bundleDir, "Contents");
//
//         if (!dryRun)
//         {
//             Directory.CreateDirectory(contentsDir);
//         }
//         else
//         {
//             Console.WriteLine($"DryRun - Created directory {contentsDir}.");
//         }
//
//         string macOsDir = Path.Combine(contentsDir, "MacOS");
//
//         if (!dryRun)
//         {
//             Directory.CreateDirectory(macOsDir);
//         }
//         else
//         {
//             Console.WriteLine($"DryRun - Created directory {macOsDir}.");
//         }
//
//         string macOsX64Dir = Path.Combine(macOsDir, "osx-x64");
//
//         if (!dryRun)
//         {
//             Directory.CreateDirectory(macOsX64Dir);
//         }
//         else
//         {
//             Console.WriteLine($"DryRun - Created directory {macOsX64Dir}.");
//         }
//
//         string macOsArm64Dir = Path.Combine(macOsDir, "osx-arm64");
//
//         if (!dryRun)
//         {
//             Directory.CreateDirectory(macOsArm64Dir);
//         }
//         else
//         {
//             Console.WriteLine($"DryRun - Created directory {macOsArm64Dir}.");
//         }
//
//         string macOsSharedDir = Path.Combine(macOsDir, "shared");
//
//         if (!dryRun)
//         {
//             Directory.CreateDirectory(macOsSharedDir);
//         }
//         else
//         {
//             Console.WriteLine($"DryRun - Created directory {macOsSharedDir}.");
//         }
//
//         string resourcesDir = Path.Combine(contentsDir, "Resources");
//
//         if (!dryRun)
//         {
//             Directory.CreateDirectory(resourcesDir);
//         }
//         else
//         {
//             Console.WriteLine($"DryRun - Created directory {resourcesDir}.");
//         }
//
//         string executableScriptHost =
//             $@"#!/bin/sh
//
// DIR=$(dirname ""$0"")
// ARM64=$(sysctl -ni hw.optional.arm64)
//
// if [[ ""$ARM64"" == 1 ]]; then
//     exec ""$DIR/osx-arm64/{projectName}""
// else
//     exec ""$DIR/osx-x64/{projectName}""
// fi";
//
//         string executableScript = Path.Combine(macOsDir, $"{projectName}.sh");
//
//         if (!dryRun)
//         {
//             await File.WriteAllTextAsync(executableScript, executableScriptHost);
//
//             await RunCommandAsync("chmod", $"+x \"{executableScript}\" ");
//         }
//         else
//         {
//             Console.WriteLine($"DryRun - Created script {executableScript}.");
//
//             Console.WriteLine($"DryRun - Assigned +x exec attribute to script {executableScript}.");
//         }
//
//         string pListFileName = Path.Combine(plistDir, "Info.pList");
//
//         if (!File.Exists(pListFileName))
//         {
//             Console.WriteLine($"[ERROR] pList file {pListFileName} not exist.");
//
//             return false;
//         }
//         
//         string destplist = Path.Combine(contentsDir, "Info.plist");
//             
//         if (!dryRun)
//         {
//             File.Copy(pListFileName, destplist);
//         }
//         else
//         {
//             Console.WriteLine($"DryRun - Copied plist file {pListFileName} to {destplist}.");
//         }
//
//         string entitlementsFileName = Path.Combine(plistDir, "Entitlements.plist");
//
//         string assetsDirName = Path.Combine(projectDir, "Assets");
//
//         if (!dryRun)
//         {
//             if (Directory.Exists(assetsDirName))
//                 await CopyDirectoryAsync(assetsDirName, resourcesDir);
//         }
//         else
//         {
//             Console.WriteLine($"DryRun - Copied assets directory {assetsDirName} to {resourcesDir}.");
//         }
//
//         Task<bool>[] publishTasks = [
//             PublishRidAsync("osx-x64", configuration, assemmblyVersion, macOsDir, dryRun),
//             PublishRidAsync("osx-arm64", configuration, assemmblyVersion, macOsDir, dryRun)
//         ];
//
//         bool[] results = await Task.WhenAll(publishTasks);
//
//         if (!results.All(success => success))
//             return false;
//
//         if (!dryRun)
//         {
//             DeduplicateAndLinkCommonFiles(macOsX64Dir, macOsArm64Dir, macOsSharedDir);
//         }
//         else
//         {
//             Console.WriteLine("DryRun - Deduplicate and link common files.");
//         }
//
//         (int exitCode, string output, string error) ret;
//
//         if (File.Exists(entitlementsFileName))
//         {
//             string cmd = "/usr/libexec/PlistBuddy";
//             string args = $"-c \"Set :CFBundleVersion {assemmblyVersion}\" \"{Path.Combine(contentsDir, "Info.plist")}\" ";
//
//             if (!dryRun)
//             {
//                 ret = await Program.RunCommandAsync(cmd, args);
//
//                 Console.WriteLine(ret.output);
//
//                 if (ret.exitCode != 0)
//                 {
//                     Console.ForegroundColor = ConsoleColor.Red;
//                     Console.WriteLine("Entitlements apply Failed!");
//                     Console.WriteLine(ret.error);
//                 }
//             }
//             else
//             {
//                 Console.WriteLine($"DryRun - Executing {cmd} {args}.");
//             }
//
//             args = $"-c \"Set :CFBundleShortVersionString {assemmblyVersion}\" \"{Path.Combine(contentsDir, "Info.plist")}\" ";
//             if (!dryRun)
//             {
//                 ret = await Program.RunCommandAsync(cmd, args);
//
//                 Console.WriteLine(ret.output);
//
//                 if (ret.exitCode != 0)
//                 {
//                     Console.ForegroundColor = ConsoleColor.Red;
//                     Console.WriteLine("info.plist apply Failed!");
//                     Console.WriteLine(ret.error);
//                 }
//             }
//             else
//             {
//                 Console.WriteLine($"DryRun - Executing {cmd} {args}.");
//             }
//         }
//         
//         if (!string.IsNullOrWhiteSpace(signIdentity))
//         {
//             Console.WriteLine("[INFO] Starting code signing...");
//
//             string[] files = Directory.GetFiles(macOsDir, "*", SearchOption.AllDirectories);
//
//             foreach (string file in files)
//             {
//                 if (File.Exists(file))
//                 {
//                     Console.WriteLine($"[INFO] Signing {file}");
//
//                     if (!dryRun)
//                     {
//                         ret = await Program.RunCommandAsync("codesign",
//                             $"--force --timestamp --sign \"{signIdentity}\" \"{file}\"");
//
//                         if (ret.exitCode != 0)
//                         {
//                             Console.ForegroundColor = ConsoleColor.Red;
//                             Console.WriteLine($"[ERROR] Failed to sign file: {file}");
//                             Console.WriteLine(ret.error);
//                             Console.ResetColor();
//
//                             return false;
//                         }
//                     }
//                     else
//                     {
//                         Console.WriteLine($"DryRun - executed codesign to signing {file}");
//                     }
//                 }
//             }
//
//             Console.WriteLine($"[INFO] Signing bundle: {bundleDir}");
//
//             if (!dryRun)
//             {
//                 ret = await Program.RunCommandAsync("codesign",
//                     $"--force --timestamp --entitlements \"{entitlementsFileName}\" --sign \"{signIdentity}\" \"{bundleDir}\"");
//
//                 if (ret.exitCode != 0)
//                 {
//                     Console.ForegroundColor = ConsoleColor.Red;
//                     Console.WriteLine("[ERROR] Failed to sign and apply Entitlements to the bundle .");
//                     Console.WriteLine(ret.error);
//                     Console.ResetColor();
//                     return false;
//                 }
//             }
//             else
//             {
//                 Console.WriteLine($"DryRun - Executing codesign to sign bundle {bundleDir}.");
//             }
//
//             if (!dryRun)
//             {
//                 ret = await Program.RunCommandAsync("codesign",
//                     $"--verify --deep --strict --verbose=2 \"{bundleDir}\"");
//
//                 if (ret.exitCode != 0)
//                 {
//                     Console.WriteLine("[WARN] Bundle codesign verification failed.");
//                     Console.WriteLine(ret.error);
//                 }
//             }
//             else
//             {
//                 Console.WriteLine($"DryRun - Executing codesign to verify bundle {bundleDir}.");
//             }
//
//             if (!dryRun)
//             {
//                 ret = await Program.RunCommandAsync("spctl", $"--assess --type execute --verbose=4 \"{bundleDir}\"");
//                 if (ret.exitCode != 0)
//                 {
//                     Console.WriteLine("[WARN] Bundle not accepted by Gatekeeper.");
//                     Console.WriteLine(ret.error);
//                 }
//             }
//             else
//             {
//                 Console.WriteLine($"DryRun - Executing spctl to notarize and verify gatekeeper..");
//             }
//         }
//
//         if (!string.IsNullOrWhiteSpace(installerIdentity))
//         {
//             string pkgPath = Path.Combine(outputDir, $"{projectName}-{assemmblyVersion}-{configuration}.pkg");
//
//             if (!dryRun)
//             {
//                 ret = await Program.RunCommandAsync("productbuild",
//                     $"--version {assemmblyVersion} --component \"{bundleDir}\" /Applications \"{pkgPath}\" --sign \"{installerIdentity}\" ");
//
//                 if (ret.exitCode != 0)
//                 {
//                     Console.ForegroundColor = ConsoleColor.Red;
//                     Console.WriteLine("[ERROR] Failed to generate pkg installer.");
//                     Console.WriteLine(ret.error);
//                     Console.ResetColor();
//                     return false;
//                 }
//             }
//             else
//             {
//                 Console.WriteLine($"DryRun - Executing productbuild to generate pkg installer.");
//             }
//         }
//         
//         string applicationsLink = Path.Combine(outputDir, "Applications");
//
//         if (!File.Exists(applicationsLink))
//         {
//             if (!dryRun)
//             {
//                 var (exitCode, _, err) =
//                     await Program.RunCommandAsync("ln", $"-s /Applications \"{applicationsLink}\"");
//
//                 if (exitCode != 0)
//                 {
//                     Console.ForegroundColor = ConsoleColor.Yellow;
//                     Console.WriteLine("[WARN] Failed to create /Applications shortcut for DMG:");
//                     Console.WriteLine(err);
//                     Console.ResetColor();
//                 }
//             }
//             else
//             {
//                 Console.WriteLine($"DryRun - Created applications link to {applicationsLink}.");
//             }
//         }
//         
//         // Exclui arquivos ".DS_Store" que nao devem ir para dmgs
//
//         if (!dryRun)
//         {
//             await RunCommandAsync("find", $"\"{outputDir}\" -name .DS_Store -delete");
//         }
//         else
//         {
//             Console.WriteLine($"DryRun - Deleted DS_Store files on bundle.");
//         }
//
//         string dmgName = $"{projectName}-{assemmblyVersion}.dmg";
//         string dmgPath = Path.Combine(outputDir, "..", dmgName); // Store .dmg *outside* the folder
//
//         Console.WriteLine($"[INFO] Creating DMG: {dmgPath}");
//
//         // hdiutil create -volname "AppName" -srcfolder "path/to/.app" -ov -format UDZO output.dmg
//
//         if (!dryRun)
//         {
//             ret = await Program.RunCommandAsync("hdiutil",
//                 $"create -volname \"{projectName}\" -srcfolder \"{outputDir}\" -ov -format UDZO \"{dmgPath}\"");
//
//             if (ret.exitCode != 0)
//             {
//                 Console.ForegroundColor = ConsoleColor.Red;
//                 Console.WriteLine("[ERROR] Failed to create DMG.");
//                 Console.WriteLine(ret.error);
//                 Console.ResetColor();
//                 return false;
//             }
//         }
//         else
//         {
//              Console.WriteLine($"DryRun - Created DMG file {dmgName} on {dmgPath}.");   
//         }
//
//         if (!dryRun)
//         {
//             if (File.Exists(applicationsLink) || Directory.Exists(applicationsLink))
//             {
//                 try
//                 {
//                     File.Delete(applicationsLink);
//                 }
//                 catch
//                 {
//                     Directory.Delete(applicationsLink);
//                 }
//             }
//         }
//         else
//         {
//             Console.WriteLine($"DryRun - Deleted applications link to {applicationsLink}.");
//         }
//
//         Console.ForegroundColor = ConsoleColor.Green;
//         Console.WriteLine($"[INFO] DMG created: {dmgPath}");
//         Console.ResetColor();
//         
//         if (notarize && !dryRun)
//         {
//             var check = await Program.RunCommandAsync("xcrun", "notarytool help");
//
//             if (check.exitCode != 0)
//             {
//                 Console.WriteLine("[ERROR] xcrun notarytool is not available. Please install Xcode command line tools.");
//
//                 return false;
//             }
//             
//             Console.WriteLine("[INFO] Submitting DMG to Apple Notary Service...");
//             
//             (int exitCode, string output, string error) result = await Program.RunCommandAsync("xcrun", $"notarytool submit \"{dmgPath}\" --keychain-profile \"{profile}\" --wait");
//
//             Console.WriteLine(result.output);
//
//             if (result.exitCode != 0)
//             {
//                 Console.ForegroundColor = ConsoleColor.Red;
//                 Console.WriteLine("[ERROR] Notarization failed.");
//                 Console.WriteLine(result.error);
//                 Console.ResetColor();
//                 return false;
//             }
//
//             Console.WriteLine("[INFO] Stapling notarization ticket...");
//
//       
//             Console.WriteLine(staple.output);
//
//             if (staple.exitCode != 0)
//             {
//                 Console.ForegroundColor = ConsoleColor.Red;
//                 Console.WriteLine("[ERROR] Stapling failed.");
//                 Console.WriteLine(staple.error);
//                 Console.ResetColor();
//                 return false;
//             }
//
//             Console.ForegroundColor = ConsoleColor.Green;
//             Console.WriteLine("[INFO] Notarization and stapling complete!");
//             Console.ResetColor();
//         }
//             
//         return true;
//     }
//
//     private static void DeduplicateAndLinkCommonFiles(string dir1, string dir2, string outputDir)
//     {
//         dir1 = Path.GetFullPath(dir1);
//         dir2 = Path.GetFullPath(dir2);
//         outputDir = Path.GetFullPath(outputDir);
//
//         Directory.CreateDirectory(outputDir);
//
//         var files1 = GetTopLevelFiles(dir1);
//         var files2 = GetTopLevelFiles(dir2);
//
//         var hashToPath1 = new Dictionary<string, string>();
//
//         Console.WriteLine("Indexing files in dir1...");
//         foreach (var file in files1)
//         {
//             string hash = ComputeFileHash(file);
//             hashToPath1[hash] = file;
//         }
//
//         Console.WriteLine("Comparing with files in dir2...");
//         
//         foreach (var file2 in files2)
//         {
//             string hash = ComputeFileHash(file2);
//             if (hashToPath1.TryGetValue(hash, out string? file1))
//             {
//                 string fileName = Path.GetFileName(file1);
//                 string targetPath = Path.Combine(outputDir, fileName);
//
//                 // Ensure unique file name in outputDir
//                 int count = 1;
//                 while (File.Exists(targetPath))
//                 {
//                     targetPath = Path.Combine(outputDir,
//                         Path.GetFileNameWithoutExtension(fileName) + $"_{count++}" + Path.GetExtension(fileName));
//                 }
//
//                 // Copy the file to outputDir
//                 File.Copy(file1, targetPath);
//
//                 // Delete original files
//                 File.Delete(file1);
//                 File.Delete(file2);
//
//                 // Create relative symbolic links
//                 CreateRelativeSymlink(file1, targetPath);
//                 CreateRelativeSymlink(file2, targetPath);
//
//                 Console.WriteLine($"Linked: {file1} and {file2} -> {targetPath}");
//             }
//         }
//
//         Console.WriteLine("Done.");
//     }
//
//     private static List<string> GetTopLevelFiles(string dir)
//     {
//         var files = new List<string>();
//         foreach (var file in Directory.GetFiles(dir))
//         {
//             // Ensure it's a file and not a directory symlink or hidden junk
//             if (!Directory.Exists(file))
//                 files.Add(file);
//         }
//         return files;
//     }
//
//     private static string ComputeFileHash(string filePath)
//     {
//         using var sha = SHA256.Create();
//         using var stream = File.OpenRead(filePath);
//         return Convert.ToHexString(sha.ComputeHash(stream));
//     }
//
//     private static void CreateRelativeSymlink(string originalPath, string targetPath)
//     {
//         string relativeTarget = GetRelativePath(Path.GetDirectoryName(originalPath)!, targetPath);
//
//         if (File.Exists(originalPath))
//             File.Delete(originalPath);
//
//         var psi = new ProcessStartInfo
//         {
//             FileName = "ln",
//             Arguments = $"-s \"{relativeTarget}\" \"{originalPath}\"",
//             RedirectStandardOutput = true,
//             RedirectStandardError = true,
//             UseShellExecute = false,
//             CreateNoWindow = true
//         };
//
//         using var proc = Process.Start(psi)!;
//         proc.WaitForExit();
//
//         if (proc.ExitCode != 0)
//         {
//             string err = proc.StandardError.ReadToEnd();
//             Console.Error.WriteLine($"Error creating symlink: {err}");
//         }
//     }
//
//     private static string GetRelativePath(string fromPath, string toPath)
//     {
//         Uri fromUri = new Uri(AppendSlashIfMissing(fromPath));
//         Uri toUri = new Uri(toPath);
//         Uri relativeUri = fromUri.MakeRelativeUri(toUri);
//         string relativePath = Uri.UnescapeDataString(relativeUri.ToString());
//         return relativePath.Replace('/', Path.DirectorySeparatorChar);
//     }
//
//     private static string AppendSlashIfMissing(string path)
//     {
//         return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
//     }
// }