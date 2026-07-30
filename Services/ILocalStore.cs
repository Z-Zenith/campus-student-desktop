using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StudentDesktop.Models;

namespace StudentDesktop.Services;

// Work Item C (SDA/SEK plan): local encrypted storage for scratch/working code projects
// — these never sync to the backend (see CodeBridge's redirect from ApiClient to this
// interface). Behind an interface (rather than injecting the concrete LocalStoreContext
// directly) specifically so CodeBridge/CodeEditorViewModel stay fakeable in tests, matching
// how they're already constructed with injectable dependencies (ApiClient itself isn't
// behind an interface today, but this is new code — no reason to repeat that gap here).
//
// Scope decision made during implementation: notes are NOT migrated to local-only storage
// in this pass (SekBridge/NotesViewModel are unchanged, still server-backed). The plan
// flagged notes as needing "a clarified scope decision — fully local-only, or a hybrid
// draft/published model" before touching wikilink resolution/backlinks/image search, which
// depend on server-side note_links; resolving that properly is its own piece of work, not
// something to rush alongside the code-project migration this pass actually delivers.
public interface ILocalStore
{
    Task<IReadOnlyList<CodeProjectSummaryDto>> ListCodeProjectsAsync();

    Task<CodeProjectDto?> GetCodeProjectAsync(Guid id);

    /// Upsert — inserts if `id` doesn't exist yet, otherwise replaces it in place. Unlike
    /// the server-backed ApiClient (create vs. update-then-404-fallback), a local upsert
    /// has no "not found" concept to fall back from.
    Task<CodeProjectDto> SaveCodeProjectAsync(CodeProjectDto project);

    Task DeleteCodeProjectAsync(Guid id);
}
