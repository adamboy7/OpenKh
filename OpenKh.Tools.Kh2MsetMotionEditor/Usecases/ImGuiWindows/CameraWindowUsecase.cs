using OpenKh.Engine;
using OpenKh.Tools.Kh2MsetMotionEditor.Helpers;
using OpenKh.Tools.Kh2MsetMotionEditor.Interfaces;
using System;
using static OpenKh.Tools.Common.CustomImGui.ImGuiEx;

namespace OpenKh.Tools.Kh2MsetMotionEditor.Usecases.ImGuiWindows
{
    public class CameraWindowUsecase : IWindowRunnableProvider
    {
        private readonly Camera _camera;
        private readonly Settings _settings;
        private readonly CameraLockOptions _locks;

        public CameraWindowUsecase(Settings settings, Camera camera, CameraLockOptions locks)
        {
            _settings = settings;
            _camera = camera;
            _locks = locks;
        }

        public Action CreateWindowRunnable()
        {
            return () =>
            {
                if (_settings.ViewCamera)
                {
                    var closed = !ForWindow("Camera", () => CameraWindow.Run(_camera, _locks));
                    if (closed)
                    {
                        _settings.ViewCamera = false;
                        _settings.Save();
                    }
                }
            };
        }
    }
}
