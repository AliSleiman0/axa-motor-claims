namespace Api.Modules.Declarations;

/// <summary>
/// An officer's comment on a decision (design.md §4).
///
/// Its own table rather than a column on `declaration`, and that is load-bearing twice over. §5.2
/// allows several comments per decision; and `declaration.state` is the EF concurrency token, so any
/// write that touched the declaration row without changing its state would fail against a concurrent
/// transition for no reason. Comments are inserts beside the row, never edits to it.
///
/// §5.2 stores rejection comments but the garage view shows them only when the state is `approved` —
/// the BRD grants comment visibility "in case of confirmation" only. That is a rendering rule, not a
/// storage rule, and it lives in the garage endpoint's DTO.
/// </summary>
public sealed class DeclarationComment
{
    public Guid Id { get; set; }

    public Guid DeclarationId { get; set; }

    public Guid AuthorUserId { get; set; }

    public required string Body { get; set; }

    public DateTime CreatedAt { get; set; }
}
