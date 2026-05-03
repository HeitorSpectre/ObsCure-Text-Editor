using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LngTool;

public enum GameType
{
    Unknown,
    Obscure1,
    Obscure2,
    FinalExam
}

public static class ObscureLng
{
    public static GameType Detect(string lngPath)
    {
        using var fs = File.OpenRead(lngPath);
        Span<byte> head = stackalloc byte[8];
        if (fs.Read(head) < 8) return GameType.Unknown;

        uint aBe = BinaryPrimitives.ReadUInt32BigEndian(head);
        uint bBe = BinaryPrimitives.ReadUInt32BigEndian(head[4..]);
        uint aLe = BinaryPrimitives.ReadUInt32LittleEndian(head);
        uint bLe = BinaryPrimitives.ReadUInt32LittleEndian(head[4..]);

        // Final Exam: v1=1, magic in 0x01_??_00_00 family (e.g. 0x01400000, 0x01800000)
        if (aLe == 1 && (bLe & 0xFF00FFFFu) == 0x01000000u) return GameType.FinalExam;
        if (aBe == 0 && bBe >= 1 && bBe <= 100000) return GameType.Obscure1;
        if (bLe >= 1 && bLe <= 10000) return GameType.Obscure2;
        return GameType.Unknown;
    }

    // ---------------- OBSCURE 2 ----------------

    public static int ExtractOb2ToTxt(string lngPath, string txtPath, string encodingName = "utf-8")
    {
        var enc = Encoding.GetEncoding(encodingName);
        var sb = new StringBuilder();
        int entryTotal = 0;

        using (var fs = File.OpenRead(lngPath))
        using (var br = new BinaryReader(fs))
        {
            uint languageCode = br.ReadUInt32();
            uint groupCount = br.ReadUInt32();

            sb.Append("### LANGUAGE\n");
            sb.Append("game = ob2\n");
            sb.Append($"languageCode = {languageCode}\n");
            sb.Append("###\n\n");

            for (int g = 0; g < groupCount; g++)
            {
                uint groupId = br.ReadUInt32();
                uint entryCount = br.ReadUInt32();

                for (int e = 0; e < entryCount; e++)
                {
                    uint meta = br.ReadUInt32();
                    uint length = br.ReadUInt32();
                    string text = "";
                    if (length > 0)
                    {
                        byte[] raw = br.ReadBytes((int)length);
                        text = enc.GetString(raw).Replace("\n", "\\n");
                    }

                    sb.Append("### ENTRY\n");
                    sb.Append($"group_index = {g}\n");
                    sb.Append($"group_id = {groupId}\n");
                    sb.Append($"entry_index = {e}\n");
                    sb.Append($"meta = {meta}\n");
                    sb.Append("###\n");
                    sb.Append(text);
                    sb.Append("\n\n");
                    entryTotal++;
                }
            }
        }

        File.WriteAllText(txtPath, sb.ToString(), new UTF8Encoding(false));
        return entryTotal;
    }

    public static void RebuildOb2FromTxt(string txtPath, string outLngPath, string encodingName = "utf-8", bool addNullTerminator = false)
    {
        var enc = Encoding.GetEncoding(encodingName);
        string content = File.ReadAllText(txtPath, new UTF8Encoding(false));

        var (header, entries) = ParseTxt(content);

        uint languageCode = 0;
        if (header.TryGetValue("languageCode", out var lc))
            languageCode = uint.Parse(lc);

        // groups[group_index] -> (group_id, dict of entry_index -> (meta, text))
        var groups = new SortedDictionary<int, (uint groupId, SortedDictionary<int, (uint meta, string text)> entries)>();

        foreach (var entry in entries)
        {
            int gi = int.Parse(entry.Header["group_index"]);
            uint gid = uint.Parse(entry.Header["group_id"]);
            int ei = int.Parse(entry.Header["entry_index"]);
            uint meta = uint.Parse(entry.Header["meta"]);
            string text = entry.Body.Replace("\\n", "\n");

            if (!groups.TryGetValue(gi, out var grp))
            {
                grp = (gid, new SortedDictionary<int, (uint, string)>());
                groups[gi] = grp;
            }
            grp.entries[ei] = (meta, text);
        }

        int groupCount = groups.Count == 0 ? 0 : groups.Keys.Max() + 1;

        using var fs = File.Create(outLngPath);
        using var bw = new BinaryWriter(fs);
        bw.Write(languageCode);
        bw.Write((uint)groupCount);

        for (int g = 0; g < groupCount; g++)
        {
            uint gid;
            int entryCount;
            SortedDictionary<int, (uint meta, string text)>? entryDict = null;

            if (groups.TryGetValue(g, out var grp))
            {
                gid = grp.groupId;
                entryDict = grp.entries;
                entryCount = entryDict.Count == 0 ? 0 : entryDict.Keys.Max() + 1;
            }
            else
            {
                gid = 0;
                entryCount = 0;
            }

            bw.Write(gid);
            bw.Write((uint)entryCount);

            for (int e = 0; e < entryCount; e++)
            {
                uint meta;
                byte[] data;

                if (entryDict != null && entryDict.TryGetValue(e, out var entry))
                {
                    meta = entry.meta;
                    data = enc.GetBytes(entry.text);
                    if (addNullTerminator && (data.Length == 0 || data[^1] != 0))
                    {
                        var withNull = new byte[data.Length + 1];
                        Array.Copy(data, withNull, data.Length);
                        data = withNull;
                    }
                }
                else
                {
                    meta = 0;
                    data = Array.Empty<byte>();
                }

                bw.Write(meta);
                bw.Write((uint)data.Length);
                if (data.Length > 0) bw.Write(data);
            }
        }
    }

    // ---------------- OBSCURE 1 ----------------

    // Visual button-tag mapping for OB1 (TXT only — reverted on rebuild).
    // Source may be multi-char (e.g. × + variation selector). Longer keys
    // are matched first to keep replacement deterministic.
    private static readonly (string Source, string Tag)[] Ob1ButtonTags = SortTagsByLengthDesc(new[]
    {
        ("÷",       "[L1]"),
        ("Æ",       "[R1]"),
        ("Ð",       "[Triangle]"),
        ("Ã",       "[X]"),
        ("Ø",       "[Select]"),
        ("Â",       "[Circle]"),
        ("À",       "[Square]"),
        ("Ç",       "[R2]"),
        ("Å",       "[Start]"),
        ("×︃", "[L2]"),
        ("Ê",       "[L3]"),
        ("õ",       "[R3]"),
    });

    private static (string, string)[] SortTagsByLengthDesc((string, string)[] arr)
    {
        Array.Sort(arr, (a, b) => b.Item1.Length.CompareTo(a.Item1.Length));
        return arr;
    }

    private static string ApplyOb1Tags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var (src, tag) in Ob1ButtonTags)
            text = text.Replace(src, tag);
        return text;
    }

    private static string ReverseOb1Tags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var (src, tag) in Ob1ButtonTags)
            text = text.Replace(tag, src);
        return text;
    }

    public static int ExtractOb1ToTxt(string lngPath, string txtPath)
    {
        var sb = new StringBuilder();
        int entryTotal = 0;
        var cp1252 = Encoding.GetEncoding(1252);
        var utf16le = Encoding.Unicode;

        using var fs = File.OpenRead(lngPath);
        using var br = new BinaryReader(fs);

        uint languageCode = ReadU32Be(br);
        uint entryCount = ReadU32Be(br);

        sb.Append("### LANGUAGE\n");
        sb.Append("game = ob1\n");
        sb.Append($"languageCode = {languageCode}\n");
        sb.Append("###\n\n");

        for (int i = 0; i < entryCount; i++)
        {
            ushort group = ReadU16Be(br);
            ushort id = ReadU16Be(br);
            uint textLen = ReadU32Be(br);
            byte enc = br.ReadByte();
            int bodyLen = (int)textLen - 1;
            byte? param = null;

            if (enc == 1)
            {
                param = br.ReadByte();
                bodyLen -= 1;
            }

            byte[] body = bodyLen > 0 ? br.ReadBytes(bodyLen) : Array.Empty<byte>();

            string text;
            if (enc == 0)
            {
                // strip trailing 0x00
                int len = body.Length;
                while (len > 0 && body[len - 1] == 0) len--;
                text = cp1252.GetString(body, 0, len);
            }
            else if (enc == 1)
            {
                // strip trailing 0x0000
                int byteLen = body.Length;
                if (byteLen % 2 != 0) byteLen--; // safety
                while (byteLen >= 2 && body[byteLen - 1] == 0 && body[byteLen - 2] == 0)
                    byteLen -= 2;
                text = utf16le.GetString(body, 0, byteLen);
            }
            else
            {
                // unknown encoding: keep raw as base64-like hex fallback
                text = "[RAW:" + Convert.ToHexString(body) + "]";
            }

            text = ApplyOb1Tags(text);
            text = text.Replace("\n", "\\n").Replace("\r", "\\r");

            sb.Append("### ENTRY\n");
            sb.Append($"index = {i}\n");
            sb.Append($"group = {group}\n");
            sb.Append($"id = {id}\n");
            sb.Append($"encoding = {enc}\n");
            if (param.HasValue) sb.Append($"param = {param.Value}\n");
            sb.Append("###\n");
            sb.Append(text);
            sb.Append("\n\n");
            entryTotal++;
        }

        File.WriteAllText(txtPath, sb.ToString(), new UTF8Encoding(false));
        return entryTotal;
    }

    public static void RebuildOb1FromTxt(string txtPath, string outLngPath)
    {
        string content = File.ReadAllText(txtPath, new UTF8Encoding(false));
        var (header, entries) = ParseTxt(content);

        uint languageCode = 0;
        if (header.TryGetValue("languageCode", out var lc))
            languageCode = uint.Parse(lc);

        var ordered = entries
            .Select(e => new
            {
                Index = int.Parse(e.Header["index"]),
                Group = ushort.Parse(e.Header["group"]),
                Id = ushort.Parse(e.Header["id"]),
                Encoding = byte.Parse(e.Header["encoding"]),
                Param = e.Header.TryGetValue("param", out var p) ? (byte?)byte.Parse(p) : null,
                Text = ReverseOb1Tags(e.Body.Replace("\\r", "\r").Replace("\\n", "\n"))
            })
            .OrderBy(x => x.Index)
            .ToList();

        var cp1252 = Encoding.GetEncoding(1252);
        var utf16le = Encoding.Unicode;

        using var fs = File.Create(outLngPath);
        using var bw = new BinaryWriter(fs);
        WriteU32Be(bw, languageCode);
        WriteU32Be(bw, (uint)ordered.Count);

        foreach (var e in ordered)
        {
            byte[] body;
            if (e.Encoding == 0)
            {
                byte[] data = cp1252.GetBytes(e.Text);
                body = new byte[data.Length + 1];
                Array.Copy(data, body, data.Length);
                body[^1] = 0;
            }
            else if (e.Encoding == 1)
            {
                byte[] data = utf16le.GetBytes(e.Text);
                body = new byte[data.Length + 2];
                Array.Copy(data, body, data.Length);
                // null terminator already 0,0
            }
            else
            {
                body = Array.Empty<byte>();
            }

            uint textLen = (uint)(1 + (e.Encoding == 1 ? 1 : 0) + body.Length);
            WriteU16Be(bw, e.Group);
            WriteU16Be(bw, e.Id);
            WriteU32Be(bw, textLen);
            bw.Write(e.Encoding);
            if (e.Encoding == 1)
                bw.Write(e.Param ?? (byte)0);
            if (body.Length > 0) bw.Write(body);
        }
    }

    // ---------------- TXT PARSER ----------------

    private record struct ParsedEntry(Dictionary<string, string> Header, string Body);

    private static (Dictionary<string, string> Header, List<ParsedEntry> Entries) ParseTxt(string content)
    {
        // Normalize line endings
        content = content.Replace("\r\n", "\n").Replace("\r", "\n");

        var headerDict = new Dictionary<string, string>();
        var entries = new List<ParsedEntry>();

        // Split: first segment is the LANGUAGE header, remainder are ENTRY segments.
        // We split on "### ENTRY\n" — but we also have "### LANGUAGE\n" at start.
        int firstEntryIdx = content.IndexOf("### ENTRY", StringComparison.Ordinal);
        string langPart;
        string rest;
        if (firstEntryIdx < 0)
        {
            langPart = content;
            rest = "";
        }
        else
        {
            langPart = content.Substring(0, firstEntryIdx);
            rest = content.Substring(firstEntryIdx);
        }

        // parse LANGUAGE keys
        foreach (var line in langPart.Split('\n'))
        {
            string l = line.Trim();
            if (l.Length == 0 || l.StartsWith("###")) continue;
            int eq = l.IndexOf('=');
            if (eq < 0) continue;
            string k = l.Substring(0, eq).Trim();
            string v = l.Substring(eq + 1).Trim();
            headerDict[k] = v;
        }

        // Split rest by "### ENTRY"
        var sections = rest.Split(new[] { "### ENTRY" }, StringSplitOptions.None);
        foreach (var sec in sections)
        {
            if (string.IsNullOrWhiteSpace(sec)) continue;

            // section now starts with metadata then "###" then body
            int closeIdx = sec.IndexOf("###", StringComparison.Ordinal);
            if (closeIdx < 0) continue;

            string headerPart = sec.Substring(0, closeIdx);
            string bodyPart = sec.Substring(closeIdx + 3);
            // bodyPart starts with newline; the entry text ends at the newline-newline before next entry
            // Strip exactly one leading \n (we wrote sb.Append("###\n") + body + "\n\n")
            if (bodyPart.StartsWith("\n")) bodyPart = bodyPart.Substring(1);
            // Remove trailing \n\n delimiter (or trailing whitespace) — but keep internal \n that the user may have added.
            // The producer wrote: body + "\n\n"; on round trip body ends with "\n\n".
            // We strip exactly the trailing two newlines if present, else one.
            if (bodyPart.EndsWith("\n\n")) bodyPart = bodyPart.Substring(0, bodyPart.Length - 2);
            else if (bodyPart.EndsWith("\n")) bodyPart = bodyPart.Substring(0, bodyPart.Length - 1);

            var entryHeader = new Dictionary<string, string>();
            foreach (var line in headerPart.Split('\n'))
            {
                string l = line.Trim();
                if (l.Length == 0) continue;
                int eq = l.IndexOf('=');
                if (eq < 0) continue;
                string k = l.Substring(0, eq).Trim();
                string v = l.Substring(eq + 1).Trim();
                entryHeader[k] = v;
            }
            entries.Add(new ParsedEntry(entryHeader, bodyPart));
        }

        return (headerDict, entries);
    }

    // ---------------- BE helpers ----------------

    private static uint ReadU32Be(BinaryReader br)
    {
        Span<byte> b = stackalloc byte[4];
        if (br.Read(b) < 4) throw new EndOfStreamException();
        return BinaryPrimitives.ReadUInt32BigEndian(b);
    }

    private static ushort ReadU16Be(BinaryReader br)
    {
        Span<byte> b = stackalloc byte[2];
        if (br.Read(b) < 2) throw new EndOfStreamException();
        return BinaryPrimitives.ReadUInt16BigEndian(b);
    }

    private static void WriteU32Be(BinaryWriter bw, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        bw.Write(b);
    }

    private static void WriteU16Be(BinaryWriter bw, ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, v);
        bw.Write(b);
    }

    // ---------------- FINAL EXAM ----------------
    //
    // File layout (all little-endian):
    //   Header (20 bytes):
    //     uint32 v1            (always 1)
    //     uint32 magic         (e.g. 0x01400000 / 0x01800000 — preserved verbatim)
    //     uint32 totalSubCount (sum of all sub-entry counts)
    //     uint32 entryCount    (# of entries in entry table)
    //     uint32 glyphCount    (# of UTF-32 chars in the font glyph table)
    //   Glyph table: glyphCount * uint32 (UTF-32 LE)
    //   Entry table: entryCount * variable-size record:
    //     uint32 sid           (string ID, e.g. 0x01402000)
    //     uint32 subCount
    //     subCount * (uint32 sub_a, uint32 sub_b)
    //         sub_a low 17 bits = byte offset into string data
    //         sub_a high 15 bits = tag/flags (preserved verbatim)
    //         sub_b is always 0 in observed files
    //   Data section:
    //     uint32 dataSize
    //     dataSize bytes of CP1252 null-terminated strings
    //
    // Strings in the data section are written sequentially in entry order,
    // so on rebuild we can recompute offsets from scratch and combine with
    // each sub's preserved tag.

    public static int ExtractFinalExamToTxt(string lngPath, string txtPath)
    {
        var sb = new StringBuilder();
        var stringEnc = new UTF8Encoding(false);
        int subTotal = 0;

        byte[] data = File.ReadAllBytes(lngPath);
        int pos = 0;

        uint v1        = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)); pos += 4;
        uint magic     = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)); pos += 4;
        uint totalSubs = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)); pos += 4;
        uint entryCnt  = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)); pos += 4;
        uint glyphCnt  = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)); pos += 4;

        var glyphs = new uint[glyphCnt];
        for (int i = 0; i < glyphCnt; i++)
        {
            glyphs[i] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
            pos += 4;
        }

        var entries = new List<(uint Sid, List<(uint Tag, int Offset)> Subs)>((int)entryCnt);
        for (int i = 0; i < entryCnt; i++)
        {
            uint sid = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)); pos += 4;
            uint cnt = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)); pos += 4;
            var subs = new List<(uint Tag, int Offset)>((int)cnt);
            for (int s = 0; s < cnt; s++)
            {
                uint a = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)); pos += 4;
                uint b = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)); pos += 4;
                int offset = (int)(a & 0x0001FFFFu);
                uint tag = a >> 17;
                subs.Add((tag, offset));
                subTotal++;
                _ = b; // observed always 0; preserved as 0 on rebuild
            }
            entries.Add((sid, subs));
        }

        uint dataSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)); pos += 4;
        byte[] strData = new byte[dataSize];
        Array.Copy(data, pos, strData, 0, (int)dataSize);

        sb.Append("### LANGUAGE\n");
        sb.Append("game = finalexam\n");
        sb.Append($"v1 = {v1}\n");
        sb.Append($"magic = 0x{magic:X8}\n");
        var glyphHex = new StringBuilder();
        for (int i = 0; i < glyphs.Length; i++)
        {
            if (i > 0) glyphHex.Append(',');
            glyphHex.Append("0x").Append(glyphs[i].ToString("X"));
        }
        sb.Append($"glyphs = {glyphHex}\n");
        sb.Append("###\n\n");

        for (int i = 0; i < entries.Count; i++)
        {
            var (sid, subs) = entries[i];
            sb.Append("### ENTRY\n");
            sb.Append($"index = {i}\n");
            sb.Append($"sid = 0x{sid:X8}\n");
            sb.Append("###\n");
            foreach (var (tag, offset) in subs)
            {
                int end = Array.IndexOf(strData, (byte)0, offset);
                if (end < 0) end = strData.Length;
                string text = stringEnc.GetString(strData, offset, end - offset)
                                    .Replace("\n", "\\n").Replace("\r", "\\r");
                sb.Append($"[tag=0x{tag:X4}] {text}\n");
            }
            sb.Append('\n');
        }

        File.WriteAllText(txtPath, sb.ToString(), new UTF8Encoding(false));
        return subTotal;
    }

    public static void RebuildFinalExamFromTxt(string txtPath, string outLngPath)
    {
        string content = File.ReadAllText(txtPath, new UTF8Encoding(false));
        var (header, entries) = ParseTxt(content);

        uint v1 = header.TryGetValue("v1", out var v) ? uint.Parse(v) : 1u;
        uint magic = ParseHexOrDec(header["magic"]);
        var glyphs = new List<uint>();
        if (header.TryGetValue("glyphs", out var gs))
        {
            foreach (var part in gs.Split(',', StringSplitOptions.RemoveEmptyEntries))
                glyphs.Add(ParseHexOrDec(part.Trim()));
        }

        // Parse entries — each body line begins with "[tag=0xNNNN] "
        var ordered = entries
            .Select(e => new
            {
                Index = int.Parse(e.Header["index"]),
                Sid = ParseHexOrDec(e.Header["sid"]),
                Subs = ParseFinalExamBody(e.Body)
            })
            .OrderBy(x => x.Index)
            .ToList();

        var stringEnc = new UTF8Encoding(false);

        // Build string data section sequentially.
        var dataStream = new MemoryStream();
        var subRecords = new List<(uint Sid, List<(uint Tag, int Offset)> Subs)>();
        foreach (var e in ordered)
        {
            var subs = new List<(uint Tag, int Offset)>();
            foreach (var (tag, text) in e.Subs)
            {
                int offset = (int)dataStream.Length;
                byte[] bytes = stringEnc.GetBytes(text);
                dataStream.Write(bytes, 0, bytes.Length);
                dataStream.WriteByte(0);
                subs.Add((tag, offset));
            }
            subRecords.Add((e.Sid, subs));
        }
        byte[] strData = dataStream.ToArray();
        uint totalSubs = (uint)subRecords.Sum(r => r.Subs.Count);

        using var fs = File.Create(outLngPath);
        using var bw = new BinaryWriter(fs);

        bw.Write(v1);
        bw.Write(magic);
        bw.Write(totalSubs);
        bw.Write((uint)subRecords.Count);
        bw.Write((uint)glyphs.Count);
        foreach (var g in glyphs) bw.Write(g);

        foreach (var (sid, subs) in subRecords)
        {
            bw.Write(sid);
            bw.Write((uint)subs.Count);
            foreach (var (tag, offset) in subs)
            {
                if (offset > 0x1FFFF)
                    throw new InvalidOperationException(
                        $"String offset {offset} exceeds 17-bit limit (sid 0x{sid:X8}). " +
                        "Total translated text is too large for the format.");
                uint a = ((tag & 0x7FFFu) << 17) | ((uint)offset & 0x0001FFFFu);
                bw.Write(a);
                bw.Write(0u);
            }
        }

        bw.Write((uint)strData.Length);
        bw.Write(strData);
    }

    private static List<(uint Tag, string Text)> ParseFinalExamBody(string body)
    {
        var result = new List<(uint, string)>();
        foreach (var rawLine in body.Split('\n'))
        {
            string line = rawLine;
            if (line.Length == 0) continue;
            // Match "[tag=0xNNNN] "
            if (!line.StartsWith("[tag=")) continue;
            int end = line.IndexOf(']');
            if (end < 0) continue;
            string tagStr = line.Substring(5, end - 5).Trim(); // skip "[tag="
            uint tag = ParseHexOrDec(tagStr);
            string text = line.Substring(end + 1);
            if (text.StartsWith(" ")) text = text.Substring(1);
            text = text.Replace("\\r", "\r").Replace("\\n", "\n");
            result.Add((tag, text));
        }
        return result;
    }

    private static uint ParseHexOrDec(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToUInt32(s.Substring(2), 16);
        return uint.Parse(s);
    }
}
