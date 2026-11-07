using System;
using System.Collections.Generic;
using System.Linq;

namespace Azurite.Core;

/// <summary>Modifier keys relevant to the dialogue-list selection gesture.</summary>
[Flags]
public enum BoxSelectionModifiers
{
    None = 0,
    Control = 1,
    Shift = 2
}

/// <summary>
/// The native control that owns the pointer-down event.  Azurite only consumes
/// pointer input from the dialogue-list surface; all other controls continue to
/// receive their normal AA event.
/// </summary>
public enum BoxSelectionPointerTarget
{
    DialogueList,
    InputField,
    Scrollbar,
    RowDragHandle,
    OutsideDialogueList
}

public readonly record struct BoxSelectionPoint(float X, float Y)
{
    public bool IsFinite => !float.IsNaN(X) && !float.IsInfinity(X) &&
                            !float.IsNaN(Y) && !float.IsInfinity(Y);

    public float DistanceSquared(BoxSelectionPoint other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        return dx * dx + dy * dy;
    }
}

public readonly record struct BoxSelectionRect(float Left, float Top, float Right, float Bottom)
{
    public float MinX => MathF.Min(Left, Right);
    public float MaxX => MathF.Max(Left, Right);
    public float MinY => MathF.Min(Top, Bottom);
    public float MaxY => MathF.Max(Top, Bottom);

    public static BoxSelectionRect FromPoints(BoxSelectionPoint first, BoxSelectionPoint second) =>
        new(first.X, first.Y, second.X, second.Y);

    public bool Contains(BoxSelectionPoint point) =>
        point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;

    /// <summary>Edges touching counts as an intersection for predictable row selection.</summary>
    public bool Intersects(BoxSelectionRect other) =>
        MinX <= other.MaxX && MaxX >= other.MinX &&
        MinY <= other.MaxY && MaxY >= other.MinY;
}

/// <summary>
/// A detached row boundary supplied by the native adapter for one gesture.
/// Row IDs must be stable for the lifetime of a displayed dialogue list.
/// </summary>
public readonly record struct BoxSelectionRow
{
    public BoxSelectionRow(string rowId, BoxSelectionRect bounds, bool isValid = true)
    {
        if (string.IsNullOrWhiteSpace(rowId))
            throw new ArgumentException("A stable row ID is required.", nameof(rowId));

        RowId = rowId;
        Bounds = bounds;
        IsValid = isValid;
    }

    public string RowId { get; }

    public BoxSelectionRect Bounds { get; }

    /// <summary>Invalid/recycled rows are ignored even if their old rectangle remains visible.</summary>
    public bool IsValid { get; }
}

/// <summary>Detached ordered state for one adapter-to-consumer handoff.</summary>
public readonly record struct BoxSelectionSnapshot(
    long Revision,
    IReadOnlyList<BoxSelectionRow> Rows,
    IReadOnlyList<string> SelectedRowIds);

/// <summary>
/// Host-independent state machine for Windows-style dialogue-list box selection.
/// It stores only UI selection state; it never mutates dialogue data.
/// </summary>
public sealed class BoxSelectionModel
{
    public const float ActivationDistance = 6f;

    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private HashSet<string> _selectionAtBegin = new(StringComparer.Ordinal);
    private string? _anchorAtBegin;
    private BoxSelectionPoint _startPoint;
    private BoxSelectionPoint _currentPoint;
    private BoxSelectionModifiers _modifiers;
    private bool _pointerDown;
    private bool _dragging;
    private string? _anchorRowId;
    private long _selectionRevision;

    public bool IsDragging => _dragging;

    /// <summary>
    /// Monotonic UI revision for effective membership/anchor changes and explicit
    /// list invalidation. It is separate from the native project's revision.
    /// </summary>
    public long SelectionRevision => _selectionRevision;

    public BoxSelectionRect? SelectionRectangle =>
        _dragging ? BoxSelectionRect.FromPoints(_startPoint, _currentPoint) : null;

    /// <summary>A detached, deterministic snapshot of selected stable row IDs.</summary>
    public IReadOnlyList<string> SelectedRowIds =>
        _selected.OrderBy(id => id, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Returns a detached snapshot preserving the supplied native row order.
    /// Consumers revalidate this UI revision and the native project revision
    /// separately before applying a batch.
    /// </summary>
    public BoxSelectionSnapshot CreateSnapshot(IReadOnlyList<BoxSelectionRow>? rows)
    {
        var orderedRows = ValidRows(rows ?? Array.Empty<BoxSelectionRow>()).ToArray();
        var selected = orderedRows.Where(row => _selected.Contains(row.RowId))
            .Select(row => row.RowId).ToArray();
        return new BoxSelectionSnapshot(_selectionRevision, orderedRows, selected);
    }

    /// <summary>
    /// Begins a possible gesture.  Only the dialogue list owns the gesture;
    /// inputs, scrollbars, drag handles and outside clicks remain native.
    /// </summary>
    public bool Begin(
        BoxSelectionPointerTarget pointer,
        BoxSelectionPoint startPoint,
        BoxSelectionModifiers modifiers)
    {
        // A replacement or native-owned pointer-down cancels an unfinished
        // preview; it must not silently commit the old gesture.
        Cancel();
        if (pointer != BoxSelectionPointerTarget.DialogueList || !startPoint.IsFinite)
            return false;

        _pointerDown = true;
        _startPoint = startPoint;
        _currentPoint = startPoint;
        _modifiers = modifiers & (BoxSelectionModifiers.Control | BoxSelectionModifiers.Shift);
        _selectionAtBegin = new HashSet<string>(_selected, StringComparer.Ordinal);
        _anchorAtBegin = _anchorRowId;
        return true;
    }

    /// <summary>
    /// Updates the pending gesture.  A six-pixel Euclidean threshold prevents a
    /// normal click from becoming a box selection due to pointer jitter.
    /// </summary>
    public bool Move(BoxSelectionPoint currentPoint, IReadOnlyList<BoxSelectionRow> rows)
    {
        if (!_pointerDown || !currentPoint.IsFinite)
            return false;

        _currentPoint = currentPoint;
        if (!_dragging)
        {
            if (_startPoint.DistanceSquared(currentPoint) <= ActivationDistance * ActivationDistance)
                return false;
            _dragging = true;
        }

        // Keep the visible highlight in sync while the pointer is down.  The
        // begin snapshot makes this idempotent for Ctrl toggles.
        ApplySelectionChange(() => ApplyBox(ValidRows(rows ?? Array.Empty<BoxSelectionRow>()).ToArray()));

        return true;
    }

    /// <summary>
    /// Commits the current click/box gesture and returns the resulting ID
    /// snapshot.  The supplied rows are read-only geometry and validity data.
    /// </summary>
    public IReadOnlyList<string> End(IReadOnlyList<BoxSelectionRow> rows)
    {
        if (!_pointerDown)
            return SelectedRowIds;

        var safeRows = rows ?? Array.Empty<BoxSelectionRow>();
        var validRows = ValidRows(safeRows).ToArray();
        ApplySelectionChange(() =>
        {
            if (_dragging)
                ApplyBox(validRows);
            else
                ApplyClick(validRows);
        });

        ResetGesture();
        return SelectedRowIds;
    }

    /// <summary>Clears the UI selection and cancels any in-progress gesture.</summary>
    public void Clear()
    {
        ApplySelectionChange(ClearSelection);
        ResetGesture();
    }

    /// <summary>Cancels a pending gesture and restores the pointer-down selection.</summary>
    public void Cancel()
    {
        if (!_pointerDown)
            return;
        ApplySelectionChange(RestoreSelectionAtBegin);
        ResetGesture();
    }

    /// <summary>
    /// A list rebuild or story switch invalidates all row mappings.  Selection is
    /// intentionally cleared even when some IDs happen to be reused.
    /// </summary>
    public void Refresh(IReadOnlyList<BoxSelectionRow>? rows = null)
    {
        // Reused IDs cannot make an explicitly rebuilt list fresh again.
        ApplySelectionChange(ClearSelection, invalidate: true);
        ResetGesture();
    }

    /// <summary>
    /// Reconciles a live row snapshot without discarding still-valid choices.
    /// Any preview is canceled back to its pointer-down state, then recycled or
    /// invalid rows are removed. The final effective change invalidates once.
    /// </summary>
    public void Reconcile(IReadOnlyList<BoxSelectionRow>? rows)
    {
        var validIds = ValidRows(rows ?? Array.Empty<BoxSelectionRow>())
            .Select(row => row.RowId)
            .ToHashSet(StringComparer.Ordinal);
        ApplySelectionChange(() =>
        {
            if (_pointerDown)
                RestoreSelectionAtBegin();
            _selected.RemoveWhere(id => !validIds.Contains(id));
            if (_anchorRowId != null && !validIds.Contains(_anchorRowId))
                _anchorRowId = null;
        });
        ResetGesture();
    }

    private void ApplyBox(IReadOnlyList<BoxSelectionRow> rows)
    {
        var hit = new HashSet<string>(StringComparer.Ordinal);
        var rectangle = BoxSelectionRect.FromPoints(_startPoint, _currentPoint);
        foreach (var row in rows)
        {
            if (rectangle.Intersects(row.Bounds))
                hit.Add(row.RowId);
        }

        if ((_modifiers & BoxSelectionModifiers.Control) != 0)
        {
            // Ctrl selection is a symmetric difference against the snapshot at
            // pointer-down, so moving the pointer never causes repeated toggles.
            var validIds = rows.Select(row => row.RowId).ToHashSet(StringComparer.Ordinal);
            _selected.Clear();
            foreach (var rowId in _selectionAtBegin)
            {
                if (validIds.Contains(rowId) && !hit.Contains(rowId))
                    _selected.Add(rowId);
            }

            foreach (var rowId in hit)
            {
                if (!_selectionAtBegin.Contains(rowId))
                    _selected.Add(rowId);
            }
        }
        else
        {
            _selected.Clear();
            foreach (var row in rows)
                if (hit.Contains(row.RowId))
                    _selected.Add(row.RowId);
        }

        if (hit.Count != 0)
            _anchorRowId = rows.First(row => hit.Contains(row.RowId)).RowId;
        else if ((_modifiers & BoxSelectionModifiers.Control) == 0)
            _anchorRowId = null;
    }

    private void ApplyClick(IReadOnlyList<BoxSelectionRow> rows)
    {
        // Any IDs that no longer map to a valid row are removed before a click
        // can toggle or extend the selection.
        var validIds = rows.Select(row => row.RowId).ToHashSet(StringComparer.Ordinal);
        _selected.RemoveWhere(id => !validIds.Contains(id));
        if (_anchorRowId != null && !validIds.Contains(_anchorRowId))
            _anchorRowId = null;

        var clicked = rows.FirstOrDefault(row => row.Bounds.Contains(_startPoint));
        bool hasClickedRow = !string.IsNullOrEmpty(clicked.RowId);
        if (!hasClickedRow)
        {
            if ((_modifiers & (BoxSelectionModifiers.Control | BoxSelectionModifiers.Shift)) == 0)
            {
                _selected.Clear();
                _anchorRowId = null;
            }
            return;
        }

        if ((_modifiers & BoxSelectionModifiers.Shift) != 0)
        {
            SelectRange(rows, clicked.RowId);
            _anchorRowId ??= clicked.RowId;
            return;
        }

        if ((_modifiers & BoxSelectionModifiers.Control) != 0)
        {
            if (!_selected.Remove(clicked.RowId))
                _selected.Add(clicked.RowId);
        }
        else
        {
            _selected.Clear();
            _selected.Add(clicked.RowId);
        }

        _anchorRowId = clicked.RowId;
    }

    private void SelectRange(IReadOnlyList<BoxSelectionRow> rows, string clickedRowId)
    {
        int clickedIndex = IndexOf(rows, clickedRowId);
        int anchorIndex = _anchorRowId is null ? -1 : IndexOf(rows, _anchorRowId);
        if (clickedIndex < 0 || anchorIndex < 0)
        {
            _selected.Clear();
            if (clickedIndex >= 0)
                _selected.Add(clickedRowId);
            return;
        }

        _selected.Clear();
        int first = Math.Min(anchorIndex, clickedIndex);
        int last = Math.Max(anchorIndex, clickedIndex);
        for (int i = first; i <= last; i++)
            _selected.Add(rows[i].RowId);
    }

    private static int IndexOf(IReadOnlyList<BoxSelectionRow> rows, string rowId)
    {
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].RowId == rowId)
                return i;
        return -1;
    }

    private static IEnumerable<BoxSelectionRow> ValidRows(IReadOnlyList<BoxSelectionRow> rows)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!row.IsValid || string.IsNullOrWhiteSpace(row.RowId) || !seen.Add(row.RowId))
                continue;
            yield return row;
        }
    }

    private void ApplySelectionChange(Action update, bool invalidate = false)
    {
        var before = new HashSet<string>(_selected, StringComparer.Ordinal);
        string? anchorBefore = _anchorRowId;
        update();
        if (invalidate || !before.SetEquals(_selected) ||
            !string.Equals(anchorBefore, _anchorRowId, StringComparison.Ordinal))
            _selectionRevision++;
    }

    private void ClearSelection()
    {
        _selected.Clear();
        _anchorRowId = null;
    }

    private void RestoreSelectionAtBegin()
    {
        _selected.Clear();
        foreach (string rowId in _selectionAtBegin)
            _selected.Add(rowId);
        _anchorRowId = _anchorAtBegin;
    }

    private void ResetGesture()
    {
        _pointerDown = false;
        _dragging = false;
        _modifiers = BoxSelectionModifiers.None;
        _selectionAtBegin = new HashSet<string>(StringComparer.Ordinal);
        _anchorAtBegin = null;
    }
}
