using McMaster.Extensions.CommandLineUtils;
using System;
using System.IO;
using System.Reflection;
using System.ComponentModel.DataAnnotations;
using OpenKh.Kh2;
using OpenKh.AssimpUtils;
using Assimp;

namespace OpenKh.Command.MdlxToFbx
{
    [Command("OpenKh.Command.MdlxToFbx")]
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
                Console.WriteLine($"FATAL ERROR: {e.Message}\n{e.StackTrace}");
                return -1;
            }
        }

        private static string GetVersion() => typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

        [Required]
        [FileExists]
        [Argument(0, "MDLX file", "The .mdlx file to convert")] 
        public string InputFile { get; } = string.Empty;

        [Argument(1, "Output path", "Optional output FBX file")] 
        public string? OutputFile { get; } = null;

        private int OnExecute()
        {
            try
            {
                Convert(InputFile, OutputFile);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                return -1;
            }
        }

        private static void Convert(string mdlxPath, string? outputFile)
        {
            using var stream = File.OpenRead(mdlxPath);
            var bar = Bar.Read(stream);

            ModelSkeletal? model = null;
            ModelTexture? textures = null;

            foreach (var entry in bar)
            {
                if (entry.Type == Bar.EntryType.Model)
                    model = ModelSkeletal.Read(entry.Stream);
                else if (entry.Type == Bar.EntryType.ModelTexture)
                    textures = ModelTexture.Read(entry.Stream);
            }

            if (model == null)
                throw new Exception("Model entry not found in MDLX file.");

            var scene = Kh2MdlxAssimp.getAssimpScene(model);

            // Fix texture names to include .png extension
            if (textures != null)
            {
                for (int i = 0; i < scene.Materials.Count; i++)
                {
                    if (i < textures.Images.Count)
                        scene.Materials[i].TextureDiffuse.FilePath = $"Texture{i:D4}.png";
                }
            }

            // Export FBX
            string outFbx = outputFile ?? Path.ChangeExtension(mdlxPath, ".fbx");
            using (var ctx = new AssimpContext())
                ctx.ExportFile(scene, outFbx, "fbx");

            // Export textures
            if (textures != null)
            {
                string outDir = Path.GetDirectoryName(outFbx) ?? string.Empty;
                for (int i = 0; i < textures.Images.Count; i++)
                {
                    string texPath = Path.Combine(outDir, $"Texture{i:D4}.png");
                    using var texStream = File.Create(texPath);
                    Imaging.PngImage.Write(texStream, textures.Images[i]);
                }
            }
        }
    }
}
