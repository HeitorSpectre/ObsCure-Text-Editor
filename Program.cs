using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace LngTool;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (args.Length > 0)
        {
            try
            {
                return RunCli(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex);
                return 2;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static int RunCli(string[] args)
    {
        string cmd = args[0].ToLowerInvariant();
        switch (cmd)
        {
            case "extract":
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: LngTool extract <file.lng> [out.txt] [--game ob1|ob2] [--encoding utf-8]");
                    return 1;
                }
                string lng = args[1];
                string? outTxt = null;
                GameType? forced = null;
                string encoding = "utf-8";
                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i] == "--game" && i + 1 < args.Length)
                        forced = args[++i] == "ob1" ? GameType.Obscure1 : GameType.Obscure2;
                    else if (args[i] == "--encoding" && i + 1 < args.Length)
                        encoding = args[++i];
                    else if (!args[i].StartsWith("--"))
                        outTxt = args[i];
                }
                outTxt ??= Path.ChangeExtension(lng, ".txt");

                GameType game = forced ?? ObscureLng.Detect(lng);
                int n = game switch
                {
                    GameType.Obscure1 => ObscureLng.ExtractOb1ToTxt(lng, outTxt),
                    GameType.Obscure2 => ObscureLng.ExtractOb2ToTxt(lng, outTxt, encoding),
                    _ => throw new InvalidOperationException("Unknown game type — pass --game ob1|ob2.")
                };
                Console.WriteLine($"OK — extracted {n} entries → {outTxt}");
                return 0;
            }
            case "rebuild":
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: LngTool rebuild <file.txt> <out.lng> [--game ob1|ob2] [--encoding utf-8] [--add-null]");
                    return 1;
                }
                string txt = args[1];
                string outLng = args[2];
                GameType? forced = null;
                string encoding = "utf-8";
                bool addNull = false;
                for (int i = 3; i < args.Length; i++)
                {
                    if (args[i] == "--game" && i + 1 < args.Length)
                        forced = args[++i] == "ob1" ? GameType.Obscure1 : GameType.Obscure2;
                    else if (args[i] == "--encoding" && i + 1 < args.Length)
                        encoding = args[++i];
                    else if (args[i] == "--add-null")
                        addNull = true;
                }

                GameType game = forced ?? GameType.Unknown;
                if (game == GameType.Unknown)
                {
                    string sniff = File.ReadAllText(txt);
                    if (sniff.Contains("game = ob1")) game = GameType.Obscure1;
                    else if (sniff.Contains("game = ob2")) game = GameType.Obscure2;
                }
                if (game == GameType.Unknown)
                    throw new InvalidOperationException("Could not determine game — pass --game ob1|ob2.");

                if (game == GameType.Obscure1)
                    ObscureLng.RebuildOb1FromTxt(txt, outLng);
                else
                    ObscureLng.RebuildOb2FromTxt(txt, outLng, encoding, addNull);

                Console.WriteLine($"OK — rebuilt → {outLng}");
                return 0;
            }
            case "roundtrip":
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: LngTool roundtrip <file.lng> [--game ob1|ob2] [--encoding utf-8]");
                    return 1;
                }
                string lng = args[1];
                GameType? forced = null;
                string encoding = "utf-8";
                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i] == "--game" && i + 1 < args.Length)
                        forced = args[++i] == "ob1" ? GameType.Obscure1 : GameType.Obscure2;
                    else if (args[i] == "--encoding" && i + 1 < args.Length)
                        encoding = args[++i];
                }
                GameType game = forced ?? ObscureLng.Detect(lng);
                string tmpTxt = Path.GetTempFileName();
                string tmpLng = Path.GetTempFileName();
                try
                {
                    if (game == GameType.Obscure1)
                    {
                        ObscureLng.ExtractOb1ToTxt(lng, tmpTxt);
                        ObscureLng.RebuildOb1FromTxt(tmpTxt, tmpLng);
                    }
                    else if (game == GameType.Obscure2)
                    {
                        ObscureLng.ExtractOb2ToTxt(lng, tmpTxt, encoding);
                        ObscureLng.RebuildOb2FromTxt(tmpTxt, tmpLng, encoding, false);
                    }
                    else
                    {
                        throw new InvalidOperationException("Unknown game.");
                    }

                    byte[] a = File.ReadAllBytes(lng);
                    byte[] b = File.ReadAllBytes(tmpLng);
                    bool eq = a.Length == b.Length;
                    int firstDiff = -1;
                    if (eq)
                    {
                        for (int i = 0; i < a.Length; i++)
                            if (a[i] != b[i]) { eq = false; firstDiff = i; break; }
                    }
                    Console.WriteLine($"{(eq ? "PASS" : "FAIL")} — {lng}");
                    Console.WriteLine($"  game={game}, srcSize={a.Length}, rebuiltSize={b.Length}");
                    if (!eq) Console.WriteLine($"  firstDiff offset = 0x{firstDiff:x}");
                    return eq ? 0 : 3;
                }
                finally
                {
                    if (File.Exists(tmpTxt)) File.Delete(tmpTxt);
                    if (File.Exists(tmpLng)) File.Delete(tmpLng);
                }
            }
            default:
                Console.Error.WriteLine("Unknown command. Use: extract | rebuild | roundtrip");
                return 1;
        }
    }
}
