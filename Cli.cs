using System;

namespace LngTool;

internal static class Cli
{
    private const string AppName = "ObsCureTextEditorCLI";
    private const string DefaultEncoding = "utf-8";

    public static int Run(string[] args)
    {
        try
        {
            return RunCore(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 2;
        }
    }

    private static int RunCore(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        return command switch
        {
            "extract" => Extract(args),
            "rebuild" => Rebuild(args),
            "help" => HelpCommand(args),
            _ => UnknownCommand(command)
        };
    }

    private static int Extract(string[] args)
    {
        if (args.Length < 4 || IsHelp(args[1]))
        {
            Console.Error.WriteLine($"Usage: {AppName} extract <file.lng> <out.txt> <--game ob1|ob2|fe>");
            return 1;
        }

        string lngPath = args[1];
        string txtPath = args[2];
        GameType? game = null;

        for (int i = 3; i < args.Length; i++)
        {
            string arg = args[i];
            if (ReadOptionValue(args, ref i, "--game", out string? value))
                game = ParseGame(value);
            else
                throw new ArgumentException($"Unknown option '{arg}'.");
        }

        GameType actualGame = game ?? throw new InvalidOperationException("Missing required option --game ob1|ob2|fe.");
        int count = actualGame switch
        {
            GameType.Obscure1 => ObscureLng.ExtractOb1ToTxt(lngPath, txtPath),
            GameType.Obscure2 => ObscureLng.ExtractOb2ToTxt(lngPath, txtPath, DefaultEncoding),
            GameType.FinalExam => ObscureLng.ExtractFinalExamToTxt(lngPath, txtPath),
            _ => throw new InvalidOperationException("Unknown game type. Pass --game ob1, --game ob2, or --game fe.")
        };

        Console.WriteLine($"OK: extracted {count} entries to {txtPath}");
        return 0;
    }

    private static int Rebuild(string[] args)
    {
        if (args.Length < 4 || IsHelp(args[1]))
        {
            Console.Error.WriteLine($"Usage: {AppName} rebuild <file.txt> <out.lng> <--game ob1|ob2|fe>");
            return 1;
        }

        string txtPath = args[1];
        string lngPath = args[2];
        GameType? game = null;

        for (int i = 3; i < args.Length; i++)
        {
            string arg = args[i];
            if (ReadOptionValue(args, ref i, "--game", out string? value))
                game = ParseGame(value);
            else
                throw new ArgumentException($"Unknown option '{arg}'.");
        }

        GameType actualGame = game ?? throw new InvalidOperationException("Missing required option --game ob1|ob2|fe.");
        switch (actualGame)
        {
            case GameType.Obscure1:
                ObscureLng.RebuildOb1FromTxt(txtPath, lngPath);
                break;
            case GameType.Obscure2:
                ObscureLng.RebuildOb2FromTxt(txtPath, lngPath, DefaultEncoding, addNullTerminator: false);
                break;
            case GameType.FinalExam:
                ObscureLng.RebuildFinalExamFromTxt(txtPath, lngPath);
                break;
        }

        Console.WriteLine($"OK: rebuilt {lngPath}");
        return 0;
    }

    private static int HelpCommand(string[] args)
    {
        if (args.Length == 1)
        {
            PrintHelp();
            return 0;
        }

        string command = args[1].ToLowerInvariant();
        Console.Error.WriteLine(command switch
        {
            "extract" => $"Usage: {AppName} extract <file.lng> <out.txt> <--game ob1|ob2|fe>",
            "rebuild" => $"Usage: {AppName} rebuild <file.txt> <out.lng> <--game ob1|ob2|fe>",
            _ => "Unknown help topic."
        });
        return command is "extract" or "rebuild" ? 0 : 1;
    }

    private static GameType ParseGame(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "ob1" or "obscure1" => GameType.Obscure1,
            "ob2" or "obscure2" => GameType.Obscure2,
            "fe" or "finalexam" or "final-exam" => GameType.FinalExam,
            _ => throw new ArgumentException($"Unknown game '{value}'. Use ob1, ob2, or fe.")
        };
    }

    private static bool ReadOptionValue(string[] args, ref int index, string option, out string value)
    {
        value = "";
        string arg = args[index];
        if (!arg.Equals(option, StringComparison.OrdinalIgnoreCase))
            return false;

        if (index + 1 >= args.Length)
            throw new ArgumentException($"Missing value for {option}.");

        value = args[++index];
        return true;
    }

    private static bool IsHelp(string arg)
    {
        return arg is "-h" or "--help" or "/?";
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        Console.Error.WriteLine($"Use '{AppName} --help' for usage.");
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine($"""
ObsCure Text Editor CLI by HeitorSpectre

Usage:
  {AppName} extract <file.lng> <out.txt> <--game ob1|ob2|fe>
  {AppName} rebuild <file.txt> <out.lng> <--game ob1|ob2|fe>
""");
    }
}
