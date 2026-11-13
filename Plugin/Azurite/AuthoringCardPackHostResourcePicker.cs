using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json;
using Azurite.Core;
using AzureArchive.Automation;
using Studio.Scripts;

namespace Azurite;

internal sealed record AuthoringCardPackResourceCandidate(string ResourceId, string Name,
    CardResourceKind Kind, bool Available, string Source);
internal sealed record AuthoringCardPackPickerChange(long Revision, string Reason);

/// <summary>
/// One user-owned logical resource binding. Separate instances retain transition
/// and sound choices independently. Search never selects a result automatically.
/// No registration, periodic search, catalog rebinding or resource write occurs.
/// </summary>
internal sealed class AuthoringCardPackHostResourcePicker
{
    private sealed record Identity(IntPtr Session, IntPtr Studio, string Project, string EditSession, string CatalogVersion);
    private readonly int _editorThreadId;
    private readonly Func<bool> _contextAllowed;
    private readonly string _resourceKey;
    private readonly Dictionary<string, AuthoringCardPackResourceCandidate> _candidates = new(StringComparer.Ordinal);
    private Identity? _context;
    private CardResourceKind _kind;
    private string _query = string.Empty;
    private string? _nextCursor;
    private bool _hasPage, _busy;
    private AuthoringCardPackResourceChoice? _choice;
    private long _revision;

    internal AuthoringCardPackHostResourcePicker(int editorThreadId, Func<bool> contextAllowed,
        string resourceKey, CardResourceKind kind)
    {
        if (editorThreadId <= 0) throw new ArgumentOutOfRangeException(nameof(editorThreadId));
        _editorThreadId = editorThreadId;
        _contextAllowed = contextAllowed ?? throw new ArgumentNullException(nameof(contextAllowed));
        if (!LogicalKey(resourceKey)) throw new ArgumentException("A logical binding key is required.", nameof(resourceKey));
        NativeKind(kind);
        _resourceKey = resourceKey; _kind = kind;
    }

    // Notifications are synchronous invalidation signals, not authorization to
    // reenter this picker or to retry a native call. Consumers may cancel a preview.
    internal event Action<AuthoringCardPackPickerChange>? Changed;
    internal long Revision => _revision;
    internal string? NextCursor => _nextCursor;
    internal IReadOnlyList<AuthoringCardPackResourceCandidate> Candidates => Array.AsReadOnly(_candidates.Values.ToArray());

    internal void SetQuery(CardResourceKind kind, string query) => Run(() =>
    {
        NativeKind(kind);
        if (query == null) throw new ArgumentNullException(nameof(query));
        SynchronizeContext();
        if (_kind != kind || _query != query)
        {
            _kind = kind; _query = query;
            ResetSearch("QueryChanged");
        }
    });

    internal void SearchFirst(int limit = 50) => Run(() => LoadPage(next: false, limit));
    internal void SearchNext(int limit = 50) => Run(() => LoadPage(next: true, limit));

    internal void Select(string resourceId) => Run(() =>
    {
        var before = SynchronizeContext();
        if (_choice != null) { _choice = null; Notify("SelectionCleared"); }
        Require(resourceId != null && _hasPage && _candidates.TryGetValue(resourceId, out var candidate) && candidate.Available,
            "Select an available resource returned by this exact query; names, paths and native_key are not IDs.");
        try
        {
            string kind = NativeKind(_kind);
            var native = AuthoringResourceCatalog.Resolve(resourceId!, kind, before.CatalogVersion, true);
            Require(native != null, "Native Resolve returned no resource object.");
            using var document = Parse(native.ToString());
            var resolved = document.RootElement;
            Require(String(resolved, "resource_id") == resourceId && String(resolved, "kind") == kind &&
                String(resolved, "catalog_version") == before.CatalogVersion && Boolean(resolved, "available"),
                "Native Resolve did not verify this available resource, kind and catalog version.");
            Require(SynchronizeContext() == before, "Project/session/catalog changed during resource resolution.");
            _choice = new(_resourceKey, resourceId!, _kind);
            Notify("SelectionChanged");
        }
        catch
        {
            _choice = null;
            Notify("SelectionFailed");
            throw;
        }
    });

    /// <summary>Use from the current resource-choices binding before Host Capture.</summary>
    internal AuthoringCardPackResourceChoice? ReadChoice()
    {
        AuthoringCardPackResourceChoice? result = null;
        Run(() => { SynchronizeContext(); result = _choice is null ? null : _choice with { }; });
        return result;
    }

    private void LoadPage(bool next, int limit)
    {
        Require(limit is >= 1 and <= 200, "Search limit must be between 1 and 200.");
        var before = SynchronizeContext();
        Require(!next || _hasPage && !string.IsNullOrEmpty(_nextCursor), "No current native next_cursor is available.");
        string? cursor = next ? _nextCursor : null;
        if (!next) ResetSearch("SearchRestarted");
        try
        {
            // Query mode supplies null ids, including when query is empty. The
            // opaque native cursor is returned unchanged for this same query.
            var native = AuthoringResourceCatalog.Search(NativeKind(_kind), _query, null!, cursor, limit, before.CatalogVersion);
            Require(native != null, "Native Search returned no page object.");
            using var document = Parse(native.ToString());
            var page = document.RootElement;
            Require(String(page, "catalog_version") == before.CatalogVersion, "Search returned a different catalog version.");
            var items = page.GetProperty("items");
            var missing = page.GetProperty("missing_ids");
            Require(items.ValueKind == JsonValueKind.Array && items.GetArrayLength() <= limit &&
                missing.ValueKind == JsonValueKind.Array && missing.GetArrayLength() == 0, "Query search returned an invalid page.");
            var nextValue = page.GetProperty("next_cursor");
            Require(nextValue.ValueKind is JsonValueKind.Null or JsonValueKind.String, "Invalid native next_cursor.");
            string? following = nextValue.ValueKind == JsonValueKind.Null ? null : nextValue.GetString();
            Require(following == null || !string.IsNullOrWhiteSpace(following) && following != cursor, "Native pagination cursor did not advance.");
            var additions = new Dictionary<string, AuthoringCardPackResourceCandidate>(StringComparer.Ordinal);
            foreach (var item in items.EnumerateArray())
            {
                UniqueObject(item);
                string id = String(item, "resource_id");
                Require(String(item, "kind") == NativeKind(_kind), "Search candidate kind does not match the query.");
                string name = String(item, "name", allowEmpty: true);
                string source = String(item, "source");
                var candidate = new AuthoringCardPackResourceCandidate(id, name, _kind, Boolean(item, "available"), source);
                Require(!additions.ContainsKey(id) && !_candidates.ContainsKey(id), "Search returned duplicate resource IDs.");
                additions.Add(id, candidate);
            }
            Require(SynchronizeContext() == before, "Project/session/catalog changed during resource search.");
            foreach (var item in additions) _candidates.Add(item.Key, item.Value);
            _nextCursor = following; _hasPage = true;
            Notify("CandidatesChanged");
        }
        catch
        {
            ResetSearch("SearchFailed");
            throw;
        }
    }

    private Identity SynchronizeContext()
    {
        Identity fresh;
        try { fresh = ReadIdentity(); }
        catch { _context = null; ResetSearch("ContextUnavailable"); throw; }
        if (_context != fresh)
        {
            _context = fresh;
            ResetSearch("ContextChanged");
        }
        return fresh;
    }

    private Identity ReadIdentity()
    {
        Require(_contextAllowed(), "Resource browsing qualification is closed.");
        var studio = StudioCommon.instance;
        var session = AuthoringEditorSession.Current;
        Require(studio != null && studio.isActiveAndEnabled && studio.gameObject.activeInHierarchy && studio.entryNode != null &&
            string.IsNullOrEmpty(studio.sessionInitializationError) && session != null && !session.disposed &&
            !session.IsApplying && !session.SaveInProgress && session.studio?.Pointer == studio.Pointer,
            "An active existing native session and its real Studio owner are required.");
        string version = AuthoringResourceCatalog.CatalogVersion;
        Require(!string.IsNullOrWhiteSpace(session!.ProjectId) && !string.IsNullOrWhiteSpace(session.EditSession) && !string.IsNullOrWhiteSpace(version),
            "Native project/session/catalog identity is missing.");
        return new(session.Pointer, studio!.Pointer, session.ProjectId, session.EditSession, version);
    }

    private void ResetSearch(string reason)
    {
        _choice = null; _candidates.Clear(); _nextCursor = null; _hasPage = false;
        Notify(reason);
    }
    private void Notify(string reason)
    {
        _revision = checked(_revision + 1);
        Changed?.Invoke(new(_revision, reason));
    }
    private void Run(Action action)
    {
        Require(Environment.CurrentManagedThreadId == _editorThreadId, "Resource picker requires its qualified editor thread.");
        Require(!_busy, "Resource picker is already running; no reentrant native call is allowed.");
        _busy = true;
        try { action(); } finally { _busy = false; }
    }
    private static string NativeKind(CardResourceKind kind) => kind switch
    {
        CardResourceKind.Transition => "transition", CardResourceKind.Sound => "sound",
        _ => throw new InvalidOperationException("This picker selects scene transition or sound resources; stage occupants remain native selections.")
    };
    private static bool LogicalKey(string value) => !string.IsNullOrWhiteSpace(value) && value != "." && value != ".." &&
        value.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-');
    private static JsonDocument Parse(string json)
    {
        var document = JsonDocument.Parse(json);
        try { UniqueObject(document.RootElement); return document; } catch { document.Dispose(); throw; }
    }
    private static void UniqueObject(JsonElement value)
    {
        Require(value.ValueKind == JsonValueKind.Object, "Native resource data must be an object.");
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject()) Require(fields.Add(field.Name), "Duplicate native resource field: " + field.Name);
    }
    private static string String(JsonElement value, string field, bool allowEmpty = false)
    {
        Require(value.TryGetProperty(field, out var item) && item.ValueKind == JsonValueKind.String &&
            (allowEmpty || !string.IsNullOrWhiteSpace(item.GetString())), "Missing native resource string: " + field);
        return item.GetString()!;
    }
    private static bool Boolean(JsonElement value, string field)
    {
        Require(value.TryGetProperty(field, out var item) && item.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "Native resource flag must be a JSON boolean: " + field);
        return item.GetBoolean();
    }
    private static void Require([DoesNotReturnIf(false)] bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
