// See https://aka.ms/new-console-template for more information



class OctoParallel
{
    public OctoParallel(OctoParallelState state)
    {
        State = state;
    }

    public OctoParallelState State { get; set; }
    public Task Execute(int maxParallelism)
    {
        var nextItems = State.GetNextItems(maxParallelism);
        if (nextItems.Count() == 0)
        {
            return Task.CompletedTask;
        }
        
        var tasks = nextItems.Select(item => Task.Run(() => item.Execute()));
        
        return Task.WhenAll(tasks);
    }

    public async Task Execute2(int maxParallelism)
    {
        var nextItems = State.GetNextItems(maxParallelism);
        while (nextItems.Count() > 0)
        {
            var tasks = nextItems.Select(item => Task.Run(() => item.Execute()));
            await Task.WhenAll(tasks);
        }
    }
}

public class WorkItem
{
    public Guid Id { get; set; }
    protected readonly Action action;

    public WorkItem(Guid id, Action action)
    {
        this.Id = id;
        this.action = action;
    }

    public bool IsCompleted { get; protected set; }
    public bool IsExecuting { get; protected set; }

    public virtual void Execute()
    {
        IsExecuting = true;
        action.Invoke();
        IsCompleted = true;
        IsExecuting = false;
    }

    public virtual void Complete()
    {
        // NOP
    }
}

public class EventDrivenWorkItem : WorkItem
{
    public EventDrivenWorkItem(Guid id, Action action) : base(id, action)
    {

    }

    public override void Execute()
    {
        IsExecuting = true;
        action.Invoke();
    }

    public override void Complete()
    {
        IsCompleted = true;
        IsExecuting = false;
    }

}

public class OctoParallelState
{
    public List<WorkItem> WorkItems { get; set; } = new List<WorkItem>();

    public IEnumerable<WorkItem> GetNextItems(int maxParallelism)
    {
        var currentlyExecutingCount = WorkItems.Count(i => i.IsExecuting);
        var capacity = maxParallelism - currentlyExecutingCount;
        return WorkItems
            .Where(i => !i.IsExecuting && !i.IsCompleted)
            .Take(capacity > 0 ? capacity : 0);
    }
}

class ControllerBase
{
    public ControllerBase(OctoParallel octoParallel, OctoParallelState state)
    {
        OctoParallel = octoParallel;
        State = state;
    }

    public OctoParallel OctoParallel { get; set; }
    public OctoParallelState State { get; set; }

    public async Task ExecuteBase(int maxParallelism)
    {
        await OctoParallel.Execute(maxParallelism);
    }

    public async Task PreExecute(Func<Guid, Action, WorkItem> workItemFactory)
    {
        await Task.CompletedTask;
        var items = new List<WorkItem>
        {
            workItemFactory.Invoke(Guid.NewGuid(), () => Console.WriteLine("Executed 1.")),
            workItemFactory.Invoke(Guid.NewGuid(), () => Console.WriteLine("Executed 2.")),
            workItemFactory.Invoke(Guid.NewGuid(), () => Console.WriteLine("Executed 3."))
        };

        State.WorkItems = items;

    }
}

class Controller : ControllerBase
{
    public Controller(OctoParallel octoParallel, OctoParallelState state) : base(octoParallel, state)
    {
    }

    public async Task Execute(int maxParallelism, Func<Guid, Action, WorkItem>? workItemFactory)
    {
        await PreExecute(workItemFactory ?? ((id, action) => new WorkItem(id, action)));
        await ExecuteBase(maxParallelism);
    }
}

public class Execution
{
    private int maxParallelism;

    public OctoParallelState OctoParallelState { get; set; } = new OctoParallelState();
    
    public async Task Start(int maxParallelism)
    {
        this.maxParallelism = maxParallelism;
        var octoParallel = new OctoParallel(OctoParallelState);
        var controller = new Controller(octoParallel, OctoParallelState);
        await controller.Execute(maxParallelism, ((id, action) => new EventDrivenWorkItem(id, action)));
    }

    public async Task StatusCheck(StatusUpdate statusUpdate)
    {
        if (statusUpdate.IsComplete)
        {
            var octoParallel = new OctoParallel(OctoParallelState);
            var controller = new Controller(octoParallel, OctoParallelState);

            OctoParallelState.WorkItems.FirstOrDefault(i => i.Id == statusUpdate.workItemId)?.Complete();
            await controller.ExecuteBase(maxParallelism);
        }
    }
}

public record StatusUpdate(bool IsComplete, Guid workItemId);

