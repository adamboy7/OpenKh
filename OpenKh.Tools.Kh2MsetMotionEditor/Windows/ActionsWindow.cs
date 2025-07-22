using OpenKh.Tools.Kh2MsetMotionEditor.Helpers;
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
            });

            return true;
        }
    }
}
