// See https://aka.ms/new-console-template for more information

class OctoParallel
{
    public async Task Execute(OctoParallelState state, int maxParallelism)
    {
        var nextItems = state.GetNextItems(maxParallelism);
        if (nextItems.Count == 0)
        {
            return;
        }
        
        var tasks = nextItems.Select(item => Task.Run(() => item.Execute()));
        state.WorkItems.Remove(nextItems);
        
        return Task.WhenAll(tasks);
    }
}

internal class OctoParallelState
{
    public List<WorkItem> WorkItems { get; set; }
}