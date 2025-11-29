using OpenKh.Kh2;
using OpenKh.Kh2.Models;
using OpenKh.Tools.Common.Wpf;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OpenKh.Command.SpawnPointExplorer.Utils
{
    internal static class Viewport3DUtils
    {
        public static PerspectiveCamera getDefaultCamera(int distance = 500)
        {
            var camera = new PerspectiveCamera
            {
                Position = new Point3D(0, 0, distance),
                LookDirection = new Vector3D(0, 0, -1),
                FieldOfView = 60,
            };

            return camera;
        }

        public static PerspectiveCamera getCameraByBoundingBox(Rect3D boundingBox)
        {
            var maxSize = boundingBox.SizeX > boundingBox.SizeY ? boundingBox.SizeX : boundingBox.SizeY;
            var camera = new PerspectiveCamera
            {
                Position = new Point3D(0, 0, maxSize * 1.2),
                LookDirection = new Vector3D(0, 0, -1),
                FieldOfView = 60,
            };

            return camera;
        }

        public static Vector3D getVectorToTarget(Point3D position, Point3D targetPosition = new())
        {
            var vector = new Vector3D(position.X, position.Y, position.Z);
            var targetVector = new Vector3D(targetPosition.X, targetPosition.Y, targetPosition.Z);
            return getVectorToTarget(vector, targetVector);
        }

        public static Vector3D getVectorToTarget(Vector3D position, Vector3D targetPosition = new())
        {
            return -(position - targetPosition);
        }

        public static GeometryModel3D getGeometryFromGroup(ModelSkeletal.SkeletalGroup group, ModelTexture? textureFile = null)
        {
            var geometryModel = new GeometryModel3D();
            var meshGeometry = new MeshGeometry3D();

            var positionCollection = new Point3DCollection();
            foreach (var vertex in group.Mesh.Vertices)
            {
                positionCollection.Add(new Point3D(vertex.Position.X, vertex.Position.Y, vertex.Position.Z));
            }
            meshGeometry.Positions = positionCollection;

            var textureCoordinatesCollection = new PointCollection();
            foreach (var vertex in group.Mesh.Vertices)
            {
                textureCoordinatesCollection.Add(new Point(vertex.U / 4096.0f, vertex.V / 4096.0f));
            }
            meshGeometry.TextureCoordinates = textureCoordinatesCollection;

            var triangleIndicesCollection = new Int32Collection();
            foreach (var triangle in group.Mesh.Triangles)
            {
                foreach (var triangleVertex in triangle)
                {
                    triangleIndicesCollection.Add(triangleVertex);
                }
            }
            meshGeometry.TriangleIndices = triangleIndicesCollection;

            geometryModel.Geometry = meshGeometry;

            DiffuseMaterial material;
            try
            {
                var textureIndex = (int)group.Header.TextureIndex;
                if (textureFile == null || textureFile.Images == null || textureFile.Images.Count < textureIndex)
                {
                    material = getDefaultMaterial();
                }
                else
                {
                    var texture = textureFile.Images[textureIndex];
                    ImageSource imageSource = texture.GetBimapSource();
                    material = new DiffuseMaterial(new ImageBrush(imageSource));
                }
            }
            catch
            {
                material = getDefaultMaterial();
            }

            geometryModel.Material = material;

            positionCollection.Add(new Point3D(0, 0, 0));
            positionCollection.Add(new Point3D(0, 0, 0));
            textureCoordinatesCollection.Add(new Point(0, 0));
            textureCoordinatesCollection.Add(new Point(1, 1));

            return geometryModel;
        }

        public static List<GeometryModel3D> getGeometryFromModel(ModelSkeletal modelFile, ModelTexture? textureFile = null)
        {
            var geometryList = new List<GeometryModel3D>();
            foreach (var group in modelFile.Groups)
            {
                geometryList.Add(getGeometryFromGroup(group, textureFile));
            }

            return geometryList;
        }

        public static void addTri(Int32Collection triangleIndicesCollection, int i1, int i2, int i3)
        {
            triangleIndicesCollection.Add(i1);
            triangleIndicesCollection.Add(i2);
            triangleIndicesCollection.Add(i3);
        }

        public static GeometryModel3D getCube(int size, Vector3D position, Color color)
        {
            var cube = getCube(size, position);
            cube.Material = new DiffuseMaterial(new SolidColorBrush(color));

            return cube;
        }

        public static GeometryModel3D getCube(int size, Vector3D position = new())
        {
            var meshGeometry = new MeshGeometry3D();
            var positionCollection = new Point3DCollection
            {
                new Point3D(position.X - size, position.Y - size, position.Z - size),
                new Point3D(position.X + size, position.Y - size, position.Z - size),
                new Point3D(position.X - size, position.Y + size, position.Z - size),
                new Point3D(position.X + size, position.Y + size, position.Z - size),
                new Point3D(position.X - size, position.Y - size, position.Z + size),
                new Point3D(position.X + size, position.Y - size, position.Z + size),
                new Point3D(position.X - size, position.Y + size, position.Z + size),
                new Point3D(position.X + size, position.Y + size, position.Z + size),
            };

            meshGeometry.Positions = positionCollection;

            var triangleIndicesCollection = new Int32Collection();

            addTri(triangleIndicesCollection, 2, 3, 1);
            addTri(triangleIndicesCollection, 2, 1, 0);
            addTri(triangleIndicesCollection, 7, 1, 3);
            addTri(triangleIndicesCollection, 7, 5, 1);
            addTri(triangleIndicesCollection, 6, 5, 7);
            addTri(triangleIndicesCollection, 6, 4, 5);
            addTri(triangleIndicesCollection, 2, 4, 6);
            addTri(triangleIndicesCollection, 2, 0, 4);
            addTri(triangleIndicesCollection, 2, 7, 3);
            addTri(triangleIndicesCollection, 2, 6, 7);
            addTri(triangleIndicesCollection, 0, 1, 5);
            addTri(triangleIndicesCollection, 0, 5, 4);

            meshGeometry.TriangleIndices = triangleIndicesCollection;

            var geometryModel = new GeometryModel3D
            {
                Geometry = meshGeometry,
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(40, 255, 0, 0))),
            };

            return geometryModel;
        }

        public static GeometryModel3D getCube(int radius, int height, Vector3D position, Color color)
        {
            var cube = getCube(radius, height, position);
            cube.Material = new DiffuseMaterial(new SolidColorBrush(color));

            return cube;
        }

        public static GeometryModel3D getCube(int radius, int height, Vector3D position = new())
        {
            var meshGeometry = new MeshGeometry3D();
            var positionCollection = new Point3DCollection
            {
                new Point3D(position.X - radius, position.Y - height, position.Z - radius),
                new Point3D(position.X + radius, position.Y - height, position.Z - radius),
                new Point3D(position.X - radius, position.Y + height, position.Z - radius),
                new Point3D(position.X + radius, position.Y + height, position.Z - radius),
                new Point3D(position.X - radius, position.Y - height, position.Z + radius),
                new Point3D(position.X + radius, position.Y - height, position.Z + radius),
                new Point3D(position.X - radius, position.Y + height, position.Z + radius),
                new Point3D(position.X + radius, position.Y + height, position.Z + radius),
            };

            meshGeometry.Positions = positionCollection;

            var triangleIndicesCollection = new Int32Collection();

            addTri(triangleIndicesCollection, 2, 3, 1);
            addTri(triangleIndicesCollection, 2, 1, 0);
            addTri(triangleIndicesCollection, 7, 1, 3);
            addTri(triangleIndicesCollection, 7, 5, 1);
            addTri(triangleIndicesCollection, 6, 5, 7);
            addTri(triangleIndicesCollection, 6, 4, 5);
            addTri(triangleIndicesCollection, 2, 4, 6);
            addTri(triangleIndicesCollection, 2, 0, 4);
            addTri(triangleIndicesCollection, 2, 7, 3);
            addTri(triangleIndicesCollection, 2, 6, 7);
            addTri(triangleIndicesCollection, 0, 1, 5);
            addTri(triangleIndicesCollection, 0, 5, 4);

            meshGeometry.TriangleIndices = triangleIndicesCollection;

            var geometryModel = new GeometryModel3D
            {
                Geometry = meshGeometry,
                Material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(100, 255, 0, 0))),
            };

            return geometryModel;
        }

        public static DiffuseMaterial getDefaultMaterial()
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
            };
            gradient.GradientStops.Add(new GradientStop(Colors.Yellow, 0.0));
            gradient.GradientStops.Add(new GradientStop(Colors.Red, 0.25));
            gradient.GradientStops.Add(new GradientStop(Colors.Blue, 0.75));
            gradient.GradientStops.Add(new GradientStop(Colors.LimeGreen, 1.0));

            return new DiffuseMaterial(gradient);
        }
    }
}
