using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

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

static class Program
{
    static async Task Main(string[] args)
    {
        string? version = Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        
        Console.WriteLine($"MacOsPublish V{version} - Copyright (C) 2025 Castello Branco Technologia LTDA");

        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            if (args.Length < 1 || args.Contains("-?") || args.Contains("-h") || args.Contains("-help"))
            {
                ShowHelp();

                return;
            }

            if (! await CheckDependencies())
            {
                return;
            }

            string projectFileName = args[0];

            if (! projectFileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) &&
                ! projectFileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) )
            {
                
                Console.WriteLine("O arquivo deve ser um .csproj ou .sln válido.");

                return;
            }

            if (!File.Exists(projectFileName))
            {
                Console.WriteLine("Projeto não encontrado");

                ShowHelp();

                return;
            }
            
            string projectDir = Path.GetDirectoryName(projectFileName) ?? Directory.GetCurrentDirectory();

            string plistDir = projectDir;
            
            int indexPlist = Array.IndexOf(args, "--plist-dir");

            if (indexPlist >= 0)
            {
                if (indexPlist < args.Length - 1)
                {
                    plistDir = args[indexPlist + 1];
                }
            }
            
            if (!args.Contains("--no-restore"))
            {
                Console.WriteLine("[INFO] Restoring NuGet packages...");

                (int exitCode, string output, string error)
                    restore = await Program.RunCommandAsync("dotnet", $"restore \"{projectFileName}\"");

                if (restore.exitCode != 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ERROR] Failed to restore project.");
                    Console.WriteLine(restore.error);
                    Console.ResetColor();

                    Environment.Exit(-1);

                    return;
                }
            }

            string outputDir = "bin/UniversalBundleApp";

            int indexOutput = Array.IndexOf(args, "--output");

            if (indexOutput >= 0)
            {
                if (indexOutput < args.Length - 1)
                {
                    outputDir = args[indexOutput + 1];
                }
            }
            
            Directory.CreateDirectory(outputDir);

            string signIdentity = "";

            int indexIdentity = Array.IndexOf(args, "--identity");

            if (indexIdentity >= 0)
            {
                if (indexIdentity < args.Length - 1)
                {
                    signIdentity = args[indexIdentity + 1];
                }
            }
            
            string installerIdentity = "";

            int indexInstallerIdentity = Array.IndexOf(args, "--installer-identity");

            if (indexInstallerIdentity >= 0)
            {
                if (indexInstallerIdentity < args.Length - 1)
                {
                    installerIdentity = args[indexInstallerIdentity + 1];
                }
            }
            
            bool notarize = false;
            string profile = "MacOsPublishProfile";

            int index = Array.IndexOf(args, "--notarize");

            if (index >= 0)
            {
                notarize = true;

                if (index < args.Length - 1)
                {
                    profile = args[index + 1];
                }
            }

            foreach (string configuration in new[] { "Debug", "Release" })
            {
                if (! await GenerateBundleAsync(configuration, 
                                                projectFileName, 
                                                outputDir, 
                                                signIdentity, 
                                                installerIdentity, 
                                                notarize, 
                                                profile, 
                                                plistDir,
                                                projectDir))
                {
                    Environment.Exit(-1);

                    break;
                }
            }

            Console.WriteLine("Done");
        }
        finally
        {
            sw.Stop();

            Console.WriteLine($"Time Elapsed: {sw.Elapsed.TotalSeconds:N1} seconds.");
        }
    }
    
    private static async Task<bool> CheckDependencies()
    {
        foreach (var tool in new[] { "dotnet", "codesign", "xcrun", "hdiutil" })
        {
            var (code, _, err) = await Program.RunCommandAsync("which", tool);
            
            if (code != 0)
            {
                Console.WriteLine($"[ERROR] Required tool not found: {tool}\n{err}");
                return false;
            }
        }

        return true;
    }
    
    private static void ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine(
            @"  
Description:
  Definitive Publish for MACOS

Uso:
  MacOsPublish <PROJECT> [<OUTPUT>] [<SIGNING_IDENTITY>] [<INSTALLER_IDENTITY>] [--notarize <AppleProfile>]

Argumentos:
  <PROJECT>                   O arquivo de projeto para operar. 
                              O arquivo de projeto deve ser um .csproj ou .sln.

  [<OUTPUT_DIR>]              O diretório de saída no qual os bundle sera gerado.
                              se nao especificado sera bin/UniversalBundleApp 
            
  [<SIGNING_IDENTITY>]        A identidade do desenvolvedor Apple. 
                              Se for vazio nao assina o aplicativo

  [<INSTALLER_IDENTITY>]      A identidade do instalador Apple. 

  --notarize <AppleProfile>   Notarize the app using Apple Notary Service.
                              If <AppleProfile> is not provided, defaults to ""MacOsPublishProfile""

  --no-restore                Skip nuget restore.

  --plist-dir  <path>         the directory of plist files (info/entitlements) if not in current directory.  

  -?, -h, --help       Mostrar a ajuda da linha de comando.

  Note that to use --notarize, you must be signed in with xcrun notarytool:

    xcrun notarytool store-credentials --apple-id <your-email> --team-id <TEAMID> --password <app-specific-password> --keychain-profile ""MacOsPublishProfile""

    ⚠️ You only need to run this once. 
    After that, you can pass --notarize in MacOsPublish and it will use the saved credentials. 

   Get this tool at:
        https://www.nuget.org/packages/MacOsPublish

   Contribute to project at:
        https://github.com/CastelloBrancoTecnologia/MacOsPublish
"); 
            
        Environment.Exit(-1);
    }
    
    private static async Task<(int exitCode, string output, string error)> RunCommandAsync(string cmd, string args)
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

        var outputTcs = new TaskCompletionSource<bool>();
        var errorTcs = new TaskCompletionSource<bool>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
                outputTcs.TrySetResult(true);
            else
                outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
                errorTcs.TrySetResult(true);
            else
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await Task.WhenAll(
            Task.Run(() => process.WaitForExit()),
            outputTcs.Task,
            errorTcs.Task
        );

        return (process.ExitCode, outputBuilder.ToString(), errorBuilder.ToString());
    }
    
    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        if (string.IsNullOrWhiteSpace(sourceDir))
            sourceDir = Directory.GetCurrentDirectory();
        
        if (string.IsNullOrWhiteSpace(destinationDir))
            destinationDir = Directory.GetCurrentDirectory();
        
        Directory.CreateDirectory(destinationDir);
        
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, true); // Overwrites if exists
        }
        
        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
            
            CopyDirectory(subDir, destSubDir);
        }
    }
    
    private static async Task<bool> PublishRidAsync(string rid, string configuration, string version, string macOsDir)
    {
        bool publishReadyToRun = false;
        bool tieredCompilation = false;
        bool publishTrimmed = false;
        bool selfContained = true;
        bool includeNativeLibrariesForSelfExtract=true;
        bool includeAllContentForSelfExtract = true;
        
        string cmd = "dotnet";
        string args = $"publish " 
                      + $" -o bin/{configuration}/{rid}/publish"
                      + $" -c {configuration} "
                      + $" -r {rid} "
                      + $" -p:PublishReadyToRun={publishReadyToRun} "
                      + $" -p:TieredCompilation={tieredCompilation} "
                      + $" -p:PublishTrimmed={publishTrimmed} " 
                      + $" -p:IncludeNativeLibrariesForSelfExtract={includeNativeLibrariesForSelfExtract} "
                      + $" -p:IncludeAllContentForSelfExtract={includeAllContentForSelfExtract} "
                      + $" -p:AppendTargetFrameworkToOutputPath=false " 
                      + $" -p:AssemblyVersion={version} "
                      + $" -nowarn:NU3004,CS8002,CS1591,NU1900 "
                      + (selfContained ? " --self-contained " : " ");
        
        Console.WriteLine($"Publishing for RID: {rid}");

        (int exitCode, string output, string error) ret = await RunCommandAsync(cmd, args);

        Console.WriteLine(ret.output);

        if (ret.exitCode != 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Publish failed for {rid}:");
            Console.WriteLine(ret.error);
            Console.ResetColor();
            return false;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Publish for {rid} succeeded.");
        Console.ResetColor();

        CopyDirectory($"bin/{configuration}/{rid}/publish",
            Path.Combine(macOsDir, rid));

        await Task.Delay(500);
        
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
                                                        string projectDir)
    {
        string version = DateTime.Now.ToString("yy.MM.dd.HHmm");
        
        string projectName = Path.GetFileNameWithoutExtension(projectFileName);
        
        string bundleDir = Path.Combine(outputDir, configuration, projectName, ".app");

        Console.WriteLine($"Generating bundle {bundleDir}.");
        
        if (Directory.Exists(bundleDir))
            Directory.Delete(bundleDir, recursive: true);
        
        Directory.CreateDirectory(bundleDir);
        
        string contentsDir = Path.Combine(bundleDir, projectName);
        
        Directory.CreateDirectory(contentsDir);

        string macOsDir = Path.Combine(contentsDir, "MacOS");
        
        Directory.CreateDirectory(macOsDir);
        
        string macOsX64Dir = Path.Combine(macOsDir, "osx-x64");
        
        Directory.CreateDirectory(macOsX64Dir);

        string macOsArm64Dir = Path.Combine(macOsDir, "osx-arm64");
        
        Directory.CreateDirectory(macOsArm64Dir);

        string macOsSharedDir = Path.Combine(macOsDir, "shared");
        
        Directory.CreateDirectory(macOsSharedDir);

        string resourcesDir = Path.Combine(contentsDir, "Resources");
        
        Directory.CreateDirectory(resourcesDir);
        
        string executableScriptHost =
            $@"#!/bin/sh

DIR=$(dirname ""$0"")
ARM64=$(sysctl -ni hw.optional.arm64)

if [[ ""$ARM64"" == 1 ]]; then
    exec ""$DIR/osx-arm64/{projectName}""
else
    exec ""$DIR/osx-x64/{projectName}""
fi";

        string executableScript = Path.Combine(macOsDir, $"{projectName}.sh");
            
        await File.WriteAllTextAsync(executableScript, executableScriptHost);
        
        await RunCommandAsync("chmod", $"+x \"{executableScript}\" ");

        string pListFileName = Path.Combine(plistDir, "Info.pList");

        if (!File.Exists(pListFileName))
        {
            Console.WriteLine($"[ERROR] pList file {pListFileName} not exist.");

            return false;
        }
        
        File.Copy(Path.Combine(plistDir, "Info.plist"), Path.Combine(contentsDir, "Info.plist"));

        string entitlementsFileName = Path.Combine(plistDir, "Entitlements.plist");
        
        if (File.Exists(entitlementsFileName))
        {
            File.Copy(entitlementsFileName, Path.Combine(contentsDir, "Entitlements.plist"));
        }
        
        string assetsDirName = Path.Combine(projectDir, "Assets");
        
        if (Directory.Exists(assetsDirName))
            CopyDirectory(assetsDirName, resourcesDir);
        
        Task<bool>[] publishTasks = [
            PublishRidAsync("osx-x64", configuration, version, macOsDir),
            PublishRidAsync("osx-arm64", configuration, version, macOsDir)
        ];

        bool[] results = await Task.WhenAll(publishTasks);

        if (!results.All(success => success))
            return false;

        DeduplicateAndLinkCommonFiles(macOsX64Dir, macOsArm64Dir, macOsSharedDir);

        (int exitCode, string output, string error) ret;
        
        if (File.Exists(entitlementsFileName))
        {
            ret = await Program.RunCommandAsync ("/usr/libexec/PlistBuddy",
                             $"-c \"Set :CFBundleVersion {version}\" \"{Path.Combine(contentsDir, "Info.plist")}\" ");
            
            Console.WriteLine(ret.output);

            if (ret.exitCode != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Entitlements apply Failed!");
                Console.WriteLine(ret.error);
            }
            
            ret = await Program.RunCommandAsync("/usr/libexec/PlistBuddy",
                $"-c \"Set :CFBundleShortVersionString {version}\" \"{Path.Combine(contentsDir, "Info.plist")}\" ");

            Console.WriteLine(ret.output);

            if (ret.exitCode != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Entitlements apply Failed!");
                Console.WriteLine(ret.error);
            }
        }
        
        if (!string.IsNullOrWhiteSpace(signIdentity))
        {
                Console.WriteLine("[INFO] Starting code signing...");

                foreach (string file in Directory.EnumerateFiles(macOsDir, "*", SearchOption.AllDirectories))
                {
                    if (File.Exists(file))
                    {
                        Console.WriteLine($"[INFO] Signing {file}");

                        ret = await Program.RunCommandAsync("codesign", 
                            $"--force --timestamp --sign \"{signIdentity}\" \"{file}\"");

                        if (ret.exitCode != 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[ERROR] Failed to sign file: {file}");
                            Console.WriteLine(ret.error);
                            Console.ResetColor();
                            
                            return false;
                        }
                    }
                }

                Console.WriteLine($"[INFO] Signing bundle: {bundleDir}");
                
                ret = await Program.RunCommandAsync("codesign",
                    $"--force --timestamp --entitlements \"{Path.Combine(contentsDir, "Entitlements.plist")}\" --sign \"{signIdentity}\" \"{bundleDir}\"");

                if (ret.exitCode != 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ERROR] Failed to sign the bundle.");
                    Console.WriteLine(ret.error);
                    Console.ResetColor();
                    return false;
                }
                
                ret = await Program.RunCommandAsync("codesign", $"--verify --deep --strict --verbose=2 \"{bundleDir}\"");
                if (ret.exitCode != 0)
                {
                    Console.WriteLine("[WARN] Bundle codesign verification failed.");
                    Console.WriteLine(ret.error);
                }

                ret = await Program.RunCommandAsync("spctl", $"--assess --type execute --verbose=4 \"{bundleDir}\"");
                if (ret.exitCode != 0)
                {
                    Console.WriteLine("[WARN] Bundle not accepted by Gatekeeper.");
                    Console.WriteLine(ret.error);
                }
        }

        if (!string.IsNullOrWhiteSpace(installerIdentity))
        {
            string pkgPath = Path.Combine(outputDir, $"{projectName}-{version}-{configuration}.pkg");
           
            ret = await Program.RunCommandAsync("productbuild",
                $"--version {version} --component \"{bundleDir}\" /Applications \"{pkgPath}\" --sign \"{installerIdentity}\" ");
            
            if (ret.exitCode != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] Failed to generate pkg installer.");
                Console.WriteLine(ret.error);
                Console.ResetColor();
                return false;
            }
        }
        
        string applicationsLink = Path.Combine(outputDir, "Applications");

        if (!File.Exists(applicationsLink))
        {
            var (exitCode, _, err) = await Program.RunCommandAsync("ln", $"-s /Applications \"{applicationsLink}\"");

            if (exitCode != 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[WARN] Failed to create /Applications shortcut for DMG:");
                Console.WriteLine(err);
                Console.ResetColor();
            }
        }
        
        // Exclui arquivos ".DS_Store" que nao devem ir para dmgs
        
        await RunCommandAsync("find", $"\"{outputDir}\" -name .DS_Store -delete");
        
        string dmgName = $"{projectName}-{version}.dmg";
        string dmgPath = Path.Combine(outputDir, "..", dmgName); // Store .dmg *outside* the folder

        Console.WriteLine($"[INFO] Creating DMG: {dmgPath}");

        // hdiutil create -volname "AppName" -srcfolder "path/to/.app" -ov -format UDZO output.dmg
        
        ret = await Program.RunCommandAsync("hdiutil",
            $"create -volname \"{projectName}\" -srcfolder \"{outputDir}\" -ov -format UDZO \"{dmgPath}\"");
        
        if (ret.exitCode != 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERROR] Failed to create DMG.");
            Console.WriteLine(ret.error);
            Console.ResetColor();
            return false;
        }
        
        if (File.Exists(applicationsLink) || Directory.Exists(applicationsLink))
        {
            try { File.Delete(applicationsLink); } catch { Directory.Delete(applicationsLink); }
        }
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[INFO] DMG created: {dmgPath}");
        Console.ResetColor();
        
        if (notarize)
        {
            var check = await Program.RunCommandAsync("xcrun", "notarytool help");

            if (check.exitCode != 0)
            {
                Console.WriteLine("[ERROR] xcrun notarytool is not available. Please install Xcode command line tools.");

                return false;
            }
            
            Console.WriteLine("[INFO] Submitting DMG to Apple Notary Service...");
            
            (int exitCode, string output, string error) result = await Program.RunCommandAsync("xcrun", $"notarytool submit \"{dmgPath}\" --keychain-profile \"{profile}\" --wait");

            Console.WriteLine(result.output);

            if (result.exitCode != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] Notarization failed.");
                Console.WriteLine(result.error);
                Console.ResetColor();
                return false;
            }

            Console.WriteLine("[INFO] Stapling notarization ticket...");

            var staple = await Program.RunCommandAsync("xcrun", $"stapler staple \"{dmgPath}\"");
            Console.WriteLine(staple.output);

            if (staple.exitCode != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] Stapling failed.");
                Console.WriteLine(staple.error);
                Console.ResetColor();
                return false;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[INFO] Notarization and stapling complete!");
            Console.ResetColor();
        }
            
        return true;
    }

    private static void DeduplicateAndLinkCommonFiles(string dir1, string dir2, string outputDir)
    {
        dir1 = Path.GetFullPath(dir1);
        dir2 = Path.GetFullPath(dir2);
        outputDir = Path.GetFullPath(outputDir);

        Directory.CreateDirectory(outputDir);

        var files1 = GetTopLevelFiles(dir1);
        var files2 = GetTopLevelFiles(dir2);

        var hashToPath1 = new Dictionary<string, string>();

        Console.WriteLine("Indexing files in dir1...");
        foreach (var file in files1)
        {
            string hash = ComputeFileHash(file);
            hashToPath1[hash] = file;
        }

        Console.WriteLine("Comparing with files in dir2...");
        
        foreach (var file2 in files2)
        {
            string hash = ComputeFileHash(file2);
            if (hashToPath1.TryGetValue(hash, out string? file1))
            {
                string fileName = Path.GetFileName(file1);
                string targetPath = Path.Combine(outputDir, fileName);

                // Ensure unique file name in outputDir
                int count = 1;
                while (File.Exists(targetPath))
                {
                    targetPath = Path.Combine(outputDir,
                        Path.GetFileNameWithoutExtension(fileName) + $"_{count++}" + Path.GetExtension(fileName));
                }

                // Copy the file to outputDir
                File.Copy(file1, targetPath);

                // Delete original files
                File.Delete(file1);
                File.Delete(file2);

                // Create relative symbolic links
                CreateRelativeSymlink(file1, targetPath);
                CreateRelativeSymlink(file2, targetPath);

                Console.WriteLine($"Linked: {file1} and {file2} -> {targetPath}");
            }
        }

        Console.WriteLine("Done.");
    }

    private static List<string> GetTopLevelFiles(string dir)
    {
        var files = new List<string>();
        foreach (var file in Directory.GetFiles(dir))
        {
            // Ensure it's a file and not a directory symlink or hidden junk
            if (!Directory.Exists(file))
                files.Add(file);
        }
        return files;
    }

    private static string ComputeFileHash(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static void CreateRelativeSymlink(string originalPath, string targetPath)
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
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            string err = proc.StandardError.ReadToEnd();
            Console.Error.WriteLine($"Error creating symlink: {err}");
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