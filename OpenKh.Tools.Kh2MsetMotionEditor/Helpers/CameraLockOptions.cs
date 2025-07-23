namespace OpenKh.Tools.Kh2MsetMotionEditor.Helpers
{
    public class CameraLockOptions
    {
        public bool LockRotX { get; set; }
        public bool LockRotY { get; set; }
        public bool LockRotZ { get; set; }
        public bool LockPosX { get; set; }
        public bool LockPosY { get; set; }
        public bool LockPosZ { get; set; }
        public bool FollowRootBone { get; set; }

        public bool MaintainDistance { get; set; }

        public float Distance { get; set; } = 5f;

        public bool ClampPitch { get; set; }
    }
}
