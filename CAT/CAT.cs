using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Principal;
using System.IO;

public class CAT
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    public static string version = "2025.1.0";

    private static volatile Process? _currentProcess = null;
    private static volatile CancellationTokenSource? _cancellationTokenSource = null;

    private static Dictionary<string, Func<string[], CancellationToken, Task>> commands = new(StringComparer.OrdinalIgnoreCase);


    public static void Main()
    {

        Console.Title = "C# Advanced Terminal | CAT";
        Console.TreatControlCAsInput = false;
        Console.CancelKeyPress += OnCancelKeyPress;

        string promptSymbol = IsAdministrator() ? "#" : "$";

        EnableColor();

        LoadBundles();

        Console.WriteLine($"\n\u001b[1;35mC\u001b[0m# \u001b[1;35mA\u001b[0mdvanced \u001b[1;35mT\u001b[0merminal\u001b[0m {version}");
        Console.WriteLine("Copyright (c) 2025 \u001b[1;35mlunaNoir\u001b[0m");

        while (true)
        {
            string prompt = GetPrompt();
            Console.Write($"\n[{prompt}]{promptSymbol} ");

            using (_cancellationTokenSource = new CancellationTokenSource())
            {
                string? input;
                try
                {
                    input = ReadLineWithCancel(_cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(input)) continue;

                string[] parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                string cmd = parts[0];
                string args = parts.Length > 1 ? parts[1] : "";

                try
                {
                    var token = _cancellationTokenSource.Token;

                    if (Path.GetExtension(cmd).Equals(".catsh", StringComparison.OrdinalIgnoreCase))
                    {
                        RunScript(cmd, args, token).Wait(token);
                    }
                    else if (commands.TryGetValue(cmd.ToLower(), out var action))
                    {
                        RunCommand(action, args.Split(' '), token).Wait(token);
                    }
                    else
                    {
                        RunExternalCommand(cmd, args, token);
                    }
                }
                catch (OperationCanceledException)
                { }
                finally
                {
                    _currentProcess = null;
                    _cancellationTokenSource = null;
                }
            }
        }
    }

    private static async Task RunCommand(Func<string[], CancellationToken, Task> action, string[] args, CancellationToken token)
    {
        try
        {
            await action(args, token);
        }
        catch (OperationCanceledException)
        { }
        catch (Exception ex)
        {
            Console.WriteLine($"\u001b[31mError in command: {ex.Message}\u001b[0m");
        }
    }

    private static void LoadBundles()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CAT", "bundles");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);

        foreach (var file in Directory.GetFiles(path, "*.cs"))
        {
            string bundleName = Path.GetFileNameWithoutExtension(file);
            Console.WriteLine($"\u001b[33mRecognized bundle: {bundleName}\u001b[0m");

            bool isElevated = false;
            using (var reader = new StreamReader(file))
            {
                if ((reader.ReadLine()?.Trim() ?? "") == "//!CAT.bundle.elevated")
                {
                    isElevated = true;
                    Console.WriteLine($"\u001b[33m{bundleName} recognized as elevated bundle.\u001b[0m");
                }
            }

            var asm = CompileScript(file);

            foreach (var type in asm.GetTypes())
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(string[]) &&
                        parameters[1].ParameterType == typeof(CancellationToken))
                    {
                        string namespacedKey = $"{bundleName.ToLower()}.{method.Name.ToLower()}";

                        commands[namespacedKey] = async (args, token) =>
                        {
                            var instance = Activator.CreateInstance(type);
                            await Task.Run(() =>
                            {
                                try { method.Invoke(instance, new object[] { args, token }); }
                                catch (TargetInvocationException tie) when (tie.InnerException is OperationCanceledException) { }
                            }, token);
                        };

                        if (isElevated)
                            commands[method.Name.ToLower()] = commands[namespacedKey];
                    }
                }
            }
        }
    }

    private static Assembly CompileScript(string path)
    {
        string code = File.ReadAllText(path);
        var tree = CSharpSyntaxTree.ParseText(code);

        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location));

        var compilation = CSharpCompilation.Create(
            Path.GetRandomFileName(),
            new[] { tree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success)
        {
            var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
            Console.WriteLine($"\u001b[4;31mFatal Error compiling {Path.GetFileNameWithoutExtension(path)}:\u001b[0m");
            foreach (var err in errors) Console.WriteLine(err.GetMessage());
            Console.WriteLine("\u001b[33mPress any key to quit.\u001b[0m");
            Console.ReadKey();
            Environment.Exit(1);
        }

        ms.Seek(0, SeekOrigin.Begin);
        return Assembly.Load(ms.ToArray());
    }

    private static string GetPrompt()
    {
        string user = Environment.UserName;
        string host = Environment.MachineName;
        string dir = Environment.CurrentDirectory;

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/');
        string unix = dir.Replace('\\', '/');
        if (unix.StartsWith(home)) unix = "~" + unix.Substring(home.Length);

        if (unix.Length > 30)
        {
            var parts = unix.Split('/');
            if (parts.Length > 3) unix = $".../{parts[^2]}/{parts[^1]}";
        }

        return $"\u001b[33m{user}\u001b[0m@\u001b[35m{host}\u001b[0m:\u001b[36m{unix}\u001b[0m";
    }

    private static bool IsAdministrator()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        else
        {
            return Environment.GetEnvironmentVariable("USER") == "root" ||
                   Environment.GetEnvironmentVariable("SUDO_USER") == "root" ||
                   (int)Process.GetCurrentProcess().Id == 0;
        }
    }

    private static string ReadLineWithCancel(CancellationToken token)
    {
        var task = Task.Run(() => Console.ReadLine() ?? "", token);
        while (!task.IsCompleted)
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException();
            Thread.Sleep(10);
        }
        return task.Result;
    }

    private static void RunExternalCommand(string command, string args, CancellationToken token)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            _currentProcess = Process.Start(psi);
            while (_currentProcess != null && !_currentProcess.HasExited)
            {
                if (token.IsCancellationRequested)
                {
                    try { _currentProcess.Kill(true); } catch { }
                    break;
                }
                Thread.Sleep(10);
            }
        }
        catch
        {
            Console.WriteLine($"\u001b[31mError, \"{command}\" was not recognized as a bundle or executable file.\u001b[0m");
        }
    }

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _cancellationTokenSource?.Cancel();
        if (_currentProcess != null && !_currentProcess.HasExited)
        {
            try { _currentProcess.Kill(true); } catch { }
        }
    }

    private static async Task RunScript(string filePath, string args, CancellationToken token)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"\u001b[31mError, \"{filePath}\" does not exist!\u001b[0m");
            return;
        }

        var lines = File.ReadAllLines(filePath)
                             .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                             .ToArray();

        foreach (var line in lines)
        {
            token.ThrowIfCancellationRequested();

            string expandedLine = line;

            for (int i = 0; i < args.Split(' ').Length; i++)
            {
                expandedLine = expandedLine.Replace($"%{i + 1}", args.Split(' ')[i]);
            }

            string[] parts = expandedLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0].ToLower();
            string commandArgs = parts.Length > 1 ? parts[1] : "";

            if (commands.TryGetValue(cmd, out var action))
            {
                await RunCommand(action, commandArgs.Split(' '), token);
            }
            else
            {
                RunExternalCommand(cmd, commandArgs, token);
            }
        }
    }

    private static void EnableColor()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var handle = GetStdHandle(-11);
            GetConsoleMode(handle, out uint mode);
            SetConsoleMode(handle, mode | 0x0004);
        }
    }
}