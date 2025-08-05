using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Diagnostics;
using System.Reflection;
using System.Collections.Generic;
using System.IO;

public class CAT
{
    public static int Main()
    {

        string? input = "";

        Dictionary<string, Action<string[]>> globalCommands = new Dictionary<string, Action<string[]>>();

        RegisterBundles();

        Console.WriteLine("");
        Console.WriteLine("\e[1;35mC\e[0m# \e[1;35mA\e[0mdvanced \e[1;35mT\e[0merminal!\e[0m");
        Console.WriteLine("Copyright (c) 2025 \e[1;35mlunaNoir\e[0m | \e[32mMIT\e[0m");

        try
        {
            string? PATH = Environment.GetEnvironmentVariable("PATH");
        }
        catch (ArgumentNullException)
        {
            Console.WriteLine("\e[4;31mFatal Error, unable to access PATH Environment Variable!\e[0m");
            Console.WriteLine("\e[33mPress any key to quit.\e[0m");
            Console.ReadKey();
            Environment.Exit(129);
        }
        catch (System.Security.SecurityException)
        {
            Console.WriteLine("\e[4;31mFatal Error, security violation when accessing PATH envrionment variable!\e[0m");
            Console.WriteLine("\e[33mPress any key to quit.\e[0m");
            Console.ReadKey();
            Environment.Exit(130);
        }

        void RegisterBundles()
        {
            string bundlePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CAT", "bundles");

            if (!Directory.Exists(bundlePath))
            {
                Directory.CreateDirectory(bundlePath);
            }

            foreach (var file in Directory.GetFiles(bundlePath, "*.cs"))
            {
                Console.WriteLine($"\e[33mRecognized bundle: {file}\e[0m");

                bool isBaseBundle = false;
                using (var reader = new StreamReader(file))
                {
                    string firstLine = reader.ReadLine()?.Trim() ?? "";
                    if (firstLine == "//!CAT.bundle.base")
                    {
                        isBaseBundle = true;
                        Console.WriteLine($"\e[33m{file} recognized as base bundle.\e[0m");
                    }
                }

                Assembly asm = CompileScript(file);
                foreach (var type in asm.GetTypes())
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(string[]))
                        {
                            string commandKey = $"{type.Name.ToLower()}.{method.Name.ToLower()}";
                            globalCommands[commandKey] = args => method.Invoke(Activator.CreateInstance(type), new object[] { args });

                            if (isBaseBundle)
                            {
                                globalCommands[method.Name.ToLower()] = args => method.Invoke(Activator.CreateInstance(type), new object[] { args });
                            }
                        }
                    }
                }
            }
        }

        Assembly CompileScript(string path)
        {
            string code = File.ReadAllText(path);

            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(code);

            string assemblyName = Path.GetRandomFileName();
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .Select(a => MetadataReference.CreateFromFile(a.Location));

            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName,
                new SyntaxTree[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using (var ms = new MemoryStream())
            {
                EmitResult result = compilation.Emit(ms);

                if (!result.Success)
                {
                    IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError ||
                        diagnostic.Severity == DiagnosticSeverity.Error);

                    Console.WriteLine($"\e[4;31mFatal Error, unable to compile bundle \e[4;35m\"{path}\"\e[4;31m:\e[0;31m");
                    Console.WriteLine();
                    foreach (Diagnostic diagnostic in failures)
                    {
                        Console.WriteLine(diagnostic.GetMessage());
                    }
                    Console.WriteLine("\e[0m");
                    Console.WriteLine("\e[33mPress any key to quit.\e[0m");
                    Console.ReadKey();
                    Environment.Exit(131);
                }

                ms.Seek(0, SeekOrigin.Begin);
                return Assembly.Load(ms.ToArray());
            }
        }

        string prompt;
        string user = Environment.UserName;
        string machine = Environment.MachineName;
        string directory = Environment.CurrentDirectory;

        string unixPath = directory.Replace('\\', '/');

        string homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/');
        if (unixPath.StartsWith(homePath))
        {
            unixPath = "~" + unixPath.Substring(homePath.Length);
        }

        if (unixPath.Length > 30)
        {
            var parts = unixPath.Split('/');
            if (parts.Length > 3)
            {
                unixPath = $".../{parts[^2]}/{parts[^1]}";
            }
        }

        prompt = $"\e[33m{user}\e[0m@\e[35m{machine}\e[0m:\e[36m{unixPath}\e[0m";

        while (true)
        {
            Console.Write($"[{prompt}] ");

            input = Console.ReadLine();

            if (string.IsNullOrEmpty(input))
            {
                continue;
            }

            string[] parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string command = parts[0].ToLower();
            string arguments = parts.Length > 1 ? parts[1] : "";
            string commandKey = "";

            if (command.Contains('.'))
            {
                var cmdParts = command.Split('.', 2);
                string bundleName = cmdParts[0];
                string methodName = cmdParts[1];
                commandKey = $"{bundleName}.{methodName}";
            }
            if (!string.IsNullOrEmpty(commandKey) && globalCommands.ContainsKey(commandKey))
            {
                globalCommands[commandKey](arguments.Split(' '));
            }
            else if (globalCommands.ContainsKey(command))
            {
                globalCommands[command](arguments.Split(' '));
            }
            else
            {
                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo()
                    {
                        FileName = command,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = false
                    };

                    using (Process? process = Process.Start(startInfo))
                    {
                        if (process != null)
                        {
                            process.WaitForExit();
                        }
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine($"\e[31mError, \"{command}\" was not recognized as a command, script, or executable file.\e[0m");
                }
            }
        }
    }
}