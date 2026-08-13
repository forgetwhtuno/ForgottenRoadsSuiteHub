using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ErenshorSuiteHub
{
    // Mod-owned drag handler for the Suite Hub's own uGUI panels. Replaces the native Erenshor
    // DragUI component used through 0.3.2.
    //
    // Why not native DragUI: disassembling DragUI.Update() (Mono.Cecil against the current
    // Assembly-CSharp.dll) shows it captures GetComponent<Image>() on its own GameObject in
    // Awake() and, every frame, force-disables that Image unless GameData.EditUIMode is true - a
    // native "customize UI positions" flag, off by default, that governs visibility of ALL native
    // drag-handle borders across the game. 0.3.2 worked around this by forcing that flag true
    // globally while the Hub was visible, but live testing showed this unlocks/decorates OTHER
    // native windows too (large white edit-mode borders) - an unacceptable global side effect for
    // an always-on mod UI. DragUI is correct for the game's own edit-mode-only native handles; it
    // is the wrong tool for a persistently visible, persistently draggable mod launcher.
    //
    // This component instead implements the standard uGUI drag interfaces directly, exactly like
    // the working third-party pattern documented in docs/WORKING_MOD_UI_FINDINGS.md (the minimap's
    // resize handles): IBeginDragHandler/IDragHandler/IEndDragHandler using Canvas-space pointer
    // conversion via RectTransformUtility, not polled Input.mousePosition (which is what corrupted
    // under cursor lock in the original OnGUI Hub). It never reads or writes
    // GameData.EditUIMode and never requires a Harmony patch - only the same
    // GameData.DraggingUIElement flag every native input gate already checks.
    internal sealed class SuiteDragGuard : MonoBehaviour,
        IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerUpHandler
    {
        // Number of Hub drag handles currently holding a gesture. Static because more than one Hub
        // surface (launcher grip + window header) can exist, and only the last one to finish should
        // release the shared native flag.
        private static int _hubOwnedDrags;

        // The panel this handle moves. Defaults to this GameObject's own RectTransform if unset.
        internal RectTransform Target;

        // "launcher" / "header" - used only by bounded diagnostics.
        internal string DiagnosticLabel = "?";

        // Invoked once when a drag gesture completes, so position is persisted exactly once per
        // drag rather than every frame while dragging.
        internal Action OnDragCompleted;

        private RectTransform _parentRect;
        private Vector2 _dragStartLocalPointer;
        private Vector2 _dragStartAnchoredPos;
        private bool _dragging;
        private bool _owning;

        internal static bool HubOwnsDrag { get { return _hubOwnedDrags > 0; } }

        private void Awake()
        {
            if (Target == null) Target = GetComponent<RectTransform>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // One bounded line per gesture start. If this never appears while pressing the grip,
            // the pointer event is not reaching this GameObject at all (raycast/ordering problem),
            // not a drag-logic problem.
            SuiteHubDiagnostics.Log("pointerDown on " + DiagnosticLabel +
                " pos=" + eventData.position +
                " pressObject=" + (eventData.pointerPressRaycast.gameObject != null
                    ? eventData.pointerPressRaycast.gameObject.name : "null"));
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Target == null) return;
            _parentRect = Target.parent as RectTransform;
            if (_parentRect == null) return;

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _parentRect, eventData.position, eventData.pressEventCamera, out local))
                return;

            _dragStartLocalPointer = local;
            _dragStartAnchoredPos = Target.anchoredPosition;
            _dragging = true;

            if (!_owning)
            {
                _owning = true;
                _hubOwnedDrags++;
            }
            GameData.DraggingUIElement = true;

            SuiteHubDiagnostics.Log("dragBegin " + DiagnosticLabel +
                " target=" + Target.name + " parent=" + _parentRect.name + " startPos=" + _dragStartAnchoredPos);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || Target == null || _parentRect == null) return;

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _parentRect, eventData.position, eventData.pressEventCamera, out local))
                return;

            Target.anchoredPosition = _dragStartAnchoredPos + (local - _dragStartLocalPointer);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            SuiteHubDiagnostics.Log("dragEnd " + DiagnosticLabel +
                " finalPos=" + (Target != null ? Target.anchoredPosition.ToString() : "n/a"));
            EndDrag(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            // Covers a plain click (down+up, no drag) so ownership never lingers if OnEndDrag was
            // never invoked because the gesture never crossed the drag threshold.
            EndDrag(false);
        }

        // The paths a native drag component cannot cover for itself.
        private void OnDisable() { EndDrag(true); }
        private void OnDestroy() { EndDrag(true); }

        private void EndDrag(bool notifyCompleted)
        {
            bool wasDragging = _dragging;
            _dragging = false;
            Release();

            if (notifyCompleted && wasDragging)
            {
                try { if (OnDragCompleted != null) OnDragCompleted(); }
                catch (Exception) { /* persistence must never break input handling */ }
            }
        }

        private void Release()
        {
            if (!_owning) return;
            _owning = false;
            _hubOwnedDrags--;
            if (_hubOwnedDrags < 0) _hubOwnedDrags = 0;
            if (_hubOwnedDrags == 0)
            {
                try { GameData.DraggingUIElement = false; } catch (Exception) { }
            }
        }

        // Called from plugin teardown / zoning / exception recovery. Only clears the native flag if
        // the Hub is the one that set it, so a native or third-party drag is never stomped.
        internal static void ForceReleaseIfHubOwned()
        {
            if (_hubOwnedDrags <= 0)
            {
                _hubOwnedDrags = 0;
                return;
            }
            _hubOwnedDrags = 0;
            try { GameData.DraggingUIElement = false; } catch (Exception) { }
        }
    }
}
