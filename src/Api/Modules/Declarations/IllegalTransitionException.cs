namespace Api.Modules.Declarations;

/// <summary>
/// A transition design.md §5.2's table has no row for — approving a draft, rejecting an approved
/// declaration, submitting twice.
///
/// It is thrown by <see cref="Declaration"/> itself rather than checked by the caller, because §5.2's
/// machine has two views and three call sites and a rule enforced by whoever remembers it is not
/// enforced. The endpoint layer turns this into `409 illegal_transition`; so does a lost race on the
/// `state` concurrency token, because from the caller's side those are the same fact — somebody else
/// moved this declaration first.
/// </summary>
public sealed class IllegalTransitionException : Exception
{
    public IllegalTransitionException()
        : base("That transition is not legal from this state.")
    {
    }

    public IllegalTransitionException(string message)
        : base(message)
    {
    }

    public IllegalTransitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public IllegalTransitionException(DeclarationState from, DeclarationTransition via)
        : base($"A declaration in state '{from.ToDbValue()}' cannot {via}.")
    {
        From = from;
        Via = via;
    }

    public DeclarationState? From { get; }

    public DeclarationTransition? Via { get; }
}
