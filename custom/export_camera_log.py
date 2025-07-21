import bpy
import csv
import math


def export_camera_log(csv_path: str, camera_name: str = "Camera"):
    """Export camera keyframes to a CSV log compatible with the motion editor."""
    cam = bpy.data.objects.get(camera_name)
    if cam is None:
        print(f"Camera '{camera_name}' not found")
        return

    scene = bpy.context.scene
    frame_start = scene.frame_start
    frame_end = scene.frame_end

    with open(csv_path, "w", newline='') as csvfile:
        writer = csv.writer(csvfile)
        writer.writerow(["Time", "X", "Y", "Z", "Yaw", "Pitch", "Roll"])
        for frame in range(frame_start, frame_end + 1):
            scene.frame_set(frame)

            loc = cam.matrix_world.translation
            rot = cam.rotation_euler

            x = -loc.x
            y = loc.y
            z = loc.z
            yaw = math.degrees(rot.y)
            pitch = math.degrees(rot.x)
            roll = math.degrees(rot.z)

            writer.writerow([frame - frame_start, x, y, z, yaw, pitch, roll])


# Example usage:
# export_camera_log("/path/to/camera-log.csv")
