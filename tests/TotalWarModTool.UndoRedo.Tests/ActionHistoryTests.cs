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
