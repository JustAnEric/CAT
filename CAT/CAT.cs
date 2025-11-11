using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

public class CAT
{
#if WINDOWS
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
#endif

    private static volatile Process? _currentProcess = null;
    private static volatile CancellationTokenSource? _cancellationTokenSource = null;

    private static Dictionary<string, Func<string[], CancellationToken, Task>> commands = new(StringComparer.OrdinalIgnoreCase);

    public static async Task Main()
    {
        Console.Title = "C# Advanced Terminal | CAT";
        Console.TreatControlCAsInput = false;
        Console.CancelKeyPress += OnCancelKeyPress;

        string promptSymbol = IsAdministrator() ? "#" : "$";

#if WINDOWS
        var handle = GetStdHandle(STD_OUTPUT_HANDLE);
        GetConsoleMode(handle, out uint mode);
        SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
#endif
        Config? config = LoadConfig();

        bool isolate = config?.isolate ?? false;

        if (!isolate)
        {
            List<string> blacklist = config?.blacklist ?? [];
            LoadBundles(blacklist);
        }

        Console.WriteLine();

        List<string> start = config?.start ?? [];
        foreach (string command in start)
        {
            using (_cancellationTokenSource = new CancellationTokenSource())
            {
                try
                {
                    await ExecuteInput(command, _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    continue;
                }
            }
        }

        Console.WriteLine($"\n\u001b[1;35mC\u001b[0m# \u001b[1;35mA\u001b[0mdvanced \u001b[1;35mT\u001b[0merminal \u001b[33m1.1.1\u001b[0m");
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

                try
                {
                    var token = _cancellationTokenSource.Token;
                    ExecuteInput(input, token).Wait(token);
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

    private class CommandNode
    {
        public string Command = string.Empty;
        public List<string> Args = new();
        public string? StdInFile;
        public string? StdOutFile;
        public bool AppendOut;
        public string? StdErrFile;
        public bool AppendErr;
    }

    private static async Task ExecuteInput(string input, CancellationToken token)
    {
        Config? config = LoadConfig();
        var aliases = config?.aliases ?? new Dictionary<string, string>();

        var pipeline = ParsePipeline(input);
        if (pipeline.Count == 0) return;

        foreach (var node in pipeline)
        {
            if (aliases.TryGetValue(node.Command.ToLower(), out string? aliasValue))
            {
                var aliasParts = Tokenize(aliasValue);
                if (aliasParts.Count > 0)
                {
                    node.Command = aliasParts[0];
                    if (aliasParts.Count > 1)
                    {
                        var aliasArgs = aliasParts.Skip(1).ToList();
                        aliasArgs.AddRange(node.Args);
                        node.Args = aliasArgs;
                    }
                }
            }
        }

        string pipelineInput = string.Empty;

        var firstNode = pipeline[0];
        if (!string.IsNullOrEmpty(firstNode.StdInFile))
        {
            try { pipelineInput = File.ReadAllText(firstNode.StdInFile); }
            catch (Exception ex) { Console.WriteLine($"\u001b[31m< {ex.Message}\u001b[0m"); return; }
        }

        for (int i = 0; i < pipeline.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var node = pipeline[i];
            bool isLast = (i == pipeline.Count - 1);

            string stdout = string.Empty;
            string stderr = string.Empty;
            int exitCode = 0;

            bool redirectInput = !string.IsNullOrEmpty(pipelineInput);
            bool redirectOutput = !isLast || !string.IsNullOrEmpty(node.StdOutFile) || !string.IsNullOrEmpty(node.StdErrFile);

            if (commands.TryGetValue(node.Command.ToLower(), out var bundleAction))
            {
                if (redirectInput || redirectOutput)
                    (stdout, stderr, exitCode) = await ExecuteBundleCommand(bundleAction, node.Args.ToArray(), pipelineInput, token);
                else
                    await RunCommand(bundleAction, node.Args.ToArray(), token);
            }
            else if (node.Command.EndsWith(".catsh", StringComparison.OrdinalIgnoreCase))
            {
                if (redirectInput || redirectOutput)
                    (stdout, stderr, exitCode) = await ExecuteScriptCommand(node, pipelineInput, token);
                else
                    await RunScript(node.Command, string.Join(' ', node.Args), token);
            }
            else
            {
                try
                {
                    if (redirectInput || redirectOutput)
                    {
                        (stdout, stderr, exitCode) = await ExecuteExternal(node.Command, string.Join(' ', node.Args), pipelineInput, token);
                    }
                    else
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = node.Command,
                            Arguments = string.Join(' ', node.Args),
                            UseShellExecute = false
                        };
                        using var proc = Process.Start(psi);
                        if (proc != null) await proc.WaitForExitAsync(token);
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    Console.WriteLine($"\u001b[31mError, \"{node.Command}\" was not recognized as a bundle command, or executable file!\u001b[0m");
                }
            }

            pipelineInput = stdout;

            if (isLast)
            {
                try
                {
                    if (!string.IsNullOrEmpty(node.StdOutFile))
                        await File.WriteAllTextAsync(node.StdOutFile, stdout, token);
                    else if (!string.IsNullOrEmpty(stdout))
                        Console.Write(stdout);

                    if (!string.IsNullOrEmpty(node.StdErrFile))
                        await File.WriteAllTextAsync(node.StdErrFile, stderr, token);
                    else if (!string.IsNullOrEmpty(stderr))
                        Console.Error.Write(stderr);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\u001b[31mRedirection error: {ex.Message}\u001b[0m");
                }
            }
        }
    }

    private static async Task<(string stdout, string stderr, int exitCode)> ExecuteBundleCommand(
        Func<string[], CancellationToken, Task> action, string[] args, string stdin, CancellationToken token)
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var originalIn = Console.In;

        var swOut = new StringWriter();
        var swErr = new StringWriter();

        try
        {
            if (!string.IsNullOrEmpty(stdin))
                Console.SetIn(new StringReader(stdin));

            Console.SetOut(swOut);
            Console.SetError(swErr);

            await RunCommand(action, args, token);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
            Console.SetIn(originalIn);
        }

        return (swOut.ToString(), swErr.ToString(), 0);
    }

    private static async Task<(string stdout, string stderr, int exitCode)> ExecuteScriptCommand(
        CommandNode node, string stdin, CancellationToken token)
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var originalIn = Console.In;

        var swOut = new StringWriter();
        var swErr = new StringWriter();

        try
        {
            if (!string.IsNullOrEmpty(stdin))
                Console.SetIn(new StringReader(stdin));

            Console.SetOut(swOut);
            Console.SetError(swErr);

            await RunScript(node.Command, string.Join(' ', node.Args), token);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
            Console.SetIn(originalIn);
        }

        return (swOut.ToString(), swErr.ToString(), 0);
    }

    private static async Task<(string stdout, string stderr, int exitCode)> ExecuteExternal(
        string command, string args, string stdin, CancellationToken token)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = !string.IsNullOrEmpty(stdin),
            CreateNoWindow = true
        };

        string stdout = string.Empty;
        string stderr = string.Empty;
        int exitCode = -1;

        try
        {
            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _currentProcess = proc;
            proc.Start();

            if (!string.IsNullOrEmpty(stdin))
            {
                await proc.StandardInput.WriteAsync(stdin);
                await proc.StandardInput.FlushAsync();
                proc.StandardInput.Close();
            }

            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();

            while (!proc.HasExited)
            {
                if (token.IsCancellationRequested)
                {
                    try { proc.Kill(true); } catch { }
                    token.ThrowIfCancellationRequested();
                }
                await Task.Delay(10, token);
            }

            stdout = await outTask;
            stderr = await errTask;
            exitCode = proc.ExitCode;
        }
        catch (Exception ex)
        {
            stderr = $"Error executing '{command}': {ex.Message}\n";
        }
        finally
        {
            _currentProcess = null;
        }

        return (stdout, stderr, exitCode);
    }

    private static List<CommandNode> ParsePipeline(string input)
    {
        var tokens = Tokenize(input);
        var segments = new List<List<string>>();
        var current = new List<string>();

        foreach (var t in tokens)
        {
            if (t == "|")
            {
                if (current.Count > 0) { segments.Add(current); current = new List<string>(); }
            }
            else current.Add(t);
        }
        if (current.Count > 0) segments.Add(current);

        var pipeline = new List<CommandNode>();
        for (int i = 0; i < segments.Count; i++)
        {
            pipeline.Add(ParseCommandNode(segments[i], isFirst: i == 0, isLast: i == segments.Count - 1));
        }
        return pipeline;
    }

    private static CommandNode ParseCommandNode(List<string> parts, bool isFirst, bool isLast)
    {
        var node = new CommandNode();

        for (int i = 0; i < parts.Count; i++)
        {
            string tok = parts[i];
            bool hasNext = (i + 1) < parts.Count;

            if ((tok == ">" || tok == ">>") && isLast && hasNext)
            {
                node.StdOutFile = parts[++i];
                node.AppendOut = (tok == ">>");
                continue;
            }
            if ((tok == "2>" || tok == "2>>") && isLast && hasNext)
            {
                node.StdErrFile = parts[++i];
                node.AppendErr = (tok == "2>>");
                continue;
            }
            if (tok == "<" && isFirst && hasNext)
            {
                node.StdInFile = parts[++i];
                continue;
            }

            if (string.IsNullOrEmpty(node.Command)) node.Command = tok;
            else node.Args.Add(tok);
        }

        return node;
    }

    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inSingle = false, inDouble = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '\\' && i + 1 < input.Length)
            {
                i++;
                sb.Append(input[i]);
                continue;
            }

            if (c == '\'' && !inDouble) { inSingle = !inSingle; continue; }
            if (c == '"' && !inSingle) { inDouble = !inDouble; continue; }

            if (!inSingle && !inDouble)
            {
                if (char.IsWhiteSpace(c)) { Flush(); continue; }
                if (c == '|') { Flush(); tokens.Add("|"); continue; }
                if (c == '<') { Flush(); tokens.Add("<"); continue; }
                if (c == '>')
                {
                    Flush();
                    if (i + 1 < input.Length && input[i + 1] == '>') { tokens.Add(">>"); i++; }
                    else tokens.Add(">");
                    continue;
                }
                if (c == '2' && i + 1 < input.Length && input[i + 1] == '>')
                {
                    if (i + 2 < input.Length && input[i + 2] == '>') { Flush(); tokens.Add("2>>"); i += 2; continue; }
                    else { Flush(); tokens.Add("2>"); i += 1; continue; }
                }
            }

            sb.Append(c);
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
        }
    }

    private static async Task RunCommand(Func<string[], CancellationToken, Task> action, string[] args, CancellationToken token)
    {
        try { await action(args, token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Console.WriteLine($"\u001b[31mError in command: {ex.Message}\u001b[0m"); }
    }

    private static Config? LoadConfig()
    {
        string configText = string.Empty;
        Config? config;

        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CAT");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);

        try
        {
            configText = File.ReadAllText($"{path}/config.json");
        }
        catch (FileNotFoundException)
        {
            File.Create($"{path}/config.json").Dispose();
            return new Config
            {
                isolate = false,
                aliases = new Dictionary<string, string>(),
                start = new List<string>(),
                blacklist = new List<string>()
            };
        }
        catch (IOException ex)
        {
            Console.WriteLine($"\u001b[31mError, unable to read config file: {ex.Message}\u001b[0m");
            return null;
        }

        catch (Exception ex)
        {
            Console.WriteLine($"\u001b[4;31mFatal Error occured when trying to read config file: {ex.Message}\u001b[0m");
            Environment.Exit(131);
        }

        try
        {
            config = JsonSerializer.Deserialize<Config>(configText);
        }
        catch (JsonException)
        {
            return new Config
            {
                isolate = false,
                aliases = new Dictionary<string, string>(),
                start = new List<string>(),
                blacklist = new List<string>()
            };
        }

        return config;
    }

    private static void LoadBundles(List<string> blacklist)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CAT", "bundles");
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);

        foreach (var file in Directory.GetFiles(path, "*.cs"))
        {
            string bundleName = Path.GetFileNameWithoutExtension(file);
            Console.WriteLine($"\u001b[33mRecognized bundle: {bundleName}\u001b[0m");

            if (blacklist.Contains(bundleName))
            {
                Console.WriteLine($"\u001b[31m{bundleName} is blacklisted, skipping.\u001b[0m");
                continue;
            }

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
        var task = Task.Run(() => Console.ReadLine(), token);

        try
        {
            task.Wait(token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
        {
            throw new OperationCanceledException();
        }

        string? result = task.Result;

        if (result == null)
        {
            System.Environment.Exit(0);
        }

        return result;
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
                             .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("//"))
                             .ToArray();

        foreach (var line in lines)
        {
            await ExecuteInput(line, token);
        }
    }
}

public class Config
{
    public required bool isolate { get; set; }

    public required Dictionary<string, string> aliases { get; set; }

    public required List<string> start { get; set; }

    public required List<string> blacklist { get; set; }
}
