using OpenKh.Command.SpawnPointExplorer.Utils;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OpenKh.Command.SpawnPointExplorer.Views
{
    public partial class MdlxViewportControl : UserControl
    {
        private static Point _leftPreviousPosition = new();
        private static Point _leftCurrentPosition = new();
        private static Point _rightPreviousPosition = new();
        private static Point _rightCurrentPosition = new();

        public Viewport3D Viewport { get; set; }
        public PerspectiveCamera VPCamera { get; set; }
        public List<GeometryModel3D> VPMeshes { get; set; }
        public Point3D AnchorPoint { get; set; }
        public Point3D AnchorPointTemp { get; set; }
        public Vector3D AnchorPointHorVec { get; set; }
        public Vector3D AnchorPointVerVec { get; set; }
        public bool AnchorPointLocked { get; set; }

        public MdlxViewportControl()
        {
            InitializeComponent();
        }

        public MdlxViewportControl(List<GeometryModel3D> vpMeshes, PerspectiveCamera? vpCamera = null)
        {
            InitializeComponent();
            Viewport = new Viewport3D();
            var boundingBox = getBoundingBox(vpMeshes);

            VPCamera = vpCamera ?? Viewport3DUtils.getCameraByBoundingBox(boundingBox);
            Viewport.Camera = VPCamera;
            AnchorPoint = new Point3D();
            AnchorPointTemp = new Point3D();
            AnchorPointLocked = false;

            var modelGroup = new Model3DGroup();
            modelGroup.Children.Add(new AmbientLight(Brushes.White.Color));

            VPMeshes = vpMeshes;
            foreach (var mesh in VPMeshes)
            {
                modelGroup.Children.Add(mesh);
            }

            var visual = new ModelVisual3D { Content = modelGroup };
            Viewport.Children.Add(visual);

            viewportFrame.Content = Viewport;
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            const double scale = 0.3;
            var position = VPCamera.Position;
            var lookVector = Viewport3DUtils.getVectorToTarget(VPCamera.Position, AnchorPoint);
            var length = lookVector.Length;
            lookVector.Normalize();

            if (e.Delta > 0)
            {
                lookVector *= length * scale;
            }
            else if (e.Delta < 0)
            {
                lookVector *= length * (-scale);
            }

            VPCamera.Position = new Point3D(position.X + lookVector.X, position.Y + lookVector.Y, position.Z + lookVector.Z);
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed)
            {
                _rightCurrentPosition = e.GetPosition(viewportFrame);

                if (_rightPreviousPosition != _rightCurrentPosition)
                {
                    moveCamera(_rightPreviousPosition, _rightCurrentPosition);
                }
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                _leftCurrentPosition = e.GetPosition(viewportFrame);

                if (_leftPreviousPosition != _leftCurrentPosition)
                {
                    rotateCamera(_leftCurrentPosition.X - _leftPreviousPosition.X, _leftCurrentPosition.Y - _leftPreviousPosition.Y, 0);
                }
            }

            _rightPreviousPosition = e.GetPosition(viewportFrame);
            _leftPreviousPosition = e.GetPosition(viewportFrame);
        }

        private void Viewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            AnchorPointLocked = true;
            AnchorPointTemp = new Point3D(AnchorPoint.X, AnchorPoint.Y, AnchorPoint.Z);

            var position = VPCamera.Position;
            AnchorPointHorVec = getHorizontalPerpendicularVector(new Vector3D(position.X - AnchorPoint.X, position.Y - AnchorPoint.Y, position.Z - AnchorPoint.Z));
            AnchorPointVerVec = getVerticalPerpendicularVector(new Vector3D(position.X - AnchorPoint.X, position.Y - AnchorPoint.Y, position.Z - AnchorPoint.Z), AnchorPointHorVec);
        }

        private void Viewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            AnchorPointLocked = false;
            AnchorPoint = new Point3D(AnchorPointTemp.X, AnchorPointTemp.Y, AnchorPointTemp.Z);
        }

        public void moveCamera(Point pre, Point cur)
        {
            var position = VPCamera.Position;
            var positionVector = new Vector3D(VPCamera.Position.X, VPCamera.Position.Y, VPCamera.Position.Z);
            var speed = positionVector.Length / 600;

            var moveHorVec = AnchorPointHorVec * (cur.X - pre.X) * speed;
            var moveVerVec = AnchorPointVerVec * (cur.Y - pre.Y) * speed;

            VPCamera.Position = new Point3D(position.X - moveHorVec.X, position.Y + moveVerVec.Y, position.Z - moveHorVec.Z);
            AnchorPointTemp = new Point3D(AnchorPointTemp.X - moveHorVec.X, AnchorPointTemp.Y + moveVerVec.Y, AnchorPointTemp.Z - moveHorVec.Z);
        }

        public void rotateCamera(Point pre, Point cur)
        {
            var position = VPCamera.Position;
            const double speed = 1;

            VPCamera.Position = new Point3D(position.X - (cur.X - pre.X) * speed, position.Y + (cur.Y - pre.Y) * speed, position.Z);
        }

        private void rotateCamera(double rX, double rY, double rZ)
        {
            var vector = new Vector3D(VPCamera.Position.X - AnchorPoint.X, VPCamera.Position.Y - AnchorPoint.Y, VPCamera.Position.Z - AnchorPoint.Z);

            var length = vector.Length;
            var theta = Math.Acos(vector.Y / length);
            var phi = Math.Atan2(-vector.Z, vector.X);

            theta -= rY * 0.01;
            phi -= rX * 0.01;
            length *= 1.0 - 0.1 * rZ;

            theta = Math.Clamp(theta, 0.0001, Math.PI - 0.0001);

            vector.X = length * Math.Sin(theta) * Math.Cos(phi);
            vector.Z = -length * Math.Sin(theta) * Math.Sin(phi);
            vector.Y = length * Math.Cos(theta);

            VPCamera.Position = new Point3D(AnchorPoint.X + vector.X, AnchorPoint.Y + vector.Y, AnchorPoint.Z + vector.Z);
            VPCamera.LookDirection = Viewport3DUtils.getVectorToTarget(VPCamera.Position, AnchorPoint);
        }

        private Vector3D getHorizontalPerpendicularVector(Vector3D vector)
        {
            var perpendicularVector = new Vector3D(vector.Z, 0, -vector.X);
            if (perpendicularVector.X != 0 || perpendicularVector.Y != 0 || perpendicularVector.Z != 0)
            {
                perpendicularVector.Normalize();
            }

            return perpendicularVector;
        }

        private Vector3D getVerticalPerpendicularVector(Vector3D cameraVector, Vector3D horizontalVector)
        {
            var perpendicularVector = Vector3D.CrossProduct(cameraVector, horizontalVector);
            if (perpendicularVector.X != 0 || perpendicularVector.Y != 0 || perpendicularVector.Z != 0)
            {
                perpendicularVector.Normalize();
            }

            return perpendicularVector;
        }

        private Rect3D getBoundingBox(List<GeometryModel3D> vpMeshes)
        {
            var boundingBox = new Rect3D();
            float minX = 0;
            float maxX = 0;
            float minY = 0;
            float maxY = 0;
            float minZ = 0;
            float maxZ = 0;
            foreach (var mesh in vpMeshes)
            {
                var localMinX = (float)(mesh.Geometry.Bounds.Location.X - mesh.Geometry.Bounds.SizeX);
                var localMaxX = (float)(mesh.Geometry.Bounds.Location.X + mesh.Geometry.Bounds.SizeX);
                var localMinY = (float)(mesh.Geometry.Bounds.Location.Y - mesh.Geometry.Bounds.SizeY);
                var localMaxY = (float)(mesh.Geometry.Bounds.Location.Y + mesh.Geometry.Bounds.SizeY);
                var localMinZ = (float)(mesh.Geometry.Bounds.Location.Z - mesh.Geometry.Bounds.SizeZ);
                var localMaxZ = (float)(mesh.Geometry.Bounds.Location.Z + mesh.Geometry.Bounds.SizeZ);

                if (localMinX < minX)
                {
                    minX = localMinX;
                }
                if (localMaxX > maxX)
                {
                    maxX = localMaxX;
                }
                if (localMinY < minY)
                {
                    minY = localMinY;
                }
                if (localMaxY > maxY)
                {
                    maxY = localMaxY;
                }
                if (localMinZ < minZ)
                {
                    minZ = localMinZ;
                }
                if (localMaxZ > maxZ)
                {
                    maxZ = localMaxZ;
                }
            }

            boundingBox.SizeX = Math.Abs(maxX - minX);
            boundingBox.SizeY = Math.Abs(maxY - minY);
            boundingBox.SizeZ = Math.Abs(maxZ - minZ);

            var X = minX + (boundingBox.SizeX / 2);
            var Y = minY + (boundingBox.SizeY / 2);
            var Z = minZ + (boundingBox.SizeZ / 2);

            boundingBox.Location = new Point3D(X, Y, Z);

            return boundingBox;
        }
    }
}
