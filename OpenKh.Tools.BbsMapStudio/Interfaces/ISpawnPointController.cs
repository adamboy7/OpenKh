using OpenKh.Tools.BbsMapStudio.Models;
using System.Collections.Generic;
using System.Numerics;

namespace OpenKh.Tools.BbsMapStudio.Interfaces
{
    interface ISpawnPointController
    {
        List<SpawnPointModel> SpawnPoints { get; }
        SpawnPointModel CurrentSpawnPoint { get; }
        string SelectSpawnPoint { get; set; }
        void TeleportCameraTo(Vector3 position);
    }
}
