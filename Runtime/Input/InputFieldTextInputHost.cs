// The default text input host: transparent uGUI InputFields laid over the
// panel, one on each text box a pointer can reach (see TextInput.cs).
//
// The player's click lands on the real InputField, which is what makes
// VRChat open its input UI (the desktop input window, or the VR keyboard);
// activating a field from code does not. What the player types is read back
// every frame and reported; MenUI draws the text.
//
// The fields live on a world-space canvas (with VRC Ui Shape) just in front
// of the panel and start inactive. The canvas is a child of the display quad,
// sized so that one canvas unit is one logical pixel of the quad: whatever
// moves, turns or resizes the quad (by hand in the editor, or at run time)
// carries the canvas along. Attach fits it again from the panel's own
// settings. The editor creates all of this when it sets a panel up.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MenUI
{
    public sealed class InputFieldTextInputHost : ITextInputHost
    {
        /// How far in front of the quad the fields are (meters, at the quad's
        /// scale): in front of the panel's collider, so pointers reach them first.
        public const float Distance = 0.002f;

        private readonly InputField[] fields;
        private readonly RectTransform[] transforms;
        private readonly RectTransform canvas;
        private readonly TextInputTarget[] boundTo;
        // The text last exchanged with each field, to tell who changed it.
        private readonly string[] exchanged;
        private readonly bool[] wasFocused;
        private readonly float[] placed;

        public InputFieldTextInputHost(InputField[] fields, RectTransform[] transforms, RectTransform canvas)
        {
            this.fields = fields;
            this.transforms = transforms;
            this.canvas = canvas;
            int count = fields.Length;
            boundTo = new TextInputTarget[count];
            exchanged = new string[count];
            wasFocused = new bool[count];
            placed = new float[count * 4];
            for (int i = 0; i < count; i++)
            {
                fields[i].gameObject.SetActive(false);
            }
        }

        public void Attach(PanelSurface surface)
        {
            if (canvas == null || surface.Display == null || surface.LogicalWidth <= 0f || surface.LogicalHeight <= 0f)
            {
                return;
            }
            // The quad is a unit square: scaling the canvas by one over the
            // logical size makes it cover the quad exactly.
            canvas.SetParent(surface.Display, false);
            canvas.localRotation = Quaternion.identity;
            canvas.localPosition = new Vector3(0f, 0f, -Distance);
            canvas.sizeDelta = new Vector2(surface.LogicalWidth, surface.LogicalHeight);
            canvas.localScale = new Vector3(1f / surface.LogicalWidth, 1f / surface.LogicalHeight, 1f);
        }

        public void Update(List<TextInputTarget> targets)
        {
            // Release fields whose box is no longer offered (never mid-edit).
            for (int i = 0; i < fields.Length; i++)
            {
                TextInputTarget bound = boundTo[i];
                if (bound != null && !targets.Contains(bound) && !fields[i].isFocused)
                {
                    Release(i);
                }
            }
            // Give each offered box a field.
            for (int t = 0; t < targets.Count; t++)
            {
                TextInputTarget target = targets[t];
                int index = FieldOf(target);
                if (index < 0)
                {
                    index = FieldOf(null);
                    if (index < 0)
                    {
                        continue;   // more boxes than fields: the rest wait
                    }
                    Bind(index, target);
                }
                Place(index, target);
            }
            // Exchange text and focus.
            for (int i = 0; i < fields.Length; i++)
            {
                TextInputTarget target = boundTo[i];
                if (target == null)
                {
                    continue;
                }
                InputField field = fields[i];
                bool focused = field.isFocused;
                string text = field.text;
                if (text != exchanged[i])
                {
                    exchanged[i] = text;
                    target.ReportText(text);
                }
                else if (!focused && target.Text != exchanged[i])
                {
                    // Changed by the program while not being edited.
                    exchanged[i] = target.Text;
                    field.text = exchanged[i];
                }
                if (focused != wasFocused[i])
                {
                    wasFocused[i] = focused;
                    target.ReportFocus(focused, !focused && field.wasCanceled);
                }
            }
        }

        private int FieldOf(TextInputTarget target)
        {
            for (int i = 0; i < boundTo.Length; i++)
            {
                if (boundTo[i] == target)
                {
                    return i;
                }
            }
            return -1;
        }

        private void Bind(int index, TextInputTarget target)
        {
            InputField field = fields[index];
            boundTo[index] = target;
            field.contentType = target.Password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            field.lineType = target.MultiLine ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            field.characterLimit = target.MaxLength;
            exchanged[index] = target.Text;
            field.text = exchanged[index];
            wasFocused[index] = false;
            for (int k = 0; k < 4; k++)
            {
                placed[index * 4 + k] = -1f;
            }
            field.gameObject.SetActive(true);
        }

        private void Release(int index)
        {
            TextInputTarget target = boundTo[index];
            boundTo[index] = null;
            fields[index].gameObject.SetActive(false);
            if (wasFocused[index])
            {
                wasFocused[index] = false;
                target.ReportFocus(false, false);
            }
        }

        private void Place(int index, TextInputTarget target)
        {
            int o = index * 4;
            if (placed[o] == target.Left && placed[o + 1] == target.Top
                && placed[o + 2] == target.Width && placed[o + 3] == target.Height)
            {
                return;
            }
            placed[o] = target.Left;
            placed[o + 1] = target.Top;
            placed[o + 2] = target.Width;
            placed[o + 3] = target.Height;
            RectTransform rect = transforms[index];
            rect.anchoredPosition = new Vector2(target.Left, -target.Top);
            rect.sizeDelta = new Vector2(target.Width, target.Height);
        }
    }
}
