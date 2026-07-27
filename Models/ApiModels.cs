using System;
using System.Collections.Generic;

namespace StudentDesktop.Models;

public record LoginRequest(string Identifier, string Password, string TotpCode, string? DeviceInfo);

public record LoginResponse(string Token, Guid UserId, Guid SessionId, string AccountType, string FullName);

public record CalendarItemDto(string Kind, Guid Id, string Title, DateTime Start, DateTime End, string? Extra);

public record MyCalendarResponse(List<CalendarItemDto> Items);

// SDA-14: personal to-do / custom-entry write operations.
public record CreateTodoRequest(string Title, DateTime? DueDate);

public record TodoDto(Guid Id, string Title, DateTime? DueDate, bool Completed);

public record SetTodoCompleteRequest(bool Completed);

public record CreateCustomCalendarEntryRequest(string Title, DateOnly EntryDate);

public record CustomCalendarEntryDto(Guid Id, string Title, DateOnly EntryDate);

// SEK-01: the Coding app's multi-file project — CodeBridge forwards SEK's CodeProject/
// CodeFile shapes here as-is (see campus-shared-editor-kit's types.ts).
public record CodeFileDto(string Path, string Language, string Content);

public record RunCodeProjectRequest(string EntryFilePath, IReadOnlyList<CodeFileDto> Files, string? Stdin);

public record CodeRunResultDto(string Stdout, string Stderr, int ExitCode, long DurationMs, bool TimedOut, string? Status);

public record CreateCodeProjectRequest(
    string Name, IReadOnlyList<CodeFileDto> Files, string EntryFilePath, string ActiveFilePath, string? Stdin, Guid? Id = null);

public record UpdateCodeProjectRequest(
    string Name, IReadOnlyList<CodeFileDto> Files, string EntryFilePath, string ActiveFilePath, string? Stdin);

public record CodeProjectDto(
    Guid Id, string Name, IReadOnlyList<CodeFileDto> Files, string EntryFilePath, string ActiveFilePath,
    string? Stdin, DateTime CreatedAt, DateTime UpdatedAt);

// SEK-01: the Coding app's integrated terminal — request/response exec against a
// persistent, workspace-mounted sandbox container (see campus-backend's
// TerminalSessionService doc comment for scope).
public record TerminalStartRequest(IReadOnlyList<CodeFileDto> Files);

public record TerminalStartResponse(Guid SessionId);

public record TerminalExecRequest(string Command);

public record TerminalExecResultDto(string Stdout, string Stderr, int ExitCode);

public record CodeProjectSummaryDto(Guid Id, string Name, DateTime UpdatedAt);

public record EventDto(Guid Id, string Title, DateTime StartTime, DateTime EndTime, bool IsRegistered);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword, string TotpCode);

// SDA-15
public record InternalMarkDto(Guid SubjectId, string SubjectName, decimal Marks, DateTime? PublishedAt);

public record ExternalMarkDto(Guid SubjectId, string SubjectName, string Grade, DateTime? ApprovedAt);

public record MyMarksResponse(List<InternalMarkDto> InternalMarks, List<ExternalMarkDto> ExternalMarks);

// SDA-03/SDA-04
public record WhitelistSiteDto(Guid Id, string Url, DateTime ApprovedAt);

public record WhitelistResponse(List<WhitelistSiteDto> Sites);

// SDA-04
public record CreateWhitelistRequestRequest(string Url);

public record WhitelistRequestDto(Guid Id, string Url, Guid RequestedBy, string Status, Guid? ReviewedBy);

// SDA-08, SEK-03
// SDA-19: outgoing links as parsed by SEK's own extractOutgoingLinks, forwarded as-is.
public record NoteLinkInput(Guid ToNoteId, string Anchor);

public record CreateNoteRequest(string Title, string ContentMarkdown, Guid? Id = null, IReadOnlyList<NoteLinkInput>? Links = null);

public record UpdateNoteRequest(string Title, string ContentMarkdown, IReadOnlyList<NoteLinkInput>? Links = null);

public record NoteDto(Guid Id, string Title, string ContentMarkdown, DateTime CreatedAt, DateTime UpdatedAt);

public record NoteSummaryDto(Guid Id, string Title, DateTime UpdatedAt);

// SDA-17. SubjectName is display-only context for the picker — the feedback itself
// (SubmitTeacherFeedbackRequest) only carries TeacherId, matching the backend's
// teacher_feedback schema, which has no subject/course column.
public record MyTeacherDto(Guid TeacherId, string TeacherName, Guid SubjectId, string SubjectName);

public record SubmitTeacherFeedbackRequest(Guid TeacherId, int Rating, string? Comments);

public record TeacherFeedbackDto(Guid Id, Guid TeacherId, int Rating, string? Comments, DateTime SubmittedAt);

// SDA-18. TeacherId/TeacherName are always present — every row comes from a
// TeacherSectionAssignment, which by definition always names a teacher.
public record MySubjectDto(Guid SubjectId, string SubjectCode, string SubjectName, Guid TeacherId, string TeacherName);

// SDA-11: request/response shapes for the auto-submit-on-exit endpoint
// (POST /api/v1/assignments/{id}/submissions/auto-submit). SubmissionFormat mirrors the
// backend's AssignmentType enum, serialized as a string (see Program.cs JsonStringEnumConverter).
public record SubmitAssignmentRequest(string ContentUrl, string SubmissionFormat);

public record SubmissionDto(Guid Id, Guid AssignmentId, Guid StudentId, string ContentUrl, DateTime SubmittedAt, bool IsLate, bool IsAutosubmitted);

// SDA-10: one row per assignment for the Assignments tile grid — GET /api/v1/assignments/mine.
public record AssignmentSummaryDto(Guid Id, string Title, string SubjectName, string Type, DateTime DueDate, DateTime? SubmittedAt, bool? IsLate);

// SDA-16
public record GroupDto(Guid Id, string Name, string Type, Guid? SectionId);

public record MyGroupsResponse(List<GroupDto> Groups);

public record CreatePostRequest(string Content);

public record GroupPostDto(Guid Id, Guid GroupId, Guid AuthorId, string Content, DateTime CreatedAt);

public record MaterialDto(Guid Id, string Title, string FileUrl, Guid? SubjectId, Guid? GroupId, Guid UploadedBy, DateTime UploadedAt);

// SDA-24, DMS-01: mirrors services/backend-api's MessageResponse/ThreadSummaryResponse
// field-for-field (see MessagingController.cs) — same shapes DmsBridge forwards to the
// DMS host bundle over the JS bridge.
public record SendMessageRequest(string Content);

public record DmsMessageDto(Guid Id, Guid ThreadId, Guid SenderId, string Content, DateTime SentAt, DateTime? ReadAt);

public record DmsThreadSummaryDto(Guid Id, Guid StudentId, Guid TeacherId, DateTime CreatedAt, DmsMessageDto? LastMessage);

// SEK-04
public record ImageSearchRequestBody(string Query);

public record ImageSearchResultDto(string Id, string Title, string SourceUrl, string ThumbnailUrl, int Width, int Height, string Attribution);

public record ImageSearchResponseDto(string Query, List<ImageSearchResultDto> Results, bool Degraded);

public record SaveImageRequestBody(string SourceUrl);

public record SaveImageResponseDto(string Url);

// SDA-25: no ClassSessionId here — the client only ever claims an AssignmentId it
// already knows (from having opened that assignment); the backend resolves the active
// class session itself when AssignmentId is omitted (see TelemetryController).
public record TelemetryEventRequest(string EventType, Dictionary<string, object>? Metadata, Guid? AssignmentId, DateTime RecordedAt);

public record SubmitTelemetryRequest(IReadOnlyList<TelemetryEventRequest> Events);
