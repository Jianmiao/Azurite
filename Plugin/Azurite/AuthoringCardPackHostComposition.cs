using System;
using System.Collections.Generic;
using Azurite.Core;
using Studio.Scripts;

namespace Azurite;

/// <summary>
/// Explicit unregistered composition for the qualified authoring UI. All activity,
/// selection and resource choices must be real caller-owned bindings; no defaults.
/// </summary>
internal static class AuthoringCardPackHostComposition
{
    internal static ICardPackV2Host Create(int editorThreadId, Func<bool> authoringAllowed,
        Func<long> selectionRevision, Func<IReadOnlyList<AuthoringCardPackResourceChoice>> selectedResources,
        Action<string, IReadOnlyList<string>> invalidateSelection,
        Func<IReadOnlyList<ScriptListItem>>? readBatchRows = null)
    {
        var content = new AuthoringCardPackHostContent(editorThreadId, selectedResources);
        return new AuthoringCardPackHost(editorThreadId, authoringAllowed, selectionRevision, content.Capture, invalidateSelection, readBatchRows);
    }
}
