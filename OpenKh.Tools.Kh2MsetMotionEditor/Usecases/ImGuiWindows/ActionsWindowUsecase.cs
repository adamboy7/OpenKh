using OpenKh.Tools.Kh2MsetMotionEditor.Helpers;
using OpenKh.Tools.Kh2MsetMotionEditor.Interfaces;
using OpenKh.Tools.Kh2MsetMotionEditor.Windows;
using System;
using static OpenKh.Tools.Common.CustomImGui.ImGuiEx;

namespace OpenKh.Tools.Kh2MsetMotionEditor.Usecases.ImGuiWindows
{
    public class ActionsWindowUsecase : IWindowRunnableProvider
    {
        private readonly Settings _settings;
        private readonly CameraLockOptions _locks;

        public ActionsWindowUsecase(Settings settings, CameraLockOptions locks)
        {
            _settings = settings;
            _locks = locks;
        }

        public Action CreateWindowRunnable()
        {
            return () =>
            {
                if (_settings.ViewActions)
                {
                    var closed = !ForWindow("Actions", () => ActionsWindow.Run(_locks));
                    if (closed)
                    {
                        _settings.ViewActions = false;
                        _settings.Save();
                    }
                }
            };
        }
    }
}
