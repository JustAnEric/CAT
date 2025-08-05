using System;
using System.Diagnostics;

public class CAT
{
    public static int Main()
    {
        Console.WriteLine("\e[1;35mC\e[0m# \e[1;35mA\e[0mdvanced \e[1;35mT\e[0merminal!\e[0m");
        Console.WriteLine("Copyright (c) 2025 \e[1;35mlunaNoir\e[0m | \e[32mMIT\e[0m");
        Console.WriteLine("Type 'exit' to quit.\n");
        
        string input = "";

        try
        {
            string? PATH = Environment.GetEnvironmentVariable("PATH");
        }
        catch (ArgumentNullException)
        {
            Console.WriteLine("\e[4;31mFatal Error, unable to access PATH Environment Variable!\e[0m");
            return 129;
        }
        catch (System.Security.SecurityException)
        {
            
            return 130;
        };

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

            #pragma warning disable CS8600
            input = Console.ReadLine();
            #pragma warning restore CS8600

            if (string.IsNullOrEmpty(input))
            {
                continue;
            }

            string[] parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string command = parts[0];
            string arguments = parts.Length > 1 ? parts[1] : "";

            if (command.ToLower() == "exit")
            {
                return 0;
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