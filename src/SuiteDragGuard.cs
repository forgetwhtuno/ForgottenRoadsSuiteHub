using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ErenshorSuiteHub
{
    // Suite-owned retained-uGUI drag guard. Ownership begins at pointer-down rather than waiting
    // for uGUI's drag threshold, because Erenshor camera input is evaluated every frame.
    internal sealed class SuiteDragGuard : MonoBehaviour,
        IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerUpHandler
    {
        private const string ProcessOwnersKey = "forgetwhtuno.erenshor.ui.drag.owners.v1";
        private const string ProcessBaselineKey = "forgetwhtuno.erenshor.ui.drag.nativeBaseline.v1";
        private const string ProcessBaselineCapturedKey = "forgetwhtuno.erenshor.ui.drag.nativeBaselineCaptured.v1";
        private const string ProcessOwner = "forgetwhtuno.erenshor.suitehub";
        private static readonly HashSet<SuiteDragGuard> ActiveOwners = new HashSet<SuiteDragGuard>();

        internal RectTransform Target;
        internal string DiagnosticLabel = "?";
        internal Action OnDragCompleted;
        internal Action OnPointerActivated;

        private RectTransform _parentRect;
        private Vector2 _dragStartLocalPointer;
        private Vector2 _dragStartAnchoredPos;
        private readonly SuitePointerOwnershipState _gesture = new SuitePointerOwnershipState();

        internal static bool HubOwnsDrag { get { return ActiveOwners.Count > 0; } }

        private void Awake() { if (Target == null) Target = GetComponent<RectTransform>(); }

        public void OnPointerDown(PointerEventData eventData)
        {
            try { if (OnPointerActivated != null) OnPointerActivated(); } catch { }
            if (eventData != null && eventData.button == PointerEventData.InputButton.Left) Acquire();
            SuiteHubDiagnostics.Log("pointerDown on " + DiagnosticLabel + " pos=" +
                (eventData == null ? "?" : eventData.position.ToString()) + " ownership=" + _gesture.OwnsPointer);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left || Target == null) return;
            Acquire();
            _gesture.BeginDrag();
            _parentRect = Target.parent as RectTransform;
            if (_parentRect == null) { EndDrag(false); return; }
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRect, eventData.position, eventData.pressEventCamera, out local))
            { EndDrag(false); return; }
            _dragStartLocalPointer = local;
            _dragStartAnchoredPos = Target.anchoredPosition;
            ReassertNativeFlag();
            SuiteHubDiagnostics.Log("dragBegin " + DiagnosticLabel + " target=" + Target.name + " startPos=" + _dragStartAnchoredPos);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_gesture.IsDragging || eventData == null || eventData.button != PointerEventData.InputButton.Left || Target == null || _parentRect == null) return;
            ReassertNativeFlag();
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRect, eventData.position, eventData.pressEventCamera, out local)) return;
            Target.anchoredPosition = _dragStartAnchoredPos + (local - _dragStartLocalPointer);
        }

        private void Update()
        {
            if (!_gesture.OwnsPointer) return;
            ReassertNativeFlag();
            // uGUI normally routes pointer-up back to pointerPress even after crossing canvases. If
            // focus/raycast teardown prevents that callback, release as soon as the physical press
            // is gone. Polling occurs only while Hub owns a press, never as an idle input path.
            try { if (!Input.GetMouseButton(0)) EndDrag(false); } catch { }
        }

        private void OnApplicationFocus(bool focused) { if (!focused) EndDrag(false); }
        private void OnApplicationPause(bool paused) { if (paused) EndDrag(false); }
        public void OnEndDrag(PointerEventData eventData) { if (eventData == null || eventData.button == PointerEventData.InputButton.Left) EndDrag(true); }
        public void OnPointerUp(PointerEventData eventData) { if (eventData == null || eventData.button == PointerEventData.InputButton.Left) EndDrag(true); }
        private void OnDisable() { EndDrag(false); }
        private void OnDestroy() { EndDrag(false); }

        private void Acquire()
        {
            if (!_gesture.PointerDown()) return;
            if (ActiveOwners.Count == 0)
            {
                AcquireProcessOwnership();
            }
            ActiveOwners.Add(this);
            ReassertNativeFlag();
        }

        private static void ReassertNativeFlag()
        {
            if (ActiveOwners.Count <= 0) return;
            try { if (!GameData.DraggingUIElement) GameData.DraggingUIElement = true; } catch { }
        }

        private void EndDrag(bool notifyCompleted)
        {
            bool wasDragging = _gesture.IsDragging;
            if (_gesture.Release())
            {
                ActiveOwners.Remove(this);
                RestoreNativeFlagIfLastOwnerReleased();
            }
            _parentRect = null;
            if (notifyCompleted && wasDragging)
            {
                try { if (OnDragCompleted != null) OnDragCompleted(); } catch { }
            }
        }

        private static void RestoreNativeFlagIfLastOwnerReleased()
        {
            if (ActiveOwners.Count != 0) return;
            ReleaseProcessOwnership();
        }

        internal static void ForceReleaseIfHubOwned()
        {
            if (ActiveOwners.Count == 0)
            {
                if (ProcessContainsOwner()) ReleaseProcessOwnership();
                return;
            }
            SuiteDragGuard[] owners = new SuiteDragGuard[ActiveOwners.Count];
            ActiveOwners.CopyTo(owners);
            for (int i = 0; i < owners.Length; i++)
            {
                SuiteDragGuard owner = owners[i];
                if (owner == null) continue;
                owner._gesture.Release();
                owner._parentRect = null;
            }
            ActiveOwners.Clear();
            RestoreNativeFlagIfLastOwnerReleased();
        }

        private static void AcquireProcessOwnership()
        {
            HashSet<string> owners = GetProcessOwners(true);
            if (owners == null) return;
            lock (owners)
            {
                if (owners.Count == 0)
                {
                    bool baseline = false;
                    try { baseline = GameData.DraggingUIElement; } catch { }
                    AppDomain.CurrentDomain.SetData(ProcessBaselineKey, baseline);
                    AppDomain.CurrentDomain.SetData(ProcessBaselineCapturedKey, true);
                }
                owners.Add(ProcessOwner);
            }
            try { GameData.DraggingUIElement = true; } catch { }
        }

        private static void ReleaseProcessOwnership()
        {
            HashSet<string> owners = GetProcessOwners(false);
            if (owners == null) { RestoreProcessBaseline(); return; }
            bool last;
            lock (owners) { owners.Remove(ProcessOwner); last = owners.Count == 0; }
            if (last) RestoreProcessBaseline();
            else { try { GameData.DraggingUIElement = true; } catch { } }
        }

        private static HashSet<string> GetProcessOwners(bool create)
        {
            try
            {
                HashSet<string> owners = AppDomain.CurrentDomain.GetData(ProcessOwnersKey) as HashSet<string>;
                if (owners == null && create)
                {
                    owners = new HashSet<string>(StringComparer.Ordinal);
                    AppDomain.CurrentDomain.SetData(ProcessOwnersKey, owners);
                }
                return owners;
            }
            catch { return null; }
        }

        private static bool ProcessContainsOwner()
        {
            HashSet<string> owners = GetProcessOwners(false);
            if (owners == null) return false;
            lock (owners) { return owners.Contains(ProcessOwner); }
        }

        private static void RestoreProcessBaseline()
        {
            try
            {
                object capturedValue = AppDomain.CurrentDomain.GetData(ProcessBaselineCapturedKey);
                bool captured = capturedValue is bool && (bool)capturedValue;
                object baselineValue = AppDomain.CurrentDomain.GetData(ProcessBaselineKey);
                bool baseline = baselineValue is bool && (bool)baselineValue;
                if (captured) GameData.DraggingUIElement = baseline;
                AppDomain.CurrentDomain.SetData(ProcessBaselineCapturedKey, false);
                AppDomain.CurrentDomain.SetData(ProcessBaselineKey, false);
            }
            catch { }
        }
    }
}
