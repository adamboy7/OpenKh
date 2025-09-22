using OpenKh.Kh2;
using OpenKh.Kh2.Models;
using OpenKh.Tools.Common.Wpf;
using System;
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

            var group = new Model3DGroup();
            foreach (var skeletalGroup in model.Groups)
            {
                var geometry = CreateMesh(skeletalGroup);
                var material = CreateMaterial(skeletalGroup, texture);
                var model3D = new GeometryModel3D(geometry, material)
                {
                    BackMaterial = material
                };
                model3D.Freeze();
                group.Children.Add(model3D);
            }

            if (!group.Children.Any())
            {
                return MdlxPreviewResult.Fail("The MDLX did not produce any renderable geometry.");
            }

            group.Freeze();
            var status = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0} • {1} mesh(es)",
                Path.GetFileName(path),
                group.Children.Count);
            return MdlxPreviewResult.Success(group, status);
        }
        catch (Exception ex)
        {
            return MdlxPreviewResult.Fail(ex.Message);
        }
    }

    private static MeshGeometry3D CreateMesh(ModelSkeletal.SkeletalGroup group)
    {
        var geometry = new MeshGeometry3D();
        var positions = new Point3DCollection(group.Mesh.Vertices.Count);
        var normals = new Vector3DCollection(group.Mesh.Vertices.Count);
        var textureCoordinates = new PointCollection(group.Mesh.Vertices.Count);

        foreach (var vertex in group.Mesh.Vertices)
        {
            positions.Add(new Point3D(vertex.Position.X, vertex.Position.Y, vertex.Position.Z));
            if (vertex.Normal != null)
            {
                normals.Add(new Vector3D(vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z));
            }
            else
            {
                normals.Add(new Vector3D());
            }

            textureCoordinates.Add(new Point(vertex.U / 4096.0f, vertex.V / 4096.0f));
        }

        geometry.Positions = positions;
        geometry.Normals = normals;
        geometry.TextureCoordinates = textureCoordinates;

        var triangleIndices = new Int32Collection(group.Mesh.Triangles.Sum(triangle => triangle.Count));
        foreach (var triangle in group.Mesh.Triangles)
        {
            foreach (var index in triangle)
            {
                triangleIndices.Add(index);
            }
        }

        geometry.TriangleIndices = triangleIndices;
        geometry.Freeze();
        return geometry;
    }

    private static Material CreateMaterial(ModelSkeletal.SkeletalGroup group, ModelTexture? texture)
    {
        if (texture != null)
        {
            try
            {
                var textureIndex = (int)group.Header.TextureIndex;
                if (textureIndex >= 0 && textureIndex < texture.Images.Count)
                {
                    var image = texture.Images[textureIndex].GetBimapSource();
                    var brush = new ImageBrush(image)
                    {
                        Stretch = Stretch.Uniform
                    };
                    brush.Freeze();
                    var material = new DiffuseMaterial(brush);
                    material.Freeze();
                    return material;
                }
            }
            catch
            {
                // Fallback to solid color below.
            }
        }

        var solid = new SolidColorBrush(Color.FromRgb(200, 200, 200));
        solid.Freeze();
        var fallback = new DiffuseMaterial(solid);
        fallback.Freeze();
        return fallback;
    }
}

internal sealed record MdlxPreviewResult(Model3DGroup? Model, string StatusMessage)
{
    public static MdlxPreviewResult Success(Model3DGroup model, string status) => new(model, status);

    public static MdlxPreviewResult Fail(string message) => new(null, message);
}
