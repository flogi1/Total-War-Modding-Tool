using TotalWarModTool.UndoRedo;

namespace TotalWarModTool.UndoRedo.Tests;

public sealed class ActionHistoryTests
{
    [Fact]
    public void Execute_runs_the_action_and_makes_it_undoable()
    {
        var history = new ActionHistory();
        var action = new TestAction("a");

        history.Execute(action);

        Assert.Equal(1, action.DoCount);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Same(action, history.NextUndo);
        Assert.Null(history.NextRedo);
    }

    [Fact]
    public void Record_does_not_run_the_action()
    {
        var history = new ActionHistory();
        var action = new TestAction("a");

        history.Record(action);

        Assert.Equal(0, action.DoCount);
        Assert.True(history.CanUndo);
        Assert.Same(action, history.NextUndo);
    }

    [Fact]
    public void Undo_and_redo_move_the_step_between_the_stacks_and_return_it()
    {
        var history = new ActionHistory();
        var action = new TestAction("a");
        history.Execute(action);

        var undone = history.Undo();

        Assert.Same(action, undone);
        Assert.Equal(1, action.UndoCount);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.Same(action, history.NextRedo);

        var redone = history.Redo();

        Assert.Same(action, redone);
        Assert.Equal(2, action.DoCount);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Same(action, history.NextUndo);
    }

    [Fact]
    public void A_new_step_clears_the_redo_stack()
    {
        var history = new ActionHistory();
        history.Execute(new TestAction("a"));
        history.Undo();
        Assert.True(history.CanRedo);

        history.Execute(new TestAction("b"));

        Assert.False(history.CanRedo);

        history.Undo();
        Assert.True(history.CanRedo);

        history.Record(new TestAction("c"));

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void RedoDiscarded_hands_over_the_steps_a_new_step_removed_from_the_redo_stack_before_Changed()
    {
        var history = new ActionHistory();
        var first = new TestAction("a");
        var second = new TestAction("b");
        history.Execute(first);
        history.Execute(second);
        history.Undo();
        history.Undo();
        var order = new List<string>();
        IReadOnlyList<IUndoableAction>? discarded = null;
        (bool CanUndo, bool CanRedo) seen = default;
        history.RedoDiscarded += steps =>
        {
            discarded = steps;
            seen = (history.CanUndo, history.CanRedo);
            order.Add("discarded");
        };
        history.Changed += () => order.Add("changed");

        history.Record(new TestAction("c"));

        Assert.NotNull(discarded);
        Assert.Equal(2, discarded.Count);
        Assert.Contains(first, discarded);
        Assert.Contains(second, discarded);
        Assert.Equal((true, false), seen);                                       // the stacks are final when the handler runs
        Assert.Equal(["discarded", "changed"], order);
    }

    [Fact]
    public void RedoDiscarded_is_raised_by_Execute_too_and_not_without_discarded_steps()
    {
        var history = new ActionHistory();
        var raised = 0;
        history.RedoDiscarded += _ => raised++;

        history.Execute(new TestAction("a"));
        history.Record(new TestAction("b"));
        history.Undo();                                                          // undo and redo themselves discard nothing
        history.Redo();
        history.Undo();
        history.RemoveWhere(_ => false);
        history.Clear();

        Assert.Equal(0, raised);

        history.Execute(new TestAction("c"));
        history.Undo();
        history.Execute(new TestAction("d"));

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Undo_on_an_empty_history_throws_InvalidOperationException()
    {
        var history = new ActionHistory();

        Assert.Throws<InvalidOperationException>(() => history.Undo());
        Assert.Throws<InvalidOperationException>(() => history.Redo());
    }

    [Fact]
    public void A_failing_Do_is_not_recorded()
    {
        var history = new ActionHistory();
        var changed = 0;
        history.Changed += () => changed++;
        var action = new TestAction("a") { FailOnDo = true };

        Assert.Throws<InvalidOperationException>(() => history.Execute(action));

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void A_failing_Undo_drops_the_step_and_rethrows()
    {
        var history = new ActionHistory();
        history.Execute(new TestAction("a"));
        var failing = new TestAction("b") { FailOnUndo = true };
        history.Execute(failing);
        var changed = 0;
        history.Changed += () => changed++;

        Assert.Throws<InvalidOperationException>(() => history.Undo());

        Assert.Equal(1, changed);
        Assert.False(history.CanRedo);
        Assert.True(history.CanUndo);
        Assert.NotSame(failing, history.NextUndo);

        history.Undo();

        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void A_failing_Redo_drops_the_step_and_rethrows()
    {
        var history = new ActionHistory();
        history.Execute(new TestAction("a"));
        var failing = new TestAction("b");
        history.Execute(failing);
        history.Undo();
        failing.FailOnDo = true;
        var changed = 0;
        history.Changed += () => changed++;

        Assert.Throws<InvalidOperationException>(() => history.Redo());

        Assert.Equal(1, changed);
        Assert.False(history.CanRedo);
        Assert.True(history.CanUndo);
        Assert.Equal("a", history.NextUndo!.Description);
    }

    [Fact]
    public void RemoveWhere_removes_from_both_stacks_and_keeps_the_order()
    {
        var history = new ActionHistory();
        foreach (var name in new[] { "a", "b", "c", "d" })
        {
            history.Execute(new TestAction(name));
        }

        history.Undo(); // d moves to the redo stack

        var removed = history.RemoveWhere(a => a.Description is "b" or "d");

        Assert.Equal(2, removed);
        Assert.False(history.CanRedo);
        Assert.Equal("c", history.Undo().Description);
        Assert.Equal("a", history.Undo().Description);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void RemoveWhere_without_a_match_raises_nothing()
    {
        var history = new ActionHistory();
        history.Execute(new TestAction("a"));
        var changed = 0;
        history.Changed += () => changed++;

        var removed = history.RemoveWhere(_ => false);

        Assert.Equal(0, removed);
        Assert.Equal(0, changed);
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void Changed_is_raised_once_per_change()
    {
        var history = new ActionHistory();
        var changed = 0;
        history.Changed += () => changed++;

        history.Execute(new TestAction("a"));
        Assert.Equal(1, changed);

        history.Record(new TestAction("b"));
        Assert.Equal(2, changed);

        history.Undo();
        Assert.Equal(3, changed);

        history.Redo();
        Assert.Equal(4, changed);

        history.RemoveWhere(a => a.Description == "a");
        Assert.Equal(5, changed);

        history.Clear();
        Assert.Equal(6, changed);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void A_failing_Undo_leaves_the_redo_stack_unchanged()
    {
        var history = new ActionHistory();
        var first = new TestAction("a");
        var failing = new TestAction("b");
        var last = new TestAction("c");
        history.Execute(first);
        history.Execute(failing);
        history.Execute(last);
        history.Undo(); // c waits on the redo stack
        failing.FailOnUndo = true;

        Assert.Throws<InvalidOperationException>(() => history.Undo());

        Assert.Same(last, history.NextRedo);
        Assert.Same(first, history.NextUndo);
        Assert.Same(last, history.Redo());
        Assert.False(history.CanRedo);
        Assert.Same(last, history.NextUndo);
    }

    [Fact]
    public void A_failing_Redo_leaves_the_undo_stack_unchanged()
    {
        var history = new ActionHistory();
        var first = new TestAction("a");
        var second = new TestAction("b");
        var failing = new TestAction("c");
        history.Execute(first);
        history.Execute(second);
        history.Execute(failing);
        history.Undo();
        failing.FailOnDo = true;

        Assert.Throws<InvalidOperationException>(() => history.Redo());

        Assert.False(history.CanRedo);
        Assert.Same(second, history.NextUndo);
        Assert.Same(second, history.Undo());
        Assert.Same(first, history.Undo());
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Changed_handlers_see_the_final_stacks()
    {
        var history = new ActionHistory();
        var seen = new List<(bool CanUndo, bool CanRedo)>();
        history.Changed += () => seen.Add((history.CanUndo, history.CanRedo));

        history.Execute(new TestAction("a"));
        history.Undo();
        history.Redo();

        Assert.Equal(new[] { (true, false), (false, true), (true, false) }, seen);
    }

    [Fact]
    public void A_failing_Undo_with_a_throwing_handler_rethrows_the_original_exception()
    {
        var history = new ActionHistory();
        history.Execute(new TestAction("a") { FailOnUndo = true });
        history.Changed += () => throw new FormatException("handler");

        var ex = Assert.Throws<InvalidOperationException>(() => history.Undo());

        Assert.Equal("Undo failed.", ex.Message);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void A_failing_Redo_with_a_throwing_handler_rethrows_the_original_exception()
    {
        var history = new ActionHistory();
        var action = new TestAction("a");
        history.Execute(action);
        history.Undo();
        action.FailOnDo = true;
        history.Changed += () => throw new FormatException("handler");

        var ex = Assert.Throws<InvalidOperationException>(() => history.Redo());

        Assert.Equal("Do failed.", ex.Message);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void A_throwing_handler_propagates_in_the_normal_path()
    {
        var history = new ActionHistory();
        history.Execute(new TestAction("a"));
        history.Changed += () => throw new FormatException("handler");

        Assert.Throws<FormatException>(() => history.Undo());

        Assert.True(history.CanRedo);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void A_throwing_match_changes_nothing()
    {
        var history = new ActionHistory();
        foreach (var name in new[] { "a", "b", "c" })
        {
            history.Execute(new TestAction(name));
        }

        history.Undo();
        var changed = 0;
        history.Changed += () => changed++;

        Assert.Throws<FormatException>(() => history.RemoveWhere(a =>
            a.Description == "c" ? throw new FormatException("match") : a.Description == "a"));

        Assert.Equal(0, changed);
        Assert.Equal("c", history.NextRedo!.Description);
        Assert.Equal("b", history.Undo().Description);
        Assert.Equal("a", history.Undo().Description);
    }

    [Fact]
    public void A_null_action_throws()
    {
        var history = new ActionHistory();

        Assert.Throws<ArgumentNullException>(() => history.Execute(null!));
        Assert.Throws<ArgumentNullException>(() => history.Record(null!));
        Assert.False(history.CanUndo);
    }

    private sealed class TestAction : IUndoableAction
    {
        public TestAction(string description)
        {
            Description = description;
        }

        public string Description { get; }

        public int DoCount { get; private set; }

        public int UndoCount { get; private set; }

        public bool FailOnDo { get; set; }

        public bool FailOnUndo { get; set; }

        public void Do()
        {
            if (FailOnDo)
            {
                throw new InvalidOperationException("Do failed.");
            }

            DoCount++;
        }

        public void Undo()
        {
            if (FailOnUndo)
            {
                throw new InvalidOperationException("Undo failed.");
            }

            UndoCount++;
        }
    }
}
