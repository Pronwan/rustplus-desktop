using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

namespace RustPlusDesk.Services;

/// <summary>
/// Runs MapParser on this app's own .NET runtime, in a child process of this same executable.
///
/// MapParser used to ship as a self-contained exe, a second copy of the runtime for 34 MB. A
/// runtime-free exe cannot borrow this app's runtime, which is private to its folder, so the app
/// starts itself with <see cref="Flag"/> and loads MapParser.dll instead. It stays a separate
/// process: a crash cannot take the app down, and its memory goes when it exits.
/// </summary>
internal static class MapParserHost
{
    internal const string Flag = "--run-map-parser";

    private const string AssemblyFileName = "MapParser.dll";
    private const int ParserStackBytes = 16 * 1024 * 1024;

    /// <summary>Runs the parser if this process was started to; false means carry on as the app.</summary>
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || args[0] != Flag) return false;

        exitCode = Run(args[1..]);
        return true;
    }

    /// <summary>MapParser.dll: beside the app when installed, or a sibling build in a source checkout.</summary>
    public static string? ResolveParserAssembly(string? baseDirectory = null)
    {
        string baseDir = baseDirectory ?? AppContext.BaseDirectory;

        string installed = Path.Combine(baseDir, "MapParser", AssemblyFileName);
        if (File.Exists(installed)) return installed;

        var candidates = new List<string>();
        for (var dir = new DirectoryInfo(Path.GetFullPath(baseDir)); dir != null; dir = dir.Parent)
        {
            string parserRoot = Path.Combine(dir.FullName, "MapParser");
            if (!File.Exists(Path.Combine(parserRoot, "MapParser.csproj"))) continue;

            foreach (string configuration in new[] { "Debug", "Release" })
            {
                candidates.Add(Path.Combine(parserRoot, "bin", configuration, "net8.0", "win-x64", "publish", AssemblyFileName));
                candidates.Add(Path.Combine(parserRoot, "bin", configuration, "net8.0", AssemblyFileName));
            }
        }

        return candidates
            .Where(File.Exists)
            .OrderByDescending(path => new FileInfo(path).LastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>A start of the parser with these arguments, its output redirected and no window.</summary>
    public static ProcessStartInfo CreateStartInfo(string parserAssembly, IEnumerable<string> parserArgs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? throw new InvalidOperationException("The app's own path is unknown."),
            WorkingDirectory = Path.GetDirectoryName(parserAssembly) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        psi.ArgumentList.Add(Flag);
        foreach (string arg in parserArgs) psi.ArgumentList.Add(arg);
        return psi;
    }

    private static int Run(string[] parserArgs)
    {
        string? assemblyPath = ResolveParserAssembly();
        if (assemblyPath == null)
        {
            Console.Error.WriteLine("MapParser was not found.");
            return 1;
        }

        // MapParser finds its data files from its base directory; this keeps that its own folder.
        string folder = Path.GetDirectoryName(assemblyPath)!;
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", folder + Path.DirectorySeparatorChar);

        int exitCode = 1;
        // The app's Main is STA and MapParser was written for a plain console thread.
        var thread = new Thread(() =>
        {
            try
            {
                Assembly assembly = new ParserLoadContext(assemblyPath).LoadFromAssemblyPath(assemblyPath);
                MethodInfo entryPoint = assembly.EntryPoint
                    ?? throw new InvalidOperationException("MapParser has no entry point.");
                entryPoint.Invoke(null, new object[] { parserArgs });
                exitCode = Environment.ExitCode;
            }
            catch (TargetInvocationException ex)
            {
                Console.Error.WriteLine(ex.InnerException ?? ex);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
        }, ParserStackBytes);
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
        return exitCode;
    }

    /// <summary>Gives MapParser its own dependencies, which may not match the versions the app uses.</summary>
    private sealed class ParserLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public ParserLoadContext(string assemblyPath) : base("MapParser")
            => _resolver = new AssemblyDependencyResolver(assemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
            => _resolver.ResolveAssemblyToPath(assemblyName) is { } path ? LoadFromAssemblyPath(path) : null;
    }
}
