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

        public static bool Run(Camera camera, CameraLockOptions locks)
        {
            ForEdit("Lock X rotation", () => locks.LockRotX, x => locks.LockRotX = x);
            ForEdit("Lock Z rotation", () => locks.LockRotZ, x => locks.LockRotZ = x);
            ForEdit("Lock X position", () => locks.LockPosX, x => locks.LockPosX = x);
            ForEdit("Lock Y position", () => locks.LockPosY, x => locks.LockPosY = x);
            ForEdit("Lock Z position", () => locks.LockPosZ, x => locks.LockPosZ = x);

            var posBefore = camera.CameraPosition;
            var rotBefore = camera.CameraRotationYawPitchRoll;

            var posVec = new Vector3(posBefore.X, posBefore.Y, posBefore.Z);
            var posChanged = ImGui.DragFloat3("Position", ref posVec, 1.0f);
            if (posChanged)
                camera.CameraPosition = posVec;

            var rotVec = new Vector2(-rotBefore.X, -rotBefore.Z);
            var rotChanged = ImGui.DragFloat2("Rotation", ref rotVec, 1.0f);
            if (rotChanged)
                camera.CameraRotationYawPitchRoll = new Vector3(-rotVec.X, rotBefore.Y, -rotVec.Y);

            var posAfter = camera.CameraPosition;
            if (locks.LockPosX && !(posChanged && posVec.X != posBefore.X))
                posAfter.X = posBefore.X;
            if (locks.LockPosY && !(posChanged && posVec.Y != posBefore.Y))
                posAfter.Y = posBefore.Y;
            if (locks.LockPosZ && !(posChanged && posVec.Z != posBefore.Z))
                posAfter.Z = posBefore.Z;
            camera.CameraPosition = posAfter;

            var rotAfter = camera.CameraRotationYawPitchRoll;
            if (locks.LockRotX && !(rotChanged && rotVec.X != -rotBefore.X))
                rotAfter.X = rotBefore.X;
            if (locks.LockRotZ && !(rotChanged && rotVec.Y != -rotBefore.Z))
                rotAfter.Z = rotBefore.Z;
            camera.CameraRotationYawPitchRoll = rotAfter;

            return true;
        }
    }
}
