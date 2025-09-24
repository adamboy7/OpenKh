using OpenKh.Kh2;
using OpenKh.Kh2.Models;
using OpenKh.Tools.Common.Wpf;
using System;
using System.Collections.Generic;
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
        var vertexCount = group.Mesh.Vertices.Count;
        var positions = new Point3D[vertexCount];
        var normals = new Vector3D[vertexCount];
        var hasNormals = new bool[vertexCount];
        var textureCoordinates = new Point[vertexCount];

        for (var index = 0; index < vertexCount; index++)
        {
            var vertex = group.Mesh.Vertices[index];
            positions[index] = new Point3D(vertex.Position.X, vertex.Position.Y, vertex.Position.Z);
            textureCoordinates[index] = new Point(vertex.U / 4096.0f, vertex.V / 4096.0f);

            if (vertex.Normal == null)
            {
                continue;
            }

            var normal = new Vector3D(vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z);
            if (normal.LengthSquared <= double.Epsilon)
            {
                continue;
            }

            normal.Normalize();
            normals[index] = normal;
            hasNormals[index] = true;
        }

        var triangleIndexList = new List<int>(group.Mesh.Triangles.Sum(triangle => triangle.Count));
        foreach (var triangle in group.Mesh.Triangles)
        {
            foreach (var vertexIndex in triangle)
            {
                triangleIndexList.Add(vertexIndex);
            }
        }

        if (hasNormals.Any(hasNormal => !hasNormal))
        {
            var computedNormals = new Vector3D[vertexCount];
            var fallbackNormals = new Vector3D[vertexCount];
            var hasFallbackNormal = new bool[vertexCount];

            for (var i = 0; i <= triangleIndexList.Count - 3; i += 3)
            {
                var i0 = triangleIndexList[i];
                var i1 = triangleIndexList[i + 1];
                var i2 = triangleIndexList[i + 2];

                if (i0 < 0 || i0 >= vertexCount ||
                    i1 < 0 || i1 >= vertexCount ||
                    i2 < 0 || i2 >= vertexCount)
                {
                    continue;
                }

                var p0 = positions[i0];
                var p1 = positions[i1];
                var p2 = positions[i2];

                var edge1 = p1 - p0;
                var edge2 = p2 - p0;
                var faceNormal = Vector3D.CrossProduct(edge1, edge2);

                if (faceNormal.LengthSquared <= double.Epsilon)
                {
                    continue;
                }

                faceNormal.Normalize();
                computedNormals[i0] += faceNormal;
                computedNormals[i1] += faceNormal;
                computedNormals[i2] += faceNormal;

                fallbackNormals[i0] = faceNormal;
                fallbackNormals[i1] = faceNormal;
                fallbackNormals[i2] = faceNormal;
                hasFallbackNormal[i0] = true;
                hasFallbackNormal[i1] = true;
                hasFallbackNormal[i2] = true;
            }

            for (var index = 0; index < vertexCount; index++)
            {
                if (hasNormals[index])
                {
                    continue;
                }

                var normal = computedNormals[index];
                if (normal.LengthSquared <= double.Epsilon)
                {
                    if (!hasFallbackNormal[index])
                    {
                        continue;
                    }

                    normal = fallbackNormals[index];
                }

                normal.Normalize();
                normals[index] = normal;
            }
        }

        var geometry = new MeshGeometry3D
        {
            Positions = new Point3DCollection(positions),
            Normals = new Vector3DCollection(normals),
            TextureCoordinates = new PointCollection(textureCoordinates),
            TriangleIndices = new Int32Collection(triangleIndexList)
        };

        geometry.Freeze();
        return geometry;
    }

    private static Material CreateMaterial(ModelSkeletal.SkeletalGroup group, ModelTexture? texture)
    {
        Brush? brush = null;

        if (texture != null)
        {
            try
            {
                var textureIndex = (int)group.Header.TextureIndex;
                if (textureIndex >= 0 && textureIndex < texture.Images.Count)
                {
                    var image = texture.Images[textureIndex].GetBimapSource();
                    brush = new ImageBrush(image)
                    {
                        Stretch = Stretch.Uniform
                    };
                }
            }
            catch
            {
                // Fallback to solid color below.
            }
        }

        brush ??= new SolidColorBrush(Color.FromRgb(200, 200, 200));
        brush.Freeze();

        var diffuse = new DiffuseMaterial(brush);
        diffuse.Freeze();

        return diffuse;
    }
}

internal sealed record MdlxPreviewResult(Model3DGroup? Model, string StatusMessage)
{
    public static MdlxPreviewResult Success(Model3DGroup model, string status) => new(model, status);

    public static MdlxPreviewResult Fail(string message) => new(null, message);
}
