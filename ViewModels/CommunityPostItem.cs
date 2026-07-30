using System;

namespace StudentDesktop.ViewModels;

// SDA-16: ClubPostDto and ClassroomDiscussionPostDto are separate API shapes (each keyed by
// its own parent id) but render identically in Community's shared Posts list — this is a
// display-only adapter, not an API contract, so it lives here rather than in Models/.
public record CommunityPostItem(Guid Id, Guid AuthorId, string Content, DateTime CreatedAt);
