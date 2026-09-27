// The boundary between input devices and MenUI.
//
// A pointer source turns whatever device it handles (desktop head ray and
// mouse, VR hands, a laser pointer, ...) into device-independent samples:
// where on the panel each pointer is, in UV, and whether its primary button
// is held. MenUI does everything else — hit testing, :hover/:active, pointer
// capture, events — in InputRouter, which never sees a device.
//
// To support a new device, implement IPointerSource and install it:
//
//   public class MyMenu : MenUIApp
//   {
//       protected override IPointerSource CreatePointerSource() { return new HandPointerSource(); }
//   }
//
// or at any time with `UI.SetPointerSource(...)`. The source is compiled into
// the panel's program like the rest of MenUI (a delegate or interface cannot
// cross between Udon programs).

using UnityEngine;

namespace MenUI
{
    /// One pointer's state for this frame.
    public sealed class PointerSample
    {
        /// Stable per pointer (e.g. 0 desktop, 1 left hand, 2 right hand).
        public int PointerId;
        /// False when the pointer does not touch the panel this frame.
        public bool IsOverPanel;
        /// Position on the panel: (0, 0) bottom-left, (1, 1) top-right.
        public float U;
        public float V;
        /// Primary button (click / trigger) held.
        public bool PrimaryPressed;
        /// Scroll this frame, in logical pixels (positive: content moves up).
        public float ScrollDelta;
        /// Distance from the pointer's origin to the panel, in meters.
        public float Distance;
    }

    public interface IPointerSource
    {
        /// Called once when the source is installed on a panel.
        void Attach(PanelSurface surface);

        /// Writes this frame's samples into `samples` and returns how many
        /// were written (at most `samples.Length`).
        int Poll(PointerSample[] samples);
    }

    /// The panel as a surface in the world, for pointer sources.
    public sealed class PanelSurface
    {
        private readonly Transform display;

        internal PanelSurface(Transform display)
        {
            this.display = display;
        }

        /// The quad the panel is shown on (a unit quad, scaled to size).
        public Transform Display { get { return display; } }

        /// The panel's size in logical pixels (what the quad shows).
        public float LogicalWidth { get; internal set; }
        public float LogicalHeight { get; internal set; }

        /// Intersects a world-space ray with the panel. Hits from either side
        /// count; returns false when the ray misses the panel's rectangle.
        public bool Raycast(Vector3 origin, Vector3 direction, out float u, out float v, out float distance)
        {
            u = 0f;
            v = 0f;
            distance = 0f;
            Vector3 normal = display.forward;
            float facing = Vector3.Dot(direction, normal);
            if (facing > -1e-5f && facing < 1e-5f)
            {
                return false;
            }
            float t = Vector3.Dot(display.position - origin, normal) / facing;
            if (t < 0f)
            {
                return false;
            }
            Vector3 local = display.InverseTransformPoint(origin + direction * t);
            u = local.x + 0.5f;
            v = local.y + 0.5f;
            distance = t;
            return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
        }
    }
}
