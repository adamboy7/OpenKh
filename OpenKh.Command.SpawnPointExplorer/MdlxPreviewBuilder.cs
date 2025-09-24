using OpenKh.Command.SpawnPointExplorer.Utils;
using OpenKh.Kh2;
using OpenKh.Kh2.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OpenKh.Command.SpawnPointExplorer;

internal static class MdlxPreviewBuilder
{
    public static MdlxPreviewResult Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (!Bar.IsValid(stream))
            {
                return MdlxPreviewResult.Fail("The selected file is not a valid MDLX archive.");
            }

            var bar = Bar.Read(stream);
            ModelSkeletal? model = null;
            ModelTexture? texture = null;

            foreach (var entry in bar)
            {
                entry.Stream.Position = 0;
                switch (entry.Type)
                {
                    case Bar.EntryType.Model:
                        model = ModelSkeletal.Read(entry.Stream);
                        break;
                    case Bar.EntryType.ModelTexture:
                        texture = ModelTexture.Read(entry.Stream);
                        break;
                }
            }

            foreach (var entry in bar)
            {
                entry.Stream?.Dispose();
            }

            if (model == null)
            {
                return MdlxPreviewResult.Fail("The MDLX does not contain model data.");
            }

            model.recalculateMeshes();
            var meshes = Viewport3DUtils.getGeometryFromModel(model, texture);
            if (!meshes.Any())
            {
                return MdlxPreviewResult.Fail("The MDLX did not produce any renderable geometry.");
            }

            FreezeMeshes(meshes);

            var status = string.Format(
                CultureInfo.InvariantCulture,
                "{0} • {1} mesh(es)",
                Path.GetFileName(path),
                meshes.Count);

            return MdlxPreviewResult.Success(meshes, status);
        }
        catch (Exception ex)
        {
            return MdlxPreviewResult.Fail(ex.Message);
        }
    }

    private static void FreezeMeshes(IEnumerable<GeometryModel3D> meshes)
    {
        foreach (var mesh in meshes)
        {
            if (mesh.Geometry is Freezable geometry)
            {
                TryFreeze(geometry);
            }

            if (mesh.Material is DiffuseMaterial diffuse)
            {
                FreezeDiffuseMaterial(diffuse);
            }
            else if (mesh.Material is Freezable material)
            {
                TryFreeze(material);
            }

            if (mesh.BackMaterial is Freezable backMaterial)
            {
                TryFreeze(backMaterial);
            }

            TryFreeze(mesh);
        }
    }

    private static void FreezeDiffuseMaterial(DiffuseMaterial material)
    {
        if (material.Brush is ImageBrush imageBrush)
        {
            TryFreeze(imageBrush.ImageSource as Freezable);
            TryFreeze(imageBrush);
        }
        else
        {
            TryFreeze(material.Brush as Freezable);
        }

        TryFreeze(material);
    }

    private static void TryFreeze(Freezable? freezable)
    {
        if (freezable != null && freezable.CanFreeze && !freezable.IsFrozen)
        {
            freezable.Freeze();
        }
    }
}

internal sealed record MdlxPreviewResult(IReadOnlyList<GeometryModel3D>? Meshes, string StatusMessage)
{
    public static MdlxPreviewResult Success(IReadOnlyList<GeometryModel3D> meshes, string status) => new(meshes, status);

    public static MdlxPreviewResult Fail(string message) => new(null, message);
}
