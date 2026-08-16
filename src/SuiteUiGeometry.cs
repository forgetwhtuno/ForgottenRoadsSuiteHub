using System;

namespace ErenshorSuiteHub
{
    // Unity-free geometry used by launcher/window code and deterministic tests. Keeping recovery
    // here prevents each panel from growing a slightly different off-screen clamp rule.
    internal struct SuiteRect
    {
        internal float X;
        internal float Y;
        internal float Width;
        internal float Height;

        internal SuiteRect(float x, float y, float width, float height)
        {
            X = x; Y = y; Width = width; Height = height;
        }
    }

    internal static class SuiteUiGeometry
    {
        internal const float LauncherGripWidth = 20f;
        internal const float LauncherHeight = 30f;
        internal const float HeaderHeight = 32f;

        internal static bool LauncherRegionsDoNotOverlap(float launcherWidth)
        {
            float buttonX = LauncherGripWidth;
            float buttonWidth = Math.Max(0f, launcherWidth - LauncherGripWidth);
            return buttonX >= LauncherGripWidth && buttonWidth >= 1f;
        }

        internal static SuiteRect ClampLauncher(SuiteRect r, float screenWidth, float screenHeight, float fixedWidth)
        {
            if (!Finite(r.X)) r.X = 0f;
            if (!Finite(r.Y)) r.Y = 0f;
            r.Width = fixedWidth;
            r.Height = LauncherHeight;
            r.X = Clamp(r.X, 0f, Math.Max(0f, screenWidth - r.Width));
            r.Y = Clamp(r.Y, 0f, Math.Max(0f, screenHeight - r.Height));
            return r;
        }

        internal const float CompactWindowMinHeight = 230f;
        internal const float WindowScreenMargin = 20f;

        // The configured/default height is an envelope, not an always-reserved content area. The
        // Hub resolves height from the selected page on structural changes; dynamic values do not resize it.
        internal static float ResolveCompactWindowHeight(float preferredTotalHeight,
            float maximumEnvelopeHeight, float screenHeight)
        {
            if (!Finite(maximumEnvelopeHeight) || maximumEnvelopeHeight <= 0f) maximumEnvelopeHeight = 430f;
            if (!Finite(screenHeight) || screenHeight <= 0f) screenHeight = maximumEnvelopeHeight + WindowScreenMargin;
            float screenCap = Math.Max(1f, screenHeight - WindowScreenMargin);
            float maxHeight = Math.Min(maximumEnvelopeHeight, screenCap);
            if (maxHeight <= 0f) maxHeight = 1f;
            float minHeight = Math.Min(CompactWindowMinHeight, maxHeight);
            if (!Finite(preferredTotalHeight) || preferredTotalHeight <= 0f) preferredTotalHeight = minHeight;
            return Clamp(preferredTotalHeight, minHeight, maxHeight);
        }

        internal static SuiteRect ResizeWindowKeepingTop(SuiteRect current, float targetHeight,
            float screenWidth, float screenHeight)
        {
            if (!Finite(targetHeight) || targetHeight <= 0f) targetHeight = CompactWindowMinHeight;
            float oldTop = (Finite(current.Y) && Finite(current.Height)) ? current.Y + current.Height : 0f;
            current.Height = targetHeight;
            current.Y = oldTop - targetHeight;
            return ClampWindow(current, screenWidth, screenHeight);
        }

        internal static SuiteRect ClampWindow(SuiteRect r, float screenWidth, float screenHeight)
        {
            float maxWidth = Math.Max(1f, screenWidth - WindowScreenMargin);
            float minWidth = Math.Min(420f, maxWidth);
            float maxHeight = Math.Max(1f, screenHeight - WindowScreenMargin);
            float minHeight = Math.Min(CompactWindowMinHeight, maxHeight);
            if (!Finite(r.Width)) r.Width = 620f;
            if (!Finite(r.Height)) r.Height = 430f;
            r.Width = Clamp(r.Width, minWidth, maxWidth);
            r.Height = Clamp(r.Height, minHeight, maxHeight);
            if (!Finite(r.X)) r.X = 0f;
            if (!Finite(r.Y)) r.Y = 0f;
            r.X = Clamp(r.X, 0f, Math.Max(0f, screenWidth - r.Width));
            r.Y = Clamp(r.Y, 0f, Math.Max(0f, screenHeight - r.Height));
            return r;
        }

        // --- Retained-uGUI position persistence ------------------------------------------------
        //
        // Positions are stored NORMALIZED (0..1 of screen extent) rather than in absolute pixels so
        // a saved layout survives a resolution change. Panels are anchored bottom-left with a
        // bottom-left pivot, so anchoredPosition is simply pixels from the bottom-left corner and
        // these helpers stay pure/Unity-free and directly testable.

        internal const float Unset = -1f;

        // A stored axis value can be one of three things:
        //   < 0        -> unset, caller should use its default placement
        //   0 .. 1     -> normalized (current format)
        //   > 1        -> legacy absolute pixels written by the pre-0.3.0 OnGUI Hub
        //
        // Legacy pixel values are deliberately NOT migrated. The OnGUI Hub stored GUI-space
        // coordinates with a TOP-left origin and Y increasing downward; retained uGUI panels here
        // are anchored bottom-left with Y increasing upward. Rescaling the number would silently
        // produce a vertically mirrored position - the panel lands somewhere the player never put
        // it, which is worse than simply reverting to the known-good default placement once.
        // Returns Unset (-1) when there is no usable stored value in the current coordinate system.
        internal static float InterpretStoredAxis(float stored, float screenExtent)
        {
            if (!Finite(stored) || stored < 0f) return Unset;
            if (stored <= 1f) return stored;
            return Unset;
        }

        internal static float NormalizeAxis(float pixels, float screenExtent)
        {
            if (!Finite(pixels) || !Finite(screenExtent) || screenExtent <= 0f) return 0f;
            return Clamp(pixels / screenExtent, 0f, 1f);
        }

        // Converts a normalized axis back to pixels and clamps so the panel stays FULLY on screen.
        // This is also the off-screen recovery path: anything that resolves outside the visible
        // area is pulled back inside rather than being left unreachable.
        internal static float ResolveAxis(float normalized, float screenExtent, float size)
        {
            if (!Finite(screenExtent) || screenExtent <= 0f) return 0f;
            if (!Finite(size) || size < 0f) size = 0f;
            float max = Math.Max(0f, screenExtent - size);
            if (!Finite(normalized)) return 0f;
            return Clamp(normalized * screenExtent, 0f, max);
        }

        // Full panel resolve: normalized position + measured size -> on-screen pixel position.
        internal static SuiteRect ResolvePanel(float normalizedX, float normalizedY,
            float width, float height, float screenWidth, float screenHeight)
        {
            if (!Finite(width) || width <= 0f) width = 0f;
            if (!Finite(height) || height <= 0f) height = 0f;
            return new SuiteRect(
                ResolveAxis(normalizedX, screenWidth, width),
                ResolveAxis(normalizedY, screenHeight, height),
                width, height);
        }

        // True when a resolved position would place the panel even partially off screen, i.e. the
        // stored value needs recovery rather than direct use.
        internal static bool NeedsRecovery(float x, float y, float width, float height,
            float screenWidth, float screenHeight)
        {
            if (!Finite(x) || !Finite(y)) return true;
            if (x < 0f || y < 0f) return true;
            if (x + width > screenWidth) return true;
            if (y + height > screenHeight) return true;
            return false;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static bool Finite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }
}
