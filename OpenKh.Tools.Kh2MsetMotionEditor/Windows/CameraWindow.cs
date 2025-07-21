using ImGuiNET;
using OpenKh.Engine;
using OpenKh.Tools.Kh2MsetMotionEditor.Helpers;
using System.Numerics;
using static OpenKh.Tools.Common.CustomImGui.ImGuiEx;

namespace OpenKh.Tools.Kh2MsetMotionEditor.Windows
{
    static class CameraWindow
    {
        public static bool Run(Camera camera) => Run(camera, new CameraLockOptions());

        public static bool Run(Camera camera, CameraLockOptions locks) => ForHeader("Camera", () =>
        {
            ForEdit("Lock X rotation", () => locks.LockRotX, x => locks.LockRotX = x);
            ForEdit("Lock Y rotation", () => locks.LockRotY, x => locks.LockRotY = x);
            ForEdit("Lock X position", () => locks.LockPosX, x => locks.LockPosX = x);
            ForEdit("Lock Y position", () => locks.LockPosY, x => locks.LockPosY = x);
            ForEdit("Lock Z position", () => locks.LockPosZ, x => locks.LockPosZ = x);

            var posBefore = camera.CameraPosition;
            ForEdit3("Position", () => camera.CameraPosition, x => camera.CameraPosition = x);
            var rotBefore = camera.CameraRotationYawPitchRoll;
            ForEdit2("Rotation",
                () => new Vector2(-camera.CameraRotationYawPitchRoll.X, -camera.CameraRotationYawPitchRoll.Z),
                x => camera.CameraRotationYawPitchRoll = new Vector3(
                    -x.X, camera.CameraRotationYawPitchRoll.Y, -x.Y));

            var posAfter = camera.CameraPosition;
            if (locks.LockPosX) posAfter.X = posBefore.X;
            if (locks.LockPosY) posAfter.Y = posBefore.Y;
            if (locks.LockPosZ) posAfter.Z = posBefore.Z;
            camera.CameraPosition = posAfter;

            var rotAfter = camera.CameraRotationYawPitchRoll;
            if (locks.LockRotX) rotAfter.X = rotBefore.X;
            if (locks.LockRotY) rotAfter.Y = rotBefore.Y;
            camera.CameraRotationYawPitchRoll = rotAfter;
        });
    }
}
