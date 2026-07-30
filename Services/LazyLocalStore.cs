using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StudentDesktop.Models;

namespace StudentDesktop.Services;

// Opening LocalStoreContext is async (it round-trips the OS keystore before the first
// query can run), but CodeBridge/CodeEditorViewModel are constructed synchronously —
// ShellViewModel wires Bridge's events immediately after `new CodeEditorViewModel(...)`,
// same session as every other ViewModel here. Deferring the actual open to the first real
// call (rather than blocking the constructor, or requiring every caller up the chain to
// become async) keeps that synchronous construction pattern intact while still surfacing
// a keystore failure as a normal LocalStoreUnavailableException on first use, same as any
// other call site would see it.
public sealed class LazyLocalStore(Func<Task<ILocalStore>>? factory = null) : ILocalStore
{
    private readonly Func<Task<ILocalStore>> _factory = factory ?? DefaultFactory;
    private Task<ILocalStore>? _store;
    private readonly object _gate = new();

    private static async Task<ILocalStore> DefaultFactory() =>
        await LocalStoreContext.OpenAsync(SecureKeyStoreFactory.Create());

    private Task<ILocalStore> GetStoreAsync()
    {
        lock (_gate)
        {
            return _store ??= _factory();
        }
    }

    public async Task<IReadOnlyList<CodeProjectSummaryDto>> ListCodeProjectsAsync() =>
        await (await GetStoreAsync()).ListCodeProjectsAsync();

    public async Task<CodeProjectDto?> GetCodeProjectAsync(Guid id) =>
        await (await GetStoreAsync()).GetCodeProjectAsync(id);

    public async Task<CodeProjectDto> SaveCodeProjectAsync(CodeProjectDto project) =>
        await (await GetStoreAsync()).SaveCodeProjectAsync(project);

    public async Task DeleteCodeProjectAsync(Guid id) =>
        await (await GetStoreAsync()).DeleteCodeProjectAsync(id);
}
