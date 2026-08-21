using Api.Modules.Declarations;

namespace Api.Tests.Unit;

/// <summary>
/// design.md §5.2's transition table, asserted row for row and — more importantly — <em>pair for
/// pair</em>.
///
/// CLAUDE.md names this state machine as one of the four test-first cores, and the reason is the
/// asymmetry: the legal edges are five rows anybody would write, while the illegal ones are
/// 6 × 5 − 5 = 25 combinations nobody enumerates by hand. Driving them from the enums means a seventh
/// state or a sixth transition cannot be added without this suite noticing, which is the property a
/// hand-written list of refusals would not have.
/// </summary>
public class DeclarationTransitionTests
{
    private static readonly DeclarationState[] States = Enum.GetValues<DeclarationState>();

    private static readonly DeclarationTransition[] Transitions = Enum.GetValues<DeclarationTransition>();

    private static readonly DateTime At = new(2026, 8, 22, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Officer = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    [Fact]
    public void TheTableIsExactlySection52sFiveRows()
    {
        // Spelled out rather than counted, so a row that silently changes its destination fails here
        // and not merely as a different number. §5.2:
        //   Draft --submit--> Submitted --approve--> Approved --start repairs--> RepairsInProgress
        //     --submit docs--> RepairDocsSubmitted (terminal)
        //                        |
        //                        +--reject--> Rejected (terminal)
        var expected = new (DeclarationState From, DeclarationTransition Via, DeclarationState To)[]
        {
            (DeclarationState.Draft, DeclarationTransition.Submit, DeclarationState.Submitted),
            (DeclarationState.Submitted, DeclarationTransition.Approve, DeclarationState.Approved),
            (DeclarationState.Submitted, DeclarationTransition.Reject, DeclarationState.Rejected),
            (DeclarationState.Approved, DeclarationTransition.StartRepairs, DeclarationState.RepairsInProgress),
            (DeclarationState.RepairsInProgress, DeclarationTransition.SubmitRepairDocs,
                DeclarationState.RepairDocsSubmitted),
        };

        Assert.Equal(expected.Order(), DeclarationTransitions.All.Order());
    }

    [Fact]
    public void EveryPairNotInTheTableHasNoTarget()
    {
        // The exhaustive half: all 30 (state, transition) combinations the enums can express, minus
        // the five legal ones, must have no destination at all. Written as a sweep rather than a list
        // so that adding a state to the enum without adding its rows fails right here.
        var legal = DeclarationTransitions.All.Select(t => (t.From, t.Via)).ToHashSet();

        var illegal = States
            .SelectMany(_ => Transitions, (state, via) => (From: state, Via: via))
            .Where(pair => !legal.Contains(pair))
            .ToList();

        Assert.Equal(25, illegal.Count);
        Assert.All(illegal, pair => Assert.Null(DeclarationTransitions.Target(pair.From, pair.Via)));
    }

    [Fact]
    public void BothTerminalStatesHaveNoWayOut()
    {
        // §1 is explicit that rejection is terminal with no resubmit edge, and that the lifecycle ends
        // at repair-documents-submitted with no closure state. Both are easy to "helpfully" add.
        foreach (var terminal in new[] { DeclarationState.Rejected, DeclarationState.RepairDocsSubmitted })
        {
            Assert.All(Transitions, via => Assert.Null(DeclarationTransitions.Target(terminal, via)));
        }
    }

    [Theory]
    [InlineData(DeclarationTransition.Submit)]
    [InlineData(DeclarationTransition.Approve)]
    [InlineData(DeclarationTransition.Reject)]
    [InlineData(DeclarationTransition.StartRepairs)]
    [InlineData(DeclarationTransition.SubmitRepairDocs)]
    public void TheEntityAgreesWithTheTableFromEveryState(DeclarationTransition via)
    {
        // The entity keeps no second copy of the table, and this is what pins that: from every state,
        // its method must agree with Target() about legality. Replace Apply()'s lookup with a
        // hand-written switch and this goes red the moment the two disagree.
        foreach (var state in States)
        {
            var declaration = InState(state);
            var target = DeclarationTransitions.Target(state, via);

            if (target is not null)
            {
                Invoke(declaration, via);
                Assert.Equal(target, declaration.State);
                continue;
            }

            var thrown = Assert.Throws<IllegalTransitionException>(() => Invoke(declaration, via));
            Assert.Equal(state, thrown.From);
            Assert.Equal(via, thrown.Via);

            // A refused transition must also leave the row untouched. A half-applied Approve that set
            // the visa and then threw would link a declaration to a claim without moving it out of
            // draft — which CK_declaration_decision would then reject at SaveChanges, hours of
            // debugging away from the line that actually did it.
            Assert.Equal(state, declaration.State);
        }
    }

    [Fact]
    public void SubmitStampsSubmittedAtAndNothingElse()
    {
        var declaration = InState(DeclarationState.Draft);

        declaration.Submit(At);

        Assert.Equal(DeclarationState.Submitted, declaration.State);
        Assert.Equal(At, declaration.SubmittedAt);
        Assert.Null(declaration.DecidedAt);
        Assert.Null(declaration.VisaNo);
        Assert.Null(declaration.OfficerUserId);
    }

    [Fact]
    public void ApproveRecordsTheVisaTheOfficerAndTheDecisionTime()
    {
        var declaration = InState(DeclarationState.Submitted);

        declaration.Approve(Officer, "PLACEHOLDER-VISA-0001", At);

        Assert.Equal(DeclarationState.Approved, declaration.State);
        Assert.Equal("PLACEHOLDER-VISA-0001", declaration.VisaNo);
        Assert.Equal(Officer, declaration.OfficerUserId);
        Assert.Equal(At, declaration.DecidedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApproveWithoutAVisaIsRefusedAndChangesNothing(string? visaNo)
    {
        // The visa is the whole point of approving: §5.2 sets it here and every deferred document is
        // then pushed under it. An approval that reached `approved` with a blank visa would produce
        // documents that can never be pushed — the exact "AXA is missing photos" outcome this project
        // exists to remove — so it is refused before the state moves.
        var declaration = InState(DeclarationState.Submitted);

        Assert.ThrowsAny<ArgumentException>(() => declaration.Approve(Officer, visaNo!, At));
        Assert.Equal(DeclarationState.Submitted, declaration.State);
        Assert.Null(declaration.VisaNo);
    }

    [Fact]
    public void RejectRecordsTheOfficerButNeverAVisa()
    {
        // §5.2: rejection pushes nothing to NEXT3, so there is no visa to link and none is taken.
        var declaration = InState(DeclarationState.Submitted);

        declaration.Reject(Officer, At);

        Assert.Equal(DeclarationState.Rejected, declaration.State);
        Assert.Equal(Officer, declaration.OfficerUserId);
        Assert.Equal(At, declaration.DecidedAt);
        Assert.Null(declaration.VisaNo);
    }

    [Fact]
    public void TheHappyPathWalksTheWholeTableAndStampsEveryTimestamp()
    {
        var declaration = InState(DeclarationState.Draft);

        declaration.Submit(At);
        declaration.Approve(Officer, "PLACEHOLDER-VISA-0002", At.AddMinutes(5));
        declaration.StartRepairs(At.AddMinutes(10));
        declaration.SubmitRepairDocs(At.AddMinutes(15));

        Assert.Equal(DeclarationState.RepairDocsSubmitted, declaration.State);
        Assert.Equal(At, declaration.SubmittedAt);
        Assert.Equal(At.AddMinutes(5), declaration.DecidedAt);
        Assert.Equal(At.AddMinutes(10), declaration.RepairsStartedAt);
        Assert.Equal(At.AddMinutes(15), declaration.RepairDocsSubmittedAt);
    }

    [Fact]
    public void EveryStateRoundTripsThroughItsDbValue()
    {
        // The check constraint is generated from DeclarationStates.All, so a state missing from that
        // array is a state the database rejects at runtime rather than at build time.
        Assert.Equal(States.Length, DeclarationStates.All.Length);
        Assert.All(States, s => Assert.Equal(s, DeclarationStates.FromDbValue(s.ToDbValue())));
        Assert.All(States, s => Assert.Contains(s.ToDbValue(), DeclarationStates.All));
    }

    [Fact]
    public void TheDecidedAndLinkedSetsMatchTheTable()
    {
        // CK_declaration_decision is built from these two arrays, so they are schema, not helpers. A
        // state reachable only by a decision must be in Decided; a state at or past `approved` must be
        // in Linked. Derived here from the table rather than restated, so the constraint cannot drift
        // away from the machine it constrains.
        var decided = States
            .Where(s => s is not (DeclarationState.Draft or DeclarationState.Submitted))
            .Select(s => s.ToDbValue());
        Assert.Equal(decided.Order(), DeclarationStates.Decided.Order());

        var linked = States
            .Where(s => s is not (DeclarationState.Draft or DeclarationState.Submitted or DeclarationState.Rejected))
            .Select(s => s.ToDbValue());
        Assert.Equal(linked.Order(), DeclarationStates.Linked.Order());
    }

    /// <summary>
    /// Stands a declaration up in the given state by walking there through legal transitions only.
    /// There is deliberately no back door that assigns <see cref="Declaration.State"/> directly — a
    /// test hook that bypasses the machine is a hole in the guarantee the machine exists to give, and
    /// it would be the one hole the suite could never see.
    /// </summary>
    private static Declaration InState(DeclarationState state)
    {
        var declaration = new Declaration { PlateNo = "PLACEHOLDER-PLATE-1" };
        if (state == DeclarationState.Draft)
        {
            return declaration;
        }

        declaration.Submit(At);
        if (state == DeclarationState.Submitted)
        {
            return declaration;
        }

        // Rejected branches off Submitted; everything else is the straight line through Approved.
        if (state == DeclarationState.Rejected)
        {
            declaration.Reject(Officer, At);
            return declaration;
        }

        declaration.Approve(Officer, "PLACEHOLDER-VISA-0001", At);
        if (state == DeclarationState.Approved)
        {
            return declaration;
        }

        declaration.StartRepairs(At);
        if (state == DeclarationState.RepairsInProgress)
        {
            return declaration;
        }

        declaration.SubmitRepairDocs(At);
        return declaration;
    }

    private static void Invoke(Declaration declaration, DeclarationTransition via)
    {
        switch (via)
        {
            case DeclarationTransition.Submit:
                declaration.Submit(At);
                break;
            case DeclarationTransition.Approve:
                declaration.Approve(Officer, "PLACEHOLDER-VISA-0003", At);
                break;
            case DeclarationTransition.Reject:
                declaration.Reject(Officer, At);
                break;
            case DeclarationTransition.StartRepairs:
                declaration.StartRepairs(At);
                break;
            case DeclarationTransition.SubmitRepairDocs:
                declaration.SubmitRepairDocs(At);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(via), via, null);
        }
    }
}
