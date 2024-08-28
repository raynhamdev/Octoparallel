// See https://aka.ms/new-console-template for more information

class OctoParallel
{
    public OctoParallel(OctoParallelState state)
    {
        State = state;
    }

    public OctoParallelState State { get; set; }
    public async Task Execute(int maxParallelism)
    {
        var nextItems = State.GetNextItems(maxParallelism);
        if (nextItems.Count == 0)
        {
            return;
        }
        
        var tasks = nextItems.Select(item => Task.Run(() => item.Execute()));
        State.WorkItems.Remove(nextItems);
        
        return Task.WhenAll(tasks);
    }
}

public class OctoParallelState
{
    public List<WorkItem> WorkItems { get; set; }
}

class ControllerBase
{
    public Controller(OctoParallel octoParallel, OctoParallelState state)
    {
        OctoParallel = octoParallel;
        State = state;
    }

    public OctoParallel OctoParallel { get; set; }
    public OctoParallelState State { get; set; }

    public async Task ExecuteBase()
    {
        OctoParallel.Execute();
    }

    public async Task PreExecute()
    {
        State.WorkItems = new List<WorkItem>();
    }
}

class Controller : ControllerBase
{
    public Controller(OctoParallel octoParallel, OctoParallelState state) : base(octoParallel, state)
    {
    }

    public async Task Execute()
    {
        await PreExecute();
        await ExecuteBase();
    }
}

public class Execution
{
    public OctoParallelState OctoParallelState { get; set; }
    
    public async Task Start()
    {
        var octoParallel = new OctoParallel(OctoParallelState);
        var controller = new Controller(octoParallel, OctoParallelState);
        await controller.Execute();
    }

    public async Task StatusCheck(bool isComplete)
    {
        if (isComplete)
        {
            var octoParallel = new OctoParallel(OctoParallelState);
            var controller = new Controller(octoParallel, OctoParallelState);

            octoParallel.Complete(workItemId);
            controller.Execute();
        }
    }
}