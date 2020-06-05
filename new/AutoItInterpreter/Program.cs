﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.IO;

using CommandLine;

using Unknown6656.AutoIt3.Runtime;
using Unknown6656.AutoIt3.Localization;
using Unknown6656.Controls.Console;
using Unknown6656.Imaging;
using Unknown6656.Common;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Unknown6656.AutoIt3
{
    public sealed class CommandLineOptions
    {
        [Option('B', "nobanner", Default = false, HelpText = "Suppresses the banner.")]
        public bool HideBanner { get; }

        [Option('N', "no-plugins", Default = false, HelpText = "Prevents the loading of interpreter plugins.")]
        public bool DontLoadPlugins { get; }

        [Option('v', "verbosity", Default = Verbosity.n, HelpText = "The interpreter's verbosity level. (q=quiet, n=normal, v=verbose)")]
        public Verbosity Verbosity { get; }

        [Option('l', "lang", Default = "en", HelpText = "The CLI language code to be used by the compiler.")]
        public string Language { get; }

        [Value(0, HelpText = "The file path to the AutoIt-3 srcript.", Required = true)]
        public string FilePath { get; }


        public CommandLineOptions(bool hideBanner, bool dontLoadPlugins, Verbosity verbosity, string language, string filePath)
        {
            HideBanner = hideBanner;
            DontLoadPlugins = dontLoadPlugins;
            Verbosity = verbosity;
            Language = language;
            FilePath = filePath;
        }
    }

    public static class Program
    {
        public static readonly DirectoryInfo ASM_DIR = new FileInfo(typeof(Program).Assembly.Location).Directory!;
        public static readonly DirectoryInfo PLUGIN_DIR = ASM_DIR.CreateSubdirectory("plugins/");
        public static readonly DirectoryInfo LANG_DIR = ASM_DIR.CreateSubdirectory("lang/");

        private static readonly ConcurrentQueue<(string prefix, string message, DateTime time)> _msg_queue = new ConcurrentQueue<(string, string, DateTime)>();
        private static volatile bool _isrunning = true;
#nullable disable
        public static LanguagePack CurrentLanguage { get; private set; }
        public static CommandLineOptions CommandLineOptions { get; private set; }
#nullable enable


        public static int Main(string[] argv)
        {
            ConsoleState state = ConsoleExtensions.SaveConsoleState();
            int code = 0;

            try
            {
                Console.OutputEncoding = Encoding.Unicode;
                Console.InputEncoding = Encoding.Unicode;
                // Console.BackgroundColor = ConsoleColor.Black;
                ConsoleExtensions.RGBForegroundColor = RGBAColor.White;

                Parser.Default.ParseArguments<CommandLineOptions>(argv).WithParsed(opt =>
                {
                    CommandLineOptions = opt;

                    if (LanguageLoader.LanguagePacks.TryGetValue(opt.Language.ToLower(), out LanguagePack? lang))
                        CurrentLanguage = lang;
                    else
                    {
                        code = -1;
                        PrintError($"Unknown language pack '{opt.Language}'. Available languages: '{string.Join("', '", LanguageLoader.LanguagePacks.Values.Select(p => p.LanguageCode))}'");

                        return;
                    }

                    PrintBanner();
                    PrintInterpreterMessage(CurrentLanguage["general.langpack_found", LanguageLoader.LanguagePacks.Count]);
                    PrintInterpreterMessage(CurrentLanguage["general.loaded_langpack", CurrentLanguage]);

                    Task.Factory.StartNew(PrinterTask);

                    InterpreterResult result = Interpreter.Run(opt);

                    if (result.OptionalError is { } err)
                        PrintError($"{CurrentLanguage["error.error_in", err.Location ?? SourceLocation.Unknown]}:\n    {err.Message}");

                    code = result.ProgramExitCode;
                }).WithNotParsed(errs => code = -1);
            }
            catch (Exception ex)
            when (!Debugger.IsAttached)
            {
                code = ex.HResult;

                PrintException(ex);
            }
            finally
            {
                _isrunning = false;

                ConsoleExtensions.RestoreConsoleState(state);
            }

            return code;
        }

        private static async Task PrinterTask()
        {
            while (_isrunning)
                if (_msg_queue.TryDequeue(out (string p, string m, DateTime t) elem))
                {
                    ConsoleExtensions.RGBForegroundColor = RGBAColor.DarkGray;
                    Console.Write('[');
                    ConsoleExtensions.RGBForegroundColor = RGBAColor.Gray;
                    Console.Write(elem.t.ToString("HH:mm:ss.fff"));
                    ConsoleExtensions.RGBForegroundColor = RGBAColor.DarkGray;
                    Console.Write("][");
                    ConsoleExtensions.RGBForegroundColor = RGBAColor.DarkCyan;
                    Console.Write(elem.p);
                    ConsoleExtensions.RGBForegroundColor = RGBAColor.DarkGray;
                    Console.Write("]  ");
                    ConsoleExtensions.RGBForegroundColor = RGBAColor.Cyan;
                    Console.WriteLine(elem.m);
                    ConsoleExtensions.RGBForegroundColor = RGBAColor.White;
                }
                else
                    await Task.Delay(50);
        }

        private static void SubmitPrint(Verbosity min_lvl, string prefix, string msg)
        {
            if (CommandLineOptions.Verbosity < min_lvl)
                return;

            _msg_queue.Enqueue((prefix, msg, DateTime.Now));
        }

        internal static void PrintInterpreterMessage(string message) => SubmitPrint(Verbosity.n, "Interpreter", message);

        internal static void PrintDebugMessage(string message) => SubmitPrint(Verbosity.v, "Interpreter-Debug", message);

        internal static void PrintScriptMessage(FileInfo script, string message)
        {
            if (CommandLineOptions.Verbosity < Verbosity.n)
                Console.WriteLine(message);
            else
                SubmitPrint(Verbosity.n, script.Name, message);
        }

        internal static void PrintException(this Exception? ex)
        {
            StringBuilder sb = new StringBuilder();

            while (ex is { })
            {
                sb.Insert(0, $"[{ex.GetType()}] \"{ex.Message}\":\n{ex.StackTrace}\n");
                ex = ex.InnerException;
            }

            PrintError(sb.ToString());
        }

        internal static void PrintError(this string message)
        {
            _isrunning = false;
            _msg_queue.Clear();

            ConsoleExtensions.RGBForegroundColor = RGBAColor.White;
            Console.WriteLine('\n' + new string('_', Console.WindowWidth - 1));
            ConsoleExtensions.RGBForegroundColor = RGBAColor.Orange;

            if (CommandLineOptions.Verbosity > Verbosity.n)
            {
                Console.WriteLine(@"
                               ____
                       __,-~~/~    `---.
                     _/_,---(      ,    )
                 __ /        <    /   )  \___
  - ------===;;;'====------------------===;;;===----- -  -
                    \/  ~:~'~^~'~ ~\~'~)~^/
                    (_ (   \  (     >    \)
                     \_( _ <         >_>'
                        ~ `-i' ::>|--`'
                            I;|.|.|
                            | |: :|`
                         .-=||  | |=-.       ___  ____  ____  __  ___  __
                         `-=#$%&%$#=-'      / _ )/ __ \/ __ \/  |/  / / /
                           .| ;  :|        / _  / /_/ / /_/ / /|_/ / /_/
                          (`^':`-'.)      /____/\____/\____/_/  /_/ (_)
______________________.,-#%&$@#&@%#&#~,.___________________________________");
                ConsoleExtensions.RGBForegroundColor = RGBAColor.Yellow;
                Console.WriteLine("            AW SHIT -- THE INTERPRETER JUST BLEW UP!\n");
            }

            ConsoleExtensions.RGBForegroundColor = RGBAColor.Salmon;
            Console.WriteLine(message.TrimEnd());
            ConsoleExtensions.RGBForegroundColor = RGBAColor.White;
            Console.WriteLine(new string('_', Console.WindowWidth - 1));
        }

        private static void PrintBanner()
        {
            if (CommandLineOptions.HideBanner || CommandLineOptions.Verbosity < Verbosity.n)
                return;

            ConsoleExtensions.RGBForegroundColor = RGBAColor.White;
            Console.WriteLine($@"
                        _       _____ _   ____
             /\        | |     |_   _| | |___ \
            /  \  _   _| |_ ___  | | | |_  __) |
           / /\ \| | | | __/ _ \ | | | __||__ <
          / ____ \ |_| | || (_) || |_| |_ ___) |
         /_/    \_\__,_|\__\___/_____|\__|____/
  _____       _                           _
 |_   _|     | |                         | |
   | |  _ __ | |_ ___ _ __ _ __  _ __ ___| |_ ___ _ __
   | | | '_ \| __/ _ \ '__| '_ \| '__/ _ \ __/ _ \ '__|
  _| |_| | | | ||  __/ |  | |_) | | |  __/ ||  __/ |
 |_____|_| |_|\__\___|_|  | .__/|_|  \___|\__\___|_|
                          | |
                          |_|  {CurrentLanguage["banner.written_by", "Unknown6656, 2020"]}
{CurrentLanguage["banner.version"]} v.{Module.InterpreterVersion} ({Module.GitHash})
");
            ConsoleExtensions.RGBForegroundColor = RGBAColor.Crimson;
            Console.Write("    ");
            ConsoleExtensions.WriteUnderlined("WARNING!");
            ConsoleExtensions.RGBForegroundColor = RGBAColor.Salmon;
            Console.WriteLine(" This may panic your CPU.\n");
        }
    }

    public static class Module
    {
        public static Version? InterpreterVersion { get; }
        public static string GitHash { get; }


        static Module()
        {
            string[] lines = Properties.Resources.version.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToArray(s => s.Trim());

            InterpreterVersion = Version.TryParse(lines[0], out Version? v) ? v : null;
            GitHash = lines.Length > 0 ? lines[1] : "";

            if ((GitHash?.Length ?? 0) == 0)
                GitHash = "<unknown git commit hash>";
        }
    }

    public enum Verbosity
    {
        q,
        n,
        v,
    }
}
