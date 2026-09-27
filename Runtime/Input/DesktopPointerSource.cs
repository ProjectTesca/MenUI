// The minimal pointer source: desktop mode only. The pointer is where the
// player looks (VRChat's desktop reticle is the screen center), the left
// mouse button presses, the wheel scrolls. In VR it reports nothing — VR
// input is a separate source.

using UnityEngine;
using VRC.SDKBase;

namespace MenUI
{
    public sealed class DesktopPointerSource : IPointerSource
    {
        private const float WheelPixelsPerNotch = 48f;
        private PanelSurface surface;

        public void Attach(PanelSurface surface)
        {
            this.surface = surface;
        }

        public int Poll(PointerSample[] samples)
        {
            VRCPlayerApi player = Networking.LocalPlayer;
            if (player == null || player.IsUserInVR() || samples.Length == 0)
            {
                return 0;
            }
            VRCPlayerApi.TrackingData head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 direction = head.rotation * Vector3.forward;

            PointerSample sample = samples[0];
            sample.PointerId = 0;
            float u, v, distance;
            sample.IsOverPanel = surface.Raycast(head.position, direction, out u, out v, out distance);
            sample.U = u;
            sample.V = v;
            sample.Distance = distance;
            sample.PrimaryPressed = Input.GetMouseButton(0);
            // The wheel axis is positive away from the user (scroll up): content moves down.
            sample.ScrollDelta = -Input.GetAxis("Mouse ScrollWheel") * 10f * WheelPixelsPerNotch;
            return 1;
        }
    }
}
