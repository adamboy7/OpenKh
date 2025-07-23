using OpenKh.Tools.Kh2MsetMotionEditor.Helpers;
using ImGuiNET;
using static OpenKh.Tools.Common.CustomImGui.ImGuiEx;

namespace OpenKh.Tools.Kh2MsetMotionEditor.Windows
{
    static class ActionsWindow
    {
        public static bool Run(CameraLockOptions locks)
        {
            ForHeader("Actions", () =>
            {
                ForEdit("Follow root bone", () => locks.FollowRootBone, x => locks.FollowRootBone = x);
                ForEdit("Maintain distance", () => locks.MaintainDistance, x => locks.MaintainDistance = x);
                ForEdit("Distance", () => locks.Distance, x => locks.Distance = x);
                if (ImGui.Button("Enable orbital camera"))
                {
                    locks.FollowRootBone = true;
                    locks.MaintainDistance = true;
                }
            });

            return true;
        }
    }
}
