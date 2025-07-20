using McMaster.Extensions.CommandLineUtils;
using OpenKh.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Reflection;
using Xe.BinaryMapper;

[Command("scd-extract")]
[VersionOptionFromMember("--version", MemberName = nameof(GetVersion))]
class Program
{
    static int Main(string[] args)
    {
        try
        {
            return CommandLineApplication.Execute<Program>(args);
        }
        catch (Exception e)
        {
            Console.WriteLine($"ERROR: {e.Message}");
            return -1;
        }
    }

    [Required]
    [FileExists]
    [Argument(0, Description = "Input SCD file")] 
    public string Input { get; set; }

    [Option(CommandOptionType.SingleValue, ShortName = "o", LongName = "output", Description = "Output directory")] 
    public string Output { get; set; }

    private int OnExecute()
    {
        if (string.IsNullOrEmpty(Output))
        {
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(Input);
            Output = Path.Combine(Path.GetDirectoryName(Input) ?? string.Empty, fileNameWithoutExt);
        }
        Directory.CreateDirectory(Output);

        using var stream = File.OpenRead(Input);
        var tracks = ScdFile.Read(stream);

        for (int i = 0; i < tracks.Count; i++)
        {
            var outFile = Path.Combine(Output, $"track_{i:D3}.bin");
            File.WriteAllBytes(outFile, tracks[i]);
            Console.WriteLine($"Extracted {outFile}");
        }

        return 0;
    }

    private static string GetVersion() =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;

    private class ScdFile
    {
        public class Header
        {
            [Data] public ulong MagicCode { get; set; }
            [Data] public uint FileVersion { get; set; }
            [Data] public byte Endianness { get; set; }
            [Data] public byte SscfVersion { get; set; }
            [Data] public ushort HeaderSize { get; set; }
            [Data] public uint TotalFileSize { get; set; }
            [Data(Count = 7)] public uint[] Padding { get; set; }
        }

        public class TableOffsetHeader
        {
            [Data] public ushort Table0OffsetSize { get; set; }
            [Data] public ushort SoundEntryCount { get; set; }
            [Data] public ushort Table2OffsetSize { get; set; }
            [Data] public ushort Unknown06 { get; set; }
            [Data] public uint Table0Offset { get; set; }
            [Data] public uint Table1Offset { get; set; }
            [Data] public uint Table2Offset { get; set; }
            [Data] public uint Unknown14 { get; set; }
            [Data] public uint Unknown18 { get; set; }
            [Data] public uint Padding { get; set; }
        }

        public class StreamHeader
        {
            [Data] public uint StreamSize { get; set; }
            [Data] public uint ChannelCount { get; set; }
            [Data] public uint SampleRate { get; set; }
            [Data] public uint Codec { get; set; }
            [Data] public uint LoopStart { get; set; }
            [Data] public uint LoopEnd { get; set; }
            [Data] public uint ExtraDataSize { get; set; }
            [Data] public uint AuxChunkCount { get; set; }
        }

        public static List<byte[]> Read(Stream stream)
        {
            var header = BinaryMapping.ReadObject<Header>(stream);
            if (header.MagicCode != 0x4643535342444553ul)
                throw new InvalidDataException("Not a valid SCD file");

            var offsetHeader = BinaryMapping.ReadObject<TableOffsetHeader>(stream);
            stream.Seek(offsetHeader.Table1Offset, SeekOrigin.Begin);
            var offsets = new uint[offsetHeader.SoundEntryCount];
            for (int i = 0; i < offsets.Length; i++)
                offsets[i] = stream.ReadUInt32();

            var tracks = new List<byte[]>();
            foreach (var off in offsets)
            {
                stream.Seek(off, SeekOrigin.Begin);
                var info = BinaryMapping.ReadObject<StreamHeader>(stream);
                tracks.Add(stream.ReadBytes((int)info.StreamSize));
            }

            return tracks;
        }
    }
}
